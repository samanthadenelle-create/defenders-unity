# WORK ORDER 1776 — Mage kit VFX: the owner's lightning on Arcane Bolt, plus the landing beats for Void Rift and Wither

**Status:** IMPLEMENTED, NOT YET GATED
**Number:** PRE-ASSIGNED by the lead (this WO does NOT touch `CLI_LANES_WO_NUMBERS.md`)
**Date:** 2026-09-16
**Silo:** VFX / hero abilities (data + one hook seam). No scene, no rig, no balance.
**Lane:** VFX/Audio (§9) — file-disjoint from world/economy lanes.

---

## 0. Owner rulings this implements (verbatim where quoted)

- The hackathon video is built around the **MAGE**.
- *"right now the fireball is the only thing decent"* — so the fireball marquee
  (`firespell_Cast`, the one `motion-castings.json` mage row) is **NOT TOUCHED** by this WO.
- She wants *"the animation for the lightning"* on **Arcane Bolt** — **RULED**, implemented here.
- Her tag pool is her own: 78 `*_Cast` / `*_Impact` / `*_Aura` keys already tagged in
  `Assets/Editor/VfxManualPicks.json` → `Assets/Resources/VFX/vfx-pick-options.generated.json`,
  and her instruction was **"pick from those"**. Every key below is one of HER rows. This seat
  chose only **which shipped hook each tag hangs on**, and every row is recorded in §3 so she can
  retag any of them in one line after the device capture.

Rule obeyed throughout (memory `vfx-map-owner-tags-no-creative-pick`): the CLI maps an owner tag
to a hook **verbatim** and never picks a look. Candidate lists are NOT proposed here — she has
already tagged, so §3 is a mapping table, not a shortlist. Descriptions are by **shape and
motion only** (memory `owner-colorblind-delegate-visual-creative`).

---

## 1. WHAT PLAYS TODAY — read at source 2026-09-16, not from any doc

### 1.1 How a hero ability's VFX is resolved

`abilities.json` carries four optional keys per ability —
`vfxCast` / `vfxProjectile` / `vfxImpact` / `vfxResidual`
(`Assets/_Modules/Village/Hero/AbilityCatalog.cs:160-167`) — and `HeroAbilities` plays them at
four beats: `PlayCastVfxKey` (`HeroAbilities.cs:2815`), `LaunchProjectile`,
`PlayImpactVfxKey` (`:2874`), `PlayResidualLoop` (`:2974`). Each resolves its key through
`VFXManager.PlayKey` → `HovlVfxCatalog.asset` row → pooled prefab.

**The gate that governs all of it:** `HeroAbilities.RegistryOnlyMotionVfx = true`
(`HeroAbilities.cs:2600`). Owner directive 2026-07-12 *"till i select individually"*: the
`abilities.json` defaults are **SUPPRESSED**, and the only VFX source is the owner's
**motion-castings registry** — **EXCEPT** an individually owner-tagged key listed in
`HeroAbilities.OwnerPickedVfxKeys` (`:2631`), which is exempt and fires its `abilities.json` key.

**The registry has almost nothing for the mage.** `Assets/Resources/Data/Canonical/motion-castings.json`
→ `targets.mage` declares **exactly ONE row**: keyword `cast`, `vfxKey: "firespell_Cast"`,
and **no `vfxImpact` and no `sfxImpact` on it** (its own `_comment` says so:
*"Unwired hooks awaiting owner tags: vfxProjectile (travel), vfxImpact (landing), sfxImpact"*).
`HeroAbilities` resolves rows through `ActionBundleCatalog.TryGetRow`
(`ActionBundleCatalog.cs:98`) — **EXACT target, NO `inherits` walk** — and `targets.humanoid`
carries only a `_comment` anyway. So for the mage there is **no `skill1`, no `skill2`, no
`castHeal` row at all**.

