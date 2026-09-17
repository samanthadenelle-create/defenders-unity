# LIVE PLAYERS TRIAGE — 2026-09-16 (Solana dApp Store, production)

**Lane:** read-only triage (no code, no commits, no Unity, no DB writes).
**Measured:** 2026-09-16, all windows **UTC** (`date_trunc('day', received_at)` — the owner's local
"today" will not line up exactly at the edges).
**Sources:** `GET /api/admin/db?view=metrics|authrejects` (production), plus **direct read-only
SELECTs against the production Neon DB**.

> ### ⚠ DEVIATION FROM THE BRIEF, DECLARED (CLAUDE.md §11B.B)
> The brief named the admin API. **No admin view can read an event's PAYLOAD**: `metrics` returns
> counts only, `traces` is hardcoded to `event_name='web_trace'` (WebGL-only, and
> `distinct_trace_sessions = 0` today because every player is on Android), and `authrejects` reads
> three event names. So questions 1 and 2 are **unanswerable through the sanctioned surface**. They
> were answered with SELECT-only queries through `@neondatabase/serverless` using `DATABASE_URL`
> from `.env.local`. No writes, every query limited, no secret written anywhere.
> **Same-DB proof:** the direct query returned **713 events / 25 distinct players** for
> 2026-09-16 — byte-identical to the `view=metrics` `per_day` row. The gap itself is ticketed as
> **WO-1793** so the next seat needs no credentials.

---

## 0. HEADLINE — the numbers do not mean what they look like

| The reported number | What the data says it actually is |
|---|---|
| 22 new players / 25 distinct player ids | **~14-15 humans.** Each signed-in player appears as TWO ids (a `guest-local-*` and a wallet), and one id (`unverified`) is a SHARED BUCKET holding 520 of the day's 713 events across at least four build versions. → **WO-1791** |
| `playtest_break 424` | **330 errors, 74 scene-load bookkeeping rows, 11 notes, 8 softlocks, 1 idle.** 327 of the 424 are unattributable. |
| `tutorial_completed 8` | **At least 6 are SKIP-ALL outs** — `tutorial_completed` fires in the same second as `tutorial_skipped_all`, `skips:0`, 8-33 s elapsed. Genuine completions today: **2**. → **WO-1794** |
| `raid_funnel_barracks_unlocked 8 / army_trained 8` | **8 grant side-effects.** Both carry `source:"StarterArmyGrant"`/`"starter-army"` and fire in the same second as `founding_path_selected`. → **WO-1794** |
| `save_reset_accepted 7` | **No town was lost.** 6 of 7 hit a `player_data` row created in the same second; the 7th carried a non-null `from`. → **WO-1795** |
| `api_auth_reject 7` | **All `AUTH_SESSION_EXPIRED` on `/api/auth/session`, 2 wallets, in retry bursts.** No save/load rejection at all today. |

---

## 1. THE FUNNEL FOR TODAY'S NEW PLAYERS

### 1a. Per identity-group (the closest thing to per-player that the data allows)

Ordered by first event. `S`=session_start, `T`=tutorial_started, `Sk`=skipped_all,
`C`=tutorial_completed, `F`=founding_path_selected, `B/A`=raid_funnel barracks/army,
`R`=first_raid_attempted.

