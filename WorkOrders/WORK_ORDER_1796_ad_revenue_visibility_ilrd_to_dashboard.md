# WORK ORDER 1796 — Ad revenue visibility: the ILRD money already in Neon reaches the dashboard

**Status:** READY TO IMPLEMENT
**Number:** PRE-ASSIGNED by the lead (this WO does **NOT** touch `CLI_LANES_WO_NUMBERS.md`)
**Date:** 2026-09-16
**Silo:** analytics read surface (`api/admin/db.js`, `api/admin/stats.js`, `api/admin/console.js`) + ONE client property change in `LevelPlayInitializer.cs`. No gameplay, no scene, no economy, no ad SDK wiring.
**Lane:** Monetization/Backend (§9) — file-disjoint from world, combat and HUD lanes.
**Owner ask, verbatim (2026-09-16):** *"noone is ever buying a pack and I cant figure out how to see if ads are making anything"*

---

## 0. ⛔ THE BRIEF'S PREMISE WAS WRONG, AND THIS SECTION IS THE DEVIATION NOTICE (§11B B)

The dispatch brief said *"EventTracker tracks rewarded_ad_impression / rewarded_ad_completed but no
revenue"* and asked for a **new `ad_revenue` event**. **Read at source 2026-09-16, that is false, and
implementing it as written would ship duplicated state** (the failure CLAUDE.md §2/§5/§16 each name in
their own words).

`Assets/_Modules/Village/Monetization/Providers/LevelPlayInitializer.cs:246-254` — the ILRD drain —
already does this, today, in the shipped build:

```csharp
EventTracker.Track("rewarded_ad_impression", new
{
    network = row.Network, format = row.Format, adUnit = row.Unit,
    placement = row.Placement, revenueUsd = row.RevenueUsd, precision = row.Precision
});
```

So **the revenue figure is ALREADY in Neon**, one row per impression, in
`analytics_events.properties->>'revenueUsd'`, under the event name `rewarded_ad_impression`.

**THE ACTUAL DEAD STEP: nothing reads it.** `grep -rn "revenueUsd" api/ tools/` returned **no matches**
(run 2026-09-16) — the property is written by the client and read by nothing on the server.
`rewarded_ad_impression` appears in exactly two server lines, and both only as a *name in a list*:
- `api/admin/stats.js:207` — inside `NOT_PLAY_EVENTS` (it is excluded from the "played" denominator).
- `api/admin/stats.js:183` (a comment) and `:200` (`QUALIFYING_PLAY_EVENTS`) name
  `rewarded_ad_completed`, not the impression.

No `SUM`, no `AVG`, no view, no tile. The money has been landing in the database and there has never
been a surface that adds it up. **That is the whole ticket.**

**Therefore §2 below specs the READ side first and treats a rename as FORBIDDEN**, for two proven
reasons:
1. `Assets/Editor/Regression/MonetizationActivationRegression.cs:50` requires the literal string
   `rewarded_ad_impression` **in the provider file** (`Require(provider, "rewarded_ad_impression",
   "ILRD does not reach analytics", failures)`). A rename FAILS that suite.
2. A server-only change reaches **every build already installed**, on the next API deploy. A client
   rename reaches nobody until a new APK ships and players update. This is the identical argument
   `api/events/track.js:33-36` made when it chose the server fallback for WO-1733, and it is right here
   for the same reason.

---

## 1. WHAT IS TRUE TODAY — every line read or measured 2026-09-16, nothing from a doc

### 1.1 The client rail (device only, and that is the half that WORKS)

