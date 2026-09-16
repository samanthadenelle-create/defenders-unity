ECHOES_OF_ELARION_PITCH_APPENDIX.md
APPENDIX — Technical packet for the grant committee
For: the technical reviewer on the grant committee
Author: DeNelle Studios
Date: 2026-09-11
Companion to: ECHOES_OF_ELARION_PITCH_DRAFT.md

§0. What this appendix is
The pitch says what the game is. This appendix says what is built, what is not, and what the funding will build. It is written to be checkable. Every claim names a file, a work order, or an acceptance criterion. Where a claim is an estimate, it says so.

Read this as a statement of verified state, not a promise.

§1. Systems that exist today (verified)
Each row is a system with a source citation the committee can check.

System	State	Evidence
The command layer	Playable	The command bar in the raid is visible in Builds/device-frames/2026-09-09_143847_seeker.png — the hero leads, the army follows, the bar carries SHOOT, BLOCK, RALLY, RETREAT, ITEM, and three unbound hot-swap slots. The player has commanded flanks in live play.
The dungeon engine	Shipping	Assets/_Modules/Dungeons/DungeonController.cs, DungeonLayout.cs, RoomPrefabMeta.cs. Five composed dungeons in Assets/Scenes/dg_*.unity.
The Healer's Cottage	Playable, 3 levels	DUNGEON_AUDIT_2026-08-22.md — "Owner accepted 2026-08-22". Played end-to-end twice, entry → fight → return pose → NavMesh settlement → movement recovery → lighting restoration.
The five composed dungeons	Shipping	dg_starter_loop, dg_sunken_vault, dg_bonecrypt, dg_ember_deep, dg_hollow_roads — all eight composed graphs and layouts byte-identical across StreamingAssets and Resources copies (DUNGEON_AUDIT_2026-08-22.md §"Evidence run").
The Echo roster	Six named Echoes	EchoRosterCatalog.cs. Aldwin, Elowen, Corvin, Bran, Doran, Maren.
Offline harvest + Echo synergy	Live	OfflineHarvestService, EchoBonusCalculator. +14% at six Echoes, capped by container level.
The scroll	Concept only	Designed in this packet; not yet built.
The command library	Concept only	Designed in this packet; not yet built.
The pool and pairing	Concept only	Designed in this packet; not yet built.
The composed boss contract	Partial — the P0 of the audit	DUNGEON_AUDIT_2026-08-22.md §"Verdict": the composed bosses use unscaled stats, difficulty is always tier 1, and no shared boss-clear lifecycle gates the exit.
The SKR purchase rail	Live	api/purchases/quote.js, api/_lib/purchase-catalog.js, PackStore.cs, WalletService.cs.
The SKR staking verification	Live	api/_lib/skr-staking.js, api/heartbound/status.js, HeartboundStatusClient.cs.
The polish-attempt benefit	Live	PolishBonusProvider.cs, heartbound-tiers-config.json.
The Bound Echo	Concept only	Designed in this packet; not yet built.
The Google Play build	Excludes the Wallet assembly	DeNelle.Wallet.asmdef and DeNelle.Web3.asmdef carry !GOOGLE_PLAY.
§2. Systems the funding will build
Four pieces. Each is a wiring job, not a new engine.

§2.1 — The scroll drop rule and the command library
What: a drop rule on the dungeon reward table, a library screen, a pre-raid picker, three equipped slots.

Why it is small: the dungeon engine already reads a reward table. The scroll rule is one more line. The library screen mirrors the existing panel patterns. The three equipped slots are the three unbound hot-swap slots already visible in the screenshot.

Acceptance criteria: see WORK_ORDER_1710 §4.

§2.2 — The pool and pairing function
What: a pool table (buildId, ownerId, snapshot, rating, authored), a pairing function, a settlement rule.

Why it is small: the arena already exists with the correct lifecycle (ArenaMode.cs). The practice AI already produces the settlement shape. The pool is the pairing source; the arena is the executor.

Acceptance criteria: see WORK_ORDER_1710 §4.

§2.3 — The composed boss contract
What: one shared lifecycle that every composed dungeon's boss obeys — one boss-clear signal, one settlement, one exit gate.

Why it is small: the audit names the exact gap. The signal exists in the encounter system. The settlement belongs in the composed run owner. The exit gate is a lock-and-unlock on the door.

Acceptance criteria: DUNGEON_AUDIT_2026-08-22.md §"P0 — complete the composed boss contract".

