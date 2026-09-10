# WORK ORDER 1680 — HEART-007: Heart / Tree-of-Life world integration and resonance visuals

**Status:** SPEC
**Silo:** World visuals + VFX on the Heart. Touches `CastleHubBuilder` (scene BUILDER, never the `.unity` file). No backend.
**Raised by:** HEARTBOUND-TRIAGE lane, 2026-09-10.
**Number:** PRE-ASSIGNED by the coordinator. `CLI_LANES_WO_NUMBERS.md` deliberately **NOT** edited by this lane.
**Spec section:** `docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:726-806` (HEART-007).
**Tree:** worktree at `dev` **`abbeb9362`**.

---

## 0. Classification — **GREENFIELD. There is no progression-driven visual system on the Heart at all.**

The spec assumes existing tree stages to map onto: *"Use the existing Tree progression assets wherever possible… Map ten resonance tiers into approximately six base Tree visual states"* (`:741-746`).

⛔ **Those assets do not exist.** Searches run and their results:
- `grep -rniE "heartStage|heartTier|treeStage|BloomStage|visualStage|StageIndex" --include=*.cs Assets/_Modules Assets/Editor` → only `HeartLevelDef` / `HeartLevelBundle`, which are **progression NUMBERS with no visual binding**.
- `grep -rniE "TreeOfLife_Visual|treeVisual" --include=*.cs Assets/_Modules` → `HeartAuraController.cs:579,582` (a VFX audit) and `HubAmbientVfxInjector.cs:122` (an ambient anchor). **Neither reads a level.**

What *does* exist on the Heart is **two visual state machines, both driven by something other than progression**, and a Heartbound ladder would be a **third axis on the same object**:
1. **Threat-driven** — `HeartController.cs:44-63`, `enum HeartState { Serene, Vigilant, Warning, Danger, Critical, Boss, Victorious }`, with `HeartStateVisual { Color, Emissive, PulseHz, PulseDepth, HaloOpacity }` at `:69-80`.
2. **HP-driven** — `Assets/_Modules/Village/Heart/HeartAuraController.cs:1-33`, an always-on, read-only presentation aura.

⛔ **A resonance axis must not fight the threat axis.** During a siege the Heart goes Critical; a Tier X resonance glow that overrides that reading breaks a combat tell the player relies on. Precedence is a design decision — see Q-AXIS.

And `heart-progression.json`'s own `_authoringNotes` already record the emptiness honestly: *"no reward grant path is wired to a Heart level"* and `maxLevel: 3` with `requiresBuildings: []` on every row. The existing Heart *level* ladder has three rungs and no visuals; HEART-007 wants ten tiers and six visual states.

---

## 1. The world object, read at source

- Anchor GameObject: **`HeartOfElarion`** — `Assets/Editor/CastleHubBuilder.cs:2709` (`private const string HeartAnchorName`), created/found `:2724-2725`.
- Scene: **`Assets/Scenes/Main_Castle_Overworld.unity`** — `CastleHubBuilder.cs:524`. (CLAUDE.md §7: this is the home hub; `MainCastle_Hall.unity` is a legacy file and is NOT the hub.)
- The visual child is **force-replaced** every build: `TreeOfLife_Visual` (`:2762-2764`), instantiated from `Assets/Art/Tree_Of_Life.fbx` (`:2780`) at `localScale 7`, `Euler(-90,0,0)`, all colliders stripped (`:2786-2791`), with `TreeOfLifeMaterialFixer` (`:2792`) and `SeatOnGroundOnStart` (`:2793`, `_baseLiftOverride = 0.4f`, PlayerPrefs `"tree.baseLift"`, `:2804-2810`).
- Exactly one `HeartController` is asserted in the scene (`:2820-2823`); orphan cleanup at `:2048-2075`.
- ⚠ `Assets/Prefabs/Environment/TreeOfLife.prefab` **exists on disk but is not what the builder instantiates** — it loads the raw FBX. Editing the prefab would change nothing and is a real trap.

