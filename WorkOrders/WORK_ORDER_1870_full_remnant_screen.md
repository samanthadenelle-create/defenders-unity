# WO-1870 — The full in-game Remnant screen (create / join / members / leaderboard / vault / ballots)

**Status:** SPEC IN PROGRESS — minted 2026-09-18 14:40 (banner bumped 1870 -> 1871 in the same edit). Owner ruling 2026-09-18: **"Full Remnant screen"** (chosen over the minimal create/join door and over an API-only workaround). Prize-path work: the Remnant chat two-device test and the hackathon video need a player to be able to found and join a Remnant from the phone.

**Owner, verbatim (2026-09-18):** "i trioed to do the chat but there isnt a join chat option or join a remnant"

## The measured gap (read at source 2026-09-18)

- The chat panel's only no-Remnant state is a sentence: `ClanChatPanel.cs:50-56` `KeyNoClan = "clanChat.noClan"` -> en.json `:514` `"You're not in a Remnant yet."`. `ClanChatPanel.cs:383-386` deliberately avoids a "join a clan" imperative and offers NO door.
- The 09-17 status sweep found **no Create / Join / Members / Leaderboard UI anywhere in `Assets/_Modules`** (WO-1859 rename record `:98-119`): only the chat panel, 5 `clanChat.*` keys, 3 titles and 2 chat-wheel phrases exist.
- The SERVER side is built: `api/clan/` holds `create.js`, `join.js` (POST `{playerId, code}`, code uppercase-normalised, `CLAN_BAD_CODE` vs no-such-code are distinct, `:4-16`), `leave.js`, `me.js` (`{ok, clan:{clanId, code, name, tag, joinPolicy, createdAt, memberCount}, role, joinedAt}` per `ClanMembershipClient.cs:14-15`), `promote.js`, `demote.js`, `kick.js`, `leaderboard.js`, `vigil.js`, `report-message.js`, plus `ballot/` and `vault/` directories. Migrations 0030-0037 (tables, reports, rate limit, join policy, messages, ballots, vaults) are applied on live Neon as of 2026-09-18 14:03 (`MIGRATIONS_OK applied=3 skipped=34`).
- Open-join is stored (`clans.join_policy`) but `join.js` still requires a code (WO-1851 `:133-138`).

## Scope (owner-ruled: FULL)

One `PanelId.Remnant` screen, code-built uGUI through the existing kit (never UXML), MVVM strict (VM holds every state + verb, View is a skin), reached from the SAME dock entry that opens Remnant chat today and from the chat panel's no-Remnant state:

1. **Not in a Remnant:** Create (name + tag, join policy invite/open) and Join by code, with the server's distinct errors surfaced in plain language.
2. **In a Remnant:** header (name, tag, my code with a copy/share affordance, member count, my role), **Members** list with roles and the leader/officer verbs promote / demote / kick, **Leave** (confirm), **Leaderboard**, **Vault**, **Ballots** — each tab bound to its existing `api/clan/*` route; a tab whose route returns not-ok shows a traced empty state, never a blank.
3. **Chat** stays where it is (WO-1858 WebView panel); this screen links to it.
4. Every string is a locale key across all 10 catalogs + 7 tables (localization law, WO-1857 shape); ASCII-only TMP; colour never carries meaning alone (owner is red/green colourblind); touch floor per `HudDockSlotLayout`.
5. Instrumented from the first line: `FlowTrace` at open / each fetch / each verb / each refusal; `Guard.TryEach` around every list build.

## Not in scope
Cherry chat internals; server-side open-join enforcement (separate ticket); admin review of `clan_reports`.

## Plan
The design lane appends the implementation plan below (VM field list verbatim, API contract per route read from `api/clan/*.js`, pin list with file:line, file-disjoint lane split, RED-first suite spec, revert recipe per case). Until it does, this WO is a SPEC, not READY.