`weaponskill-animations.json:75-76` (`class: mage, skill: "Lightining", slot: w`, clip
*Standing 2H Magic Area Attack 01*, `_note` "Frost Nova / Lightning (mage W aoe)") is an
**ANIMATION-CLIP** casting row. It carries **no `vfxKey` field** and is a different file from
`motion-castings.json`, so it has never supplied a lightning EFFECT to anything. The clip note is
the only place "Lightning" existed in the mage kit before this WO.

### 1.2 The three abilities, before this change

| Ability (id) | `castAnim` → keyword | `vfxCast` (before) | Did the cast beat play? | `vfxImpact` (before) | Did the landing play? |
|---|---|---|---|---|---|
| Arcane Bolt — **two ids**, `mage.arcane-bolt` (`abilities.json:529`) and `universal.arcane-bolt` (`:795`) | none → effect `strike` → `AnimKeyForEffect` returns `skill1` (`HeroAbilities.cs:2712-2713`) | `SimpleCast_Cast` | **NO.** Not in `OwnerPickedVfxKeys`, so suppressed; then `TryGetRow("mage","skill1")` **MISSES** → `FlowTrace.Once("Vfx","no-row-skill1", "...silent by design")` (`HeroAbilities.cs:2836-2838`) | `FireImpact_Impact` | **NO.** Registry-only mode never reads `def.VfxImpact` at all (see §2.1); `_currentCastKeyword="skill1"` has no row → silent |
| Void Rift (`mage.void-rift`, `:565`) | `voidrift` → unmapped → effect `aoe` → `skill2` | `RangedSpell-Powerful(Longcast)_Cast` | **YES** — it IS in `OwnerPickedVfxKeys`, fires at the caster | *(absent)* | **NO** — no key, and no registry row either |
| Wither (`mage.wither`, `:687`) | `wither` → unmapped → effect `dot` → `cast` | `PosionCloud_Cast` | **YES** — in `OwnerPickedVfxKeys`; the owner-picked branch returns before the registry `cast` row, so the fireball marquee does **not** double-fire on Wither | *(absent)* | **NO** — the mage `cast` row carries no `vfxImpact` |

**Not silent, and worth naming:** `SpellVfxFactory.PlayCast(def.EffectEnum, _heroClass, def.UnityColor, origin)`
fires unconditionally one line above `PlayCastVfxKey` (`HeroAbilities.cs:1144-1145`, WO-875), so
every cast still gets the semantic element flash. Arcane Bolt today = **element flash + engine
projectile only**. That is exactly the "not decent yet" she is describing.

**Answer to "which hook key does Arcane Bolt's cast and impact resolve through today":**
its cast resolves through `motion-castings` keyword **`skill1`**, which has **NO mage row** →
nothing plays; its impact resolves through the same keyword's `vfxImpact` → **also nothing**.
Its `abilities.json` keys `SimpleCast_Cast` / `FireImpact_Impact` were **dead data**, never
reaching a prefab.

---

## 2. WHAT THIS WO CHANGES

### 2.1 The one code seam: the IMPACT beat had no owner-tag exemption

`PlayImpactVfxKey` (`HeroAbilities.cs:2874`) under `RegistryOnlyMotionVfx` read the landing key
**only** from the motion-castings row for the current cast keyword. The cast beat (`:2822`) and
the residual loop (`:2978`) each already carried the `IsOwnerPickedVfxKey` exemption; the impact
beat did not. So **no amount of tagging in `abilities.json` could ever make a mage landing play** —
the tag would sit in data and resolve to nothing, the silent-failure shape §12 forbids.

Added: the same exemption, in the same shape as its two siblings.

- **Placed AFTER the `sfxImpact` audio block and BEFORE the bundle-row `vfxImpact` read.** An
  early return at the top of the method would have silenced the registry landing SOUND for every
  tagged ability — a regression bought for a feature.
- Reuses the registry branch's **yaw-toward-travel rotation** (WO-678 item 4), so Electro splash
  and BigExplosion land oriented along the cast like every other impact, never at identity and
  never pitched into the ground.
