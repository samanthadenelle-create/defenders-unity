# WORK ORDER 1776 — RESULT

**Status:** IMPLEMENTED, NOT YET GATED
**Date:** 2026-09-16
**Lane:** VFX / hero abilities (data + one hook seam)
**Gate:** NOT run by this lane (no Unity fired, nothing committed, no banner touched)

---

## 1. What shipped

The ruled half — **the owner's lightning on Arcane Bolt** — plus the two landing beats her other
tags cover. Six owner tags used, two hooks moved, **no number and no prefab path in code**.

| Ability (id) | Hook | Owner key | Resolved prefab |
|---|---|---|---|
| Arcane Bolt — `mage.arcane-bolt` **and** `universal.arcane-bolt` | `vfxCast` (was `SimpleCast_Cast`, **dead**) | `Lightningspellmaybe_Cast` | `AOE Magic spells Vol.1/Lightning strike.prefab` |
| Arcane Bolt — both ids | `vfxImpact` (was `FireImpact_Impact`, **dead**) | `lighteningOnSpellLand_Impact` | `RPG VFX Bundle/Electro splash.prefab` |
| Void Rift — `mage.void-rift` | `vfxImpact` (was **absent**) | `Explosion_Impact` | `ParticlePack/BigExplosion.prefab` |
| Wither — `mage.wither` | `vfxResidual` (was **absent**) | `Sleep_Impact` | `Lana Studio/States/Character_status_sleep.prefab` |

Void Rift's and Wither's **cast** looks are **unchanged** — see §3.

## 2. The finding that made this a code change and not a data edit

`PlayImpactVfxKey` (`HeroAbilities.cs:2874`) under `RegistryOnlyMotionVfx = true` read the
landing key **only** from the motion-castings row for the current cast keyword. The cast beat and
the residual loop each already carried the `IsOwnerPickedVfxKey` exemption; **the impact beat did
not**, so an owner-tagged `vfxImpact` in `abilities.json` could never reach a prefab.

Compounding it, proven at source: `motion-castings.json` `targets.mage` declares **one** row
(`cast` → `firespell_Cast`) with **no `vfxImpact`**, and `HeroAbilities` looks rows up through
`ActionBundleCatalog.TryGetRow` — **exact target, no `inherits` walk**. So **every mage landing in
the shipped game was silent by construction**, and Arcane Bolt's cast was silent too
(`skill1` has no mage row → the `no-row-skill1` "silent by design" trace). Arcane Bolt was
element flash + engine projectile only. That is the *"only the fireball is decent"* she reported.

The exemption branch was added **after** the `sfxImpact` audio block (an early return at the top
would have silenced the registry landing sound for every tagged ability) and reuses the registry
branch's yaw-toward-travel rotation (WO-678) so the two new landings are oriented, not identity.

## 3. Two rows HELD — blocked by her own ruling, awaiting one word

The lead's mapping also asked for Void Rift cast → `SpecialAbilityMage_Cast` and Wither cast →
`Posion_Cast`. **Both would fail the gate**, because each is already another mage spell's look:

- `SpecialAbilityMage_Cast` is pinned to `mage.cataclysm` (`MageSpellIdentityRegression.cs:21`),
  and that suite fails on a duplicate `vfxCast` (`:48`); it also pins void-rift's current cast (`:19`).
- `Posion_Cast` is pinned to `mage.poison` (`:17`, and `MageSpellKitAuthoringRegression.cs:231`),
  and `OverTimeEffectRegression` Case 8 separately requires Wither to retain `PosionCloud_Cast`.

The ruling behind all of it is hers, recorded verbatim at `HeroAbilities.cs:2633-2635`:
**"learned Mage spells must not collapse onto one cast look" (2026-09-09).** So her two new tags
were spent on the **landings**, which were silent, rather than on displacing casts that already
read distinctly. Moving them anyway is mechanical once she rules — WO §3.1 states the exact edit.

## 3b. Each ability's landing beat was read to its enclosing method, not inferred

`ResolveStrikeLike` (declared `:1466`) plays Arcane Bolt's landing at `:1607`; `ResolveEffect`
(`:1284`) `case AbilityEffect.Aoe:` plays Void Rift's at `:1404` on `centre = ResolveBlastCentre(...)`
— the blast centre; `ResolveDot` (`:2180`) plays Wither's residual at `:2208` parented to the FOE's
transform for `burnSecs`. So all three tags reach a live call site.

