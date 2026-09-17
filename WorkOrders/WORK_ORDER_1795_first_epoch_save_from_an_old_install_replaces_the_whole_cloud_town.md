# WORK ORDER 1795 — A FIRST epoch-declaring save (`from: null`) REPLACES the entire cloud state, and it has fired against towns up to 32 days old

**Status:** BLOCKED - needs data (the capture named in the ticket)
**Minted:** 2026-09-16 (number PRE-ASSIGNED by the lead from the 1791-1795 block; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)
**Silo:** `api/game/save.js` (`judgeResetEpoch` + the `replaceState` arm). Server only.
**Priority:** P1 — low frequency, maximum blast radius (a player's whole town), and it is the open half of WO-1742.
**Blocks on:** the owner ruling that `WorkOrders/WORK_ORDER_1742_guest_save_rail_vs_google_signin_hypothesis.md:3` is already BLOCKED on — *"Lane A needs an owner ruling on conflict resolution (§8)"*. This ticket supplies the measurement that ruling was missing.
**Lane disjointness:** file-disjoint from WO-1791, 1792, 1793, 1794.

---

## 1. TODAY'S ANSWER FIRST: NO PLAYER LOST A TOWN ON 2026-09-16

`save_reset_accepted` fired **7 times today, 7 distinct ids**. Joined to `player_data.created_at`:

| time (UTC) | player (prefix) | `from` | row age at reset | keys |
|---|---|---|---|---|
| 14:00:29 | `A2gx78TqNt` | null | **0 s** | 86 |
| 16:53:16 | `EFhRvgYeNQ` | null | **0 s** | 86 |
| 17:09:38 | `BBJLKxyhav` | null | **0 s** | 86 |
| 18:08:00 | `CHKKFkPGz8` | 1789498048 | 1 732 397 s (row from 2026-08-27) | 87 |
| 18:54:26 | `3xLP7UyxD6` | null | **0 s** | 85 |
| 19:13:12 | `DKYiurDc3L` | null | **0 s** | 85 |
| 21:02:43 | `2ZurmHauf4` | null | **0 s** | 85 |

Six of seven are a brand-new player's FIRST save — the `player_data` row was created in the same
second — so the "replace" replaced nothing. The seventh (`CHKKFk`, a row from 2026-08-27) carries a
NON-NULL `from`, i.e. the client had already declared an epoch and legitimately bumped it: a
deliberate new game by an existing account. **So the WO-1742 Lane A worry did not materialise today.**

## 2. THE MECHANISM THAT IS STILL OPEN, AND IT HAS FIRED

`judgeResetEpoch` (`api/game/save.js:212-232`), when the stored row has **no** epoch:

```js
if (s == null) {
    // Nothing stored: ... A positive epoch is a reset this server has not seen
    return { ok: true, epoch: v, bypass: v > 0, from: null };
}
```

and `bypass` is what selects REPLACE over MERGE at `api/game/save.js:713`:

```js
const replaceState = resetJudgement.bypass;
…
game_state = CASE WHEN ${replaceState}::boolean THEN EXCLUDED.game_state
                  ELSE player_data.game_state || EXCLUDED.game_state END
```

So **the first epoch-carrying save from any install whose cloud row predates the epoch column
REPLACES that row's entire `game_state`** — no comparison, no merge, no refusal. The comparative
guards are stood down by design (the WO-1598 note at `:153-188` is right that a new game must
inherit nothing); the exposure is that `from: null` cannot distinguish *"I deliberately started a
new game"* from *"this device's local state is blank/stale and the cloud holds the real town"*.

**It has fired against real, old rows** — every `save_reset_accepted` in the last 30 days with
`from: null` on a row that pre-dated the event:

| time (UTC) | player (prefix) | row created | age at reset |
|---|---|---|---|
| 2026-09-08T19:48:41 | `guest-local-da5ebd75c2` | 2026-08-08 | **31.7 days** |
| 2026-09-08T14:29:40 | `guest-local-04bdcfed65` | 2026-08-31 | **7.9 days** |
| 2026-09-07T18:32:50 | `CHKKFkPGz8VZ` | 2026-08-27 | **11.1 days** |

30-day totals: 41 accepted resets, 18 of them `from: null`. All but the three above landed on a row
created the same second.

⚠ **AND THE THREE IDS ABOVE ARE SERIAL RESETTERS, which argues "deliberate".** Over the same 30 days
`CHKKFkPGz8VZ` accepted **11** resets, `guest-local-da5ebd75c2` **7**, `guest-local-04bdcfed65` **6**.
A player who resets eleven times in a month is testing, not losing a town — which lowers this
ticket's urgency and is part of why it is `NEEDS DATA` rather than P0. It does not remove the
exposure for a first-time resetter whose local state is blank.

## 3. WHY THIS IS `NEEDS DATA` AND NOT `READY`

The rows above prove the PATH executed against established towns. They do **not** prove any of the
three players was unhappy — each may have pressed "start new game" on purpose, which is exactly what
the epoch is for. The server cannot tell, and neither can this lane. **Deciding requires the owner's
WO-1742 Lane A ruling on conflict resolution**, which is precisely the question: when a device's
declared reset meets an older, richer cloud town, whose state wins?

### The exact queries / captures that close it

1. **Intent, from the client side.** Instrument `ResetToNewGame` to send the reset with a reason
   (`source: "player_new_game" | "migration" | "unknown"`) and log it. A `from: null` REPLACE whose
   source is not `player_new_game` is the defect; today there is no field that can say so.
2. **Historical damage, re-runnable** (read-only; this is the query this ticket was written from):
   ```sql
   SELECT e.received_at, e.player_id, e.properties->>'from' AS from_epoch,
          p.created_at, EXTRACT(EPOCH FROM (e.received_at - p.created_at))::int AS row_age_secs
     FROM analytics_events e LEFT JOIN player_data p ON p.player_id = e.player_id
    WHERE e.event_name = 'save_reset_accepted'
      AND e.properties->>'from' IS NULL
      AND e.received_at > NOW() - INTERVAL '30 days'
      AND p.created_at < e.received_at - INTERVAL '1 minute'
    ORDER BY e.received_at DESC;
   ```
   ⚠ It needs `view=events&name=save_reset_accepted` from **WO-1793** to be runnable without
   `DATABASE_URL` — no admin view reads this event today.
3. **A support answer for a player who reports a lost town.** There is none: `player_data` keeps no
   prior version, so a REPLACE is unrecoverable. If the ruling is "the cloud town wins on doubt",
   the cheap belt is to write the pre-replace `game_state` to a side table (or a `prior_state` jsonb
   column) on a `from: null` bypass ONLY, bounded, so one restore is possible.

## 4. CANDIDATE FIX SHAPES (for the ruling, not to implement yet)

- **A** Treat `stored == null && incoming > 0` as a bypass **only when the stored row is younger
  than N minutes**; older rows get MERGE plus an audit row naming the refusal. Safe, and it makes a
  genuine new game on an old account need one extra save round-trip.
- **B** Keep the bypass but snapshot the replaced state first (see §3.3). Costs storage, loses nothing.
- **C** Require the client to carry an explicit intent field alongside the epoch, and treat a bypass
  with no intent as MERGE. Reaches only builds shipped after it — the `api/events/track.js:26-33`
  lesson about client-side fixes not reaching installed builds applies.

## 5. WHAT NOT TO TOUCH

The epoch's monotonic contract (`:175-178` — a bare boolean is replayable forever, and the
`GREATEST()` belt at `:749` is what spends an epoch once). The BOUNDS guards, which still run on a
bypass by design (`:181-188`). `save_reset_refused` / `SAVE_RESET_STALE`, which WO-1745 already
closed — the 24-hour `authrejects` window shows only **2 hits / 1 id**, both on 2026-09-15T23:44 for
`guest-local-c7…`, and **none today.**