- Traces `FlowTrace.Step("Vfx", "owner-picked impact vfx '<key>' for '<id>' at <pos> yaw=<y>deg ...")`.

No hardcoded prefab path anywhere: the code names only a **key**, and the key→prefab resolution
stays in `HovlVfxCatalog` / the pick-options rail.

**Each of the three abilities reaches that branch — the enclosing method was read, not inferred:**

| Ability | Effect shape | Where its landing is played |
|---|---|---|
| Arcane Bolt | `strike` | `ResolveStrikeLike` (declared `HeroAbilities.cs:1466`) → `PlayImpactVfxKey(hitDef, hitFoe.WorldPosition)` at `:1607`, inside the projectile-arrival closure |
| Void Rift | `aoe` | `ResolveEffect` (declared `:1284`), `case AbilityEffect.Aoe:` → `PlayImpactVfxKey(def, centre)` at `:1404`, where `centre = ResolveBlastCentre(atk, origin)` — **the blast centre**, so BigExplosion lands where the rift opens |
| Wither | `dot` | `ResolveDot` (declared `:2180`) → `PlayImpactVfxKey(hitDef, hitFoe.WorldPosition)` at `:2202` and `PlayResidualLoop(hitDef, <foe transform>, burnSecs, ...)` at `:2208` — the residual is parented to the FOE, which is what makes the status marker an on-target marker |

(Wither keeps `vfxImpact` empty, so its `:2202` call is a deliberate no-op; only `:2208` draws.)

### 2.2 The tag → hook wiring (data only)

Four keys added to `HeroAbilities.OwnerPickedVfxKeys` with full provenance
(`HeroAbilities.cs`, in the 2026-09-16 block): `Lightningspellmaybe_Cast`,
`lighteningOnSpellLand_Impact`, `Explosion_Impact`, `Sleep_Impact`. Without that set membership
the registry-only gate suppresses them, so this is the enabling half, not a second source of truth.

### 2.3 Why Wither's marker is a RESIDUAL, not an impact

`Sleep_Impact`'s baked row is **`IsLoop: 1`** (`HovlVfxCatalog.asset:1073-1079`; source
`VfxManualPicks.json` `isLoop:true`). A loop-flagged row played on the fire-and-forget impact path
registers **no reclaim deadline** and leaks one of the 20 loop slots for the rest of the session —
the precise leak `VfxLoopFlagRegression` documents from six F8 captures. `PlayResidualLoop` is the
**only** beat that stops a loop on a deadline (`StopHandleAfter(seconds)`, `HeroAbilities.cs:3010`),
and `ResolveDot` calls it with `burnSecs` = `dotSeconds` = 4 (`:2208`). So the on-target status
marker rides the burn window and ends with it. **Wither's `vfxImpact` stays empty** — that is the
brief's "leave the tick silent otherwise", answered by the loop flag rather than by taste.

### 2.4 Both Arcane Bolt ids are tagged — deliberate, and stated so she can overrule

There are **two** Arcane Bolts in `abilities.json`: `mage.arcane-bolt` (mage-skills pool, and
`MageSpellKitAuthoringRegression.cs:437` records it as having **no granting talent node** — a
pre-existing gap) and `universal.arcane-bolt` (universal-skills, granted by `hero-talents.json:1675`
and referenced by `troop-upgrades.json:331`). `ConceptIconIdentityRegression.cs:86` already
declares them *"same named spell 'Arcane Bolt' under a class-scoped and a shared-pool id"*.
**I could not prove which id the tester's HUD bar held.** Both were therefore tagged identically,
so the lightning shows whichever one she has equipped. They are one spell to the player, so one
look is correct; if she wants them to differ, that is one line each.

---

## 3. THE MAPPING TABLE — key → prefab → hook (her retag surface)

Every `key` below is an owner row in `Assets/Editor/VfxManualPicks.json` (`manual: true`) and
resolves to a non-null `Prefab` in the baked `Assets/Resources/VFX/HovlVfxCatalog.asset`
(each row opened and the prefab GUID confirmed present, 2026-09-16).

