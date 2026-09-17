# Clock In — Solana Mobile Hackathon: submission plan

Date: 2026-09-16. Planning lane, read-only on code. **Nothing here is a ruling.**
Owner rulings this plan serves (2026-09-16): two weeks to ~09-30 of **video + bugs only**; the video
**leads with Heartbound** and **finishes with raid → 3-star Iron Bastion → inherit → repair → practice**;
**the hero is the Mage** (*"i plan on doing the video around the mage, as he is my favorite and i think
the most fun"*).

Hackathon facts, from the owner's brief (cryptobriefing.com): 30 days, $135k — ten $10k grand prizes,
five $5k honourable mentions, a separate $10k SKR bonus for the best Solana Mobile Stack integration.
A submission is a **functional Android APK with SMS integration, Mobile Wallet Adapter support, and a
demonstrably mobile-first design**. **Deadline, video length and judging criteria: TBC from the owner** —
`https://solanamobile.radiant.nexus/` is client-rendered and was not fetched by this lane.
Assume **3:00 max** until she confirms.

Every source below was opened on `dev` today unless the row says otherwise (CLAUDE.md §11B).

---

## 0. The four findings that decide the plan

| # | Finding | Proof read today |
|---|---|---|
| F1 | **The verified-stake read IS driven client-side.** The research brief's "LIVE" label survives. | `HeartboundStatusClient.Driver` — `Assets/_Modules/Wallet/HeartboundStatusClient.cs:309`, `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` `:316`, polls 300 s once `WalletPreferenceStore.CurrentSessionWalletAddress` is non-empty, `RefreshAsync(false).Forget()` `:347`. `Sys = "Heartbound"` `:56`. |
| F2 | **The Echo Event has NO client caller.** `HeartboundEventClient.AcceptPayload` (`:63`, `:95`) is referenced by **zero** files under `Assets/_Modules` — only by `Assets/Editor/Regression/HeartboundEventRegression.cs:93`. The engine is committed (`a65c4959a`, WO-1678) and unreachable. | grep of `HeartboundEventClient` across `Assets/_Modules`, 2026-09-16 |
| F3 | **A Heart Pulse cannot be scheduled for a filming day.** It mints only when the staking program's share price is observed to advance, ≤1/day (`api/cron/heart-pulse.js`, per the 09-11 research brief §5 and the 09-16 essence brief). | briefs; route file not opened by this lane |
| F4 | **The Mage body is a REMOTE R2 bundle.** `d706b430b perf(heroes): move the hero bodies and atlases to R2 — 80 MB leaves the initial download (WO-1187)`; the file is `Assets/HeroContent/Mage.fbx` (8 565 536 B). CLAUDE.md §16 therefore governs the filming build: **no push, no Mage.** | `git log -- Assets/Resources/Heroes/Mage.fbx`; `ls -la Assets/HeroContent/` |

⛔ **There is no Heartbound simulation in the repo.** `FeatureFlags.StakeDemo` (`ff.stakedemo`, default
OFF, `Assets/_Modules/Core/FeatureFlags.cs:615-624`) seeds `StakeRewardsDemoBootstrap` → `MockStakeQuery`
→ `StakeRewardsResolver` / `StakeRewardsPanel` — the **retired standalone stake ladder** the 09-11 brief
says must not be described as the current benefit authority. It touches neither
`HeartboundStatusClient`, nor resonance tiers, nor pulses. `FeatureFlags.SkrPreview` (`:642`, OFF) is a
Title-screen badge + read-only showcase. **Neither can produce a Heartbound tier, a pulse or an Echo
Event on camera.** So act 1 needs a real verified stake, or it cannot be filmed honestly.

**Is there a real stake?** `WorkOrders/WORK_ORDER_1686_heart_013_grant_reviewer_demo_path.md:21` records
the owner as holding **1,000,000 SKR staked**, citing `WORK_ORDER_skr_staking_and_seeker.md` §1 — and
flags it as **dated 2026-06-28, in a CLOSED WO, NOT verified at HEAD**. This lane cannot verify a wallet
position from the repo. **Open question #1 for the owner.** Minimum eligibility is 100 SKR (essence brief).

---

## 1. SHOT LIST — 3:00 max, Mage save, Seeker capture

`E` = exists today · `BNW` = built, not wired · `MB` = must be built.
The **proof line** is what a device capture must show before the shot is filmed (CLAUDE.md §12).

### Act 1 — Heartbound (0:00–1:25)

| # | What the viewer sees | Surface | State | Cite | Device proof line |
|---|---|---|---|---|---|
| 1 | Cold card: *"The Heart remembers every soul it has kept."* | edit | E (edit asset) | `docs/ECHOES_OF_ELARION_PITCH_APPENDIX.md:134-140` | — |
| 2 | Seeker home → the game opens in the town, Mage standing at the Heart | `Main_Castle_Overworld` | **E** | `CastleHubBuilder.cs:524`, Heart anchor `HeartOfElarion` `:2709` | `[Flow:Scene]` scene-loaded + a Mage body resolved (see §4 M1) |
| 3 | **Wallet connect via MWA** — the Seeker wallet sheet, approve, back in game with an address | Night Market wallet chip / connect | **E** | `IWalletProvider.Connect` `Assets/_Modules/Wallet/WalletService.cs:189` (*"MWA on Android/Seeker"* `:187`), `MwaSessionStore.cs`, `ConnectTimeoutSeconds = 30f` `:289` | `[Flow:Wallet]` connect → `Connected`; `[Flow:WalletUI]` |
| 4 | **"Native SKR detected"** → the verified stake amount | Heartbound panel | ⛔ **MB (panel) over E (data)** | data F1; **panel is HEART-008 = SPEC**, `WorkOrders/WORK_ORDER_1681_...md` | `[Flow:Heartbound]` a refresh that returns a verified snapshot |
| 5 | The **resonance tier** by name (Emberbound…Heartbound) + stake power / tenure power | Heartbound panel | **MB** (same panel; tier maths is BUILT server-side, WO-1676/1679) | `WORK_ORDER_1679_...md` **IMPLEMENTED**; ten tier names in the essence brief | `[Flow:Heartbound]` tier in the parsed response |
| 6 | Back in the world: a **Heart Pulse lands** — Heartfire, then roots, then an outward wave the buildings catch | the Heart in town | ⛔ **MB** (HEART-007 = SPEC, greenfield) + **F3** (no pulse on demand) | `WORK_ORDER_1680_...md` §0: *"There is no progression-driven visual system on the Heart at all"* | a pulse id in `[Flow:Heartbound]`, then `[Flow:HeartboundEvent]` |
| 7 | **Echo Event reveal** — a named event card (e.g. Resource Surge / Rough Stone) | event reveal surface | **BNW** — engine committed, **no client caller (F2)** | `HeartboundEventInbox.Sys = "HeartboundEvent"` `:62`; `HeartboundEventApplier.cs:72` | `[Flow:HeartboundEvent]` accepted → applied |
| 8 | **A benefit the player uses** — the extra jewel-polishing attempt, taken on camera | Jeweler | **E** (the one live benefit) | `PolishBonusProvider.cs:17-18, :66, :84` — reads `VerifiedStakeSnapshot`, gated on verified active stake | `[Flow:Stake]` / the polish grant line |

⚠ **Shot 7 climax risk.** WO-1678 **Q-STONE** is unruled: whether a pulse may grant a rough stone.
WO-1686:59 — *"the demo's climax changes"* if it may not. **Do not storyboard shot 7 on the stone until
she rules.** Safe substitute: **Resource Surge** or **Echo Labor** (both in the authored table, both
modifier-shaped, neither touches the ruled stone chain).

### Act 2 — raid → capture → town (1:25–2:50), Mage throughout

> **Owner ruling 2026-09-16 (later the same day):** act 2 OPENS at the end of a dungeon. "We would start
> recording at the end of the dungeon right before we kill the boss and we get the new plans. The plans
> open the new bastion and then we go to the bastion." Shot 8 below is therefore preceded by: boss kill ->
> Bastion Plans drop + reveal (WO-1804, second plans kind) -> the Iron Bastion unlocks -> raid it. The
> dungeon lantern must read well in that opening (WO-1805 teach/gauge is now video-adjacent).

| # | What the viewer sees | Surface | State | Cite | Device proof line |
|---|---|---|---|---|---|
| 9 | Realm Map → Iron Bastion → deploy screen, **BEGIN ASSAULT** | `RaidDeployScreen` | **E** | WO-932 status line (verified in-tree 2026-08-24): `RaidDeployScreen` "BEGIN ASSAULT", `RaidScoring` "RAID CLOCK armed" | `[Flow:Raid]` clock armed |
| 10 | **Mage breach**: Fireball on the primary button, **Void Rift** on the wall line, **Wither** on the defenders, troops pour the breach | raid scene | **E** | kit §4; breach behaviour WO-1746 **FIXED/gated 2026-09-15** | `[Flow:Combat] ranged-primary-mage` (once, §4 M3); `[Flow:Raid]` breach |
| 11 | The clear resolves **3 stars** | raid victory | **E** | `RaidScoring.cs:1625` reads `_lastHonorStars == 3` | `[Flow:Raid]` stars=3 |
| 12 | **"The town is yours"** — Iron Bastion becomes the player's | capture settlement | **E**, but ⛔ **UNPROVABLE AS SHIPPED** | `OwnedBaseProgression.FinalRaidId = "iron_bastion"` `:11`, `CaptureStarsRequired = 3` `:20`, gate `:28-32` | ⛔ **NONE — `OwnedBaseProgression.cs` and `RaidCaptureCensus.cs` contain ZERO `FlowTrace` calls.** §12 build item B0. |
| 13 | The inherited town in its damaged state; **repair** one structure with the essential envelope | owned-town scene | **E** | `OwnedTownRepairService`; proofs `OwnedTownRepairPaymentProof`, `OwnedTownMovePlayProof` (WO-1705 RESULT table) | the repair commit line |
| 14 | **Practice** — three attackers against the frozen saved build, no wager | practice scene | **E** | `OwnedTownPracticeSession`; WO-1705 RESULT: *"local practice, with no wager, ranked award, or claim of server-verified combat"* | practice session start/settle |
| 15 | *(optional, if a wave shot is wanted)* town wave: the Mage holds the gate | town waves | **E, but see WO-1773** | §5 | `[Flow:Wave]` + hero damage taken ≠ 0 |
| 15b | **Future state (owner ruling 2026-09-16):** the last gameplay beat shows the ARENA UI as "what comes next" — creating/entering an arena match on the captured town's layout (the same 169-structure layout the raid, the owned town and the practice arena now share, WO-1767). Filmed as a design frame or a build-mode mock, labelled on screen as upcoming, never as live matchmaking | mock / practice scene | **MUST BE BUILT (mock)** | WO-1705 §"AI arena" + WO-1681 panel patterns; `docs/RAIDS_TO_PVP_ARCHITECTURE_2026-09-10.md` | — (a mock; the on-screen "coming next" label is the honesty proof) |
| 16 | Closing card: logo over the world tree; one line of claim-safe copy | edit | E | — | — |

**One closing card, and it must be claim-safe.** Use the 09-11 reconciliation's own wording
(`docs/SKR_VISION_RECONCILIATION_2026-09-11.md`, "A safer pitch draft"). ⛔ **Do not** reuse the
appendix's 1:50 sponsor beat ("the winner's wallet receives SKR") — there is no SKR payout loop and
`PackStore.cs:467-473` says there must never be one. ⛔ Do not say "play to earn", "yield", or
"rewards from staking"; say **the stake never moves and Elarion only reads it**.

The appendix's production rules still hold and are the right discipline:
*"Every shot is a live screen capture. No animated mockups. No stills."* and *"If the drop is not yet
wired when the video is shot, shoot the video after the drop is wired."* (`:195-197`).

---

## 2. BUILD LIST — cheapest first, ordered by shot dependency

Lane-day = one focused agent-day. Estimates are this lane's judgement, not measured.

| Order | Item | Shots | Minimum for the video | Full spec | Days |
|---|---|---|---|---|---|
| **B0** | **FlowTrace at the capture seam** — `Enter/Step/Fail` in `OwnedBaseProgression.TryCapture` + `RaidCaptureCensus` | 12 | 3–4 lines naming raidId, stars, receipt, accept/refuse | same | **0.25** |
| **B1** | **Gate + device-pass the six landed raid fixes** (§3) | 9–14 | one gate, one Seeker pass | same | **0.5** |
| **B2** | **HEART-005 client delivery** — one caller connecting the status response to `HeartboundEventClient.AcceptPayload` → `HeartboundEventInbox` → `HeartboundEventApplier` | 7 | the wire + **one** event reveal (a toast naming the event) | nine events, full reveal ceremony, choice UI | **1** |
| **B3** | **HEART-008 panel + door** (WO-1681) | 4, 5 | a **read-only** panel: "Native SKR detected", verified amount, tier name, the trust sentence; one door | no-stake / unstaking / RPC-failure states, effective-stake explainer, refresh | **1.5** |
| **B4** | **Pulse presentation, minimum cut** (HEART-007 D3, WO-1680) | 6 | Heartfire brighten → root illumination → one outward wave → hand off to B2's reveal. 2.5–4 s, skippable. **No tier ladder.** | ten tiers → six visual states, tier-up ceremony, all nine differentiators | **2** |
| **B5** | **A capturable pulse** (WO-1686 §0a-i) — *replay the presentation of a pulse that already happened*; fabricates nothing, grants nothing | 6, 7 | replay only | + the test-only fabricator in `test/` | **1** |
| **B6** | **HEART-007 tier visuals on the Heart** | — | ⛔ **CUT.** Act 1 reads the tier off the panel (B3); the world does not have to carry ten steps for a 3-minute video. | the full ladder | 3–5 |

**Total minimum: ~6.25 lane-days** across B0–B5, four of them file-disjoint (B0 state · B2 wallet/core ·
B3 UI · B4 world VFX), so a two-week window absorbs it alongside §3.

⛔ **B3 and B4 are `DAPP_STORE`-only.** `WORK_ORDER_1681_...md` §0a: the trust sentence alone contains
`SKR` and `Solana`, which `GooglePlayPackagingGate.cs:52-59` treats as forbidden surface and
`AndroidBuild.cs:199-204` fails the build over (`PLAY_ARTIFACT_REJECTED`). Panel **and door** live in
`DeNelle.Wallet` (`defineConstraints: ["!GOOGLE_PLAY"]`). Mirror the Realm Store card's channel gate;
do not invent a mechanism.

### Q-AXIS needs a ruling — and it does not block filming

`WORK_ORDER_1680_...md` §5 Q-AXIS: the Heart already has a **threat** axis (`HeartController.cs:44-80`,
seven states with authored colour/emissive/pulse) and an **HP** aura (`HeartAuraController.cs:1-33`).
Resonance would be a third. Options: (a) suppressed in combat; (b) resonance uses only channels threat
does not (ground markings, root illumination, ambient wisps); (c) resonance wins.

**Safe default for filming: (b), and order the shots so the question never arises.** Act 1 is the town
**in peace** (threat axis at Serene); act 2 is the raid scene, which has no Heart. Film every resonance
frame before any wave starts. **Recommend (b)** — WO-1680 notes the spec's own differentiator list is
already shaped for it. ⚠ **Q-COLOR is the harder one and is also unruled**: the owner is colourblind, so
a ten-step ladder cannot be signalled by hue; the precedent is `HeartAuraController.cs:16-24` (size,
luminance, motion). B4's minimum cut avoids it by carrying no ladder.

---

## 3. BUGS **IN** the video path

**Six landed fixes are `IMPLEMENTED, NOT YET GATED` — the first action is the lead's gate, not a device
test.** All six sit in act 2.

| WO | Path | In which shot |
|---|---|---|
| 1763 raid difficulty remote tunables | `WorkOrders/WORK_ORDER_1763_raid_difficulty_remote_tunables.md` | 9–11 |
| 1764 troops prefer walls over nearby targets (Bastion) | `..._1764_troops_still_prefer_walls_over_nearby_targets_bastion.md` | 10 |
| 1765 Bastion camera rotates with walls | `..._1765_bastion_camera_rotates_with_walls.md` | 10 — **a rotating camera ruins the breach shot** |
| 1767 capture census lacks baked stable identity | `..._1767_bastion_capture_census_lacks_baked_stable_identity.md` | 12–13 |
| 1768 hero down after victory resettles army as failure | `..._1768_hero_down_after_victory_resettles_army_as_failure.md` | 11–12 — **a Mage is glass; he plausibly dies at the win** |
| 1748/1750/1751/1752/1756/1758 | same bucket, all raid-scene | 9–11 |

**READY and in-path:**
- `WorkOrders/WORK_ORDER_1773_high_level_hero_takes_no_damage_in_town_waves.md` — **only if shot 15 is
  kept.** Its own banner: *"BUILD UNDER TEST IS UNKNOWN… Nothing here is proven to be the code the
  tester ran."* An invincible hero on camera is worse than cutting the shot — **recommend cutting shot
  15** unless she wants a wave beat, in which case 1773 is a blocker.
- `WorkOrders/WORK_ORDER_1722_wall_collider_residual_and_destroyed_wall_walkability.md` — shot 10. The
  board flags it `READY_BUT_MOVED` (files committed after mint); re-verify before dispatch.
- `WorkOrders/WORK_ORDER_1747_raid_watchtower_and_cleric_glass_material_has_no_albedo.md` — shot 9–10
  framing; tower half already refuted at material level, awaiting the per-renderer probe.

**In-path but BLOCKED on an owner word (surface, don't work):**
`WORK_ORDER_1769_baked_town_camera_enemy_mask_is_structure_layer.md`,
`WORK_ORDER_1771_town_hub_camera_flaps_pull_in_78_per_minute.md` — **1771 is a town-camera flap 78×/minute
and act 1 is entirely in the town.** If it is visible on a capture it becomes a blocker.

**OUT of the video path** (do not spend the window on them): `1701` (WebGL/Pi hero art — the submission
is an Android APK), `1736` (blocked on which build the external tester ran), `1770` (garrison/outpost
raids — act 2 is Iron Bastion), `1772` (dungeon camera lints), `1712`, `1356`, and every art-drop ticket
(`1487`, `1509`, `1539`).

---

## 4. MAGE CHECKLIST

**The shipped default kit** — `Assets/Resources/Data/Canonical/abilities.json`, `classes.mage.abilities`:

| Slot | Id | Name | Effect | cd / range / dmg | VFX keys authored |
|---|---|---|---|---|---|
| **Q (LOCKED)** | `mage.fireball` | Fireball | `strike` | 0.6 s / 14 m / 30 | `Fire_Cast`, `FireballTower_Projectile`, `FireImpact_Impact` |
| W | `mage.shell` | Arcane Shell | `shield` | 16 s / self | `ShieldBuff_Cast`, `ShieldBuff_Aura` |
| E | `mage.drain` | Drain | `drainshot` | 9 s / 13 m / 28 | `EnemyCast_Cast` **(cast only — no projectile/impact)** |
| R | `mage.poison` | Poison Cloud | `dot` | 42 s / 14 m | `Posion_Cast` *(owner's spelling, mapped verbatim)* |

Resource: **Mana**, max 24, regen 0.6/s (`classes.mage.resource`). Q is never loadout-swappable; only
W/E/R are (CLAUDE.md §7).

- **M1 — the Mage rig in this build is the ORIGINAL, not the decimated one. PROVEN.**
  `sha256(Assets/HeroContent/Mage.fbx)` = `86027e893f22eacad8017ba3490758807abfc4b6439aeb1864e191832fc2d6ab`
  = `Mage.fbx.ORIGINAL` in `Backups/mesh-decimation-2026-09-03/SHA256.txt`. The 50k decimation that the
  owner reported as *"smashed and shapeless"* was reverted and the revert is intact. Memory
  `decimation-50k-broke-mage-deformation` — **still judge him IN MOTION on device**, because that memory's
  whole lesson is that stills prove nothing about skinning. Parked until after prod: **do not re-decimate
  inside this window.**
- **M2 — F4 is the capture killer.** The Mage body ships from R2. `R2_PARITY_OK` on a **fresh** log for
  the **filming build** (CLAUDE.md §16 — bundle names are content-hashed, so yesterday's push proves
  nothing), then `adb logcat` grepped for `RemoteProviderException` / `404` on the hero bundle before a
  single frame is kept. WO-1701 is this exact failure on another channel.
- **M3 — the Mage's primary attack button casts Fireball, not a melee sweep.**
  `HeroAbilities.TryGetRangedPrimary` (`Assets/_Modules/Village/Hero/HeroAbilities.cs:498-513`) returns
  true for a `strike`/`drainshot` locked Q whose `Range` exceeds `2 ×` measured melee reach
  (`RangedPrimaryReachFactor = 2f`, `:479`); Fireball is `strike` at 14 m. It emits
  `FlowTrace.Once("Combat", "ranged-primary-mage", …)`. ⚠ **CLAUDE.md §7 records this as CONTESTED**
  (`WO-1504`, unruled): canon prose says the primary is melee for every class, the code derives it per
  class. **Filming freezes current behaviour on tape.** Risk, not a blocker — but flag it to her before
  the shoot, because a ruling landing after the video invalidates shot 10.
- **M4 — the tester's HUD kit is NOT the default kit.** CAST / BLOCK / **Void Rift** / **Arcane Bolt** /
  **Wither** are `classes.mage-skills` **pool** spells equipped into W/E/R: `mage.void-rift` (`aoe`,
  `RangedSpell-Powerful(Longcast)_Cast`), `mage.arcane-bolt` (`strike`, `SimpleCast_Cast` /
  `SimpleCast_Projectile` / `FireImpact_Impact`), `mage.wither` (`dot`, `PosionCloud_Cast`).
  **Decide and record the demo save's loadout.** Void Rift on a wall line reads better on camera than
  Arcane Shell; Wither over defenders reads better than Poison Cloud's 42 s cooldown.
- **M5 — every ability in the chosen loadout has a VFX key, and the keys are owner-tagged.** Verified
  above. Run `EliteVfxWiringRegression`, `VfxLoopFlagRegression`, `HeroElementCastVfxRegression`,
  `VfxParticleNullSlotRegression` (`VFX_NULL_SLOT_OK`) and `VfxResourceSelfContainmentRegression`
  (`VFX_SELF_CONTAINED_OK`) on the filming build, then **look at the PNGs** — a wired key is not a
  visible effect. ⛔ Never creative-pick a missing key (memory `vfx-map-owner-tags-no-creative-pick`).
  ⚠ Drain has **no impact/projectile key** — if the reversed beam reads as nothing on camera, that is an
  owner-tag request, not a fix.
- **M6 — Mage-specific board history, all CLOSED, none open:** `WO-1429` out-of-mana Mage has no attack
  (CLOSED 2026-09-08, owner PASS), `WO-1059` hero preview render texture blank (DONE). **No open
  Mage-specific defect was found.** Sanity-check mana on camera anyway: 24 max at 0.6/s regen means a
  long breach can dry him out, and 1429's fantasy ("out of mana and engaged = death") is the design.
- **M7 — the demo save must be a Mage save**, on the owner's device, with a verified stake attached to
  the wallet it connects (§0) and Iron Bastion reachable.

---

## 5. PARK LIST — proposed `**Status:** PARKED`

Serve neither act nor an in-path bug. **This lane edited none of them**; paths for the lead:

`WorkOrders/` — `WORK_ORDER_1001_deep_dungeon_program.md`,
`WORK_ORDER_1004_composed_dungeon_pipeline_visual_fixes.md`,
`WORK_ORDER_1008_dungeon_exit_beacon_reads_as_light.md`,
`WORK_ORDER_1053_battle_and_monthly_packs.md`,
`WORK_ORDER_1071_storehouse_deeds_permanent_storage_skus.md`,
`WORK_ORDER_1072_one_crystal_valuation.md`,
`WORK_ORDER_1074_kingdom_cosmetic_identity_program.md`,
`WORK_ORDER_1117_monetization_profitability_program.md`,
`WORK_ORDER_1122_season_pass_and_revenue_kpi.md`,
`WORK_ORDER_1164_one_store_tabbed_commerce.md`, `WORK_ORDER_1169_command_center.md`,
`WORK_ORDER_1176_shelf_redesign_and_companion.md`,
`WORK_ORDER_1182_crafting_panel_is_the_last_uxml_surface.md`,
`WORK_ORDER_1183_season_pot_competitive_ladder.md`, `WORK_ORDER_1185_timed_builder_taste.md`,
`WORK_ORDER_1247_patron_covenant_reward.md`,
`WORK_ORDER_1262_dungeon_no_repair_surface_while_burning.md`,
`WORK_ORDER_1267_command_center_identified_ops_drilldowns.md`,
`WORK_ORDER_1276_animated_town_showcase_top10_visits.md`,
`WORK_ORDER_1277_community_showcase_voting_competitive_cosmetics.md`,
`WORK_ORDER_1356_board_submit_button_and_bounce_with_note.md`,
`WORK_ORDER_1444_manage_queue_face_count_composed_never_painted.md`,
`WORK_ORDER_1475_grantspendable_callers_discard_the_clamped_remainder.md`,
`WORK_ORDER_1487_twenty_of_twenty_four_build_tiles_have_no_portrait.md`,
`WORK_ORDER_1509_orc_enemies_have_no_albedo_the_texture_tree_is_empty.md`,
`WORK_ORDER_1527_army_cap_moves_onto_barracks_progression.md`,
`WORK_ORDER_1528_camp_ladder_moves_several_difficulty_axes_at_once.md`,
`WORK_ORDER_1529_verify_the_field_cleric_205_gold_price.md`,
`WORK_ORDER_1534_the_raid_loop_never_closes_and_manage_cannot_reach_it.RESULT.md`,
`WORK_ORDER_1539_three_enemy_models_have_no_basecolor_anywhere_in_the_project.md`,
`WORK_ORDER_1592_raid_felt_northstar_program.md`,
`WORK_ORDER_1683_heart_010_exploit_protection_and_failure_handling.md`,
`WORK_ORDER_1691_wandering_merchant_visiting_npc_lifecycle.md`,
`WORK_ORDER_1701_webgl_hero_art_falls_back_to_blink_base_sync_addressables_load.md`,
`WORK_ORDER_1712_tap_building_in_world_to_enter_upgrade_screen.md`,
`WORK_ORDER_1770_garrison_outpost_raids_still_run_town_camera.md`,
`WORK_ORDER_1772_dungeon_camera_lints_pin_the_dungeon_only_gate.md`,
`WORK_ORDER_467_moat_bridges_seam_geometry.md`, `WORK_ORDER_513_coordinated_family_combat.md`,
`WORK_ORDER_514_tower_cap_and_saved_echoes.md`, `WORK_ORDER_822_barracks_teach_v2.md`,
`WORK_ORDER_827_realm_map_discovery_travel.md`, `WORK_ORDER_876_troop_combat_vfx.md`,
`WORK_ORDER_904_fortification_walls_and_gates.md`,
`WORK_ORDER_907_elemental_affinity_system.md`,
`WORK_ORDER_914_status_mount_compass_waveblock_layout.md`,
`WORK_ORDER_925_kill_persistent_foot_fire_vfx.md`,
`WORK_ORDER_926_combat_anim_root_motion_recovery.md`,
`WORK_ORDER_932_raids_full_functional_audit_and_fix.md`,
`WORK_ORDER_COMBAT_VFX_BATCH_2026-07-10.md`, `WORK_ORDER_Village2_Enemy_Stronghold.md`,
`WORK_ORDER_ad_generator.md`, `WORK_ORDER_offline_storage_logic.md`,
`WORK_ORDER_outpost_base_footprint.md`, `WORK_ORDER_techdebt_ledger_2026-06-28.md`,
`WORK_ORDER_PROGRAM_723_731_coc_arena_barracks.md`.

**Explicitly NOT parked** (they serve this plan): `1680` (B4), `1681` (B3), `1686` (B5), `1766` + `1775`
(capture tooling), `1773` (only if shot 15 survives), `1722`, `1747`, `1504` (a ruling this video's
shot 10 depends on — see M3).

---

## 6. CAPTURE PLAN

1. **Build and prove the filming APK first.** CLAUDE.md §16: `tools\r2-ship.ps1`, judged by
   `R2_PUSH_OK` + `R2_PARITY_OK` on a **fresh** log, never an exit code; install through
   `install-apk-to-seeker.ps1`, **never a raw `adb install`**. Then §8's ladder:
   `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` + `UI_CAPTURE_OK`. Record `Application.version`.
2. **Mirror read-only** — `.claude/skills/run-defenders/SKILL.md:202-262`. Seeker serial
   `SM02G4061955851`; put the Unity `platform-tools` `adb` on PATH for the session.
   ⛔ **`--no-control` is mandatory** — a scrcpy window forwards every click to the device, and memory
   `device-lanes-overlay-apps-and-start-new` records the owner's save being wiped by an untapped
   START NEW under an overlay. Check for overlays first:
   `adb shell dumpsys window windows | grep -i "SYSTEM_ALERT\|TYPE_APPLICATION_OVERLAY"`.
3. **Record** — `scrcpy -s SM02G4061955851 --no-control --no-playback --no-audio --video-codec=h264
   --record logs\device\hackathon-<shot>-<stamp>.mp4`. ⚠ `--video-codec=h264` is **load-bearing** on
   this Seeker: the default codec wrote 0-byte files three times. A static screen starves the encoder —
   record while something moves. Kill every scrcpy process when the lane ends.
4. **Scripted scenarios** via `WorkOrders/WORK_ORDER_1775_device_scenario_harness_scrcpy_devkit.md`
   (READY) for the repeatable act-2 legs, and `WORK_ORDER_1766_mobile_use_agent_felt_test_pilot.md` for
   the driven passes. Land 1775 **before** the shoot — re-shooting a 3-star Iron Bastion clear by hand
   is the most expensive shot in the video.
5. **Logcat beside the mirror** — `adb -s <serial> logcat -v time Unity:V '*:S'`, after
   `adb logcat -g` (memory `logcat-ring-buffer-destroys-evidence`: the ring is per device and the Flow
   firehose can evict the window you need). Every proof line in §1 must appear on the same run as the
   footage it proves.
6. **The demo save** — a **Mage** save (M7), Iron Bastion reachable, on the owner's device.
   ⛔ **The stake cannot be simulated for act 1.** `ff.stakedemo` and `ff.skrpreview` reach the retired
   ladder, not Heartbound (§0). So either her real ≥100 SKR native stake is live, **or act 1 is cut.**
7. **The honesty constraint, if anything is not a real on-chain event.** WO-1686's rule and the
   appendix's are the same: the capture must contain **no fabricated chain state**, and a pre-recorded
   pulse may be **replayed as presentation** (it already happened, it already paid) but never re-granted.
   If a pulse is dev-forced for the shoot, **the video must not claim it fired from an observed
   share-price advance** — say "a Heart Pulse", not "today's on-chain pulse". A false shot is the one
   thing that loses a hackathon after it is won.

---

## 7. Open questions for the owner

1. ⛔ **Is a real native SKR stake of ≥100 SKR live on the wallet the demo device will connect?**
   1,000,000 SKR is recorded but only in a dated, closed WO (§0). **Act 1 is gated on this answer.**
2. **Video length and the exact deadline** — the site is client-rendered and was not fetched. 3:00 assumed.
3. **Judging criteria** — unknown. If "SMS integration depth" is scored, shot 3 (MWA) and shot 4 deserve
   more of the runtime than this cut gives them.
4. **Must the submitted APK be the live dApp-store build, or a tester build?** The store build ships
   `ff.skrpreview` OFF and the Heartbound panel does not exist in it yet. A tester twin burns no store
   version code (memory `dapp-store-version-codes-are-burned-on-upload`), but "functional APK" may mean
   the shipped one.
5. **Q-STONE** — may a pulse grant a rough stone? It decides shot 7 (§1).
6. **Q-AXIS / Q-COLOR** — recommended default (b) + no ladder for the video (§2); a ruling is still owed
   before HEART-007 proper.
7. **WO-1504** — is the Mage's primary Fireball (as the code does) or the melee sweep (as canon says)?
   Shot 10 films whichever is true on the day (M3).
8. **Music and VO** — the appendix says neither is required and that silence is the point. Confirm, or
   name a track; if VO, whose voice.
9. **Shot 15** — keep a town-wave beat (then WO-1773 is a blocker) or cut it?

---

## 8. What this lane could NOT verify

- The hackathon deadline, required video length, and judging criteria — **client-rendered site, not fetched.**
- Whether the owner's SKR stake is currently active (§7.1). Not derivable from the repo.
- `api/cron/heart-pulse.js`, `api/heartbound/status.js` and the resonance config were **not opened**;
  every backend claim here is relayed from `docs/SKR_CURRENT_USE_RESEARCH_BRIEF_2026-09-11.md` and
  `docs/HEARTBOUND_ESSENCE_BRIEF_2026-09-16.md`. Re-read at source before building B2/B5.
- The lane-day estimates are judgement, not measurement.
- Whether the six `NOT YET GATED` raid fixes actually pass — no Unity run in this lane.
- Whether the Mage's measured melee reach is under 7 m (it must be for M3's `2×` threshold to bite at
  14 m); `PlayerAttackController.AttackRange` was not read.
