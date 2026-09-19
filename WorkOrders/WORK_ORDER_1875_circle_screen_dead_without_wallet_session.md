# WO-1875 — The Circle screen is dead without a wallet session ("Could not reach the server")

**Status:** IMPLEMENTED — gated 2026-09-19 `COMPILE_GATE_OK` (`Builds/c1875d.log`) + `REGRESSION_OK 585/585` (`Builds/r1875c.log`). Owner felt-closes on a tester APK: fresh boot → Circle → SIGN IN → Members.

**Owner, verbatim (2026-09-18, Seeker build 2026.09.18.375785):** "when i click circle chat says no circles" — then the Circle door opened a screen reading **"Could not reach the server. Showing what was last loaded."** with only REFRESH / CLOSE (`Logs/device/circle-202656.png`).

## The measured cause (`Logs/device/logcat-circle-unreachable-20260918.txt`)
- `:193010-193012` the wallet auto-resumed at boot: `Login connect bound save identity to wallet CHKK…sfkC`, then **`boot never signs (ruling 2026-09-07) ... There is no backend session to reuse (why=missing)`**.
- `:196430` `[Flow:Wallet] authed call has no live session and this route may NOT mint (no SignMessage here). why=missing ... /api/clan/me` -> `[Flow:ClanChat] clan/me NOT sent — could not attach auth (fail-closed)` -> `not embedding — reason=no_room` (so chat says "no Circle").
- `:196501` same for `/api/profile/usernames` -> `[Flow:Circle] name lookup could not be answered (http=0)` -> `refusal surfaced to the player: circle.error.unreachable` -> `state=Unreachable`.
- The server was never contacted. The player has a wallet and no session, and every Circle READ is `allowInteractiveSessionMint=false` by the WO-1870 plan (16 call sites in `CircleSource.cs`: reads false, writes true), so nothing on this screen can ever mint the session that the reads need. **A screen whose every entry path is a non-minting read is unreachable for every player who did not just perform a minting write elsewhere.**

## Ruling (lead, architecture; the 2026-09-07 "boot never signs" ruling stands)
1. Opening the Circle screen is an explicit player act, so its **entry read may mint interactively when the session is missing**: on open, if the attach fails for `why=missing`, the VM enters a new **`NotSignedIn`** state that renders the wallet-sign-in copy and ONE face, **SIGN IN** (`circle.signIn.face`), which performs the interactive session mint through the SAME seam a minting write uses (`allowInteractiveSessionMint=true` on that one deliberate call, respecting the 180 s signing ceiling), then re-runs the opening reads. Refresh / polling reads stay non-minting.
2. **The refusal is named correctly:** `why=missing` (no session) -> `circle.error.notSignedIn` ("Sign in with your wallet to see your Circle."), never `circle.error.unreachable` (which stays for http=0 with a live session). A no-wallet player (Play build) gets `circle.error.noWallet`.
3. `ClanChatPanel`'s `/api/clan/me` refresh (WO-1858) gets the same treatment: with no session the no-room copy reads `clanChat.notSignedIn` and the Circle door is the way in (the screen mints).
4. FlowTrace names the state transition (`NotSignedIn -> minting -> <result>`); Guard around the mint.

## Files (one lane, file-disjoint from everything live)
`Assets/_Modules/HUD/Circle/CircleScreenVM.cs`, `CircleSource.cs`, `CircleScreenPanel.cs`, `Assets/_Modules/HUD/ClanChatPanel.cs` (copy only), `Assets/Tests/EditMode/CircleScreenVMTests.cs`, `Assets/Editor/Regression/CircleScreenRegression.cs` (re-point `[circle-interactive-mint]`: exactly ONE read site may be true — the entry mint — and it must be the one gated on `why=missing`), sidecar `Logs/debug/scratch/sweep-sidecar-wo1875.json` for the new keys (`circle.signIn.face`, `circle.error.notSignedIn`, `clanChat.notSignedIn`; reuse `circle.error.noWallet` / `clanChat.noWallet` if present).

## RED-first
`[circle-screen]` gains: (F) with a source whose attach reports `missing`, the VM's state is `NotSignedIn` and `SignIn` is the only enabled verb; (G) after a successful mint the opening reads run again and the state leaves `NotSignedIn`; (H) `circle.error.unreachable` is never chosen for `why=missing`. Revert recipe: restore the unconditional `Unreachable` mapping -> F and H red.

## Acceptance
`COMPILE_GATE_OK` + `REGRESSION_OK n/n`; device: fresh boot, open Circle Chat -> Circle door -> SIGN IN -> wallet sheet -> the Members tab loads (or the claim-name step for a nameless player). Owner felt-verify closes.