⛔ **CLAUDE.md §3 / memory `owner-prefs-scenes`: never hand-edit a curated `.unity`.** Every change goes through the builder and a bake.

---

## 2. Deliverables

- **D1** — a resonance visual-stage model: tier → stage, plus the differentiators the spec lists at `:748-758` (particle density, Heartfire intensity, root illumination, Echo particles, ambient wisps, ground markings, bloom, crown illumination, occasional Echo silhouettes).
- **D2** — the tier-up moment (`:760-774`): camera acknowledgement, pulse through the roots, VFX intensify, new tier appears, the newly unlocked passive appears, **celebrate once**. `:774`: *"Do not replay a giant celebration every login."*
- **D3** — the Heart Pulse presentation (`:776-799`): Heartfire centre → root illumination → outward pulse → surrounding buildings catch the light → Echo Event reveal. Target **2.5-4 s**, **skippable after first viewing**.
- **D4** — pooled VFX and a measured frame cost (`:801-806`, mobile first: no huge particle counts, no permanent transparent overdraw, no expensive real-time lights, no heavy shader permutations).

⭐ **D4 is a §12 requirement, not a nice-to-have.** Use the 4-arg frame-budget overload — `using var _ = FlowTrace.Measure("Perf", "Heartbound.TreeVfx", 4f, 1f);` (`Assets/_Modules/Core/Diagnostics/FlowTrace.cs:308`) — **never the 3-arg form on a per-frame site**, because that floods the log and evicts the boot window out of the device logcat ring (`FlowTrace.cs:293-300`; memory `logcat-ring-buffer-destroys-evidence`). Shape pinned by `FrameBudgetMeasureRegression`. **Name the dominant cost in ms in the RESULT.**

---

## 3. Acceptance

1. Tier change produces exactly one celebration, and a re-login produces none — proven with two captures, not asserted.
2. Pulse presentation lands in 2.5-4 s and is skippable on the second viewing.
3. Frame cost named in ms from a `FlowTrace.Measure` 4-arg scope on device.
4. **`UI_CAPTURE_OK` with the PNGs OPENED** — memory `screenshots-are-primary-evidence-for-visual-defects`: FlowTrace shows belief, the screenshot shows what the player sees.
5. **Greyscale check passes** — see Q-COLOR.
6. `COMPILE_GATE_OK`, `gate_brace.py` clean, zero NUL bytes. Rebuild via the builder; **no `.unity` hand-edit**.
7. `**Status:**` flipped in this file in the same commit as the work; `.RESULT.md` written; both paths reported.

---

## 4. Dependencies

**Blocked by:** WO-1676 (tiers), WO-1679 (which tiers grant what), WO-1678 (Echo Bloom is an event), and Q-AXIS / Q-COLOR / Q-VFX below.
**Blocks:** WO-1686 (the demo path is this presentation).

---

## 5. ⛔ OWNER QUESTIONS

### Q-AXIS. Resonance vs threat on the same object — which wins, and when?
§0. `HeartState` has seven threat states with authored colour/emissive/pulse (`HeartController.cs:44-80`) and `HeartAuraController` layers an HP tell on top. A ten-tier resonance glow is a third axis. **Options:** (a) resonance is suppressed entirely during combat and only reads in peace; (b) resonance modulates only channels threat does not use (ground markings, root illumination, ambient wisps) so the two never contend; (c) resonance wins. **(b) is the one the spec's own differentiator list (`:748-758`) is already shaped for**, but it is the owner's call because it decides what a Tier X player sees during a siege.

### Q-COLOR. ⛔ The owner is colourblind. A ten-step ladder cannot be signalled by hue.
Memory `owner-colorblind-delegate-visual-creative`: never ask her to pick hues; **the greyscale check is the gate**. `HeartAuraController.cs:16-24` already solves this once for the health tell — a *colour-free* signal using **size, luminance and motion** — and that is the precedent to follow, not an exception to it. **Ruling needed on which non-hue channels carry the tier** (density? luminance? motion rate? silhouette count?), because ten steps is a lot to fit into three channels and the answer shapes the whole ladder.

