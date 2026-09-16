# SKR vision and game systems — reconciled review

Date: September 11, 2026. Status: **review and recommendations, not approved implementation scope**.

Requested by the owner: highest-reasoning review of four supplied documents. One GPT-6 Astra advisor was assigned at `ultra` reasoning; the parent separately compared claims with local implementation. Original documents remain unchanged. This report does not certify the currently installed APK, endorse legal assertions, or authorize deployment.

Review coverage: the advisor completed all substantive material in all four files. Exact duplicate blocks were compared programmatically rather than reread: archive lines 7308–8032 match the scroll document, 7757–8033 match the pitch, and 8056–8584 match appendix lines 2–530. No substantive section was left unread. No embedded script was executed.

## Overall judgment

The strongest idea is a connected player journey: **capture a town, make it your own, learn tactical commands through exploration, and use those choices against challenging defenses**. Scrolls as permanent memories fit the fiction and can make dungeon rewards more interesting than another resource payout.

The packet is not ready to become a single work order or an external factual pitch. It combines historical advice, proposed designs, duplicate drafts, stale defect reports, and unsupported claims about implementation, fairness, blockchain necessity, legal treatment, and development effort.

Protect the current APK/AAB release. Prove its capture → repair/build → save/revisit → no-wager practice loop. Then test one tactical command and one permanent scroll reward before committing to seasons, ten commands, public competition, or token prizes.

## Source roles

| Source | How to use it |
|---|---|
| [SKR Vison.md](<SKR Vison.md>) | Creative pitch draft. Strong thematic framing; implementation and external claims need correction. |
| [ECHOES_OF_ELARION_PITCH_APPENDIX.md](ECHOES_OF_ELARION_PITCH_APPENDIX.md) | Proposed technical appendix, video script, and work order concatenated into one file. Its embedded `READY TO IMPLEMENT` is not an approved or verified work order. |
| [SCROLLS AND THE COMMAND LIBRARY.md](<SCROLLS AND THE COMMAND LIBRARY.md>) | Design exploration, followed by seasonal-policy discussion and a repeated pitch. Competing proposals need explicit resolution. |
| [eVERYTHING dEEPsEEK.MD](<eVERYTHING dEEPsEEK.MD>) | Historical conversation archive, not present-day authority. Later repetitions are not independent corroboration. |
| [Current SKR implementation brief](SKR_CURRENT_USE_RESEARCH_BRIEF_2026-09-11.md) | Local source-review baseline, with explicit distinctions between implemented, incomplete, simulated, and unverified installed-build behavior. |
| [Current Android release plan](ANDROID_RELEASE_PLAN_2026-09-11.md) | Ongoing implementation and validation record. Later dated entries supersede earlier status statements. |

## Corrections that materially change the plan

### 1. The proposed boss-contract work starts from a stale diagnosis

The appendix, lines 26 and 409–444, treats composed boss lifecycle and difficulty as missing. But the cited [dungeon audit](qa/DUNGEON_AUDIT_2026-08-22.md) opens with completed follow-up: authored tiers, threat scaling, one boss-clear event, boss-room exit gates, hoard unlocking, and captured loot rolls. Its initial failure findings remain further down as historical context.

Current source supports the follow-up: `ComposedDungeonHost` subscribes to `BossCleared`, records `BossDefeated`, and reads layout tier; `DungeonExitInteractable` checks that boss state; `DungeonRuntimeState.MarkBossDefeated` guards repeat completion.

**Action:** validate the existing implementation and close any demonstrated gaps. Do not budget a rebuild of an allegedly absent contract. The audit explicitly says its final Windows/six-dungeon runtime matrix was not executed and was owner-waived; that is a remaining evidence limitation, not proof of missing code.

### 2. Empty HUD slots are not evidence of an available troop-command system

The scroll specification, lines 114–146, assumes the three empty slots are waiting for troop commands and describes each command as a simple flag or position offset. `HeroAbilitiesHudBridge`, lines 323–339, already resolves equipped hero abilities through `HeroLoadout` and `AbilityCatalog` for W/E/R. A screenshot does not establish that those slots can be repurposed.

The inspected `TroopController` rally branch runs when no combat target is acquired. Merely changing a destination does not establish how Hold, Withdraw, Focus Fire, or Pincer overrides active combat. A source search did not establish the proposed named command library as implemented.

**Action:** define command authority and input ownership first. Specify target selection, affected units, interruption, expiration, unreachable destinations, dead targets, hero death, and interaction with autonomous attacks. Test the resulting behavior in combat. Keep hero abilities and troop orders distinct.

A tactic also needs a reason to exist: Pincer needs encounters where approaching from two sides creates a situational advantage; Shield Wall needs actual protection behavior; Bait needs an enemy response. Formation appearance alone does not prove tactical depth. Consider one Flank technique with directional targeting instead of separately unlocking Left and Right.