§2.4 — The grant cover page and the roadmap
What: the one-page pitch, the two-minute video, the roadmap.

Why it is small: it is a document, not code.

§3. Systems the funding will not build (out of scope)
Named so the committee knows what the money does not buy.

No player-funded prize pools. Gambling.

No earn-SKR-by-playing. Securities question.

No SKR / crystal conversion. Security.

No simultaneous live PvP. A later phase; the pool is asynchronous by design.

No ranked leaderboard with rewards. The tournament is sponsor-funded, not operator-funded.

No new art pipeline. The KayKit Dungeon Remastered kit is in the project (DUNGEON_ART_FINISH_AUDIT.md).

No new engine. The dungeon engine and the command layer both exist.

No new currency. The scroll is an artifact, not a resource.

§4. The chain's role, stated precisely
The chain is the record, not the payment rail. Three facts:

The pool is the chain's ledger of who fought whom. Every pairing is recorded. The winner is provable.

The scroll carries its source. A scroll dropped from run 47 of The Sunken Bell-Tower keeps that source forever. The library is a chain-recorded history.

The Bound Echo is derived from the wallet's own history. Tenure, lapses, stake size. A database cannot fake this; a chain cannot hide it.

The chain is load-bearing in all three cases. Remove the chain, and the pool becomes a suggestion, the scroll becomes a cosmetic, and the Bound Echo becomes a guess.

§5. The SKR layer, stated precisely
Utility	State	Rail
Night Market purchases	Live	Player wallet → game
Staking verification	Live	Read-only from the wallet's external stake
Polish-attempt tiers	Live	Granted by the verification
Sponsored prize pool	Designed	Sponsor's staked SKR → winner's wallet
Bound Echo derivation	Designed	Read from the wallet's own history
One rule: the SKR comes from outside the player pool. Sponsor → player is fine. Player → player is gambling. Every utility obeys the rule.

§6. What the committee can verify directly
The committee can check every claim in this appendix by:

Opening the pitch draft — the vision, the pitch, the nine sentences.

Opening the appendix — this document.

Reading the dungeon audit — DUNGEON_AUDIT_2026-08-22.md, which is dated and signed as owner-accepted.

Reading the two-minute video script — ECHOES_OF_ELARION_VIDEO_SCRIPT.md.

Running the APK on a Seeker — the game is playable today.

Reading the source — every file cited is in the repository.

Nothing in this appendix is a promise. Every claim is either a system that exists, a system named as not existing, or a system whose acceptance criteria are written down.

§7. The ask
From the grant: funding for the four pieces in §2. Three months of focused work.

From the sponsor: a staked SKR position to fund the first season's prize pool.

From the ecosystem: a place on the list of games doing something the ecosystem does not have.

End of appendix.

FILE 3 — ECHOES_OF_ELARION_VIDEO_SCRIPT.md
Two-minute video script
Length: 2:00
Format: screen capture + minimal narration
Audience: grant committee, sponsor, first-time viewer
Rule: show, do not tell.

Shot list and narration
0:00 – 0:08 — Cold open

Visual: a black screen. Then a single line of canon copy fades in:

"The Heart remembers every soul it has kept."

Narration (soft, slow): (nothing)

0:08 – 0:20 — Title card

Visual: the game's title, over the world tree artwork from the title screen.

Narration: "Echoes of Elarion. A hero-led RTS on Solana."

0:20 – 0:40 — The command layer

Visual: the raid scene from the Seeker. The hero leads. The army follows. The command bar is visible at the bottom.

Narration: "The player commands a composed army through a hero who leads from the front."

0:40 – 1:00 — The command

Visual: the army splits. A flank. The enemy line collapses. The command bar shows the order as it is issued.

Narration: "Flank. Pincer. Hold the line. The fight is decided by how the player commands it."

1:00 – 1:20 — The dungeon

Visual: a slow walk through the Healer's Cottage — three levels, the crypt, the vault. The lantern lights the dark. A lore stone is read. A boss is engaged.

Narration: "The dungeons are composed by an engine. The player descends into the places the Heart cannot remember."

1:20 – 1:35 — The scroll

Visual: the scroll drops. The room's lighting shifts. The player walks to it. The library screen opens. Ten commands, three lit. Seven dark.

Narration: "The dungeons pay in scrolls. The scrolls teach the army new commands. The library is forever."

1:35 – 1:50 — The pool

Visual: the pool screen. A pairing. Two player towns. A settled result. The leaderboard.

