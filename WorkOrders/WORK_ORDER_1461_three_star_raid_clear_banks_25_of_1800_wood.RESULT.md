# WO-1461 RESULT - raid loot settles to a Raid Cache, never LOST; repeat clears pay 60%

**Status:** IMPLEMENTED - awaiting gate (2026-09-09 lane SPOILS)
**Lane:** SPOILS, edit-only. No Unity run, no gate, no `git add`, no commit.
**Branch:** `dev`. **Tree at start:** HEAD `184c8ff06`.

---

## 1. WHAT THE ACCEPTANCE ASKED FOR, ITEM BY ITEM

Quoted verbatim from the WO's section 5, each with its verdict.

> - [ ] Deploy card figure equals banked + cached for a repeat clear against a full bank.

**DELIVERED.** `RaidSelectionVM` now quotes the SCALED estimate (`RepeatScaled` ->
`RaidClaimService.ScaleLootForClear`, the settle path's own method) and names the cached remainder
(`CacheNotice` -> `RaidClaimService.SplitAxis`, also the settle path's own method). The law
`Banked + Cached + Refused == amount` is asserted over a 168-case sweep in
`SpoilsAreBankableRegression` case `[split-law]`, and the quote/split equality in case `[quote]`.

⚠ **One honest caveat, recorded rather than hidden:** the equality holds EXACTLY on the numbers and
only approximately on the two rendered strings, because the spoils line is rounded to a range-feel
"~" by WO-1402's separate owner ruling and the cache notice deliberately is not (her own example
message, *"1,775 Wood held in Raid Cache"*, is exact). The reasoning is written at
`RaidSelectionVM.CacheNotice`'s doc comment, and the regression asserts on the numbers for that reason.

> - [ ] Regression: full bank + 3-star repeat clear -> nothing burned; the cache carries the
>       remainder to its cap.

**DELIVERED.** `SpoilsAreBankableRegression` cases `[split-law]` (bank full / cache empty ->
`refused == 0`) and `[retain]` (the logged 1080-wood-vs-25-credited case retains 1055 into the store
and persists it; a full cache refuses by a STATED cap and says so).

> - [ ] Regression: first clear after cooldown pays 100%; a repeat in the same cycle pays 60%; the
>       multiplier resets to 100% when the cooldown expires.

**DELIVERED, with the reset half pinned by source-lint rather than behaviourally** - stated because
it matters. `[repeat-share]` runs the real `ScaleLootForClear` (first clear pays in full; a repeat of
an 1800-wood payout settles 1080, not the 450 in her log). `[cycle-predicate]` asserts
`IsRepeatClearInCycle` is FALSE with no claim on record and pins, from stripped source, that it reads
`RaidCooldownService.IsOnCooldown` and that `RepeatClearLootMultiplier` is not a `const` again.
**Why not behavioural:** stamping a cooldown requires a loaded `GameState` (`RaidCooldownService`
persists to `SaveSchema "raidCooldowns"`, not PlayerPrefs), and this suite deliberately loads nothing.
A PlayMode case that stamps a real cooldown is the honest way to close it and is NOT claimed here.

> - [ ] The cache CLAIM door opens a registered `PanelId`.

⛔ **NOT DELIVERED - out of this lane's files, and it needs a design decision this lane does not have.**
The store's consume half exists and is pinned (`RaidClaimService.ConsumeCached`, which settles the
APPLIED basket, not the requested one - the WO-1392 lesson, or the haul burns a second time on the way
out). What is missing is the surface: which `PanelId` the door lives on, and whether it belongs on the
Welcome Back popup, the harvest overflow modal, or Manage. Every candidate is another lane's file.
**Seams for whoever takes it:** `RaidClaimService.Cached(BankResource)` / `.CacheRoomFor(...)` /
`.ConsumeCached(BankResource, applied)`.

> - [ ] `REGRESSION_OK n/n` on a fresh log; the deploy PNG opened.

**NOT DONE - this lane runs no Unity.** The lead's gate.

---

## 2. THE ONE THING THAT IS DELIBERATELY RED

`SpoilsAreBankableRegression` case `[settle-retains]` reads `RaidVictoryController.cs` and FAILS
because nothing calls `RaidClaimService.RetainOverflow`. That file belongs to lane RAID and this lane
did not touch it, so the retention API currently has **no caller** and the raid settle still burns
what the bank refuses.

**THE ONE LINE, in `HandleVictory`, immediately after `GrantLoot(loot);` (read at
`RaidVictoryController.cs:262` on 2026-09-09):**

```csharp
GrantLoot(loot);
RaidClaimService.RetainOverflow(configId, loot, _credited);   // <- WO-1461
```