| Ability (id) | Hook field | Owner key | Prefab (path) | Shape / motion (no hue) | Catalog `IsLoop` |
|---|---|---|---|---|---|
| Arcane Bolt (`mage.arcane-bolt`, `universal.arcane-bolt`) | `vfxCast` | `Lightningspellmaybe_Cast` | `Assets/Hovl Studio/AOE Magic spells Vol.1/Prefabs/Lightning strike.prefab` | A single hard bolt that arrives top-down onto a point, with a brief ground flash and upward sparks; one snap, no lingering body | 0 (`:568`) |
| Arcane Bolt (both ids) | `vfxImpact` | `lighteningOnSpellLand_Impact` | `Assets/Hovl Studio/RPG VFX Bundle/Random effect prefabs/Electro splash.prefab` | A short crackling splash that spreads outward flat from the hit point and dies in well under a second | 0 (`:561`) |
| Void Rift (`mage.void-rift`) | `vfxCast` — **UNCHANGED** | `RangedSpell-Powerful(Longcast)_Cast` | `Assets/Hovl Studio/AAA Projectiles Vol 1/Prefabs/Projectiles 2D/2D Projectile 17 nova violet.prefab` | A billowing nova that swells at the caster | 0 |
| Void Rift | `vfxImpact` | `Explosion_Impact` | `Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/BigExplosion.prefab` | A heavy volumetric burst at the blast centre that expands, rolls upward and settles — reads as the rift collapsing | 0 (`:365`) |
| Wither (`mage.wither`) | `vfxCast` — **UNCHANGED** | `PosionCloud_Cast` | `Assets/Hovl Studio/AAA Projectiles Vol 1/Prefabs/Flash and hits/Hit 24 green explosion.prefab` | A compact puff at the caster | 0 |
| Wither | `vfxResidual` (per-duration, see §2.3) | `Sleep_Impact` | `Assets/Lana Studio/Casual RPG VFX/Prefabs/States/Character_status_sleep.prefab` | A slow repeating status marker that hovers above the afflicted target and pulses while it lasts | **1** (`:1079`) — loop, deadline-stopped at `dotSeconds` = 4s |

**One property of the shared rail she should know before retagging:** these are catalog KEYS, not
per-ability copies, so a `realm.vfx.<key>` override from the Command Center moves **every** hook on
that key at once. Checked 2026-09-16: `Explosion_Impact` has **no other code consumer**
(`grep -rn "Explosion_Impact" Assets/_Modules Assets/Editor` returns only this WO's own two lines
in `HeroAbilities.cs`), so today each of the four keys drives exactly one hook.

### 3.1 ⛔ TWO ROWS ARE BLOCKED, NOT DECLINED — and they need her word, not a code change

The lead's mapping also asked for **Void Rift cast → `SpecialAbilityMage_Cast`** and
**Wither cast → `Posion_Cast`**. **Both were held**, because each collides with an owner ruling
that is already encoded in a gate — wiring either would fail `REGRESSION_OK`:

1. **`SpecialAbilityMage_Cast` already belongs to `mage.cataclysm`.**
   `MageSpellIdentityRegression.cs:21` pins `("mage.cataclysm", "cataclysm", "SpecialAbilityMage_Cast")`
   and `:48` fails on a **duplicate `vfxCast`** across the mage kit; `:19` separately pins
   void-rift's cast as `RangedSpell-Powerful(Longcast)_Cast`. Two failures from one edit.
2. **`Posion_Cast` already belongs to `mage.poison`** (the stock R).
   `MageSpellIdentityRegression.cs:17` pins it there and `MageSpellKitAuthoringRegression.cs:231`
   pins it again with the note that her spelling must not be "corrected";
   `OverTimeEffectRegression.cs` Case 8 additionally requires
   *"mage.wither must retain the owner's 2026-09-09 distinct cast pick PosionCloud_Cast"*.