| first seen | identity | S | T | Sk | C | F | B/A | R | furthest scene reached |
|---|---|---|---|---|---|---|---|---|---|
| 00:05 | `unverified` (**multiple humans**) | Y | Y | Y | Y | Y | Y | **no** | town, waves, store |
| 10:07 | `guest-local-eaf246c7` | Y | – | – | – | – | – | – | town (1 wave), enemy art stuck downloading |
| 14:00 | `guest-local-27a0353d` → `A2gx78TqNt` | Y | – | – | – | – | – | – | HeroSelect; then wallet-only server rows, save updated 14:16 |
| 14:31 | `guest-local-520d82de` → `325h1eAqSS` | Y | – | – | – | – | – | – | HeroSelect; wallet connect **TIMED OUT after 30 s** |
| 16:22 | `guest-local-15a0732f` | Y | – | – | – | – | – | – | **Title only** |
| 16:28 | `guest-local-0821c1bb` → `9tKB3Qqkfe` | Y | Y | Y | Y | – | – | – | tutorial, skipped at `founding_greet` after 10.4 s |
| 16:53 | `guest-local-782feaa2` → `EFhRvgYeNQ` | Y | – | – | – | – | – | – | HeroSelect |
| 17:00 | `guest-local-aaf35027` → `BBJLKxyhav` | Y | – | – | – | – | – | – | HeroSelect; 3x `AUTH_SESSION_EXPIRED` |
| 18:07 | `CHKKFkPGz8` (row from 2026-08-27 — returning) | Y | – | – | – | – | – | – | town, waves, SKR verified, 4x `AUTH_SESSION_EXPIRED` |
| 18:49 | `guest-local-92be2871` → `6Mrx27CdmV` | Y | Y | – | – | – | – | – | tutorial step 1 |
| 18:52 | `guest-local-ca8cbd95` | Y | – | – | – | – | – | – | Title → HeroSelect |
| 18:54 | `3xLP7UyxD6` | – | Y | – | – | Y | Y | **no** | town; `tutorial_step_drop` |
| 19:06 | `guest-local-4b2fc333` | Y | Y | – | – | – | – | – | tutorial step 1 |
| 19:13 | `DKYiurDc3L` | Y | Y | – | – | Y | Y | **no** | town; `tutorial_step_drop` |
| 19:41 | `CMV5WumRnsvF` | Y | Y | – | – | – | – | – | tutorial; SKR verified |
| 19:55 | `guest-local-ac89679f` | Y | – | – | – | – | – | – | **Title only** |
| 20:08 | `guest-local-f6035f25` | Y | Y | Y | Y | – | – | – | skipped at `founding_greet` after 28.5 s |
| 21:02 | `guest-local-76db9270` → `2ZurmHauf4` | Y | Y | Y | Y | Y | Y | **no** | town; skipped after 11.2 s |

### 1b. Aggregate, and where they drop

```
session_start            29 events / 14 guest-or-wallet ids + the unverified bucket
tutorial_started         14 events /  9 ids          <-- FIRST BIG DROP
tutorial_skipped_all      6 events /  4 ids
tutorial_completed        8 events /  4 ids  (>=6 of them are the same-second skip, see §0)
founding_path_selected    8 events /  4 ids          <-- SECOND BIG DROP
raid_funnel_barracks       8 events /  4 ids  (grant side-effect, not an action)
raid_funnel_army           8 events /  4 ids  (grant side-effect, not an action)
raid_funnel_first_raid_attempted   0 events / 0 ids  <-- ZERO. NOBODY RAIDED.
wave_completed           48 events (defence, not raids)
```

- **Drop 1 — boot → tutorial.** 9 guest ids emitted `session_start` + `scene_loaded Title`
  (7 of them also `HeroSelect`) **and then nothing**. Verified per id: their only other event is
  `playtest_break`. **Five of those nine are paired** to a wallet id appearing 1-24 s later that
  carries only SERVER-written rows (`session_issued`, `save_reset_accepted`) — `27a0353d`,
  `520d82de`, `782feaa2`, `aaf35027`, `76db9270` — i.e. they *did* continue and their client events
  went into the `unverified` bucket. **The drop is partly real and partly WO-1791.** The other
  **four** (`eaf246c7`, `15a0732f`, `ca8cbd95`, `ac89679f`) have no wallet pair at all; three of
  those never got past Title/HeroSelect.
- **Drop 2 — founding → raid.** Four ids reached founding. **Zero launched a raid.** Over the whole
  7-day window `raid_funnel_first_raid_attempted` fired **3 times** (2 on 09-15 by one id, 1 on
  09-11 by one id). The raid on-ramp is not being taken by new players at all.
- **Skip-out is the dominant tutorial ending:** every skip today came from `founding_greet` (index 0)
  or `founding_walk` (index 1), after 6.9-33 s.

### 1c. What is NOT instrumented (so it cannot be answered today)

- **No `hero_selected` event.** HeroSelect is visible only as a `scene_loaded` break record. Whether
  a player chose a hero or backed out is unknowable.
- **No sign-in / wallet-connect outcome event**, and no alias event when the guest id is replaced by
  a wallet id. → WO-1791.
