# WORK ORDER 1859 — Rename the player-facing "clan" term to "Remnant"

**Status:** READY TO IMPLEMENT

**Minted:** 2026-09-17, by the CLI lead, from the owner's ruling (see memory
`clan_renamed_to_remnant.md`): *"Remnant being the one that immediately tells me I'm playing Echoes
of Elarion, not Fantasy Game #4,782."* Ties into the game's own title (a remnant of a forgotten
civilization, restoring it together).

## Scope

**This is a content/copy edit to EXISTING locale keys, not a new-hardcoded-string problem.** Per the
standing localization law (WO-1857's own governing rule), every player-facing "clan" string should
already be routing through a locale key — confirm that's actually true for every hit below before
assuming it; a raw hardcoded "Clan" literal found during this sweep is itself a WO-1857-class defect
and should be fixed the same way (wired to a key), not just re-worded in place.

1. Grep every locale JSON (`Assets/Resources/Data/Canonical/*.json`,
   `Assets/StreamingAssets/Data/Canonical/*.json`) and `Assets/Localization/Tables/GameStrings_*.asset`
   for any English value containing "Clan"/"clan" (case-insensitive) and rename per this table:

   | Old | New |
   |---|---|
   | Clan | Remnant |
   | Create Clan | Create Remnant |
   | Join Clan | Join Remnant |
   | Clan Members / Clan Member | Remnant Members / Remnant Member |
   | Clan Rank | Remnant Rank |
   | (clan hall / clan HQ concept, if one exists) | Remnant Hall |
   | (clan quest concept, if one exists) | Remnant Quest |
   | (clan raid concept, if one exists) | Remnant Raid |
   | (clan war/conflict concept, if one exists) | Remnant War |

   Only rename concepts that actually EXIST today — do not invent new features (Remnant Quest/Raid/War)
   that have no current equivalent. If no "clan hall"/"clan quest"/"clan raid"/"clan war" concept
   exists in the shipped game yet, say so plainly in the hand-back rather than fabricating copy for a
   feature that isn't built. Only apply the corresponding rename where a real, shipped concept exists.

2. Translate the renamed English values into all 9 other locales, matching the register/tone of
   neighboring rows in each locale's file (placeholder-quality acceptable per WO-1857's own
   precedent — flag anything that reads awkward for a follow-up linguistic pass).

3. Do NOT rename internal code identifiers: `clan_id`, the `clans`/`clan_members`/`clan_messages`/
   `clan_reports`/`clan_rate_limit` tables, `ClanService`, `ClanFeatureGate`, `api/clan/*.js`,
   `api/_lib/clan*.js`, `ClanChatPanel`/`ClanChatVM`/etc. class names, git history, work order numbers
   and filenames (WO-1844 etc. keep their names), or this repo's own internal docs/comments that refer
   to "the clan system" as an engineering concept. Only the WORDS A PLAYER SEES change.

4. Check `site/clan-chat.html` and any Unity UI copy built from string concatenation (not just JSON
   keys) for a hardcoded "Clan" that also needs the swap — cite each one found.

## Non-scope

- Do NOT build the Remnant → Alliance → Kingdom progression tiers — that's real future design
  direction (recorded in memory `clan_renamed_to_remnant.md`), not this ticket's scope. This ticket
  is the rename only.
- Do NOT touch the actual gameplay logic of any clan feature (roles, rate limits, chat, leaderboard)
  — copy/locale only.
- Do NOT rename the git-tracked file/directory names for the clan API (`api/clan/`, `api/_lib/clan.js`,
  etc.) or any migration file — those are internal, not player-facing.

## Acceptance criteria

- [ ] Every player-facing occurrence of "Clan" in English locale files reads "Remnant" (or the
      correctly-mapped compound term per the table above, only for concepts that actually exist).
- [ ] All 9 other locales have a corresponding translated value for every changed key — no locale
      left with a stale "Clan"-equivalent word while English says "Remnant."
- [ ] `Assets/Localization/Tables/GameStrings_en.asset` (and any other language's `.asset` file, if
      those exist per-language) matches the JSON — `LocaleParityRegression` proves this, not prose.
- [ ] No internal code identifier is renamed (grep confirms `clan_id`, table names, class names, file
      names all unchanged).
- [ ] Full regression suite green (`REGRESSION_OK <n>/<n>`), `node --test test/*.test.js` before/after
      counts reported.

## Test plan

1. Grep the full diff for any renamed line; spot-check 5 non-English locales render sensibly.
2. Boot the game (or the relevant screen headlessly) and confirm the clan UI now reads "Remnant"
   throughout — screenshot if a UI surface can be captured headlessly per this repo's existing
   capture tooling.
3. Full regression suite green.

## Rollback

Revert the locale JSON and `.asset` file changes; straight content revert, no schema/code implications.

## Copy rules

Match the existing tone of the game's copy (warm, in-world, not corporate) — "Remnant" language should
read consistently with existing lines like "The Folk built these long before you," not as a dry system
label.
