# WORK ORDER 1750 — RESULT

**Status:** IMPLEMENTED, NOT YET GATED
**Lane:** edit-only implementation lane, 2026-09-15. NO Unity, NO gate, NO bake, NO build, NO git were run — the lead holds the Unity lock and is the sole committer. Every claim below is either a source read taken this session (file:line) or is explicitly marked UNPROVEN.

---

## 1. Enumeration — writers of the alive flag, and subscribers to the death event

### `IsAlive` is not a flag. It is derived.
`Assets/_Modules/Village/Hero/HeroHealth.cs:196` — `public bool IsAlive => _hp > 0f;`

So **`IsAlive=false` in the owner's logcat is exactly "HP is at zero"**, and the empty HP bar in `Screenshot_20260915-134632.png` is the same fact reported twice, not two symptoms. There is no separate boolean that can go stale independently of HP. That collapses half of the WO's "What is NOT proven": the flag cannot have flipped without HP reaching zero.

### Every writer of `_hp` (the only thing that can move `IsAlive`), read at source
| file:line | writer | can it reach zero? |
|---|---|---|
| `HeroHealth.cs:191` | `RestoreAfterPractice` — `Clamp(hp, 1f, MaxHp)` | no (floored at 1) |
| `HeroHealth.cs:251` | `Awake` — `_hp = MaxHp` | only if `MaxHp` resolves to 0 |
| `HeroHealth.cs:304` | `Start` — `_hp = MaxHp` | only if `MaxHp` resolves to 0 |
| `HeroHealth.cs:323-324` | `SyncGearHp` — `_hp += delta; _hp = Mathf.Min(_hp, MaxHp)` | **yes, silently**, if the effective max ever resolves to 0 |
| `HeroHealth.cs:849` | `TakeDamage` — `_hp = newHp` | **yes — and this is the ONLY path that also runs the death branch** |
| `HeroHealth.cs:859` | Legendary Resolve cheat-death | no (restores) |
| `HeroHealth.cs:954` | practice-scene defeat — `_hp = 1f` | no |
| `HeroHealth.cs:1517` | `Respawn` | no (restores) |
| `HeroHealth.cs:1846`, `:1882` | heal / regen | no |
| `HeroHealth.cs:1906` | `RestoreToFull` | no |

`MaxHp` is `_maxHp + EffectiveBonus` (`:183`); `_maxHp` is `[SerializeField]` and is **never written at runtime** (grep over the file: only reads at `:174/:179/:183/:308/:328` and in the TakeDamage trace). So a zero max would have to come from a negative `EffectiveBonus`. That is a narrow, unlikely, but *reachable* seam, and it is the shape nothing in the game would report — which is why it is now instrumented rather than argued about.

### The death path (`_hp <= 0f && !_isDead` -> `BeginDeathSequence`, `HeroHealth.cs:876` -> `:923-1039`, post-edit numbering)
Sets `_isDead`, logs the `[Flow:Death] lethal hit` dump with both listener lists (and, new in this change, `lethalFrom=` at `:845`), opens the DeathTrace window, plays the death anim, calls `EnterDeathFreeze()`, fires `OnDeath` then `OnDied`, and starts `HandleDeath()`.

### Every subscriber to the death events, repo-wide (grep `\.OnDeath *+=|\.OnDied *+=`)
| file:line | subscriber | what it does |
|---|---|---|
| `Assets/_Modules/Village/Hero/HeroHitReaction.cs:104` | `HandleDied` | flash + slow-mo beat. **Does not re-enable anything** (read at source). |
| `Assets/_Modules/Village/Heart/GameOverScreen.cs:230` | `ShowHeroFell` | hub-only; stands down for arena and overworld. **Does not re-enable anything.** |
| `Assets/_Modules/Village/UI/EndState/HeroDeathEndState.cs:88` | `OnHeroDeath` | end-state sting; **stands down entirely inside a live raid** (WO-1526 branch, `HeroDeathEndState.cs:~135`). |
| `Assets/_Modules/Village/Waves/WaveManager.cs:3275` | `HandleTelemetryHeroDied` | telemetry latch only. |
| `Assets/Editor/OwnedTownMovePlayProof.cs:1143` | editor proof harness | not shipped. |