## 4. Verification actually performed by this lane

- `python tools/gate_brace.py Assets/_Modules/Village/Hero/HeroAbilities.cs Assets/Editor/Regression/OverTimeEffectRegression.cs`
  → **`GATE_BRACE_SUMMARY bad=0 of 2`, exit 0**
- Raw brace counts balanced (385/385 and 44/44); **0 NUL bytes**; **0 CRLF** in either `.cs`
- Both `abilities.json` copies: `json.load` **VALID**, `cmp` **IDENTICAL**, LF **848 → 850**, 0 CRLF,
  len 45150 → 45274 (edits applied as **binary** patches, per memory
  `canonical-json-edits-binary-only-verify-newlines`)
- All four keys confirmed as `manual: true` rows in `VfxManualPicks.json` **and** as rows with a
  non-null `Prefab` GUID in the baked `HovlVfxCatalog.asset` (`:568`, `:561`, `:365`, `:1079`)
- `Sleep_Impact` confirmed **`IsLoop: 1`** → routed to `vfxResidual` (deadline-stopped at
  `dotSeconds` = 4s via `StopHandleAfter`), **not** to the fire-and-forget impact path, which would
  leak one of the 20 loop slots per cast
- `MarqueeSpellVfx.Keys` contains **only `firespell_Cast`** → no new marquee, so Arcane Bolt's
  engine projectile is **not** suppressed
- Repo-wide grep of `arcane-bolt|void-rift|wither|SimpleCast_Cast|FireImpact_Impact|PlayImpactVfxKey|OwnerPicked`
  across `Assets/Editor/Regression`, `Assets/Tests`, `Assets/Data/Tests`: **no suite pins either
  Arcane Bolt's VFX keys**, and no suite source-lints `PlayImpactVfxKey`

## 5. NOT proven by this lane — stated as unproven, per §11B

1. **`COMPILE_GATE_OK` and `REGRESSION_OK`.** No Unity was fired (by instruction). §5 of the WO is
   a mental run against each suite's linted facts with citations — **reasoning, not a marker**.
2. **That the effects look right in motion.** Nobody has seen these four prefabs play on these
   hooks. Shapes were described from prefab names and pack roles; the device capture is the proof,
   and she is the judge (she is colourblind, so no hue was described or chosen).
3. **Which Arcane Bolt id the tester's HUD held.** Unproven — `mage.arcane-bolt` has no granting
   talent node while `universal.arcane-bolt` is granted by `hero-talents.json:1675`, which points
   at the universal id, but I did not capture her bar. **Both ids were tagged identically** so the
   lightning shows either way. If she wants them to differ, that is one line each.
4. **Whether `Lightning strike` reads better on the cast or the landing.** The mapping was followed
   verbatim (cast = Lightning strike at the caster, landing = Electro splash at the target). Worth
   flagging for her eye: *Lightning strike* is a **strike-from-above onto a point**, so it may read
   better at the TARGET with Electro splash on the cast — swapping the two key strings in
   `abilities.json` is the whole change. **Not done unprompted: that would be a CLI creative pick.**
5. **Whether `mage.arcane-bolt` is reachable at all.** `MageSpellKitAuthoringRegression.cs:437`
   records it as having no granting node ("pre-existing gap"). Not in this WO's scope; flagged.

## 6. Files touched

- `Assets/_Modules/Village/Hero/HeroAbilities.cs`
- `Assets/Resources/Data/Canonical/abilities.json`
- `Assets/StreamingAssets/Data/Canonical/abilities.json`
- `Assets/Editor/Regression/OverTimeEffectRegression.cs`
- `WorkOrders/WORK_ORDER_1776_mage_kit_vfx_lightning_on_arcane_bolt.md` (+ this RESULT)

Untouched, as ordered: hero rig / animator, every balance number, all `.unity` scenes,
`VfxManualPicks.json`, both `vfx-pick-options.generated.json` copies, `HovlVfxCatalog.asset`,
the fireball, `CLI_LANES_WO_NUMBERS.md`. Nothing committed; no board regenerated by this lane.

## 7. Next

Lead: batch-gate (`COMPILE_GATE_OK` + `REGRESSION_OK`), commit by explicit path with the WO
Status flip, then device capture of Arcane Bolt for her felt-pass. **PO closes.** The two held
cast rows (§3) need one owner word, not a ticket.
