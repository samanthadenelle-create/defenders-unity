# WO-1461: raid loot settles to a Raid Cache, never LOST; repeat clears pay 60%

**Status:** FIXED 2026-09-10 - cap 1800 ruled kept; landed 63aaa8a6c, gated wave1-reg3; owner felt-test closes (was: "IMPLEMENTED - awaiting gate (2026-09-09 lane SPOILS)")
**Prior status:** READY TO IMPLEMENT - carries owner rulings 2026-09-06 20:33
**RESULT:** `WorkOrders/WORK_ORDER_1461_three_star_raid_clear_banks_25_of_1800_wood.RESULT.md`
⚠ **Two acceptance items are NOT delivered and are named in the RESULT:** the cache CLAIM door
(`PanelId`) and the one-line settle wiring in `RaidVictoryController.cs`, both outside this lane's
files. `SpoilsAreBankableRegression` is deliberately **RED** on the settle wiring until that line lands.
**Silo:** raid reward settle + `RaidDeployScreen` spoils line + Core/Economy bank, beside the WO-1434
pending-retention stores.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1461 -> 1462 in the same edit). **RULINGS ADDED 2026-09-06 20:33** from
her review of `docs/RAID_BALANCE_AUDIT_2026-09-06.md`.

### OWNER RULING 2026-09-10 (morning)
**Question put to her** (`docs/HANDOVER_2026-09-10_overnight.md` §3 item 6): the Raid Cache cap of 1800
per resource is a stated DERIVATION, not her number.

**Owner chose, verbatim:** *"Keep 1800 per resource"* - and the cap stays a **tunable**, not a constant
to re-litigate.

Verified at source this session, so the ruling needs no code change: the cap is already remote-tunable -
`RemoteTunables.RaidCacheCapPerResourceDefault = 1800` under key `raid.cacheCapPerResource`
(`Assets/_Modules/Core/Ops/RemoteTunables.cs:411`, `:458`, `:1157`).
- Landed: `63aaa8a6c` *"fix(raid): WO-1461 spoils above cap are retained in the raid cache; repeat clear
  pays the ruled 60%"* (2026-09-09 23:51:49 -0500; `git branch --contains` -> `dev`).
- Gated wave 1, markers read on the logs this session: `Builds/wave1-compile4` -> `COMPILE_GATE_OK :: scripts compiled clean`; `Builds/wave1-reg3` -> `REGRESSION_OK 492/492 suites -- 492 green, 0 red, 0 skipped` (log mtime 23:57, postdates the commit).
- ⚠ **The header's "deliberately RED" note is SUPERSEDED - measured, not inferred.** The settle wiring
  DID land: `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs:281` opens
  `// STEP 3.5b (WO-1461) - RETAIN WHAT THE BANK REFUSED INSTEAD OF BURNING IT.` (note the path - the WO
  cited `Village/Troops/`, the file is under `Village/World/Camps/`), and `SpoilsAreBankableRegression`
  sits inside the green 492/492 with **0 red**, so it is no longer failing.
- ⚠ **The cache CLAIM door is still ABSENT - opened at source, not inferred.** `enum PanelId` lives at
  `Assets/_Modules/Core/UI/PanelRouter.cs:37-183`; scanned member-by-member it carries **no** member
  matching `raid|cache|claim|spoil`. `grep RaidCache` over `Assets/_Modules/Core/` returns only the three
  `RemoteTunables.cs` hits (`:411`, `:458`, `:1157`). This matches the SPOILS lane's own RESULT file,
  which leaves `- [ ] The cache CLAIM door opens a registered PanelId.` unticked (`:46`) and says
  "What is missing is the surface: which `PanelId` the door lives on" (`:51`). The owner's felt test
  should look for a way to claim the cache; if there is none, that half re-opens as its own ticket.

## 1. EVIDENCE

`troop-ai-blind-2026-09-06.log`:

```
14:37:40.331  loot settled ... 1800w 1100i
              repeat-clear multiplier x0.25 -> 450w / 275i
14:37:40.333  [Flow:Bank] BANK FULL [Grant] Wood: requested 450, banked 25, LOST 425
```

The deploy screen for that same camp reads `Spoils: ~1800 wood, ~1100 iron`. So 1,800 was promised and 25
banked - the promise ignores both the repeat multiplier and the cap, and the remainder is burned, against the
WO-1434 law that capped yield is recoverable.

## 2. THE OWNER'S RULINGS (2026-09-06 20:33)

**On overflow, verbatim:**

> "Never destroy raid loot because storage is full. Put overflow into a temporary Raid Cache with a modest
> cap... A message like 1,775 Wood held in Raid Cache - storage full turns frustration into a progression
> prompt."

**On the repeat penalty, verbatim:**

> "100% first clear after cooldown, 60% repeat clear during the same cycle, then reset to 100% when the camp's
> cooldown expires."

## 3. FIX SHAPE

- A **`RaidCache` store** with a MODEST authored cap - one authority, sitting beside the WO-1434 pending
  stores and obeying the same retention law. Not a second retention mechanism.
- Cap authored in the raid loot tunables, never a literal.
- A **CLAIM door** on the welcome-back / harvest surfaces, so the cache is claimable after the player upgrades
  or spends. This is the "progression prompt" half of her ruling - a cache with no door is just a slower burn.
- `repeatClearMultiplier` **0.25 -> 0.60** in the tunables, and it RESETS to 1.00 when the camp's cooldown
  expires.
- The deploy card quotes what will BANK **and** what will CACHE.
- Keep the `[Flow:Bank]` line; `LOST` becomes the cached amount so the log stops asserting a burn.

## 4. WHAT NOT TO DO
- Do not raise the bank cap to make the number fit. The cap is the progression (memory `stockpiles-cap-capacity`).
- Do not make the Raid Cache unbounded - she said "modest cap". An infinite cache removes the upgrade pressure
  the cache exists to create.
- Do not add a third pending store.

## 5. ACCEPTANCE
- [ ] Deploy card figure equals banked + cached for a repeat clear against a full bank.
- [ ] Regression: full bank + 3-star repeat clear -> nothing burned; the cache carries the remainder to its cap.
- [ ] Regression: first clear after cooldown pays 100%; a repeat in the same cycle pays 60%; the multiplier
      resets to 100% when the cooldown expires.
- [ ] The cache CLAIM door opens a registered `PanelId`.
- [ ] `REGRESSION_OK n/n` on a fresh log; the deploy PNG opened.