### ⭐ Does the raid have a death handler? **YES. It is not an event subscriber — it is a branch inside `HandleDeath`.**
`HeroHealth.cs:1170`:
- `:1270` — `if (raidScorer != null) raidScorer.NotifyHeroDied();` — latched on **every** path before any branch (the 2-star cap).
- `:1294` — `liveRaidContinues = raidInProgress && raidScorer != null && !raidSettled && !RaidScoring.RaidDeathEndsRaid`.
- Inside that branch (`:1306`): `liveRaidDeploy.NotifyHeroDown()` → `RaidDeployController.cs:1415-1436`, which latches on the scorer again (idempotent) and sets the on-screen status line `EndStateVM.HeroDownArmyFightsOn`, then `yield break` — **the hero stays down and the raid continues** (owner ruling WO-1526).

**There is no design gap and no question for the owner.** The raid's rule is written, cited and live. The ticket's `Ask 2` ("if none exists, say so") does not apply.

### Does the town handler survive the scene change?
Yes — it is not a handler that can be lost. The raid branch lives inside `HeroHealth.HandleDeath` itself, and the hero (with its `HeroHealth`) is the object carried across per WO-1109. `HeroDeathEndState` re-hooks on `sceneLoaded` (`:67-70`) and late-resolves in `Update` (`:73-81`); `HeroHealth.OnDestroy` clears `Instance` only when it is still itself (`:279`).

---

## 2. What the evidence PROVES, and what it does not

### PROVEN by source read, this session — a real hole, and it is mobile-only

The death path turns the hero's input surfaces off **by component**:
- `HeroHealth.HandleDeath:1173-1174` — `_locomotion.enabled = false; _abilities.enabled = false;`
- `HeroHealth.EnterDeathFreeze:1754` — `if (_pac == null) _pac = GetComponent<PlayerAttackController>(); if (_pac != null) _pac.enabled = false;`

**`enabled = false` suppresses Unity's callbacks. It does not make a public method unreachable.** The phone's one attack button does not go through `Update`:

```
Assets/_Modules/Village/HUD/HudKitCommandBridge.cs:108   var atk       = Object.FindAnyObjectByType<PlayerAttackController>();
Assets/_Modules/Village/HUD/HudKitCommandBridge.cs:109   var abilities = Object.FindAnyObjectByType<HeroAbilities>();
                                        :117   if (abilities.TryCast(AbilitySlot.Q)) ...
                                        :134   bool swung = atk.TriggerBasicAttack();
```

`FindAnyObjectByType` filters on **GameObject active state**, never on **component enabled state**, so a disabled component on a living hero is found exactly as before. And neither target had a dead-hero gate:
- `PlayerAttackController.TriggerBasicAttack()` (`Assets/_Modules/Village/Enemies/PlayerAttackController.cs:454`, before this change) gated on `HeroLocomotion.InputSuppressed`, `BattleLock.IsInBattle()`, `_isInSwing`, `_nextAttackTime` — **and nothing about the hero being alive.**
- `HeroAbilities.TryCast(AbilitySlot)` (`Assets/_Modules/Village/Hero/HeroAbilities.cs:795`, before this change) gated on def-resolve, wind-up, cooldown and mana — **and nothing about the hero being alive.**

So on mobile the death freeze disabled three components and changed **nothing** about what the attack button does. `PlayerAttackController.Update` (the keyboard/mouse/gamepad path) *is* stopped by `enabled = false`, so the desktop input path is UNREACHABLE for a downed hero by construction. (That is what was proven. It is NOT proven that nobody ever reproduced this on desktop — only that the desktop path cannot produce it. `HudKitCommandBridge.cs:105-107` already writes down the same mobile-only asymmetry for a different defect.)