| Fact | Proof, read at source |
|---|---|
| ILRD is subscribed **per ad instance**, in `AddRewarded` | `LevelPlayInitializer.cs:314` `ad.OnAdImpressionDataReady += OnImpressionDataReady;` |
| The callback only touches primitives and enqueues | `:258-275` → `ConcurrentQueue<ImpressionEvent>` at `:79`. Correct: the SDK raises ILRD off the Unity main thread. |
| The queue drains on the main thread in `Update`, emitting a FlowTrace line **and** the analytics event | `:236-255`. Trace shape: `ILRD network= format= unit= placement= revenueUsd= precision=` |
| Ads are **ON** by default | `Assets/_Modules/Core/FeatureFlags.cs:811` (`=> true`) and `:840` (`Get("rewardedadskip", defaultOn: true)`) |
| Only **two** network adapters exist in this build | `Assets/LevelPlay/Editor/IronSourceSDKDependencies.xml` and `ISUnityAdsAdapterDependencies.xml` — nothing else. The Unity Ads adapter pins `com.unity3d.ads-mediation:unityads-adapter:5.12.0` + `com.unity3d.ads:unity-ads:4.20.0`. |
| Three rewarded placements, no banner, no interstitial | `:71-73` / `:299-301` — `place.build.skip` (`2ibxid58jat3sxyd`), `place.harvest.doubler` (`imk56dcdi5mym2wq`), `place.daily.chest` (`it6izgx1flbj5rce`) |
| App key | `:70` — `27850b635`, Android, `com.denellestudios.echoesofelarion`. Not a secret; it ships in the APK. |

> ### ⛔ DO **NOT** "FIX" THE SUBSCRIPTION TO THE GLOBAL EVENT. The skill reference is stale for this SDK.
> `.agents/skills/levelplay-unity-integration/references/ilrd-api.md:47,122` says to register
> `LevelPlay.OnImpressionDataReady` **before** `Init()`. In the SDK this project actually has
> (`com.unity.services.levelplay@9.5.1`), that member is **`[Obsolete]`**:
> `Library/PackageCache/com.unity.services.levelplay@16215dfb563e/Runtime/Api/LevelPlay.cs:103` reads
> *"Use OnAdImpressionDataReady on each ad instance (LevelPlayBannerAd, LevelPlayInterstitialAd,
> LevelPlayRewardedAd) instead."*
> **The repo's per-instance shape at `:314` is the CURRENT, correct API.** The only theoretical loss is
> an impression on a unit that does not exist yet — and the units are created in `OnInitSuccess`
> (`:282 CreateRewardedAd()`) before any ad can load or show, so there is no window. There is no
> banner and no interstitial in this game, so nothing else can raise ILRD. **No client subscription
> change is in scope.**

### 1.2 The server rail (this is the broken half)

- `api/events/track.js` inserts every event verbatim into `analytics_events (player_id, event_name,
  properties::jsonb, client_ts)` (`:295-299`), stamping `properties._auth` (`:275`). **It has no event
  allowlist** — grepped 2026-09-16: there is no `ALLOWED_EVENTS` / name filter anywhere in the file.
  **So `rewarded_ad_impression` rows land already, and a new property needs NO server change to be
  accepted.** (The brief's item (b) — "the api track handler + schema accepting it" — is therefore
  **already satisfied**; nothing to do. Stated rather than quietly skipped, §11B B.)
- `api/admin/db.js?view=metrics` (`:166-206`) returns **counts only** — `per_event_per_day`,
  `per_day`, `trace_error_lines_per_day`. No `properties` are read except `->>'session'`. This is why
  the owner cannot see revenue: **the view she has cannot express it.**
- `api/admin/console.js` (1860 lines) is the owner's Command Center. Its `load()` at `:365-378` fetches
  five `/api/admin/stats` views (`overview`, `ops`, `purchases`, `command`, `skus`) plus tunables.
  **There is no ads view and no ad tile anywhere in it.** Tile helper: `:430-433`. Money tiles:
  `:462-522`.

### 1.3 MEASURED — `GET /api/admin/db?view=metrics`, HTTP 200, this session

Ad events, last 7 days (`per_event_per_day`):

| Day | `rewarded_ad_impression` | `rewarded_ad_completed` |
|---|---|---|
| 2026-09-16 | 4 | 2 |
| 2026-09-15 | 3 | 2 |
| 2026-09-14 | 6 | 3 |
| 2026-09-13 | 0 | 0 |
| 2026-09-12 | 1 | 0 |
| 2026-09-11 | 3 | 2 |
| 2026-09-10 | 0 | 0 |
| **7-day total** | **17** | **9** |

