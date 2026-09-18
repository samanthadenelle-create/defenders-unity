# WO-1847 — Clan system, step 4: Cherry Chat embed integration

**Status:** DONE - committed 4fe4535, gated (COMPILE_GATE_OK/REGRESSION_OK or node --test as applicable). PRIOR: READY FOR LEAD REVIEW

*(PRIOR STATUS: BLOCKED pending WO-1845, which has since landed — `clans`/`clan_members`
committed, so the `clan_id` this ticket scopes a room to exists.)*

## Context — clan WO-4 in the chain, depends on WO-1845

Source material: `docs/SKR Integtration.md` (DeepSeek's original draft, "WO-4"),
`docs/CLAN_WORK_ORDERS_PROOFING_2026-09-17.md` (three research passes on the real Cherry SDK — read
in full before starting, the draft's original embed-shape/wallet-bridge assumptions were wrong and
corrected there). Depends on WO-1845 (`clans`/`clan_members` tables, so a `clan_id` exists to scope
a room to).

## Owner ruling — ship decision, revised under the Root Network lens (2026-09-17)

> Cherry chat ships with free text enabled for the hackathon demo. The pre-ship rate-limit gate is
> waived because (a) there are no real users, (b) the demo needs free text for legibility, and (c)
> the fix-in-house path (client-side throttle, then server-side relay) is cheap and deferred until
> real users exist. This is a documented departure from WO-1265's preset-phrase ruling, made
> explicitly because that ruling was written for a shipped live game and this is a pre-revenue
> hackathon demo. Revisit before any public launch with real users.

This supersedes the earlier "do not finalize until Cherry confirms rate limits" gate. Cherry's own
rate-limit policy for embedded room chat remains genuinely undocumented (confirmed after an
exhaustive search this session — no GitHub issues/discussions, no docs page, no community channel
surfaced anything). The owner has made the call to ship anyway, with the mitigation below, rather
than wait indefinitely on an answer that may never come from a small external team.

## Scope

Replace `ClanChatPanel.cs`'s native UI with a WebView hosting Cherry's embed SDK
(`@cherrydotfun/chat-embed-sdk`). Use `roomId` = the clan's UUID (`clans.id`) for real per-clan room
isolation — confirmed native to the SDK, not a workaround. Default to Cherry's **wallet-only auth
mode**, where the embedded iframe handles wallet connection and signing entirely itself — this
eliminates the signature-bridging risk outright rather than needing to guard it, since the game's
own wallet adapter is never asked to sign anything on the embed's behalf.

Add the minimum reporting path specified by the owner ruling, satisfying WO-1265's underlying
intent (moderation/reporting must exist before free text) structurally, even without a review
tool yet:

**Minimum reporting path:** a "report message" action in the chat panel that writes
`{ reporter_wallet, message_id, clan_id, reported_at }` to a new `clan_reports` table. No admin
view in this ticket (deferred, matching the already-deferred admin clan-health ticket). The table
exists so the ruling's condition — moderation and reporting exist — is structurally true even
though the review tooling comes later.

## Non-scope

- No native chat rendering. `ClanChatPanel.cs` becomes a thin WebView host.
- No server-side message moderation or storage beyond the `clan_reports` table above. Cherry owns
  message persistence and delivery.
- No message read endpoints on the game backend. Cherry's SDK surfaces messages directly.
- No fallback native chat. If Cherry fails to load, show an error state, not the old UI.
- No admin review tooling for `clan_reports` — that table is written to, never read from, in this
  ticket.
- No app-trusted signature-bridge mode. Wallet-only mode is the default per this ticket; do not
  build the `signChallengeHandler`/`embedToken` path unless a later ticket explicitly asks for it.

## Client changes

**`ClanChatPanel.cs`:**
- Remove all message rendering, phrase catalog, and ring-buffer logic.
- On enable, instantiate a WebView. **Confirm which WebView plugin this project already uses before
  writing integration code** — Unity does not ship a built-in WebView; check for `unity-webview`,
  Vuplex, or a custom plugin already present in the project.
- Cherry's SDK is browser-only and cannot load directly inside a bare native WebView with no
  intermediate page — it requires a small host HTML page loaded inside the WebView (Cherry's own
  docs provide React Native/Flutter examples of this exact pattern; adapt the same shape for
  Unity's WebView). Scope real time for this — it is a real, documented, but non-trivial piece of
  work, not a one-line URL load.
- Initialize the SDK with `appId` (one per game, confirm/obtain from Cherry's portal) and
  `roomId` = the clan's UUID.
- Add the "report message" button/action wired to a new backend endpoint (below).
- Register a message listener only for presence/unread-count updates shown in the HUD, not for
  rendering — Cherry's own UI renders messages.

**`ClanChatPanelBootstrap.cs`:** Keep the `ClanFeatureGate.PlayerFacingEnabled` check as the literal
first line of `SpawnInScene` — the regression lint requires this exact string and this ticket does
not change the gate.

**`ClanChatVM.cs`:** Reduce to a thin state holder: `{ walletAddress, isMounted, unreadCount }`. No
message list — Cherry owns that.

## Server changes

New migration (`api/migrations/<timestamp>_clan_reports.sql`):

```sql
CREATE TABLE IF NOT EXISTS clan_reports (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  reporter_wallet TEXT NOT NULL REFERENCES wallet_identity(wallet),
  message_id TEXT NOT NULL,
  clan_id UUID NOT NULL REFERENCES clans(id) ON DELETE CASCADE,
  reported_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS clan_reports_clan_idx ON clan_reports (clan_id, reported_at DESC);
```

`message_id` is `TEXT`, not a foreign key — the game backend does not own Cherry's message IDs and
cannot validate them against a table it doesn't have.

New endpoint `POST /api/clan/report-message` — body `{ messageId, clanId }`. Auth:
`authenticate()`. Caller must be a member of `clanId` (per `clan_members`). Insert a `clan_reports`
row. Return `{ ok: true }`. No rate limit beyond whatever generic limits already exist — this is a
low-value abuse target and not worth scoping further here.

`clan_messages` (from WO-1845) remains in the schema but is not written to by this ticket — Cherry
owns message persistence, not this game's database.

## Acceptance criteria

- [ ] With the gate open and a wallet connected, the clan chat panel loads Cherry's embed UI in a
  WebView, scoped to the clan's own room (verify via a second clan's messages never appearing).
- [ ] Cherry's wallet-only auth mode is used — verify the game's own wallet adapter is never invoked
  by the embed at any point (no `onSignChallenge`/`signChallengeHandler` wiring exists in this
  build).
- [ ] Sending a message via Cherry's UI appears in Cherry's UI on a second device within 5 seconds,
  scoped to the same clan's room only.
- [ ] The HUD shows an unread count driven by Cherry's message events.
- [ ] If Cherry's embed fails to load, the panel shows a non-blocking error and does not fall back
  to the old native UI.
- [ ] The "report message" action successfully writes a `clan_reports` row for a real message.
- [ ] The client-side `ChatPhraseCatalog` and ring buffer are no longer referenced anywhere in the
  compiled Android build (grep the build output).
- [ ] `node --test test/*.test.js` before/after counts reported for the new report-message endpoint.

## Test plan

1. Two devices, two wallets, same clan. Send a message from device A, verify it appears on device B.
2. A third wallet in a different clan. Verify it never sees the first clan's messages.
3. Kill network on device A mid-load. Verify the error state, not a crash.
4. Report a message from device B. Verify a `clan_reports` row exists with the correct clan/reporter.
5. Grep the build for `ChatPhraseCatalog` and `MessageBufferCap`. Confirm zero references in the
   wallet build.

## Rollback

Restore the previous `ClanChatPanel.cs` from version control. Drop `clan_reports` — nothing else
reads from it. `clan_messages` was never written to, so no data migration is needed either way.

## VERIFY BEFORE BUILD

- Which WebView plugin the project already uses — confirm before writing integration code.
- Cherry's exact SDK init parameters and portal signup process (`portal.cherry.fun`) — obtain a
  real `appId` before implementation, not a placeholder.
- Cherry's CSP/frame requirements for the host page, if any.
- Whether the game already has (or needs) a project-level "Cherry account" — this is likely a
  one-time setup step for the owner, not something a lane can self-serve.

## Copy rules

No investment language in the chat panel chrome or empty states. The panel is communication, not a
financial surface. If Cherry's own UI shows any token-gated room language, flag it for review
before shipping. The "report message" action's copy should be plain and non-alarming (e.g. "Report
this message" / "Reported — thank you"), not implying an immediate consequence to the reported
player, since no review tooling exists yet to act on a report.

---

## Implementation hand-back (lane, 2026-09-17)

Code-complete and self-verified. **Two external prerequisites remain** — both named in the
ticket's own VERIFY BEFORE BUILD, neither self-servable by a lane:

### 1. No WebView plugin exists in this project (verified at source, not assumed)
`Packages/manifest.json` lists no WebView package, and there is no Vuplex /
`unity-webview` / UniWebView anywhere under `Assets/` or `Packages/`. Unity ships none
built in. So the embed cannot render until a plugin is adopted — an owner/lead decision
with a real cost:
- **gree/unity-webview** — free, MIT, `.unitypackage` import. Its bridge convention is a
  `unity:` scheme navigation, which `site/clan-chat.html` already speaks.
- **Vuplex 3D WebView** — paid, per-platform; a spend decision under the standing stop-loss.

The lane did **not** hand-roll an Android `AndroidJavaObject` WebView: that is a new
native-infrastructure subsystem (UI-thread marshalling, view insertion under
UnityPlayerActivity, lifecycle, input-focus conflict with Unity), unverifiable without
Unity and a device, and smuggling it into a player-facing ticket is exactly what the
architecture rule forbids. Instead `IClanChatWebHost` is the drop-in seam: implement it,
pass it to `ClanChatPanel.Bind`, done. Until then the panel binds
`ClanChatWebHostUnavailable`, which fails loudly with reason `no_webview_plugin` and shows
the panel's error state — **which is the behaviour this ticket already specifies for a
failed embed load**, so the panel is correct today, just permanently in that state.

### 2. `CHERRY_APP_ID` is a placeholder
`site/clan-chat.html` carries `PLACEHOLDER_APP_ID_REPLACE_BEFORE_SHIP` — exactly one
occurrence, and the page refuses to mount while it is in place (error `missing_app_id`)
rather than half-loading. A real appId is minted by hand in Cherry's portal
(`portal.cherry.fun`): an external account signup. **Replacing that one constant is the
only code change left for a working embed.**

### Known gap: Cherry exposes no per-message id
Read from the SDK's own README this session: the documented event surface is
`unreadState` and `authStateChange` only. Neither carries a message id, and in
wallet-only mode the host page never sees the message stream — so there is no documented
way to obtain the id of a *specific* message. `resolveReportableMessageId()` is the single
place that decides what gets reported: it uses a real id if some event ever supplies one,
and otherwise reports `room:<clanId>@<ISO timestamp>` — an honest, actionable pointer for a
human reviewer, never a fabricated message id. Acceptance criterion 6 ("writes a row for a
real message") is therefore met as a working end-to-end report path, but the id is
room+time granularity until a per-message event is confirmed with Cherry.

### Deviations from the acceptance criteria, stated rather than quietly skipped
- **"`ChatPhraseCatalog` / ring buffer unreferenced in the build"** — achieved for the HUD
  clan files (pinned by `test/clan-chat-embed.test.js`). `ChatPhraseCatalog` and
  `ClanService` still exist in `DeNelle.Core` and still reference each other; retiring the
  Core-side local prototype is not this ticket's scope, so the criterion cannot be fully
  true here. Needs its own ticket.
- **Two-device / room-isolation / 5-second-delivery criteria** are device tests and are
  blocked on both prerequisites above. Nothing here claims them.
- **`ClanFeatureGate.PlayerFacingEnabled` stays `false`.** This ticket does not open the
  gate, and the bootstrap's gate check is untouched as the literal first line of
  `SpawnInScene` (pinned twice: `clan-chat-release-gate.test.js` and the new embed test).
- **One open wiring point:** `ClanRoomBinding.ClanId` (in `ClanChatSource.cs`) is the
  server-side `clans.id` the room is scoped by. Nothing sets it yet — the client has no
  remote clan client (Core's `ClanService` is still the local prototype and mints local ids
  that do not exist on the server; passing one would open a room no other member is in, so
  the binding refuses a non-UUID). Wiring `/api/clan/me` into a real remote clan client is a
  later ticket; it feeds this one property and nothing else changes.

### Files
Server: `api/migrations/20260917_0031_clan_reports.sql`, `api/clan/report-message.js`,
`api/_lib/clan.js` (appended `reportMessage` + 3 codes + exports), `api/schema.sql`
(appended the `clan_reports` block, CRLF preserved).
Client: `Assets/_Modules/HUD/ClanChatPanel.cs` (rewritten as a thin host),
`ClanChatVM.cs` (rewritten as a thin state holder), `ClanChatSource.cs` (new),
`IClanChatWebHost.cs` (new), `site/clan-chat.html` (new host page),
`Assets/Tests/EditMode/ClanChatVMTests.cs` (rewritten to the new shape).
Tests: `test/clan-report-message.test.js` (new, 20), `test/clan-chat-embed.test.js` (new, 13).

### Verification
`node --test` on the lane's own files: **56/56 pass**. Brace gate
`python tools/gate_brace.py` on all 5 touched `.cs`: **bad=0 of 5**; raw brace counts
balanced; **0 NUL bytes**. No Unity process was run (batchmode/CompileGate/DataRegression
held for the lead, per the concurrent-lane instruction). No DDL was fired against Neon.

⚠ **Finding for the lead before gating:** the full `node --test test/*.test.js` run shows
**12 failures that are not this lane's** — `tunables-manifest` (6), `admin.skus.view` (3),
`admin.sku.dropdown` (2), `command-center` (1). All trace to one line new in the working
tree and absent from HEAD: `api/admin/console.js:1228` (`...times are read in this
browser\'s local zone...`), whose apostrophe breaks the inline script those tests `eval`.
That file belongs to a concurrent lane and was deliberately not touched here.