### ⛔ NOT PROVEN — and named as such per §11B
1. **Which of the two shapes the owner actually hit.** No 2026-09-15 device log is saved in this tree (`logs/debug/` holds nothing newer than `2026-09-06` plus three undated `seeker-*` files; listed this session). Without it I cannot say whether HP reached zero through `TakeDamage` (shape A: the death path ran and input survived it) or outside it (shape B: `_isDead` never set, no event, no handler). **The bypass above is a located candidate, not a measured cause.** The watchdog added below is the discriminator and settles it on the next capture in one grep.
2. **Movement.** The bypass explains the **swing** (the screenshot). It does **not** explain movement: `HeroLocomotion.enabled = false` stops its `Update`, the agent is stopped with `updatePosition=false`, and the LateUpdate death pin re-asserts the pose. The WO's evidence is a mid-swing still, not a movement clip. If the hero was genuinely walking, that is a second, unexplained mover — and the existing death-pin residual watchdog (`[Flow:HeroDeath]`, Fail) will name it on the next capture.
3. **That the hero was ever MOVING.** The bypass explains the **swing**; it does not explain movement. In shape A `HeroLocomotion.enabled = false` (`:1173`), the agent is stopped with `updatePosition=false` and the LateUpdate death pin re-asserts the pose, so the hero cannot walk. The WO's evidence is a mid-swing still, not a movement clip. **This change refuses ATTACK and CAST. It does not refuse MOVEMENT**, deliberately: in shape A movement is already off, and in shape B the correct fix is "zero HP outside `TakeDamage` must enter the lethal branch", not a stick gate bolted on top of a state that should not exist. Deferred until the watchdog names the shape. If the hero really was walking, the existing death-pin residual watchdog (`[Flow:HeroDeath]`, `Fail`) names the mover on the next capture.
4. **That the fix removes the acceptance line.** The acceptance is "zero `still steered at the hero … IsAlive=false` lines while the hero is under control." Refusing input makes "under control" false, so the line should stop. That is a prediction from a headless raid run, and this lane ran none.

---

## 2b. Which shape the capture FITS — a weighting, still unproven

Two facts read at source this session bear on it:

1. **In a raid, the hero's only damage intake is its own contact-tick sweep**, and that sweep sits BELOW `Update`'s zero-HP early-return (`HeroHealth.cs:535`; the sweep begins below it and calls `TakeDamage(tickDamage)` at `:602`). A sweep of `TakeDamage(` callers across `Assets/_Modules` found exactly one external caller that damages the HERO — `Assets/_Modules/Dungeons/ComposedTrapHazard.cs:72`, a dungeon trap, not reachable in a raid. (Tower/ability files only *mention* `HeroHealth.TakeDamage` in comments about the mitigation scalars it consumes; they damage enemies.) `HeroAggroTarget.cs:10-24`, written by the WO-1752 lane this same session, independently records the same finding: there is no attacker-attributed hero damage seam, and it had to derive one from `HeroHealth.Hp` dropping.
   **Consequence: if shape B occurs, it is SELF-SUSTAINING.** HP stays at zero, nothing re-enters `TakeDamage`, the lethal branch is never reached, and the hero plays the rest of the raid at zero HP. That is precisely the nine-minute arc the WO records (13:46 still fighting → Victory at 13:55:00).
2. **The WO's two screenshots are two DIFFERENT heroes** — Grom Lv15 at 13:46:32 and Thrain Lv15 at 13:53:55 — both with an empty HP bar *while playing*. Two independent heroes both dying to exactly zero and both continuing is a far worse fit for shape A than for a state present from the start of the run.

**Weighting: shape B is the better fit.** It is still a weighting. The watchdog settles it in one grep on the next capture, and nothing in this change depends on which shape is true.

### ✅ THE FELT RISK IS CLOSED — SECOND PASS, coordinator direction 2026-09-15
The first pass refused attack and cast while `_isDead` was false, which would have left a **spectator who cannot fight and never dies** — a state no ruling describes. That is no longer the end state.

