# WO-1412 RESULT - store close ejects the player from Manage

**Lane:** STORE-RETURN (edit-only), 2026-09-09, branch `dev`.
**Status handed back:** IMPLEMENTED (item 1) - item 2 needs ruling.
**Never fired Unity, never staged, never committed** (lane constraint). Every marker below is UNRUN.

---

## 1. What was already on HEAD (re-read at source this session, not taken from a doc)

The 09-06 note on the ticket - *"no diff hunk in the working tree carries a WO-1412 marker"* - is WRONG,
and this is the correction:

| Half | File:line read 2026-09-09 | What it does |
|---|---|---|
| Sending door | `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:3015-3021` | `OpenRealmStoreFromManage` captures `ManageTab sendingTab = Tab`, calls `PanelManager.SetReturnDoor("Manage tab=" + tab, () => PanelRouter.Open(PanelId.Manage, tab))`, traces `[Flow:Manage] store handoff source=<src> returnTab=<tab>`, and on a failed open calls `ClearReturnDoor("manage-store-open-failed")` + `FlowTrace.Warn` |
| Its caller | `ManageScreenVM.cs:2977` | `BuySlot(ChannelId)` - the builder offer - routes through that one handoff (as does `OpenCrystalStore` at `:3008-3012`) |
| Closing door | `Assets/_Modules/Wallet/PackStore.cs:1301-1310` | `CloseStore` carries the literal comment `// WO-1412: the sending Manage surface owns the return door`, reads `PanelManager.ReturnDoorName`, traces `[Flow:Store] close -> return opener=<door or HUD>`, and closes via `CloseViaInteractor()` so the ordinary lifecycle arms the WO-1400 arbiter. It deliberately opens no second route. |
| Busy-only upsell | `ManageScreenVM.cs:1186-1194` | `BuilderUpsellVisible = svc != null && slots > 0 && busy >= slots`; the offered label is `"Buy builder" + " - " + pack.UsdReference`, with the in-code note that SKR conversion is intentionally unavailable to the Village assembly |

So the two acceptance items were unmet for different reasons: item 1 had the CODE but no ORACLE; item 2 is
blocked on a rail/assembly boundary that a lane may not cross unilaterally.

## 2. What this lane changed

**One new file. Zero edits to `ManageScreenVM.cs` and zero to `PackStore.cs`** - the shapes they needed were
already correct, and not touching `ManageScreenVM.cs` also keeps this lane clear of WO-2007's hunk near `:5296`.

- `Assets/Editor/Regression/StoreReturnToManageRegression.cs` (NEW, 46/46 braces, 0 NUL bytes, ASCII-only)

Six cases, three of them behavioural against the real VM and the real arbiter:

