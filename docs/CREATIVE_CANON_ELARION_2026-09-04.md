# CREATIVE CANON — Elarion, and what lies beyond the Heart (2026-09-04)

> # ⭐ THIS IS THE CREATIVE NORTH STAR. IT TAKES PRECEDENCE ON FICTION, NAMING AND COPY.
>
> Owner ruling 2026-09-04, delivered as a full creative direction and then **revised by its own author
> into a stronger second version**, which she closed with ***"That is the version I'd build around."***
> The second version is what this file records. See §2 for exactly what it superseded, so nobody
> implements the first draft by accident.
>
> **Its relationship to the economy map is clean and worth stating once:**
> `docs/PROGRAM_RAID_ECONOMY_2026-09-04.md` rules **numbers, ladders and release order**. This file
> rules **fiction, naming and copy**. Neither overrides the other, because they do not overlap — and
> the whole point of this direction is that it **changes no economy ruling whatsoever**. Every number
> in the map survives intact.
>
> ⚠ **Load-bearing, per CLAUDE.md §15.** Joins the read-first canon set. Any change to the premise, a
> name, or a shipped string updates this file IN THE SAME COMMIT.

**Status:** CANONICAL · takes precedence on fiction/naming/copy
**Minted:** 2026-09-04 (CLI, from the owner's creative direction)

---

## §0. THE IDENTITY — the locked paragraph

> *Elarion is a realm that has forgotten itself.*
>
> *The Heart, an ancient world tree at the centre of your settlement, preserves the Echoes of those who
> came before. But its memories were shattered when the Realm fell.*
>
> *Rebuild beneath its branches. Awaken the Echoes it still remembers. Raise armies and carry Heartfire
> beyond its fading reach. Reclaim forgotten lands, recover what was lost, and piece together the truth
> of what happened to Elarion.*
>
> *Because beyond the Heart lie places it cannot remember.*
>
> **And some of them remember you.**

⭐ **The three-word spine, and the thing to test every future system against:**

| Element | Is |
|---|---|
| The Heart | **light** |
| The Echoes | **memory** |
| The player | **reclamation** |

**Progression is not** *get stuff → get stronger → get more stuff*. It is:

> ## The stronger Elarion becomes, the farther from the Heart you can go.

---

## §1. THE PREMISE

The Heart does not merely protect souls. **The Heart remembers Elarion.** When someone dies within its
reach their essence can remain as an Echo. When the Realm fell, the Heart itself was damaged, and
pieces of its memory scattered.

**The hook:** *the world outside the settlement is forgetting what it used to be.* Places distort.
People lose themselves. Old guardians turn hostile. Fortresses repeat the final days before the fall.
The farther from the Heart you travel, the less stable reality becomes.

So the enemy is not a cartoon villain sitting on 1,800 wood. **Raids are reclamation expeditions into
fractured territory** — you recover the physical resources Elarion needs to survive, and you push the
Heart's influence back into land it has forgotten.

> **At home you restore. Beyond the Heart you reclaim.**
> And the Echoes are the bridge between the two.

⛔ **This settles the question the whole brief was built around** (*"why do I raid?"*) without making
the player a cheerful village pillager, and without a lore bible: the answer is one premise that every
existing system was already reaching for.

---

## §2. ⚠ WHAT THE SECOND VERSION SUPERSEDED — read before implementing anything

The direction arrived in two passes. The second is canon. **Implementing a first-pass name is a defect,
not a preference**, so the table is explicit:

| Item | First pass | ⭐ CANON (second pass) |
|---|---|---|
| Premise | darkness spread beyond the protective light | **the world beyond is FORGETTING what it was** |
| Raid charges | "Marches" | **Heartfire** — self-reconsidered: *"Mechanically good. Fictionally still a little administrative."* |
| Target 1 | The Splinter Camp | **The Forsaken Camp** |
| Target 2 | Ironwatch Garrison | **The Broken Garrison** |
| Target 3 | The Ashen Enclave | **The Veiled Enclave** |
| Target 4 | The Blackiron Bastion | **The Iron Bastion** — *"Keep that name. It's strong."* |
| Failure screen | FORCED RETREAT | **THE HEART'S REACH FAILED** |
| Victory screen | a recovered chest arriving home | **MEMORY RECLAIMED**, then the supplies |
| Echoes in raids | a companion who recognises old roads | **an Echo GUIDE chosen before each expedition** |

**Survives from the first pass, unchanged and still canon** (the second pass revised, it did not
replace): Realm Vigil (§5), mercenaries as the same unit (§6), the Journey subtitles (§8.4), Season I
(§11), the jewelry chain (§12), and the manual-retreat screen (§8.3).

⚠ **One reconciliation the owner did not have to make, recorded as an assumption she can overturn in a
word:** the charge is **Heartfire**, but **"march" survives as the verb**. You spend Heartfire; you
march. So `MARCH AGAIN` stays a button and `MARCHES` never appears as a noun for the resource.

---

## §3. THE FOUR RAID TARGETS

Escalation must be readable at a glance, and the sequence tells a story that gets quietly worse:

| # | Name | Line on the target card |
|---|---|---|
| 1 | **The Forsaken Camp** | Scavengers strip an abandoned settlement the Heart can no longer reach. |
| 2 | **The Broken Garrison** | Its soldiers still guard their post, though no living commander remains to give the order. |
| 3 | **The Veiled Enclave** | Something inside has learned to bend fractured memories into magic. |
| 4 | **The Iron Bastion** | **The Heart remembers no fortress here.** |

⭐ **That fourth line is the engine of the game's mystery, and it should be treated as such.** If the
Heart remembers Elarion, why does it not remember the Bastion? The Bastion is also the **evergreen
enemy** — the player is meant to eventually realise *we are not destroying the darkness, we are pushing
it back*, which is what makes an endless difficulty ladder feel like a story instead of a number going
up.

> ⛔ **IMPLEMENTATION RULE — display names only.** The stable ids `raider_camp_small`,
> `fortified_garrison`, `mage_enclave` are **live save keys and must NOT be renamed** (memory
> `structure-role-enum-and-format-normalization`). Change `displayName` and fill the **empty
> `description` field** in `Assets/Resources/Data/Canonical/scene-configs.json` — verified empty on all
> three this session, which is why the target card has a slot and nothing in it. Keep the
> StreamingAssets twin byte-identical; **Resources wins at runtime**.

---

## §4. HEARTFIRE — the charge, and the timer that stops feeling like a timer

**"Raid Orders" is dead.** The player is the ruler; nobody is issuing them orders.

**Heartfire** is the Heart's ability to sustain an expedition beyond its own reach. Three charges. One
rekindles every four hours. Stacks to three, so sleeping or working is not punished.

```
🔥 🔥 🔥        HEARTFIRE

🔥 🔥 ◌         Heartfire rekindles in 3:42:18
```

> The same integer, and a completely different experience. Not *you may not raid because TIMER*, but
> **the Heart is not ready to send you back yet.**

### ✅ RULED 2026-09-05 — THE PLATE SAYS WHAT A CHARGE **BUYS**, AND SOMETHING FINALLY INTRODUCES IT (WO-1415)

> Owner, felt-test on build 2026.09.05.356468: ***"Heartfire is full, i dont understand as a new
> player what to do with that. No one in game has introduced me to heartfire."***

⚠ **The mock above is superseded as SHIPPED COPY** (its fiction stands; only the words on the plate
moved). Measured that morning: `guide-content.json`, `dialogues.json` and `tutorial-steps.json`
returned **ZERO** mentions of "heartfire" between them, while WO-1379 had just made it the ONE gate on
raiding — so the mechanic that decides access to the core loop was the one mechanic the game never
explained, and `Heartfire is full` reported a STATE with no consequence attached.

- **THE SENTENCE, in the owner's words: *"each one sends you on a raid"*.** One owner in code —
  `DeNelle.Core.State.HeartfireCharges.SpendSentence` — carried verbatim by the guide entry and by the
  introduction beat; `HeartfireRegression` PIN H reds if either file stops carrying it.
- **THE PLATE USES PARENTHESES, NOT THE SENTENCE** (owner ruling, and the reason is measured): the
  Heartfire row is ONE fitted line inside the WO-1384 Heart plate, and a sentence ellipsises there.
  Charged: **`Heartfire 3/3 (raids)`**. Spent: **`Heartfire 0/3 (raids) - next in 3h 12m`**. The three
  greyscale-safe marks stay; a full pool no longer paints a second row at all. Both strings are
  composed by `HeartfireCharges.PlateLabel` / `PlateRekindle` and pinned byte-exact (PIN G).
- Those English phrases remain the semantic source of truth, while the player-facing Heart plate resolves
  `hud.heart.*` through `HeartHudText` and `LocalText`. Every required region must update with an English
  wording or argument change; translated grammar may reorder named values without changing the mechanic.
- **INTRODUCED AT THE FIRST RAIDS-GRID OPEN, never at founding** — a new player has no raid to spend a
  charge on. One non-mandatory, one-shot dialogue beat (`ctx_heartfire` → `tut_ctx_heartfire`), latched
  on `seenTutorials`, no schema bump; the mandatory chain stays at eight.
- The refusal (`HeartfireService.BlockedMessage`) is UNCHANGED and still names the precise wait.

⛔ **Heartfire is a CHARGE, not a currency.** It is never earned, traded, stored, gifted or bought.
Economy map §3 (*do not add another currency*) is **not** violated and must not be read as licence to
add one.

### ✅ RULED 2026-09-04 — HEARTFIRE REPLACES THE PER-CAMP WALL

> Owner, asked directly: **"Heartfire replaces the camp wall."**

**One gate on WHEN you may raid, and it is Heartfire.** The per-camp `raidCooldownSeconds` lockout
(4h / 8h / 12h) is **RETIRED as a gate**. Three expeditions is the limit; the player spends them
wherever they like.

⛔ **The once-per-UTC-day crystal stamp SURVIVES and is NOT part of this ruling.**
`RaidClaimService.CrystalsPaidToday` is the monetisation guard, keyed on the UTC day rather than on the
cooldown window, and it bounds the one unbounded faucet. It gates **what a clear PAYS**, not **whether
you may go** — a different axis, and retiring it was never asked for.

**Why this is safe, worked through rather than assumed:** the obvious worry is that a single gate makes
re-clearing the easiest camp the optimal play. It does not, because camps carry an escalating
`rewardMultiplier` (1.0 / 1.5 / 2.2 at `scene-configs.json:106,169,224`), so the best use of a charge is
**the hardest camp you can actually beat**. The single gate turns camp choice into a real decision
instead of a rotation forced by three staggered timers.

⚠ **IMPLEMENTATION NOTE — this is a RETIREMENT, not a re-tune.** Do not shorten `raidCooldownSeconds`;
that file's own authoring note explains at length why those hours are not the lever. Retire the gate,
and record in `scene-configs.json` that the field is superseded rather than deleting it silently.

<details><summary>Superseded: the three-gate analysis this ruling answered</summary>

### ⚠ CONFLICT — three gates would stack, and someone must rule

Two pacing gates already ship, and Heartfire is a third:

| Gate | Where | Shape |
|---|---|---|
| **Per-camp cooldown** | `scene-configs.json` `raidCooldownSeconds` — 4h regular / 8h hard / 12h extreme | *that camp* is broken for now |
| **Crystals once per UTC day** | `RaidClaimService.CrystalsPaidToday` | further clears pay reduced ordinary resources and zero crystals |
| **Heartfire** (NEW) | global | *the Heart* can only sustain three expeditions |

**Recommendation, not a ruling:** keep all three, because under the new fiction each has its own true
reason — Heartfire is the Heart's strength, the camp cooldown is that foothold being broken, and the
daily crystal stamp is the monetisation guard. They only become punishing if a player is ever blocked
by two at once with no third target available, so **the acceptance criterion is that a player holding
Heartfire always has somewhere to spend it.** ⛔ Do NOT shorten `raidCooldownSeconds` to make room —
that file's own authoring note explains at length why those hours are not the lever.

</details>

---

## §5. REALM VIGIL — resolving the "threat" collision

**Threat** stays with the endless Iron Bastion ladder (Threat I / II / III…, +8% enemy strength and
+5% loot per level).

The weekly 1–10 climb becomes the **Realm Vigil** — Vigil I → Vigil X, reset weekly, and a reset is
*the beginning of a new Vigil*. Thematically the kingdom keeps a vigil against what lies beyond the
Heart; conversationally it passes the only test that matters for a weekly ladder:

> *"What Vigil did you reach this week?"*

---

## §6. MERCENARIES — the same unit, and nothing more

> ⛔ **NO second roster system. No upkeep, contracts, expiration, mercenary inventory, or Mercenary
> Management Simulator 2026.**

Gold fills the remaining ranks *immediately* with battle-ready outsiders who are, mechanically, the
same Footman/Archer/etc. The button says:

```
HIRE REINFORCEMENTS — 300 Gold
Fill the remaining ranks immediately with battle-ready mercenaries.
```

not `Skip Training — 300 Gold`. **Flavour wins; scope stays dead.** This closes the fork that was
blocking implementation — the ruling is the cheap half, deliberately.

---

## §7. ECHO GUIDES — ⛔ USE THE SIX WE HAVE

> ## ⛔ THE ECHO NAMES IN THE CREATIVE DIRECTION DO NOT EXIST IN THIS GAME.
> Sylas, Thrain, Grom and Elara were illustrative. Owner, asked directly: ***"Whatever we have use
> those."*** The real roster is **Aldwin, Elowen, Corvin, Bran, Doran, Maren**, verified this session
> at `Assets/_Modules/Village/Harvest/EchoRosterCatalog.cs:149-222` (identity lives in that CODE table,
> never in JSON — `echoes-balance.json` holds numbers only). ⚠ A seat implementing the illustrative
> names would ship four characters the game has never had.

**The mechanic:** before each expedition the player chooses an **Echo Guide**. The Echo does not fight.
**It remembers.** The Heart cannot see clearly beyond its protection; an Echo can recognise fragments
of the world that existed before.

⭐ **This is not new work.** `EchoWorldPresence` already escorts the player to the gate and returns once
after the battle (WO-1108 Lane B, one owner, one lifecycle). **Make the existing behaviour canon and
give it a voice** — do not build a second spawner.

**The recognition domains are derived from each Echo's SHIPPING lore, not invented** — every one is a
quotation from the roster catalog:

| Echo | Shipping lore | Recognises |
|---|---|---|
| **Corvin**, the Void Echo | *"the scout who ranged the far dark for Elarion and never came home… mapping the dark so others need not fear it"* | **roads and far paths — the natural first Guide, and the only Echo who has already been out there** |
| **Elowen**, the Nature Echo | *"the grove-warden who once walked Elarion's every furrow"* | land, growth, what the wild has taken back |
| **Bran**, the Storm Echo | *"the watchman who held Elarion's wall through every gale… calling every alarm in time"* | fortifications, garrisons, defensive works |
| **Doran**, the Earth Echo | *"the mason who raised Elarion's stones… laid the first stones of Elarion's walls"* | masonry — **who built this, and whether Elarion hands built it** |
| **Maren**, the Fire Echo | *"the hearth-keeper whose forge never went cold"* | forges and hearths — the domestic remains; *someone lived here* |
| **Aldwin**, the Ice Echo | *"a keeper of the old light… the first it kept"* | the old light's boundary, and Elarion's former holdings |

**The payoff, in the owner's own framing:** you do not need forty cinematics, you need **little
memories**. Two lines, same road, different Guide:

> **Corvin:** *"Wait. I've walked this road."*
> **Bran:** *"Road? This was a defensive trench."*

⭐ **THE HOOK THIS ROSTER MAKES POSSIBLE, and the reason using our own six is better than the
illustrative names:** send **Doran** — the mason who laid Elarion's first stones — to the Iron Bastion,
the one place *the Heart remembers no fortress*. He recognises the stonework. Nobody has to explain why
that is frightening.

Later, the line that stops the player thinking about gold:

> **Aldwin:** *"…there's someone here."*
>
> **RULED** (owner: *"Map however you like"*, 2026-09-04). Aldwin carries it because he is the FIRST
> soul the Heart kept and a keeper of the old light — so he is the one Echo whose recognition of a
> person out there implies the Heart's memory is incomplete rather than merely distant. The player
> stops thinking about 2,200 gold and starts wondering **who**.

⚠ The illustrative line addressed the player by first name. **ANSWERED 2026-09-04 (WO-1380): THIS GAME
KNOWS NO PLAYER FIRST NAME** — the only persisted `*name` field is `SaveSchema.cs:239`'s `petName`, and
that names the starter Echo (`GameState.cs:290-297`, WO-277). The line ships **without** a name and is
better for it. Full evidence + the regression that keeps a placeholder out: §14 item 2.

⭐ **The 24 lines are AUTHORED and SHIPPING** (WO-1380): `Assets/Resources/Data/Canonical/
echo-guide-memories.json` (+ its StreamingAssets twin). Six Echoes x four targets — the fourth,
`iron_bastion`, **IS** authored in `Assets/Resources/Data/Canonical/scene-configs.json:241`
(`sceneName` `RaidBase_IronBastion` at `:245`) and **IS** registered in `EditorBuildSettings`, with
`enabled:0` pending a re-bake that adds a `HeroStartPoint_PlayerSpawn` marker — that scene entry's own
authoring note at `:249` carries the measurement and the one-flag finish. So the target is
forward-declared as *not yet playable*, never as *not yet declared*. ⛔ Reword a line **here first**:
`EchoGuideMemoryRegression`
pins Corvin's road, Doran's courses and Aldwin's closing beat verbatim, and FAILS below 24 lines.

---

## §8. THE COPY SET

### ⛔ 8.0 THE WRITING RULE — QUESTIONS BEFORE ANSWERS (owner ruling 2026-09-04, BINDING on every line)

> **Especially early.** A line that raises a question the player wants answered beats a line that
> answers one they never asked.

The worked example, because it is the difference between the two and it is not subtle. **Doran, at the
Iron Bastion, must NOT say:**

> ~~*"These stones use the ancient masonry technique employed by the third-age wardens of…"*~~
> Owner's verdict on that draft: **straight into the sea.**

He says:

> **Doran:** *"I know this stone."*
>
> *(a beat)*
>
> **Doran:** *"I laid it."*

⭐ **Four words, and the player starts the next raid.** No lore dump, no villain monologue, no
encyclopedia — just a fact that should not be possible, sitting next to *"The Heart remembers no
fortress here."* That is environmental storytelling, and it is the standard every memory line is held
to. The same restraint is why **Aldwin's** *"…there's someone here"* works: it carries enormous
implication and explains nothing.

⛔ **This rule outranks completeness.** A writer who fills all 24 slots with explanations has failed the
brief more thoroughly than one who leaves a line short. When in doubt, cut the second sentence.

### 8.1 Teaching the loop — the beats that do not exist today

⛔ **Nothing in the shipping game ever tells a player that raiding is how they get richer.** Verified
this session across the tutorial beats, `guide-content.json`, the daily quests and the Journey screen.
The Guide's opening line is *"Raids are where your trained troops earn their keep"* — a sentence about
payroll — and it must be rewritten along with everything below.

> ## ⛔ ONE CONTINUOUS EVENT. NOT A LORE SEQUENCE AND A TUTORIAL SEQUENCE.
> Owner ruling 2026-09-04: **the FTUE teaches fiction and economy SIMULTANEOUSLY.** There is no
> `LORE MOMENT` beat followed later by a `RAID TUTORIAL` beat. ⭐ **The three free Footmen are the
> bridge** — the grant is both the story moment and the economic unlock, so the player learns fiction,
> navigation, combat, the reward economy and the progression loop **in one unbroken run**.
> **No encyclopedia required**, which is the entire test.

| # | Beat | String |
|---|---|---|
| 1 | Barracks completes | *Your Barracks stands ready.* |
| 2 | Three Footmen appear | *Three soldiers answer Elarion's call.* |
| 3 | **Corvin arrives** ⭐ | **Corvin:** *"There's movement beyond the Heart."* |
| 4 | Directed to Journey → Raids | *Beyond the Heart lie hostile camps holding resources Elarion needs.* |
| 5 | Target card | **THE FORSAKEN CAMP** — *Scavengers are stripping an abandoned settlement beyond the Heart's reach.* |
| 6 | Spend the first Heartfire | *(the charge is spent, and the spending is the lesson)* |
| 7 | Fight, win, resources return | — |
| 8 | Victory | **MEMORY RECLAIMED** — *The Heart remembers this place.* |
| 9 | The economy, taught explicitly | *Recovered Gold, Wood and Iron strengthen Elarion and prepare your army to venture farther.* |
| — | The loop, in six words | **Every victory opens the road farther.** |

⭐ **Beat 3 is the one that does the most work and is easiest to cut by accident.** Corvin is *"the
scout who ranged the far dark for Elarion and never came home"* — having him be the one to notice
movement beyond the Heart introduces the Echo Guide mechanic, the premise, and the reason to go, in a
single seven-word line. He is also the default Guide (WO-1380), so the player's first expedition is
led by the Echo who already knows what is out there.

⚠ **The first raid must be reachable within MINUTES of the Barracks completing**, not hours. That is
what the free army exists for — economy map §2, *"one raid teaches the entire economy."*

### 8.2 Victory — a homecoming, not a receipt

Combat ends. The Echo approaches the focal object. **The Heart symbol pulses. A little colour and
warmth return to the environment.** Then:

```
        MEMORY RECLAIMED
    The Heart remembers this place.

        RECLAIMED
        Wood   +1,800
        Iron   +1,100
        Gold   +2,200
        ⭐ ⭐ ⭐

        REALM VIGIL ↑
        Elarion grows stronger.

    [ RETURN HOME ]   [ MARCH AGAIN ]
```

Performance stars are **three stamped campaign seals**, not mobile-game gold stars.

> The numbers become the consequence of the adventure instead of being the adventure.

### 8.3 Failure — you do not lose the memory, you fail to reclaim it

**Lost raid:**
```
    THE HEART'S REACH FAILED
Your Echo held the path long enough for the survivors to escape.

        Brought Home
        Wood +270 · Iron +135 · Gold +330
        Nothing carried home is wasted.

    [ REBUILD RANKS ]   [ RETURN HOME ]
```

**Manual retreat** (survives from the first pass, and is a different thing — the player made a
decision):
```
    TACTICAL WITHDRAWAL
You called your soldiers home before the battle was lost.
Survivors and recovered supplies have returned to Elarion.
```

⭐ This makes the map's deliberately generous 15–20% failure payout **diegetic** — the number was
always right and never had a reason attached to it. Now it does.

### 8.4 Journey — five fantasies, not five mechanics

| Card | Subtitle |
|---|---|
| QUESTS | Answer the needs of Elarion. |
| RAIDS | March beyond the Heart. Reclaim what was lost. |
| DUNGEONS | Descend into forgotten places. Return with what survived. |
| REALM MAP | Explore the lands beyond the Heart's light. |
| SEASON | Face the challenge shaping the Realm now. |

**"Journey" stays** — it is stronger once the cards are filled.

---

## §9. THE HEART AS THE MASTER PROGRESSION VISUAL

The recovering → thriving tree artwork already exists. **Do not let the Heart merely level up. Let it
remember.**

Early: damaged, quiet, sparse Echoes. As the player builds, quests, raids and explores — more leaves,
more particles, more Echoes visible around it, old inscriptions illuminating, branches extending, and
eventually the settlement behind it growing warmer.

> Account progression becomes *"I am bringing Elarion back"* rather than *Town Hall Level 17.*

---

## §10. SEASON I — *Beyond the Heart*

Do not open with cosmic apocalypse fireworks. **The first season's job is to teach this fiction.**

> *For the first time since Elarion began rebuilding, scouts have found old roads beyond the Heart's
> protection. Something has occupied them. The settlement must venture outward.*

Visually the season moves from warm Heart-lit Elarion into colder ruined territory. Rewards share
motifs: old expedition gear, recovered Elarion heraldry, weathered banners, reclaimed armour, ancient
maps, lanterns, forgotten symbols.

---

## §11. JEWELRY — dungeons get the identity

⛔ **Otherwise raids become the vending machine that dispenses everything.** The activity split:

| Activity | Pays |
|---|---|
| **Raids** | economy resources |
| **Dungeons** | rare materials / artifacts |
| **Quests** | directed progression |
| **Seasons** | cosmetics / status / special rewards |

**Rough Stones are primarily a DUNGEON drop**, with extremely rare high-tier raid drops later if
crossover is wanted. Jeweler NPC: **Mirelle the Facetkeeper**; the building stays plain *Jeweler*.

**Rarity ladder:** Rough → Cut → Refined → Radiant → **Echoed**

⭐ *Echoed* as the top tier belongs to this game instead of Generic Fantasy Rarity™.

⚠ Must not double-pay against the economy map's §1 reward table (map §12.3).

---

## §12. ⭐ THE PROOF THIS IS NOT A RETROFIT

The direction closed with *"most of the pieces were already pointing here, they just weren't holding
hands yet."* **That is literally true, and it is verifiable in the shipping data.** Recorded here
because it is the strongest argument for adopting this premise, and because a future seat will
otherwise assume the fiction was bolted on:

| Already shipping | Says |
|---|---|
| `canon-strings.json:53` — the game's tagline since 2026-07-24 | **"Echoes of a Forgotten Civilization"** |
| `glossary.json:34` — the definition of an Echo | *"**The world tree remembers every soul it has kept**, and wakes one to work beside you."* |
| `EchoRosterCatalog.cs:178` — Corvin's lore, written months ago | *"mapping the dark so others need not fear it… **His Echo reaches what no other can**"* |
| `EchoWorldPresence` (WO-1108) | already escorts the player to the gate and returns after the battle |
| `RaidBase_IronBastion.unity` | baked, tooled, and never switched on |

The premise **"a realm that has forgotten itself"** and the tagline **"a forgotten civilization"** are
the same sentence. This direction did not invent an identity; it named the one the project already had.

---

## §13. WHAT THIS CHANGES IN THE ECONOMY

> ## ⛔ NOTHING. NOT ONE NUMBER.
> Every ruling in `docs/PROGRAM_RAID_ECONOMY_2026-09-04.md` survives: the reward table, the
> performance ladder, gold at 125–140% of designed army-replacement cost, crystals decreasing, the free
> starter army, the 3/10/20 unlock ladder, no new currency, no PvP, troops-defend deferred.
> The charge count is still 3 and the regen is still four hours.
>
> **This file changes what those numbers MEAN to the player, and nothing else.** That is why it is
> cheap and why it should be built now rather than "after the economy lands" — the strings ship in the
> same release as the numbers they explain.

---

## §14. ⭐ THE FIVE QUESTIONS — the narrative gate (owner ruling 2026-09-04, BINDING)

> The technical regressions are extensive and they prove the strings EXIST. **This gate proves the
> build COMMUNICATES.** Owner: *"The interesting work after the gate is going to be seeing whether the
> actual build feels like this canon rather than merely containing strings that describe it."*

**Take a completely fresh account. After the first successful raid, and WITHOUT OPENING THE GUIDE, can
the player answer these five?**

| # | Question | Answered by |
|---|---|---|
| 1 | Why did I attack that place? | the premise + the target card (§1, §3) |
| 2 | What did I bring home? | the victory screen showing EVERY non-zero currency (§8.2) |
| 3 | What should I spend those resources on? | the explicit teaching line, FTUE beat 9 (§8.1) |
| 4 | Why can't I immediately raid forever? | Heartfire, and its copy — not a timer refusal (§4) |
| 5 | What am I trying to reach next? | the unlock ladder saying something opened (§3, economy map §4) |

> **Reclaim territory → recover resources → strengthen Elarion and your army → Heartfire rekindles →
> push farther.** If the build itself communicates that, the loop closes.
> ⛔ **If the Guide has to explain any of the five, something UPSTREAM is not doing its job** — and the
> fix is upstream, never a longer Guide entry.

### ⛔ AND THE SIXTH QUESTION, WHICH THE PLAYER MUST **NOT** BE ABLE TO ANSWER

> ## *What actually happened to Elarion?*

**That is the hook, and it is protected.** ⛔ No FTUE beat, tooltip, guide entry, card description,
season blurb or memory line may answer it. A seat that "helpfully" explains the fall of the Realm in a
tooltip has removed the reason to play the next chapter.

> **The economy gives them a reason to perform the next raid. The mystery gives them a reason to play
> the next chapter.** Two different jobs, and only one of them is allowed to be satisfied.

**How this is enforced, so it is a gate and not a wish:** a `NarrativeGateRegression` walks the
first-raid string path for a fresh save and asserts each of the five answers is present on a surface
the player reaches before or at that moment — and asserts that no FTUE-reachable string answers the
sixth. ⚠ It must be proven RED first against the tree as it stands, where questions 1, 3 and 5 have no
answering string at all.

---

## §15. ⚠ OPEN — not decided, and deliberately not guessed

Per CLAUDE.md §11B, an unproven thing named as unproven is useful; an unproven thing stated as fact
costs someone a day.

1. ~~Which Echo says *"…there's someone here"*~~ — **RULED: Aldwin.** See §7.
2. ~~**Does the game know the player's first name?**~~ — **ANSWERED: NO. IT DOES NOT, ANYWHERE**
   (verified at source 2026-09-04, WO-1380). Aldwin's line therefore ships **without** a name, and
   ⛔ **no shipped string may use one** until a name field is deliberately added.
   - `SaveSchema.cs:239` — the persisted state holds **exactly one** `*name` field:
     `[JsonProperty("petName")] public string PetName;`. `GameState.cs:290-297` documents it as *"the
     player-chosen name for their **starter pet**, set in the tutorial's pet introduction (WO-277)"* —
     it names the Echo, not the player.
   - `PiSignInController.cs:74` `SignedInUsername` is a **Pi Network platform handle**, not a name the
     player gave this game, and `PiSignInController.cs:19` (the `GOOGLE_PLAY` branch) hard-returns
     `null` — on the Android/Seeker lane, which is the priority lane, it is always null.
   - `LeaderboardService.cs:290,304-306` and `TownShowcaseClient.cs:14` read a `username` off **server
     rows describing OTHER players**; neither is the local player's own name.
   - The only local identity is `GameState.BoundWallet` — an address or guest id
     (`GameStateService.cs:1663`), not a first name.
   ⭐ **The line is stronger without it anyway:** *"...there's someone here."* does not need to know who
   you are; it needs you to wonder who **they** are. `EchoGuideMemoryRegression` [canon] fails the build
   if any memory line ever grows a `{0}` / `{name` / `{player` / `{first` placeholder, because with no
   name to substitute, a placeholder renders as literal brace characters on the player's screen.
3. **The three-gate stack** (§4). Recommendation given; the acceptance criterion — *a player holding
   Heartfire always has somewhere to spend it* — needs owner sign-off.
4. ~~Echo Guide selection: narrative-only at launch?~~ — **RULED: NARRATIVE ONLY, and here is the
   reason it is a hard fence rather than a scope preference** (owner 2026-09-04): *"The second Corvin
   gives +8% scouting loot and Doran gives +5% stone, players stop choosing whose memories they want
   and start choosing the spreadsheet answer."* ⛔ **A buff does not add to the feature — it REPLACES
   it.** The whole value of the Guide is that it makes exploration personal; the moment one is
   mathematically correct, the choice is over and the characters are furniture. **Let them be
   characters first.** There is plenty of time to add mechanics once players care who they are. The
   direction is explicit that differences start narrative *"so we don't explode scope"*. ⛔ A Guide
   grants NO mechanical bonus in V1; adding one later is a deliberate design decision, never a quiet
   one. This also keeps §6's scope discipline consistent — flavour wins, scope stays dead.
5. ~~How many memory lines per target~~ — **RULED: all 24 ship, or the feature does not.** Six Echoes
   × four targets, one line each. A recognition system that fires for two Guides and stays silent for
   four does not read as depth; it reads as broken, and it teaches the player to stop noticing. 24
   short lines is an afternoon of writing and it is the whole payoff.
6. **Does the Heart's visual state have authored stages, or is it continuous?** §9 needs art direction
   before anyone wires a progression signal to it.
7. **Where the "why was the Heart damaged" mystery is allowed to go.** Flagged only so nobody answers
   it accidentally in a tooltip.
