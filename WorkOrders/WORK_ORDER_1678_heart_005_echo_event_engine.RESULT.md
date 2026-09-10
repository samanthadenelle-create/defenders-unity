# WORK ORDER 1678 — HEART-005: Echo Event engine — RESULT

**Date:** 2026-09-10
**Lane:** HEART-005 SME, isolated worktree `.claude/worktrees/agent-a00db190be27b5549`, branch `dev`
**Tree:** fast-forwarded to `dev` `4329accd1` at lane start. The WO's own evidence index was
read at `abbeb9362`, so **no line number was copied from the ticket** - every one below was
`grep`-ed in THIS tree at hand-back time. (The first draft of this file asserted that and was
wrong: five test-case line numbers were written before a 28-line block was inserted above them
and had drifted by about thirty. They were re-measured rather than re-asserted, which is the
whole of CLAUDE.md section 11B in one sentence.)
**Gate state:** brace gate + NUL scan clean; node oracle 23/23. **No Unity run, no commit, no push** — per the lane brief.

---

## 0. THE ONE-PARAGRAPH TRUTH

The backend event engine is real, pure, deterministic and covered by a 23-case oracle that
passes. The Heartfire second source is real, capped, and its lint moved in the same change.
The client seam is real, compiled out of every non-Seeker artifact, and instrumented. **What
is NOT real is any player-visible effect of the three modifier lanes or of Scout's Whisper**:
both are written into their seams and read by nothing yet, deliberately, and §4 says exactly
why and which tickets own the reads. Nothing in this lane has been executed inside Unity.

---

## 1. Rulings, and where each one is now enforced by something that can fail

| Ruling (2026-09-10) | How it is enforced | Where |
|---|---|---|
| **DROP** the dungeon-ingredient event | node case `[ruled] the dropped crafting-ingredient event is absent...` + editor PIN A (sweeps the table AND all seven client files, RAW) | `test/heartbound-events.test.js:176`, `Assets/Editor/Regression/HeartboundEventRegression.cs` PIN A |
| Visiting vendor **leaves this ticket** (own WO) | node case `[ruled] the visiting-vendor event left this table...` + editor PIN B; the ticket itself written | `test/heartbound-events.test.js:185`, `WorkOrders/WORK_ORDER_1691_wandering_merchant_visiting_npc_lifecycle.md` |
| Scout's Whisper = **existing report, EARLIER, no new intel** | node case `[ruled] Scout carries NO new intel fields` + editor PIN C (both directions: the existing builder still exists; the Heartbound path builds nothing) | `test/heartbound-events.test.js:216`, PIN C |
| Tier II Echo Labor = **modifier only, no world actors** | node case `[ruled] Echo Labor is a MODIFIER...` (also sweeps for spawn/prefab/actor/figure) | `test/heartbound-events.test.js:204` |
| Heartfire Spark = **allowed second source; move the doc + the lint in the same change** | doc rewritten at `HeartfireCharges.cs:29-64`; pure `Spark` at `HeartfireCharges.cs:378`; service seam `HeartfireService.TryGrantSpark` (`:245`); lint re-pointed as `HeartfireRegression` **PIN I** (`SecondSourceCases`, 7 cases) | see §2 |
| **Economic acceleration yes, combat power never** | node `[covenant]` cases + editor PIN E (kind allow-list, lane allow-list, combat-stat sweep, withdrawable-asset sweep) | `test/heartbound-events.test.js:231` |
| **Seeker only; Play never compiles it** | every new `.cs` is `#if DAPP_STORE`; the Wallet one also sits behind the asmdef's `!GOOGLE_PLAY`; editor PIN H asserts both | PIN H |
| **At most one pulse per day** | not implemented here and not claimed — cadence belongs to the pulse loop (WO-1677). This module is called once per pulse and has no clock at all. | §4 |

---

## 2. Files, with what each one is