⛔ **NOT inside `GrantLoot`.** Its signature is `GrantLoot(ResourceCost loot)` and carries no
`configId`, so the obvious placement does not compile. In `HandleVictory` the `configId` local is in
scope (`:217`) and the `_credited` field has just been set by the `GrantLoot` call above it.

**And the second, ruled, one-line substitution** at `RaidVictoryController.cs:229`, which currently
reads `bool repeatClear = RaidClaimService.IsClaimed(configId);`:

```csharp
bool repeatClear = RaidClaimService.IsRepeatClearInCycle(configId);
```

Without it the 60% share is applied but never RESETS - `IsClaimed` is permanent by design (WO-1134:
it also gates the one-time companion unlock), so the reduced share would stick for the life of the
save, which is only half the owner's ruling.

⚠ **THE ORDERING IS ALREADY CORRECT AT `:229` AND MUST STAY THERE.** The read must precede BOTH
`ClaimBase` -> `MarkClaimed` (`:240`) **and** `RaidCooldownService.BeginAfterClear` (`:249`) - the new
predicate reads the cooldown stamp as well as the claim flag, so a read taken after `BeginAfterClear`
would report every clear, including the first, as inside a cycle. The existing "read `IsClaimed`
before `MarkClaimed`" note in `RaidClaimService` covers only half of that; both writes now matter.

