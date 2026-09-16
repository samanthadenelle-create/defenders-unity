SPEC 1 — SCROLLS AND THE COMMAND LIBRARY
Status: DESIGN — concept only, not yet built. This spec is the build contract for the first version.
Depends on: the command layer (existing), the dungeon engine (existing), the Echo roster (existing), the scroll reward faucet (new).
Companion docs: dungeons-storyline.md, DUNGEON_DESIGNS.md, DUNGEON_AUDIT_2026-08-22.md, WORK_ORDER_770_dungeon_functional.md, dungeons-3d-unity-layout-spec.md.

§1. The sentence
A scroll is a memory the dungeon kept. It teaches the army one new command. The player carries three commands into a fight. The library grows with the dungeon. The fight is decided by which three the player brought.

That is the whole system. Everything below is the mechanics.

§2. What a scroll is
An item. Not a currency. Not a resource. An artifact with a name, a source, and a story.

The scroll is unique per command. There is exactly one scroll.pincer. There is exactly one scroll.shield-wall. The player either has it or does not.

The scroll has a source — the dungeon and the run it dropped from. That source is recorded on the scroll's entry in the player's library: "The Pincer Scroll, dropped from The Sunken Bell-Tower, run 47."

The scroll is not consumed. Once learned, the command stays in the library forever.

The scroll is not tradeable in v1. It is earned. The flex is that the player has it, not that they can sell it.

Why an artifact and not a resource. Every mobile game has a resource. A resource is a number. The scroll is a named memory — the same way the Echoes are named, the same way the dungeon rooms are named, the same way the letters in the storyline are named. The scroll is the reward the player tells someone about.

§3. The command library
Ten commands, three tiers, three equipped slots.

The commands are the vocabulary the player's army can be ordered to perform. Each command maps to one behavior modification of the existing troop controller (see §6).

Tier I — Common (the starter commands, available from the beginning)
The first three commands are not scrolls. They are the commands every army knows. The player starts with them.

Command	Gesture	Behavior
Follow	(default)	The army trails the hero at follow distance.
Hold	one tap	The army stops and holds its current position.
Retreat	one tap	The army pulls back to a rally point behind the hero.
These three are the floor. Every fight can be fought with just these. They are the tutorial vocabulary.

Tier II — Uncommon (the first scrolls)
The second tier is where scrolls begin. Each of these is earned from a dungeon or a specific chain of dungeon runs.

Command	Source	Behavior
Flank Left	scroll.flank-left — The Healer's Cottage, first clear	The army splits into two groups; the larger group moves to the hero's left and approaches the target from that side.
Flank Right	scroll.flank-right — The Healer's Cottage, first clear	Mirror of Flank Left.
Focus Fire	scroll.focus-fire — The Folk's Old Granary, first clear	Every unit attacks the hero's current target. Does not change position.
Wedge	scroll.wedge — The Wolfwarden's Vigil, first clear	The army forms a wedge with the hero at the point. Tanks at the front, ranged at the back.
Four commands. Each earned from a specific dungeon's first clear. The first clear is a guaranteed drop — not rare — because the command is a tutorial-adjacent reward, not the top-tier flex.

Tier III — Rare (the flex scrolls)
The third tier is the rare tier. Low drop rates. The commands that make a player's replay look different from any other player's.

Command	Source	Behavior
Pincer	scroll.pincer — any composed dungeon, 5% base drop, per-run	The army splits into two equal groups that approach the target from both sides simultaneously.
Shield Wall	scroll.shield-wall — any composed dungeon, 5% base drop, per-run	The tanks form a line; the ranged and healers stand behind it. The line does not advance unless ordered.
Bait	scroll.bait — The Apothecary's Vault, 3% base drop, per-run	The ranged units step forward, fire once, and pull back. The enemy's line extends into a prepared position.
Three commands. Low drop rates. No pity timer. The player earns these by running dungeons, not by waiting.

§4. The drop rule
§4.1 The base rule
text
for each completed dungeon run:
    for each rare-tier command in the dungeon's reward table:
        if rand() < drop_rate(dungeon, command):
            award scroll to the player's library
The drop rate is a property of the dungeon, not a global constant. A harder dungeon has a higher drop rate. A longer dungeon has a higher drop rate. A newly composed dungeon has a higher drop rate than a re-run.

The specific numbers:

