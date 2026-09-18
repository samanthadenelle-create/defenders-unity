# WO-1873 — Chat at a GLOBAL level and at a CIRCLE level

**Status:** READY TO IMPLEMENT — minted 2026-09-18 16:40 (banner bumped 1873 -> 1874 in the same edit). Prize-path (the social loop). ⚠ Dispatch AFTER WO-1870 lanes B and C land: this touches `ClanChatPanel.cs` and `site/clan-chat.html`, which those lanes own right now.

**Owner, verbatim (2026-09-18):** "there should be chat as a global level and then chat at a circle level"

## Ruling
Two rooms, one chat surface, one Cherry embed:
1. **Global** — every Remnant, one fixed world room. Open to a player with no Circle (Bob the Lonely can talk; it is where he finds one).
2. **Circle** — the existing per-Circle room (WO-1847/1858), unchanged in isolation: a third wallet in another Circle never sees it.
A room selector (two faces, GLOBAL / CIRCLE) sits at the top of the chat panel; CIRCLE is dimmed, never hidden, while the player has no Circle and its caption says why. The panel remembers the last room per install (PlayerPrefs, a per-viewer convenience, never state).

## Seams (read at source 2026-09-18)
- `site/clan-chat.html:49-50` mounts ONE `roomId` per page load from the Unity host's query string, validated as a UUID (`:126`, `:165`). Global = a second, FIXED UUID minted once and held in `api/_lib/clan-chat` config + mirrored as a constant the client reads from `/api/clan/me` (never hardcoded in C#), so it can rotate server-side. The page accepts `roomId` + a `roomKind=global|circle` and mounts the one it is given; switching rooms reloads the embed (Cherry `getUnreadCount(roomId?)` at `:63` can badge the other face).
- `Assets/_Modules/HUD/ClanChatSource.cs:52-58` `ClanRoomBinding` holds one clan id and raises `Changed`; extend with `ActiveRoom` (Global|Circle) and a `GlobalRoomId` fed from `/api/clan/me` (WO-1858 refresh path), so the WebView host (`ClanChatPanel.cs`, thin host per WO-1858) rebuilds its URL on room change.
- Report-message (`api/clan/report-message.js`) must carry the room kind; global reports land in `clan_reports` with `clan_id` null — check the column is nullable, add a migration `20260918_0038_clan_reports_global_room.sql` if not (ledger-driven, `tools/run-migrations.mjs`).
- Feature gate: `ClanFeatureGate.PlayerFacingEnabled` covers both rooms.

## Not in scope
Moderation tooling; Cherry display names (Cherry's own); voice.

## RED-first regression
`[chat-rooms]` suite: (A) the panel exposes exactly two room faces; (B) with `ClanRoomBinding.ClanId == null` the CIRCLE face is present and not interactable and GLOBAL is interactable; (C) the page source validates `roomKind` and never mounts two rooms; (D) `/api/clan/me` returns `globalRoomId` as a UUID (node test). Revert recipe per case.

## Acceptance
`COMPILE_GATE_OK` + `REGRESSION_OK n/n`; device: a Lonely Remnant talks in Global; two Remnants in one Circle talk in Circle; a third in another Circle sees Global only. Owner felt-verify closes.