### 3. Seasonal re-attunement does not prove fair competition

The scroll discussion, lines 352–441, preserves the library but resets access. A veteran can regain a known rare command after a short fixed run count; a newcomer must still acquire it through RNG or a limited seasonal guarantee. The starting screen can be identical while access diverges almost immediately.

**Recommendation:** keep earned PvE commands permanently usable in the first version. If ranked competition comes later, give all participants the same permitted command pool for that format. Normalize or explicitly budget roster, equipment, tower levels, consumables, and economic advantages too. Equal command slots alone do not create an equal contest.

### 4. Rare tactical verbs have a substantial acquisition cost

The scroll specification proposes 2–5% base chances and 15–35-minute dungeon runs. For one specified command with independent constant probability per eligible run:

| Base chance | Expected runs to acquire | Runs for at least 95% cumulative acquisition probability |
|---|---:|---:|
| 5% | 20 | 59 |
| 2% | 50 | 149 |

At 5%, the mean is roughly 5–12 hours at the stated run lengths. These are illustrative geometric calculations, excluding boss modifiers, first-clear guarantees, and seasonal guarantees; they are not a prediction of the final combined reward table.

**Recommendation:** make the first useful tactical unlock guaranteed. Test whether rare provenance, presentation, or mastery can supply prestige without withholding competitively essential actions. Define duplicate handling, guarantee choice, all-commands-owned behavior, and recovery after a interrupted reward reveal.

The proposed award timing is internally unresolved: the rule rolls after a completed run, while the presentation describes a boss-kill pickup that is not automatically collected. Decide when entitlement is durable, what happens if the player dies or exits before pickup, and how a retry avoids rerolling. Simple learned/equipped string lists cannot also represent source-run provenance, first-clear eligibility, guarantees, and recovery receipts.

Only seven commands are discoverable beyond the three starters. Once a player learns all seven, this reward source is exhausted. Define ordinary repeat-run value without assuming endless new tactical verbs. Keep permanent memories meaningful without making the release depend on a continual command-content pipeline.

### 5. An asynchronous pool can still have an indefinite wait

The appendix, lines 350–408, promises no queue but settles only after both players finish their attack legs. Its final design does not establish when the absent second player must act or how an authored opponent produces its leg. The earlier archive, lines 6338–6340, does propose an N-day timeout and forfeit; that rule was lost from the final packet. Restoring it resolves indefinite settlement, but makes participation timing part of the win condition and still leaves authored-opponent behavior unspecified.

**Recommendation:** begin with independent attacks against frozen defense snapshots, with immediate practice results. A later two-leg competition needs deadlines, expiry/forfeit rules, authored-opponent scoring, tie handling, repeat-opponent limits, and idempotent settlement. Do not introduce those obligations into the local practice milestone.

The archive also combines best-M-of-N attempts, two-leg comparisons, authored-opponent rating exclusions, and highest-score wins. Choose one format. Scores against differently difficult defenses do not automatically compare fairly. A first competitive experiment could use the same fixed challenge set and attempt allowance for every participant. Simultaneous attacks on separate towns would still use defense AI; they do not establish interactive multiplayer defense.

The proposed stolen memory fragment introduces a separate loss-and-recovery mechanic. Start with an earned commemorative copy of a defeated town's motif if this reward is tested; actual cosmetic theft, revenge prompts, and recovery pressure need their own player-value decision.

### 6. Blockchain records do not establish valid gameplay by themselves

The pitch, lines 124–141 and 171–180, and appendix, lines 80–98, claim a wallet-derived creature cannot exist in a database and that an on-chain result proves the winner. These are overclaims. A service can read public wallet history and store a derived creature. Recording a submitted result does not establish that the client played honestly.

**Recommendation:** describe the actual benefit precisely: independently inspectable provenance or publicly recorded payments, if implemented. For competitive rewards, define who validates the build, ruleset, roster, command loadout, run identity, result, and settlement. Do not assume deterministic replay of Unity combat is already available.

### 7. SKR utility and prize language need precise boundaries

Verified local implementation includes the Night Market purchase rail, external staking verification, connected polish-attempt benefits, and wallet display. It does not establish a real Arena escrow or token-earning/cash-out loop. The older Arena SKR balance is a PlayerPrefs stub seeded to 500; current captured-town practice has no wager or payout.

The pitch says there is no SKR-to-crystal conversion, yet the purchase catalog contains crystal packs and bundles with crystals. Say **one-way purchases may grant in-game items/currency; no redeemable crystal-to-SKR exchange or cash-out loop was established**. Do not erase the distinction between buying a pack and operating a reversible exchange.

Extra polishing attempts can affect progression even without changing per-attempt odds. Resource and builder purchases can also affect progression. A future skill-competition claim must account for those effects rather than treating unchanged odds as proof of no advantage.

