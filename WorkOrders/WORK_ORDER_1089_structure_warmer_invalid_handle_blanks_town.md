# WORK ORDER 1089 — StructureContentWarmer dies on an invalid handle and the town renders with no buildings

**Status:** IMPLEMENTED — awaiting the resident-structures confirm (see "Root cause, PROVEN" below)
**Minted:** 2026-09-09 by the UI seat (from the UI reserved block; banner bumped 1089 → 1095 in the same edit)
**Silo:** Content / Addressables
**Severity:** P0 — player-facing, every structure in town, on the current device build
**Source:** F8 captures seq=4708 (exception) and seq=4709–4766 (the cascade)

> ⚠ **UNTRUSTED WORKING-TREE EDITS EXIST FOR THIS TICKET.** The UI seat overstepped and ran an edit
> agent against `StructureContentWarmer.cs` (and it had begun on the sibling `EnemyContentWarmer.cs`)
> before being stopped mid-work. Both files are dirty. Braces balance and there are no NUL bytes, but
> **the logic is half-written and must not be trusted.** CLI: review by explicit path or `git
> checkout --` them and implement fresh. See `WO_1089_1094_WORKING_TREE_NOTE.md`.

---

## Symptom (owner's Seeker, build of 2026-09-09, scene `Main_Castle_Overworld`)

Every structure in town is missing. Forge, lumbermill, farm, PetHouse2, armorer, arcane tower,
store, jeweler, ShopAndCrafting, IronMine, GenericContainer, Tower_Wooden_Watchtower, barracks — all
of them fall back to a pending-art proxy, and `[Flow:MagentaGuard]` then hides each stray magenta
placeholder, so the failure renders as an **empty town** rather than a pink one.

## The proving data — the chain is captured, not inferred

**1. `t=6.07s`, scene `Title` (seq=4708):**

```
Exception: Attempting to use an invalid operation handle
  UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle`1[TObject].get_InternalOp()
  UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle`1[TObject].get_Status()
  DeNelle.Core.StructureContentWarmer+<WarmRoutine>d__70.MoveNext()
  UnityEngine.SetupCoroutine.InvokeMoveNext(...)
```

`WarmRoutine` queries `.Status` on a handle that is no longer valid. **The coroutine throws and dies
at six seconds**, before any structure is spawned.

**2. `t=14.38s` (seq=4709 onward):**

```
[Flow:VisualFactory] model not found via Addressables OR Resources: 'Structures/Forge' — returning
null (caller falls back). UNDERLYING FETCH CAUSE: none recorded — no async fetch has FAILED for this
address, so the bytes were either never requested or are still in flight
[fetchAttempts=1/3, resolveAttempts=1, warmerState=Warming, resident=0, pending=1,
 lastTransportUrl=(none)]
```

via `VisualFactory.ReportResolveMiss` ← `VisualFactory.Skin` ← `StructureFactory.Create` ←
`BaseLayoutLoader.Spawn/Rebuild/LoadFromState/Start`.

Two fields carry the whole diagnosis: **`warmerState=Warming`** forever (the warmer never reached a
terminal state, because it died) and **`lastTransportUrl=(none)`** (no transport was ever attempted
for these addresses).

## Second occurrence, 2026-09-09 11:14 (seq=4769–4773) — and it narrows the cause

A fresh device session 34 minutes later reproduced it exactly: same exception, same
`WarmRoutine.MoveNext()` frame, same cascade, and the resolve-miss fields are **character-identical**
(`fetchAttempts=1/3, resolveAttempts=1, warmerState=Warming, resident=0, pending=1,
lastTransportUrl=(none)`), this time on `Structures/store`, `jeweler`, `Forge`, `lumbermill`.

**The one thing that changed is the timing, and it is diagnostic:**

| Session | Throw at | Scene |
|---|---|---|
| 10:40 (seq=4708) | `t=6.07s` | Title |
| 11:14 (seq=4769) | `t=17.34s` | Title |
| 11:23 (seq=4805) | `t=5.96s` | Title |

Three occurrences in 43 minutes, spread across ~11 s of boot time — and note the third is *earlier*
than the second, so this is scatter, not a drift or a warm-up effect. **So this is not a deterministic sequence point** — the
handle is invalidated by something asynchronous (a completed/released op, a timing-dependent
teardown), not by a fixed line always running in the same order. Look for a handle whose lifetime is
owned elsewhere and can end before `WarmRoutine` next inspects it, rather than for an
always-wrong line.

Also confirmed by the repeat: **the outcome is deterministic even though the timing is not** — every
session so far ends with the warmer dead and the town empty. Treat "it booted fine once" as
unproven until a run shows structures resident.

### Blast radius is wider than the model path (seq=4790, same 11:14 session)

```
[Flow:Hub] 'ArcaneTower_MagicUpgrades': forced albedo 'Structures/ArcaneTower_Albedo' did NOT resolve
via StructureAssetLoader (registered=False, knownAbsent=False, warmerState=Warming, resident=10) —
the model keeps its embedded materials and a Tripo FBX WILL RENDER WHITE. Arming one retry for when
structure content settles.
```

A **second consumer** — the texture/albedo path through `StructureAssetLoader`, not just
`VisualFactory`'s model path — fails off the same dead warmer, and reports the same
`warmerState=Warming`. Note `resident=10` here versus `resident=0` in the model misses: the warmer
had partially populated before it died, so **the failure is partial and address-dependent, which is
why some art can appear while the rest does not.** Anything that waits on "when structure content
settles" waits forever, because the warmer never reaches a terminal state (fix item 3).