⚠ **THE TWO COLUMNS ARE NOT THE SAME RAIL, so the ratio is not a completion rate.**
`rewarded_ad_impression` is emitted **only** by `LevelPlayInitializer` (LevelPlay/Android).
`rewarded_ad_completed` is emitted by `AdGateService.cs:249`, which is **provider-agnostic** — it fires
for the Pi Developer Ad Network too — and its properties are `{placement, outcome, reason,
rewardApplied}`, read at `:248-254`. **There is no `network` or `provider` property on it.** So a
completion cannot today be attributed to a rail. That is a second, smaller gap and §2.4 fixes it.

**⛔ WHAT I HAVE *NOT* PROVEN:** I have not read a single `revenueUsd` **value**. No admin view exposes
`properties`, and this lane is read-only/narrow-call, so I cannot run the aggregate. **I therefore do
not claim the ILRD revenue in Neon is non-zero** — §2's first acceptance step is the query that settles
it. What IS proven: 17 impression rows exist, and the emitter that writes them writes `revenueUsd`
alongside.

---

## 2. THE SPEC

### 2.1 (a) CLIENT — one small honesty change only. NOT a new event.

File: `Assets/_Modules/Village/Monetization/Providers/LevelPlayInitializer.cs`

1. **`:267` `double revenue = data.Revenue ?? 0d;` collapses "the network reported $0" into "the
   network reported nothing".** The skill's own Common Issues section
   (`references/ilrd-api.md:246-250`) says some networks do not provide revenue. Change
   `ImpressionEvent.RevenueUsd` to `double?`, keep `data.Revenue` nullable end-to-end, and send
   `revenueUsd = row.RevenueUsd` (null when absent). The tile then prints **"NOT REPORTED"** instead of
   **"$0.00"**, which is the rule `console.js:409-412` already writes down for itself: *"a failed query
   rendered as 0 is a confident lie and this project has been bitten by exactly that."*
   The `<unknown>` sentinel at `:263` and `:270-274` stays as-is for the string fields.