Keep Bound Echo as an optional future identity/presentation experiment initially. Wallet history should not become an undefined competitive advantage. Specify wallet linking, recovery, multiple wallets, missing history, and stake changes before using it as persistent character identity.

The claims that sponsorship is “legal in every jurisdiction,” that payer direction alone determines gambling, or that a particular conversion automatically determines securities treatment are unsupported by this packet. Remove them from the pitch. This review does not make a legal determination; proposed prize formats need jurisdiction-specific and distribution-specific review before commitment.

Similarly, “sponsor's stake” is not a payout specification. Decide whether a future prize is paid from a fixed liquid budget, principal, or realized proceeds; define funding availability and shortfalls. A concept is not a funded season. No sponsor, return, legal status, or guaranteed prize is established here.

Keep development funding, the studio's sponsorship fee, the winners' prize budget, and operating costs separate. A sponsored prize is not automatically studio revenue. Any scheduled prize commitment should be backed by available funding rather than an assumed future staking yield.

### 8. Other evidence and scope overclaims

- “Live” must name a tested build/channel and evidence. Source wiring alone is insufficient to certify the installed APK.
- “Five shipping dungeons” must distinguish included scenes/catalogs from complete device-tested journeys.
- “+14% at six Echoes” needs a precise definition. `EchoBonusCalculator.AggregateHarvestMultiplier` also includes count, assignments, affinity, levels, pair/set bonuses, and hidden synergy. It is not safely described as the complete harvest formula or a universal cap.
- “Nobody else has this” and assumptions about grant evaluators require actual external research. They are not demonstrated by these documents.
- Three-week/two-week/one-week estimates and a three-month funding ask are unvalidated planning placeholders. Data authority, save recovery, AI behavior, UI, abuse handling, and release checks must be included before estimating.
- The appendix's pool-first order is not a necessary dependency for proving a scroll or tactical command. A video shot list should not determine engineering priority.

## Useful material to recover from the large archive

The 8,584-line transcript is an evolution of advice, not one consistent specification. Its final sections substantially repeat the three smaller documents. Preserve the originals as history and use a short current decision record instead of treating repeated proposals as additional evidence.

| Archive region | Disposition |
|---|---|
| Early HUD/layout discussion | Useful design history; current owner-accepted layout and measured captures supersede mockup assumptions. |
| Lines 818–1511: release priorities and revisions | Historical planning. Some passages prioritize Pi/WebGL or park Google Play; the owner's later APK/AAB focus governs current work. |
| Lines 1512–3659: generated state, documentation cleanup, scripts | Separate tooling proposals. Do not execute scripts or reorganize the worktree as part of this review. |
| Lines 3949–4710: release recovery and build identity | Preserve durable completion, stable authored identity, detached snapshots, isolated practice outcomes, and failure/retry proofs. Preserve the corrections rejecting fabricated pristine damage and invented implementation certainty. |
| Lines 4712–4947: SKR implementation review | Preserve the distinction between verified wiring, authored configuration, missing consumers, simulated currency, and actual token transfers. |
| Lines 4950–5392: Echo/offline economy reasoning | Useful questions about storage and production; correct the arithmetic and inspect the actual formula before accepting conclusions. |
| Later tournament, command, and pitch exploration through line 7307 | Keep alternatives as proposals. Record which were owner statements, recommendations, or later discarded assumptions. |
| Lines 7308 onward | Repeated scroll/pitch/appendix material; consolidate conceptually without rewriting the historical files. |

One owner statement deserves particular weight: at line 6908, the intended reward is a rare scroll earned through dungeon play, allowing a player to show an unusual strategy in competition. Earlier AI-generated idle-drilling proposals do not express that instruction. Preserve this intent while testing whether rarity creates interesting prestige or decisive competitive exclusion. Standardized ranked access is a recommendation that changes how competitive rarity works, not an already accepted owner decision.

That same owner statement allows a new **defense or strategy**. The later specification silently narrows every scroll to an army command. Command-only scope is a sensible small first experiment, but remains a recommendation rather than an approved permanent limit on scroll rewards.

The proposed maintenance scripts also need review before reuse. In the archive's `doc_migrate.sh`, lines 2873–2885 write archive files before the later dry-run guard, despite claiming dry run changes nothing. The extraction can keep appending later non-`Latest` content after a section ends. The proposed state generator at lines 1677–1686 finds the first configured marker rather than establishing the latest run's outcome; success-marker ordering can conceal a later failure. Its remote fetch at lines 1703–1710 does not establish a successful HTTP response before accepting content. Keep the idea of a small evidence index; do not trust these pasted implementations or run them against the dirty worktree.

### Correct the offline-bonus arithmetic