- **No raid *entry attempt* below `first_raid_attempted`** — that step is a per-install PlayerPrefs
  latch (`RaidFunnel.cs:98-105`), so a returning player who raids again fires nothing, and a player
  who opened the raid map and bounced fires nothing either. There is no `raid_selected`,
  `raid_deploy`, `raid_abandoned` or `raid_settled` event anywhere: `grep` of `api/` and
  `Assets/_Modules` finds exactly six `raid_funnel_*` names (`RaidFunnel.cs:79-94`) and no other
  raid analytics.
- **No app version on any event but `session_start`** (`BreakRecord` =
  `kind/message/stack/scene/t/utc`, `BreakCaptureHarness.cs:1141-1150`). Build attribution is a
  per-player join and is ambiguous for anyone who booted two builds.
- **No session-length / quit event.** Retention is inferred from save `updated_at` only.

### 1d. Build versions in play today (from `session_start`)

| appVersion | sessions | ids |
|---|---|---|
| `2026.09.16.371701` | 13 | 10 |
| `2026.09.07.359722` | 13 | 7 |
| `2026.09.16.371627` | 3 | 1 |
| `2026.09.07.359670` | 2 | 1 |
| `2026.09.15.371500` | 1 | 1 |

All Android. No WebGL, no Windows.

**The version split IS the attribution split.** Every `session_start` that landed `unverified` today
came from `2026.09.07.359722` (7), `2026.09.16.371627` (3), `2026.09.15.371500` (1) — plus exactly
one from `2026.09.16.371701` at 17:48:13. Every `_auth:'guest'` / `_auth:'session'` row came from
`2026.09.16.371701`; every `_auth:'guest-body'` row (the WO-1733 server fallback) came from
`2026.09.07.*`. So **WO-1735's identity attach works on the new build** and the bucket is dominated
by an installed cohort no client change can reach. Read at source:
`EventTracker.SendBatch` (`Assets/_Modules/Core/Analytics/EventTracker.cs:293-333`) already calls
`BackendRequestSigner.TryAttachCachedSession`.

---

## 2. `playtest_break` 424 — WHAT THEY ARE

`BreakCaptureHarness.Record` sends **every** record through `EventTracker.Track("playtest_break", …)`
(`BreakCaptureHarness.cs:1153`) — the `session_start`/`scene_loaded`/`note` filter applies only to
the snapshot dump and the console line, **not** to the telemetry. So the count is not 424 errors.

| `kind` | hits | distinct ids |
|---|---|---|
| `error` | **330** | 14 |
| `scene_loaded` | 74 | 19 |
| `note` | 11 | 2 |
| `possible_softlock` | 8 | 1 |
| `idle` | 1 | 1 |

### 2a. The 330 errors, grouped by message prefix