### Backend (new)
- **`api/_lib/heartbound-events.js`** — the event table roll. `rollEvent({globalPulseId, playerId, tier})` → `{eventId, kind, tableVersion, claimId, seedHex, reward}`. Seed is `SHA256(pulse <NUL> player <NUL> version)`; the pick walks a cumulative weight sum over an **id-sorted** row list. No `Math.random`, no `Date.now`, no fs, no network — asserted by the `[purity]` case, which reads the module's own **comment-stripped** source.
- **`api/_lib/heartbound-events-config.json`** — `tableVersion: 1`, kinds allow-list, and **five** rows: `resource_surge` (modifier gather_rate 0.15/7200s, minTier 1), `crafting_inspiration` (craft_speed 0.10/7200s, 1), `scout_whisper` (scout, 86400s, 1), `echo_labor` (build_speed 0.10/7200s, **minTier 2** — the HEART-006 Tier II identity), `heartfire_spark` (1 charge, minTier 3, weight 4 — the rarest row).
- **`test/heartbound-events.test.js`** — 23 cases. Output quoted verbatim in §3.

### Client seam (new, all `#if DAPP_STORE`)
- **`Assets/_Modules/Core/Heartbound/HeartboundEchoEvent.cs`** — the descriptor + `HeartboundRewardKind` (Modifier/Scout/Heartfire/Item). Dumb data; carries **no seed** by design.
- **`.../HeartboundEventInbox.cs`** — the pending event, the `IHeartboundEventApplier` registration seam, and the **local** once-only guard (PlayerPrefs `heartbound.claimed.<claimId>`). A declined application does **not** burn the claim (`HeartboundEventInbox.TryApplyPending`).
- **`.../HeartboundModifiers.cs`** — the three economic lanes, time-boxed, non-stacking (larger magnitude + later expiry win). Clock is a **parameter**.
- **`.../HeartboundScoutAccess.cs`** — a boolean and a deadline. No intel.
- **`.../HeartboundEventCopy.cs`** — the reveal words. No reward value is written here (PIN G cross-checks the authored magnitudes against this file's source).
- **`Assets/_Modules/Wallet/HeartboundEventClient.cs`** — parses the payload `toClientPayload` emits and posts it to the inbox. **Parser only** — no endpoint (§4).
- **`Assets/_Modules/Village/Heartbound/HeartboundEventApplier.cs`** — the ONE applier; `[RuntimeInitializeOnLoadMethod]` registers it before the first scene.

### Heartfire (edited — the ruled second source)
- **`Assets/_Modules/Core/State/HeartfireCharges.cs`** — the header's *"exactly one source - the passage of time"* is **corrected in place** to name two sources and the three properties the ruling was granted on (capped by the same ceiling, unbuyable, still not a balance). New pure `public static int Spark(Pool, int charges, int maxCharges, out Pool sparked)` at `:378`: clamps at the ceiling, returns 0 on a full pool, refuses a non-positive request, **does not touch the accrual stamp**.
- **`Assets/_Modules/Village/World/Camps/HeartfireService.cs:245`** — `public static int TryGrantSpark(int charges, string reason)`: `Current()` → `HeartfireCharges.Spark` → `Store` → `Publish`, fully traced.
- **`Assets/Editor/Regression/HeartfireRegression.cs`** — **PIN I** added (`SecondSourceCases`, registered in `RunCore`, described in the file header, and the OK reason string now states the two-source invariant). Seven cases I1–I7, including `I6`, which counts `out Pool` mutators and fails if a **third** appears.

### New regression (new)
- **`Assets/Editor/Regression/HeartboundEventRegression.cs`** — markers `HEARTBOUND_EVENTS_OK` / `HEARTBOUND_EVENTS_FAIL`, entry points `Run(out string)` and `RunStandalone()`. Nine pins A–I, described in the file header.

### Board / docs
- `WorkOrders/WORK_ORDER_1678_heart_005_echo_event_engine.md` — **Status flipped to IMPLEMENTED.**
- `WorkOrders/WORK_ORDER_1691_wandering_merchant_visiting_npc_lifecycle.md` — **new, Status: SPEC.**
- `CLI_LANES_WO_NUMBERS.md` — **deliberately NOT touched** (number pre-assigned by the lead).

---

## 3. Evidence

### The node oracle, verbatim (`node --version` → `v24.11.1`)

```
> node --test test/heartbound-events.test.js
ℹ tests 23
ℹ suites 0
ℹ pass 23
ℹ fail 0
ℹ cancelled 0
ℹ skipped 0
ℹ todo 0
ℹ duration_ms 152.7151
```

Case names (all ✔): `[determinism]` x4, `[spread]`, `[weight]`, `[tier]` x3, `[ruled]` x5,
`[covenant]` x3, `[config]` x3, `[contract]` x2, `[purity]`.

### The brace gate (the port of `CompileGate.BraceBalanced`'s exact rule)

```
> python tools/gate_brace.py <11 .cs files>
GATE_BRACE_SUMMARY bad=0 of 11
EXIT=0
```

### NUL scan (all 14 files this lane wrote or edited, .cs + .js + .json)

```
NUL_SCAN bad=0 of 14
```

⚠ **AND THAT SCAN CAUGHT A REAL DEFECT, WHICH IS WHY IT IS REPORTED RATHER THAN TICKED.**
The first draft of `api/_lib/heartbound-events.js` contained **three literal NUL bytes** — I
had written the seed separator as a raw `\x00` rather than the escape `<NUL>`. `grep` reported
the file as *"Binary file ... matches"* and refused to show line numbers. It is now
`` `${pulse}<NUL>${player}<NUL>${ver}` `` and the file is byte-clean. The repo's NUL guard
(`CompileGate`, WO-434) only scans `.cs` under `Assets/`, so **a NUL in a backend `.js` would
not have been caught by any existing gate** — recorded here as a finding, not fixed, because
widening that guard is not this ticket.

⚠ **And it happened a SECOND time, in this very file.** The first draft of this RESULT quoted
that separator literally and therefore carried **five** NUL bytes of its own; `grep` called
the document "Binary file ... matches" and refused to show a line. They are rendered as
`<NUL>` above. The generalisable lesson is not "be careful": **a document that quotes a
control character contains that control character**, and only a byte scan will tell you.
Every file this lane touched was scanned, including the markdown — which is how this was
caught rather than committed.

### The new regression's pins, simulated

`HeartboundEventRegression` could not be executed (no Unity in this lane), so its nine pins
were **ported to python and run against the real files**: `SIMULATED_FAILS: 0`, exit 0. The script lives in the SESSION SCRATCHPAD and is **not durable** - it is evidence for this
hand-back, not a checked-in tool.

⛔ **THAT IS A SIMULATION, NOT THE GATE, AND THE DIFFERENCE IS NAMED IN §5.** It proved its
worth twice, though — it caught two pins that would have failed on landing:

1. **PIN E3 was structurally unable to pass.** It looked for `"gather_rate"` in the
   *comment-and-literal-stripped* source, and `SourceLint.ReadCode` blanks string **contents**
   — the lane names live inside literals, so the pin could never have seen them. Now the C#
   side is pinned by the **const identifier** (`LaneGatherRate`) and the wire value is pinned
   in the **config**, which is where the client and the backend actually have to agree.
2. **The config's own prose failed its own combat-stat sweep** (an explanatory note quoted a
   stat name). This is the identical trap the node test hit — see below — and it is the
   `HeartfireRegression` lesson exactly: a lint that fires on the prose written to prevent a
   defect is a lint somebody deletes. The prose was rewritten to explain the rule without
   quoting the tokens.