2. Add two properties to the SAME `Track` call at `:246-254` — **do not add a second call**:
   - `build = Application.version` (so a revenue row is attributable to a build; the store build in
     players' hands is a different waterfall from a dev sideload),
   - `country = data.Country` and `instance = data.InstanceName`. **Both verified in the installed SDK,
     not in the skill doc:** `Library/PackageCache/com.unity.services.levelplay@16215dfb563e/Runtime/Api/LevelPlayImpressionData.cs`
     — `Country` at `:42`, `InstanceName` at `:67` (alongside `AdFormat` `:25` and `Precision` `:82`,
     which the file already uses). `InstanceName` is the field LevelPlay's own Firebase sample maps to
     `ad_unit_name`. `auctionId` is deliberately NOT added: high-cardinality, and nothing a tile shows.
3. **The event NAME does not change.** `MonetizationActivationRegression.cs:50` pins it.
4. Keep the `FlowTrace.Step` line at `:242-245` exactly as it is (§12: instrumentation is permanent,
   never stripped) and extend it with the new fields.

**What NOT to touch:** the per-instance subscription (`:314`), the thread-hop queue (`:79`, `:240`),
privacy-before-init (`:202-234`), the placement/ad-unit maps, `FeatureFlags.RewardedAdSkip`.

### 2.2 (b) SERVER INGEST — nothing to do, and here is the proof

`api/events/track.js` has no event-name allowlist and no property schema: `properties` is parsed to
JSONB wholesale at `:262-272`. `api/schema.sql` stores it as a JSONB column. **A new property needs no
migration and no route change.** Do not add an allowlist to create something to change.

### 2.3 (c) THE READ SURFACE — this is the deliverable

**New: `GET /api/admin/db?view=ads[&days=N]`** in `api/admin/db.js`, placed after the `metrics` block
(`:206`) and added to the unknown-view error string at `:549`.
⚠ **While you are on that line, note it is ALREADY stale:** it lists
`overview | players | metrics | traces | bugreports | bugreport | authrejects` and **omits `purchases`**,
which has existed since WO-1169. Add both names in the same edit — one line, and it stops the error
message lying about what the endpoint serves. Same house style as every view there:
`SELECT`-only, hard `LIMIT`, `clampLimit` on `days` (default 7, max 90), aggregates only — no raw rows.

Four aggregates. `properties->>'revenueUsd'` is TEXT in JSONB and must be cast; a row whose value is
absent or non-numeric must be **counted separately, never silently summed as zero**:

```sql
-- 1. per day
SELECT date_trunc('day', received_at)::date::text AS day,
       COUNT(*)::bigint AS impressions,
       COUNT(*) FILTER (WHERE properties->>'revenueUsd' IS NULL)::bigint AS impressions_without_revenue,
       COALESCE(SUM((properties->>'revenueUsd')::numeric), 0) AS revenue_usd
FROM analytics_events
WHERE event_name = 'rewarded_ad_impression'
  AND received_at > NOW() - (${days} * INTERVAL '1 day')
GROUP BY 1 ORDER BY 1 DESC LIMIT 90

-- 2. per network   (GROUP BY properties->>'network')
-- 3. per format + per placement  (GROUP BY properties->>'format', properties->>'placement')
-- 4. eCPM = SUM(revenue) / COUNT(*) * 1000, computed per network AND overall,
--    returned ONLY when COUNT(*) >= 20, else null with low_n:true
```

⛔ **eCPM ON 17 IMPRESSIONS IS NOISE, NOT A METRIC.** Return `low_n: true` and let the surface print
*"too few impressions to trust"* — the exact phrasing `console.js:521` already uses for the quote
funnel. A confident eCPM off a handful of impressions is the same class of lie as a failed query
rendered as 0.

⚠ **`(properties->>'revenueUsd')::numeric` THROWS on a non-numeric value** (`22P02`) and would take the
whole view down. Guard it: `CASE WHEN properties->>'revenueUsd' ~ '^-?[0-9]+(\.[0-9]+)?$' THEN
(properties->>'revenueUsd')::numeric END`. Do not use a bare cast.

**Console tile block** in `api/admin/console.js`, inside the **Sales** area so pack income and ad income
sit on ONE screen (the owner's literal ask):
- Add `getJson('/api/admin/db?view=ads&days=' + d)` to the `Promise.all` at `:365-378` and
  `state.ads = ...` with the same `read_ok` / `COULD NOT READ` discipline as `:378-392`.
  ✅ **`getJson` ALREADY sends the admin key** — `console.js:317-320`
  `fetch(url, { headers:{ 'X-Admin-Key': READ_KEY } })` — so it reaches `/api/admin/db`
  (`db.js:40-46`) with no change, even though the five existing calls happen to target
  `/api/admin/stats`. **This was checked so the implementing seat does not have to: put the view in
  `api/admin/db.js` and nowhere else.** The SQL lives in exactly one file.
- Tiles, via the existing `tile(k,v,s)` helper at `:430-433`:
  `Ad revenue, window` · `Impressions` · `eCPM (or "too few to trust")` · `Rewards delivered`,
  plus a small per-network table.
- Put them adjacent to the existing `Settled revenue` / `Quotes issued` tiles (`:462-522`), so the
  screen reads: packs sold **0**, ad income **$x**.
- **No colour carries meaning** (owner is red/green colourblind — memory
  `owner-colorblind-delegate-visual-creative`). Every verdict is a word.

### 2.4 (d, second half) Attribute the completion

Add `provider = AdServices.Current.ProviderName` to the `rewarded_ad_completed` payload at
`AdGateService.cs:249-254`. Verified at source: `Assets/_Modules/Core/Ads/IAdService.cs:180`
`public static IAdService Current => s_current ?? NullAdService.Instance;` — it **never returns null**,
so the read is safe; keep `?.` anyway per §10 checklist. `ProviderName` is on the interface
(`LevelPlayInitializer.cs:114` returns `"UnityLevelPlay"`; `PiAdProvider` has its own). One property,
one line, and the impressions-vs-completions ratio stops being cross-rail.

---

## 3. (d) THE OWNER-FACING ANSWER FOR **TODAY** — before any of the above ships

### 3.1 FACTS — the owner's own LevelPlay Home dashboard, 30 days to 2026-09-15 (supplied by her)

- **LevelPlay revenue: $0.19 total.**
- **All of it is ironSource Ads ($0.19). Unity Ads: $0.00.** Tapjoy Offerwall: "coming soon".
- Daily peaks ~**$0.02**, on Aug 22-26 and Sep 6-8. Near zero otherwise.
- One app: `com.denellestudios.echoesofelarion`. Ad Quality: 0 notifications. No UA spend.

**That is the number, and it is consistent with what we measure.** 17 impressions in 7 days at ~$0.02
on a good day is the same order of magnitude. **Ads are serving and they are earning — just $0.19 a
month.** Nothing is broken; the volume is tiny.

Two further facts, not opinions:
- **Only ironSource can earn today, and that is partly a BUILD fact, not only a dashboard fact.** This
  project ships exactly two adapters (§1.1): the ironSource SDK and the Unity Ads adapter. Unity Ads
  reading $0.00 has two candidate causes — no Unity Ads instance enabled in the LevelPlay waterfall for
  these three ad units, or enabled but never winning/filling. **I cannot tell which from here**: the
  waterfall is dashboard state, there is no waterfall config in the repo, and no code names a network.
  The one cheap check is in §3.3.
- **Adding any OTHER network (AppLovin, AdMob, Meta, Vungle, Mintegral) needs BOTH halves:** an instance
  in the LevelPlay dashboard **and** an adapter `*Dependencies.xml` under `Assets/LevelPlay/Editor/` +
  a resolve + a new build. Neither half alone does anything. There are no other adapters in the tree.

### 3.2 RECOMMENDATIONS (clearly separated, per the brief — these are judgement, not measurement)

- **The lever is impression VOLUME, not eCPM.** 17 impressions in a week cannot be fixed by a better
  waterfall. At any plausible rewarded eCPM, revenue is `impressions × pennies`. More networks would
  raise fill and eCPM by some percentage of ~$0.19/month.
- So the monetisation question is a **design** one: how many rewarded offers does a session present,
  and are they in front of the player at the moments she wants them (build-skip, harvest doubler, daily
  chest are the three that exist). **That is out of this WO's scope** and belongs with the pack-conversion
  work (§4, parked).
- Enabling more networks is cheap and worth doing **eventually**; it is not this week's lever.

### 3.3 Where she sees revenue now, and the one owner action

**PROVEN identifiers** (read at source, so she does not have to hunt): app key **`27850b635`**, app
`com.denellestudios.echoesofelarion` (Android), mediation platform = **LevelPlay** (Unity), ad unit ids
`2ibxid58jat3sxyd` / `imk56dcdi5mym2wq` / `it6izgx1flbj5rce`.

⚠ **UNPROVEN, marked as such (§11B A):** I have **not** opened the LevelPlay dashboard and cannot state
its menu path. The brief's *"Monetization > Reports"* is **not verified by this lane** — do not print it
to the owner as fact. She has already found the Home revenue chart (§3.1), which is the number that
matters; the per-network / per-ad-unit breakdown lives in the same product and she can reach it from
there. **The one check worth her 30 seconds:** open the waterfall for these three ad units and say
whether a **Unity Ads instance** is enabled at all. That single answer closes the §3.1 ambiguity.

### 3.4 The 09-15 handover item — it does **not** currently suppress revenue

`docs/handoffs/SESSION_HANDOVER_2026-09-15_play_track_and_wall_fork.md:62` (read this session):
> *"**LevelPlay:** set Store availability → "Not live yet" until the Play listing is public (404s
> today)."*

**Assessment, with the evidence:**
- **Ads ARE serving now.** 17 impressions and 9 rewards in the last 7 days (§1.3), and $0.19 of real
  ironSource revenue in 30 days (§3.1). So the app is **not** currently blocked from fill.
- `LevelPlayInitializer.cs:224-232` records why: the app is set to **Temporary** in the dashboard and
  *"live inventory is enabled manually by LevelPlay support"* — the store has no https listing URL for
  auto-verification. That manual enablement is what the impressions prove.
- **The RISK the handover item guards is the opposite direction:** a Store-availability field pointing
  at a **404 Play listing** is a broken app record, and a Temporary/manually-enabled app can be
  re-reviewed. Setting it to "Not live yet" is the honest state and protects the account.
  ⚠ **UNPROVEN (§11B A):** I have read nothing about how LevelPlay's demand partners treat a
  non-resolving store URL. Do **not** tell the owner it suppresses bidding — the recommendation stands
  on account honesty alone, which is reason enough.
- **Net: it is an account-hygiene item, not a revenue blocker today.** Do it; expect no change to the
  $0.19. ⚠ I have **not** measured any fill change attributable to that field and do not claim one.

---

## 4. PACK CONVERSION — MEASURED FUNNEL, FACTS ONLY. **WORK IS PARKED.**

`GET /api/admin/db?view=metrics` + `?view=purchases`, both HTTP 200, this session.

**The funnel, last 7 days (2026-09-10 → 09-16):**

| Step | Emitter (read at source) | 7-day total |
|---|---|---|
| `store_opened` | `Wallet/PackStore.cs` (OnEnable) | **34** |
| `bundle_viewed` | `PackStore.cs` | **426** |
| `pack_tapped` | `PackStore.cs` (FocusPack) | **42** — all on 09-10, **zero since** |
| `checkout_started` | `PackStore.cs` (Purchase head) | **5** — all on 09-10 |
| `checkout_failed` | `PackStore.cs` (TrackCheckoutFailed) | **2** — both on 09-10 |
| `purchase_completed` | `PackStore.cs:2632/3128` | **0 — the event does not appear at all in 7 days** |

Adjacent Pi-rail audit rows (server-written, `api/_lib/audit.js:118` inserts into `analytics_events`):
`pi_quote_issued` 11 on 09-10 + 1 on 09-15; `pi_payment_lookup_failed` **17** on 09-10;
`pi_payment_approved` 1 and `pi_entitlement_created` 1 on 09-10.

**THE FIRST ZERO IS `purchase_completed`, and the second is `pack_tapped` after 09-10.** 426 bundle
views produced 42 taps (09-10 only) and 5 checkout attempts (09-10 only). Nobody has tapped a pack in
six days.

**Lifetime ledger** (`purchase_entitlements`, via `?view=purchases`): **4 rows total.**
- 3 `fulfilled` — 2026-08-23 00:09Z (`hearth-spark`, devnet, `usd_anchor` NULL), 08-24 17:45Z
  (`impulse-iron-medium`, mainnet-beta SKR, $2.99), 08-25 02:45Z (`impulse-wood-medium`,
  mainnet-beta SKR, $2.99) — **all three the same wallet** `CHKKFkPGz8VZ…`.
- 1 `verified`, never fulfilled — 2026-09-10 23:42Z, `hearth-spark`, Pi, $4.99. **That row is
  WO-1797**, and it is a ledger defect, not a lost sale.
- `amount_mismatches: 0`.

`SESSION_HANDOVER_2026-09-15_play_track_and_wall_fork.md:37`: *"The **"2 paying buyers / $10.97" are
BOTH the owner** (one SKR, one Pi). External revenue is ZERO."*

> ### ⛔ CONVERSION WORK IS **PARKED** — DO NOT START IT FROM THIS WO.
> The 09-03 → 09-16 window is the hackathon video plus a bug queue; the store has been a low-traffic
> side surface and the numbers above measure that, not a pricing or copy failure. Redesigning packs off
> a 5-checkout sample would be guessing. The owner has separately pre-assigned **WO-1798** for
> *"how do we make the packs better"* (`CLI_LANES_WO_NUMBERS.md:193-195`) — the funnel above is the
> input to that ticket. **Nothing in §4 is in scope here.** This WO ships ad-revenue VISIBILITY only.

---

## 5. ACCEPTANCE — the chain must be walked end to end, in this order

1. **PROVE THE DATA IS ALREADY THERE (do this FIRST — it may change the tile's copy).** After the view
   ships, `GET /api/admin/db?view=ads&days=30` and read `revenue_usd` +
   `impressions_without_revenue`. Record both numbers verbatim in the `.RESULT.md`.
   - If `revenue_usd > 0`: the whole gap was the read surface. Say so.
   - If `revenue_usd = 0` and `impressions_without_revenue = impressions`: the ILRD payload is
     arriving with a null `Revenue`, the `?? 0d` at `:267` was hiding it, and §2.1's nullable change is
     what surfaces it. Say that too — **do not report either outcome as the expected one.**
2. **Device capture → row → tile, one continuous chain.** On the Seeker, watch one rewarded ad. Then:
   - `adb logcat` shows `[Flow:LevelPlay] ILRD network=… revenueUsd=… precision=…`. **Paste the line.**
     (⚠ use `adb logcat -g` first — memory `logcat-ring-buffer-destroys-evidence`.)
   - `?view=metrics` shows that day's `rewarded_ad_impression` count **incremented by one**.
   - `?view=ads&days=1` shows `impressions` incremented and the network named.
   - The console Sales area renders the ad tiles with that figure. **Open the screenshot.**
3. `COMPILE_GATE_OK` + `tools/gate_brace.py` on the touched `.cs` before the gate (§1).
4. `REGRESSION_OK <n>/<n>` on a **fresh** log, and specifically `MonetizationActivationRegression`
   **green** — it pins both `rewarded_ad_impression` in the provider and `rewarded_ad_completed` in the
   gate (`:49-50`).
5. **A non-numeric `revenueUsd` must not 500 the view.** Prove the CASE guard: the view still answers
   200 when the regex rejects a value. (An `_raw`-wrapped property from `track.js:268` is the real
   shape that would do this.)
6. **No colour-only state** on the new tiles; every verdict is a word.
7. Board: flip this WO's `**Status:**` line and write the `.RESULT.md` in the SAME commit as the work;
   regenerate `BOARD.html` (`python tools/board_build.py`).

## 6. ⚠ COORDINATION — TWO OTHER LIVE TICKETS TOUCH THE SAME TWO FILES

- **WO-1793** (`WORK_ORDER_1793_admin_db_cannot_read_any_event_payload_so_424_breaks_are_invisible.md`,
  READY) adds a **generic payload-reading view to `api/admin/db.js`** and its §1 explicitly names
  `rewarded_ad_*` among the events no view can read. **Read it before writing §2.3.** If its generic view
  lands first, `?view=ads` should be the *aggregate* layer on top of it, not a second raw reader — and
  in either order there must be **one** `properties`-casting guard helper, not two. Same
  duplicated-state rule as everywhere else in this file.
- **WO-1797** (the Pi `verified`-forever ledger defect) edits the `view === 'purchases'` block of
  `api/admin/stats.js` and adds a `pi_payments` probe to `api/admin/db.js:96-119`. **Disjoint blocks in
  the same two files.** Per §11 the lanes must not hold these files simultaneously: run them
  sequentially, or let ONE seat carry all the `api/admin/*` edits. The lead batch-gates and commits by
  explicit path.
- Useful precedent from WO-1793: that triage read Neon directly with `DATABASE_URL` from `.env.local`,
  SELECT-only. If acceptance step 1 needs the `revenueUsd` aggregate **before** the view ships, that is
  the sanctioned way to get it.

## 7. WHAT NOT TO TOUCH

The ILRD subscription shape (`:314`) · the background-thread queue · privacy-before-init (`:202-234`)
· the app key or the three ad unit ids · `FeatureFlags.RewardedAdSkip` · `api/events/track.js` (no
allowlist, nothing to add) · the `rewarded_ad_impression` / `rewarded_ad_completed` event NAMES ·
`purchase_entitlements` or anything on the money rail (that is WO-1797) · pack pricing, copy or the
store funnel (WO-1798) · the LevelPlay dashboard (owner-only, §3.3).

---

**Prepared by:** read-only spec/RCA lane, 2026-09-16. Every path, line number and figure above was
opened or measured in this session; the two things I could not prove are labelled as such in §1.3 and
§3.3.