| Case | Kind | Asserts |
|---|---|---|
| A `[returns-to-the-sending-tab]` | BEHAVIOURAL | the WO walk with **Manage already OPEN**: real `ManageScreenVM` + fake `PanelRouter` openers; `BuySlot` SWAPS Manage out for the store (arbiter exclusivity asserted) and SETS `"Manage tab=<tab>"`; the door is KEPT (not armed) while the store is up; the store's close ARMS it; a pump ON `CloseGraceUntilFrame` does NOT fire (WO-1393); the pump after it re-opens Manage AND hands the opener the SAME tab string; Manage ends on `Opens == 2` (the player's own open, then the return); the door is consumed; both `[Flow:Manage] store handoff` and `[Flow:Navigation] return door SET/FIRED` lines exist |
| B `[failed-open-clears-the-door]` | BEHAVIOURAL | a RealmStore opener that opens nothing (PanelRouter's WO-465 visibility verify fails it) leaves NO live door, nothing re-opens, and the dead end is WORDED |
| C `[hud-opened-store-does-not-return]` | BEHAVIOURAL | a store opened with no Manage handoff returns nowhere - the door belongs to the SENDER, it is not a global rule |
| D `[source-manage-handoff]` | SOURCE | the door is set BEFORE the store open, the callback carries the tab, the failure branch clears + warns, and the handoff sets exactly ONE door |
| E `[source-store-close]` | SOURCE | `CloseStore` reads the door name, traces the opener, still closes via the interactor (the soft-lock guard), and contains NO `SetReturnDoor` / `PanelRouter.Open` - the "no second return path" half |
| F `[busy-only-upsell]` | MIXED | behavioural: with no all-busy Builder line the upsell is OFF and no surface reads "Buy builder" (case-insensitive - the authored copy is `"Buy builder"`, not `BUY BUILDER`). source: gated on `busy >= slots`, priced from `pack.UsdReference`, free-slot branch names the channel's verb |

**Deliberate anti-vacuity note in case F:** no assertion is made about SKR *in either direction*. Pinning
"contains SKR" would fail forever; pinning "contains no SKR" would enforce the OPPOSITE of the ticket - the
exact `ManageQueueDrawerRegression` failure mode where a suite guaranteed the defect it was meant to catch.
The reason is written in the file at the point a future seat would reach for it.

### Registration line for `DataRegression.RunAll` (lead applies it - this lane owns no `DataRegression.cs` edit)

Shape copied verbatim from the `deck-return-door` row at `Assets/Editor/Regression/DataRegression.cs:1737`:

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "store-return-to-manage suite", () => { if (!DeNelle.Editor.Regression.StoreReturnToManageRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[store-return-to-manage] " + r); });
```

Markers `STORE_RETURN_TO_MANAGE_OK` / `STORE_RETURN_TO_MANAGE_FAIL` are substring-disjoint from every existing
marker (`grep -rn "STORE_RETURN" Assets/ --include=*.cs` returned nothing before this file). No `.cs.meta` was
hand-authored - Unity generates it on the gate seat's import.

## 3. RED-first: the mutations, and what is UNPROVEN about them

The suite is written RED-first and the mutations are recorded in its header. The one that matters:

> **Delete `PanelManager.SetReturnDoor("Manage tab=" + tab, ...)` from `OpenRealmStoreFromManage`.**
> Cases A and D fail. No door is ever set, so `PackStore.CloseStore` reads `ReturnDoorName == null`, traces
> `close -> return opener=HUD`, and the close lands on the town HUD with Manage gone. **That is the WO-1412
> defect verbatim** - the `11-research-upgrade-door.png -> 12-hud-after-store-close.png` walk.

Others: point the callback at `PanelRouter.Open(PanelId.Manage, null)` -> A fails on the wrong tab; add a
`PanelRouter.Open` into `CloseStore` -> E fails; delete `ClearReturnDoor("manage-store-open-failed")` -> B
fails; change `busy >= slots` to `busy >= 0` -> F fails.

> ### UNPROVEN (section 11B, stated as unproven rather than ticked)
> - **The suite has never been RUN.** This lane may not fire Unity, so there is no `REGRESSION_OK` on a fresh
>   log and no observed RED. The RED-first claim above is REASONED FROM THE MUTATION, not measured. The gate
>   seat should run it once as written (expect GREEN on HEAD) and, if it wants the RED proof, once with the
>   `SetReturnDoor` line commented out.
> - **The device walk (acceptance item 3) and the headless `ManageQueueTroops_2670x1200.png` recapture
>   (acceptance item 2) are untouched by this lane.** Both remain open.
> - **The suite REPLACES any `PanelId.Manage` / `PanelId.RealmStore` opener for the duration of cases A and B
>   and cannot restore it.** `PanelRouter.Register` overwrites and `Unregister` removes only an identical
>   delegate, and the router exposes no read-back. Nothing under `Assets/Editor/` registers either id today
>   (grepped 2026-09-09 - `RealmStoreSingleRegistrarRegression` only names the call as a source string) and
>   `DataRegression` runs with no live panels, so no collision exists NOW. The suite logs
>   `PanelRouter.IsRegistered` before each Register so a future ordering hazard is visible in the run log
>   rather than silent.
> - **Cases E and the all-busy half of F are SOURCE sweeps, and the file says so.** `PackStore` is a
>   MonoBehaviour needing the store canvas + the marketplace interactor; `BuildTimerService` is a scene
>   singleton (`Assets/_Modules/Village/Buildings/BuildTimerService.cs:87-137`, `Instance` set in `Awake`)
>   that cannot be driven to an all-slots-busy state in editor batchmode. The runtime half of both is the
>   device walk the WO already asks for.

## 3b. BOARD BUCKET WARNING - the mandated status string renders this row as DONE

The lane brief mandated the exact status `IMPLEMENTED (item 1) - item 2 needs ruling`, and that string was
written as ordered (section 11B B: follow the procedure as written, deviation needs permission IN ADVANCE).
**Read `tools/board_build.py:187` before regenerating the board:** the bucket is decided on the LEADING WORD,
and `if lead in ("DONE", "IMPLEMENTED", "COMPLETE"): return "Done", False` puts this ticket straight into
Done - the one bucket the file's own comment at `:166` says nobody re-opens. `has_result` at `:211` would do
it a second time now that this RESULT exists. None of the contradiction patterns at `:289-304` match
"item 2 needs ruling" (they match "AWAITING OWNER RULING", not this phrasing), so the row will carry no flag
either.

**This ticket is NOT done:** acceptance item 2 is blocked on a ruling, the new suite has never been run, and
acceptance items 2 (headless recapture) and 3 (device walk) are untouched. If the lead wants the board to say
so, the status needs a leading word that is not a finished lead - `BLOCKED` or `AWAITING OWNER RULING` both
bucket correctly. **This lane did not make that change unilaterally**; it is the lead's call.

## 4. Item 2 (SKR + $ label) - THE ONE THING NEEDING A LEAD/OWNER RULING

The WO asks for `BUY BUILDER - 511 SKR (~$9.99)`. **No sanctioned seam delivers the SKR figure to
`DeNelle.Village`, and this lane did NOT add an asmdef reference.** The label is unchanged and still reads
`Buy builder - $9.99` (from `pack.UsdReference`; `Assets/Resources/Data/Canonical/packs.json` authors
`permanent-builder` at `usd: 9.99`). Note the ticket's own "511 SKR" is not an authored figure either - that
file authors `skr: 120` for this SKU, and neither number is the one a player would be charged, which is the
whole reason the SKR amount is server-quoted. Evidence, all opened at source 2026-09-09:

1. **The asmdef.** `Assets/_Modules/Village/DeNelle.Village.asmdef:4-28` - `references` contains
   `DeNelle.Core` (`:5`), `DeNelle.Commerce` (`:6`), `DeNelle.Data`, `DeNelle.Audio`, etc. **`DeNelle.Wallet`
   is NOT among them.** So Village may reference Commerce and Core, and may NOT reference Wallet.
2. **The Core seams named in the brief do not carry a price.**
   `Assets/_Modules/Core/Platform/CurrencySkinResolver.cs` resolves a skin - `CurrencySymbol`, `CurrencyName`,
   auth mode, connected-wallet address - and its only currency-amount surface is
   `ResolveWagerCurrency`/`WagerCurrencyName` (Arena wagers, WO-1366). It exposes **no USD->SKR conversion and
   no per-SKU amount.** `Assets/_Modules/Core/Payments/PaymentChannelResolver.cs` returns only which channel is
   stamped. Neither can answer "how many SKR is this pack".
3. **The one reachable SKR number is the one source forbids rendering.** `PackPricing.Skr` exists at
   `Assets/_Modules/Commerce/PackCatalog.cs:78` and IS reachable from Village today (Commerce is referenced;
   note the type sits in `namespace DeNelle.Wallet` but in the **`DeNelle.Commerce` assembly**, which is why a
   `using DeNelle.Wallet;` already compiles at `ManageScreenVM.cs:47`). **Using it would be the bug.**
   `Assets/_Modules/Wallet/SolanaPackPricing.cs:62-64` (WO-1158) says verbatim: *"the authored `pricing.skr` in
   packs.json is a stale hand-typed figure from before the SKR rail existed at a real rate, and rendering it
   would put a number on screen that nobody will honour."*
4. **The honest SKR figure is server-quoted and lives in the assembly Village may not reference.**
   `PurchaseQuoteService.SkrAmountFor(sku)` (`Assets/_Modules/Wallet/PurchaseQuoteService.cs:270-274`) returns
   the server's quote or **0**, and 0 is rendered as the WORDS "Price unavailable". It is in `DeNelle.Wallet`.
5. **Adding the reference is explicitly forbidden, with the blast radius named.**
   `Assets/_Modules/Commerce/PackCatalog.cs:327-345` (the WO-1282 pointer block): *"A Google Play artifact
   excludes `DeNelle.Wallet` entirely (GooglePlayPackagingGate) ... DO NOT re-add a CurrencyKind member to this
   file ... Adding it re-breaks the Play artifact, silently, and the gate that catches it runs at BUILD time."*
   A `Village -> Wallet` reference is the same failure one level up.

**The question for the owner/lead** (two parts, and the second is the one this lane cannot answer):

- **(a)** Does `KEY_FACTS.md:528-536` (owner ruling 2026-09-04 23:12, *"the USD ladder is the one price
  authority on every channel"*) govern **DISPLAY** as well as the wallet-gating threshold it was written
  about? If it governs display, the WO's `511 SKR (~$9.99)` spec is itself superseded by a ruling made the day
  before the ticket was minted, item 2 is CLOSED as written, and the current USD-only label is already correct.
  **This lane does not claim that reading - the ruling's own subject is guest-buyability, not label copy.**
- **(b)** If SKR must appear on the Manage label, the only correct implementations are a **Core-side DTO** the
  Wallet assembly PUBLISHES into (the exact shape `CurrencySkinResolver.PublishWalletConnected` already uses
  for the connect seam - Wallet pushes, Core holds, Village reads), or moving the upsell label's composition
  to a surface that already lives on the rail. Both are new mechanism and belong in their own ticket, not in a
  return-door lane. **No asmdef edit should be made under any circumstances** - see (5) above.

Until (a) or (b) is answered the label stays USD-only and the oracle stays silent on SKR by design.

## 5. Files

| Path | Change |
|---|---|
| `Assets/Editor/Regression/StoreReturnToManageRegression.cs` | NEW - the acceptance item 1 oracle |
| `WorkOrders/WORK_ORDER_1412_store_close_ejects_the_player_from_manage.md` | Status flipped; 09-06 note resolved with file:line; acceptance item 1 annotated WRITTEN-NOT-RUN |
| `WorkOrders/WORK_ORDER_1412_store_close_ejects_the_player_from_manage.RESULT.md` | NEW - this file |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs` | **UNCHANGED** (shape correct on HEAD; also avoids WO-2007's hunk) |
| `Assets/_Modules/Wallet/PackStore.cs` | **UNCHANGED** (the WO-1412 trace at `:1303-1310` already exists) |
| `Assets/Editor/Regression/DataRegression.cs` | **UNCHANGED** - registration line handed to the lead in section 2 |

Quality gate on the one `.cs` written: **braces 46 open / 46 close, 0 NUL bytes, 0 non-ASCII characters.**
Every existing `FlowTrace` call was left in place; none was added to a shipped file and none was removed.
