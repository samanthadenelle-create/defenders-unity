# WORK ORDER 1781 — The Mage kit is **silent**: no ability of any class can carry an SFX, because `abilities.json` has no sfx field

**Status:** NEEDS DATA

Why NEEDS DATA: the seam fix is clear from source (§4.1), but the **sound choices are the owner's** (memory `vfx-map-owner-tags-no-creative-pick`: CLI maps key -> hook verbatim and holds un-tagged hooks). The blank tag table in §4.2 is what must come back before anything is audible.

**Minted:** 2026-09-16 by the raid-polish audit lane (number PRE-ASSIGNED from the block 1777-1790; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)

**Silo:** ability audio — `Assets/_Modules/Village/Hero/AbilityCatalog.cs`, `Assets/Resources/Data/Canonical/abilities.json` + its StreamingAssets twin, `Assets/Resources/Data/Canonical/motion-castings.json`. **No VFX code (WO-1776 lane), no combat maths, no `.unity`.**

**Build under test:** `2026.09.16.371701`, built 2026-09-15 22:06 from `f6653501f`.

**Video path:** the hero is the Mage in every gameplay beat (owner ruling 2026-09-16). **The video's protagonist casts in silence.** P0.

---

## 1. SYMPTOM

The Mage's Fireball, Void Rift, Wither and Arcane Bolt make **no sound** — no cast, no landing. The same is true of every other class.

## 2. EVIDENCE — read at source 2026-09-16

⚠ **`Assets/Resources/Data/Canonical/abilities.json` and its StreamingAssets twin are MODIFIED-UNCOMMITTED in the working tree** (`git status --short`); the readings below are working-tree values, not what shipped.

1. **The data has no field for it.** `grep -ci 'sfx'` over `abilities.json` returns **0**.
2. **The parser has no field for it.** `grep -n 'sfx\|Sfx' Assets/_Modules/Village/Hero/AbilityCatalog.cs` returns **nothing** — the catalog parses only `vfxCast`, `vfxProjectile`, `vfxImpact`, `vfxResidual`.
3. **The one place that could carry it is empty, and says so.** `Assets/Resources/Data/Canonical/motion-castings.json` → `targets.mage` holds **exactly one row** (`cast`) whose `"sfxId"` is `""`, with its own `_comment`: *"Unwired hooks awaiting owner tags: vfxProjectile (travel), vfxImpact (landing), sfxImpact (landing sound)"*.

**Conclusion, stated at the width of the evidence: this is not a missing clip or a broken mixer — the ability-to-sound seam does not exist.**

**For completeness, the Mage's VFX state after WO-1776** (`WORK_ORDER_1776_...md:3` = `IMPLEMENTED, NOT YET GATED`, so **not in the build under test**): stock four `mage.fireball` (`abilities.json:17`), `mage.shell` (`:35`), `mage.drain` (`:53`), `mage.poison` (`:70`); pool includes `mage.void-rift` (`:565`), `mage.wither` (`:688`), `mage.arcane-bolt` (`:529`), `mage.thunder` (`:617`), `mage.cataclysm` (`:600`), `mage.blink` (`:584`), `mage.siphon` (`:670`), `mage.manaweave` (`:547`). **Still carrying no `vfxImpact` at all:** `mage.poison`, `mage.manaweave`, `mage.blink`, `mage.cataclysm`, `mage.thunder`, `mage.siphon`, `mage.drain`, `mage.wither`, `mage.shell`. ⛔ Those VFX gaps belong to WO-1776's lane, **not this one** — recorded here only so they are not lost.

## 3. SCOPE RULING NEEDED BEFORE CODE

Two shapes are possible and they are not equivalent. **Ask before choosing:**

- **(a)** add `sfxCast` / `sfxImpact` / `sfxResidual` beside the existing `vfx*` keys in `abilities.json`, parsed by `AbilityCatalog` — the ability owns its sound, symmetric with its VFX; or
- **(b)** extend `motion-castings.json` so each class gets a row per ability and the existing `sfxId` slot is filled — the motion registry owns it, and the `_comment` above suggests that was the intent.

## 4. THE WORK

**4.1 The seam (clear from source).** Whichever shape is ruled, the path is: parse the key → resolve through `CoreServices.Audio` with `?.` (CLAUDE.md §10) → fire on the same beat the matching `vfx*` key fires on, so sound and picture cannot drift. Instrument the miss: a named sfx key that resolves to nothing must `FlowTrace.Warn`, never fail silently (§12).

**4.2 The tag table (owner's, blank on purpose).** One row per Mage ability, cast and landing:

| ability | cast sound | landing sound |
|---|---|---|
| `mage.fireball` | | |
| `mage.void-rift` | | |
| `mage.wither` | | |
| `mage.arcane-bolt` | | |
| `mage.shell` | | |
| `mage.drain` | | |

⛔ **Do not pick these.** Hold every un-tagged hook.

## 5. ACCEPTANCE

- A fresh device capture of one raid in which every Mage cast emits an `[Flow:Audio]`/`[Flow:Sfx]` line naming the resolved clip, and `grep -c` for the "no clip" warn is **0** for tagged abilities.
- The owner confirms by ear on the filming build — a log line is not proof a sound is audible.
- `python tools/gate_brace.py` exit 0, then `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs.
- ⚠ Any `abilities.json` edit is a **canonical JSON** edit: patch from HEAD bytes and prove the LF count (memory `canonical-json-edits-binary-only-verify-newlines`); the `Assets/StreamingAssets` twin must be updated in the same change.

## 6. DO NOT TOUCH

WO-1776's VFX mappings and the two rows it holds pending an owner word. `MageSpellIdentityRegression.cs` pins (`:17`, `:21`, `:48`). Combat maths. Any `.unity` file.
