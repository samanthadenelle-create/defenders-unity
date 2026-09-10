# WORK ORDER 1700 - Nothing is buyable on the Pi rail since WO-1386: a server-verified Pi sign-in must count as the durable identity

**Status:** FIXED - owner felt-test PASS 2026-09-10 23:42Z (testnet purchase completed on the Seeker in Pi Browser)
**Minted:** 2026-09-10 16:55 by the CLI lead from the owner's live Seeker session in Pi Browser ("all say not on sale", "says connect a wallet", "but other screen showed i am connected", "last step to get to mainnet is successful testnet purchase"). Main-line banner bumped 1700 -> 1701 in the same edit.
**Silo:** Wallet / Pi. One predicate in `Assets/_Modules/Wallet/PurchaseGate.cs`. No scene, no JSON, no backend.
**Exception named (orchestration cadence):** lead-implemented. The owner is live, near her weekly token limit, and the Pi portal is waiting on one purchase; the change is one channel-gated predicate with the captured data in hand.

## 1. Captured data (2026-09-10, `analytics_events` web_trace, session wt-56a6e825d88, build 2026.09.10.364108@defenders-pi.vercel.app, Seeker in Pi Browser 1.17.1)
- 21:46:31Z `[Flow:Skin] Currency skin resolved: 'pi' (auth=PiSdk, symbol=π, identity=PiUid).`
- 21:46:36Z `[Flow:Pi] Pi.authenticate OK on TESTNET/sandbox.` then `[Flow:Pi] Signed in as samanthadenelle (uid bound to session).` - this line prints only after `VerifyWithBackend` returned true (`PiSignInController.cs`, the uid comes from `/api/pi/verify`, which validates the token against api.minepi.com).
- No `[Flow:Skin] Bound NeonDB identity key (PiUid) from Pi sign-in.` line: `skin.json` has `bindIdentityOnAuth:false` for the `pi` and `wallet` skins (read 2026-09-10), so the Pi uid is never the save's BoundWallet.
- 21:49:06Z, every card tapped: `[Flow:Store] PurchaseGate: 'impulse-stone-medium' is $2.99 on channel PiBrowser; the channel is PiBrowser, where NO price is guest-buyable (WO-1386, owner 2026-09-04), and this save has NO attested wallet identity` followed by `BuildSpotlightCta '...': wallet-rule refusal, PI wording`. Same for builders-hour and folks-thanks. The plate the owner saw: "Connect a wallet before buying in Pi, at any price..." (`storePiWalletGate`).

## 2. Cause
- WO-1318 (Pi U2A, one SKU) felt-test PASS 2026-09-04T14:37. WO-1386 landed in 65d5a7eae at 2026-09-04 23:32 and made `PurchaseGate.RequiresWallet` TRUE at every price on `PaymentChannel.PiBrowser` (owner: "mark anything for Pi as same logic based on USD").
- The only identity test `PurchaseGate.CanBuy(pack)` consulted was `HasDurableIdentity => GameStateService.HasAttestedWalletIdentity`, which requires the BoundWallet to be a 32-44 char base58 string (`IsCloudIdentityShaped`) AND equal to the address a signing wallet vouched for (`AttestedCloudIdentity`). A Pi uid can never satisfy either, and the Pi skin never binds it anyway. So on Pi, since 09-04 23:32, every price is refused as "guest". The owner's intent in WO-1386 was an ATTESTED identity, which on Pi is the server-verified uid.

## 3. Change
`PurchaseGate.HasDurableIdentity` is now `HasAttestedWalletIdentity || HasVerifiedPiIdentity`, where
`HasVerifiedPiIdentity => PaymentChannelResolver.Current == PaymentChannel.PiBrowser && PiSignInController.IsSignedIn`.
`IsSignedIn` is true only after `/api/pi/verify` returned the uid; off WebGL the controller is the inert stub and it is false, so the SKR and Google Play rails are unchanged. `RequiresWallet` (the WO-1386 rule pinned by `BuyGateAndPriceLadderRegression` at every Pi price) is untouched; the identity axis is what widened.

## 4. Acceptance
- Seeker, Pi Browser, signed in with Pi: opening the Night Market spotlights the Hearth Spark with a live Pi Buy control (the spotlight is the only Pi-quotable SKU; every shelf card stays "Not on sale in Pi yet" by WO-1069/WO-1323, which is correct); tapping Buy runs quote -> Pi.createPayment -> approve -> complete on TESTNET and the grant lands. That purchase is the Pi portal's final checklist item before mainnet.
- Not signed in with Pi: the plate still reads the wallet-gate sentence.
- Gate: COMPILE_GATE_OK + REGRESSION_OK on `Builds/wave11-compile1` / `Builds/wave11-reg1` (chain 51), then the WebGL build + R2 parity + alias.

## 5. Not proven / open
- The purchase itself (owner, live). The Pi API key in Vercel was an 11-character placeholder until 16:20 today; the production redeploy that makes the new key visible to `/api/pi/approve` and `/api/pi/complete` is the owner's click (the seat is not permitted to run it). Without that redeploy, approve will fail at api.minepi.com with the placeholder key.
- Copy: the Pi plate says "Connect a wallet" where "Sign in with Pi" is the actual remedy (`storePiWalletGate`). Not changed here; a copy ticket if the owner wants it.
- Owner also reported "modal pop ups everywhere are not loading settings" in Pi Browser; no capture yet, no ticket minted.

## 6. RESULT (2026-09-10 23:42Z, Seeker, Pi Browser 1.17.1, build 2026.09.10.364108 on the alias)
Captured (web_trace): `createPayment ... sku=hearth-spark amount=52.72 Pi`, `approve OK paymentId=r33smtC4UjtMoqvemqewjJOBi24L`, `complete OK ... txid=51a1fd62cb5a6ad5fd453af62829df9fd180e8cff582fa12a4487daf5f95bb89`, `grant for 'hearth-spark' applied and journalled as settled`, `purchase COMPLETE`. Owner: "completed payment". This is the Pi Developer Portal's last checklist item before mainnet.

Two earlier attempts (23:28Z, 23:34Z) failed at approve with PI_PAYMENT_UNKNOWN (Pi upstream 404). Cause, proven by calling api.minepi.com directly: the payment was created under the app Pi Browser binds to the URL, whose consent sheet reads "Defenders of the Realm", while the API key configured at 16:20 belonged to the separate App Studio record "Echoes Of Elarion" (that key returned 404 for both payment ids and an empty incomplete_server_payments list; the Defenders of the Realm key returned 200). PI_NETWORK_API_KEY now holds the Defenders of the Realm key (Production + Preview, redeployed by the owner, dpl f0b86w6sk). Keep that app record; deleting it breaks payments again. Mainnet is a new app record and a new key (network fixed at registration), flipped together with PiEnvironment.Sandbox (WO-1325).
