# MASTER CATALOG — village-hero (rewritten 2026-08-02)

> **Verified from CODE at HEAD `b77a178e` (branch `wip/village2-and-f8-tickets`), 2026-08-02.**
> Every entry below was read from source, NOT from file-header comments (comments lie — several
> headers in this folder still describe retired systems; each lie is flagged inline + in FLAGS).
> Supersedes the 2026-06-12 body and its 06-28 / 07-22 / 07-26 banners entirely.

**Scope:** `Assets/_Modules/Village/Hero/` — 99 `.cs` files (~40k lines). The player-hero feature
area: locomotion, abilities/casts, gear/equip, health, cameras/input, HUD bridges, plus the
UI surfaces that live here by location: raid front-end, barracks/troop panels, rumor board,
realm map (WO-826), vendor shops, inventory/equip screens. **Assembly:** `DeNelle.Village`
(`DeNelle.Village.asmdef`). Namespace split: gameplay components = `DeNelle.Village`; the
MVVM UI cluster (shops/inventory/raid/rumor/realm-map/barracks VMs + views) = `DeNelle.Village.Hero`.

## DELTA 2026-09-09 — staff grip is DERIVED at attach, off-hand shield seats through hero authority, playable bound measured, and shop loader branch by row

Four work order results landed on 2026-09-09 across four lanes. All read-verified from RESULT files:

- **HERO-GRIP (WO-1431 RESULT § 2):** `EquipmentController.SeatMeleeGripPoint` dispatcher + `MeleeGripSeat` struct (public static, shipped dispatch). Staff grip 0.75 derived at attach; non-native melee props seat by archetype.
- **NPC-SHIELD (WO-1616 RESULT § 2–3):** `EquipmentController.SeatShieldMountRotation / SeatShieldPlateOnSocket` public statics shared with `TroopGearApplier`. One authority for shield seating (hero steps lifted to public entry points).
- **LOCOMOTION (WO-1094 RESULT § 1):** `HeroLocomotion` measured bound from `BiomeRoads.TryMeasureWorldBounds`, no typed fallback; `RemoteTunables.KeyHeroPlayableFallbackHalf` (50 m) is the scene-less fallback, no `PlayableHalf` const.
- **SHOP (WO-1096 RESULT § 2):** `PartyShopVM` loader branch decided by row `loadVia`, never by `"blink_"` id prefix; armor flag governs armor only.

## DELTA 2026-08-21 — sheathe orientation is DERIVED PER MESH, not decided by one global sign

Read from source 2026-08-21 (`EquipmentController.cs`, now **4,453 lines**;
`Core/Geometry/WeaponOrientHelper.cs`, now **1,395**).

**THE DEFECT WAS STRUCTURAL, NOT A TUNING MISS.** One serialized field
`_sheatheLongAxisSign` decided sheathed orientation for the ENTIRE catalogue, so it was correct for
at most half of it **by construction** and flipping it only moved the defect to the other half. Two
F8 captures proved it from opposite directions: Blaise wanted -1 (2026-08-20), the Flameblade wanted
+1 (2026-08-21).

**WHAT IT IS NOW.** `EquipmentController.ResolveSheathedTipSign` (around `:3160`) calls
`WeaponOrientHelper.TryResolveSheathedTipSign(prop, gripRoot, out _sheathedTipScratch)` **once per
attach**, inside `Guard.Try`, and caches `_sheatheTipSign` + a human-readable `_sheatheTipWhy`.
`ComputeSheathRotation` (`:3246`) then reads the measured sign and falls back to the global field
only when nothing could be measured:
`float sign = _sheatheTipSign != 0f ? ... : (_sheatheLongAxisSign >= 0f ? 1f : -1f);`

- ⛔ **The derivation reads `mesh.bounds`, NOT vertices.** The shipped props have Read/Write **OFF**,
  so every vertex-based approach is silently inert **on device** while appearing to work in the
  editor. This is the single most mis-diagnosable property in the area.
- ⛔ **`_sheatheLongAxisSign` is NOT deleted** (§12: instrumentation and documented fallbacks stay).
  It is the last-resort path for a prop that cannot answer, and the fallback emits a `FlowTrace.Warn`
  that names the prop, the reason, and — explicitly — that flipping the field is the WRONG cure.
- Shield and Bow return early ("no tip to invert / own derived carry"); the sheathe pose is
  sword-class only. `HeroArmorVisual`, `GearAura` and `HeroBodySwapper` took small supporting edits
  in the same change.