**The ruling for zero HP already exists and this lane found it**: `HandleDeath` caps the raid at 2 stars (`raidScorer.NotifyHeroDied()`, `:1270`) and continues it (`liveRaidDeploy.NotifyHeroDown()`, `:1306` → `RaidDeployController.cs:1415`, `HeroDownArmyFightsOn`, WO-1526). The defect was only that the sequence which *starts* `HandleDeath` never ran. So the watchdog now runs it — **the same body, not a copy** — and the input refusal drops back to being belt-and-braces.

Residual felt change, stated honestly: in shape B the hero now **goes down** where before it played on as a ghost. That is the documented outcome of zero HP, not a new rule.

**Why the recovery cannot go through `TakeDamage` — proven at source, and it is the same line that makes the bad state terminal:**
```
public void TakeDamage(float amount)   ->   if (_hp <= 0f || amount <= 0f) return;      HeroHealth.cs:741
```
Once HP is at zero with no latch, **every** later call returns at that line. The lethal branch is permanently unreachable no matter how many enemies hit the hero — which is independently why shape B would survive a whole raid, and why the recovery has to enter the sequence directly.

---

## 3. What was changed

### `Assets/_Modules/Village/Hero/HeroHealth.cs`

**The death sequence now has ONE body with TWO callers.**
- **`:876`** — `TakeDamage`'s lethal branch is now `if (_hp <= 0f && !_isDead) BeginDeathSequence(DeathCauseLethalHit); else HitStopManager.DoImpact(HitTier.Light);`
- **`:923-1039` (moved, not rewritten)** — `private bool BeginDeathSequence(string cause)`. The lethal block that used to live inline in `TakeDamage` was **moved verbatim** — every line, comment and ordering preserved, and kept inside its original brace block so the diff reads as a move. **Nothing was copied.** The practice branch's bare `return;` became `return false;`; that is the only statement altered.
  - **`:881` / `:887`** — `DeathCauseLethalHit = "damage"` and `DeathCauseZeroHpNoLatch = "zero-hp-no-latch recovery"`.
  - **`:978-983`** — the death trace keeps the phrase `lethal hit:` verbatim (captures and `GameOverScreenLifecycleRegression`'s header already grep it) and now appends `cause=`, so the same line says which door the death came through.
  - **Exemption 1 — the FTUE peace window.** `TutorialFlow.HostilesSuppressedForTutorial` => **decline**, with a `Warn`. `TakeDamage` already floors a would-be-lethal blow at 1 HP during onboarding (the "died in tutorial" F8 net), so the game has a standing rule that the hero cannot die there; a recovery death that ignored it would reintroduce that defect through a new door.
  - **Exemption 2 — the practice scene**, original behaviour unchanged (floor at 1 HP, latch `PracticeDefeated`, no global death events).
  - **Searched for, and NOT found: any other route to zero HP that legitimately expects no death.** The invulnerability window (`_invulnUntil`) means "takes no damage", not "may not die", and a hero already at zero inside one is still the anomaly — noted, deliberately **not** exempted. Legendary Resolve's cheat-death resolves **synchronously** inside `TakeDamage` (`:797`), so there is no frame in which HP reads zero with a revive pending. `BattleArena` is not an exemption either: `HandleDeath` already defers to it (`AnyBattleInProgress`, with a 10 s safety net), so the arena case is handled *inside* the sequence, exactly as it is for a hero who died to damage.

**Idempotence — proven from the code, not asserted.**
- `if (_isDead) return false;` is the **first statement** of `BeginDeathSequence` (`:925`).
- `_isDead = true` is the **first mutation**, before the trace, the VFX, `EnterDeathFreeze`, `OnDeath?.Invoke()` and `StartCoroutine(HandleDeath())` (`:1031`). Nothing that could re-enter runs before the latch is set.
- The watchdog cannot call twice: `WatchZeroHpWithoutDeath` returns at its `if (_isDead || PracticeDefeated)` guard (`:390`) from the very next frame onward, and it sets its own `_zeroHpNoDeathReported` latch before calling.
- Verified by a ported count over a comment-stripped copy of the file: **exactly one** `StartCoroutine(HandleDeath())` and **exactly one** `_isDead = true` in the whole file.
- Two further independent latches downstream: `RaidScoring.NotifyHeroDied` returns on `_heroDied` (`RaidScoring.cs:622-624`) and `RaidDeployController.NotifyHeroDown` returns on `_heroDownAcknowledged` (`RaidDeployController.cs:1417-1418`). **Three latches for one death; the first is set here.**

**The watchdog and the recovery.**
- **`:535`** — `Update`'s zero-HP early-return calls `WatchZeroHpWithoutDeath()` first.
- **`:344-459`** — the alarm: `_hp <= 0f && !_isDead && !PracticeDefeated`, not the practice scene -> one `FlowTrace.Fail("HeroDeath", "ZERO HP WITH NO DEATH LATCH (WO-1750 shape B): ...")` carrying hp/MaxHp with the full max composition, both scene names, the instance id, `RaidScoring.RaidInProgress`, `SceneOwnership.IsEnemyOwned`, both listener lists and the stack — **then** `BeginDeathSequence(DeathCauseZeroHpNoLatch)` (`:451`) with a `Warn` reporting whether it RAN or was DECLINED by an exemption. **The `Fail` is kept**: recovering silently would hide the defect behind its own fix and the shape would never be diagnosed.
- **`:386` (new) — `ZeroHpNoDeathGraceSeconds = 0.25f`, a debounce, declared.** The effective max is assembled across `Awake` (`:251`), `Start` (`:304`) and `SyncGearHp` (`:323-324`, which runs one line above the watchdog every frame), so a rig whose `GearLoadout` has not resolved can read a transient zero. Killing the hero off one frame of rig assembly would be a far worse defect than the one being closed. 0.25 s is far longer than any assembly transient and far shorter than a player could notice. **This is the one number this lane chose; it is a debounce on an anomaly detector, not a design rule.**
- **`:541-542`** — both the report latch and the debounce clock are re-armed on the ALIVE path, so a recurrence is not silent and a resolved transient leaves no residue.

**Instrumentation (unchanged from pass 1).**
- **`:983`** — the death Step carries `lethalFrom=<Type.Method>`, the first frame outside `HeroHealth`, with compiler-generated coroutine/closure types walked out one level so a coroutine caller prints as its real class.
- **`:1133`** — `TopCallerFrame()`, frames-only, never throws; `"(HeroHealth self-tick)"` when nothing external is on the stack (the ordinary contact-tick death).

**The refusal seam (now belt-and-braces).**
- `public bool IsDeathLatched => _isDead;` (`:466`)
- `public static bool EvaluateInputRefusedForDeath(bool heroPresent, bool heroIsAlive, bool deathLatched)` (`:511`) — a **pure** predicate, the same shape as `HeroLocomotion.EvaluateInputSuppressed` that `DialogueInputGateRegression` tests headless. `heroPresent == false` => never refuse (test scenes / headless rigs stay playable — the same conservative reading `BattleArena.cs:2236` takes).
- `public static bool InputRefusedForDeath(GameObject heroGo)` (`:523`) — resolves the model from the rig, falls back to `Instance`.
- ⛔ Deliberately **not** `HeroLocomotion.InputSuppressed`: that latch belongs to the dialogue beat and carries the WO-1714 stuck-gate watchdog (`HeroLocomotion.cs:412-420`), which a raid-long hero death (WO-1526 keeps the hero down for the rest of the raid) would trip on **every** death.

> **DEVIATION FROM THE BRIEF, DECLARED (§11B B).** The brief asked for `FlowTrace.Fail` **at the `IsAlive→false` transition**. That transition is the NORMAL death — the most common event in the game — and `HeroDeathSeverityRegression` exists specifically because a `Fail` there once filled the owner's F8 triage stream with her own deaths and taught seats to ignore Hero failures. So the normal transition got `Step` + `lethalFrom=` (already the richest dump in the file, with both listener lists), and `Fail` was reserved for the ANOMALY — zero HP with no death latch — which is the state that actually cannot legitimately exist. Both halves are still traceable, the discrimination the brief wanted is stronger, and no expected event raises an error.

### `Assets/_Modules/Village/Enemies/PlayerAttackController.cs`
- **`:454` (first statement of `TriggerBasicAttack`)** — refuses through `HeroHealth.InputRefusedForDeath(gameObject)` with a `FlowTrace.Throttle("HeroDeath", "input-while-down-attack", 1f, …)` naming the component's `enabled` and `activeInHierarchy` state. This is **both** the fix and the "throttled warn while `!IsAlive` and input is still being applied" the WO asked for: when it fires, a caller is proven to be reaching the method past the component disable.
- Placed **before** `HeroLocomotion.InputSuppressed` and far above `StartAttack()`. `PrimaryFallbackRegression` and `ClassPrimaryAndKnightBlockRegression` lint the **bridge's** handler body (`Require(bridge, "atk.TriggerBasicAttack()")`, ordering vs `TryCast`), not this method's body — checked this session; neither is affected.

### `Assets/_Modules/Village/Hero/HeroAbilities.cs` — **declared silo extension**
- **`:795` (first statement of `TryCast`)** — the identical guard, key `input-while-down-cast`.
- **Why it is in scope even though the brief scoped me to `PlayerAttackController`:** the bridge calls `TryCast(Q)` at `:117`, **one line before** the melee fallback at `:134`. Guarding only the sweep would have left a downed ranger or mage still firing its locked Q from the phone's one attack button — half a fix, and the half the owner is most likely to see next. Flagging it here rather than shipping it silently (§11B B).
- Untouched: `TryGetRangedPrimary` and everything WO-1504 disputes.

### `Assets/Editor/Regression/HeroDownInputRefusalRegression.cs` — **new**
Seven cases, headless, no play mode:
1. `[predicate]` — the truth table, including the "no health model ⇒ never refuse" row and the "living hero is never refused" row (the opposite, worse defect).
2. `[attack]` — `TriggerBasicAttack` refuses through the predicate **before** `StartAttack()`, and is not silent.
3. `[cast]` — `TryCast` refuses through the predicate **before** the def resolve, and is not silent.
4. `[one-rule]` — the predicate, the live reading and `IsDeathLatched` all still exist, and the refusal does not write the dialogue latch.
5. `[watchdog]` — `WatchZeroHpWithoutDeath`, the pullable phrase `ZERO HP WITH NO DEATH LATCH`, and `lethalFrom=` all survive (§12: instrumentation is permanent).
6. `[raid-hud]` — the raid-side handler and its on-screen death state survive: `raidScorer.NotifyHeroDied()`, the live-raid branch's `NotifyHeroDown()`, `RaidDeployController.NotifyHeroDown`, and its `EndStateVM.HeroDownArmyFightsOn` status line. **This is the "HUD shows the death state" half of the acceptance, pinned against behaviour that already exists (WO-1526) rather than a screen invented by this ticket** — a live raid deliberately shows no modal, so that status line is the whole presentation of the beat.

7. `[recovery]` — **new, second pass.** `BeginDeathSequence` exists; the file contains **exactly one** `StartCoroutine(HandleDeath())` and **exactly one** `_isDead = true` (counted on a comment-stripped copy, so the file may document its own invariants without failing its own suite); the watchdog calls `BeginDeathSequence(DeathCauseZeroHpNoLatch)`, still raises the `Fail`, and still debounces; the re-entry guard precedes the latch and the latch precedes both `OnDeath?.Invoke()` and `StartCoroutine(HandleDeath())`; both exemptions are present by name; `TakeDamage` still routes through `BeginDeathSequence(DeathCauseLethalHit)` **and still carries `if (_hp <= 0f || amount <= 0f) return;`** — the line that makes the bad state terminal, pinned so the reasoning for entering the sequence directly cannot silently rot.

All seven cases were dry-run in Python against the real files this session: every method body extracts cleanly under the suite's own brace matcher, every asserted needle is present, every asserted ORDER holds (guard 66 < latch 2345 < OnDeath 7891 < StartCoroutine 8006 inside `BeginDeathSequence`), and both counted invariants read exactly 1.

### `Assets/Editor/Regression/DataRegression.cs`
- One `Guard.Try` line registered after the `hero-death-severity` suite, tag `[hero-down-input]`. An unregistered suite never runs.

---

## 4. Gate evidence (the only checks this lane is permitted to run)

```
python tools/gate_brace.py  Assets/Editor/Regression/DataRegression.cs \
                            Assets/_Modules/Village/Hero/HeroHealth.cs \
                            Assets/_Modules/Village/Hero/HeroAbilities.cs \
                            Assets/_Modules/Village/Enemies/PlayerAttackController.cs \
                            Assets/Editor/Regression/HeroDownInputRefusalRegression.cs
GATE_BRACE_SUMMARY bad=0 of 5      exit=0        (re-run after the second pass)
```
NUL-byte scan (§1 WO-434 guard), all five files: **0**.

Two further claims were PORTED and run locally rather than reasoned about, because both are the kind of thing §11B calls hearsay until measured:
- **`HeroDeathSeverityRegression` case 2's exact matcher, re-run after the second pass** (`HeroDeathSeverityRegression.cs:149-173`: comment-strip → every `FlowTrace.Fail(` → span to the first `;` → four phrases, case-insensitive) was reimplemented in Python and re-run over the edited `HeroHealth.cs` after the death-sequence extraction: **3 `FlowTrace.Fail` sites, 0 banned-phrase hits.** Neither the new alarm nor the moved block trips that suite, and case 1's `death freeze armed` -> `FlowTrace.Capture` pairing was not touched.
- **The new suite's own brace-matched method extraction** was dry-run against the real source files: all bodies extract cleanly and every asserted needle is present at the asserted relative order (guard before `StartAttack()`, guard before `Resolve(slot)`).

These are local ports, not the gate. They do not substitute for `COMPILE_GATE_OK` / `REGRESSION_OK`.

⛔ **NOT run, and therefore NOT claimed:** `COMPILE_GATE_OK`, `REGRESSION_OK`, any bake, any build, any device run, any git operation. The new suite has never executed — it is source-structural plus a pure predicate, so it cannot be verified without Unity.

---

## 5. For the lead / the owner

- **No owner question is outstanding.** The raid's hero-death rule exists, is cited above, and this change did not invent one — it makes the existing one reachable from the state that was bypassing it.
- **Shared files, take care at the gate:**
  - `DataRegression.cs` — one registration line, left exactly as it was in pass 1 as instructed; other lanes' lines batch cleanly on top.
  - `HeroAbilities.cs` — the guard is a single early return reading `HeroHealth.InputRefusedForDeath(gameObject)`. It **does not call, reference or depend on `TryGetRangedPrimary`** in either direction, so the UNRULED WO-1504 dispute (CLAUDE.md §7) is untouched and unprejudiced whichever way the owner rules it.
  - `HeroHealth.cs` — the diff is large but is dominated by a MOVE. A reviewer should read `BeginDeathSequence` as relocated, not rewritten; the only altered statement inside it is `return;` -> `return false;` in the practice branch.
- **The next capture still answers the open question for free**, and now says more:
  - `[Flow:Death] lethal hit: cause=damage` → shape A (an ordinary death).
  - `[Flow:HeroDeath] ZERO HP WITH NO DEATH LATCH` followed by `[Flow:Death] lethal hit: cause=zero-hp-no-latch recovery` → shape B, caught and recovered.
  - `input-while-down-attack` / `input-while-down-cast` → the mobile direct-call bypass confirmed as the mechanism rather than merely a candidate.
- **Still never run, still not claimed:** `COMPILE_GATE_OK`, `REGRESSION_OK`, any bake, build, device run or git operation. The new suite has never executed.
- **One number this lane chose**, flagged for a ruling if you want a different one: `ZeroHpNoDeathGraceSeconds = 0.25f` (`HeroHealth.cs:386`) — the debounce before an anomaly is converted into a death.