The ruling behind both is hers, recorded verbatim in `HeroAbilities.cs:2633-2635`:
**"Owner 2026-09-09: learned Mage spells must not collapse onto one cast look."** Re-pointing
either cast would put two mage spells on one look — the exact thing that ruling forbids — so the
honest move was to keep the distinct casts she already has and spend her two new tags on the
**landings**, which were silent. **All six of her tags are used; only two hooks moved.**

**If she wants those cast looks moved anyway**, it is a one-word ruling and a mechanical edit:
move the KEY in `abilities.json` and update the two pinned rows in
`MageSpellIdentityRegression.Expected` in the same commit, giving the displaced ability
(`mage.cataclysm` / `mage.poison`) a different tag of hers so nothing collapses.

---

## 4. §12 instrumentation — what already exists, and what was added

**Already there, and it is the one that proves the swap:** `VFXManager.PlayKey` traces
every successful play as `FlowTrace.Throttle("VFXManager", "hovl-play:<key>", 1f,
"PlayKey('<key>') -> prefab '<row.Prefab.name>'")`
(`Assets/_Modules/Village/Vfx/VFXManager.Hovl.cs:412-413`) — key **and resolved prefab name**,
throttled ~1/sec. A second trace at `:530` (`hovl-at:<key>`) names the world position, parent,
follow target and lifetime. **No duplicate instrument was added** — duplicating it on a per-cast
path would flood the ring buffer and evict the boot window (memory
`logcat-ring-buffer-destroys-evidence`).

**Added:** the missing per-hook line on the new branch — `FlowTrace.Step("Vfx", "owner-picked
impact vfx '<key>' for '<id>' at <pos> yaw=<deg>deg ...")` — so the device log pairs
*which ability asked* with *which prefab drew*. Its siblings on cast (`:2827`) and residual
(`:2984`) already existed.

**What to grep on the device capture (acceptance evidence):**
```
[Flow:Vfx] owner-picked cast vfx 'Lightningspellmaybe_Cast' for 'universal.arcane-bolt'
[Flow:VFXManager] PlayKey('Lightningspellmaybe_Cast') -> prefab 'Lightning strike'
[Flow:Vfx] owner-picked impact vfx 'lighteningOnSpellLand_Impact' for 'universal.arcane-bolt'
[Flow:VFXManager] PlayKey('lighteningOnSpellLand_Impact') -> prefab 'Electro splash'
```
Absence of the `no-row-skill1` "silent by design" line on an Arcane Bolt cast is the negative
control that the exemption took effect.

---

## 5. Regression reasoning — run mentally against the change, with citations