The archive, lines 5126–5131 and 5225, asserts that Echo synergy dilutes a common multiplicative bonus and that multiplying each contribution differs from multiplying their sum. Under a common factor, both assertions are incorrect:

`1.06 × (a + b) = 1.06a + 1.06b`.

If baseline production is `base × synergy`, applying a common `1.06` multiplier increases the uncapped result by exactly 6%. Adding 0.06 to an existing bonus bucket is a different formula and can produce a different relative gain. Resource-specific factors, rounding, and per-container caps can also change the realized result; those must be specified rather than assumed.

With a simple common storage cap, compare `min(cap, 1.06 × production)` against `min(cap, production)`. A full container can make the extra collected amount zero. That does not mean synergy mathematically reduced the underlying multiplier. The current Heartbound production benefit still lacks a verified gameplay consumer, so no proposed formula here should be advertised as live behavior.

## Recommended sequence

| Stage | Deliverable | Evidence required before expanding |
|---|---|---|
| Current release | Final raid → owned damaged town → repair/build → durable revisit → no-wager practice | Real player-route completion; save-failure and restart recovery; complete owned build integration; settled regression and Android channel/artifact checks; owner device acceptance. |
| Command experiment | One command, one army interaction, one encounter | Same roster and encounter produce a clear, repeatable tactical difference; commands remain usable under combat and navigation pressure; mobile controls are understandable. |
| Scroll experiment | One guaranteed dungeon-earned command and a minimal permanent library | One durable run reward; no duplicate grant on retry; reload/account recovery; equip and use in an actual raid. A failed reveal must not lose the earned command. |
| Defense-pool experiment | Authored snapshots, then opt-in player snapshots | Validated immutable versions, fair selection, immediate results, no modification of another player's live town, useful repeat-play evidence. |
| Competitive experiment | Standardized access and an explicitly versioned scoring format | Validated results, abuse controls, fair roster/build rules, season boundaries, timeout/recovery semantics, and evidence that players want the format. |
| Optional SKR expansion | Bound Echo identity and/or a separately funded prize pilot | Demonstrated player value, truthful channel-specific presentation, funding and payout design, applicable external review, and verified end-to-end operation. |

These stages are dependency recommendations, not a commitment to implement all of them. The current release does not need the last five stages to be valuable.

## Decisions to settle before a scroll work order

1. Are new commands tactical alternatives, stronger capabilities, or prestige variants? Recommendation: alternatives with clear situational tradeoffs.
2. Are basic army orders always available? Recommendation: yes; do not spend scarce loadout slots on basic control. Use a distinct name such as Withdraw for a troop order so it cannot be confused with leaving the raid.
3. Does a season remove access to learned PvE commands? Recommendation: no for the initial system.
4. What makes future ranked play fair? Recommendation: common permitted commands plus explicit roster/build/equipment rules, not access resets alone.
5. Is the first pool a one-leg defense challenge or a two-leg duel? Recommendation: one-leg challenge first.
6. Does publishing a defense require explicit player choice? Recommendation: certification and opt-in publication of a frozen version, not every town save automatically becoming public competition content.
7. What does SKR add that players value independently of promised earnings? Recommendation: retain honest existing purchase/staking utility while testing optional identity features later.

## A safer pitch draft

> Echoes of Elarion combines hero-led raids, town building, and dungeon exploration. Our current release work connects capturing a town with repairing, improving, and testing its defenses in no-wager AI practice. The next design experiment is a permanent command library: dungeon discoveries teach tactical options that players can use in later battles. The Solana edition has SKR purchase and staking integrations; a broader opponent pool, wallet-derived Echo identities, and sponsored competitions remain future proposals.

For external use, attach a short, dated list of what the demonstrated build actually proves. Keep proposed footage clearly separate from captured gameplay. Replace claims of uniqueness with the specific experience the viewer can see.

## Questions to send for further advice

1. “Design one useful troop command for our existing target-seeking/rally AI. Specify who it affects, how it overrides combat, how it ends, blocked-path handling, and a test encounter. Do not assume three empty HUD slots are unowned.”
2. “Design permanent dungeon-earned tactics for casual play and equal command access for a later ranked format. Show which advantages remain from roster, gear, buildings, consumables, purchases, and extra polishing attempts.”
3. “Compare guaranteed, random, and mastery-based unlocks for 15–35-minute runs. Quantify median and long-tail acquisition time for a specific command, and define duplicates, guarantees, and crash recovery.”
4. “Specify a one-leg asynchronous defense challenge using frozen build snapshots. Cover publication, versioning, result validation, retries, disconnects, opponent scarcity, and preservation of the owner's real town. Exclude token prizes.”
5. “Review the proposed SKR pitch strictly for claim accuracy. Separate working purchase/staking features, simulated balances, incomplete benefits, and future prize/identity ideas. List evidence needed for each public claim.”
