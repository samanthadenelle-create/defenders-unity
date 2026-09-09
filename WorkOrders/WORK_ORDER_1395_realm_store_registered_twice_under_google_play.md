# WO-1395: PanelId.RealmStore is registered by two classes under GOOGLE_PLAY - which store a tap opens depends on static-init order and on the CALL SHAPE

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:09:39, build 2026.09.08.361259). PRIOR STATUS: FIXED (re-scoped) - on Firebase App Distribution as build 2026.09.05.356386 (07:05); awaiting the owner RULING below more than a felt-test (the Seeker build is the Wallet artifact, so the Play changes are not visible on it). Landed + gated (COMPILE_GATE_OK, REGRESSION_OK 382/382 incl. the new realm-store-single-registrar suite), RE-SCOPED by the lane's finding: **the premise "registered twice" is disproved at source** - `DeNelle.Wallet.asmdef` carries `"defineConstraints": ["!GOOGLE_PLAY"]` (WO-1282) and `DeNelle.GooglePlay.asmdef` carries `["GOOGLE_PLAY"]`, so PackStoreBootstrap and GooglePlayStorefront are never in the same artifact; each build has exactly ONE registrar. What WAS real and is fixed: under Play the door-tagged open fell back to a plain open (no `store_opened {door}` funnel line), registration was untraced, the open unguarded. Now both registrars register the door-context opener, trace `RealmStore registrar=<class> skin=<skin>`, and a second-registrar detector Fails loudly. **OWNER RULING NEEDED:** keep two artifact-exclusive storefronts (what is pinned now) OR collapse Play into a PackStore skin, which means reversing WO-1282's Wallet exclusion for Play builds (compiling the Solana SDK into the Play artifact). Acceptance bullet 1 as written is unmeetable without that reversal. Minted 2026-09-05 from the UI screen graph (overnight STRETCH).

## Evidence
- Graph: `docs/qa/UI_SCREEN_GRAPH_2026-09-04.md:208` (RealmStore node: "ALSO GooglePlayStorefront.cs:17 under GOOGLE_PLAY") and `:244` (dead end 2); capture gap `:279`.
- Registrar A: `Assets/_Modules/Wallet/PackStoreBootstrap.cs:48` `PanelRouter.Register(PanelId.RealmStore, OpenRealmStore)` (plain) and `:52` the WO-1388 CONTEXT opener `(Action<string>)OpenRealmStoreFromDoor` - both `RuntimeInitializeLoadType.BeforeSceneLoad` (`:43`).
- Registrar B: `Assets/_Modules/GooglePlay/GooglePlayStorefront.cs:16-17` `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] private static void RegisterRoute() => PanelRouter.Register(PanelId.RealmStore, Open);` - a PLAIN opener only. It builds its own modal titled "REALM STORE" (`:44`) with SKU rows + Restore + deletion request (`:59-72`). WO-1388's lane observed the same line.
- Replacement semantics: `Assets/_Modules/Core/UI/PanelRouter.cs:185` `_openers[id] = open;` - "Register (or replace)". Last registrar wins the plain slot; Unity gives no ordering guarantee between two BeforeSceneLoad methods in different assemblies.
- Compile scope: `Assets/_Modules/GooglePlay/DeNelle.GooglePlay.asmdef` `defineConstraints ["GOOGLE_PLAY"]`, `includePlatforms ["Android","Editor"]`; `Assets/Editor/AndroidBuild.cs:214-215` sets GOOGLE_PLAY vs DAPP_STORE per build.
- THE CALL-SHAPE SPLIT (new finding this session): `PanelRouter.Open(id, context)` (`PanelRouter.cs:335-338`) prefers `_contextOpeners`, which ONLY PackStoreBootstrap registers. So in a GOOGLE_PLAY build a door-tagged open (`Open(RealmStore, "settings")`) ALWAYS lands in the Night Market, while a plain open (`HudKitController.cs:1408` the HUD card, `PlayerDeckWorkspace.cs:626` the Realm card, `RealmStoreVendor.cs:103`) lands in whichever registrar ran last. Two stores, one id, chosen by how the caller spelled the call. Not observed on a device - a collision proven at source.

## What the player experiences
On a Play build the Night Market card may open a plain "REALM STORE" list one session and the illustrated Night Market the next, and a store reached from Settings is a different screen from the store reached from the HUD card. Two storefronts for one word; the WO-1388 funnel's `store_opened {door}` cannot be trusted because half the opens never pass through the door opener.

## Fix shape (one mechanism)
ONE registrar for `PanelId.RealmStore`: PackStoreBootstrap. The Google Play surface becomes a RAIL SKIN of the one Night Market, the way the Pi skin already is (`canon-strings.json:231` `_storePiSkinNote`; `StorefrontRegistry`, WO-1282 "the rail-neutral host handle", `PackStoreBootstrap.cs:32,:55`). `GooglePlayStorefront.RegisterRoute` is deleted; its three verbs (purchase via Play, Restore purchases, Request account and data deletion) are exposed through the Commerce rail the PackStore already hosts - Restore + deletion as utility tabs on the FREE/utility rail under the Play skin, never a second modal.

```
any caller --PanelRouter.Open(RealmStore[, door])--> PackStoreBootstrap (SOLE registrar)
                                                          |
                                                    PackStore (Night Market)
                                                    skin = StorefrontRegistry.ActiveRail
                                                    SKR | Pi | Google Play
```
Trace: `FlowTrace.Step("Store", "RealmStore registrar=PackStoreBootstrap rail=<rail>")` at registration; `FlowTrace.Fail("Store", "second PanelId.RealmStore registrar detected: <type>")` if `PanelRouter` ever sees a plain re-register of this id from another file (add an `IsRegistered`-guarded check in `RegisterOpener`).

## Acceptance
- [ ] RED first: a new `RealmStoreSingleRegistrarRegression` - source scan: exactly ONE `PanelRouter.Register(PanelId.RealmStore, <Action>)` plain-opener site under `Assets/_Modules`, and it is in PackStoreBootstrap.cs. Fails on the current tree.
- [ ] Headless (GOOGLE_PLAY define in the Editor): `NightMarket_2670x1200.png` regenerated shows the Night Market with the Play rail's Restore + deletion tabs; `NightMarketUiRegression` green; capture gap `:279` closed.
- [ ] Device (Play build): HUD card, Realm deck card, vendor and Settings-door opens all log the same `PackStore` open line; `store_opened {door}` recorded for each.

## Not in scope
Pack pricing, the Play billing client itself (`DeNelle.PaymentProviders.GooglePlay`), Pi rail, the Night Market's two labels (WO-1398), Manage -> store return hop (graph dead end 10).

## Owner question
None - WO-1164 section 0 ruling ("just the store") and the Pi-skin precedent settle the shape.
