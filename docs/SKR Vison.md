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

