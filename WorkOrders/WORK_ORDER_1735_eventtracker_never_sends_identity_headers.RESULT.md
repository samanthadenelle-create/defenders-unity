# WORK ORDER 1735 — RESULT

**Outcome: IMPLEMENTED, NOT YET GATED.** Edit-only lane: no Unity, no compile gate, no regression
run, no build, no bake, no git, no deploy. Everything below is a **CLAIM** until the lead gates it.

⛔ **ONE BLOCKING FINDING FOR THE LEAD, IN THE `api/` SILO — read §5 before shipping a WebGL build.**

---

## 1. The seam reused (the whole point of the ticket)

**`BackendRequestSigner.TryAttachCachedSession(UnityWebRequest, string playerId)`**
— `Assets/_Modules/Core/Web3/BackendRequestSigner.cs:421-433`, read at source this session.

It is the **non-minting, non-signing** variant of the save rail's attachment:

* guest id -> `X-Guest-Id` (`:426-429`)
* wallet with a usable cached session -> `X-Session` (`:430`) + `X-Wallet` (`:431`)
* otherwise -> `SessionUsable` (`:86-89`, pure static-field comparison) fails and it returns
  `false`, setting nothing

It never awaits, never opens a wallet `SignMessage` sheet, never makes a network call. It is already
the seam four other passive callers use, so this is reuse of a proven path, not a new one:
`SkuEntitlementService.cs:65`, `CommunityShowcaseVoting.cs:265`,
`GooglePlaySettlementComposer.cs:176`, `GameStateService.cs:2333` (the save rail itself),
`GooglePlayStorefrontVM.cs:105`, `GooglePlayIdentityClient.cs:34`.

The identity itself comes from the **same one accessor the save rail uses**:
`BackendRequestSigner.CurrentPlayerId()` (`:142-147`) -> `GameState.BoundWallet`, trimmed, empty when
there is no account. **No second identity source was created; one pre-existing one was removed** (see
§2b).

---

## 2. Files changed — two, both inside the declared silo

### 2a. `Assets/_Modules/Core/Analytics/EventTracker.cs` — the fix

**`SendBatch`, immediately after the `Content-Type` line (the block is now `:303-328`):**

```csharp
string identityPlayerId = ResolveIdentityPlayerId();
bool   identityAttached =
    DeNelle.Core.Web3.BackendRequestSigner.TryAttachCachedSession(req, identityPlayerId);
ReportIdentityMode(identityPlayerId, identityAttached);
```

⛔ **It deliberately does NOT `return false` when `identityAttached` is false**, and a 12-line comment
at the call site says why. Every *other* caller of this seam fails closed, because an unauthenticated
read/write of player state is a security question. Analytics is fire-and-forget telemetry: a wallet
player holding no session yet is the **documented** boot state (`BackendRequestSigner.cs:412` —
the token is memory-only by design, boot never signs, ruling 2026-09-07), and dropping their batch
would convert a fixed attribution bug into a dropped-telemetry bug. The batch still delivers exactly
as it did before; only the headers are new. This satisfies WO §4's "never fail a batch because no
session exists".

**New private helpers (`:350-433`):** `ResolveIdentityPlayerId()` (try/catch around
`CurrentPlayerId`, so identity resolution can never throw a flush) and `ReportIdentityMode()` (§3).

### 2b. Same file, `Enqueue` (`:168-176`) — a second identity source removed

Was: `GameStateService.Instance?.State?.BoundWallet ?? "anonymous"` — a **second copy of the identity
rule** living three lines from the batch that would now carry a differently-derived header. Now routed
through `BackendRequestSigner.CurrentPlayerId()`, with `"anonymous"` preserved for the no-account case.

**Behaviour is unchanged for the server**, proven rather than assumed: `CurrentPlayerId` returns
`BoundWallet.Trim()` or `string.Empty`; `"anonymous"` and an empty id are both rejected by
`isGuestId`'s `/^guest-local-[0-9a-f]{64}$/`, so WO-1733's `guest-body` rail treats them identically
to before. The only difference is a trimmed id, which is strictly better.

### 2c. `Assets/Editor/Regression/EventTrackerIdentityRegression.cs` — NEW

See §4.

---

## 3. Instrumentation (CLAUDE.md §12)

`ReportIdentityMode` emits **one `FlowTrace.Once` per identity MODE per session**, keyed
`"identity-mode:" + mode` (`guest` | `wallet`), naming which header rail is being sent and which
`_auth` value the rows should land under.

**Keyed on the mode, not on a fixed string, on purpose:** a wallet player who mints a session
mid-session transitions `none -> wallet`, and a fixed key would have swallowed the one line that
proves the fix works for the cohort the owner most wants to see.

When nothing attached it emits **`FlowTrace.Fail`**, as briefed — and the message **names which of the
two causes it is**, because conflating them is exactly how the original bug hid for eight days:

* `BoundWallet` empty — events queued before `GameStateService.EnsureAccount` mints the guest id.
* A wallet is bound but no session is held in memory.

⛔ **THE `none` FAIL IS GATED TO ONCE PER CAUSE, AND IT HAD TO BE.** Read at source,
`FlowTrace.cs:157-238` exposes `Once` (Step level) and `Throttle`, but **no Fail-level once variant**.
An ungated `Fail` here would not have been once per session at all — `SendBatch` runs every 30 s (or
every 10 events) **and again on each of up to 4 retries**, so it would have fired continuously for the
entire session of every wallet player. That is precisely the log firehose CLAUDE.md §12 warns about:
it evicts the boot window out of the device logcat ring and destroys the evidence the trace exists to
preserve (memory `logcat-ring-buffer-destroys-evidence`). So the file keeps a small static
`_identityModeReported` set keyed on the CAUSE (`none:no-account` / `none:wallet-without-session`),
not on `"none"` — a player who starts with no account and later binds a wallet still gets the second,
genuinely different, Fail.

⚠ **ONE QUESTION FOR THE LEAD (deviation NOT taken silently — implemented as briefed, flagged here).**
The wallet-without-session cause is the **documented, expected** boot state, so that `Fail` fires once
per session for every wallet player even when nothing is wrong. That is arguably a `Warn`. I
implemented `Fail` as the brief specified and did not quietly downgrade it; **changing it is a one-word
edit and the lead's call.** The counter-argument for keeping `Fail`: those rows genuinely do land as
`unverified`, which is the ticket's entire complaint.

The whole helper is wrapped in try/catch — instrumentation must never be the thing that breaks a flush.

---

## 4. The regression — `[analytics-identity]`, two halves, honestly labelled

`Assets/Editor/Regression/EventTrackerIdentityRegression.cs`, namespace `DeNelle.Editor.Regression`.

`Run` wraps all three cases in a try/catch that turns an escaped exception into a normal failure
string. `DataRegression.RunAll` calls suites in the bare `if (!X.Run(...))` shape and `Guard.Try`-wraps
only one of them (`:402`), so an exception escaping a suite aborts the **whole** gate run and the
`REGRESSION_OK` marker simply never prints — a failure mode that reads as "the gate hung", not as
"this suite is broken".

**Cases 1a / 1b — BEHAVIOURAL, a real measurement, not a source read.** They construct an actual
`UnityWebRequest`, call the production seam on it, and read the header back off the request object.
`UnityWebRequest` headers are settable and readable without `SendWebRequest`, so this runs headless
with **no network, no wallet, no backend**, and both requests are disposed.

* **1a** — a well-formed `guest-local-<64 hex>` id: the seam returns true **and** `X-Guest-Id` reads
  back equal to the id.
* **1b** — a base58-shaped id with no session: the seam returns **false** and `X-Session` is **null**.
  A wallet address is public, not a credential; asserting one without proof is the hole WO-1506 closed.

**Case 2 — SOURCE-TEXT, and declared as source-text in the file's own header.** Stating the WO's
"say which": `SendBatch` is a private `async UniTask` method on a `DontDestroyOnLoad` MonoBehaviour
whose only observable output is a request it immediately awaits. There is no seam to drive it headless
without standing up a live player loop or refactoring the flush path, and refactoring a shipped
fire-and-forget rail purely to make it testable is a larger risk than the pin is worth. So it asserts
the source contract and does not dress a grep up as a test. It requires
`BackendRequestSigner.TryAttachCachedSession`, requires `BackendRequestSigner.CurrentPlayerId`,
requires the named `identityAttached` result (so the never-fail-closed property stays reviewable),
and **fails if the file ever contains `SetRequestHeader("X-`** — i.e. if someone re-copies the header
logic instead of calling the seam.

That negative case matches the **setting** of an `X-` header, not the mere mention of one: the
`FlowTrace` message legitimately names the header it sent, and a pin that could not tell a log line
from a second implementation would get "fixed" by deleting the instrumentation.

**Self-checked against the edited tree this session:** `grep -n 'SetRequestHeader("X-'
Assets/_Modules/Core/Analytics/EventTracker.cs` -> exit 1, no match; the three required tokens are
present (9 combined hits).

### Registration line for the lead to add — I did NOT add it

Into `Assets/Editor/Regression/DataRegression.cs`, `RunAll`, in the `:395`-style shape:

```csharp
if (!DeNelle.Editor.Regression.EventTrackerIdentityRegression.Run(out var evtIdentityReason)) failures.Add(evtIdentityReason); else log.AppendLine("[analytics-identity] " + evtIdentityReason);
```

The suite count in `REGRESSION_OK <n>/<n>` rises by one.

---

## 5. ⛔ BLOCKING FINDING FOR THE LEAD — `X-Wallet` is not CORS-allowed (server silo, not touched)

**Proven, both halves read at source this session:**

* `api/events/track.js:197` sets
  `Access-Control-Allow-Headers: 'Content-Type, X-Session, X-Guest-Id'`.
