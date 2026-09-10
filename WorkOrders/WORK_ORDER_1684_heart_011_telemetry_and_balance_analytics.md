# WORK ORDER 1684 — HEART-011: Telemetry and balance analytics

**Status:** READY TO IMPLEMENT
**Silo:** Analytics only — client `EventTracker.Track` calls + backend `logApiEvent` rows. No gameplay, no economy, no UI, no scene files.
**Raised by:** HEARTBOUND-TRIAGE lane, 2026-09-10.
**Number:** PRE-ASSIGNED by the coordinator. `CLI_LANES_WO_NUMBERS.md` deliberately **NOT** edited by this lane.
**Spec section:** `docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:1030-1100` (HEART-011).
**Tree:** worktree at `dev` **`abbeb9362`**.

---

## 0. Classification — **NEW events on an EXISTING, fully determined pipeline, on both sides**

This is READY because nothing about it needs a decision: the pipeline exists, both ends of it exist, the table exists, the retention policy exists, and the spec lists the exact event names.

### 0a. Client → `EventTracker`
`Assets/_Modules/Core/Analytics/EventTracker.cs` — the whole client pipeline is two files (`EventTracker.cs`, `RaidFunnel.cs`).
- `:52-54` — `TrackUrl = "https://defenders-of-the-realm-v2.vercel.app" + "/api/events/track"`, offline queue key `"dotr-event-queue"`.
- **`:109` — `public static void Track(string eventName, object properties = null)`** — batching singleton; warns rather than throws if called before boot (`:113-115`).
- `:101-108` documents the convention: **snake_case** names (`session_start`, `wave_completed`, `purchase_completed`, `bundle_viewed`), properties as an anonymous object serialized to JSON. **The spec's names at `:1036-1064` already follow it.**
- `:123` — `EnsureExists()`, called by `GameStateService.Awake`.
- Live callers across `SceneRouter`, `GameStateService`, `EconomyService`, `AdGateService`, `PromoCodeService`, `ReferralService`, `GooglePlayStorefront`, `FoundingChoiceController`, `HonestFeedbackService`, `BarracksProgression`, `StarterSettlementCompletion`, `ResourceBuildingProgression`, `StructureContentWarmer`.

