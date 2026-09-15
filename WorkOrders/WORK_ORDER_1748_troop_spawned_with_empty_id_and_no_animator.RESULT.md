# WO-1748 RESULT — the empty id was the INSTRUMENT, and the catapult was a FALSE ALARM

**Status of this document:** a CLAIM by the implementing lane. Nothing here was gated, baked,
built or committed — the lane was explicitly barred from Unity, the gate and git, and a build
chain was running over the same working tree throughout. The lead re-gates.

**Lane:** edit-only, 2026-09-15. **Branch:** dev.

---

## 1. The WO's central inference was FALSE — and that is the finding

The WO (and the ticket that minted it) reasoned from `id=` being empty that the spawn was NOT one
of the deployed ten — "a raid guard, a rally reinforcement, or a prop skinned as a troop".

That is wrong, and it is provable from two lines read at source this session:

- `Assets/_Modules/Village/Troops/TroopController.cs:423` — `_troopId = def.Id;` sits inside
  `Configure(TroopDef, Vector3)`.
- `Assets/_Modules/Village/Troops/TroopFactory.cs` — `go.AddComponent<TroopController>()` is
  followed on the **very next line** by `troop.Configure(def, pos)`.

`AddComponent` runs `Awake` **synchronously**. So every diagnostic `Awake` emitted printed one
line BEFORE `_troopId` was assigned. **`id=` has been structurally empty on every troop this game
has ever spawned.** It narrowed nothing. The instrument lied.

The caller was never a mystery either — the capture's own stack names it verbatim:

```
DeNelle.Village.TroopController:Awake()
UnityEngine.GameObject:AddComponent()
DeNelle.Village.TroopFactory:Build(TroopDef, Vector3, Quaternion, Transform)
DeNelle.Village.TroopDeployer:SpawnTroop(String, Vector3)
DeNelle.Village.TroopDeployer:SpawnFromArmy(PlayerTroop, Vector3, Int32, Single)
DeNelle.Village.RaidDeployController:DeployAll()
```

That is the ordinary player deploy path. Ask (2) — enumerate every caller that can reach the
factory with a null/empty id — answered by grep over `Assets/**/*.cs`:

| Reaches `TroopFactory.Build` | Site | Can it carry an empty id? |
|---|---|---|
| `TroopDeployer.SpawnTroop` | `TroopDeployer.cs:51` — **the only `TroopFactory.Build(` call site in `Assets/**/*.cs`** | **No.** `TroopCatalog.Find(troopId)` returns null → `TroopDeployer.cs:39-40` logs and returns **before** `Build`. `def` is never null at `Build`. |
| `TroopDeployer.SpawnFromArmy` | `TroopDeployer.cs:88` → `SpawnTroop` | No — same guard. |
| `RaidDeployController.DeployAll` | `RaidDeployController.cs:814` | No. |
| `RaidDeployController` (garrison/seat muster) | `RaidDeployController.cs:2583` | No. |
| `RaidAssaultTraceCapture` (editor harness) | `Assets/Editor/Regression/RaidAssaultTraceCapture.cs:65` | No. |
| `CatapultProofCapture` (editor tool) | `Assets/Editor/TroopTools/CatapultProofCapture.cs:31-32` | No. |

**`TroopFactory.Build` has exactly ONE runtime caller chain and it is guarded.** There is no raid
guard, no rally reinforcement and no prop path spawning through the troop factory. The only
remaining way a `_troopId` could be empty post-`Configure` is an empty `id` in `troops.json` —
now pinned (Case 7 part F).

## 2. What actually fired — PROVEN, statically

`troop-catapult` was in the owner's warband (the WO lists it). Chain, every link opened this session:

1. `Assets/Resources/Data/Canonical/troops.json:141-148` — `troop-catapult` has `"role": "siege"`
   and `"model": "Structures/Catapult"`.
2. `TroopFactory.Build` — `isSiege` is true for `role == "siege"`, and the animator bind reads
   `if (vis != null && !isSiege) ApplyTroopAnimator(...)`. **Siege never gets an animator bound.**
3. `Assets/AddressableAssetsData/AssetGroups/Structure_Art.asset:64-65` (working tree) — address
   `Structures/Catapult` → GUID `33e8158e1b664284f8914c043af78cd5`.
4. That GUID resolves (via `git grep` over `*.meta`) to
   `Assets/StructureContent/Synty/Catapult.prefab`.
5. `grep -c "Animator" Assets/StructureContent/Synty/Catapult.prefab` → **0**.

**Provenance caveat, closed.** `Structure_Art.asset` is ` M` in `git status`, so steps 3-4 above
read the DIRTY working tree, not what the `2026.09.15.371127` tester build shipped. Checked at
HEAD: **HEAD declares `Structures/Catapult` TWICE, with two different GUIDs** —
`33e8158e1b664284f8914c043af78cd5` (→ `Assets/StructureContent/Synty/Catapult.prefab`) and
`7f7d4fe86bedeb54a9c82b36e3632c25` (→ `Assets/StructureContent/Catapult.prefab`). The working
tree has de-duplicated it down to the first. **`grep -c "Animator"` returns 0 for BOTH prefabs**,
so the proof holds at HEAD and in the working tree, whichever entry wins. (Step 2 — siege skips
`ApplyTroopAnimator` unconditionally — already carries the verdict independently of which prefab
loads, or of whether it loads at all.)

