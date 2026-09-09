# WORK ORDER 1366 - One Arena, one code path: the wager CURRENCY is the only per-channel difference

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:09:37, build 2026.09.08.361259). PRIOR STATUS: FIXED - on Firebase App Distribution as build 2026.09.05.356468 (08:2x); the Seeker build wagers SKR as before (felt-test: Arena still prices in SKR); the Play/Crystals half is proven by the pin, not the device. Landed + gated (COMPILE_GATE_OK, REGRESSION_OK 383/383 incl. ArenaCatalogRegression case 8): wager currency resolved per channel through CurrencySkinResolver (Play -> Crystals, Solana dApp -> SKR, Pi / Unknown -> refused in words); four tunable keys handed to the rail (wiring lane in flight). **FINDING / RULING:** the Editor and the desktop exe resolve PaymentChannel.Unknown, so they now REFUSE arena wagers as specified - keep, or map a dev channel? Also: should Pi wager at all, and in what? Awaiting the next APK; the artifact metadata scan stays a CLI AAB step behind WO-1362. Was READY TO IMPLEMENT.
**Silo / Lane:** Economy / Arena - `Assets/_Modules/Village/Arena/*` + the existing channel seam
**Type:** EXISTING system, currency abstraction + a real balance change
**Minted:** 2026-09-04 (CLI), on owner rulings
**Split from:** WO-1363 (which keeps the `DeNelle.Core` / catalog string purge). File-disjoint from
1363's Parts 1-2, so the two can run in parallel.

## THE RULINGS

Owner, 2026-09-04, in sequence:

1. ***"the arena will go to the google play store, just needs to remove crypto"*** - ⛔ **Arena is
   NOT cut from Play.** It ships. (This superseded an earlier cut-Arena ruling from the same session.)
2. **The Play build wagers CRYSTALS.**
3. ***"both to use same logic just different curency for wagers"*** - ⛔ **ONE Arena, ONE code path.
   The currency is the only thing that varies by channel.**

| Channel | Wager currency |
|---|---|
| Google Play (`GOOGLE_PLAY`) | **Crystals** (`GameState.Resources.Crystals`) |
| Seeker / dApp Store (`DAPP_STORE`) | **SKR**, exactly as it behaves today |

## ⛔ WHAT "ONE CODE PATH" FORBIDS

**Do NOT fork `ArenaMode`, `ArenaVM`, `ArenaCatalog` or the opponent tiers per channel.** No
`#if GOOGLE_PLAY` scattered through the Arena loop. The ruling is explicitly *same logic*: Arena
debits a wager, credits a purse, and does not know or care what the currency is. **The currency is
injected; it is not branched on.**

Two forked Arenas is the duplicated-state defect this repo has paid for four separate times -
CLAUDE.md §2 (the WO number block), §5 (the assembly table), §16 (the copy-pasted R2 verify), and
WO-1137 (the 3-of-28 fallback catalog). Every one of them started as "just two small copies."

## ⭐ THE SEAM ALREADY EXISTS. DO NOT BUILD A SECOND ONE.

This project already resolves per-channel currency presentation. **Extend it; never greenfield a
parallel resolver** (PREFLIGHT Gate A item 3; `docs/ARCHITECTURE_PRINCIPLES.md` §1/§2b).

- `Assets/_Modules/Core/Platform/CurrencySkinResolver.cs` - `#if GOOGLE_PLAY` at `:96`, `:239`,
  `:267`; `:267` pins `requested = "wallet"` above the whole chain and the SKR branch sits in the
  `#else` arm (`:271-313`), uncompiled on Play.
- `Assets/_Modules/Core/Platform/CurrencySkin.cs:130` - under `GOOGLE_PLAY` yields symbol `""`,
  name `"Store credit"`.
- `Assets/_Modules/Core/Payments/PaymentChannelResolver.cs:18`, `:21`, `:27` - the channel select.

⚠ **Read all three before designing anything.** If `CurrencySkinResolver` can already answer *"what
currency does this channel wager in"*, the Arena change is wiring, not invention. If it cannot,
extend it there - **not** in `Assets/_Modules/Village/Arena/`.

## THE CURRENT IMPLEMENTATION - measured at source 2026-09-04

`Assets/_Modules/Village/Arena/ArenaWalletService.cs` is a **client-side PlayerPrefs stub**:

```
:2   // ArenaWalletService - CLIENT-SIDE SKR WAGER STUB (ARENA MVP).
:19  // SCOPE (MVP): client-stub SKR - NOT real on-chain custody, NOT a backend-escrowed
:38  private const string PrefBalanceKey = "dotr-arena-skr-balance";
:41  private const long SeedBalance = 500L;
```