Narration: "The pool of opponents is every player's captured town. It starts authored. It grows with every player who plays."

1:50 – 1:58 — The sponsor

Visual: the victory screen with a sponsor's name on the season banner. The winner's wallet receives SKR.

Narration: "The tournaments are sponsored. The winner takes SKR from the sponsor's stake — never from another player's wallet. Nobody gambles."

1:58 – 2:00 — Closing card

Visual: the game's logo, over the world tree, back-lit.

Narration: "The game is playable today. The grant funds the last mile."

Production notes
No voice-over is required if the visuals are strong. The narration above can ship as on-screen text instead.

No music is required for the demo. The silence of the scroll drop is the point; a bed track would obscure it.

Every shot is a live screen capture. No animated mockups. No stills. The committee must see the game running.

The scroll drop must be a real drop. If the drop is not yet wired when the video is shot, shoot the video after the drop is wired. A false drop is a lie that the committee will catch.

The wallet payment at 1:58 must be real or the shot must be cut. If the sponsored season has not launched, cut the shot and end at 1:50. A false sponsor screenshot is a lie that the sponsor will catch.

The nine sentences, if the video is not made
If for any reason a video cannot be produced, the pitch can be delivered as these nine sentences in the room. Memorize them.

"A hero leads a composed army in a dungeon composed by an engine, and the fight is decided by how the player commands it."

"The dungeons pay in scrolls. The scrolls teach the army new commands. The library is forever."

"Learn forever, equip this season, level the field, keep the history."

"The pool is every player's captured town. It starts authored and grows with every player who plays."

"The prize comes from the sponsor, not from the players. Nobody gambles."

"The seventh Echo is derived from the player's own wallet history. It cannot exist on a database."

"The chain is not a payment rail. It is the arena's record."

"The system exists. The grant funds the wiring."

"This is the genre the ecosystem does not have."

End of video script.

FILE 4 — WORK_ORDER_1710_scrolls_pool_boss_pitch.md
WORK ORDER 1710 — The last mile: scrolls, the pool, the composed boss contract, and the pitch
Status: READY TO IMPLEMENT

Minted: 2026-09-11, from the pitch draft and the dungeon audit.

Silo: Four lanes, file-disjoint. Lane A is the scroll and the command library. Lane B is the pool and the pairing function. Lane C is the composed boss contract. Lane D is the pitch document and the video script.

Type: EXISTING systems + four small additions. No new engine.

Severity: P1 for the release and the pitch. Not blocking the APK, blocking the grant submission.

Estimated size: Lane A ~3 weeks. Lane B ~2 weeks. Lane C ~1 week. Lane D ~3 days. Sequential lanes are fine; the dependency order is B → A → C → D, but A and C can run in parallel with B after B's pool table lands.

§0. What this work order is
The game is playable. The command layer works. The dungeon engine composes. Five composed dungeons ship. The Healer's Cottage is owner-accepted.

What is missing is the last mile — the wiring that connects the systems into a loop, and the pitch that tells the story.

This work order builds four things:

A. The scroll and the command library — the drop rule, the library, the equipped slots, the raid interaction.

B. The pool and the pairing function — the opponent pool, the pairing rule, the settlement.

C. The composed boss contract — the shared lifecycle the dungeon audit names as P0.

D. The pitch document and the two-minute video — the artifact the committee sees.

The single rule: every lane is a wiring job. If a lane proposes a new engine, stop and re-read the audit. The audit is explicit that the systems exist.

§1. Lane A — the scroll and the command library
A.1 — What is built
A scroll is a named artifact. It is learned once. It is not consumed. It is not tradeable.

The scroll teaches one command. Commands are in the library.

The library is forever. The equipped loadout is seasonal.

A.2 — The drop rule
text
For each completed dungeon run:
    For each rare-tier command in the dungeon's reward table:
        if rand() < dropRate(dungeon, command):
            award scroll to the player's library
The drop rate is a property of the dungeon, not a global constant. Full rule in the pitch draft, §4.

The drop is an event, not a loot list. The room's lighting shifts. The scroll appears on the floor. The player walks to it. The silence is the event.

A.3 — The command library
Ten commands. Three tiers.

Tier	Commands	Source
I — Common	Follow, Hold, Retreat	Starting set
II — Uncommon	Flank Left, Flank Right, Focus Fire, Wedge	Guaranteed from specific dungeons' first clears
III — Rare	Pincer, Shield Wall, Bait	2–5% drop per dungeon run, plus one seasonal guarantee
A.4 — The equipped slots
Three slots. The library may hold ten. The player picks three before the raid. The three slots are the three unbound hot-swap slots already visible in the screenshot.