| Suite | What it lints | Verdict |
|---|---|---|
`vfx-loop-flag` (`VfxLoopFlagRegression.cs`) | stored `IsLoop` == derived-from-prefab emission, for every `HovlVfxCatalog` / `VFXCatalog` row; plus the mirror join | **GREEN — no catalog row, prefab or flag was touched.** The change *respects* its charter: the one loop-flagged key (`Sleep_Impact`) went to the only deadline-stopped slot (§2.3) |
`vfx-null-slot` (`VfxParticleNullSlotRegression.cs`) | enabled all-null-material particle renderers on catalogued prefabs | **GREEN — no prefab and no catalog exposure changed;** the four keys were already catalogued rows before this WO |
`elite-vfx-wire` (`EliteVfxWiringRegression.cs`) | literal `AddComponent<EliteVFXController>` in `Assets/_Modules/Village/**`, an `OnEliteAttack` caller outside its own file, `ArmForTier` public-instance by reflection, `DragonBoss` → `VFXType.Boss_Spawn` | **GREEN — none of those four symbols appears in the diff** |
`realm-vfx` / VFX pick override (`VfxPickOverrideRegression.cs`) | the two `vfx-pick-options.generated.json` copies byte-identical; every option id resolvable in `HovlVfxCatalog`; `realm.vfx.*` override applies; loop/burst mismatch refused | **GREEN — neither generated copy nor `VfxManualPicks.json` was edited.** Her six keys keep their existing stable option ids (e.g. `Lightningspellmaybe_Cast` = id 62, `lighteningOnSpellLand_Impact` = id 61), so **every wired hook stays retargetable from the Command Center** with no rebuild |
`MageSpellIdentityRegression` | per-id `castAnim` + `vfxCast`, **no duplicates**, and the `PlayCastVfxKey(def, origin, animVariant);` source lint | **GREEN — no `vfxCast` in its 13-row `Expected` table changed** (neither Arcane Bolt id is in it), no new duplicate introduced, and the linted line is untouched. This is also the suite that BLOCKS §3.1 |
`MageSpellKitAuthoringRegression` | `RequireUntagged` on `mage.poison` / `mage.drain` / `mage.thunder` VFX stages | **GREEN — none of those three abilities was touched** |
`OverTimeEffectRegression` Case 8 `[owner-tag]` | `mage.wither` retains `PosionCloud_Cast`; every other stage on the new abilities stays EMPTY | **UPDATED IN THIS SAME CHANGE, as the suite's own failure message instructs** — the `Sleep_Impact` residual is now *pinned* (so removing her tag fails) and exempted from the empty rule. `vfxProjectile` / `vfxImpact` on Wither remain empty and still linted; `knight.ironblood` untouched |
`banned-vfx` (`BannedVfxRegression.cs`) | code/catalog references to `Spell_Fire_6`, `Magic circle sun loop` | **GREEN — neither basename appears in the diff** |
`HeroKitMirrorRegression`, `MageAbilityIconRegression`, `ConceptIconIdentityRegression` | class-card slot/name mirror; icon coverage per id | **GREEN — no name, slot, id or icon changed** |

---

## 6. Files touched

| File | Change |
|---|---|
`Assets/_Modules/Village/Hero/HeroAbilities.cs` | four owner keys added to `OwnerPickedVfxKeys` with provenance; owner-tag exemption branch added inside `PlayImpactVfxKey`'s registry-only path (after the sfx block) |
`Assets/Resources/Data/Canonical/abilities.json` | `mage.arcane-bolt` + `universal.arcane-bolt`: `vfxCast`/`vfxImpact` retagged. `mage.void-rift`: `vfxImpact` added. `mage.wither`: `vfxResidual` added |
`Assets/StreamingAssets/Data/Canonical/abilities.json` | **byte-identical twin** of the above (the two copies must stay equal — `OverTimeEffectRegression.cs:446`) |
`Assets/Editor/Regression/OverTimeEffectRegression.cs` | Case 8 records + pins the `mage.wither` → `Sleep_Impact` tag |

## 7. What NOT to touch (honoured)

Hero rig / animator / `HeroAnimatorFactory` · ability damage, cooldown, mana, range, `dotDamage`,
`dotSeconds` (**no number changed anywhere**) · any `.unity` scene · `VfxManualPicks.json` ·
`vfx-pick-options.generated.json` (either copy) · `HovlVfxCatalog.asset` · the fireball
(`firespell_Cast`, `mage.fireball`, `motion-castings.json` `targets.mage.cast`) ·
`CLI_LANES_WO_NUMBERS.md`.

## 8. Acceptance

- [ ] `COMPILE_GATE_OK` — lead runs (this lane fires no Unity gate)
- [x] `python tools/gate_brace.py` clean on both `.cs` (`GATE_BRACE_SUMMARY bad=0 of 2`), 0 NUL bytes, no CRLF introduced
- [x] Both `abilities.json` copies parse as JSON, `cmp`-identical, LF 848 → 850, 0 CRLF
- [ ] `REGRESSION_OK <n>/<n> suites` — lead runs; §5 is the reasoning, not the proof
- [ ] **Device VFX capture of Arcane Bolt** with the §4 grep lines present — owner felt-pass, **PO closes**
- [ ] The two **held** cast rows (§3.1) ruled on by the owner