Three node cases hit the same class of bug during authoring and are worth recording because
the fix is a *design* rule, not a tweak: a substring sweep over the whole serialised config
failed on **"resolving"** and **"consolation"** (they contain `sol`) and on the module's own
header sentence *"NO Math.random"*. The tests now sweep the **authored data** (`_`-prefixed
prose keys dropped) and the **comment-stripped** source, and the reasoning is written into
`test/heartbound-events.test.js:44-70` so the next person does not re-derive it.

---

## 4. WHAT WAS **NOT** DONE, AND WHY — read this before believing anything works end to end

1. **No endpoint. Nothing calls `rollEvent` yet.** `api/heartbound/status.js` belongs to the
   HEART-001 lane and the pulse loop to HEART-004; the brief forbade touching either. The
   contract between the halves is `toClientPayload` (backend) ↔ `HeartboundEventClient`
   (client). **Nobody has run the two halves against each other.**
2. **The three modifier lanes are written and read by NOBODY.** `HeartboundModifiers` is
   populated by the applier and consumed by no economy code. This is deliberate: the owner's
   ruling of 13:36 puts a **10% ceiling on the sum of production-rate modifiers**, and that
   meter is WO-1682. Wiring `gather_rate` into `EchoService` before the meter exists would be
   shipping an uncapped acceleration and calling it capped. **So a Resource Surge event today
   changes no number the player can see.**
