# WO-1874 — The Ceremony of Vigil upon the epoch

**Status:** SPEC — minted 2026-09-18 16:40 (banner bumped 1874 -> 1875 in the same edit). Prize-path: the "ancestors wake" beat is the video's payoff for the Circle loop. Needs the owner's word on the beat sequence below before it is READY.

**Owner, verbatim (2026-09-18):** "and the ceremony of Vigil upon epoch"

## What exists (read at source 2026-09-18)
- The epoch is Postgres's clock (`api/_lib/clan-ballot.js:30`, `:63`): the ballot closes itself on the first request after the boundary; `api/cron/heart-pulse.js` runs daily. The settle writes `clan_perks`; nothing in `Assets/_Modules` reads it yet (WO-1870 plan, section 0).
- Owner rulings 2026-09-18: perks are VISUAL + NARRATIVE only; the Circle's vigil weight is the sum of members' staked share x tenure (Heartbound / WO-1674-1675).

## Proposed beat (the owner redirects; nothing here is built yet)
When the game first learns that the player's Circle's epoch settled since their last visit (a `settledEpoch` on `/api/clan/vigil`, compared to a per-install last-seen epoch), on the next entry to the home hub:
1. **The tree stirs.** The Heart of Elarion plays a sequenced VFX (memory: marquee moments use ORDERED prefabs, not one-shots): roots glow, a pulse climbs the trunk, the canopy lights.
2. **The Circle is named.** A full-screen ceremony plate: "The Circle of RiverRun held vigil." with the Circle's vigil weight as a tier word (Ember / Flame / Beacon / Pyre / Dawn — five words for the five tiers), never a number first.
3. **What woke.** The winning ballot's title + description (never its effect), read from `clan_perks`; the visual outcome applies (the perk's tree dressing / an ancestor figure appears beside the Heart).
4. **The vigil continues.** One line naming the next epoch boundary as a countdown, then a single CONTINUE face. Skippable after the first frame; never blocks a raid in progress.
Playable once per epoch per install; replayable from the Circle screen's Ballots tab ("Watch the last vigil").

## Owner decisions needed
1. The five tier words above (creative; the owner names them or approves).
2. Whether the ceremony fires for a Lonely Remnant (no Circle) as a quieter "you kept vigil alone" beat, or not at all.
3. The ancestor figure set for v1 (one silhouette per tier, reusing existing NPC/KayKit rigs, or none until art lands).

## Not in scope until ruled
Stat effects (ruled out), new art, sound (owner-ruled out of scope for localization; separate call for VFX audio).