- A second `FlowTrace.Warn` fires when the measured long axis is not Y: the sheathe pose maps
  grip-root-local +Y onto the vertical, so a non-Y long axis breaks the pose's premise in a way no
  SIGN can repair (it would hang the prop's WIDTH vertically). It says so rather than shipping a
  confident number about the wrong direction.
- **11 of 12 shipped meshes resolve.** The twelfth is `staff_A` and it is **not a bug**: both ends
  measure identical to four decimal places on the taper test (relGap 0.001) and on the
  grip-proximity test (**relGap 0**). The mesh genuinely does not encode which end is up. Ticketed
  as **WO-1136** — see the risk ledger in `docs/MASTER_CATALOG.md`.
- Oracle: `Assets/Editor/Regression/SheathePoseRegression.cs` grew ~500 lines in the same change.

---

## LIVE CANON (what is actually true at HEAD)

- **Hero = ONE Tripo/CC Knight, "Grom".** `HeroBodySwapper` routes the Knight through
  `ff.knightv3` (**default ON**, `FeatureFlags.cs:484`) → `Resources/Heroes/KnightV3.fbx`
  (CC/AccuRIG humanoid), controller slug "Knight", and — with `ff.mocaploco` (default ON,
  `FeatureFlags.cs:497`) — binds **`KnightMocap.controller`** (studio-mocap idle/walk/run/turn)
  (`HeroBodySwapper.cs:78-90, 331-390, 458-471`). Forward yaw +15°, height 1.75m
  (`HeroBodySwapper.cs:354-373`). Global animator speed restored to **1.0×** (the old 0.5×
  Mixamo band-aid removed, `HeroBodySwapper.cs:31-38`). Blaise/party/class-bodies/Blink armor
  swap = historical; **`ff.knightonly` default OFF** (`FeatureFlags.cs:68`
  `Get("knightonly", defaultOn: false)`) — *(corrected 2026-08-06; was "default ON
  (`FeatureFlags.cs:58`)", which is now only the XML-summary line, not the value)*. The
  playable roster is **Knight / Ranger / Mage** via `DeNelle.Core.State.PlayableHeroes`;
  Grom is still the only FINISHED body. See the DELTA block below (`9a0ff548`, `d0c7b8fd`).
- **Owner rulings recorded 2026-08-01/02 (BINDING):**
  1. **Basic attack = swing + hit ONLY.** No impact bursts on the basic melee swing — the
     2026-08-02 F8 ("rocks on swing" / which-VFX-drew-that) ruled the extra impact bursts OUT;
     `VFXManager.PlayKey` now traces every successful play (`b77a178e`) so the offending key is
     one grep away. Conformance work is IN FLIGHT — keep the swing clean (swing anim + connect
     feedback on the enemy; no cast-style burst at the blade).
  2. **Single Knight Grom, no mesh-swap.** No body/armor mesh swapping on the hero; armor
     reads via tier TINT (`EquipmentController.SetArmorTier`, MPB multiply — see below) and
     rim-light, never a body swap.
  3. **Mobile-first: no keyboard letter hotkeys.** The 1/2/3/4 ability hotkeys are REMOVED
     (`HeroAbilityInput.cs:110-113`); abilities fire from HUD skill buttons + gamepad face
     buttons only. The HUD **ATTACK PILL** (bottom-right stadium button, built in
     `HUD/Kit/HudKitController.cs:390` via `ElarionUiKit.BuildAttackPill` :2918) is the mobile
     primary-attack input; desktop keeps left-click/Space.
  4. **Registry-only motion VFX** (owner 2026-07-12, still in force): abilities.json `vfx*`
     defaults are SUPPRESSED; the ONLY cast/projectile/impact VFX authority is the owner's
     Motion Caster registry (`HeroAbilities.cs:1424-1435`, `RegistryOnlyMotionVfx=true` :1430).
  5. **Elemental on-hit: owner-tagged keys only; earth/holy/water/nature HELD**
     (`WeaponVfxMap.cs:182-241`).
- **2026-08-01 dungeon-rig guard:** `HeroControlEnsurer` now SKIPS its entire camera takeover
  when a `DeNelle.Dungeons.DungeonCameraRig` is live in the scene (reflection type lookup — no
  asmdef cycle), while all hero component ensures still run (`HeroControlEnsurer.cs:287-302,
  508-529`).
- **RumorBoard**: WO-810 master-detail rebuild (owner-signed wireframe) is the live layout; a
  2026-08-02 conformance fix wave is IN FLIGHT — re-verify this section after it lands.
- **RealmMap (WO-826)** shipped `eb5d0710`; travel is a DISABLED stub until WO-827.

---

## DELTA 2026-08-06 - the hero roster unlocked (Knight / Ranger / Mage)

*Sourced from commits `9a0ff548`, `d0c7b8fd`, `04d375c3` (2026-08-05). This block SUPERSEDES
every "knight-only" statement in the body below; the individual lines are corrected in place
and cross-reference here.*

**1. The flag flipped (`9a0ff548`).** `ff.knightonly` now **defaults OFF**
(`FeatureFlags.cs:68`). The flag-OFF playable roster resolves through the ONE registry
`DeNelle.Core.State.PlayableHeroes` (`Assets/_Modules/Core/State/PlayableHeroes.cs`):
`Roster = { Knight, Ranger, Mage }`, `SoloKnight = { Knight }` is now the opt-in ROLLBACK set.
`GameStateService.ChooseHero` no longer forces the class; `HeroSelectController` and
`VendorStockResolver` widened at once with no further code change (that is what the registry
was built for). Set PlayerPrefs `ff.knightonly`=1 to restore the solo-Knight V1 pivot.

**2. The CLERIC STAYS OUT, deliberately** (`PlayableHeroes.cs:20-26`, header ROSTER NOTE): no
kit, no tree, no body work is authored for her, and `HeroAbilities` already aliases Cleric to
the mage loadout. This narrows the pre-Phase-0 `VendorStockResolver.FullRoster` (which listed
"cleric"), so cleric-ONLY weapons stop appearing on shelves - correct, the shelf follows the
roster. Add `HeroClass.Cleric` to `Roster` the day her kit is authored; nothing else changes.

**3. Canon debt the unlock left behind.** `9a0ff548` touched ONLY `FeatureFlags.cs`, so a dozen
source comments and two docs still asserted KnightOnly was ON - including FeatureFlags' own XML
summary, **the source lie every other copy came from**. Fixed in `d0c7b8fd`. If you find another
"under knight-only" line anywhere, it is stale by default.

**4. LATENT P0 FIXED - the invisible hero (`d0c7b8fd`).** Ranger and Mage have **no FBX at
all**. Both fell through to a **Blink base body**, and `Assets/Blink` is **GITIGNORED**. On a
fresh clone the terminal fallback logged a failure and **RETURNED WITHOUT INSTANTIATING
ANYTHING**, after `Start` had already destroyed the placeholder - not a Knight-degrade, an
**INVISIBLE HERO**. Both bail-outs now build a **tracked KayKit body**
(`HeroBodySwapper.BuildTrackedFallbackBody`, verified via `git ls-files` as the only humanoid
bodies actually in the repo). The live chain is now documented on the file itself
(`HeroBodySwapper.cs:11-23`): (1) Knight-only KnightV3 -> KnightPackage; (2) all other classes
= the Blink LowPoly base via Addressables; (3) legacy `Resources/Heroes/<slug>.fbx`; (4)
**BuildTrackedFallbackBody - the tracked KayKit humanoid stage, the floor**. A missing art pack
may now look WRONG; it can never look like NOTHING.

**5. Identity: nameplate + portrait (`d0c7b8fd`).** Pick Ranger and, after load-in, nothing in
the game ever said "Sylas": the nameplate printed the CLASS word and the inventory medallion
hardcoded **Grom's face for every class**, so all four heroes wore the Knight's portrait. The
nameplate now reads the canon name through a **Core-side reader** (the existing one lives in
Onboarding and HUD may only reach Core); a missing file/key degrades to exactly the old
capitalized class word, warned once. Inventory resolves its slug by **COMPOSING the two
existing maps** rather than writing a third - a duplicated map is how these drift apart.

**6. OPEN - the blurry-portrait defect.** **Thrain's** portrait was imported as a plain texture
so `Load<Sprite>` returned null and it fell to the blurrier RawImage path while Sylas hit the
crisp one; its `.meta` now differs from Sylas's only by guid. **Grom and Elara have the
IDENTICAL defect and are NOT fixed** - and **Grom is the default hero**, so the default hero is
on the blurry path. Flagged, not closed.

**7. REFUTATION - do not re-chase `*.fbx.tripo-extracted`.** `Ranger.fbx.tripo-extracted` is a
**125-byte PLAIN TEXT SENTINEL** written by `TripoAssetPostprocessor` - **NOT a parked mesh**.
There is nothing to un-park. Knight's sentinel sits beside a live `Knight.fbx`, which proves
the marker never blocked an import. **WO-909's premise was wrong**; the comments repeating it
are fixed. Nobody should spend another cycle on it.

**8. TALENT TREES - WO-910, READY FOR OWNER RULING (`04d375c3`).**
`TalentStrategyRegression` hardcoded `HiddenTrees = { "ranger", "mage" }` from the knight-only
era and was never updated at the unlock, so guard **G3 (no dead talent nodes) had NEVER
audited them** while players could reach them. Emptying that set
(`TalentStrategyRegression.cs:190-202`) surfaced **31 real pre-existing dead nodes across 40
player-reachable talents** (17 ranger + 14 mage; split 16 unregistered-effect-key + 15
registered-key-with-a-stub-note). **Knight's 32 nodes and the 9 shared are fully green.**
- `hero-talents.json` is **UNTOUCHED** (md5 unchanged) - this is an AUDIT, not a data edit.
- The 31 are a **dated, WO-910-numbered RATCHETED BASELINE** (`KnownDeadNodeBaseline`,
  `TalentStrategyRegression.cs:204-235`): a dead node NOT in the set FAILS (no new debt), and
  a baseline id that **stops reporting dead ALSO FAILS**, naming the line to delete - so the
  baseline can never outlive its debt and rot into a lie.
- **Why not `"hidden": true` on each node (the REJECTED fix - this reasoning must survive):**
  on 2026-08-05 `HeroTalentNodeDef.Hidden` had **ZERO runtime readers** (`HeroSkillTreeVM.
  Rebuild` never consulted it) while its own comment claimed the View skips hidden nodes - so
  writing `hidden:true` would have turned the gate GREEN while leaving all 31 nodes fully
  CLICKABLE. `Hidden` is now genuinely wired into both `Rebuild` loops; **no node sets it
  today.** Second reason: hiding all 31 would strand **three whole tiers** (ranger t4, mage
  t3 + t4) and orphan 3 survivors - **Ranger would collapse to ONE reachable talent out of
  20, Mage to five, and BOTH would lose their entire tier-4 capstone row.**
- Hiding is the **OWNER's call**, not the gate's. **WO-910 is READY FOR OWNER RULING.**

---

## 1. Movement / control core

### HeroLocomotion — `HeroLocomotion.cs` (1499 lines) — `DeNelle.Village`
**The headline comment-lie, now self-documenting:** header :1-16 carries the 2026-06-12
CORRECTION — the class XML summary (:27-31 "Kinematic transform translation") is still stale.
**Real model: a NavMeshAgent driven KINEMATICALLY by input.** Awake gets-or-adds the agent and
configures it (radius 0.4, height 1.8, speed 30 so `Move()` never caps, `updateRotation=false`,
no avoidance) `:462-474`; Awake also **overrides serialized tuning** (speed 6, accel 55, decel 45
`:452-454` — scene-baked values are dead). Update: input → eased `Velocity` → `_agent.Move(step)`
when on-mesh else `transform.position += step` `:905-909`; facing = manual Slerp toward the move
heading (town: input heading; combat: velocity) `:930-947`.
- **Speed tiers (owner 2026-07-10 "Option 2"):** overworld = RUN 6 m/s, combat = planted 5 m/s
  (`:95-96`, cap chosen at `:798-799` via `IsWaveInCombat()` `:672-689` — BattleLock OR wave
  Active OR countdown ≤5s, mirroring the HUD posture rule).
- **Input** (`ReadMoveInput` `:1410-1473`): new Input System WASD/arrows + gamepad stick/dpad +
  `VirtualJoystick.Move` + reflection-read `DeNelle.HUD.Kit.HudMoveInput.Move` (`:1479-1496` —
  NOTE: `VirtualDPadLean` is DELETED; HudMoveInput replaced it) + deadzoned legacy axis fallback.
  Test seam: `SetScriptedMove/ClearScriptedMove` `:121-124` for the headless HERO_TURN_PROBE.
- **`static bool InputSuppressed`** `:258` — THE global player-input gate. Raised/cleared by
  **`DeNelle.Core.Dialogue.DialogueService.Started/Ended`** (`HookDialogueGate` `:529-549`;
  **Yarn is REMOVED — WO-557**; the old Yarn-runner retry coroutine is gone). Consumed here and
  by HeroAbilityInput / PlayerAttackController / BuildMode.
- `static bool WantsToMove` `:57` (0.02 deadzone — cancels cast wind-ups);
  `static bool HasAnyMoveInput` `:66` (0.0001 — camera recenter suspend).
- **`WarpTo(Vector3, Quaternion?)`** `:290-339` — teleport-aware seam warp: NavMesh.SamplePosition
  (5m), agent disable→move→`Warp`→re-enable, guarded `ResetPath` (F8 2026-07-30 off-mesh throw),
  raises `event OnTeleported` (SmartMobileCamera snaps). **Signature is reflection-load-bearing**
  (BattleArena resolves it by exact signature, `:293-295`). DeathTrace names every caller.
- **Facing writers** (sole rotation owner): move-Slerp `:930-947`; `FaceToward` `:143` (WO-423
  attack-facing, cancelled by input); **lock-face/strafe** `SetLockFace/ClearLockFace` `:161-180`
  + `ApplyLockFaceYaw` (WO-512, gated on `FeatureFlags.LockOn` — default OFF `FeatureFlags.cs:334`);
  pure `StepYaw` `:207` (edit-mode tested). Turn-in-place clip feed `DriveTurnSignal` `:224-245`
  fires ONLY in combat at the run tier `:1039-1048` (KnightMocap TurnDir; guarded no-op elsewhere).
- **Auto-walk** (WO-277) `SetAutoWalk/ClearAutoWalk/IsAutoWalking/AutoWalkArrived` `:354-393` +
  `AutoWalkStep` `:1362-1397`; auto-walk also traverses crossings `:758-765`.
- **Ground-snap** (DEF-147) `static GroundSnapEnabled=true` `:402`; off-mesh gravity fall +
  agent re-bind `:1085-1157`; suspended while `LiftPlatform.AnyCarrying()` `:1405-1408`.
- **Seam traversal** `TryTraverseSeamLink` `:1244-1328`: (a) paired `HeroLinkCrossing` portals —
  enter-triggered, id-paired, re-arm on exit `:1285-1311`; (b) LEGACY castle↔OuterWorld slide
  (corridor-guarded, endpoints ride `castle.liftY` PlayerPrefs) `:1235-1240, 1313-1348`.
- Animator: `ActorAnimator.SetLocomotion` is the **sole Speed writer** (raw SetFloat retired,
  `:1162-1168`); `SetAnimator(Animator)` `:624` is the WO-581 direct injection HeroBodySwapper
  calls (no reflection). Victory pose = 2.5s, input-cancellable `:436-443, 775-781`. Combat
  stance + `EquipmentController.SetCombatActive` driven off the same `engaged` flag `:1059-1076`.
- Diagnostics: `[Flow:HeroTurn]` probe (TurnDebug, default off) `:106, 958-977`; `[Flow:HeroDrift]`
  10 Hz forward-hold trace `:979-1024`; T-pose-while-moving `FlowTrace.Fail` `:1196-1205`.

### HeroControlEnsurer — `HeroControlEnsurer.cs` (545) — `DeNelle.Village`
DDOL singleton, `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` `:49-54`; re-runs `Ensure()` on
every sceneLoaded. Active-scene predicate `IsVillageScene` `:42-47` = Village*/Castle*/
MainCastle*/CastleHub* **+ raid scenes** (`HubScenes.IsRaid`). **The runtime attach point for the
hero component stack** — `Ensure()`:
1. `DedupeHeroes()` `:94-115` (return-to-town double-hero: keep the DDOL-carried one).
2. **Recover before fabricate** `TryRecoverCarriedHero` `:125-185`: re-homes a real hero parked
   in the DontDestroyOnLoad scene (component, Player-tag, or "Hero (" name match) into the
   active scene, seats it at `HeroStartPoint_PlayerSpawn` via `WarpTo`; only then
   `SpawnEmergencyHero()`.
3. Tags root **`Player`** (WO-450 canon) `:238`; adds if absent: HeroLocomotion `:239`,
   HeroDeathLogger `:241`, HeroTargetIndicator `:243`, **PlayerAttackController** `:250` (melee,
   every class), **WeaponTrailController** `:255`, GearLoadout `:258`, **HeroArmorVisual** `:264`,
   **HeroLoadout** `:273` + `ReloadFromPrefs()` `:280` (carried heroes skip Awake).
   Deliberately does **NOT** attach HeroReachRing (DEF-205) `:281-285`.
4. **DUNGEON GUARD (2026-08-01):** if `DungeonCameraRigPresent()` `:516-529` (cached reflection
   type `DeNelle.Dungeons.DungeonCameraRig` — Village must not ref Dungeons) → log + RETURN
   `:296-302`; everything below is camera takeover and would stomp the dungeon rig.
5. Camera takeover: find/create a gameplay camera (creates "GameplayCamera (ensured)" +
   AudioListener when none, `:323-330`), attach SmartMobileCamera `:331-335`, disable
   CinemachineBrain `:337-344`, `EnforceSoleCamera` `:347-351`, `SetTarget`+`ForceFollowImmediate`
   `:358-363`, then a **hard direct camera seat** behind the hero `:374-380`.
- `Watch()` coroutine `:386-399`: 0.5s poll; ≤8 emergency spawns per scene.
- `SpawnEmergencyHero` `:411-499`: root "Hero (Blaise)" + Player tag + a "HeroBody" capsule child
  (collider stripped) so the attached **HeroBodySwapper** `:466-475` swaps in the REAL Knight even
  on the fabricate path; lavender tint via `MagentaGuard.BuildUrpLitMaterial` `:452-455` (never
  Standard-shader magenta); seats at the marker or `castle.liftY+1` fallback `:432-433`.
  `FlowTrace.Fail("Hero", "EMERGENCY pill spawned…")` is the §12 proving line `:416-417`.
- Nested `HeroDeathLogger` `:533-544`: warns only if destroyed while Village2 is active.

---

## 2. Abilities (Q/W/E/R + extra bar)

### HeroAbilities — `HeroAbilities.cs` (1958) — `DeNelle.Village`
The cast engine. Class data in `abilities.json` supplies the pool and regen (Mage v6: 24 max,
0.6/s regen; Fireball costs 3) × Aether-perk × talent × Cathedral modifier, per-slot
cooldowns `:93`, per-id extra-bar cooldowns `:101-103`.
- **Resolution:** `Resolve(slot)` `:172-185` = HeroLoadout-equipped id (W/E/R only; **Q is
  locked** to the class basic) → `AbilityCatalog.FindById`, else class stock def. Public facade
  `ResolvedDef(slot)` `:198` — the HUD medallion icon source of truth (icon == what casts).
- **`TryCast(slot)`** `:400-448`: refuses while `_casting`/lockout; cooldown+mana gate; talent
  cooldown scale; then either INSTANT commit or — when `def.CastSeconds > 0` — an
  **interruptible wind-up** `CastRoutine` `:511-538`: mana/cd charged up front, moving
  (`HeroLocomotion.WantsToMove`) CANCELS + refunds + 0.2s anti-flicker lockout; commit calls the
  unchanged `CastResolved`. External `CancelCast()` `:545-553` aborts WITHOUT refund.
- **`TryCastExtra(abilityId)`** `:459-496` (WO-574 assignable bar): same core, per-id cooldown,
  resolves its own anim variant.
- **`CastResolved(def, castVariant)`** `:562-645`: param-guarded "Cast" trigger; ActorAnimator
  `PlayAttack(0)` **suppressed for heal/gracebuff** (F8-48 — a support cast must never read as a
  sword swing, `:606-610`); **`ResolveAnimVariant`** `:1450-1525` picks the cast CLIP from the
  RESOLVED ability (explicit `castAnim` key > effect-shape keyword > pressed slot) — fixes
  "equipped ability plays the wrong cast clip"; `AttackTimingBonus.NotifyCast` `:627`;
  `FaceCastTarget` `:695-735` (offensive only; faces the SAME blast centre the effect uses);
  then registry VFX + `ResolveEffect`.
- **Effect shapes** `ResolveEffect` `:737-956` + raw-string pre-switch `:751-761`:
  `dash` (Heroic Leap: WarpTo + strike + stun `:970-999`), `knockback` (cone + slow + interrupt
  `:1027-1057`), `taunt` (slow-hold + temp-shield heal; Holy-Retribution burn rider `:1066-1112`),
  `blink` (universal dash `:1009-1019`), `dot` (Emberbrand: projectile + Burn DoT `:1128-1161`),
  `healovertime` (Oathmend drip via `HeroHealth.RegenTick` `:1253-1265`), `invuln` (Eternal Aegis
  → `HeroHealth.ActivateInvuln` `:1272-1283`), **`gracebuff`** (WO-750 Warden's Grace: %-max-HP
  heal + Defense-scaled bonus + 8s Grace-Shield HoT + HUD marker; **the −20% DR is PENDING a
  HeroHealth mitigation seam** `:1310-1353`). Canonical enum shapes: Heal (self, Heal_Cast/Heal_Aura
  full prefabs `:786-817`), Strike/Snare (locked-target reach-gated, projectile-carried damage
  `:819-910`), Aoe/Cleave (reach-capped blast centre `:912-925`), Meteor (projectile + blast
  `:927-954`).
- **Damage chain** `:776`: `def.Damage × talent × HeroProgression.DamageMultiplier ×
  AttackTimingBonus chain × GearLoadout.WeaponMult` (WeaponMult now carries the WO-808 gear
  LEVEL — see GearLoadout). Attribution via `DamageAttribution.Record(…, "hero", …)`;
  `MarkNextHitFromHero` on every hit (combo/RAMPAGE eligibility).
- **Registry-only motion VFX** (owner 2026-07-12): `RegistryOnlyMotionVfx = true` `:1430`.
  `CastVariantKeyword = { "cast","skill1","skill2","castHeal", null }` `:1435` (variant 4 / R has
  NO registry keyword yet → silent). `RegistryTarget = "knight"` `:1529` (single-Knight canon).
  Cast beat `PlayCastVfxKey` `:1539-1558` (row vfxKey + vfxDelay; no row = silent BY DESIGN);
  projectile key from the row's `vfxProjectile` `:1394-1395`; impact `PlayImpactVfxKey`
  `:1575-1603` (row vfxImpact, travel-oriented, + `sfxImpact` clip); residuals SUPPRESSED
  `:1626`. abilities.json vfx fields stay authored but dormant — flip the const to restore.
- **Knight projectile rule** `:1405-1415`: Knight resolves melee-INSTANT; thrown actives fly a
  COSMETIC Hovl projectile (`FlyCosmeticProjectile` `:1644-1671`, rotation Y-flattened per F8
  2026-07-11) while damage lands instantly.
- Venombrand poison rider: per-foe stack ledger capped at 2, None-element DoT (no Burn tell)
  `:1188-1245`. Reach caps: melee blast centre = weapon reach (default 3.4m), ranged 45m
  `:1745-1772`. `Mana`/`RestoreManaOverTime`/`RestoreManaToFull` `:354-392` (safe-zone restore).
  DTT-era `AimPointOverride`/`LockedTarget`/`HealHandler` `:676-685` — today AimPointOverride is
  ONLY ever the HeroTargetIndicator reticle.

### HeroAbilityInput — `HeroAbilityInput.cs` (125) — `DeNelle.Village`
`[RequireComponent(HeroAbilities)]`. Respects `InputSuppressed` `:51`; **combat-gated on
`BattleLock.IsInBattle()`** `:59-70` (owner 2026-06-24: no combat moves in town; suppression is
FlowTraced). Primary attack = **left-click / Space** (+ legacy mouse fallback) fires slot Q at
the locked target `:76-82, 93-102`. **Keyboard 1/2/3/4 REMOVED (mobile-first)** `:110-113`;
gamepad face buttons S/E/W/N = Q/W/E/R `:114-120`. HUD paths (skill buttons, ATTACK pill)
converge on the same `TryCast` gate.

### AbilityCatalog — `AbilityCatalog.cs` (339) — `DeNelle.Village`
Typed loader for `abilities.json` (WebGL-safe `CanonicalJson.Read`: Resources first,
StreamingAssets fallback `:309-337`). Enums `AbilitySlot{Q,W,E,R}` `:31`, `AbilityEffect`
(6 canonical shapes) `:47` — the dash/knockback/taunt/blink/dot/healovertime/invuln/gracebuff
strings deliberately parse to Strike and are handled by HeroAbilities' raw-string switch.
`AbilityDef` fields incl. `CastSeconds` `:106` (wind-up), `CastAnim` `:117` (explicit clip
keyword), `Id` `:80`, and the 4 dormant `vfx*` keys `:133-139`. `DefaultClass="mage"` `:207`;
`GetLoadout` / `Find` / **`FindById`** `:262-278` (flat id index for loadout-equipped skills).

### abilities.json — `Assets/Resources/Data/Canonical/` (+ StreamingAssets mirror)
**5 class blocks:** `mage`, `knight`, `ranger`, **`knight-skills`** (13 skill-tree actives:
Throwing Spear, Shield Bash, Thunderbolt, Emberbrand Throw, Warden's Roar, Oathmend, Eternal
Aegis, Sweeping Cut, Champion's Combo…), **`universal-skills`** (Arcane Bolt / Mend / Dash-blink).
Live Knight stock kit: Q **Sword Heroic** (dash), W **Shield Charge** (knockback), E **Warden's
Grace** (gracebuff), R **Radiant Strike** (meteor). Most spells carry `castSeconds` 0.35–0.5
(interruptible); basics are instant. **No `cleric` class** — Cleric maps to the mage loadout in
code (`HeroAbilities.cs:276`).

### Supporting ability files
- **HeroLoadout** (`HeroLoadout.cs`, 255): per-hero slot→abilityId map (W/E/R only; **Q locked**),
  PlayerPrefs-persisted, edits battle-locked (`EditsLocked` via the Core HUD-context signal).
  Attached by HeroControlEnsurer; `ReloadFromPrefs` re-syncs carried heroes.
- **HeroLoadoutAccess** (103): static resolver over the live hero's HeroLoadout (Player tag);
  safe no-ops with no hero.
- **AssignableSkillBar** (193) + **AssignableSkillBarAccess** (99): the bottom-middle EXTRA
  skill bar (slotIndex→abilityId, PlayerPrefs, battle-locked) — casts go through
  `HeroAbilities.TryCastExtra`. Separate-by-design from HeroLoadout.
- **AttackTimingBonus** (190): DDOL singleton; 1.2s chain window → 1.0/1.15/1.30/1.50× damage;
  `NotifyCast`, `ChainDepth`, `OnChainChanged`; "CHAIN ×N" label.
- **AbilityCooldownUI** (64): legacy `Image.fillAmount` sweep; belt-and-braces only (live sweep
  = HUD bridge).
- **AbilityAudioBridge** (189): static per-class/kind SFX via `CoreServices.Audio` + procedural
  synth fallback.
- **AbilityVfxKit** (994): static procedural class-shaped ability VFX — legacy fallback layer;
  skipped when a def has authored Hovl keys (`HeroAbilities.cs:1887-1891`) and largely bypassed
  in registry-only mode.

---

## 3. Combat feel / projectiles / targeting

- **RangedAttackVFX** (342): `FireArrow` / `FireSpellOrb(target, onArrive, projectileKey, …,
  tint)` — flies the Hovl travel FX (or placeholder body) via ProjectileMover; used by
  mage/ranger paths and companions; the Knight bypasses it (cosmetic-projectile rule).
- **ProjectileMover** (191): lerp/arc mover, `onArrive` payload, impact FX; now pooled via —
- **MoverProjectilePool** (295): sibling of the tower ProjectilePool for hero/companion shots
  (per-shot GC kill; owner "control strays" directive).
- **ImpactFXPool** (165): pooled impact burst bodies keyed by prefab (Clear+Play reset contract).
- **HeroTargetIndicator** (861): reticle billboard + soft lock. Auto-acquire = TargetManager
  registry ∪ Enemy-layer OverlapSphere, 45m, forward-arc gate; manual lock via right-click/
  shoulder/tap. Writes `HeroAbilities.AimPointOverride` + `LockedTarget`; drives
  `HeroLocomotion.SetLockFace` when `ff.lockon` (default OFF). Attached by HeroControlEnsurer.
- **HeroCombatStatus** (56): buff/debuff timer tracker on the hero for the HUD row
  (`ApplyNamed`/`ClearNamed` — Mana Draught, Grace Shield markers).
- **HeroImpactFeedback** (147): `PlayHaptic` LIVE (HeroHealth damage + `ReportRumble` on hero
  hits, `HeroAbilities.cs:1372-1378`); `PlayRecoil` still UNWIRED (no caller).
- **HeroInjuredVignette** (152): IMGUI low-HP red edge vignette, driven by
  `HeroHealth.SetInjuredVisual` (single injured-threshold owner).
- **HeroHitReaction** (150): red damage flash + death slow-mo off HeroHealth events; ActorAnimator
  `PlayHit(Gut)`.
- Dead/dormant (unchanged since 06-12, re-verified): **HeroChargeVFX** (no caller), **HeroAimIK**
  (no SetAimTarget caller; controller lacks the UpperBody layer), **HeroReachRing**
  (dead-by-policy DEF-205), **HeroVictoryPoseBridge** (param-guarded no-op; HeroLocomotion owns
  the live victory pose), **HeroFootstepController** (clips never assigned; NOTE HeroLocomotion
  ALSO has its own `DriveFootsteps` loop `:697-720` loading `Sfx/FootstepsWalk` — the live path).

---

## 4. Health / progression

### HeroHealth — `HeroHealth.cs` (1261) — `DeNelle.Village`, `IDamageableStructure`
Singleton `Instance` `:37`. Max HP = `_maxHp + EffectiveBonus` (gear/talent HP bonuses) `:175`.
Contact damage intake (Enemy-layer scan, 1s ticks); IMGUI bar suppressed when `CoreServices.Hud`
is live. `TakeDamage` `:463` (armor reduction via GearLoadout, parry consume, invuln window),
`ActivateInvuln(seconds)` `:663`, `Heal` `:1008`, `RegenTick` `:1025` (silent drip),
`RestoreToFull` `:1055`, `Respawn` `:846` (DEF-102 — hero death ≠ lose). Events:
`OnHealthChanged` `:202`, `OnDied` `:204`, `OnDeath` `:679` (+ F8-15 listener-name death
forensics `:579-597`). **`static MoveSpeedMultiplier`** `:195` — the injured-stance slow
(`:1102`) HeroLocomotion multiplies into every step. Nested `HeroHealthBootstrap` `:1224` DDOL
attaches HeroHealth + HeroHitReaction to the HeroAbilities GO per scene.

### HeroProgression — `HeroProgression.cs` (280) — `IXpEarner`, id `"hero"`
XP/level; curve `level*120 + 80`; `DamageMultiplier` (+6%/level, cap 3×); grants **Wisdom**
per level (level-gated only — WO-763: Wisdom is earned via level-ups, not sprayed) + skill
points. **Persisted in GameState (HeroLevel/HeroXp/HeroLifetimeXp, schema v29, F8-47)** —
restores on attach (never downgrades a live higher level), writes back on every change; the
old "level 1 after outpost" reset is fixed. `OnLevelUp`, `static OnAnyLevelUp`, `OnXpChanged`.

---

## 5. Gear / equip system

### GearCatalog — `GearCatalog.cs` (500) — `DeNelle.Village`
Loader for **weapons.json (100 weapons)** / **armor.json (24 armors)** + accessories
(`AccessoryDef`). `WeaponDef` now carries **`element`** (drives `WeaponVfxMap.ElementalOnHitKey`;
only `knight_flameblade` = "fire" is authored so far) and `reach`; both defs carry rarity /
`setId` / saga / makersMark / buy costs / `IsAegis`. `BestWeapon/BestArmor(job,level)`,
`FindWeapon/FindArmor`, `WeaponFitsClass/ArmorFitsClass`, `MeetsReq`, `GetBuyCost`, `Reload`.
**The old aegis data bug is FIXED:** all four `aegis_*` weapons now carry `"setId":"aegis"`
(verified in JSON) → `GearLoadout.AegisSetActive` is reachable. Legendary armor sets
oathplate/leafcloak/aethercloak also authored.

### GearLoadout — `GearLoadout.cs` (690) — the canonical equip model
Slots: main-hand `EquippedWeapon`, `EquippedOffHand` (2H↔off-hand mutually enforced via
`EnforceHandSlots`), `EquippedArmor`, `EquippedRing`/`EquippedAmulet` (WO-543 accessories —
pure stat, no mesh). Publishes **`WeaponMult`** / **`ArmorDefense`** / `GearHpBonus`;
`event OnGearChanged`. Per-class PlayerPrefs persistence (`dotr-equip-*-<class>`) with the
**`__none__` removed-slot sentinel** `:109-119` (a deliberate unequip is never auto-refilled).
**WO-808 gear levels are applied at the ONE choke point** `ApplyStats` `:260-272`:
`GearStatResolver.EffectiveDamageMult/EffectiveDefense(def, GearProgression.GearLevelOf(gs,id))`
— every combat consumer reads the leveled scalars. Aegis set (WO-295): ward refund 0.25 +
per-class perk mult + defense bonus `:76-101`; lazily attaches AegisSetEffect + HeroArmorRimLight.
`EquipWeaponById/EquipOffHandById/EquipArmorById/EquipAccessoryById/Unequip*` `:385-668`,
`BindOwnerClass` `:131` (companions), `ArmorVisualTier` `:343`.

### GearProgression — `GearProgression.cs` (276) — WO-808 Option A (owner-locked 2026-07-30)
Per-INSTANCE gear power levels ("improve THIS sword"): `gear-levels.json` curves →
`GearLevelCatalog` → pure `GearStatResolver`; levels keyed by gear id in
`GameState.GearLevels` (additive, no schema bump). **Improve is INSTANT V1** (no queue channel;
Resources-only). Mirrors the barracks troop-level stack piece for piece. Surfaced as
"Improve at the Forge/Armorer + Lv N" across gear UIs (WO-808 `3b66efb3`).

### EquipmentController — `EquipmentController.cs` (2879 — the biggest file in the folder)
Visual weapon attach on the Humanoid rig. Equip pipeline: Addressables first
(`BeginAddressableEquip` `:735`), Resources `Heroes/Props/Weapons/<mesh>` fallback `:808`,
tinted-primitive last resort. Weapon-class map (Sword/Dagger/Axe/Hammer/Staff/Wand/Bow/Shield
`:81-168`); geometric **`SeatByHandle`** sword-grip inference; bounds-normalizing `NormalizeInto`;
render-verify + rollback (`VerifyWeaponRendersNow` `:1046`, `RollbackWeaponProp` `:1091`).
Off-hand shields → `Socket_Shield` on `LeftLowerArm` via `GearSeat` (never LeftHand for a strapped heater). `fullOverride` Offset Forge rows are the seat exactly: no `NormalizeInto`, no `vis.gripPos` add, no `ApplyGlobalWeaponYaw`. Locked heater (`ShieldWithItemLogic` / `knight_shield_starter`, 2026-08-30 persist Play): pos `(-0.103, 0.164, -0.238)` rot `(1.915, -48.302, -127.941)` scale `0.71` on `EquipmentProp_OffHand` (not `_Mesh`). ApplyHoldPose restamps that row; same-id in-flight Addressable skips so Refresh cannot rebuild it. **`SetCombatActive(bool)`** `:1722` —
drawn↔sheathed (back-socket resolve `:1961`, sheath rotation `:1994`); driven per-frame by
HeroLocomotion's `engaged` flag (the "sword out in town" fix). **`SetArmorTier(int)`** `:2095` —
WO-567 armor read WITHOUT mesh swap: MPB multiply-tint on the body renderers `:2120-2158`
(merges with HeroArmorRimLight emission via GetPropertyBlock). Attach-override seams:
`RigAttachmentRegistry` (rig-profiles.json authored attach bones, authoritative over the
avatar auto-map) + `AttachmentOffsetRegistry` (Offset Forge authored offsets; persistentDataPath
wins, reload before every equip). Bows delegated to HeroBowAttachment. `ReseatForBody` `:547`
re-seats after a body swap; `GripRoot` `:329` exposed for the trail. Skips everything when a
`PackageBakedGearMarker` is present `:226`.

### WeaponVfxMap — `WeaponVfxMap.cs` (244) — pure resolver
Rarity → swing-trail color/width (steel/green/blue/violet/**gold** apex; monotonic widths;
makersMark theme tint 0.18) `:52-173` — DataRegression-pinned. **`ElementalOnHitKey(WeaponDef)`**
`:207-241`: element string → owner-tagged full-prefab impact key (fire→Fireball_Impact,
ice/frost→Frost_Impact, lightning→Thunderbolt_Impact, arcane→Arcane_Impact,
poison→PosionCloud_Cast [sic — the catalog key really is misspelled]); **holy/water/earth/nature
HELD — return null until the owner tags a key. Never substitute creatively.**

### ArmorVfxMap (208) + HeroArmorRimLight (140)
Armor/accessory channel analog: dominant rarity across armor+ring+amulet → rim-light
color/intensity via MPB emission (no material instancing); legendary apex plays "Burst_rings".
Lazily attached by GearLoadout; fully Guard-ed.

### HeroArmorVisual — `HeroArmorVisual.cs` (979)
**Header lies by omission:** it describes the Blink full-body armor mesh-swap. Under the
single-Grom no-mesh-swap ruling the Blink swap path is effectively RETIRED; the component
self-guards (no humanoid "HeroBody"/no Blink asset → keeps the existing body, never naked) and
HeroBodySwapper's KnightV3 path suppresses the Blink overlay (`useKnightV3:true` keeps KnightV3
visible, `HeroBodySwapper.cs:386-390`). Still universally attached by HeroControlEnsurer —
treat as a dormant compatibility layer, do not extend.

### Other gear files
- **AegisSetEffect** (136): Oathweld ward — refunds `WardRefundFraction` of hero damage to the
  Heart; now REACHABLE (setId data fixed).
- **GearVisualApplier** (307): legacy primitive-cube gear, `EnablePrimitiveGear=false` — gated
  off; still clears stale `GearVisual_*` children.
- **HeroBowAttachment** (336): ranger bow on LeftHand; procedural fallback; retry until bones
  bind. **LIVE again since the 2026-08-05 roster unlock** — Ranger is playable, so this path is
  no longer dormant *(corrected 2026-08-06; was "dormant under knight-only", `9a0ff548`)*.
- **GearAppraisal** (276): pure lore/value surface (maker's mark, tier label, crystal value) —
  shop labels.
- **ItemIconCatalog** (317) + **GearIconCatalog** (113): sprite resolution. GearIconCatalog is
  the MVVM presentation seam — Views resolve icons by ROLE+ID keys and never name GearCatalog.
- **AccessoryDef** (90): rings/amulets typed model (damageMult/defense/hpBonus additive);
  Jeweler-only.
- **HeroEquipment** (157, `DeNelle.Village.Hero`) — the WO-109 demo stub (basic_sword/
  leather_armor, log-only bonuses). **DO NOT EXTEND** — superseded by GearLoadout/
  EquipmentController. Note: EquipmentPanel NO LONGER pairs with it (see §9).

---

## 6. Body / animation

### HeroBodySwapper — `HeroBodySwapper.cs` (2129)
**Header lies:** still says "Knight / Ranger / Mage" class swap. LIVE path: `ff.knightv3` ON →
`BuildKnightV3Body` `:331-390` skins `Resources/Heroes/KnightV3.fbx` via `VisualFactory.Skin`
(forward yaw **+15°**, height 1.75m, owner felt-tuned `:354-373`), names it "HeroBody", then
`WireHeroBody(..., "Knight", …, useKnightV3:true)` `:390` — controller slug stays "Knight" so
cast/skill states resolve; **`ff.mocaploco` ON binds `KnightMocap.controller`** `:458-471`
(MOCAP-DECISION FlowTrace proves the bind). Legacy Tripo Knight / Paladin package = fallback
chain; **the other classes' body chain is LIVE again** since `ff.knightonly` went default-OFF
*(corrected 2026-08-06; was "unreachable under knight-only")* — and its terminal step no longer
bails out empty: Blink base (Addressables, GITIGNORED pack) -> legacy
`Resources/Heroes/<slug>.fbx` (**no Ranger.fbx / Mage.fbx exists**) -> **`BuildTrackedFallbackBody`,
the tracked KayKit humanoid floor** (`HeroBodySwapper.cs:11-23`, the `d0c7b8fd` invisible-hero P0
— see the DELTA block). Post-swap it
calls `HeroLocomotion.SetAnimator` + `HeroAbilities.SetAnimator` DIRECTLY (WO-581 — the old
reflection write is gone) and `SetHeroClass`. `applyRootMotion=false`; animator speed 1.0
`:33-38`; `cullingMode=AlwaysAnimate`. Holds a clean idle during dialogue (DialogueService
hooks). Material pipeline: embedded PBR diffuse for KnightV3; URP retarget + class tints for
legacy bodies. Also attached to the EMERGENCY hero (HeroControlEnsurer) so even the pill
becomes Grom.

### Animation-adjacent
- **HeroPoseController** (396): TOWN↔COMBAT pose flips + weapon-prop SetActive at transition
  midpoint (param-guarded "Combat" bool is still absent from the controller → the prop half is
  the working half).
- **HeroEmote** (158): plays KnightV3's extracted DANCE clip as a one-shot PlayableGraph, then
  restores the locomotion controller. Trigger hook only — no wheel/HUD (coordinator scope rule).
- **HeroLocomotionCadence** (119): runtime cadence knob — PlayerPrefs `anim.runCadence`
  (default 1.5) applied only while in a locomotion state.
- **HeroGaitForensics** (164): owner-F8 2026-07-12 per-frame gait/camera capture —
  `[Flow:GaitF]` change lines + `gait-forensics.csv` (hip weave vs camera yaw). Diagnostic;
  leave in place per §12 until the gait file is closed.

---

## 7. Cameras / input drivers

- **SmartMobileCamera** (1102) — THE camera. Follow + movement-lead + combat zoom + optional
  framing; player-authoritative orbit (`AddYaw/AddPitch`, never velocity-driven — curl-proof);
  occluder-FADE (WO-385); teleport snap via `HeroLocomotion.OnTeleported`; `CameraYaw` is the
  movement basis (0 in top-down); `EnforceSoleCamera` disables sibling VillageCamera;
  facing-recenter suspends on `HasAnyMoveInput` (SME 2026-07-12 fix). Runtime-attached by
  HeroControlEnsurer wherever missing — EXCEPT dungeons (rig guard).
- **VillageCamera** (175): legacy fixed-offset follow — disabled at runtime by SMC; kept as
  fallback. Header still reads as if it were live.
- **HeroCinemachineRig** (136): DEAD (Cinemachine OTS rig; brain actively disabled by the
  ensurer). In dungeons the live Cinemachine rig is `DeNelle.Dungeons.DungeonCameraRig`, NOT
  this.
- **CameraModeController** (452) + **CameraModeControllerBootstrap** (83): TOWN bird's-eye
  mode — still **build-mode-only** (idle-town TOWN caused the "stuck on the tree" bug);
  bootstrap auto-attaches in Village scenes.
- **CameraPanInput** (235): Lean-Touch slide-to-pan + right-mouse + Q/E/R/F keyboard orbit →
  SMC AddYaw/AddPitch; excludes joystick zone/GUI/build mode.
- **VirtualJoystick** (241): code-built touch thumbstick; `static Move` feeds HeroLocomotion;
  `IsInZone` excludes the pan input; touch-targets only, late-touch re-check for mobile init.

---

## 8. HUD bridges

- **HeroAbilitiesHudBridge** (473): reflection bridge to `DeNelle.HUD.VillageHudController`
  (Village must not ref HUD). IN: HUD `AbilityRequested` → `TryCast`. OUT per-frame: SetMana /
  SetHeroHp / SetAbilityCooldown / SetAbilitySlot (glyph/name/desc/accent from `ResolvedDef` —
  the icon==cast guarantee). Self-resolves the HUD at runtime.
- **HeroEquipHud** (260): the single bag-icon inventory button + bootstrap; wires the HUD
  TOWN-ACTIONS BAG via reflection → `HeroInventoryController.Open`. NOTE commit `16783d62`
  moved the visible entry to a **Grom portrait in the HUD kit** — the HUD side is the live
  skin; this component remains the Village-side opener/bridge.
- **RaidEntryBridge** (222): reflection-subscribes the HUD's `RaidRequested` UnityEvent →
  `RaidSelectionScreen.Open()`; self-bootstraps per hub scene. The HUD **Raids button** itself
  (added `2598f2f7`) + the WO-820 full-army dim gate live HUD-side (`ArmyReadiness` in Core is
  the single readiness source, WO-823).

---

## 9. UI surfaces (namespace `DeNelle.Village.Hero`, all code-built uGUI — NO UXML, canon §8)

All of these follow the strict-MVVM split (UI_MVVM_MIGRATION_PLAN): a PURE VM (IPanelViewModel,
no UnityEngine UI types, icons as ROLE+ID keys, unit-testable) + a dumb-skin View on the shared
`ElarionUiKit` chrome (BuildObsidianPanel / BuildModalCanvas + Scrim + the ONE shared Close),
registered with PanelManager (modal arbiter) and/or PanelRouter.

### Raid front-end
- **RaidSelectionScreen** (372) + **RaidSelectionVM** (122): the Raids card grid over
  `SceneConfigCatalog` — 3 flagship enemy raids (`raider_camp_small`, `fortified_garrison`,
  `mage_enclave`; fallback = all enemy raids). Card = displayName + difficulty badge +
  3-star target time + reward hint. Tap → `RaidDeployScreen.Open(def)`. Static `Open()`
  self-heals a host.
- **RaidDeployScreen** (458) + **RaidDeployVM** (245): pre-raid deploy — party portraits,
  deployable troops grouped by TroopDefId from `ArmyStorage.GetDeployable()`, army-cap readout,
  power rating (attack × veterancy), DEPLOY → `SceneRouter.GoRaid(def.sceneName)`. Still
  first-pass: static clear-time estimate, stub Auto-Recommend (file-header TODOs are accurate).
  The in-raid half (TroopDeployer/RaidScoring/RaidHudController) lives in
  `Assets/_Modules/Village/Troops/` — see that catalog section.

### Barracks / troops
- **TroopTrainingPanel** (676) + **TroopTrainingVM** (441): the Barracks TRAIN panel
  (WO-737 FrameCrafting master-detail; WO-744 strict MVVM). WO-778: `CreateDefault` wires
  **`BarracksService.EnqueueTraining`** → training is TIMED on the Train channel
  (`TrainOutcome.Queued`); the instant `ArmyStorage.TrainNow` loop survives only for tests.
  Ladder shows ALL 7 troops incl. locked (tier education).
- ⛔ **BarracksPanel + BarracksPanelVM — DELETED 2026-09-06 (WO-1430 Group A). Do not look for
  them.** They were the WO-771.9 UPGRADE panel (barracks level card + per-troop levels/abilities/
  costs over `BarracksService`/`BarracksProgression`), and the panel **had no door** — its only
  entry point `ShowBarracksUI()` had zero callers, proven by source grep and a script-GUID search
  of every scene/prefab (`PanelDoorRegression`). That is the defect that stranded 7 of 9 troops
  (`OWNER_RULINGS_LOCKED.md` §21): the panel was the sole composer of the `JobKind.BarracksUpgrade`
  job that was the sole writer of `GameState.BarracksLevel`, the old unlock gate. Troop unlocks now
  read the barracks **BUILDING tier**; the player-facing barracks and troop surfaces are the Manage
  screen's **Build** and **Army** tabs. `JobKind.BarracksUpgrade` and `GameState.BarracksLevel`
  survive the deletion because both are persisted save keys.

### Rumor board (⚠ 2026-08-02 conformance wave IN FLIGHT)
- **RumorBoardPanel** (689): WO-810 owner-signed master-detail — scrollable full-label filter
  chips (selected = gilt + underline + ASCII `*` marker; colorblind law: shape never color
  alone; ASCII only — the font tofus non-Latin glyphs), left ~42% two-line quest CARDS (whole
  card selects, NO row buttons), right ~58% detail with the primary CTA pinned bottom
  (Accept/Track/Pinned — the "ACC…" truncation fix), portrait stacks panes.
  **WO-1192 delta (2026-08-25):** landscape keeps list-left/detail-right. Portrait now uses a
  narrow list rail at left and gives the remaining region to selected detail; its tabs scroll
  horizontally at the touch floor. Missing quest art creates no placeholder slab. Reward chips
  continue to consume `RumorBoardVM.RewardPartsFor`, the typed WO-1201/1202 authority, never a
  fixed chip schema.
- **RumorBoardVM** (328): pure VM over `IRumorBoardBackend` (live = QuestService + QuestCatalog +
  DailyQuestService; tests fake it). Tabs All/Story/Daily/Gear/Endgame; tracked-quest flag;
  StartQuest/SetTracked writes.
- **RumorBoardPanelBootstrap** (40): eager `PanelRouter.Register(PanelId.RumorBoard, …)` at
  scene load — the "HUD quest button cold-open" fix.

### Realm map (WO-826, shipped `eb5d0710`)
- **RealmMapPanel** (441): full-screen parchment map on the Obsidian modal frame — Elarion
  centre + 5 fog regions at `realm-map.json` mapPoint (y DOWNWARD, the React convention);
  node language locked/discovered/cleared/home/selected (all shape+text, ≥48dp buttons);
  landscape map-left/detail-right, portrait stacked; PanelManager + PanelRouter
  (PanelId.RealmMap). Adjacency lines skipped on first ship.
- **RealmMapVM** (348): pure VM over `RealmMapCatalog` (Core) + an ISource save seam.
  RegionState is DERIVED (never stored): home → Home; ledger Cleared → Cleared; ledger
  Discovered OR live gate (bestWave / regionCleared) → Discovered; else Locked. **Nothing
  writes the Discovered ledger yet** and **TravelEnabled is always false** — both are the
  WO-827 stub (FlowTrace.Once documents it). Fresh save: all fog except Thornwood at wave 3.
- **RealmMapPanelBootstrap** (70): per-scene host spawn; suppressed in enemy-owned scenes
  (WO-550); global dedupe.

### Shops / vendors
- ⛔ **ShopPanel + ShopVM — DELETED 2026-09-06 (WO-1430 Group A). Do not look for them.** The
  legacy-context vendor shop (WO-431 MVVM slice). `PanelDoorRegression` reported it
  `[panel-door-is-harness-only]`: the ONLY constructors were `AutoPilotDriver` and
  `UICaptureLaunch`, harnesses that AddComponent every panel so it can be photographed. The claim
  that it "opened when `ff.partyshop` is OFF" was **never true of the code** — `DialogueCommandSink`
  and `DialogueService` both route `OpenShop` to `PanelId.PartyShop` unconditionally. `ff.partyshop`
  is now a **kill-switch**, not a chooser. Deleted with it: `IShopEquipTarget`, `ShopMode`,
  `ShopDetail` (all declared in `ShopVM.cs`), `ShopVMTests`, `ShopPanelRowRenderTests`, and the
  `RealmGoldStore` / `RealmStorePurchase` capture entry points in `UICaptureLaunch`.
- **PartyShopPanelMvvm** (1631) + **PartyShopVM** (1596) + **PartyShopPanelMvvmBootstrap** (75):
  the store-to-spec party shop (STORE_EQUIP_SPEC): party-member selector (IEquipTarget), tap→
  filter by class/level/vendor-type, BUY/SELL same screen, real item art + stat deltas.
  **Flag-gated on `FeatureFlags.PartyShop`** — bootstrap + PanelId.PartyShop routing only when ON.
- **ShopCatalog** (217): the ONE "what is shoppable here" resolver —
  `Shoppable(vendorContext, job, level)` reconciles VendorStockContract × GearCatalog fit/req
  gates, and extends to craftables via the Core Catalog seam.
- **VendorRegistry** (125): `vendors.json` loader — each vendor's shelf is a declared QUERY
  (layout/categories/classFilter/maxReqLevel/emptyLine), consulted FIRST by
  `VendorStockContract.AllowedFor`.
- **VendorStockContract** (166): store-TYPE → allowed GearKind flags; one contract, two
  consumers (shop filter + AutoPilot assertion).
- **VendorStockResolver** (376): resolves a vendor's query against Gear/Consumable/Material/
  Craftable catalogs, **roster-filtered through `PlayableHeroes` — the live set is
  Knight/Ranger/Mage, so Mage wands are ON the shelf again** *(corrected 2026-08-06; was
  "knight-only ⇒ no wands", `9a0ff548`; cleric-ONLY rows are the ones now excluded)* +
  level-locked rows;
  `[Flow:Vendor]` traces; `DataRegression.CheckVendorStock`.
- **StoreStockService** (219): WO-429 offline-first stock — LOCAL catalog is authoritative;
  an optional `IStoreStockProvider` remote snapshot MERGES on top; no network creds client-side.

### Inventory / equip screens
- **HeroInventoryController** (772, partial across **InventoryUIBuilder** 434 /
  **InventoryPaperDoll** 214 / **InventoryGrid** 382 / **InventorySidebar** 116): the
  full-screen inventory modal (tabs Weapons/Armor/Outfits/Consumables, paper-doll, grid,
  detail sidebar; Tech W/A dark-wood+gold skin). Drives GearLoadout equip; PanelManager
  registered; opened via HeroEquipHud / HUD BAG.
- **InventoryVM** (425) + **EquipVM** (480) over the seams **IInventoryStore** (234 — owned
  items + defs + fit-by-class over VillageInventory+GearCatalog) and **IEquipTarget** (210 —
  equip/unequip a hero OR companion without naming GearLoadout): the WO-434 Phase-B pure VMs.
  InventoryVM lists what the player OWNS (closing the old catalog-only data gap).
- **EquipmentPanel** (1003): **REWORKED (2026-06-28 Gear-Preview WO)** — no longer the
  HeroEquipment demo pair. Now the Obsidian "Gear Preview" screen: a live 3D hero preview
  (**HeroPreviewViewer**, 450 — RenderTexture rig with its own EquipmentController so the
  preview holds the real weapon) framed by slot plates (Full Armor / Shield / Weapon / Amulet /
  Ring) + a bottom drawer of compatible owned items; bound to EquipVM. Main-hand and off-hand
  lists are delineated (shields only in off-hand).

---

## 10. Misc / infrastructure

- **HeroLinkCrossing** (66): the id-paired portal marker (`crossingId`, `enterRadius`,
  `bidirectional`, static `Registry`) HeroLocomotion's crossing loop consumes.
- **CastleSpawnMarkerHider** (175): runtime guard hiding every stray placeholder capsule near
  the hub spawn (multiple independent pill sources — see its header list).
- **AttachmentOffsetRegistry** (362): Offset Forge authored offsets; load order Resources →
  persistentDataPath (wins) + PlayerPrefs mirror; `Reload()` before every equip.
- **RigAttachmentRegistry** (203): rig-profiles.json attach-bone overrides (model stays
  pristine; runtime-only resolution; avatar auto-map is the fallback).

## 11. Data files consumed by this folder (all dual-copy: `Assets/Resources/Data/Canonical/` wins in WebGL; StreamingAssets mirror must stay in sync)

| File | State |
|---|---|
| `abilities.json` | 5 blocks (mage/knight/ranger/knight-skills/universal-skills); castSeconds wind-ups; vfx keys authored but dormant (registry-only mode) |
| `weapons.json` | **100 weapons**; `element` field (flameblade=fire only); aegis setId FIXED |
| `armor.json` | **24 armors**; aegis_plate + 3 legendary class sets |
| `accessories.json` | rings/amulets (WO-543) |
| `gear-levels.json` | WO-808 per-instance level curves |
| `vendors.json` | vendor shelf queries (WO-598) |
| `realm-map.json` | WO-826 home + 5 regions, mapPoint/gate/description |
| `motion-castings.json` / `VfxManualPicks.json` | the owner's Motion Caster registry — the ONLY motion-VFX authority |

---

## FLAGS / RISK LEDGER (2026-08-02)

### Comment-vs-code lies (trust the lines cited above, not the headers)
1. **HeroLocomotion** class XML summary still says "kinematic transform translation" — it is a
   NavMeshAgent (`Awake :462`). The file header is corrected; the summary isn't.
2. **HeroBodySwapper** header describes the Knight/Ranger/Mage class swap — which as of
   2026-08-05 is **true again**, not a lie: `ff.knightonly` defaults OFF and the other class
   paths are REACHABLE *(corrected 2026-08-06, `9a0ff548`/`d0c7b8fd`; this entry previously
   read "unreachable code under `ff.knightonly`")*. The Knight still routes
   KnightV3+KnightMocap (`:78-90`); Ranger/Mage have no FBX and land on the tracked KayKit
   floor (DELTA block, item 4).
3. **HeroArmorVisual** header describes the Blink full-body armor swap — retired under
   no-mesh-swap; component is a dormant guard layer.
4. **VillageCamera** header reads as if it were the live camera — SMC disables it at runtime.
5. **HeroAbilityInput** header (":1/2/3/4 keys") contradicts its own body — the number-row keys
   are REMOVED at `:110-113`; only the header's first paragraph is stale.
6. **AbilityCatalog.AbilityEffect** doc says "the six shapes" — eight more raw-string shapes
   (dash/knockback/taunt/blink/dot/healovertime/invuln/gracebuff) resolve in HeroAbilities and
   deliberately parse to Strike in the enum. Reading only the enum under-counts the kit.

### Dead / dormant / do-not-extend
- Dead: HeroChargeVFX, HeroCinemachineRig, HeroAimIK (dormant), HeroVictoryPoseBridge (inert),
  HeroReachRing (dead-by-policy DEF-205), HeroImpactFeedback.PlayRecoil,
  GearVisualApplier (gated off), HeroFootstepController (superseded by HeroLocomotion's own
  footstep loop — two implementations exist, only the locomotion one can go live today).
- **Do-not-extend: `HeroEquipment` (WO-109 stub).** All equip goes through GearLoadout →
  EquipmentController. EquipmentPanel no longer references it.
- ~~Dormant under knight-only~~ — **NO LONGER DORMANT (corrected 2026-08-06, `9a0ff548`).**
  HeroBowAttachment, the mage/ranger branches of LaunchProjectile / HeroBodySwapper and the
  `ranger`/`mage` `abilities.json` blocks are all **LIVE**: `ff.knightonly` defaults OFF and
  the roster is Knight/Ranger/Mage (`PlayableHeroes`). The "second-class future" arrived. What
  IS still missing for those two: **body art** (no Ranger/Mage FBX — tracked KayKit floor) and
  **31 dead talent nodes** pending the WO-910 owner ruling (`04d375c3`). Cleric remains out.

### Open gaps / pending seams (real, verified)
- **Warden's Grace −20% damage reduction is NOT implemented** — needs a HeroHealth.TakeDamage
  mitigation seam (`HeroAbilities.cs:1352` logs "PENDING"). The HUD shows the Grace marker
  regardless.
- **CastVariantKeyword[4] (R) = null** — the R-slot cast beat has NO registry keyword; R casts
  are VFX-silent until the vocabulary grows a row (`HeroAbilities.cs:1432-1435`).
- **Realm map WO-827 stub:** Discovered ledger never written; TravelEnabled always false.
- **RaidDeployScreen first-pass TODOs** (live clear-time, scout report, quantity sliders) are
  real and documented in-file.
- **EquipmentController weapon meshes**: Addressables path first now, but the Resources
  `Heroes/Props/Weapons/` fallback dir remains partially populated — a missing mesh still
  degrades to the tinted primitive (render-verify + rollback protects against invisible props).
- **`ff.lockon` default OFF** — the whole WO-512 lock-face/strafe path is dark in normal play.
- **elemental on-hit**: only `fire` is authored on a weapon (flameblade); ice/lightning/arcane/
  poison keys are mapped but no weapon data uses them yet; earth/holy/water/nature HELD by
  owner rule.
- **2026-08-02 in-flight:** the basic-attack swing+hit-only conformance (impact-burst removal)
  and the RumorBoard conformance wave — re-verify §2/§9 and this ledger when they land.
- **(added 2026-08-06) Ranger/Mage body art is OPEN content work** — no FBX in the tree; both
  degrade to the tracked KayKit humanoid floor on any machine without the gitignored Blink pack
  (`d0c7b8fd`; DELTA item 4).
- **(added 2026-08-06) WO-910 — 31 dead talent nodes across Ranger + Mage**, ratcheted baseline
  in `TalentStrategyRegression`, **READY FOR OWNER RULING** (`04d375c3`; DELTA item 8).
- **(added 2026-08-06) Blurry hero portraits: Grom + Elara UNFIXED.** Imported as plain textures,
  so `Load<Sprite>` nulls and they fall to the RawImage path. Thrain was fixed in `d0c7b8fd`;
  Grom is the DEFAULT hero and is still on the blurry path (DELTA item 6).

### Duplicate / parallel systems (by design or debt)
- Two footstep systems (HeroLocomotion.DriveFootsteps LIVE vs HeroFootstepController unwired).
- Two victory-pose subscribers on OnWaveCleared (locomotion live; bridge inert).
- ~~Two shop stacks (ShopPanel legacy-context vs PartyShopPanelMvvm flag-gated)~~ — **RESOLVED
  2026-09-06 (WO-1430):** there is now ONE shop stack, `PartyShopPanelMvvm` + `PartyShopVM`. The
  legacy pair was deleted; it was never actually a live alternative, because the `OpenShop` route
  had no flag branch. `ff.partyshop` OFF now means no gear shop at all.
- Two inventory-ish surfaces (HeroInventoryController modal vs EquipmentPanel gear-preview) —
  both live, different jobs (bag vs paperdoll/preview).
- Namespace split `DeNelle.Village` vs `DeNelle.Village.Hero` persists — check the `using`
  before referencing UI types from gameplay code.

---

*99 files verified from source at HEAD `b77a178e`. Keep this file green: any change to the cast
engine choke points (`Resolve`/`CastResolved`/`ApplyStats`), the ensurer attach list, the
dungeon-rig guard, or a flag default listed above MUST update this catalog in the same commit
(CLAUDE.md §15).*