3. **Scout's Whisper sets a flag nothing reads.** `RaidDeployScreen` already paints
   `vm.ScoutIntel` **unconditionally** (`RaidDeployScreen.cs:787`), so "earlier" must mean a
   surface *before* the deploy screen — the raid selection card, which is the raid lane's
   files, not this ticket's. The one-line consumer is `HeartboundScoutAccess.IsRevealEarly(now)`
   at that visibility site.
4. **`HeartboundRewardKind.Item` is declared and REFUSED.** Once the ingredient event was
   dropped and the vendor moved out, **no V1 row carries an item**, so there was nothing to
   grant. The applier logs a `Warn` and returns false, which leaves the claim unburned so a
   later build can still pay it. `VillageInventory.AddEarned` is therefore **not called by this
   lane at all** — the WO's D3 named it, and this is the honest deviation.
5. **Echo Bloom and Ancient Echo are not in table V1.** Both need application seams that do not
   exist (the Heart's visual is WO-1680; a codex/lore collectible is greenfield). A reveal card
   naming a reward with nothing behind it is worse than the row's absence. They land with a
   **`tableVersion` bump**, and the reason is written into the config's `_authoringNotes`.
6. **`MonetizationCovenantRegression` was NOT extended** (the WO's §1g). Its `MonetizationFiles`
   list (`:97-104`, re-read this session) is six project-root-relative paths, all under
   `Assets/` or `WorkOrders/`, and its sweep is built around *sellable* JSON — reward `kind`
   values like `modifier`/`scout` are not on its derived allow-lists and would have failed it.
   The Echo Event table is not a monetization file: **nothing in it is sold.** The equivalent
   protection was written into `HeartboundEventRegression` PINs E and F instead (probability
   fields, combat stats, withdrawable assets, banned words), which is a *stronger* sweep for
   this file's actual risks. **If the lead prefers the covenant list, that is a one-line add and
   a reworked allow-list — say so and it will be done rather than argued.**
7. **The reveal CARD itself was not built.** D4's copy half exists (`HeartboundEventCopy`) and
   so does the seam a surface binds to (`HeartboundEventInbox.Arrived` / `.Applied`), but there
   is no panel, no layout and no screen. ⛔ **And it must NOT ship before the consumers in items
   2 and 3 above exist**: the copy says "Gathering yields 15 percent more" while nothing yet
   changes a gathering number, so a card shown today would tell the player something untrue.
   A surface lane should take the copy and the events, not re-invent them.
8. ⛔ **EVERY TUNING VALUE IN THE TABLE IS LANE-INVENTED AND NEEDS THE OWNER'S WORD.** Only two
   numbers came from the spec or a ruling: Resource Surge's **2 hours** (spec `:531`) and Echo
   Labor at **Tier II** (HEART-006). The weights (34/26/22/14/4), the magnitudes (0.15 / 0.10),
   the 24-hour scout window and `heartfire_spark` at **minTier 3** are placeholders I chose so
   the engine had a table to be tested against. They are balance decisions, and balance is the
   owner's call, not a lane's. Changing any of them is a one-line config edit **plus a
   `tableVersion` bump** (the version is inside the seed; editing a row without bumping would
   silently re-roll every past pulse).
9. **`SaveSchema` was not touched**, and must not be for this feature: the save is
   client-authored, so a claim recorded in the blob could be rewritten by a replayed blob.
10. **`CLI_LANES_WO_NUMBERS.md` was not edited** (numbers pre-assigned).
11. **`HeartboundStatusClient.cs` and `VerifiedStakeSnapshot.cs` were not edited** — the
   coordinator ruled them read-only for this lane mid-work. `VerifiedStakeSnapshot` does not
   exist in this worktree at all, so nothing here references it; the tier the roll needs is a
   **backend** input to `rollEvent`, never read from the client.

---

## 5. ⛔ THINGS THIS LANE HAS **NOT** PROVEN — named as unproven

- **Nothing here has been compiled.** No Unity run of any kind. The brace gate proves brace
  balance and nothing else; `COMPILE_GATE_OK` has not been sought or seen.
- **`HeartboundEventRegression` has never executed.** Its nine pins were simulated in python
  against the same files. A simulation can be wrong about `SourceLint`'s exact stripping
  behaviour in a way my port does not model — it already was, once (PIN E3, §3). **Treat a
  green simulation as "likely to pass", not as a pass.**
- **`HeartfireRegression` PIN I has never executed** either, and it is the pin the owner's
  ruling actually turns on. Its I1–I5 cases run against the pure function and should be
  decisive; I6/I7 are source-lints and carry the same caveat.
- **No `#if DAPP_STORE` build has been produced,** so the claim "this compiles under the
  define" is unproven in both directions: neither the guarded path nor the excluded path has
  been through a compiler. The excluded path is the likelier of the two to hold (it is a
  single early-return method plus usings), and that is a judgement, not a measurement.
- **The determinism claim is proven in Node and only in Node.** No C# ever computes an event,
  by design, so there is no cross-language equivalence to prove — but if a future lane adds a
  client-side mirror, this sentence stops being true.
- **The line/column references in the ticket's own evidence index (§7) were read at
  `abbeb9362`.** Every path I depended on was re-opened in this tree; any line number in the
  ticket that I did not personally re-read should be treated as hearsay.

---

## 6. Registration line for the lead (DataRegression.cs — NOT edited by this lane)

Add to `DeNelle.Editor.DataRegression.RunAll`'s suite list, in the same shape as its neighbours:

```csharp
if (!DeNelle.Editor.Regression.HeartboundEventRegression.Run(out var heartboundEventsReason)) failures.Add(heartboundEventsReason); else log.AppendLine("[heartbound-events] " + heartboundEventsReason);
```

⚠ This **raises the suite count in the `REGRESSION_OK <n>/<n> suites` marker by one.** Judge
the run by the marker on a fresh log, never by the exit code, and never by a count written in
a doc — including this one.

`HeartfireRegression` needs **no** registration change: PIN I was added inside its existing
`RunCore`, so it runs wherever that suite already runs.

---

## 7. Full file list

**New — backend**
```
api/_lib/heartbound-events.js
api/_lib/heartbound-events-config.json
test/heartbound-events.test.js
```

**New — client (+ hand-authored .meta for each)**
```
Assets/_Modules/Core/Heartbound.meta                              (folder)
Assets/_Modules/Core/Heartbound/HeartboundEchoEvent.cs
Assets/_Modules/Core/Heartbound/HeartboundEventInbox.cs
Assets/_Modules/Core/Heartbound/HeartboundModifiers.cs
Assets/_Modules/Core/Heartbound/HeartboundScoutAccess.cs
Assets/_Modules/Core/Heartbound/HeartboundEventCopy.cs
Assets/_Modules/Wallet/HeartboundEventClient.cs
Assets/_Modules/Village/Heartbound.meta                           (folder)
Assets/_Modules/Village/Heartbound/HeartboundEventApplier.cs
Assets/Editor/Regression/HeartboundEventRegression.cs
```

**Edited**
```
Assets/_Modules/Core/State/HeartfireCharges.cs                    (header + Spark)
Assets/_Modules/Village/World/Camps/HeartfireService.cs           (TryGrantSpark)
Assets/Editor/Regression/HeartfireRegression.cs                   (PIN I + header + OK reason)
WorkOrders/WORK_ORDER_1678_heart_005_echo_event_engine.md         (Status flip)
```

**New — board**
```
WorkOrders/WORK_ORDER_1691_wandering_merchant_visiting_npc_lifecycle.md   (Status: SPEC)
WorkOrders/WORK_ORDER_1678_heart_005_echo_event_engine.RESULT.md          (this file)
```