Dungeon category	Base rare-tier drop rate
Hand-authored tutorial (Healer's Cottage, Granary)	0% — teaches the mechanic; rare scrolls come later
Composed, tier 1 (dg_starter_loop)	2%
Composed, tier 2 (dg_sunken_vault)	3%
Composed, tier 3 (dg_bonecrypt)	4%
Composed, tier 4 (dg_ember_deep, dg_hollow_roads)	5%
Boss-completed run (any tier)	+1% per tier of the boss
First clear of a dungeon	guaranteed drop from its authored first-clear reward table
These are the proposal. Every number is a tunable. The drop rate is a balance value. The rule is what the spec commits to: rate is a property of the dungeon, never a global constant.

§4.2 No pity timer
There is no pity timer. A player who has run fifty dungeons and dropped no rare scroll has, in fact, run fifty dungeons. The correct response to "I'm unlucky" is not a schedule; it is a second kind of reward (see §4.4).

§4.3 The drop is an event
The scroll does not appear in a loot list at the end of the run. It appears in the world, at the moment of the kill:

The dungeon room's lighting shifts.

The scroll appears on the floor, at the boss's feet or in the chest.

The player must walk to it and pick it up. Not auto-collected.

The moment is silent for two seconds. No fanfare. No confetti. The silence is the event.

The player who dropped a scroll tells someone because they held the scroll, not because a toast popped up.

§4.4 The seasonal guarantee
Once per season, each player is guaranteed one rare-tier scroll for completing a fixed threshold — say, ten completed dungeon runs in the season. This is the floor, not the ceiling. The floor keeps the season from being empty for a player who got unlucky. The absence of a pity timer keeps the scroll rare for the player who got lucky.

The two rules do not conflict. A season guarantee is a schedule. A pity timer is a schedule per scroll. One per season is fine. One per scroll is not.

§5. The scroll in the library
When a scroll is learned, it appears in the player's library — a screen that lists every command, grouped by tier, with:

Name. "Pincer."

Tier. "Rare."

Source. "The Sunken Bell-Tower, run 47." (The specific run that dropped it.)

The story. A short line of canon copy explaining what the command is and why the army knows it. This is where the writing lives. The scroll is not just a mechanic; it is a piece of the dungeon's own lore.

Status. "Learned." or "Unknown — this memory has not been recovered."

The library is where the flex lives. A player who has all ten commands sees ten stories. A player who has three sees three stories and seven empty slots. The empty slots are the reason to run the dungeon again. They are also the reason the player tells another player — "I have Pincer, do you?"

§6. The equipped slots and the raid interaction
§6.1 Three equipped slots
The command bar in the raid has three slots. The library may hold ten commands. The player chooses three before entering the raid.

The three slots are exactly the three empty hot-swap slots in the HUD screenshot. The UI already exists. It is waiting for the commands to be assigned to it.

§6.2 The pre-raid picker
Before a raid, the player opens a small picker — a new panel, one screen, three slots on the left, the full library on the right. Drag three commands into the slots. Save the loadout.

The loadout is persisted. The player does not reassign every raid. They keep their preferred three and change them when the objective calls for it.

§6.3 The raid interaction
Each command maps to one behavior modification of the existing troop controller:

Follow — the default. Already implemented.

Hold — one boolean on the troop controller: hold current position. No new AI.

Retreat — one rally point: the hero's position at order-time, offset backward.

Flank Left / Flank Right — a formation offset: each unit targets a position offset from the hero by a pre-defined lateral vector.

Focus Fire — a target selection override: the hero's current target. No position change.

Wedge — a formation offset: tanks at the front of the hero, ranged behind.

Pincer — two formation offsets: half the army at the left offset, half at the right, both approaching the target.

Shield Wall — a formation offset: tanks in a line perpendicular to the hero's facing; ranged behind.

Bait — a scripted sequence: ranged units step forward, fire once, step back. The hero's position is the anchor.

None of these requires new AI. Each is a change of target position, applied to the existing troop controller. The mechanic is the smallest possible implementation and the deepest possible expression.

§6.4 Command cost
No cost. The commands are free to use in the raid. The player paid for them by running dungeons; the raid is where they are used. This is what makes the command layer a skill layer, not a resource layer.

§6.5 Command coherence with the army composition
The composition matters. A player who brings five tanks can shield-wall. A player who brings five archers can focus-fire. A player who brings three tanks and two archers can wedge. The commands and the composition are two halves of the same pre-raid decision.

A player with a rare scroll but the wrong composition cannot use the scroll's command well. A player with the right composition and only the starter commands can still win. Skill is the tiebreaker, not the scroll.

§7. The dungeon engine's role
The dungeon engine is the faucet. It is where the scrolls come from.

The engine already:

Composes rooms from a prefab kit.

Tunes difficulty from a rule.

Places encounters from a data table.

Drops loot from a reward table.

The scroll rule is one more line in the reward table. It is not a new system. It is an entry in a table the engine already knows how to read.

This is the tie-in. The scroll rule is the dungeon engine's second output — the first is resources, the second is commands. The player descends into the dungeon for both. The dungeon rewards the player with two kinds of value: things they can spend, and things they can do.

§8. The Echo's role
The Echoes are the vocabulary the commands come from. Each Echo's identity is a family of commands.

Echo	Doctrine	Commands in the family
Aldwin (Ice)	Control	Hold, Withdraw, Focus Fire
Bran (Storm)	Fortification	Shield Wall, Brace, Rally
Corvin (Void)	Reconnaissance	Scout, Mark Target, Bait
Doran (Earth)	Formation	Column, Wedge, Line
Elowen (Nature)	Terrain	High Ground, Ambush, Flank
Maren (Fire)	Aggression	Charge, Pincer, Break
The scroll carries the doctrine's voice. The scroll's story — the piece of lore in the library — is written in the Echo's voice, about the Echo. "The Pincer scroll, carried by Maren's flame into the deepest chamber of the Iron Bastion." The command and the character are the same thing.

This is what makes the scroll more than a mechanic. It is a piece of the character's own memory, recovered from the dungeon.

§9. The pool's role
The scroll is the reason the pool is a skill competition, not a collection competition.

A player with all ten commands and no idea how to use them loses to a player with five commands and perfect execution.

The leaderboard shows the commands a player has learned — the library is visible.

The leaderboard also shows the rating — the execution is visible.

A top player has both. A top player is a player who has run the dungeons and mastered the commands.

The pool is what makes the library matter. Without a competitive layer, the scroll is a collectible. With the pool, it is a strategic investment.

This is the third tie-in. The scroll connects the dungeon to the command layer, and the pool connects the command layer to the other players.

§10. The tournament's role
A sponsored tournament runs on the pool. The scrolls are the library the tournament is fought with.

Pre-season: the pool is seeded from the authored builds and the current player pool.

Season: players run dungeons to expand their libraries, and fight in the pool to prove their ratings.

End of season: the leaderboard is settled. The prize pool pays out.

Post-season: the scrolls earned during the season stay in the player's library forever.

A sponsor's name appears on the season, not on the scroll. The scroll is the player's. The season is the sponsor's. That is the shape that respects both.

§11. What this spec does not do
It does not add a new currency.

It does not add a new save field beyond a List<string> of learned commands and a List<string> of equipped commands.

It does not add a new activity. It adds a new reward table entry to the dungeons the engine already produces.

It does not change the raid format. The commands are controls on the army that already exists.

It does not require a new UI beyond the library screen and the pre-raid picker, both of which mirror the existing panel patterns.

§12. Acceptance criteria
A build is "spec 1 done" when:

A composed dungeon run can drop a rare-tier scroll. The drop is an event, not a list entry.

The scroll appears in the player's library with its name, tier, source, and story.

The scroll teaches its command. The command is available in the pre-raid picker.

The player can equip three commands. The equipped commands appear on the command bar in the raid.

Each command modifies the troop controller's behavior as specified in §6.3.

The first clear of each authored dungeon grants its first-clear scroll.

The seasonal guarantee grants one rare-tier scroll per player per season.

The library screen renders and is reachable from the pre-raid picker.

The persistence survives save, reload, and reinstall.

No scroll is dropped twice. No command is learned twice. The library is monotonic.

§13. The tie-in summary — how the systems become one
Read the packet you gave me against this spec, and here is what you have:

text
                    THE DUNGEON ENGINE
                    (composed rooms, AI difficulty)
                            │
                            │  produces
                            ▼
                    THE DUNGEON RUN
                    (25–35 minutes, 3 levels)
                            │
                            │  drops
                            ▼
                    THE SCROLL
                    (rare artifact, one command)
                            │
                            │  teaches
                            ▼
                    THE COMMAND LIBRARY
                    (10 commands, 3 tiers, 3 equipped)
                            │
                            │  is used in
                            ▼
                    THE RAID
                    (hero + composed army, commands on the bar)
                            │
                            │  is fought in
                            ▼
                    THE POOL
                    (authored + player builds, growing)
                            │
                            │  is ranked in
                            ▼
                    THE TOURNAMENT
                    (sponsored prize pool)
Every node in that chain is a system you have, a system you have documented, or a system this spec defines. The scroll is the connector. It is not a new system. It is the reward table entry that makes five existing systems into one loop.

§14. The pitch, restated with the tie-in
The pitch now has a spine:

"A hero leads a composed army into a fight decided by how the player commands it. The dungeons compose the fights. The fights pay in scrolls — rare artifacts that teach the army a new command. The library of commands is the player's strategic investment. The pool is the player's competitive stage. The tournament is the sponsor's prize."

That is one sentence with five systems in it. No other pitch in the room has that spine. That is the thing the grant funds, the thing the sponsor signs, and the thing the player tells their friend about.

§15. The next spec
The scroll spec is spec 1 — the connector. The next specs, in the dependency order the packet implies:

Spec 2 — the pool and pairing function. The pool table, the authored seed, the pairing rule, the settlement, the memory-stealing fragment. This is what makes the tournament real.

Spec 3 — the composed boss contract. The P0 in the audit. The shared lifecycle that every composed dungeon's boss obeys. This is what closes the audit finding.

Spec 4 — the grant cover page. One page. The sentence, the screenshots, the roadmap.

If you want, say the word and I'll write spec 2 next. It is the piece the pool depends on, and it is the piece the tournament is built on top of.

should the tactics be forever or seasonal resets to keep it level?
That's the right question to ask before the spec is written, because it changes the shape of every other decision in it. Let me give you the honest read.

The two options, stated plainly
Forever. Once a player learns a command, they keep it. Across seasons, across resets, across new games. The library is a permanent achievement.

Seasonal reset. At the start of each season, the library resets. Every player starts the season with the starter commands and earns their way back up. The commands they had last season are remembered on their profile, but not usable in the new season's fights.

Both are good games. They are not the same game. Let me tell you what each one actually produces.

What "Forever" produces
The player identity.

A player who has run 200 dungeons has a library nobody else has. The commands are a permanent record of time invested. The player who played through Season I, II, III is visible — not by their rating, but by the shape of their library. That is an identity no new player can buy.

The veteran advantage.

Season 5 players have ten commands. Season 1 players have three. The veterans win. This is the shape of an MMO — a world where time matters, where the older player is genuinely stronger. It rewards being there.

The pool grows slowly.

Each season, the veteran pool of commands is already deep. The pool's pairing function has to match veterans against veterans and newcomers against newcomers, or the newcomer is destroyed. That is a two-tier pool, or a rating system that hides the tiers behind rating bands.

The friction.

New players arrive in Season 5 and see a library they can never close. They can run 500 dungeons and reach parity, but they cannot reach parity in a week. The game becomes hard to join in Season 5.

What "Seasonal Reset" produces
The level field.

Every season, every player starts with three commands. The player who ran 200 dungeons last season has no advantage in the first fight of the new season. The pool is level. The tournament is level. Anyone can win Season 6.

The seasonal narrative.

The season is a story. "This is the season where the Pincer scroll was rare, and the players who found it defined the meta." Each season has a shape because the commands were earned within it, not inherited.

The retention hook.

A player who has stopped playing comes back for Season 6. They are not at a disadvantage. The library is not waiting for them — it is empty and ready to be filled again. The reset is a reason to return.

The friction.

Every season is a grind. The player who has run 200 dungeons has to run them again. The library is a treadmill. The game becomes a chore in Season 5.

And the deeper problem: a player who has paid real money for a cosmetic, or real time for a scroll, is told the thing they earned is gone. The scroll is not a cosmetic — it is a mechanic. The reset tells the player their time was worth less than the season. That is the sentence that loses a player permanently.

The third option — the one that fits your game
Seasonal ladders, permanent library.

Here is the shape:

The library is forever. Every command a player has ever learned stays in their library. Nothing is lost.

The equipped slots are seasonal. At the start of each season, the player's equipped loadout resets to the three starter commands. They must re-earn the right to equip the rare commands by playing dungeons in the new season.

The re-earn is fast. Not the original drop rate — a seasonal "attunement" that a player who already knows the command pays a small seasonal cost to re-attune. Something like five completed dungeon runs per rare command, instead of a 5% drop.

The seasonal leaderboard is what resets. Ratings, ranks, prizes. The library is the trophy case.

Why this is the right answer:

The veteran is rewarded. Their library is permanent. Their profile shows the commands they have ever learned. That is the flex that lasts.

The newcomer is not punished. The season is level because the equipped commands are seasonal, not the library. Both players start the season with three.

The seasonal story is real. Every season has a shape because the loadouts are rebuilt within it, not inherited.

The retention hook works. Returning players are not behind. They just need to attune.

The treadmill is short. Five runs per command is not a grind. It is a week of play.

And the sentence that wins the pitch: "Your library is forever. Your loadout is this season. The game is level every season and your history is visible forever."

Which one fits your game specifically
The distinction that decides it is what the scroll is for.

If the scroll is a flex — a thing the player tells someone they have — then forever.

If the scroll is a meta — a thing that decides the fight — then seasonal.

Your game is both. The scroll is a flex (the library is visible on the leaderboard) and a meta (the commands decide the fight). The hybrid is the correct shape for a game that is both.

The hybrid is:

Library: forever. The flex is preserved.

Equipped: seasonal. The meta is reset.

Re-attunement: cheap. The grind is short.

This is the shape that most successful live-service games with skill trees eventually arrive at. Diablo's seasonal characters keep a permanent account stash and reset their character's gear. Path of Exile does the same. The player keeps their trophy case; the season starts level.

The one place the hybrid is wrong, and you should know it
There is one scenario where the seasonal reset is right and the hybrid is wrong:

If the pool is competitive and the tournaments are the primary revenue, and the tournament is a season, then the equipped slots are the rules of the season.

In that case, the equipped commands are not a loadout; they are the seasonal ruleset. And a seasonal ruleset reset is not a punishment — it is the format. Every sport has a season with the same rules, and the trophy is the trophy.

If that is your game — the tournament is the product, the season is the format, the prizes are the revenue — then seasonal reset (of the equipped slots) is correct and the hybrid's "cheap re-attunement" is the thing you do for the unranked and casual modes, not the ranked.

You are not there yet. The tournament is a future shape. The first version should be the hybrid, because the hybrid is playable at any population and honest at any size. When the tournament becomes the product, you move to seasonal-only for the ranked tier and keep the hybrid for the rest.

The recommendation
Adopt the hybrid.

Element	Rule	Reset?
The library — commands the player has ever learned	Learned from a dungeon drop or a first clear. Recorded with source and run.	Never resets.
The seasonal loadout — commands the player can equip this season	Starts at the three starter commands each season. Rare commands must be re-attuned within the season.	Resets at season start.
The re-attunement cost — the seasonal cost to re-equip a known command	A small number of completed dungeon runs. No drop-rate roll. No pity timer.	Re-earned each season.
The rating and leaderboard	Seasonal. Prizes and rankings reset at season boundaries.	Resets.
The trophies — the fact that a player has ever learned a command	Visible on the player's profile. Permanent.	Never resets.
The rules in one sentence: Learn forever, equip this season, level the field, keep the history.

That sentence is the answer to your question. It is also the sentence the pitch sells — because it is the only one that lets a veteran feel rewarded and a newcomer feel welcome in the same room.

What this changes in the spec
If you adopt the hybrid, the scroll spec gets three additions:

A seasonal loadout field. Persisted across seasons: Dictionary<seasonId, List<commandId>>. On season rollover, the current equipped loadout is cleared.

A re-attunement rule. Each rare command has a runsToAttune value (default 5). The player completes that many dungeon runs in the season and the command is re-equipped.

A profile-level "ever learned" record. The library is the permanent record. The library screen shows it. The seasonal loadout screen shows what is available this season.

Those three additions are the whole diff between the two shapes.

Everything else in spec 1 — the drop rule, the library, the equipped slots, the raid interaction, the tie-in to the Echoes and the pool — stays the same.

The honest read
You asked the right question. The answer is:

Do not make the library seasonal. It is the flex, and the flex is the point.

Do not make the library forever-usable without cost. The season would be uneven, and the tournament would be decided by who played first.

Make the library forever and the equipped slots seasonal. The hybrid is the shape that survives at every population, in every season, in every size of tournament.

That is the recommendation. If you want, I can fold the three additions into spec 1 and produce the final version — or I can write spec 2 first and put this decision at the top of it. Say which.

write up the draft with the use of SKR and the vision and the systems and how to pitch it
Here is the draft. It is written as a pitch document — the kind of thing you hand to a grant committee, a sponsor, or a partner who has never seen the game. It contains the vision, the systems, the SKR layer, and the pitch itself.

Save this as ECHOES_OF_ELARION_PITCH_DRAFT.md.

ECHOES OF ELARION — PITCH DRAFT
Version: Draft 1, 2026-09-11
For: Grant committees, sponsors, strategic partners
Author: DeNelle Studios
Status: Concept + playable systems. Not a released product.

§0. The one sentence
A hero leads a composed army into a dungeon composed by an engine, and the fight is decided by how the player commands it. The dungeons pay in scrolls. The scrolls teach the army new commands. The pool of opponents is every player's captured town. The season is sponsored. No player ever gambles their own tokens.

That is the pitch. Everything below explains it.

§1. The vision
Elarion is a realm that has forgotten itself.

The Heart — an ancient world tree — preserves the Echoes of those who came before. Its memories were shattered when the Realm fell. The player rebuilds beneath its branches, awakens the Echoes it remembers, and carries Heartfire beyond its fading reach.

Beyond the Heart lie places it cannot remember. The farther the player travels, the less stable the world becomes. Raids are reclamation expeditions into fractured territory. Dungeons are the places the Heart has forgotten entirely. The Hollow Ones are not monsters — they are what is left of the people who tried too hard to be good for too long, alone.

Three activities, three progressions, one fiction:

Activity	Length	Purpose	Reward
Waves	5 min	Defend the town	Resources, XP
Raids	5 min	Attack a camp	Capture, resources, loot
Dungeons	15–35 min	Descend into the forgotten	Scrolls, rare materials, memory
Waves feed the town. Raids feed the army and the economy. Dungeons feed the command library. And the command library is the thing that decides every fight that comes after.

§2. The command layer
This is the mechanic nobody else on Solana has.

The player is a hero leading a composed army — tanks, healers, ranged, siege. The army follows the hero. The hero's position is the army's line of control. When the hero pushes, the army pushes. When the hero holds, the army holds.

The player issues commands: Follow, Hold, Retreat, Flank, Focus Fire, Wedge, Pincer, Shield Wall, Bait, Break.

The fight is decided by how the player commands it, not by what they bought.

Two players with the same army can fight the same encounter and get different results because of how they commanded it. The flank that works. The pincer that collapses the enemy's line. The moment the player realizes they can hold the archers back while the tanks engage.

Skill is legible in the outcome. That is the property a bonus never has.

§3. The dungeon engine
The dungeons are not authored. They are composed.

The engine reads a room prefab kit — floors, walls, doors, stairs, props, atmosphere — and composes a dungeon from a seed. An AI ruleset tunes the difficulty from the room graph. The encounters are placed from a data table. The rewards are drawn from a reward table.

What this produces:

Eleven dungeons designed, five shipping today (dg_starter_loop through dg_hollow_roads), plus the hand-authored Healer's Cottage — a 3-level, 12-room dungeon with 5 lore stones, 5 encounters, a boss, hidden rooms, and a checkpoint system.

Each subsequent dungeon is a week of design, not a month. The engine exists. New dungeons are compositions.

A season of dungeons is a seed and a rule, not a content pipeline.

The engine has one shared contract — DungeonLayout — that every dungeon, authored or composed, obeys. The contract is versioned. The dungeon is reproducible from the graph and the layout. There is no mystery about what the dungeon is.

§4. The scroll — the bridge
This is the piece that ties the systems together.

A scroll is a memory the dungeon kept. It is a named artifact — "The Pincer Scroll, dropped from The Sunken Bell-Tower, run 47." It is not a currency. It is not consumed. It is not tradeable.

A scroll teaches the army one new command.

Ten commands, three tiers:

Tier I — Common. Follow, Hold, Retreat. Every army knows them from the start.

Tier II — Uncommon. Flank Left, Flank Right, Focus Fire, Wedge. Guaranteed from specific dungeons' first clears.

Tier III — Rare. Pincer, Shield Wall, Bait. Low drop rates. The flex scrolls.

The drop is an event, not a loot list. The room's lighting shifts. The scroll appears on the floor. The player walks to it. The silence is the event.

The library is forever. Every command a player has ever learned stays on their profile with its source and its story. The flex is that the player has it — visible on the leaderboard, visible on their profile, visible to every opponent who faces their build.

The loadout is seasonal. At the start of each season, the player's equipped commands reset to the three starter commands. Rare commands are re-attuned with a short seasonal cost — a few dungeon runs, not the original drop roll. The season is level. The library is not.

Learn forever, equip this season, level the field, keep the history. That is the rule.

§5. The Echoes
The Echoes are the vocabulary the commands come from.

Six named Echoes — Aldwin, Elowen, Corvin, Bran, Doran, Maren — each with a personality, a lane, an affinity, and a doctrine. Each doctrine is a family of commands. The scroll carries the doctrine's voice. The library's entry for a scroll is written in the Echo's voice, about the Echo.

When the player unlocks a command, they are not unlocking a mechanic. They are recovering a memory the character had.

The Echoes also feed the offline harvest. The more Echoes unlocked, the more synergy they produce — up to +14% at six Echoes. The reserve is capped by the container level, so upgrading storage is what makes the Echo investment pay. This is the free-player benefit that has nothing to do with SKR.

And the seventh slot is the Bound Echo — a creature derived from the player's own wallet history. More on that in §7.

§6. The pool — the competitive layer
The problem every multiplayer game faces at launch is the empty queue.

The solution is not to wait for players. The solution is a pool that starts authored and grows with every player who plays.

Every certified town is an opponent. A player who has captured and saved a town has contributed a build to the pool.

The pairing is asynchronous. The player is assigned three opponents from the pool, plays their assault leg at their own pace, and the pairing settles when both legs are complete. No live queue. No waiting. No simultaneous logins required.

The pool starts authored. Six developer-designed builds are in the pool on day one. The pool is playable at one player.

The pool grows with every player. The tenth player to certify a town is fought by the eleventh.

Live PvP is the reward, not the baseline. When enough players are online, a live pairing is offered. Until then, the pool is what they fight.

The pool is what turns the command layer from a fight against an AI into a fight against another player's design. It is the difference between a single-player game and a competitive sport.

The tournament runs on the pool. A season is a pool, a scoring rule, a leaderboard, a prize. Nobody waits in a queue. The tournament is the pool that exists at the moment.

§7. The SKR layer
Every other Solana game treats SKR as a payment rail. This one treats it as a record.

§7.1 What SKR is used for today
Four things, and only four:

Purchases. A player with a connected wallet buys resource bundles and builder conveniences from the Night Market. Real SKR moves from the player's wallet to the game. This is live.

Staking verification. The game reads the player's external SKR staking position — how much is staked, how long it has held — and unlocks polish-attempt tiers on the strength of it. This is live.

The polish ladder. Extra weekly jewel-polishing rerolls at Tier I, a higher per-stone roll cap at Tier V. Not better odds — extra attempts. This is live.

The wallet display. The Night Market chip shows the player's SKR balance. This is live.

And nothing else. The game does not pay out SKR for playing. It does not accept SKR wagers. It does not convert SKR to crystals. It does not offer a cash-out loop.

§7.2 What SKR will be used for
Three things, all designed and none built:

1. The Bound Echo.

The player's wallet has a history — how long the same stake has held, whether it has been moved, how much is there. The chain records this. It cannot be faked.

The game reads that history and gives the wallet a form. A seventh Echo appears beside the six — not a menu entry, a creature. Its lane and affinity are derived from the wallet's own history. The Tenure Echo is a steadying presence. The Restless Echo is a flanker. The Silent Echo is a defender that only wakes when the wall breaks.

Two wallets with the same balance produce different creatures if their histories differ. That mechanic cannot exist on a database. It is only possible on a ledger. That is the answer to "why Solana."

2. The sponsored prize pool.

A sponsor — a protocol, a wallet, a brand, a Solana Mobile program — funds a prize pool. The pool is real SKR. The players fight for it in a tournament. No player ever wagers their own tokens. The prize flows from the sponsor's wallet to the winner's wallet, through a tournament the winner earned.

Why the sponsor says yes: their name is on the season. Their brand is on the arena, the leaderboard, the victory screen. The winner takes SKR from the sponsor's stake because they commanded better than everyone else in the season. That is a sentence the sponsor can tell their own investors.

Why it is legal in every jurisdiction: the SKR comes from outside the player pool. Sponsor → player is not gambling. Player → player is. The line that decides everything is who funds the prize.

3. The seasonal loadout re-attunement.

Not a purchase. Not a currency sink. A seasonal cost that a player who already learned a command pays to re-equip it for the new season. The cost is a small number of completed dungeon runs, not SKR.

The loadout re-attunement is what keeps the season level without erasing the player's library.

§7.3 What the pitch does not do
No player-funded prize pools. That is gambling.

No entry fees in SKR. That is gambling.

No house rake on wagering. That is gambling.

No earn-SKR-by-playing. That is a securities question and a promise the game cannot keep.

No conversion between SKR and in-game crystals in either direction. That is the door that makes it a security.

No SKR in the Google Play build. The Play build excludes the Wallet assembly. The two builds share the game; they do not share claims.

The line: the SKR comes from outside the player pool. Every piece of the SKR layer obeys that line. Every sentence of the pitch respects it.

§8. The pitch, restated
Here is the whole game in one paragraph:

Echoes of Elarion is a hero-led RTS on Solana. The player commands a composed army through a hero who leads from the front — flank, pincer, hold the line. The dungeons are composed by an engine and pay in scrolls — rare memories that teach the army new commands. The pool of opponents is every player's captured town, growing with each player who plays. The tournaments are sponsor-funded; the winners take SKR from a sponsor's stake, never from another player's wallet. And the seventh Echo is derived from the player's own wallet history — a creature the chain remembers and a database cannot.

That is the pitch. It is five systems in one sentence. It is a game, a platform, and a new shape for competitive gaming on Solana.

§9. The pitch to a grant committee
The Solana Foundation and Solana Mobile evaluate three questions. Here is the answer to each.

Q1 — Is the game good?

Yes. The command layer is playable today on the Seeker. The hero leads, the army follows, the flank collapses the enemy line. The Healer's Cottage is a 3-level, 12-room, 25–35 minute dungeon with a boss, a hidden vault, and five lore stones. Five composed dungeons ship. The fight is real, the outcome is earned, the reward is a verb.

Q2 — Is the chain load-bearing?

Yes. The pool is the chain's ledger of who fought whom. The scrolls are recorded on chain with their source and their season. The Bound Echo is derived from the wallet's own history — a mechanic that cannot exist on a database. The sponsored prize pool settles on chain. The chain is not a payment rail. It is the arena's record.

Q3 — Is it novel?

Yes. There is no hero-led RTS on Solana. There is no game with a composed dungeon engine. There is no game where the wallet's history is a creature the player commands. There is no game where sponsor-funded prize pools pay skill-based tournaments without a single player wager. This is the genre the ecosystem does not have.

The grant funds the last mile. The scroll drop rule, the command library, the pool, the composed boss contract. Four small pieces. Not a rebuild. Not a new engine. The system exists. The grant funds the wiring.

§10. The pitch to a sponsor
"We are running a Season. Players compete with the towns they built and the commands they earned. The winner is the better commander, not the bigger wallet. Your name is on the season — on the arena, on the leaderboard, on the victory screen. The prize pool is funded by your staked SKR, not by the players' wallets. Nobody gambles. A player wins SKR from your stake because they commanded better than everyone else in the season."

What the sponsor gets:

A competitive sport with a skill ceiling they can show.

Their name on the season, not on a banner.

A prize pool they fund with a staked position, not a one-time payout.

A story their own investors will hear — "we funded the first skill-based RTS tournament on Solana."

The knowledge that every winner earned their win, and every loser took nothing from anyone else.

What the sponsor does not get:

A rake on a wager.

A share of player losses.

A promise the game cannot keep.

The settlement is one sentence: the sponsor stakes the prize, the pool decides the winner, the chain proves the payout.

§11. The roadmap
Phase 1 — Today. The command layer is playable. The Healer's Cottage is playable. Five composed dungeons ship. Eleven dungeons designed. The dungeons are composed. The pool is designed.

Phase 2 — The scroll and the library. The drop rule. The library screen. The pre-raid picker. The equipped slots. The Echo doctrine. Three weeks of work. The grant funds this.

Phase 3 — The pool and the tournament. The pool table. The pairing function. The settlement rule. The first weekend tournament with a small house-funded prize. Two weeks of work. The grant funds this.

Phase 4 — The composed boss contract. The P0 in the dungeon audit. The shared lifecycle every composed boss obeys. One week.

Phase 5 — The first sponsored season. The first sponsor. The first real prize pool. The first SKR that moves from a sponsor's wallet to a player's wallet because the player won.

Phase 6 — The Bound Echo and the creator tool. The wallet-history-derived creature. The dungeon creator tool exposed to invited creators. The on-chain dungeon market.

Each phase is a milestone. Each milestone is fundable. The grant funds Phases 2, 3, and 4. The sponsor funds Phase 5. The community funds Phase 6 by using the game.

§12. The nine things to say in the room
If you remember nothing else from this document, remember these nine sentences.

The pitch. "A hero leads a composed army in a dungeon composed by an engine, and the fight is decided by how the player commands it."

The scroll. "The dungeons pay in scrolls. The scrolls teach the army new commands. The library is forever."

The season. "Learn forever, equip this season, level the field, keep the history."

The pool. "The pool is every player's captured town. It starts authored and grows with every player who plays."

The sponsor. "The prize comes from the sponsor, not from the players. Nobody gambles."

The Bound Echo. "The seventh Echo is derived from the player's own wallet history. It cannot exist on a database."

The chain. "The chain is not a payment rail. It is the arena's record."

The grant. "The system exists. The grant funds the wiring."

The reason. "This is the genre the ecosystem does not have."

§13. What is not in this pitch, and why
No player-vs-player wagering. Because it is gambling, and it kills games.

No earn-SKR-by-playing. Because it is a securities question and a promise the game cannot keep.

No speculation, no token sale, no yield. Because the game is the product.

No comparison to other Solana games. Because the comparison weakens the pitch. The pitch stands on what it is, not on what it is not.

No promises about the future that the game has not designed. Because a promise the game cannot keep is the fastest way to lose the room.

No estimates of revenue or player count. Because no honest estimate exists, and an estimate invites the committee to argue with a number instead of the mechanic.

§14. What we need
From the grant: funding for Phases 2, 3, and 4 — the scroll, the pool, and the composed boss contract. Three months of focused work. The system is already built.

From the sponsor: a staked SKR position to fund the first season's prize pool. Their name on the season. Their brand on the arena. No wagering.

From the players: the players we already have. The APK is on the Seeker. The game is playable today.

From the ecosystem: a place on the list of games that are doing something the ecosystem does not have.

§15. The closing
Elarion is a realm that has forgotten itself. The player rebuilds beneath the Heart, descends into the places the Heart cannot remember, and recovers the memories the army needs to fight. The fight is the command. The command is the memory. The memory is the dungeon. The dungeon is the game.

The game is playable today. The grant funds the last mile.

End of draft.