### Q-VFX. Every VFX prefab here needs an owner tag.
Memory `vfx-map-owner-tags-no-creative-pick`: **the CLI maps key→hook verbatim and holds un-tagged hooks.** One tag already exists and is directly relevant — `Assets/Editor/Regression/VfxMirrorPairSet.cs:102`: *"Owner tag 2026-08-16 verbatim: ParticlePack FireFlies -> 'Tree of Life Aura'."* Everything else HEART-007 asks for (roots, wisps, ground markings, bloom, crown, silhouettes) is **untagged**. Also note memory `sequenced-vfx-special-cases-for-special-events`: a marquee moment gets ordered prefabs, **never a second spawner or pool**.

### Q-NAME. The spec says "Tree of Life". Canon says "the Heart".
`grep -n "Tree of Life"` across the repo returns **only internal/dev usages** — `CityManifest.json:75`, `HovlVfxCatalogGenerator.cs:180`, three regression comments, `RegressionSuite.cs:717,768,778`. The player-facing canon is *"The Heart, an ancient world tree at the centre of your settlement"* (`docs/CREATIVE_CANON_ELARION_2026-09-04.md:28`) and CLAUDE.md §7 names it **Heart of Elarion**. **"Tree of Life" is fine as an internal identifier and must not appear in player-facing copy.** Confirm, so HEART-008's strings are written once.

---

## 6. What NOT to touch

- ⛔ **`Assets/Scenes/Main_Castle_Overworld.unity`.** CLAUDE.md §3 — never hand-edit; rebuild via `CastleHubBuilder`. And never bake with the Unity editor open (project lock).
- ⛔ **`Assets/Prefabs/Environment/TreeOfLife.prefab`.** §1 — the builder does not use it; editing it changes nothing and wastes a cycle.
- ⛔ **`HeartController.HeartState` and its authored visuals** (`:44-80`). That is the combat tell. Q-AXIS may layer beside it; it may not repaint it.
- ⛔ **`HeartAuraController`'s colour-free health tell** (`:16-24`). It exists because the owner is colourblind.
- ⛔ **The `CastleHubBuilder` single-`HeartController` assertion** (`:2820-2823`) and the orphan cleanup (`:2048-2075`). Both exist because duplicates happened.
- ⛔ **A second VFX spawner or pool.** `Assets/_Modules/Cosmetics/CosmeticApplier.cs:19` names the rule by reference: *"ONE OWNER, NOT A SECOND SPAWNER (CLAUDE.md §7, the EchoWorldPresence rule)."*
- ⛔ **`FlowTrace`'s 3-arg `Measure` on a per-frame site.** §2 D4.
- No backend. No `SaveSchema` change.

---

## 7. Evidence index (opened 2026-09-10 at `dev` `abbeb9362`)

`Assets/Editor/CastleHubBuilder.cs:524,2048-2075,2709,2724-2725,2762-2764,2780,2786-2793,2804-2810,2820-2823`
`Assets/_Modules/Village/Heart/HeartController.cs:26-29,44-63,69-80`; `Assets/_Modules/Village/Heart/HeartAuraController.cs:1-33,16-24,579,582`
`Assets/_Modules/Village/HubAmbientVfxInjector.cs:122`
`Assets/_Modules/Core/State/HeartProgressionCatalog.cs:60`; `Assets/Resources/Data/Canonical/heart-progression.json` (`maxLevel: 3`, `requiresBuildings: []`, `_authoringNotes`)
`Assets/_Modules/Core/Diagnostics/FlowTrace.cs:293-300,308`; `Assets/Editor/Regression/FrameBudgetMeasureRegression.cs`
`Assets/Editor/Regression/VfxMirrorPairSet.cs:102`; `Assets/_Modules/Cosmetics/CosmeticApplier.cs:19`
`docs/CREATIVE_CANON_ELARION_2026-09-04.md:28`
Searches proving §0's greenfield claim are quoted inline with their results.
