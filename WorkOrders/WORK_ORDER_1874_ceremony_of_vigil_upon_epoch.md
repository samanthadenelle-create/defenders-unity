# WO-1874 — The Ceremony of Vigil upon the epoch

**Status:** IMPLEMENTED — gated 2026-09-19 `COMPILE_GATE_OK` (`Builds/c1875d.log`) + `REGRESSION_OK 585/585` (`Builds/r1875c.log`).

**Owner, verbatim (2026-09-18):** "and the ceremony of Vigil upon epoch"

## What exists (read at source 2026-09-18, re-checked 2026-09-19)
- The epoch is Postgres's clock (`api/_lib/clan-ballot.js:30`, `:63`): the ballot closes itself on the first request after the boundary; `api/cron/heart-pulse.js` runs daily. The settle writes `clan_perks`; nothing in `Assets/_Modules` reads it yet (WO-1870 plan, section 0).
- Owner rulings 2026-09-18: perks are VISUAL + NARRATIVE only; the Circle's vigil weight is the sum of members' staked share x tenure (Heartbound / WO-1674-1675).
- Ballot tiers are integers 1..5 (`api/_lib/clan-ballot.js` `TIER_THRESHOLDS`). They have **no names in code**. These five words are that missing name column.
- ⛔ These words are **not** `HeartState` and **not** Heartbound resonance. Do not reuse either ladder.

## 1. The five tier words — LOCKED (owner draft 2026-09-19)

Map 1:1 onto ballot tiers. Never a number first. Never spoken as "tier 3".

| Ballot tier | Word | Bar (from `TIER_THRESHOLDS`) | Ceremony line |
|---|---|---|---|
| 1 | **Ember** | vigil_weight > 0 | A spark moves beneath the roots. The Circle has begun. |
| 2 | **Flame** | 86,400 | The Circle keeps its watch. The Heart is warm. |
| 3 | **Beacon** | 604,800 | The Heart answers. Light climbs the trunk. |
| 4 | **Pyre** | 2,592,000 | The crown blooms. The ancestors are near. |
| 5 | **Dawn** | 7,776,000 | The canopy shifts. The Circle has been heard. |

Voice matches `docs/narrative-bible.md`: short sentences, present tense, no fake archaic English.

Dawn's third sentence in the first draft ("The ancestors stir") is **cut on purpose**. That string is already the live Tier-5 perk title *and* the opening of its description (`api/_lib/clan-ballot.js:237-239`). The ceremony must not steal the perk's line. Ember sits next to Heartbound's **Emberbound** (personal resonance, `heartbound-resonance-config.json`); that adjacency is kept — personal spark vs Circle spark — and the two words must never be swapped on a plate.

### Shared plate (every tier; not the tier line)

1. "The Circle of {name} held vigil."
2. "{Word}." (Ember / Flame / Beacon / Pyre / Dawn — the title, not a number)
3. The one ceremony line from the table.
4. "The Circle of {name} has chosen {perk title}. The Heart has heard."
5. The winning perk's **description** (never `effect`).
6. One countdown line to the next epoch boundary, then CONTINUE.

Skippable after the first frame. Never blocks a raid in progress. Once per epoch per install; replay from Ballots ("Watch the last vigil").

## 2. Tree state spec (four decisions)

The Heart already has two visual machines. Ceremony is a **third axis** and must not fight them (`docs/specs/HEARTBOUND_TRIAGE_2026-09-10.md` Q-AXIS / Q-COLOR):

- **Threat** (`HeartState` in `HeartController.cs:45-61`): Serene / Vigilant / Warning / Danger / Critical / Boss / Victorious. Crystal hue + pulse. Combat tell. **Hands off.**
- **HP** (`HeartAuraController`): size / luminance / motion. Colour-free because the owner is colourblind. **Hands off.**
- **Vigil dressing (this ticket):** roots, Heartfire, wisps, crown bloom, Echo particles, ground markings. Density and motion, not hue.

### Visual state — what changes per word

| Word | Canopy | Light | Leaves | Particles | Inscription |
|---|---|---|---|---|---|
| Ember | unchanged | first glow at the roots | unchanged | sparse wisps at the base | none |
| Flame | unchanged | Heartfire at the roots | unchanged | denser wisps | none |
| Beacon | rim lights | pulse climbs the trunk | unchanged | wisps follow the pulse up | none |
| Pyre | crown bloom | crown + roots together | unchanged | Echo motes in the crown | none |
| Dawn | canopy dressing on | full height lit | unchanged | Echo motes + (ancestor silhouette **if** decision 3 says yes) | none |

Crystal colour never moves with this ladder. Leaves/bark stay on the HP axis (yellow at half wound, brown at deep wound — `docs/narrative-bible.md` §2). No carved inscription in v1.

### Transition rule

**Short ceremony, not instant, not hours.** On the next home-hub entry after the epoch has settled: ordered VFX prefabs (roots → trunk pulse → canopy), 2.5–4 s, then the plate. Same shape as the Heartbound pulse presentation (`docs/HEARTBOUND_ESSENCE_BRIEF_2026-09-16.md`). Marquee moments use ordered prefabs, never a second spawner. Hub `HeartState` ease (0.6 s) is a different machine — do not reuse it for this.

If both Seekers are already on the hub when the ballot closes, both play the ceremony then. If one is in a raid, that Seeker plays it on the next hub entry; the other does not wait.

### Duration

**Until the next epoch.** The dressing persists. It does not decay over hours. The next settle replaces it. No mid-epoch fade.

### What the player sees on next login

**The change, then the ceremony — not a notice, not nothing.** If `settledEpoch` > last-seen-epoch on this install: hub loads with the new dressing already on, then the plate plays once. Skip still leaves the new dressing. A later login in the same epoch does not replay (replay is the Ballots tab). If the tree changed while they were away, they still see the ceremony on that first hub entry; they do not get a toast instead.

## 3. Demo script, with the tree

Two Seekers, two wallets, one Circle. Stake stays in the native program the whole time.

1. Both open Circle. Both are in the same Circle. (Needs WO-1875 or the fresh-session Circle screen stays "Could not reach the server.")
2. A ballot opens at the Circle's current word (Ember…Dawn). Both vote.
3. The epoch boundary hits (Postgres clock, 48 h; a test settle is allowed for the take).
4. Both are on the home hub. The Heart plays the sequenced VFX on both screens at the same moment.
5. Plate: "The Circle of {name} held vigil." → **{Word}.** → the one ceremony line → "The Circle of {name} has chosen {perk title}. The Heart has heard." → perk description → next-epoch countdown → CONTINUE.
6. The dressing that belongs to that word is on. The stake never moved. The world is the record.

## Owner decisions (locked 2026-09-19)

1. Five tier words — **locked** as Ember / Flame / Beacon / Pyre / Dawn, with the table above.
2. Lonely Remnant ceremony — **v1: not at all.** No Circle, no plate.
3. Ancestor figure set — **v1: none.** Dawn is canopy + motes. No silhouette.

## Not in scope until ruled
Stat effects (ruled out), new art, sound (owner-ruled out of scope for localization; separate call for VFX audio).

## Honest remainder (not this ticket)

Five words close **this** spec. They do not close a live Circle demo by themselves:

- **WO-1875** — fresh Circle session cannot reach the server (boot never mints a wallet session). That is the live client blocker on Seeker `2026.09.18.375785`.
- **WO-1873** — Global + Circle chat, READY, not shipped.
