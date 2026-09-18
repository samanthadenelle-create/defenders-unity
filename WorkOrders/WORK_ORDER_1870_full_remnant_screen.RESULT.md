# WO-1870 RESULT — the Circle screen (Remnants in Circles)

**Status:** FIXED PENDING DEVICE BUILD — lead gated 2026-09-18 (`Builds/cg1870c` COMPILE_GATE_OK 17:59; `Builds/reg1870b` REGRESSION_OK 584/584 18:03, Android target). Owner felt-verify on the next tester APK closes.

## What shipped (the three lane hand-backs in the WO carry file:line; this is the index)
- **Lane A** — `Assets/_Modules/HUD/Circle/{CircleWire,CircleScreenVM,CircleSource}.cs`: the VM (state machine, every verb, composed names `ComposeDisplayName` -> "Bob of RiverRun" / "Bob the Lonely", `ResultKeyFor` mapping the server's three ballot reasons), 16 signed call sites, `api/profile/usernames.js` (batch first-name read) + `test/profile-usernames.test.js` 12/12, EditMode fixtures `CircleScreenVMTests` (32) + `CircleErrorMapTests` (9). The Remnant name IS the existing `player_profiles.username` (3..16 token, unique + profanity gate untouched) — no migration, no second name column.
- **Lane B** — `CircleScreenPanel.cs` + `CircleScreenPanelBootstrap.cs`, `PanelId.Circle = 28` (`PanelRouter.cs:184-194`), door inside Circle Chat in a strip subtracted from the WebView area (`ClanChatPanel.cs`), "Claim your Remnant name" first for a nameless player, forms in the scroll column, no join-policy toggle, no vault register form, ballots render title/description only.
- **Lane C** — `CircleScreenRegression.cs` ([circle-screen], 18 cases) + the 128-key sidecar and the group-sense "Remnant" -> "Circle" rename (values only, keys unchanged); `site/clan-chat.html` title.
- **Locale merge (lead-verified)** — 131 keys (128 + WO-1872's 3) across 10 catalogs x 2 mirrors and 7 Unity tables, parity green after the reconciliation lane rebuilt the appended table rows inside `m_TableData` (root cause: the first merge appended them after the trailing `references:` node, invisible to Unity's reader).

## Owner rulings honoured (2026-09-18)
Remnant = the player; Circle = the group; composed name "Bob of RiverRun" / "Bob the Lonely"; perks visual + narrative only; joining never gated on staking; read-only vault; open-join toggle out of v1.

## Not proven here
Device layout of the screen and the door under the live WebView (no UI capture of this screen exists yet); the two-device Circle chat isolation test. Both are the owner's felt-verify on the next APK.

## Open
ar/ja/ko/zh-Hans values for the new keys are English (pending translation); `SmartFormatTag.m_SharedEntries` registration is partial for `{0}` rows (pre-existing convention, no parity effect); WO-1873 (global + Circle chat rooms) and WO-1874 (Ceremony of Vigil) follow.