* `BackendRequestSigner.cs:430-431` sets `X-Session` **and `X-Wallet`** on the wallet rail.

On **WebGL** (the Pi surface) a request header absent from that list fails the browser's CORS
preflight, so a wallet player's analytics batch would be **blocked entirely** — strictly worse than
today, where it is delivered but unattributed. Native Android/Windows builds are unaffected (no
preflight).

**I did not work around it**, because both available workarounds are forbidden: `UnityWebRequest`
cannot unset a header, and copying the header logic into `EventTracker` to send only `X-Session` is
the duplicated-state defect WO §4 explicitly rules out. **The fix is one word in the server silo** —
add `X-Wallet` to that list — and `api/` is a different gate (WO §6), so it is handed over, not taken.

**Recommendation: do not ship a WebGL build carrying this change until that word lands.** Android and
Windows can ship immediately.

---

## 6. The other two ACs — proven, not assumed

* **No retry storm (WO §4 / §5).** Read `resolveIdentity` at `api/events/track.js:135-150`: when
  `X-Session` is present but the verifier rejects it, the route **`console.warn`s and falls through**
  to the guest rail, then the body rail, then `unverified`. It does **not** 401. So a stale token
  cannot fail the POST, cannot burn the 4 retries, and cannot open the circuit breaker.
* **The guest save budget is untouched (WO §4).** `guest_rate_limit` is spent by `verifyGuest()`;
  the route deliberately never calls it for analytics, and **this change adds no call of any kind** —
  `TryAttachCachedSession` sets a header string and returns. Nothing client-side can spend the budget
  that `game/save` and `game/load` share.

---

## 7. Gate evidence produced in this lane

| Check | Result |
|---|---|
| `python tools/gate_brace.py Assets/_Modules/Core/Analytics/EventTracker.cs` | `GATE_BRACE_SUMMARY bad=0 of 1`, exit 0 |
| `python tools/gate_brace.py Assets/Editor/Regression/EventTrackerIdentityRegression.cs` | `GATE_BRACE_SUMMARY bad=0 of 1`, exit 0 |
| NUL bytes, `EventTracker.cs` | none (24236 bytes) |
| NUL bytes, `EventTrackerIdentityRegression.cs` | none (12125 bytes) |
| NUL bytes, the WO + this RESULT | none (4911 / 14465 bytes) |

---

## 8. PROVEN vs UNPROVEN (CLAUDE.md §11B)

**Proven this session, by reading at source or measuring:**

* The seam exists, is public, and sets the headers claimed — `BackendRequestSigner.cs:421-434`.
* `CurrentPlayerId` returns trimmed `BoundWallet` or empty — `:142-147`.
* `SessionUsable` (`:86-89`) compares static fields only — it touches neither
  `GameStateService.Instance` nor `CoreServices.WalletSigner`, both of which can be null in a
  headless editor run. That is what makes regression case 1b safe to run at the gate.
* `FlowTrace` exposes `Once` and `Throttle` but **no Fail-level once variant** — `FlowTrace.cs:157-238`.
* `EventTracker.cs:290-294` previously set only `Content-Type` — read before editing.
* The CORS list omits `X-Wallet` — `track.js:197`.
* A rejected session falls through instead of 401-ing — `track.js:135-150`.
* Braces balanced and no NUL bytes in both files — commands and outputs in §7.
* The negative source pin does not trip on the edited file — grep, §4.

**NOT proven — nobody should read this hand-back as if it were:**

* ⛔ **It does not compile.** No Unity ran in this lane. `COMPILE_GATE_OK` is outstanding.
* ⛔ **The new suite has never executed.** Cases 1a/1b are *designed* to run headless with no network;
  that design is unproven until `DataRegression.RunAll` runs with the §4 line registered. In
  particular, if some other suite leaves a session token in `BackendRequestSigner`'s static state
  earlier in the same run, **case 1b could fail on ordering** — that would be a fixture problem, not
  a product defect, and the fix is to run it before any session-minting suite.
* ⛔ **No live-DB verification.** The WO's acceptance criteria (§5: the `guest-body` -> `guest`
  crossover, `_auth:'session'` rows, `COUNT(DISTINCT player_id) > 1`) are measurable **only after a
  build carrying this ships and real traffic arrives**. Nothing in this lane can stand in for that.
* The once-per-cause gate is **reasoned, not observed**: no run has yet confirmed the Fail appears
  exactly twice at most in a real log. The gating logic is a `HashSet.Add` guard, which is as simple
  as it gets, but "I read the code" is not "I read the log".
* `.meta` files for the new `.cs` were not created — Unity generates them on import.

---

## 9. Board

* WO-1735 `**Status:** IMPLEMENTED, NOT YET GATED` (flipped in the WO by this lane).
* `BOARD.html` **not** regenerated here — no git, no scripts in this lane; the lead regenerates and
  commits the flip in the same commit as the work.