So a catapult reaches `Awake` with `GetComponentInChildren<Animator>() == null` **on every single
deploy**, and trips the unconditional `FlowTrace.Fail` written for humanoid rigs — with no id on
it. The body was working exactly as authored.

**Verdict: no game defect in the captured line. Two instrument defects — unattributable, and a
false alarm on a by-design animator-less body.** The fix is entirely instrumentation plus the
siege carve-out. Ask (3) was therefore NOT exercised as a gameplay fix: the evidence named an
instrument, so no bind-order or model-path code was changed.

**Not proven** (stated as unproven, per §11B): whether a *second*, non-siege troop was ALSO
animator-less in those raids. The old line could not distinguish them, and I ran no headless
repro (barred from Unity). Eight captures carry this message (seq 5057, 5062, 5066, 5109, 5118,
5123, 5128, 5258) and all eight have the identical stack and scene; I cannot tell from the inbox
how many fired per raid. **The relocated, named trace will settle it on the next device run** —
a non-siege troop will now print its id, role and resolved address.

---

## 3. Files touched

All line numbers below were re-read at source with `grep -n` after the final edit — not estimated.

### `Assets/_Modules/Village/Troops/TroopFactory.cs` (silo)
- **:145-155** (message at **:153**) — the missing-mesh fallback branch: bare `Debug.LogWarning` → `FlowTrace.Fail`
  naming `id` and the resolved address. A `LogWarning` is invisible to the F8 harness, which is
  why the one line that names WHICH model failed never reached a capture. The identical reasoning
  is already recorded in this file for the off-mesh branch (`TroopFactory.cs:60-75`) — cited in
  the new comment.
- **:196-218** (message at **:213-218**) — NEW spawn identity line, emitted **immediately before**
  `AddComponent<TroopController>()`: id, role, siege flag, resolved address, whether the body is
  `skinned` / `siege-proxy-fallback` / `capsule-fallback`, and the animator state
  (`NONE` / `present-but-unbound` / `bound:<name>`).
  - **DEVIATION FROM THE WO, declared:** the WO asked for `FlowTrace.Once`. `Once` fires for the
    first troop only, so it would name one troop per session and hide the other nine. Used a
    per-spawn `FlowTrace.Step`, matching the existing per-spawn Step at `TroopFactory.cs:113`
    and `TroopDeployer.cs:47`.
  - No `System.Diagnostics.StackTrace` was added: `FlowTrace.Fail` → `Debug.LogError`, and Unity
    already attaches the managed stack (that is how the capture named `DeployAll`). The missing
    piece was the id and the address, not the caller.

### `Assets/_Modules/Village/Troops/TroopController.cs` — ⚠ WO-1746's SILO, DISCLOSED
The brief required this to be called out explicitly. **The fix cannot live anywhere else:** the
id does not exist until `Configure`, and only `TroopController` can report a verdict it cached.
The edit is confined to the diagnostic and touches no WO-1746 behaviour.
- **:575-585** — the four-branch verdict block **removed from `Awake`** and replaced with a
  comment recording why. **The `_has*` parameter caching STAYS in `Awake` untouched** — that is
  the bind-order law the file's own header states and `RuntimeSpawnVisualRegression` Case 5 pins.
  Only *when the verdict is spoken* moved; *what is measured* is byte-identical.
- **:484-486** — `ReportVisualVerdict()` called at the end of `Configure`, after `_troopId`,
  `_troopRole` and `_preferStructures` are real.
- **:488-556** (declared at **:508**) — NEW `private void ReportVisualVerdict()`. Same four branches, now carrying
  `id=` and `role=`, plus a **SIEGE branch that returns early with `Step`, not `Fail`**, citing
  the Catapult.prefab evidence above in its doc comment.
- No other member, field or behaviour in the file was touched. `RaidAssaultAi.cs` was **not**
  touched.

### `Assets/Editor/Regression/RuntimeSpawnVisualRegression.cs` (suite `[runtime-spawn-visual]`)
Chosen over `AddressableTroopVisualRegression` because it is the suite that already owns the
troop animator contract (Cases 4-6) and already reads `troops.json` as data.
- **:114** — `Case(failures, "troop-identity", () => Case7_SpawnIsAttributable(failures, notes));`
- **:124-132** — OK-reason extended.
- **:538-736** (declared at **:566**) — NEW `Case7_SpawnIsAttributable`, six pins:
  - **A** the factory emits an identity line naming the address, and it precedes `AddComponent`;
  - **B** the missing-mesh branch reports through FlowTrace and has not reverted to `Debug.LogWarning`;
  - **C** the verdict is NOT inside `Awake`'s body — **and** the parameter caching still IS;
  - **D** `Configure` calls `ReportVisualVerdict` **after** assigning `_troopId`;
  - **E** `ReportVisualVerdict` has a SIEGE branch and it precedes the no-animator `Fail`;
  - **F** DATA: no `troops.json` entry has an empty `id` or an empty `model` (the one remaining
    route to an unattributable troop), and a note fires if the siege carve-out covers nothing.