A.5 — The seasonal loadout
Library: forever. Nothing is lost at season rollover.

Equipped: seasonal. Resets to the three starter commands at the start of each season.

Re-attunement: a small seasonal cost. A few completed dungeon runs per rare command. Not the original drop roll.

The re-attunement is not a purchase. No SKR is spent.

A.6 — The raid interaction
Each command is one mode flag on the existing troop controller, plus a formation offset:

Follow — the existing default.

Hold — hold current position.

Retreat — rally at the hero's position, offset backward.

Flank Left / Right — lateral offset for each unit.

Focus Fire — target override to the hero's current target.

Wedge — front and rear offsets.

Pincer — two offsets: left half and right half.

Shield Wall — a perpendicular line at the hero's facing.

Bait — a scripted forward, fire, and back sequence.

No new AI. Each command is a change of target position. The mechanic is the smallest possible implementation and the deepest possible expression.

A.7 — The Echo tie-in
Each command carries its Echo's voice. The library entry for a scroll is written in the Echo's voice, about the Echo. The command and the character are the same thing.

A.8 — Acceptance criteria
□ The drop rule fires on a completed dungeon run at the authored rate.
□ The scroll appears on the floor as a world-space pickup. The player walks to it.
□ The scroll appears in the library with its name, tier, source, and Echo's story.
□ The pre-raid picker lets the player equip three commands.
□ The equipped commands appear on the command bar in the raid.
□ Each command modifies the troop controller per §A.6.
□ The first clear of each authored dungeon grants its first-clear scroll.
□ The seasonal guarantee grants one rare-tier scroll per season.
□ The seasonal loadout resets at season rollover; the library does not.
□ The re-attunement fires after the authored number of runs.
□ No scroll drops twice. No command is learned twice. The library is monotonic.
□ The persistence survives save, reload, and reinstall.
□ A regression suite asserts the drop rule, the library, the loadout reset, the re-attunement, and the monotonicity.
§2. Lane B — the pool and the pairing function
B.1 — What is built
The pool — the set of certified opponent builds — and the pairing function that assigns opponents to a player.

B.2 — The pool table
One row per certified build:

