# WORK ORDER 1345 - The AoE targeting reticle is the Danger Zone marker, and it must scale to the spell

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:10:45, build 2026.09.08.361259). PRIOR STATUS: FIXED 2026-09-03 - shipped in 2026.09.03.353999. The reticle radius is DERIVED from AbilityDef.Range - the same value ResolveEffect hands Blast() - against the prefab measured 2.42m authored ring radius, so the ring cannot drift from the damage. frost-nova 5.20m -> localScale 2.149; meteor 9.00m -> 3.719; ratio 1.731 equals the radius ratio, proportional rather than a lookup table. Her tag scale applies as a multiplier ON TOP (1.0 = no-op). Input transparency proven from YAML (zero colliders; the six Collider string hits are ParticleSystem collision-module fields). Replaced the pre-existing point pointer for BLAST shapes only - strike and snare keep it, one marker per cast. isLoop conflict reported; her tag not edited.
**Silo / Lane:** Combat targeting presentation + VFX wiring of an owner-tagged key
**Type:** EXISTING system, presentation supplied by an owner-tagged effect
**Minted:** 2026-09-03 (CLI) from an owner tag and her direct question.
**Severity:** P2 - an AoE reticle that misreports its footprint is worse than no reticle.

## THE RULE THAT GOVERNS THIS TICKET

⛔ **The owner tags VFX keys in the Caster. The CLI maps key -> named hook VERBATIM and NEVER picks,
substitutes or rescales a prefab.** A suspect or un-tagged hook is HELD, never guessed.
(Memory `vfx-map-owner-tags-no-creative-pick`.) She is **red/green colourblind** - the reticle may never
carry its meaning by hue. It reads as a RING; that shape is the affordance.

## HER TAG, VERBATIM FROM `Assets/Editor/VfxManualPicks.json`

| key | prefabPath | isLoop | scale |
|---|---|---|---|
| `DangercastAOERange_Cast` | `Assets/Hovl Studio/Map track markers VFX/Prefabs/Marker 7 Danger zone Loop.prefab` | **`false`** | **1.0** |

> *"Danger cast AOE Range for casting AoE target, does thios work"*

**Re-read the file before wiring.** It has changed under us repeatedly tonight and the file always wins
over this table.

## THE ANSWER TO HER QUESTION - MEASURED, NOT THEORISED

The prefab was opened and read. Both facts come from its YAML, not from its name:

| measured | value | consequence |
|---|---|---|
| both `ParticleSystem`s | `looping: 1` | it persists natively while the player aims - no re-trigger scaffolding needed |
| `scalingMode` | `0` = **Hierarchy** | `transform.localScale` DOES scale the effect, so ONE prefab serves every spell radius |
| `m_LocalScale` | `1,1,1` on both | authored at unit scale - it is meant to be driven |

**So yes, it works, and it is the correct KIND of asset:** a ground-projected looping ring that scales
with its transform is exactly what an AoE targeting indicator is.

⚠ **`scalingMode: 0` is `Hierarchy`, NOT `Local`.** Do not re-derive that from memory - Unity's enum is
`Hierarchy=0, Local=1, Shape=2`, and getting it backwards would lead you to conclude the ring cannot be
scaled and to build a pointless workaround.

## ⭐ THE TWO REAL RISKS - BOTH ARE IN THE TAG'S NUMBERS, NOT IN THE ART

### RISK 1 - `scale: 1.0` must NOT override the spell's radius

If the wiring applies the tag's `scale` literally and stops there, **every AoE shows an identical
footprint regardless of its actual reach.** That is not a cosmetic miss: **a reticle that lies about
where the damage lands is worse than having none**, because the player aims by it and it teaches them a
false radius.

**The reticle's world radius MUST be driven from the ability's own AoE radius data.** Find where that
radius lives (the ability/spell definition) and drive `localScale` from it. Treat the tag's `scale` as
an authoring MULTIPLIER on top of the data-derived radius, not as the radius.

- ⛔ Do not hardcode a radius. Do not hardcode a per-spell scale table.
- Prove the mapping: a spell with radius R must produce a ring whose visible ground radius is R, and
  two spells with different R must produce visibly different rings. State how you verified the ring's
  authored world radius at scale 1 - **you cannot map data to scale without knowing that number.**

### RISK 2 - `isLoop: false` on a natively looping prefab