| hits | ids | prefix | scenes | verdict |
|---|---|---|---|---|
| 156 | 1 (`unverified`) | `[Flow:RaidArt]` | `RaidBase_IronBastion`, `RaidBase_raider_camp_small` | → existing **WO-1747 / WO-1758** (arena boundary ring, "URP shader with NO ALBEDO bound and a light tint (flat grey slab)", `#1`-`#11`). One unattributed id, raid scenes, matching the owner's own Bastion run today (`Logs/device/pull-20260916-143101-bastion-owner-run`). |
| 68 | **9** | `[Flow:TripoMatFix]` | `Main_Castle_Overworld` | → **NEW: WO-1792.** `Jeweler` renderers `Glass`/`Icon`/`Rim` have no albedo and no fallback tint, on `2026.09.16.371701`. 51 of the 68 name `Jeweler` explicitly. |
| 43 | 1 | `RemoteProviderException` / `OperationException` / `System.Exception:` / `ResourceManagerException` | `Title` | **NOT a missing R2 push.** Every line reads `UnityWebRequest result : ConnectionError : Cannot connect to destination host`, 10:30:15-10:30:29Z, one device — not `404`. Bundles named: `structure_art_assets_structures_arcanetower1_…`, `arcanespire_2/_3/_11`, `…_albedo_…`, `armorer1_…`. No ticket; device connectivity. |
| 18 | **10** | `[Flow:StructureAssets]` | `Title` | → existing **WO-1709** (its Addressables-INIT half is still `READY`). Verbatim: *"Addressables INIT handle is INVALID after 2.6-3.4s: something else owns the shared initialisation operation and released it on completion"*. Ten distinct ids, all `2026.09.16.371701` — **the widest-reach break of the day**. |
| 8 | 1 | `[Flow:Manage]` | `Main_Castle_Overworld` | Queue row label `'Polish:ing Rough Stone:1'` for job `polish:ing_rough_stone:1` — *"the player is reading…"*. Looks like a **regression of WO-1564** (`…research_picker_orphans_a_school_and_the_queue_prints_raw_ids`, RESULT on disk). Recommend the lead re-open 1564 rather than mint a sixth number; 1 unattributable id, no second witness. |
| 7 | **5** | `[Flow:Tutorial]` | `Main_Castle_Overworld` | 5 hits / 3 ids `SKIP_TOP_HIT_BLOCKED top=ObsidianPanel` (the skip tap is being eaten by the Obsidian panel) → maps to **WO-1090**'s SKIP_TOP_HIT family; 2 hits / 1 id `STEP-STUCK :: founding_walk — no 'hero.reached:guide_gate' after 120s`. |
| 5 | 2 | `[Flow:Quiescence]` | town | no player-visible symptom reported; no ticket. |
| 4 | 2 | `[Flow:EnemyAssets]` | town | *"enemy asset 'Enemies/Orc_Warrior' … NOT-YET-DOWNLOADED for 37.0s"* — slow R2 fetch on the same 10:07Z session, same connectivity story as the 43 above. |
| 3 | 1 | `[Sync]` | `HeroSelect` | unattributed; no ticket. |
| 3 | 2 | `[Flow:Wallet]` | `Title` | *"Connect TIMED OUT after 30.0s … no wallet app installed, or the handshake was never answered"* — one of them is `guest-local-520d82de`, who then dropped at HeroSelect. Worth watching; not enough witnesses for a ticket. |
| 2/2/2/1/1 | 1 each | `[Flow:VisualFactory]`, `[Flow:AudioAssets]`, `[Flow:Waves]`, `InvalidKeyException`, `[Flow:Raid]` | mixed | singletons, no ticket. |

### 2b. The 8 softlocks