**⛔ Deviation from the WO's acceptance wording, declared.** The WO asked to pin *"every troop
root carries an Animator"*. Pinning that **literally would FAIL on `troop-catapult`** — siege is
animator-less by design. Case 7 pins the honest invariant instead: **every troop is
attributable** (non-empty id, named address, verdict spoken where the id exists) and the
animator Fail is correctly scoped to non-siege.

### `WorkOrders/WORK_ORDER_1748_troop_spawned_with_empty_id_and_no_animator.md`
- `**Status:**` flipped `READY TO IMPLEMENT` → `IMPLEMENTED, NOT YET GATED`.

---

## 4. Lane checks (run; results claimed)

```
python tools/gate_brace.py Assets/Editor/Regression/RuntimeSpawnVisualRegression.cs \
                           Assets/_Modules/Village/Troops/TroopFactory.cs \
                           Assets/_Modules/Village/Troops/TroopController.cs
GATE_BRACE_SUMMARY bad=0 of 3      (exit 0)
```

NUL bytes: **0** in all three. Raw brace counts also balanced
(RuntimeSpawnVisual 70/70, TroopFactory 93/93, TroopController 248/248) — so the raw one-liner and
the gate's own state machine agree, which matters here because both new FlowTrace messages nest
quotes inside interpolation holes (CLAUDE.md §1).

## 5. Edge cases checked and closed (no code change needed)

- **A `TroopController` that is never `Configure`d** would now emit NO verdict, where before it
  emitted one with an empty id. Proven a non-case: `TroopController.cs.meta` guid
  `609c5607bea946c46a735eb7b4059db7` returns **zero hits** across every `*.unity` and `*.prefab`
  in the repo (`git grep -l`), so no scene or prefab carries the component. And
  `TroopFactory.cs:220` is the **only** `.Configure(` call site under `Assets/_Modules/Village/Troops`,
  so every live troop is Configured exactly once — the verdict prints once per spawn, never twice.
- **`_preferStructures` as the siege test.** WO-1746 just landed a persistent auto-chaining Breach
  stance, which would make a runtime-mutable flag a latent lie in the new comment. Checked:
  `_preferStructures` is assigned at **exactly one line**, `TroopController.cs:432`, from
  `def.Role`. It is set once at `Configure` and never written again, so `// role == "siege"` is
  accurate and the branch cannot be flipped by a stance change.

## 6. For the lead

- **No DataRegression.cs line is needed.** Case 7 was added INSIDE
  `RuntimeSpawnVisualRegression`, already registered at `Assets/Editor/Regression/DataRegression.cs:743`.
  Nothing to register; marker stays `RUNTIME_SPAWN_VISUAL_OK`.
- No Unity, gate, bake, build or git was run by this lane. A build chain was running over this
  working tree the whole time, so **its compile result does not cover these edits.**
- `grep` confirmed no other oracle pins the moved strings (`"NO Animator anywhere"`,
  `"runtimeAnimatorController at"`) — nothing else breaks on the relocation.
- **Acceptance cannot be closed by this lane.** The WO's acceptance is a fresh headless
  IronBastion run showing zero `NO Animator` and zero empty ids. After the gate, the expected
  result is: **zero empty ids** (structural, guaranteed), and the catapult's line downgraded from
  `Fail` to `Step`. If a `Fail` still appears, it will now name the troop and its address — which
  is the point of the change, and that would be a genuine second defect worth its own ticket.

### Two things found OUT OF SILO — the lead decides whether either earns a ticket
Neither was touched. Both are in Addressables/content, not in this lane's files.

1. **`Structures/Catapult` is declared TWICE at HEAD** in
   `Assets/AddressableAssetsData/AssetGroups/Structure_Art.asset`, with two different GUIDs
   (`33e8158e…` → `Assets/StructureContent/Synty/Catapult.prefab`, `7f7d4fe8…` →
   `Assets/StructureContent/Catapult.prefab`). Which one an address resolves to when duplicated is
   not something I proved. The **uncommitted** working-tree copy of that asset has already
   de-duplicated it to the first — so whatever is in the tree right now silently changes which
   prefab ships, and that asset is ` M` in `git status`, i.e. it will go out with the next build.
2. **That entry carries `m_SerializedLabels: []`** while its neighbours (e.g.
   `Structures/OwnerStorage/Iron_Pallet`) carry the `Structure_Art` label. **Unproven, and I did
   not chase it**: if bundle grouping or the content warmer selects by label, the catapult address
   may have no bundle on device — which would land every catapult on the siege-proxy fallback
   every raid. That is a §16 content-ship question, and it is cheap for the lead to settle.