Her tag says `false`; the prefab's systems say `looping: 1`. **If the wiring honours `false` by playing
one burst and stopping, the reticle disappears while the player is still aiming.**

**Honour `isLoop: false` as authored** - and make the behaviour correct by driving the reticle's
lifetime from the **aiming window** (shown while targeting, hidden on cast or cancel), which is the
right owner of that lifetime regardless of the flag. Then **report the conflict in one plain sentence**
so she can retag in seconds.

⛔ **Do NOT edit her `isLoop` or `scale` values, and do NOT write to `VfxManualPicks.json` at all.**
Read it freely. *(This is the THIRD loop mismatch of the evening - WO-1343's night-store tag and
WO-1344's FTUE pointer carry the same shape. Report it the same way; do not "fix" any of them.)*

## Behaviour

- Appears when the player begins targeting an AoE ability; follows the target point; hides on cast and
  on cancel. One owner of that lifetime.
- ⛔ **Must not block input** - the player taps THROUGH the reticle to place the cast. A targeting
  indicator that eats the placement tap makes the ability uncastable.
- ⛔ **Do not add a second spawner or a second pool** (CLAUDE.md s7). If a targeting indicator already
  exists in any form, REPLACE its visual - do not run two.
- Report whether an AoE targeting indicator already existed and what it was.

## Instrumentation

`FlowTrace`: key requested, prefab resolved or null, the ability and its radius, the computed
`localScale`, the resolved ground position, and show/hide transitions. **A missing VFX and a subtle VFX
are indistinguishable without this.** ⛔ Never strip FlowTrace (CLAUDE.md s12).

## ⛔ LIVE LANES - stay out

- **WO-1343** (agent live): night-store aura, the `Aura_*` tunable rotation, tree-foot + boss-death
  unbound hooks, `KnightShieldBash_Impact`, the tagger investigation.
- **WO-1344** (agent live): the FTUE pointer replacing the yellow Glow highlight.
  ⚠ **All three of you are wiring owner-tagged VFX keys. Do NOT edit a shared VFX resolver, registry or
  spawner** - if your fix wants to touch one, report the collision to the lead instead of editing it.
- **WO-1342**: `HeroSkillTreePanelMvvm.cs`, `SkillsPanelLayoutRegression.cs`, both `hero-talents.json`
  twins. **WO-1341**: `PlayerDeckWorkspace.cs`, `HudLabelFitRegression.cs`.
- **WO-1340**: `tutorial-steps.json` + the tutorial registry. **WO-1339**: `BOARD.html`,
  `tools/board_build.py`, `tools/owner_validations.py`, `proof/owner-validations.json`.
- **WO-1316**: `tools/web-ship.ps1`, `tools/command-centre.ps1`.
- **WO-1337**: `Enemy.cs`, `BattleArena.cs`, `PanelManager.cs`, `BattleQuiescenceGate.cs`.
- Decimation: `Assets/HeroContent`, hero FBX + `.meta`.

## Constraints

- ⛔ Never hand-edit a `.unity` scene. UXML does not work in builds. ASCII-only in player-facing strings.
- Phone-first landscape; the reticle must not cover or shrink a touch target (>= 112px).
- ⚠ **The Ranger's PRIMARY is the melee sweep; the bow is the slot-Q ability** (CLAUDE.md s7). Do not
  assume which abilities are AoE from class intuition - **read the ability data**.
- Do not run a Unity gate, do not commit, do not build. The lead does all three.

## Acceptance

- [ ] The reticle appears while targeting an AoE ability and hides on cast/cancel.
- [ ] ⭐ **Its ground radius is driven by the ability's own radius data**, proven with two abilities of
      different radii. State the prefab's authored world radius at scale 1 and how you measured it.
- [ ] **Input passes through** - the placement tap reaches the ground. Proven.
- [ ] Whether a targeting indicator already existed, and what happened to it.
- [ ] The `isLoop: false` conflict reported in one sentence; her tag NOT edited.
- [ ] No prefab chosen, substituted or rescaled by the implementer. Say so explicitly.
- [ ] An oracle pins the key -> prefab mapping against `VfxManualPicks.json` and pins that the reticle's
      scale derives from ability radius (so a later refactor cannot silently pin it to a constant).
      **Prove it RED first; report the mutation.**
- [ ] Brace + NUL check per `.cs` file.
- [ ] ⛔ **Owner felt-verifies on device and CLOSES** - specifically that the ring matches where the
      damage actually lands.