All eight from the single `unverified` id, all `Main_Castle_Overworld`, verbatim:
`No movement or progress for 180s in 'Main_Castle_Overworld'; classification=SOFTLOCK input=True
focused=True worldLive=True` — at 01:03, 01:33, 02:59, 11:59, 12:10, 12:20, 12:30, 12:40 Z.
`input=True focused=True worldLive=True` matches the title of **WO-1237** (`CLOSED 2026-08-26` —
"The softlock detector fires on AFK, and that noise will bury a real softlock"; its §42 names *input
presence* as the discriminator — *"A stuck player TAPS and nothing happens; an AFK player does not
tap at all"*, and these rows say `input=True`, so by 1237's own rule they are NOT AFK). The
10-minute cadence from 11:59 to 12:40 nonetheless reads as one device repeating, not eight distinct
softlocks. I did not read 1237's acceptance section in full. **No ticket minted:** the id is the shared
bucket, so it cannot be attributed or reproduced until WO-1791 lands. Flagged for the lead.

### 2c. Build grouping caveat

`playtest_break` carries **no build**. The version columns in §2a come from joining each id's own
`session_start.appVersion` for the same day. For `unverified` that join returns four versions at
once, which is exactly why 327 rows have no usable build attribution.

---

## 3. `save_reset_accepted 7` AND `api_auth_reject 7`

### 3a. Are real players' towns being reset? **NO — not today.**

Full per-row classification is in **WO-1795 §1**. Six of the seven landed on a `player_data` row
created **in the same second** (a brand-new player's first save, so the REPLACE replaced nothing);
the seventh (`CHKKFk…`, row from 2026-08-27) carried a **non-null `from`**, meaning the client had
already declared an epoch and bumped it — a deliberate new game by an existing account.

The **latent** defect is real and has fired before: `judgeResetEpoch` (`api/game/save.js:212-232`)
returns `bypass:true` when the stored row has NO epoch and the incoming one is positive, and
`bypass` selects `game_state = EXCLUDED.game_state` (full REPLACE) at `api/game/save.js:713-760`.
In the last 30 days that arm fired against rows **31.7, 7.9 and 11.1 days old**
(2026-09-08 ×2, 2026-09-07). 30-day totals: 41 accepted resets, 18 with `from: null`.
Whether those three were deliberate new games is **not decidable from the server** — which is the
**WO-1742 Lane A ruling that is still open** (`WORK_ORDER_1742…md:3`: *"Lane A needs an owner ruling
on conflict resolution (§8)"*). → **WO-1795**, status `NEEDS DATA`, with the queries.

### 3b. `SAVE_RESET_STALE` — 2 hits, 1 id, and none today

`view=authrejects&code=…&since_hours=24` summary:

| code | path | mode | hits | distinct ids | latest |
|---|---|---|---|---|---|
| `AUTH_SESSION_EXPIRED` | `/api/auth/session` | wallet | 7 | **2** | 2026-09-16T18:26:27Z |
| `SAVE_RESET_STALE` | `/api/game/save (pre-1745)` | guest | 2 | **1** | 2026-09-15T23:44:45Z |

Both `SAVE_RESET_STALE` rows are `guest-local-c7…` on 2026-09-15 at 23:44:32 and 23:44:45 — **one
frozen device, yesterday, zero today.** The `(pre-1745)` path label means those two rows were still
written under the OLD `save_reset_refused` event name, i.e. the **WO-1745 server-side detection was
not yet live in production at 2026-09-15T23:44Z**. WO-1745's own status says as much:
*"DONE (code + tests landed; the one acceptance criterion that needs a deploy is NOT PROVEN — see
§6)"*. **Not a new bug — a deploy that has not been confirmed.** Flagged for the lead: the next
production deploy should be followed by one `authrejects` read to prove the new name is writing.

### 3c. Which auth codes are firing: exactly one, and it is a retry loop

All 7 `api_auth_reject` rows today are `AUTH_SESSION_EXPIRED` on `/api/auth/session`, `mode=wallet`,
`detail={}`, across two wallets, in bursts:

```
CHKKFkPGz8VZ…  18:23:32, 18:24:28, 18:26:07, 18:26:27   (4 in 3 min)
BBJLKxyhavDN…  17:25:28, 17:26:18, 17:26:40             (3 in 75 s)
```

Against `session_issued 11` and `session_renewed 1` for the day. **No save or load rejection fired
at all today** — the save rail is healthy. The shape (repeat expiries seconds apart, one renewal)
suggests a client that re-presents an expired token instead of renewing, but **that is not proven
from here**: it needs the client-side renew path read at source plus one device capture. Mapped to
**WO-1742** (the only ticket that names `AUTH_SESSION_EXPIRED`) rather than a new number, because
1742's Lane B is the sign-in rail and this is its symptom surface. 2 players, both of whom kept
playing afterwards.

---

## 4. TICKET MAP

### 4a. Minted from the pre-assigned block

| WO | Title | Status | Players affected |
|---|---|---|---|
| **1791** | 73% of client events land under one shared `unverified` id — no funnel is readable per player. **The header attach is already shipped (WO-1735, proven working on `.371701`)**; what remains is the pre-fix build cohort (7 of 17 sessions), the wallet-without-session hole, and the missing guest→wallet alias event | READY TO IMPLEMENT | all |
| **1792** | Jeweler ships with three unbound albedo slots — untextured in town on the live build | READY TO IMPLEMENT | 9 ids |
| **1793** | `api/admin/db.js` can read no event payload — 424 breaks were invisible to every console | READY TO IMPLEMENT | ops (all) |
| **1794** | `tutorial_completed` fires on SKIP-ALL; raid funnel steps 1-2 fire from the starter grant | READY TO IMPLEMENT | all (metric truth) |
| **1795** | A first epoch-declaring save (`from: null`) REPLACES the whole cloud town; has fired on rows up to 32 days old | NEEDS DATA (owner ruling, WO-1742 Lane A) | 0 today, 3 in 30 d |

**Numbers from the block NOT used: none — all five of 1791-1795 were minted.** The banner was not
touched (`CLI_LANES_WO_NUMBERS.md` reconciles the main line at 1799+ independently).

### 4b. Mapped to existing tickets (no new number)

| Finding today | Existing ticket | Note |
|---|---|---|
| `[Flow:RaidArt]` boundary-ring no-albedo, 156 hits | **WO-1747** (`READY`) + **WO-1758** (`IMPLEMENTED, NOT YET GATED`) | raid scenes, 1 unattributed id |
| `[Flow:StructureAssets]` Addressables INIT handle INVALID, 18 hits / **10 ids** | **WO-1709** — Addressables-INIT half still `READY` | widest-reach break of the day; recommend prioritising |
| `[Flow:Tutorial] SKIP_TOP_HIT_BLOCKED top=ObsidianPanel`, 5 hits / 3 ids | **WO-1090** | the skip tap is eaten by the Obsidian panel |
| `[Flow:Manage]` queue prints `'Polish:ing Rough Stone:1'`, 8 hits | **WO-1564** (has a RESULT) | looks like a regression — lead to decide reopen vs new |
| `AUTH_SESSION_EXPIRED` ×7, 2 wallets | **WO-1742** (Lane B, `BLOCKED`) | retry-loop shape, unproven |
| `SAVE_RESET_STALE` written under the pre-1745 name | **WO-1745** (`DONE`, deploy unproven) | confirm on the next prod deploy |
| Beige untextured object in the tester's town | **WO-1774** (`NEEDS DATA` — "BUILD UNDER TEST IS UNKNOWN") | **the version question is answered**: today's players run `2026.09.16.371701` (10 ids) and `2026.09.07.359722` (7 ids). Object is still a WALL, not the Jeweler. |
| 8 × `possible_softlock` AFK shape | **WO-1237** (`CLOSED`) | unattributable; revisit after WO-1791 |

---

## 5. CLAIMS I COULD NOT VERIFY

1. **"22 new players."** Not provable from analytics. 25 distinct `player_id` values, of which one is
   a shared bucket and seven are wallet ids paired to a guest id 1-24 s earlier → **~14-15 humans**.
   `player_data` shows **18 rows created today** (9 wallet + 9 `guest-local-*`, of which **7 were
   written once and never updated again**) plus 3 updated rows that pre-date today
   (`CHKKFkPGz8VZ…` from 08-27, `guest-local-c63873ab…` from 08-31, `guest-local-eaf246c7…` from
   08-31). 18 rows for ~14-15 humans is the same double-counting as §0.
2. **"17 active now."** No session-heartbeat or session-end event exists; concurrency is not
   measurable from this schema.
3. **Owner-vs-player attribution — STILL OPEN.** `ANALYTICS_EXCLUDED_PLAYER_IDS` is a Vercel env var
   and is **not in `.env.local`**, and `grep -rl CHKKFk Logs/device/` returns **no local hit**, so I
   could not establish whether `CHKKFkPGz8VZ…` (row created 2026-08-27, SKR verified, 8 of the
   TripoMatFix hits, and today's only non-null-`from` reset) is the owner's own device. Its 18:08
   session on `.371701` is close in time to
   `Logs/device/pull-20260916-143101-bastion-owner-run`. Note the 156 `[Flow:RaidArt]` rows are under
   `unverified`, **not** under CHKKFk, so they are unattributable either way. **If CHKKFk is hers, 4
   of the 7 auth rejects are not player traffic.** One `vercel env` read, or one grep of her device
   pull for the wallet, closes it.
4. **Whether the three historical `from: null` REPLACEs destroyed a wanted town.** The server cannot
   distinguish a deliberate new game from a blank local state. Needs the WO-1742 Lane A ruling.
5. **The `AUTH_SESSION_EXPIRED` retry-loop hypothesis.** The burst shape is measured; the client
   renew path was **not** read at source in this lane and no device capture exists.
6. **Whether the 9 boot-drop ids quit or continued under a wallet id.** **Five** have a wallet pair,
   so they continued; but their client events are in the `unverified` bucket, so the boot-to-tutorial
   drop cannot be sized until WO-1791 lands. The other four are genuinely silent after HeroSelect.
7. **Which build each `unverified` row came from.** Structurally impossible today (§2c).
