# WO-1717 RESULT — **SECTION 6C ONLY** (structure damage feedback)

**⛔ THIS RESULT IS SCOPED TO SECTION 6C AND NOTHING ELSE.** WO-1717 carries three
findings and recommends three file-disjoint follow-ups. Only **6C (numbers / sound /
punch / HP-bar latency)** is implemented here.

- **6A — the breach VERB** (tap a wall section to focus the warband): **NOT STARTED, still
  open.** It is a NEW FEATURE and section 6A lists three design questions the PO has not
  ruled on. Nothing in this change touches `RaidDeployController`, `TroopRally`,
  `RaidAssaultAi` or `TroopController`.
- **6B — rally-on-a-wall never arrives**: **NOT STARTED, still open**, and correctly so —
  6B's own gate is "capture before code" and no capture has been taken.
- **FINDING 1** (wall damage maths): no change. The RCA proved it correct; §7 puts
  `WallSegment.ApplyDamage` off-limits and it was not opened for edit.

**Lane:** edit-only implementation lane, branch `dev`. **No Unity run, no gate, no build,
no commit** — the lead gates once over the combined tree.

---

## 1. What changed, file:line

### The seam — `Assets/_Modules/Village/Vfx/StructureHitReaction.cs`
| line | change |
|---|---|
| `:57-95` | header block: why the number is post-tier-divide *by construction*, and the accumulate-never-discard rule |
| `:133` | `MinNumberInterval = 0.35f` — the number cadence (see §3) |
| `:140` | `MinShownDamage = 0.5f` — below this a number would print `0`, so it is held, not printed |
| `:143-161` | `public struct HitSample` — `DropFraction` / `Damage` / `At` / `Credible` |
| `:164-171` | `_maxHp`, `_nextNumberAt`, `_pendingDrop`, `_pendingNumberDrop` |
| `:200-215` | `Attach(...)` gains `Func<float> maxHp` as the **LAST optional** parameter (so `HeartAuraController`'s positional call still binds) |
| `:232-290` | **`TrySample(hpNow, time, out HitSample)`** — the decision half, split out of `Update` with an injected clock so the oracle can drive it with no camera, no VFX pool, no audio device and no PlayMode |
| `:297-312` | `BoundsCentre()` — unchanged logic, lifted out of `Update` |
| `:313-370` | `Update` is now presentation only: dust → number → sound → trace |
| `:350` | `DamageNumberSpawner.Spawn(hit.Damage, hit.At)` — **the new caller. Signature read at `DamageNumberSpawner.cs:88`: `Spawn(float amount, Vector3 worldPos)`.** Not guessed. |
| `:356-357` | `AudioService.Instance?.PlaySfxAtPosition(SfxId.StructureImpact, hit.At)` |

### The sound
- `Assets/_Modules/Audio/SfxId.cs:54` — new `StructureImpact` member, inside the "Impact
  sounds" block as the ticket specifies.
- `Assets/_Modules/Audio/ProceduralSfx.cs:91-92` — synth recipe (short dry low thud).
- `Assets/Editor/Audio/SfxClipLibraryBuilder.cs:128` — the identical recipe, so the baked
  `.wav` and the runtime synth are the same placeholder rather than two different ones.

### The HP-bar latency + max-HP plumbing — `Assets/_Modules/Village/Vfx/StructureDamageVisuals.cs`
| line | change |
|---|---|
| `:755-758` | `RegisterRepairables<WallSegment>("wall", _ => WallSegment.MaxHp)`, `<Building>(… b.MaxHp)`, `<Gate>(… g.MaxHp)` |
| `:773-816` | tower / defensetower / arcanetower / harvestsite `Register` calls gain `() => tt.MaxHp` |
| `:764-772` | collector registration deliberately passes **no** max HP (see §5) |
| `:829` | `RegisterRepairables<T>` gains a `Func<T,float> maxHpOf` selector |
| `:844` | `Register(...)` gains `Func<float> maxHp = null` |
| `:909-910` | `StructureHitReaction.Attach(host, hp, name, () => AttachBar(rec, "first-hit"), maxHp)` |
| `:913-934` | new `AttachBar(rec, via)` — the **single** `FloatingHealthBar.Attach` call site |
| `:1021` | `Evaluate`'s own attach re-pointed at `AttachBar(rec, "eval-poll")` |

### The Heart — `Assets/_Modules/Village/Heart/HeartAuraController.cs:349-354`
Passes `() => FullHp`, so the thing the game is named for gets the number channel too.

### Regression
- **NEW** `Assets/Editor/Regression/StructureFeedbackRegression.cs` — markers
  `STRUCTURE_FEEDBACK_OK` / `STRUCTURE_FEEDBACK_FAIL`, 8 cases.
- Registered in `Assets/Editor/Regression/DataRegression.cs` — **exactly ONE line added,
  at line 413** (`[structure-feedback]`, immediately after `[removal-husk]`). That file
  carries other lanes' uncommitted edits; nothing else in it was touched.

**No gameplay class was edited.** `WallSegment.cs`, `Building.cs`, `Tower.cs`, `Gate.cs`
and `RaidSpire.cs` are untouched — that was the whole point of using this seam.

---

## 2. Acceptance criteria, one by one

1. **Number on every structure hit, existing pool.** ✅ `StructureHitReaction.cs:350`.
   `DamageNumberSpawner.Spawn(float, Vector3)` — signature read at source
   (`DamageNumberSpawner.cs:88`), not guessed. No second pool; oracle case 8 fails if
   `TextMesh` or `new GameObject(` ever appears in the hit reaction.
2. **The value is post-tier-divide.** ✅ **and it is post-divide by construction, which is
   stronger than forwarding the right variable.** This component never sees the damage
   *request*; it sees the HP *fraction* move. `WallSegment.ApplyDamage` adds
   `effective = amount / ToughnessFor(t)` (`WallSegment.cs:334`) — after the Faction-gated
   BULWARK reduction (`:343-344`) — to the 0-100 `_damage` track, `MaxHp` is a `const 100`
   (`:109`), and `RepairTarget.DamageFraction` is exactly `_wall.Damage / 100f`
   (`RepairTarget.cs:141`, opened this session). So `drop × maxHp == effective`, exactly,
   and the raw request is not reachable from here even by mistake. **No reach beyond
   `StructureHitReaction` was needed** — only a max-HP delegate, supplied by the existing
   registration choke point. Gate and Building wrap their own `HpFraction`; same identity.
   Oracle case 2 pins it with a real tier-3 archer hit (29 raw → **11.33** shown) and fails
   if the number ever equals 29.
3. **Rate-limited.** ✅ See §3 for the chosen values and why.
4. **Masonry SfxId, same beat, colour-free.** ✅ See §4 — **and it needs a real clip.**
5. **HP bar on the FIRST hit.** ✅ See §6 for which fix was chosen and why.

---

## 3. The rate limit — chosen values, and why

**Dust / flinch: `MinBurstInterval = 0.15 s` — UNCHANGED.** It already shipped and it is
the MOTION channel; one puff per blow is the read.

**Numbers: `MinNumberInterval = 0.35 s` — NEW, deliberately slower.**
`DamageNumberSpawner.Lifetime` is `0.55 s` (read at `DamageNumberSpawner.cs:46`). At
0.35 s at most **two** numbers are alive over one structure at once, which is legible. At
the dust's 0.15 s it would be four, which is the overlapping wall of text the criterion
forbids. The number floats `RiseDistance = 1.0` world units, so two in flight are visually
separated.

**The half that matters more than the cap: the cap NEVER DISCARDS DAMAGE.** The old code
wrote `_last = now` *before* the cooldown return, so anything landing inside a window was
gone. A naive number on that path would under-report a six-troop warband — a throttle on
the tell silently becoming a lie about the total. Damage now accumulates into
`_pendingNumberDrop` and flushes into the next number, so a warband produces **fewer,
larger, still-honest** numbers. Oracle case 4 pins it: four blows of 12+8+8+4 print `12`
then `20`, and it fails if the flush ever prints only the last blow.

**A sum still under 0.5 points is held rather than printed**, because
`DamageNumberSpawner` renders a rounded integer and `0` reads as a miss.

---

## 4. ⚠ THE SOUND IS A PLACEHOLDER — an artist/owner pick is still owed

`SfxId.StructureImpact` (`SfxId.cs:54`) has **no authored clip**. It degrades exactly the
way this project's convention says (verified at source, not assumed):
`AudioService.PlaySfxAtPosition` (`AudioService.cs:635-673`) resolves the library, finds
nothing, and falls through to `ProceduralSfx.For(id)` — which is **never a silent no-op**
(`AudioService.cs:663-676` says so in its own words). I added a recipe
(`ProceduralSfx.cs:91`) rather than letting it hit the `default` generic tick, so it reads
as stone and not as a UI blip, and I mirrored it into `SfxClipLibraryBuilder.cs:128`.

**It is a placeholder thud and it should not ship as the final sound.** A real clip dropped
at the audio key `Sfx/Sfx_StructureImpact` overrides it with **no code change**.

**Enum-ordinal safety, proven rather than assumed** (inserting mid-enum shifts every later
value if any ordinal is serialized): the script GUID from
`SfxClipLibrary.cs.meta` is `4be1efe504ead184ea71cb8b2101b940`; grepping it across
`Assets/` + `ProjectSettings/` returns **only the `.meta` file itself**, and
`Assets/_Modules/Audio/Resources/Audio/SfxClipLibrary.asset` **does not exist** (the folder
does not exist). No `SfxId` ordinal is persisted anywhere, so the in-block insertion the
ticket asked for is safe. Clip overrides are keyed by NAME (`Sfx_<Id>`), not ordinal.

**Colourblind constraint honoured:** MOTION (dust) + SOUND (this) + NUMBER. No tint carries
meaning anywhere in the change; remove any one channel and the blow still reads.

---

## 5. ⚠ Two things the lead should know before gating

**a. Collectors get dust + sound but NO number.** `ResourceCollector` exposes `HpFraction`
but no public max HP, and every runtime caller of `Configure` takes the component's own
`DefaultMaxHp = 120` rather than the catalog row (`ResourceCollectorBootstrap.cs:171`,
`StructureFactory.cs:1245`) — so reading `BuildingCatalog` here would print a number that
does not match the HP that actually moved. A wrong number is worse than none. **The fix is
one read-only getter on `ResourceCollector`, which this slice is forbidden from adding
(no gameplay-class edits).** Reasoned in-code at `StructureDamageVisuals.cs:764-772`.

**b. Scope this seam genuinely has, which is wider than the owner's words.**
`StructureHitReaction` is faction-blind by design, so **enemy contact hits on the player's
OWN walls during a siege will also number**, not only hero/troop hits on raid walls.
Distinguishing them is impossible from here without a gameplay-class edit. I did **not**
gate it — a defender seeing numbers on their own wall under attack is arguably the same
"tell me something is happening" the owner asked for — but it is a deliberate choice, not
an oversight, and the lead should not be surprised by a wave lighting up the town.

---

## 6. The HP-bar latency fix — which one, and why

**Chosen: attach from the hit flinch** (`StructureDamageVisuals.cs:909-910` →
`AttachBar`), **not** pre-registration at scene load.

Why, read at source rather than assumed: `OnEnable` sets `_scanTimer = 0f`
(`StructureDamageVisuals.cs:671`, *"scan immediately on install"*), so the 2.0 s `Scan`
already runs on the install frame — **pre-registering would remove a delay that mostly is
not there, would not touch the 0.3 s `Evaluate` half at all, and would attach bars across a
whole pristine town**. The flinch, by contrast, fires the frame the HP moves, so the bar
now appears on the **first hit, same frame**, for structures already registered and for
ones registered later alike. It is also the smaller diff: one callback argument plus a
helper.

Both paths now go through **one** `AttachBar` — `FloatingHealthBar.Attach(` appears exactly
once in the file (oracle case 8 fails at 0 or 2), so the argument lists cannot drift.
`AttachBar` refuses a **broken** host, so the collapsing blow cannot bond a bar to a ruin
that the next `Evaluate` would tear down. `Evaluate`'s attach survives as the catch-all for
damage too small to trip the flinch's `MinDropFraction`.

---

## 7. Regression — `Assets/Editor/Regression/StructureFeedbackRegression.cs`

Deterministic: fixture `GameObject`s + an **injected clock**. No scene load, no PlayMode, no
camera, no audio device, no VFX pool — which is exactly why `TrySample` was split out of
`Update`.

| case | asserts |
|---|---|
| 1 | the baseline sample is never a hit (a scene load must not rain numbers) |
| 2 | **the number is the POST-tier-divide points** — 29 raw on a tier-3 wall shows 11.33; fails if it ever shows 29 |
| 3 | rate-limited: a second blow 0.02 s later fires no burst, and no second NUMBER inside 0.35 s |
| 4 | **the throttle HOLDS damage, never discards it** — 12+8+8+4 prints 12 then 20 |
| 5 | a `> MaxCredibleDrop` save-restore prints no number and plays no sound |
| 6 | a structure with no knowable max HP still flinches and invents no number |
| 7 | the host flinch fires **once, on the hit sample** — the hook the first-hit bar hangs off |
| 8 | source contract: one pool, `SfxId.StructureImpact` declared **and** played, dust still there, bar attached from the flinch, one `FloatingHealthBar.Attach` |

Case 8 is a source lint because **`DeNelle.EditorRegression` does not reference
`DeNelle.Audio`** (read at `DeNelle.EditorRegression.asmdef`), so `SfxId` is not a type this
assembly can name. **Comments and string literals are stripped before matching** — every
file involved argues about this ticket in prose, and an unstripped match would keep passing
after the code was deleted.

---

## 8. Proof, and what is NOT proven

```
$ python tools/gate_brace.py <8 touched .cs files>
GATE_BRACE_SUMMARY bad=0 of 8
GATE_BRACE_EXIT=0

$ NUL scan (embedded 0x00) over the same 8 files
NUL_SCAN bad=0 of 8
```

### ⛔ NOT PROVEN — the headless screenshot capture was NOT run by this lane

Per §11B, named as unproven rather than ticked:

- **No Unity run happened.** This lane's brief is explicitly *"do NOT run Unity, gate,
  build, or commit — the lead gates once."* `RunCaptureHeadless` is a Unity batchmode entry
  point, so the instruction to produce PNG proof and the instruction not to run Unity are in
  direct conflict; I resolved toward the binding constraint and did not run it.
  **There are no PNG paths to list, and I have opened none.**
- **I did not add a capture entry point either.** `Assets/Editor/UICaptureLaunch.cs` is a
  ~10,000-line file that other lanes are editing this session; adding an entry point to it
  from a parallel lane is the §9 serialization-bottleneck hazard for no benefit, since this
  lane cannot execute it anyway.
- **Nothing here is compile-verified.** Brace + NUL are clean; `COMPILE_GATE_OK` and
  `REGRESSION_OK` have not been run and are the lead's gate.

**For the lead, at the single gate:** the deterministic proof is
`REGRESSION_OK` carrying `[structure-feedback] 8/8 cases`. The *visual* proof needs eyes on
a live structure taking damage — the existing `UICaptureLaunch.RunPopulatedRaidHudCapture`
is the nearest candidate surface, **but I have not read it far enough to assert it renders a
damaged wall**, so treat that as a suggestion, not a finding. Felt-verification (does the
number read at arm's length on the Seeker?) is the PO's per §13 and cannot be judged
headless.

---

## 8b. Four things the lead should check at the gate

**a. NO DOUBLE SOUND — verified, not assumed.** `VFXManager.Play` auto-fires
`VfxToSfx(type)` unless `playSound:false`, so a mapped dust type would now play two sounds
per hit. I read the **whole** `VfxToSfx` switch (`VFXManager.cs:446-487`; `Env_DestructionDust`
returns zero grep hits in that file):
`Env_DestructionDust` **is not in it** and falls to `_ => SfxId.None`. The dust has always
been silent; the direct `AudioService` call is the only sound on this path. No
`playSound:false` needed.

**b. Working-tree diff sizes, so foreign hunks are visible.** `git diff --numstat` for the
touched files (added/removed vs HEAD):

| +/- | file |
|---|---|
| 213 / 35 | `Assets/_Modules/Village/Vfx/StructureHitReaction.cs` |
| 83 / 20 | `Assets/_Modules/Village/Vfx/StructureDamageVisuals.cs` |
| 13 / 0 | `Assets/_Modules/Audio/SfxId.cs` |
| 7 / 0 | `Assets/_Modules/Audio/ProceduralSfx.cs` |
| 6 / 1 | `Assets/_Modules/Village/Heart/HeartAuraController.cs` |
| 4 / 0 | `Assets/Editor/Audio/SfxClipLibraryBuilder.cs` |
| **3 / 0** | `Assets/Editor/Regression/DataRegression.cs` — **I added exactly ONE line (`:413`). The other two are another lane's uncommitted edits; they are not mine.** |
| new file | `Assets/Editor/Regression/StructureFeedbackRegression.cs` |

**c. Three files beyond the two the brief named.** The brief named
`StructureHitReaction.cs` and `SfxId.cs` (+ `SfxClipLibrary`). I also touched
`ProceduralSfx.cs` and `SfxClipLibraryBuilder.cs` (the project's actual
unmapped-id convention — §4) and `HeartAuraController.cs` (one argument, so the Heart is
not the single structure left without a number). `StructureDamageVisuals.cs` is the
registration choke point the ticket's own 6C text points at. Calling the expansion out
explicitly rather than burying it in the table.

**d. One guard that does not mean what it looks like.** In
`RegisterRepairables<T>` the lambda's `ss != null` on a generic `T : Component` compiles as
a plain **reference** comparison, not Unity's overloaded `Object.operator!=` — so a
DESTROYED component passes it. Harmless here (it only then reads a `MaxHp` field on a
managed object that still exists, and the delegate is nulled with the record), but it is
the one line in this lane where the guard is weaker than it reads.

**e. §15 canon:** `docs/MASTER_CATALOG/` does not yet know `StructureHitReaction.TrySample`
/ `HitSample` exist. **Not updated by this lane** — flagging it as the lead's call rather
than skipping §15 silently.

---

## 9. Board

Per the ticket's own recommendation the WO covers three separable pieces, so its
`**Status:**` line has **NOT** been flipped to Fixed/Implemented. A scoped addendum naming
6C as implemented — and 6A/6B as still open — was added to the ticket's status line instead.

- Ticket: `WorkOrders/WORK_ORDER_1717_wall_breach_targeting_and_structure_damage_feedback.md`
- This result: `WorkOrders/WORK_ORDER_1717_wall_breach_targeting_and_structure_damage_feedback.RESULT.md`