Its header claims the seam is *"deliberately confined to this one file: ArenaMode calls
Debit/Credit/Balance and nothing else."* ⛔ **PROVE THAT, DO NOT TRUST IT** - `grep -rn
"ArenaWalletService" Assets/` and confirm. This repo's founding lesson is that comments lie
(`HeroLocomotion`'s "pure transform" header hiding a `NavMeshAgent`; CLAUDE.md's mandatory-first-step).

Callers in `ArenaMode.cs`: `:161` `Debit(opponent.Wager)`, `:455` `Credit(_stakedWager)`, purse math
at `:379-393` (`WinPurse => Wager * 2L`, `ArenaCatalog.cs:48`), forfeit at `:431-436`.
Tiers: `ArenaCatalog.cs:87/:101/:114` = **50 / 100 / 200**.

## THE WORK

### 1. Abstract the wager currency behind ONE seam
`ArenaWalletService` stops being an SKR stub and becomes a currency-agnostic wager wallet:
`Balance` / `CanAfford` / `Debit` / `Credit`, resolving its backing store from the channel. Arena's
call sites do not change shape.

### 2. Play: back it with `GameState.Resources.Crystals`
⚠ `GameState.AetherCrystals` is **DEPRECATED** (folded at save v18, `GameState.cs:54-58`) - it is
kept at 0 for back-compat and **nothing writes it**. **Use `Resources.Crystals`**, the single source
of truth, or you will debit a field nobody reads.

⚠ Crystals are also spent through `ResourceLedger` / `EconomyService`. **Find the existing spend
seam and use it** - do not write `GameState.Resources.Crystals -= n` inline. Canon records a live
dual-wallet hazard (Wood/Iron pooled in `EconomyService` vs read-through in `GameState`); adding a
third writer to Crystals is how that class of bug reproduces.

### 3. Seeker: keep SKR behaving exactly as it does today
⛔ **DO NOT PROMOTE THE STUB TO REAL ON-CHAIN SKR.** The owner said *"different currency"*, not
*"make it real money"*. Today's Seeker Arena wagers a PlayerPrefs number; it must still do that
after this ticket. Turning a wager loop into real custody is a money-path change requiring its own
ruling, its own `WalletService` integration and its own security review. **Out of scope. Say so if
you are tempted.**

### 4. PlayerPrefs migration - ⛔ THE TRAP
The Play build must **NOT** inherit the stub balance. `dotr-arena-skr-balance` is seeded to **500
free**; converting it to 500 Crystals would **grant premium currency for nothing** to every player
who ever opened Arena.
- **Play:** ignore the stub key entirely; the balance IS the player's real Crystals.
- **Seeker:** the key keeps working. If you rename it, **read-migrate** - a renamed key reads as a
  fresh 500-seed while the old value sits unreachable.
⚠ If you believe the Play side should grant something for a stub balance, that is an owner call -
**ask, do not decide.**

### 5. ⛔ THE TIERS ARE TUNABLES. THIS IS BINDING, NOT A SUGGESTION.
**Standing rule, owner 2026-09-02** (`KEY_FACTS.md`): *"be smart, dont make it need a code change,
make it tweakable from a db call"* - followed by *"i have been screaming this for months."*
**A balance value is a TUNABLE, not a constant. The default answer is YES.**

50 / 100 / 200 and the 2x purse are **hardcoded today** (`ArenaCatalog.cs:87`, `:101`, `:114`,
`:48`) and they are about to become the price of a **real** currency. **Register them on the remote
tunables rail in this ticket** - `docs/PROD022_TUNABLE_FLAGS.md` is the contract, the rail is
`Assets/_Modules/Core/Ops/RemoteTunables.cs` (`Registry`) + `RemoteTunablesService.cs` + the
`TUNABLE_KEYS` allowlist in `api/_lib/tunables.js` + the Command Center Balance tab. **All four
change in the SAME commit**; the `[tunable-defaults]` oracle goes red naming which two disagree.

⛔ **Do not build a second tunables rail.** ⛔ **The registered default MUST equal the value the
constant has today** - no row / no network / no parse ⇒ today's behaviour exactly.

⭐ **Why this matters more than usual here:** with Crystals wagered, a mis-tuned tier is the
owner losing premium currency in a felt-test, and every re-tune without this rail is a ~10-minute
APK round trip on the one resource the project cannot buy more of.

### 6. Balance intent is the OWNER'S
Crystals are earned slowly and are the premium currency; a lost raid **forfeits the stake with no
refund** (`ArenaMode.cs:431`). 50/100/200 was authored against a free 500-seed stub, so those
numbers carry **no information** about what they should be in Crystals. ⛔ **Do not silently re-pick
them** (`SAMANTHA.md` rule 8). Ship the rail with today's values as defaults, tell the owner the
knob is live, and let her feel it.

## ACCEPTANCE

- [ ] ⛔ **ONE code path proven** - no per-channel fork of `ArenaMode`/`ArenaVM`/`ArenaCatalog`.
      Show the diff; a reviewer should see the currency injected, not branched on.
- [ ] The existing channel seam was EXTENDED, not duplicated. Name the file you extended and why.
- [ ] Play: a wager debits real Crystals through the existing spend seam; an insufficient balance
      blocks the raid with the existing refusal path; a win credits the purse; a loss forfeits.
      **Prove each with a captured `[Flow:Arena]` / `[Flow:ArenaWallet]` line, not a code read** (§12).
- [ ] Seeker: behaviour byte-identical to today. Prove the stub balance survives.
- [ ] ⛔ **No player is granted Crystals by this change.** Prove a pre-existing stub balance of 500
      does NOT become 500 Crystals.
- [ ] The tiers and purse multiplier are live on the tunables rail; `[tunable-defaults]` green; the
      registered defaults equal today's constants. Prove a knob change reaches a running client.
- [ ] Zero `SKR` literals remain in the Arena files **in the Play artifact** - scan
      `global-metadata.dat`, quote before/after counts. (Coordinate with WO-1363/1364; a source grep
      does not close it.)
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on fresh logs, `error CS` count zero.

## WHAT NOT TO TOUCH

- ⛔ Do not compile Arena out of Play, or gate any route into it. Arena ships on both channels.
- ⛔ Do not make Seeker's SKR wager real/on-chain.
- ⛔ Do not touch `arenaDefense` (save v19) or `ArenaProgress` (save v34) - both persist and both
      stay.
- ⛔ Do not write `GameState.Resources.Crystals` inline; use the existing spend seam.
- ⛔ Do not re-pick the wager amounts. Register them, default them to today's values, hand her the knob.

---
## RCA re-verified 2026-09-04 (QA read-only pass)
**Verdict:** VALID
**Evidence:**
- `Assets/_Modules/Village/Arena/ArenaWalletService.cs:2` "CLIENT-SIDE SKR WAGER STUB", `:38` `PrefBalanceKey = "dotr-arena-skr-balance"`, `:41` `SeedBalance = 500L` - exactly as cited. API `:48 Balance`, `:54 CanAfford`, `:65 Debit`, `:86 Credit`, `:97 DevReset`. Last touched `387dc2bf1 2026-07-08`.
- Callers: `ArenaMode.cs:161 Debit(opponent.Wager)`, `:379-380` `purse = opponentWager * 2L`, `:384 Credit(purse)`, `:431-436` forfeit, `:455 Credit(_stakedWager)`; `ArenaVM.cs:208 Balance`, `:212 CanAfford`. The header's "confined to one file" is not quite true - ArenaVM reads it too (still contained).
- `ArenaCatalog.cs:48 WinPurse => Wager * 2L`, `:87 Wager = 50L`, `:101 100L`, `:114 200L` - match.
- `grep -c GOOGLE_PLAY` = 0 across ArenaMode/ArenaVM/ArenaWalletService/ArenaCatalog. Unchanged.
- Seam line numbers MOVED (`6979fb961 2026-09-04` touched CurrencySkinResolver): `#if GOOGLE_PLAY` now at `CurrencySkinResolver.cs:96, :140, :248, :276` (WO says :96, :239, :267); `requested = "wallet"` now `:279`. `CurrencySkin.cs:130-138` SkrDefault block matches. `PaymentChannelResolver.cs:18-27` matches. `GameState.cs:54-58` AetherCrystals DEPRECATED comment matches.
- Tunables rail exists (`RemoteTunables.cs:26/:54 Registry`, `api/_lib/tunables.js:55 TUNABLE_KEYS`); `grep -i arena` in both = 0 hits - not registered yet.
- The "existing spend seam" named in WHAT NOT TO TOUCH does NOT exist as a method: no `SpendCrystals` anywhere (only comment mentions at `ResourceBuildingProgression.cs:22,:454`); Crystals are debited inline at `ResourceBuildingProgression.cs:525,:564` and `WardTetherService.cs:655`; `GameStateService.cs:495 AddCrystals` exists; `EconomyService.cs:110 ResourceCost.CrystalsOnly` exists.
- Suite: `Assets/Editor/Regression/ArenaCatalogRegression.cs:51-54` pins Wager 50/100/200 ascending and `WinPurse == Wager*2` (registered `DataRegression.cs:359`) - a tunables move must keep it green.
- No RESULT file; WO-1363 `:120` explicitly defers Arena to this ticket; WO-1377 covers the metadata literals separately.
**What changed since the RCA:** only the CurrencySkinResolver line numbers (:239->:248, :267->:276/:279). Arena code untouched since July.
**Ready for a lane?** yes - RCA holds; files a lane would touch: `Village/Arena/ArenaWalletService.cs`, `ArenaMode.cs` (trace strings), `Core/Platform/CurrencySkinResolver.cs`, `Core/Ops/RemoteTunables.cs`, `RemoteTunablesService.cs`, `api/_lib/tunables.js`, `docs/PROD022_TUNABLE_FLAGS.md`, `Editor/Regression/ArenaCatalogRegression.cs`, Command Center Balance tab.
**Pins/rulings needed:** (1) LEAD sign-off on the Crystals spend shape - the WO's "existing spend seam" is not a method; a lane either uses `GameStateService.AddCrystals(-n)` or adds one. (2) Owner ruling on wager amounts stays deferred (defaults = today's values).