### 0b. Backend → `analytics_events`
`api/events/track.js:113` (`makeHandler(deps)`, DI'd for tests), identity resolution `:104-110`, IP-budget spend `:158-168`, one multi-row insert `:172-175` / `:217`. Malformed events are skipped rather than failing the batch (`:181`); surplus over `MAX_EVENTS_PER_BATCH` is dropped **and counted** (`:75-77`).

Table: `api/schema.sql:370-378` — `event_id` identity PK, `player_id TEXT`, `event_name TEXT`, `properties JSONB`, `client_ts BIGINT` (device epoch ms), **`received_at TIMESTAMPTZ` — the trusted clock**. Indexes `:381` (per-player, newest first) and `:385` (by name over time).

Backend-side events use `logApiEvent` (`api/_lib/audit.js:112`, insert `:118`), IP hashed at `:38`. Named precedents already in the table: `save_schema_version_refused`, `save_reset_accepted`, `save_accrual_reconcile`, `save_sanity_reject` (`api/game/save.js:509,522,549,607,613`).

⚠ **The same table also carries `web_trace` rows** (`api/trace.js:12-14`: *"reuses the proven analytics_events table… NO new table/migration required"*), which `api/admin/cleanup.js:79-85` prunes at `RETENTION_DAYS = 7` (`:34`). **That sweep is keyed on `event_name = 'web_trace'`** — Heartbound rows are not swept, and must not be named so they are.

### 0c. Read surfaces already exist
`api/admin/stats.js` (owner-only aggregates; `:5-6` notes *"87k+ rows already sitting in analytics_events"*), `api/admin/console.js` (the phone Command Center), `api/admin/db.js` (read-only viewer). `ANALYTICS_EXCLUDED_PLAYER_IDS` filters the owner's own rows out of stats — **relevant here, because the owner is a large SKR staker and would otherwise be a visible outlier in every Heartbound tier chart.**

### 0d. ⭐ The one privacy note, and the repo already agrees with the spec
Spec `:1084`: *"Do not put unnecessary wallet addresses into general analytics."*

⚠ On the Seeker rail **`player_id` IS the wallet** (`api/schema.sql:60`) — so the wallet is already the key on every existing row, unavoidably, because it is the identity. **The spec's qualifier is "unnecessary", and the primary key is necessary.** What must not happen is a wallet address appearing *inside `properties`* as a second copy. Say this explicitly; it is the kind of thing a later reader flags as a violation when it is actually the identity model.

---

## 1. Deliverables

### D1 — Emit the fourteen events, exactly as named (spec `:1036-1064`)
`heartbound_detected`, `heartbound_activated`, `skr_verification_success`, `skr_verification_failed`, `heart_pulse_global_detected`, `heart_pulse_player_processed`, `heart_pulse_player_deferred`, `resonance_score_changed`, `resonance_tier_up`, `resonance_tier_down`, `echo_event_generated`, `echo_event_claimed`, `echo_event_expired`, `heartbound_screen_opened`.

**Split by side:** the six that happen server-side (verification, global pulse, player processed/deferred, event generated/expired) go through `logApiEvent`; the client-observable ones (`heartbound_screen_opened`, `echo_event_claimed`, tier up/down as *seen*) through `EventTracker.Track`. ⛔ Do not emit the same event from both sides — a double-counted funnel is worse than a missing one.

### D2 — Properties, **bucketed** (spec `:1068-1090`)
tier, **effectiveStake bucket**, **actualStake bucket**, **streak bucket**, event type, player level, account age, source platform, processing duration, RPC latency. ⭐ The spec asks for *buckets*, not raw amounts — that is a deliberate privacy choice and it also makes the charts readable. Define the bucket edges in config, once.

### D3 — A documented answer path for the spec's ten questions (`:1096-1100`)
Not a dashboard build — a written note saying which query answers each of the ten, so the analytics are *usable* rather than merely *present*. Question 9 (*"Are whales gaining disproportionate gameplay advantage?"*) is the one that feeds WO-1682's ceiling, and it is the reason this ticket should land **early**, not last.

### D4 — Keep the owner out of the aggregates
Confirm `ANALYTICS_EXCLUDED_PLAYER_IDS` covers the owner's staking wallet before the first Heartbound chart is read (§0c).

---

## 2. Acceptance

1. All fourteen events land in `analytics_events` with the exact spec names — proven by a row query on a real run, quoted in the RESULT, not by reading the call sites.
2. No event is emitted from both client and server (D1).
3. `properties` carries **no wallet address as a field** (§0d).
4. Amounts are bucketed, and the bucket edges live in config.
5. No Heartbound event is named `web_trace` or otherwise caught by the 7-day retention sweep (§0b).
6. If any `.cs` was touched: `COMPILE_GATE_OK`, `gate_brace.py` clean, zero NUL bytes.
7. `**Status:**` flipped in this file in the same commit as the work; `.RESULT.md` written; both paths reported.

---

## 3. Dependencies

- **Blocked by:** nothing structural — but there is nothing to *emit* until WO-1674/1675/1677 exist. **Instrument as each lands**, rather than as a final pass: spec `:1035` says *"Instrument Heartbound from day one"*, and CLAUDE.md §12 makes that binding rather than advisory.
- **Blocks:** nothing. **Feeds** WO-1682 (the whale-advantage question is the ceiling's evidence).

---

## 4. OWNER QUESTIONS

**None.** The pipeline, the table, the naming convention, the retention policy and the exclusion list are all determined and were read at source. ⚠ One adjacent item is already open elsewhere and is noted so it is not rediscovered: whether a **Play-artifact** client emits any Heartbound event at all follows from **WO-1681 Q-PLAY** — if Heartbound does not exist on Play, `heartbound_screen_opened` cannot fire there, and no separate ruling is needed.

---

## 5. What NOT to touch

- ⛔ **`api/events/track.js`'s validation order.** `:143-152` — every free validation happens **before** the IP budget is spent. Adding a check after the spend inverts a deliberate ordering.
- ⛔ **`api/_lib/ip-budget.js`'s scopes** or the guest rate limiter (`api/_lib/wallet-auth.js:211-212`, `guest_rate_limit`, `api/schema.sql:333`).
- ⛔ **`api/admin/cleanup.js`'s `web_trace` retention sweep.** §0b — do not widen it to Heartbound rows, and do not name a Heartbound event so it is caught by it.
- ⛔ **A new analytics table.** `api/trace.js:12-14` states the house position: reuse `analytics_events`, no new table, no migration. Two pipelines already share it and that is the pattern.
- ⛔ **A wallet address inside `properties`.** §0d.
- No gameplay, no economy, no UI, no `.unity` scene files, no `SaveSchema` change.

---

## 6. Evidence index (opened 2026-09-10 at `dev` `abbeb9362`)

`Assets/_Modules/Core/Analytics/EventTracker.cs:52-54,101-108,109,113-115,123`
`api/events/track.js:75-77,104-110,113,120-124,143-152,158-168,172-175,181,217`
`api/schema.sql:60,333,370-378,381,385`
`api/_lib/audit.js:38,112,118`; `api/game/save.js:509,522,549,607,613,753-755`
`api/trace.js:12-14,35-37,123-137`; `api/admin/cleanup.js:34,79-85`
`api/admin/stats.js:5-6,59,62`; `api/admin/console.js:66,203`
`api/_lib/ip-budget.js:70`; `api/_lib/wallet-auth.js:211-212`