This is RED on purpose. A retention API with no caller is the exact write-only shape that let the
claim set go unread for months (this file's own 2026-08-15 header records it); a suite that went
green on it would repeat the mistake it was written to catch.

---

## 3. FILES CHANGED

| File | What |
|---|---|
| `Assets/_Modules/Village/World/Camps/RaidClaimService.cs` | `RepeatClearLootMultiplier` const -> tunable-backed property; new `RepeatClearPct`, `IsRepeatClearInCycle`, `CacheCapPerResource`, `SpoilsDestiny`, `SplitAxis`, `Cached`, `CacheRoomFor`, `RetainOverflow`, `ConsumeCached`, `SetCached`; third PlayerPrefs prefix `dotr-raid-cache-`. Also ASCII-fixed two pre-existing em-dashes in `MarkClaimed`'s log strings. |
| `Assets/_Modules/Village/Hero/RaidSelectionVM.cs` | Quote is now the SCALED estimate; new `RepeatScaled`, `CacheNotice`, `CacheNoticeLineFor`, `CacheNoticePrefix/Suffix`, `RepeatInCycleProvider`, `BankRoomProvider`, two guarded resolvers, cache-notice dictionary; trace line widened. |
| `Assets/_Modules/Village/Hero/RaidSelectionScreen.cs` | Wires providers (f) and (g); the cache notice takes the third slot of the existing lock/warning row, ahead of `RewardHint`; trace line widened. **No band, rect or layout constant changed** - `HudDrawOrderAndRectRegression` is another lane's pin and buying disclosure with a rect change is the wrong trade. |
| `Assets/_Modules/Core/Ops/RemoteTunables.cs` | Two defaults, two keys, two `TunableSpec` registry entries. |
| `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` | Two `ExpectedDefaults` literals. |
| `api/_lib/tunables.js` | Two allowlist entries. |
| `api/_lib/tunable-manifest.js` | Two hand-authored presentation cards. |
| `api/_lib/tunable-manifest.generated.json` | Regenerated by `node tools/gen-tunable-manifest.mjs` (never hand-edited). |
| `docs/PROD022_TUNABLE_FLAGS.md` | Rows 43 and 44. |
| `Assets/Editor/Regression/SpoilsAreBankableRegression.cs` | NEW. |

**No canonical JSON twin was touched** - nothing here needed an authored data value; the rail is code
plus manifest. So there is no newline count to prove.

---

## 4. THE TUNABLE, ON ALL FOUR SOURCES IN ONE CHANGE

Per `docs/PROD022_TUNABLE_FLAGS.md`:

| Source | `raid.lootRepeatClearPct` | `raid.cacheCapPerResource` |
|---|---|---|
| `RemoteTunables.cs` const + key + `TunableSpec` | 60 | 1800 |
| `RemoteTunablesDefaultsRegression.ExpectedDefaults` (independent literals) | 60 | 1800 |
| `docs/PROD022_TUNABLE_FLAGS.md` DEFAULT column | row 43, `60` | row 44, `1800` |
| `api/_lib/tunables.js` allowlist + `api/_lib/tunable-manifest.js` presentation | present | present |

`node tools/gen-tunable-manifest.mjs` then `--check`: **`TUNABLE_MANIFEST_GEN_OK knobs=46 (checked, no
drift)`**. Measured lines from this session, not expectations. Counted independently at source the same
way: 46 `TunableSpec` entries in `RemoteTunables.Registry`, 46 keys in `api/_lib/tunables.js`, 46
`ExpectedDefaults` literals, 46 keys in the generated JSON, 46 backticked keys in the doc table - all
five agree, which is what the four-source rule is for.

⚠ **`raid.lootRepeatClearPct` is the FOURTH deliberate departure** from "the default is today's
behaviour" (after the two `vfx.*` fixes, the ruled drain rate and the two raid bases): today's
behaviour is 25, and 25 IS the defect. Setting the row to 25 restores it exactly.

---

## 5. ⛔ THE UNRULED NUMBER - SAY IT OUT LOUD

**The Raid Cache cap has NO owner ruling.** She ruled the mechanic - *"a temporary Raid Cache with a
modest cap"* - and named no size. The shipping default `1800` is a **stated derivation, not a pick**:
exactly one perfect Camp I wood haul (`raid.lootWoodBase`), so the cache holds at most one raid's
worth of any one resource and cannot quietly become a second, larger bank. It is a knob so her number
lands in seconds with no rebuild. **This is an open item, not a settled one.**

Two other things this lane did NOT do, and did not pretend to:
- **The `[Flow:Bank] ... LOST n` wording** stays as it is. `TownBankCapacity.cs:790` owns it and it is
  outside this lane. The WO asks for `LOST` to become the cached amount; the cleaner fix is the one
  WO-1434 already reasoned out - the bank REFUSES, the caller retains, and the caller's own trace
  (`RAID CACHE: ... RETAINED, not burned`) now says so on the line right after.
- **`docs/reference/TUNABLE_LEVER_INVENTORY.md:410`** still proposes the key name `loot.repeatClearPct`.
  The key shipped as `raid.lootRepeatClearPct` (the raid family prefix, matching its thirteen
  siblings). That row wants updating by whoever owns that doc.

---

## 6. RED-FIRST PROOF

No Unity run is available to this lane, so the RED is stated as what FAILS against the pre-change tree
rather than as a captured log line - and it is stated as exactly that, not dressed up as a run:

1. `[repeat-share]` asserts `RepeatClearPct == 60` and that an 1800-wood repeat settles **1080**.
   Against HEAD `184c8ff06`, `RaidClaimService.cs:78` read `public const float
   RepeatClearLootMultiplier = 0.25f` and that payout was **450** - the number in the owner's own log
   (`troop-ai-blind-2026-09-06.log 14:37:40.331`, quoted in the WO). Both assertions FAIL there.
2. `[cycle-predicate]`, `[split-law]`, `[cache-roundtrip]`, `[retain]` and `[quote]` reference symbols
   that **did not exist** on HEAD (`IsRepeatClearInCycle`, `SplitAxis`, `Cached`, `RetainOverflow`,
   `RepeatScaled`, `CacheNotice`), so the suite does not compile against the pre-change tree.
3. `[cycle-predicate]` also fails on HEAD's `public const float RepeatClearLootMultiplier` pin.
4. `[settle-retains]` FAILS on HEAD **and still fails on this tree** - see section 2. That is the one
   case whose RED is live and intended.

---

## 7. GATE CHECKS RUN IN THIS LANE

Brace balance and NUL scan, every `.cs` touched (measured this session):

```
file                                 braces   NUL   non-ASCII outside comments
RaidClaimService.cs                   36/36    0    0
RaidSelectionVM.cs                    72/72    0    0
RaidSelectionScreen.cs                53/53    0    6  <- ALL PRE-EXISTING, see below
RemoteTunables.cs                     44/44    0    0
RemoteTunablesDefaultsRegression.cs   86/86    0    0
SpoilsAreBankableRegression.cs        56/56    0    0
```

Every line of all six files was classified, not merely counted: a line counts as a hit only if it
carries a code point above 126 **outside** a comment.

- **Five files: zero.** Their non-ASCII is confined to comments and XML doc (`⛔`, `⚠` and the house
  style), which is what this repo's canon already uses.
- **`RaidSelectionScreen.cs` reports six, and NONE of them is from this change.** They are
  pre-existing em-dashes at `:71`, `:72`, `:1301` (trailing comments) and `:360`, `:633`, `:672`
  (string literals: the `ff.raidtest` bypass warn and two `Debug.Log` lines). **Deliberately NOT
  touched** - they predate this ticket, three of them are live log strings that another suite could
  pin, and silently rewriting them would be unrelated churn in a file this lane only just entered.
  Flagged here so the next seat can fix them on purpose.
- Two pre-existing em-dashes in `RaidClaimService.MarkClaimed`'s log strings **were** ASCII-fixed,
  because that file is this lane's own and nothing in `Assets/Editor` pins those strings (checked).

`node tools/gen-tunable-manifest.mjs --check` -> `TUNABLE_MANIFEST_GEN_OK knobs=46 (checked, no drift)`.

**Not proven from here:** compilation (no Unity), `REGRESSION_OK n/n`, and the deploy PNG. Those are
the lead's gate and are not claimed.

---

## 8. REGISTRATION LINE FOR `DataRegression.RunAll`

This lane did not edit `DataRegression.cs`. Insert beside the `raid-repeat-clear` line (`:1489`):

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "spoils-bankable suite", () => { if (!DeNelle.Editor.Regression.SpoilsAreBankableRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[spoils-bankable] " + r); });
```

Markers: `SPOILS_BANKABLE_OK` / `SPOILS_BANKABLE_FAIL`. Standalone entry:
`run-unity-method DeNelle.Editor.Regression.SpoilsAreBankableRegression.RunStandalone`.

⛔ **THE REGISTRATION AND THE `RaidVictoryController` INSERTION ARE AN ATOMIC PAIR.** Registering this
suite without landing the two lines from section 2 in the SAME gate turns `REGRESSION_OK` **RED** -
`[settle-retains]` is a live, intended failure until the retention has a caller. Land both, or land
neither.

---

## 9. WHAT WAS CHECKED AND FOUND CLEAN (so nobody re-checks it)

- **`HeartfireRegression` PIN F does not red on the new comment.** The comment wired at (f) in
  `RaidSelectionScreen.cs` names `RaidCooldownService`, but PIN F reads that file through
  `SourceLint.ReadCode`, which strips comments and string literals - `HeartfireRegression.cs:695`
  says so in as many words. The runtime read is routed through `RaidClaimService`, not the screen,
  so no identifier reaches the linted text.
- **`IsRepeatClearInCycle` cannot throw headless.** `RaidCooldownService.RemainingSeconds` handles a
  missing `GameState` with a `FlowTrace.Warn` and returns 0 (`:225-240`). It is nonetheless
  short-circuited behind `claimed`, because an unclaimed camp has no cycle to be in and reading the
  record on one would emit that Warn on every headless frame, burying the trace it annotates.
- **There is no fifth tunable source.** `api/_lib/tunables.js:46` claims the allowlist is kept in
  step by hand with `tools/client-tunables.mjs`; that file actually IMPORTS `TUNABLE_KEYS` from
  `api/_lib/tunables.js` (`tools/client-tunables.mjs:61`) and carries no list of its own. The doc
  comment is stale - not this lane's file, flagged rather than edited.
- **No existing suite required `RepeatClearLootMultiplier` to be a `const`.** `RaidRepeatClearRegression`
  reads its VALUE (`:145`, needs `< 1f` - 0.60 passes) and `RaidSelectionSpoilsRegression` formats it
  (`:405`). Both compile and pass against a static property.

---

# WO-1461 ADDENDUM - 2026-09-09, lane RAID-2: THE ATOMIC PAIR HAS LANDED

Section 8 above states that registering `SpoilsAreBankableRegression` without the two
`RaidVictoryController` lines turns the suite RED, and that the pair must land together. **Both lines
are now in `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs`, in `HandleVictory`:**

| # | Line | Where | Why there and nowhere else |
|---|---|---|---|
| 1 | `bool repeatClear = RaidClaimService.IsRepeatClearInCycle(configId);` | STEP 1.5, replacing `IsClaimed(configId)` | The order is load-bearing on BOTH sides now: it must precede `ClaimBase` (STEP 2), which flips the claim flag, AND `RaidCooldownService.BeginAfterClear` (STEP 2.5), which STAMPS the cooldown window this predicate reads. Query after either and every clear reads as a repeat. |
| 2 | `ResourceCost retained = RaidClaimService.RetainOverflow(configId, loot, _credited);` | STEP 3.5b, immediately after `GrantLoot(loot);` | `GrantLoot` has just written the measured `_credited` basket; `RetainOverflow` needs `configId`, which `GrantLoot`'s signature does not carry but this scope's local does. It is not idempotent - exactly once per settle. |

A `FlowTrace.Step("Raid", "RAID CACHE at settle: ...")` naming requested / credited / **retained**
per axis was added beside line 2, so a capture states what was held rather than leaving the reader to
diff two baskets. Instrumentation is permanent (CLAUDE.md section 12).

**Both signatures were read at source in the working tree before being called:**
`RaidClaimService.IsRepeatClearInCycle(string)` at `RaidClaimService.cs:151`,
`RaidClaimService.RetainOverflow(string, ResourceCost, ResourceCost)` at `RaidClaimService.cs:280`.

**Checked and clean:** `ApplyFirstClearGate` already routes through
`RaidClaimService.ScaleLootForClear`, whose multiplier is `RepeatClearPct / 100f` off the
`raid.lootRepeatClearPct` rail (`RaidClaimService.cs:103`) - the old `const float 0.25f` is gone, so
there is no second gap behind this pair.

## ADD-1. ⛔ ONE CONSEQUENCE THIS LANE COULD NOT FIX - `RaidRepeatClearRegression` GOES RED

`Assets/Editor/Regression/RaidRepeatClearRegression.cs:321` reads `HandleVictory`'s body and requires
the literal text `RaidClaimService.IsClaimed(`:

```csharp
int iRead = handleVictory.IndexOf("RaidClaimService.IsClaimed(", StringComparison.Ordinal);
if (iRead < 0)
    fails.Add("RaidVictoryController.HandleVictory no longer calls RaidClaimService.IsClaimed - ...");
```

Substituting `IsRepeatClearInCycle` (which the owner's ruling requires, and which CALLS `IsClaimed`
internally at `RaidClaimService.cs:153`) removes that exact string, so this case fails. **It is a
stale pin, not a real defect** - the ordering half of the same case (`iRead > iClaim`) is simply
skipped because `iRead` is `-1`.

**That file is outside this lane's ownership and was NOT edited.** The minimal re-point, for the seat
that owns it - it moves WITH the owner's ruling, so it is a sanctioned re-point and must carry a
comment naming WO-1461:

```csharp
int iRead = handleVictory.IndexOf("RaidClaimService.IsRepeatClearInCycle(", StringComparison.Ordinal);
```

with the failure text updated to name the cycle predicate, and the `iRead > iClaim` ordering check
extended to also require `iRead < handleVictory.IndexOf("BeginAfterClear(")`, because the cooldown
stamp is now the second thing that must not come first.

**Unproven from this lane:** no compile, no Unity, no gate. `SpoilsAreBankableRegression`'s
`[settle-retains]` case is *expected* to flip green on the next gate; that is a prediction, not a
result.

## ADD-2. Recorded, not hidden - two things this pair does NOT fix

- **`_credited` is not written on every `GrantLoot` path, and `RetainOverflow` cannot tell.**
  `GrantLoot` early-returns on `loot.IsZero` at `RaidVictoryController.cs:547`, which is BEFORE the
  `_credited = default(ResourceCost);` reset at `:549`; and both LOOT-LOST branches (`:588-593`,
  where `GameStateService` has no loaded `State` or is absent entirely) leave `_credited`
  untouched. On any of those paths `RetainOverflow(configId, loot, _credited)` sees credited = 0
  and treats the WHOLE basket as "the bank refused it", caching it. Benign today: a zero-loot
  settle has nothing to cache, and the no-wallet branches are headless-only. It is written down
  because the day one of them fires on a device, the Raid Cache is where the loot will be, and the
  reader should not have to derive that. Fixing it means moving the `_credited` reset above the
  `IsZero` return - `RaidVictoryController` was edited by this lane, but that is a THIRD change the
  owner did not ask for, so it is reported rather than smuggled in.
- **`Village2RaidController.HandleCleared` still reads the PERMANENT `IsClaimed`**, pinned as such
  by `RaidRepeatClearRegression.cs:371`. The owner's cycle ruling ("reset to 100% when the camp's
  cooldown expires") therefore does not reach the raid-VILLAGE path - only the raid-CAMP path this
  lane owns. Out of lane, not fixed, named here so it is not discovered as a mystery.

## ADD-3. ⚠ `SpoilsAreBankableRegression` IS NOT REGISTERED IN `DataRegression`

Read at source 2026-09-09 on the working tree:
`grep -n "SpoilsAreBankableRegression" Assets/Editor/Regression/DataRegression.cs` returns
**nothing**. `RaidWatchdogHonorRegression` IS registered (`DataRegression.cs:1944`).

So the `[settle-retains]` case this addendum satisfies **does not run at the gate yet**. The suite
exists and is green-able; nothing is calling it. `DataRegression.cs` is explicitly outside this
lane's ownership and was not opened for edit. **For the lead:** one registration line, in the same
gate as this pair - the SPOILS RESULT's section 8 says the registration and the insertion are an
atomic pair, and this lane has landed only the insertion half.