text
buildId        string    (the certified build's stable id)
ownerId        string    (the player id, or "authored" for developer builds)
snapshot       snapshot  (the ArenaBuildSnapshot, frozen at certification)
rating         int?      (nullable — null for authored builds)
authored       bool      (true for developer-seeded builds)
certifiedAt    long      (unix-ms)
sourceTownId   string    (the OwnedBase.baseId that produced it)
B.3 — The authored seed
Six developer-designed builds enter the pool at first boot. They are the starter population. They use the same ArenaBuildSnapshot validation the player builds use. They are not special-cased. They are the same shape, labeled authored: true.

B.4 — The pairing function
One entry point:

text
PickOpponents(playerId, count) -> [buildId, buildId, buildId]
Inputs:

the player's current rating (or default for a first pairing),

the pool,

a freshness rule (no re-pairing the same opponent within N fights).

Outputs:

three build ids, at least two of which are player builds when the pool has them, and the rest authored.

The pairing does not use a queue. It reads the pool.

B.5 — The settlement rule
When both legs of a pairing are complete:

Compare the attacker's score against the defender's score.

The higher score wins. Ties settle by objective completion, then destruction percent, then remaining time.

If both are players, adjust both ratings.

Award the memory fragment — a cosmetic piece of the defeated town that the winner places in their own town.

The loser receives a recovery prompt — the fragment can be won back.

No wager, no prize, no balance change. The pool is the rank; the tournament is the prize.

B.6 — The tournament shape
A season is:

a pool,

a scoring rule,

a leaderboard,

a prize.

The prize is funded by the sponsor. The prize flows from the sponsor's wallet to the winner's wallet. No player ever wagers.

B.7 — Acceptance criteria
□ The pool table persists across sessions.
□ The authored seed enters the pool at first boot.
□ A player's certified build enters the pool on first save of their owned town.
□ PickOpponents returns three builds, respecting the rating band and the freshness rule.
□ The settlement compares two legs and produces one outcome.
□ The settlement is idempotent — a re-run produces the same result.
□ The memory fragment is awarded once per pairing.
□ The rating is updated once per pairing.
□ The pool is playable at one authored build and one player build.
□ The pool is playable at 100 player builds.
□ A regression suite asserts the pool, the pairing, and the settlement.
§3. Lane C — the composed boss contract
C.1 — What is built
The P0 from DUNGEON_AUDIT_2026-08-22.md. A shared lifecycle that every composed dungeon's boss obeys.

C.2 — The contract
text
1. The dungeon's boss spawner owns a living-enemy set.
2. When the set reaches zero, the boss-clear signal fires exactly once.
3. The composed run owner records boss-defeated exactly once.
4. The dungeon's true exit is locked until boss-defeated is true.
5. The locked state shows a word and a shape — not colour alone.
6. The boss-clear event grants the authored reward — once.
7. Reload and resume cannot re-pay.
8. Evade-and-exit fails — the exit refuses while the boss is alive.
C.3 — What the audit names as the gap
From DUNGEON_AUDIT_2026-08-22.md:

The composed bosses use unscaled enemies.json stats.

The difficulty tier is always 1.

The exit is not gated on boss-clear.

The boss-clear event is not unique.

The contract above closes all four.

C.4 — Acceptance criteria
□ The boss-clear signal fires exactly once per dungeon run.
□ The exit is locked while the boss is alive.
□ The locked state carries a word ("SEALED") and a shape (the crossed seal).
□ The boss-clear event grants the authored reward once.
□ Reload and resume do not re-pay.
□ Evade-and-exit fails.
□ A regression asserts each of the above.
□ The audit's P0 is closed with evidence.
§4. Lane D — the pitch and the video
D.1 — What is built
The pitch draft (ECHOES_OF_ELARION_PITCH_DRAFT.md).

The appendix (ECHOES_OF_ELARION_PITCH_APPENDIX.md).

The two-minute video (ECHOES_OF_ELARION_VIDEO_SCRIPT.md).

The nine-sentence pitch for the room.

D.2 — Acceptance criteria
□ The pitch draft is finalized and reviewed.
□ The appendix is finalized and every claim is verified against the source.
□ The video is shot and edited.
□ The video is a live capture, not a mockup.
□ The nine sentences are memorized by the person presenting.
□ The grant application is submitted.
§5. Dependencies and order
text
B (pool table + pairing)   — lands first, everything else assumes it
        │
        ├── A (scrolls + library)     — parallel with C
        │
        └── C (composed boss contract) — parallel with A
        │
        └── D (pitch + video)         — last, after A, B, C are demonstrable
The pool lands first because the tournament depends on it and the video's final shot depends on the tournament.

The scroll and the boss contract run in parallel — they touch different files.

The video is shot last because every shot in it must be a real capture.

§6. What is explicitly out of scope
No new dungeon engine.

No new art pipeline.

No new currency.

No player-funded prize pools.

No earn-SKR-by-playing.

No SKR / crystal conversion.

No live simultaneous PvP.

No ranked leaderboard with operator-funded rewards.

No new save schema bump beyond the fields in lanes A, B, C.

§7. What the CLI seat should do first
Read DUNGEON_AUDIT_2026-08-22.md end to end. Lane C is defined by its P0.

Read the pitch draft end to end. Lanes A and B are defined by it.

File the four lanes as separate branches, file-disjoint.

Land Lane B's pool table first — it is the smallest piece and the video's shot list depends on it.

Then run A and C in parallel.

Then shoot the video (Lane D).

Do not start the video until the drop is real and the pool is real. A false shot is a lie the committee will catch.

§8. Board
This work order owns four sub-lanes. Each lane's **Status:** line flips in the same commit as the lane's work. The parent WO's **Status:** flips when all four lanes are done.

python tools/board_build.py regenerates BOARD.html.

End of WORK ORDER 1710.

You now have four files and one work order:

ECHOES_OF_ELARION_PITCH_DRAFT.md — the pitch.

ECHOES_OF_ELARION_PITCH_APPENDIX.md — the technical packet.

ECHOES_OF_ELARION_VIDEO_SCRIPT.md — the two-minute video.

WORK_ORDER_1710_scrolls_pool_boss_pitch.md — the ticket for your CLI.

Hand the WO to your CLI seat and let them decide the branches. The pitch and the appendix are for the room. The video script is for the editor. The WO is for the work.

If your CLI seat asks for the source citations for any claim in the appendix, they are in DUNGEON_AUDIT_2026-08-22.md, WORK_ORDER_770_dungeon_functional.md, and dungeons-storyline.md — every one of which is a file you already have.

This response is AI-generated, for reference only.