## ⛔ This is NOT the §16 R2-push trap

`lastTransportUrl=(none)` and "no async fetch has FAILED" rule it out: nothing 404'd, because nothing
was requested. Do not spend a morning re-pushing bundles. The failure is inward, in the warmer.

## ✅ Root cause, PROVEN — fix landed in `StructureContentWarmer.cs:1084-1105` (CLI seat, 2026-09-09)

Editor capture **seq=4966** (`t=4.09s`, scene Title) shows the new guard running and the pass
**continuing** instead of dying. Read at source, the explanation is complete:

> Addressables hands **every caller the same shared `m_InitializationOperation`** once initialisation
> has started (`AddressablesImpl.cs:359-360`), and arms `ReleaseHandleOnCompletion` on it when the
> **first** caller used the default `autoReleaseHandle=true` (`:420-421`). Addressables' own chain
> does exactly that (`:107`, reached by any load issued before init), and so does
> `EnemyFamilyPullProbe.cs:33`. Our `InitializeAsync(false)` only protects the operation when **we**
> are the first caller; on the shared-return path the `false` never reaches `:421`, the op
> self-releases on completion, is recycled, its `Version` is bumped — and our copy of the handle is
> stale.

And the ordering half, which is what actually threw:

> `IsDone` is **TRUE for an invalid handle**, so an `if (!IsDone) ... else <read Status>` ladder sends
> the invalid case into the branch that throws. `IsValid()` is now tested **first**, and that order is
> the fix.

**This confirms the timing-scatter reading above.** Whether we are the first caller is a race against
`EnemyFamilyPullProbe` and any load issued before init — which is exactly why the throw landed at
6.07 s, 17.34 s and 5.96 s across three sessions with no drift.

### Still open on this ticket

1. **The P0 is not yet proven cured.** seq=4966 proves the coroutine survives; it does **not** prove
   structures become resident. Acceptance still needs a run showing resident structures and no
   `model not found ... Structures/*` cascade — on the **device**, since all three original
   occurrences were device-side.
2. **Severity is wrong for a handled condition.** The new line is a `FlowTrace.Fail` →
   `Debug.LogError` → an **F8 error capture on essentially every boot** where we are not the first
   caller. It is now a *known, expected, handled* path that the code itself says is benign ("an
   invalid handle means the operation is finished and gone"). Left at `Fail` it cries wolf every
   session and buries real captures in the inbox — the §14 failure mode this instrumentation exists
   to prevent. **Recommend `FlowTrace.Warn`, or `Once`**, keeping the full explanatory text.

   **This is now measured, not predicted.** It has already fired twice as an F8 *error* capture in
   75 minutes of ordinary play — seq=4966 (11:45, invalid after 1.6 s) and seq=4972 (13:00, invalid
   after 2.0 s), both Editor, both scene Title, both the handled path behaving exactly as designed.
   Two captures that needed triage and produced nothing. At this rate every session buys several,
   and the inbox is the queue where real defects wait behind them.
3. `EnemyContentWarmer.cs` — confirm whether the sibling carries the same `IsDone`-before-`IsValid`
   ladder, and fix it the same way if so.

## Root cause to confirm and fix

Locate the `.Status` / `.Result` access in `WarmRoutine` that runs against a handle which was
released, never assigned, or otherwise invalid. Cite the line in the RESULT file.

## Fix spec

1. **Guard the access.** Never touch `.Status` / `.Result` without an `IsValid()` check.
2. **Make `WarmRoutine` survive a bad handle** rather than dying. This is the severity: one bad
   address must never be able to blank the entire town. A single throw taking out every structure is
   the actual defect; the invalid handle is only the trigger.
3. **Terminal state.** The warmer must record a terminal state (not a permanent `Warming`) when it
   finishes *or* aborts. `warmerState=Warming` forever is itself a reporting defect — it made a dead
   coroutine look like work still in flight, which is what sent the first read down the wrong path.
4. **FlowTrace** (§12, never strip): a `Warn`/`Fail` at the point a handle is found invalid, **naming
   the address**, so the next occurrence identifies itself.
5. Check the sibling `EnemyContentWarmer.cs` for the identical trap — the stopped agent's last words
   were that it had found one. Confirm at source before changing it; if it is the same shape, fix it
   the same way in the same commit.

## Do NOT touch

`VisualFactory`, `BaseLayoutLoader`, `MagentaGuard`, `StructureFactory`. The fallback and the guard
behaved correctly here — they are what turned a crash into a degraded render. The fix lives in the
warmer.

## Acceptance criteria

- [ ] The invalid-handle line is cited at `file:line` in the RESULT.
- [ ] An invalid handle logs and is skipped; `WarmRoutine` continues and reaches a terminal state.
- [ ] `warmerState` can never read `Warming` after the routine has exited by any path.
- [ ] A headless or device run shows structures resident, and no `model not found ... Structures/*`
      cascade at boot.
- [ ] `EnemyContentWarmer` either fixed the same way or explicitly cleared, with the reason recorded.
- [ ] Brace balance on every `.cs` touched; `COMPILE_GATE_OK` + regression markers on a fresh log.
- [ ] **Owner felt-verifies on device that the town renders its buildings**, then closes (§13).

## Ship note

The APK at `Builds/Android/DefendersOfTheRealm.apk` (written 2026-09-09 10:41) is the build these
captures came from. It must not be used as the hackathon submission APK; that link waits on this fix.
