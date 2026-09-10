# PROD-022 remote tunables — the flags you flip instead of rebuilding

**Owner ruling 2026-09-02, verbatim:**

> *"make the testing as robust as possible with as many solutions as possible... all we really have to
> do is just flip a flag and possibly redeploy"*

A WebGL rebuild costs about **thirty minutes**. PROD-022 is a P0 crash loop that reproduces inside
**Pi Browser on the owner's iPhone and nowhere else** — desktop Chrome ran the identical build for
62 minutes. So every candidate mitigation ships in **one** build, each behind its **own independent
flag**, and the bisect becomes flag flips against the database instead of half an hour per hypothesis.

---

## ⛔ The invariant that outranks everything else here

> **No row, no network, no server, no parse ⇒ TODAY'S BEHAVIOUR, EXACTLY.**

Every default in the table below is the value the shipping code hardcoded before PROD-022 touched it.
A player who is offline, whose fetch times out, who gets a 404, or who receives malformed JSON resolves
**every** knob to that default. The remote read is an **override**, never a dependency, and the fetch
never blocks or delays boot.

**An empty `client_tunables` table is the correct resting state, and it is what ships.**

> **⚠ #10 and #11 are the honest exception, and it is stated rather than hidden.** They are BUG FIXES
> (WO-1327), so their defaults are the FIXED values, not the broken ones — an empty table gives you
> this build's corrected VFX collision and light budget, not `Spell_Fire_9`'s authored 25 lights and
> perfectly-elastic fireballs. The invariant still holds in the form that matters: **no row, no
> network, no server, no parse ⇒ exactly what this build hardcodes**, and the previous behaviour is
> reachable in one flip (`vfx.particleBouncePct 100`, `vfx.maxParticleLights 25`) if the owner judges
> the new feel wrong.

---

## The flags

| # | Key | Kind | Default (= today) | ON / raised does | Hypothesis it tests |
|---|---|---|---|---|---|
| 1 | `pi.eagerStructureWarm` | bool | `0` (OFF) | Pi Browser runs the **full desktop warm pass** — await Addressables init, harvest keys, `DownloadDependenciesAsync`, load and retain all 35 structure prefabs — instead of the on-demand policy. | That on-demand streaming is itself the problem and eager residency is healthier on this webview. WO-PROD-022 forbids re-enabling eager residency *without proof*; this knob is how the proof gets gathered rather than assumed. |
| 2 | `pi.awaitInitBeforeFirstLoad` | bool | `0` (OFF) | Pi awaits `Addressables.InitializeAsync` **and harvests every registered key before the first on-demand load**. Requests raised meanwhile are queued, never dropped. Residency policy untouched — this is *not* the eager warm. Also holds `WhenSettled` until init lands. | **PRIME SUSPECT.** Today the Pi branch returns from `Boot()` without ever awaiting init and without harvesting keys, so the *first on-demand request is the first thing that touches the catalog*, and `State` is `Degraded` from frame one — making `IsSettled` **true immediately**, so a `WhenSettled` retry can fire before a single location exists. That is the shape of the observed `model not found` storm. |
| 3 | `pi.disableRemoteStructureArt` | bool | `0` (OFF) | Pi issues **no remote structure-art request at all**. Callers keep the path they already take when an asset is not resident: the baked twin or the visible pending-art proxy. | **THE BIG HAMMER**, decisive in *both* directions. If the crash loop **stops**, asset streaming is implicated beyond argument. If it **continues**, streaming is exonerated and the cause is elsewhere — worth just as much. Trades visual fidelity for a clean signal, on purpose. |
| 4 | `assets.maxConcurrentRequests` | int | `0` (= today) | Caps residency fetches in flight. `0` = today: Pi serialises through its own latch, desktop is unbounded. `1`+ installs an explicit shared queue with that ceiling on **every** host. | That several simultaneous multi-MB bundle downloads plus decompression blow a memory ceiling **outside the managed heap** — exactly how the captured sessions look, dying with `mem=247MB` flat and no exception. |
| 5 | `pi.requestTimeoutSeconds` | int | `20` | The `UnityWebRequest` timeout installed by the Pi Addressables `WebRequestOverride`. | That 20 s is the wrong bound. **Unchanged at 20 deliberately** — the root is not proven and picking a new constant would bake in a guess. It ships tunable so the number moves on *data*. |
| 6 | `assets.maxRequestAttempts` | int | `3` | Async fetch attempts one address gets before it is retired for the launch. | That the retry budget is mis-sized: too high and the retry storm is itself the load that kills the tab; too low and one transient stall costs a building its art for the session. |
| 7 | `visuals.missLogCap` | int | `3` | Full resolve-miss `Fail` lines `VisualFactory` emits per address before announcing its cap and dropping to a throttled line. **It never goes silent.** | That trace *volume* is a contributor — the observed final seconds were nothing but four addresses cycling, and every line is a remote trace POST from the suspect device. |
| 8 | `trace.assetVerbosity` | int | `2` (= today) | Narration level for `[Flow:StructureAssets]` and `[Flow:VisualFactory]`. `2` = today (every Step, including the `-> Skin(...)` / `<- Skin(...)` pair). `1` = lifecycle Steps only. `0` = no Steps. | Same volume hypothesis as #7 but separable: silences the *success* narration while leaving every failure line intact, so a quiet session can be compared against a loud one. |
| 9 | `combat.drainReturnPct` | int | `60` | Percent of the damage a **drainshot** ability actually deals that returns to the caster as healing. Applies to **every** drainshot - `mage.siphon` (Syphon Essence), `mage.drain`, `ranger.healing-shot` - because `HeroAbilities.HealFromDrain` is the single owner of the drain heal. Clamped to `0..1000` at that consumer. Set the row to `100` for the old heal-equals-damage-dealt behaviour. | **Not a PROD-022 hypothesis - a BALANCE lever (WO-1306, retuned WO-1330).** ⛔ **This default is a RULED VALUE, not the previously-shipped one, and must not be "corrected" back to 100.** Owner ruling 2026-09-02, verbatim: *"keep drain at 60% for now"*, with the governing intent *"drain should help stave off not run the show"* - sustain buys time, it does not win fights. A mage who out-heals incoming damage never has to disengage, which deletes the tension the loop depends on. WO-1306 shipped 100 because that was what the resolver hardcoded; she has since chosen 60. |
| 10 | `vfx.particleBouncePct` | int | `0` | Percent restitution allowed on a **world-colliding particle** inside any VFX host the pooled spawner checks out. `0` = this build: a particle that hits scene geometry stops there and terminates (bounce 0, dampen 1, lifetime-loss 1). `100` = leave the art pack's authored collision completely alone. The clamp only ever **tightens**, so it can never make an effect bouncier than its author made it. | **Not a PROD-022 hypothesis — a FEEL lever (WO-1327).** `Spell_Fire_9`'s `Fireballs` emitter is authored `bounce 1.0` / `dampen 0` / `minKillSpeed 0` against **all 32 layers** at High quality — perfectly elastic, and no impact ever kills the particle. Cast inside a walled town that is a projectile in a box. The owner reported the fire spell twice (F8 seq 4152, 4644). The offending numbers live in a **gitignored** pack prefab, so the clamp has to live at the spawn owner; this knob is how she moves it without a rebuild, and how she puts the authored behaviour back in one word. |
| 11 | `vfx.maxParticleLights` | int | `4` | Caps the concurrent real-time point lights **one spawned VFX host** may drive through its ParticleSystem LightsModules, summed across every emitter on that host. `0` turns particle lights off outright; a number at or above a host's authored total leaves that host untouched. The budget is spent evenly across the host's enabled modules and each module's `ratio` scales down with it. It never deletes a light prototype. | **Not a PROD-022 hypothesis — a MOBILE PERF lever (WO-1327).** `Spell_Fire_9` drives **20** lights from its `Fireballs` emitter and **5** more from its `Explosion` sub-emitter: **25 real-time point lights per cast**, intensity 5, range 5, on the Seeker. That is a frame-rate event on every fireball. Same gitignored-prefab problem as #10, same answer. What it tests is how many lights the device can actually carry — which only a device capture answers. |
| 12 | `combat.overTimeTickMs` | int | `1000` (= today) | Milliseconds between the pulses of **every** over-time effect, damage and healing alike. `1000` = today: exactly the `const float tick = 1f` that both shipped DoT coroutines hardcoded. Magnitude per pulse is derived as `perSecond * interval`, so this moves **cadence only** - total delivery is invariant under it. Clamped to `50..60000` at `OverTimeTuning`. | **Not a PROD-022 hypothesis - a FEEL lever (WO-1330).** How often a DoT ticks *is* the read of the effect: at 1000 ms it is four countable thuds over four seconds; at 250 ms the same total damage becomes a continuous drain. Which one communicates "this is still hurting you" is a question only felt-testing answers - and with the owner red/green colourblind, **rhythm is carrying signal that colour cannot**, so it has to be movable in seconds. |
| 13 | `combat.overTimeMagnitudePct` | int | `100` (= today) | Percent scale on the magnitude of every over-time pulse, **both signs**. `100` = today: the authored `dotDamage` / heal-per-second, unscaled. `50` halves every DoT and every regen at once; `0` makes them inert without unauthoring anything. Clamped to `0..1000`. | **Not a PROD-022 hypothesis - a BALANCE lever (WO-1330).** One shared knob rather than one per ability, because the first tuning question is always whether over-time damage *as a class* pulls its weight against burst - and that is a single dial. Per-ability numbers stay in `abilities.json`; this scales all of them together. |
| 14 | `combat.overTimeDurationPct` | int | `100` (= today) | Percent scale on the duration of every over-time effect, **both signs**. `100` = today: the authored `dotSeconds` / `seconds`, unscaled. Raising it **adds pulses**, so unlike #13 it moves TOTAL delivery rather than per-pulse size. Clamped to `0..1000`. | **Not a PROD-022 hypothesis - a BALANCE lever (WO-1330).** Deliberately separate from #13: "each pulse hurts more" and "it lasts longer" feel completely different at the same total, and collapsing them into one dial would make that distinction untestable. This is the knob that decides whether an over-time ability is a commitment or a garnish. |
| 15 | `vfx.nightStoreAuraMode` | int | `0` (= today) | Which effect the Realm / Night Store wears. `0` = this build: her FIRST tagged key `NightStoreoption_Aura` -> `top_down_starfall_line_blue.prefab`, a one-shot burst re-fired on the cadence. `1` = her SECOND tagged candidate `Store_Aura` -> `Loot_flicker.prefab`, also a burst. `2` = walk the seven continuous `Aura_*` spell prefabs, one at a time, in folder order, advancing on the cadence. `3` = the `store.beacon.near` Marker8 safe-zone ring this build replaced. Any other value is ignored and resolves to `0`. | **Not a PROD-022 hypothesis, and not balance either - a CREATIVE choice the owner has explicitly not made (WO-1343).** She tagged one store aura, then a second (*"i added another option for REalm store, not sure which will be best"*), then asked whether the `Aura_*` family could cycle *"slowly one after another instead ... IF THE OTHER ONE DOESNT LOOK GOOD"*. Three candidates and a conditional, every one of which needs to be seen on a device. Building one and discarding the rest would either pick for her or cost a 30-minute rebuild per opinion. Her first pick is the default and nothing promotes the others (memory `vfx-map-owner-tags-no-creative-pick`). |
| 16 | `vfx.nightStoreAuraCadenceMin` | int | `30` (= today) | Minutes between Night Store aura cadence ticks - her number verbatim (*"its to be random when in town every 30~min"*). What a tick DOES depends on #15: in a **burst** mode it re-fires the burst; in **rotate** mode it advances to the next aura; against the **continuous** legacy ring it is inert. Clamped to `1..1440` at the consumer. The clock runs in **TOWN ONLY** (`HubScenes.IsHub`) - never during a raid, a battle or a dungeon. | **Not a PROD-022 hypothesis - a FEEL lever (WO-1343).** Whether a half-hourly pulse reads as *"the store just caught my eye"* or as *"nothing ever happens there"* is a question only a felt-test on the device answers - and with the owner red/green colourblind, **rhythm is carrying signal that colour cannot**. |
| 17 | `vfx.nightStoreAuraFamilyMask` | int | `127` (= all seven) | Bitmask of which `Aura_*` prefabs the rotation may select, in the folder's own alphabetical order: `1` Arcane, `2` Dark, `4` Fire, `8` Ice, `16` Light, `32` Nature, `64` Storm. **Inert unless #15 is `2`.** A member she has not yet tagged in the VFX Caster does not resolve, is **skipped BY NAME in the trace**, and is never substituted for. A mask that enables nothing falls back to her first tagged key rather than leaving the store bare. | **Not a PROD-022 hypothesis - the WO-1343 requirement that "a prefab she dislikes comes out without a code change".** A bitmask rather than a string list because that rides the existing **integer** rail: adding a new tunable VALUE KIND for one feature is exactly the second configuration mechanism this rail exists to avoid. |
| 18 | `vfx.nightStoreAuraBurstRepeatSec` | int | `0` (OFF = today) | Seconds between EXTRA re-fires of the burst **inside one cadence period**. `0` = this build: exactly one burst per cadence tick, which is her spec read literally. A few seconds turns the half-hourly pulse into a slow heartbeat. Clamped to `0..600`. **Ignored entirely in the two continuous modes** (rotate-family and the legacy ring), where there is no burst to repeat. | **Not a PROD-022 hypothesis - the escape hatch for the one number in this feature that was MEASURED rather than chosen (WO-1343).** Both store candidates were verified one-shot (every ParticleSystem `looping:0` on `top_down_starfall_line_blue` and `Loot_flicker`), so her `isLoop:false` is **correct** and a burst is what she authored - there is no flag conflict at this site. But `30~min` was a rough number said in passing, and if one pulse per half hour reads as nothing at all, this fixes it without a rebuild and without anyone re-tagging her prefab. |
| 19 | `raid.lootWoodBase` | int | `1800` | **WOOD** a raid pays at a PERFECT result - 3 stars AND 100% destruction - on a Camp I-tier base, **before** the camp's own `rewardMultiplier`. Every lesser result pays a share of it off the ladder in #21-#25. `0` restores the old behaviour, in which a raid paid **no wood at all**. Clamped to `0..1000000` at `RaidLootTunables`. | **Not a PROD-022 hypothesis - the CENTRAL BALANCE NUMBER of the raid programme (WO-1374).** ⛔ **This default is the OWNER'S TARGET, not the previously-shipped value** - today a raid pays zero wood, which is the defect the work order exists to close, and `docs/PROGRAM_RAID_ECONOMY_2026-09-04.md` §1 states 1,800 outright. Same deliberate departure as #9/#10/#11. The map sizes it at 60-65% of four hours of passive wood output so that a raid funds real construction *without making collectors worthless* - a ratio only a felt-test on a real save can confirm, and she is setting this curve by feel. |
| 20 | `raid.lootIronBase` | int | `1100` | **IRON** a raid pays at a perfect result, on the same terms as #19. `0` restores the old no-iron payout. Clamped to `0..1000000`. | **Not a PROD-022 hypothesis - the second half of the WO-1374 reward table.** Its own knob rather than a ratio off #19, because wood and iron bottleneck at different points in the build tree and "which should a raid favour" is a real question she will want to answer separately. ⭐ **The gold knobs are #26-#29 now.** This cell used to say there was deliberately no gold knob because WO-1374 was BLOCKED on an unresolved fork; **that fork was CLOSED at commit 281902df0** - troops cost GOLD, also take time, and a second gold spend hires mercenaries to skip the clock. Gold is four per-camp knobs rather than one, because the map publishes a designed target per tier. |
| 21 | `raid.lootFailPct` | int | `18` | Percent of #19/#20 a **FAILED** attack still pays. `18` is the middle of the map's stated 15-20% band. Clamped to `0..1000`. | **Not a PROD-022 hypothesis - a RETENTION lever (WO-1374).** ⚠ Deliberately **not zero**: the map is explicit - *"A failed attack still pays 15-20%. That is deliberate - it keeps a loss from being a dead end."* Whether a wipe reads as *"twenty wasted minutes"* or as *"I nearly had it, go again"* is exactly what this number sets, and it is unknowable from a spreadsheet. |
| 22 | `raid.lootOneStarPct` | int | `50` | Percent of #19/#20 paid at **1 star**. Clamped to `0..1000`. | **Not a PROD-022 hypothesis - one rung of the map's performance ladder (WO-1374).** The ladder is the mechanism by which *"getting better at raiding has an economic payoff"* - the map's own phrase, and the reason the rungs are separate knobs rather than one curve constant. |
| 23 | `raid.lootTwoStarPct` | int | `75` | Percent of #19/#20 paid at **2 stars**. Clamped to `0..1000`. | **Not a PROD-022 hypothesis - see #22.** The gap between this rung and #24 is what decides whether a player pushes for the full clear or banks a safe two and leaves. |
| 24 | `raid.lootThreeStarPct` | int | `100` | Percent of #19/#20 paid at **3 stars**. `100` means three stars pays exactly the base - i.e. this rung is what *defines* what #19/#20 mean. Clamped to `0..1000`. | **Not a PROD-022 hypothesis - see #22.** Moving it off 100 re-anchors the whole table without touching the two bases, which is the cheap way to answer "is every raid paying slightly too much". |
| 25 | `raid.lootPerfectPct` | int | `110` | Percent of #19/#20 paid at **3 stars AND 100% destruction** - the only rung that pays **above** the base. Clamped to `0..1000`. | **Not a PROD-022 hypothesis - the top of the ladder (WO-1374),** and the one number that says mastery is worth more than victory. If the 10% premium is not worth the extra minutes of mop-up, this is where that is discovered and fixed without a rebuild. |
| 26 | `raid.lootCoinsBaseCamp1` | int | `2200` | **GOLD** a raid pays at a PERFECT result on **Camp I** (`raider_camp_small`), and the fallback for any raid config id the per-camp table does not name. Rides the SAME five-rung ladder (#21-#25) as wood and iron. ⚠ **NOT multiplied by the camp's `rewardMultiplier`** - the escalation lives in #27/#28/#29. `0` stops raids paying gold. Clamped to `0..1000000` at `RaidLootTunables`. | **Not a PROD-022 hypothesis - THE MISSING ARROW.** `docs/PROGRAM_RAID_ECONOMY_2026-09-04.md` names it outright: *"You currently have Gold -> troops but not troops -> raids -> gold. That arrow has to exist."* Sized at 125-140% of that camp's **designed** 1,650-gold army replacement cost, so a win is **+550 gold of advancement** - *"now the player can actually raid again."* Whether that reads as advancement or as barely-worth-it is a felt question. |
| 27 | `raid.lootCoinsBaseCamp2` | int | `3100` | **GOLD** at a perfect result on **Camp II** (`fortified_garrison`), against that camp's designed 2,300-gold army. Clamped to `0..1000000`. | **Not a PROD-022 hypothesis - one rung of the map's per-camp gold escalation.** Its own knob rather than a multiplier off #26 because the map publishes a **designed target per camp**: x1.5 of 2,200 is 3,300, not 3,100. The step between camps is what decides whether unlocking a harder raid feels like progress. |
| 28 | `raid.lootCoinsBaseCamp3` | int | `4500` | **GOLD** at a perfect result on **Camp III** (`mage_enclave`), against a designed 3,300-gold army. Clamped to `0..1000000`. | **Not a PROD-022 hypothesis - see #27.** Sized against the army the player is **expected** to bring, never their actual one: *"not the player's ACTUAL army cost, because that could be gamed."* |
| 29 | `raid.lootCoinsBaseBastion` | int | `6500` | **GOLD** at a perfect result on the **Iron Bastion** (`iron_bastion`), against a designed 4,800-gold army. The `iron_bastion` config row exists as of 2026-09-04 (`rewardMultiplier` 2.8, which gold deliberately ignores - 2,200 x 2.8 is 6,160, not her 6,500). Clamped to `0..1000000`. | **Not a PROD-022 hypothesis - the top of the per-camp gold ladder,** registered now so the number is a knob from the day the Bastion is switched on rather than a literal someone has to find later. |
| 30 | `raid.lootCrystalsBase` | int | `20` | **CRYSTALS** a raid pays at 100% destruction, before the per-star bonus in #31. With #31 a perfect clear pays **26**, inside the map's 20-30 band and **down from the 55** this build used to pay. ⚠ **NOT multiplied by the camp's `rewardMultiplier`.** Clamped to `0..1000000`. | **Not a PROD-022 hypothesis - a PACING lever, and the only reward in the map's table that DECREASES.** *"Crystals are timer compression. If raids dump huge amounts of crystals, you accidentally accelerate the already-too-short progression curve."* Crystals buy instant-finish, so this number silently sets how long the whole build tree takes. A harder camp must pay more gold/wood/iron, never more time compression - which is why the camp multiplier is excluded. |
| 31 | `raid.lootCrystalsPerStar` | int | `2` | Extra **CRYSTALS** per earned star, on top of #30. Down from `10`. Clamped to `0..1000000`. | **Not a PROD-022 hypothesis - the second half of the crystal cut.** Kept separate from #30 so *"should a great raid pay more crystals, or just more gold"* stays a question that can be answered without re-deriving the base. |
| 33 | `raid.heartfireMaxCharges` | int | `3` | The **Heartfire** pool ceiling - how many raids can be launched back to back before waiting on a rekindle (canon section 4: three charges). `HeartfireCharges` aliases the same const. | **Not a PROD-022 hypothesis - the ONE gate on when you may raid (WO-1379).** Replaced the per-camp cooldown wall. Higher = more raids per session. |
| 34 | `raid.heartfireRegenSeconds` | int | `14400` | Seconds for ONE Heartfire charge to rekindle. `14400` = 4 h, deliberately equal to the shortest authored per-camp cooldown it superseded. | **Not a PROD-022 hypothesis - the Heartfire pacing lever (WO-1379).** Lower = faster raid cadence. |
| 35 | `economy.packTemporaryBuilderSeconds` | int | `21600` | Seconds of extra Builder crew ONE `temporary-builder` pack charge grants - the $1.99 **Builder's Hour** (`builders-hour`, WO-1388). `21600` = 6 h, the owner's number. A charge bought while a window is running is **deferred** behind it (PlayerPrefs `convenience.temporary-builder.deferred`) and starts when it ends - never stacked, never burned. `0` refuses the grant and keeps the charge deferred. `BuildTimerConfig.packTemporaryBuilderSeconds` authors the same 6 h; the row wins when set. | **Not a PROD-022 hypothesis - the one lever on the cheapest micro-transaction (owner 2026-09-04: "we have 0 sales" / "6 hours").** Convenience compresses TIME, never power. Whether six hours turns a first tap into a habit is felt, not derived. |
| 36 | `hud.nightMarketGlowLapSec` | int | `5` | Seconds for the HUD **Night Market card's** three comets to make ONE lap of the card's perimeter (WO-1384b). Clamped to `1..60` at `HudKitController.NightMarketGlowKnobs.LapSec` - never frozen, never a blur. Read from the rail when the card is built (`BuildNightMarketCard`), traced once as `[Flow:Store] Night Market glow knobs from rail: lap=.. alpha=.. mask=..`; the animator reads the static every frame. | **Not a PROD-022 hypothesis - a FEEL knob on the store's permanent HUD face (owner 2026-09-02: "make it tweakable from a db call").** Whether a five-second lap reads as alive or as nagging on a phone in a dark room is judged on device, never derived. |
| 37 | `hud.nightMarketGlowAlphaPct` | int | `35` | Peak alpha, in percent, of the Night Market card's ring and comet heads (WO-1384b) - a rim light, not a spotlight. Clamped to `0..100` at `NightMarketGlowKnobs.AlphaPct`; `0` keeps the card and hides the glow entirely. Same read point and trace line as #36. | **Not a PROD-022 hypothesis - the brightness half of the same feel question.** The card must stand out from the Heart plate above it without out-shouting the action bar; the owner is colourblind, so contrast is judged on device. |
| 38 | `hud.nightMarketGlowPaletteMask` | int | `7` | Bitmask of the warm palette stops the glow drifts through (WO-1384b): **Gold=1, Amber=2, Rose=4**; `7` = all three (gold -> amber -> rose -> gold). Clamped to `0..7` at `NightMarketGlowKnobs.PaletteMask`; a single bit holds one steady colour, and an empty mask resolves to Gold alone (logged once), never to nothing. Same read point and trace line as #36. | **Not a PROD-022 hypothesis - "take that colour out of the cycle" as one number,** on the same integer rail as the WO-1343 aura mask (#17). No code change, no schema change. |
| 39 | `arena.wagerTier1` | int | `50` | **CRYSTALS** staked to fight the tier-1 Arena opponent (Ironhold Marauders, WO-1366). `50` = exactly what `ArenaCatalog.cs` hardcoded. Clamped to `1..100000` at `ArenaWagerTunables`, which reads the rail only once the key has a `TunableSpec` and answers its own default otherwise - so a stake can never resolve to the rail's unregistered `0`. | **Not a PROD-022 hypothesis - a PRICE.** Crystals are bought on Google Play, so the wager is real money at one remove, and the three amounts were authored against a free 500-seed stub and carry no information about Crystals (SAMANTHA.md rule 8: not re-picked here). Whether `50` reads as a flutter or a gamble is felt on device. |
| 40 | `arena.wagerTier2` | int | `100` | **CRYSTALS** staked against the tier-2 opponent (Grimwatch Reavers). Same clamp and read point as #39. | **Not a PROD-022 hypothesis - the middle rung of the same price ladder.** Its own knob rather than a multiple of #39 because the step between tiers is what decides whether the next fight reads as a step up or as a wall. |
| 41 | `arena.wagerTier3` | int | `200` | **CRYSTALS** staked against the tier-3 opponent (Blackbanner Host). Same clamp and read point as #39. | **Not a PROD-022 hypothesis - the top of the wager ladder,** and the one stake a player has probably bought Crystals to reach. Raising it is asking for a bigger purchase. |
| 42 | `arena.winPursePct` | int | `200` | The purse an Arena **WIN** pays, as a **percent of the wager**: `200` = stake back plus the same again, exactly the `2x` `ArenaCatalog.cs` hardcoded (`PurseFor = wager * pct / 100`). Clamped to `100..1000` at `ArenaWagerTunables`; `100` returns only the stake, and below that a win would lose money, which the clamp refuses. | **Not a PROD-022 hypothesis - the house edge as one number.** Whether `2x` is generous enough to make a Crystal wager worth the risk, or so generous the Arena becomes a Crystal faucet that undercuts the store, is the balance question this row answers without a rebuild. |
| 32 | `raid.starterArmySize` | int | `3` | How many **free Footmen** a save receives the first time it has a Barracks. `3` is her number, and exactly what 1,650 gold used to buy. Granted **once per save** via the monotonic acquired-ledger key `grant.starter-army`, so a demolished-and-rebuilt Barracks is **not** a troop faucet. `0` disables the grant. Clamped to `0..10` at `StarterArmyGrant`. | **Not a PROD-022 hypothesis - the FTUE lever the map opens with (WO-1374, §2):** *"A player starts with 200 gold but needs 1,650 to participate in the thing you're trying to teach them. That's basically putting a nightclub behind a velvet rope and handing the player twelve cents."* Whether three troops makes the first raid feel winnable - or whether it needs four - is a felt question about the **first ten minutes of the game**: the most expensive ten minutes to get wrong and the most expensive to iterate on through a rebuild. ⚠ Correct under BOTH sides of the blocked troop-cost fork, which is why it shipped while the gold half did not. |
| 43 | `raid.lootRepeatClearPct` | int | `60` | Percent of the ordinary **wood / iron / stone / gold** a raid pays when the player clears the **same camp again while its cooldown is still running**. `100` removes the repeat penalty. ⚠ **Crystals are NOT on this axis** - they are all-or-nothing on the once-per-UTC-day stamp (`RaidClaimService.CrystalsPaidToday`). Clamped to `0..100` at `RaidClaimService.RepeatClearPct`, deliberately **not** `0..1000`: a repeat that paid more than a first clear would invert the whole first-clear gate, so a console typo must degrade to "pays full", never to "pays double". | **Not a PROD-022 hypothesis - the FARM-SUPPRESSION lever (WO-1461).** ⛔ **This default is the OWNER'S RULING, not the previously-shipped value** - same deliberate departure as #9/#10/#11/#19/#20. Owner, 2026-09-06 20:33, verbatim: *"100% first clear after cooldown, 60% repeat clear during the same cycle, then reset to 100% when the camp's cooldown expires."* The build shipped `25`, as a compiled `const float RepeatClearLootMultiplier = 0.25f`, and **that is why the two could disagree for three days**: a constant cannot be flipped. Setting this to `25` restores the old behaviour exactly. How hard a re-clear should bite is a felt question about whether replay reads as practice or as a chore. |
| 46 | `raid.stagingCeilingSeconds` | int | `900` | Raises the absolute **wall-clock ceiling on raid STAGING** before the stranding watchdog routes the player home. `900` = fifteen minutes, and it is exactly the fallback `RaidDeployController` already answers for itself while this key has no `TunableSpec` — so registering it changes nothing on its own and simply makes the number reachable without a rebuild. | **WO-1095 - staging is free on the raid clock, so only a dead session should trip a wall-clock bound.** How long a player may sit in staging before the game decides the session is dead is a felt question about patience, not a constant anyone chose. |
| 45 | `hero.playableFallbackHalf` | int | `50` | Half-extent, in **metres**, of the hero's off-mesh **playable-bounds clamp** in a scene whose world extent cannot be **measured** from a `Terrain` (WO-1094). `50` is **exactly the hardcoded bound this build replaced**, so an empty table reproduces today's behaviour in every unmeasured scene. Where a Terrain **is** present the **measured** extent wins and this row is never read. Clamped `1..100000` at the consumer, so a console typo of `0` cannot pin every unmeasured scene's hero to the origin. The relocation trace names which bound applied (`source=fallback-tunable` vs the measured extent). | **Not a PROD-022 hypothesis - the honest FLOOR under a derived bound.** The old clamp was a `±50` box written for a castle and left standing in a merged 1000x1000 world, where it teleported a legitimately-placed hero back to the old bounds (WO-1091 capture: `(-400,0) -> (-50,0)` in one tick). Deriving the bound fixes the hub - but `Village2`, `RaidBase_*` and the legacy `MainCastle_Hall` carry **zero** Terrain components, and there a purely-derived bound is **no** bound: the hero drifts unbounded on the off-mesh transform fallback, inside the raid loop. Whether `50` is still the right box for a **raid base** - it was authored for the old castle - is a felt question, and now a row. |
| 44 | `raid.cacheCapPerResource` | int | `1800` | Per-resource ceiling on the **RAID CACHE** - the temporary hold that catches raid loot the town bank had no room for, so a win above the cap **waits** instead of being destroyed. Applies to the **capped** resources only (wood / iron / stone); crystals and gold have no ceiling at all (`TownBankCapacity` Law 1) and are never clamped. `0` turns the cache off and restores the pre-WO-1461 burn. Clamped to `0..1000000` at `RaidClaimService.CacheCapPerResource`. | **Not a PROD-022 hypothesis - and ⚠ THE ONE ROW IN THIS TABLE WHOSE DEFAULT IS NEITHER TODAY'S BEHAVIOUR NOR A NUMBER THE OWNER STATED.** She ruled the **mechanic** (2026-09-06 20:33: *"Never destroy raid loot because storage is full. Put overflow into a temporary Raid Cache with a modest cap... A message like 1,775 Wood held in Raid Cache - storage full turns frustration into a progression prompt."*) and named **no size**. `1800` is a **stated derivation, not a pick**: exactly one perfect Camp I wood haul (#19), so the cache holds at most **one raid's worth** of any one resource and cannot quietly become a second, larger bank. It is registered as a knob precisely so her number lands in seconds. Too small and a win still mostly evaporates; too large and the cache removes the upgrade pressure it exists to create (WO-1461 §4). |
| 47 | `raid.honorThirdStarSeconds` | int | `90` | Elapsed **raid** seconds after which the **third honor star** goes dark - the speed honor (WO-1594). `90` is half the 180 s clock and exactly the `90f` `RaidScoring` hardcoded. The clock is engagement-gated (WO-1520), so staging never spends it. Read through `RemoteTunables.SpecFor` **before** `Int`, so an unregistered key answers this default rather than `Int`'s `0`-for-unknown; floored at `1` at the consumer. ⚠ This is **not only presentation**: `RaidScoring.Finalize` clamps the payout to `min(settle, honor)`, so lowering it lowers what raids pay. | **Not a PROD-022 hypothesis - the PACE OF A RAID as one number.** ⛔ Owner ruling 2026-09-09, verbatim choice: *"Tunables with those defaults"* - she was offered the milestones as constants or as rows and chose rows at 90 / 150 / 50. Whether half the clock is the right price for the third star is felt on device, and it used to cost a 30-minute rebuild per opinion. |
| 48 | `raid.honorSecondStarSeconds` | int | `150` | Elapsed **raid** seconds after which the **second honor star** may go dark - and only if destruction is still under #49 (WO-1594). `150` is the last 30 s before a 180 s timeout, exactly the `150f` `RaidScoring` hardcoded. Same `SpecFor` guard and `1` s floor as #47. Setting it above the raid clock retires the milestone entirely. ⚠ The **first** star is never snuffed mid-fight for time alone - cracking the camp always pays something - and no row can change that. | **Not a PROD-022 hypothesis - the second half of the same pacing question,** on the same 2026-09-09 ruling as #47. Its own knob rather than a multiple of #47 because the gap between the two snuffs is what decides whether a long raid reads as tense or as already lost. |
| 49 | `raid.honorSecondStarMinDestructionPct` | int | `50` | Destruction **percent** a camp must have reached by #48 to **keep** the second honor star (WO-1594). `50` is the `0.50f` `RaidScoring` hardcoded. It is a **percent, not a fraction**, because the rail is integer-only; `RaidScoring.HonorSecondStarMinDestruction` clamps `0..100` and divides by `100`, so a row of `400` cannot make the milestone unsurvivable. `0` means the second star is never taken; `100` means only a total razing keeps it. | **Not a PROD-022 hypothesis - the "are you actually making progress" half of the T2 milestone,** same 2026-09-09 ruling. It is the mercy clause: a player who is dismantling the camp keeps the star however long it takes, and this number is where that line sits. |
| 50 | `raid.roughStoneMinTier` | int | `3` | Lowest raid camp **TIER** that may drop a rough stone (WO-1373). The camps sit on an ordered I..IV ladder - `raider_camp_small` / `fortified_garrison` / `mage_enclave` / `iron_bastion`, the four `RaidLootTunables.CampIdCamp1..CampIdBastion` constants - and `3` is the **LOWER of the top two rungs**, so the Veiled Enclave and the Iron Bastion drop and nothing below them does. `2` admits the Broken Garrison; `5` turns the raid drop off entirely and is the row that restores the pre-WO-1373 build. Clamped `1..99` at `RaidScoring.RoughStoneMinTier`. | **Not a PROD-022 hypothesis - the owner's ruling 2026-09-09, verbatim:** *"only top two tiers of raids can drop stone"*. ⚠ **There is no `tier` field in `scene-configs.json`** - the ladder is the code table above, corroborated by that file's own `unlockVictories` (`0` / `3` / `10` / `20`) and `difficulty` (`Regular` / `Hard` / `Extreme` / `Extreme`). Which rung counts as "top" is the one part of her sentence a fifth camp would change, which is why it is a row and not a hardcoded id list. |
| 51 | `raid.roughStonePerDayCap` | int | `1` | How many rough stones **every raid together** may pay inside one UTC day (WO-1373). **GLOBAL, not per camp**: clearing both eligible camps on the same day pays one stone, not two. `0` turns the raid drop off without touching #50. The ledger is a UTC day key plus a count in PlayerPrefs (`dotr-raid-stoneday` / `dotr-raid-stonecount`), so it self-expires exactly like the crystal day-stamp and needs **no save-schema bump**. Clamped `0..99` at `RaidScoring.RoughStonePerDayCap`. | **Not a PROD-022 hypothesis - the FAUCET BOUND on the only material the Jeweler chain consumes,** and the owner's ruling 2026-09-09: *"no more than 1 per day"*. ⚠ Like #50 this opens a faucet that **did not exist before this ticket** - no raid path granted the stone - so it has no "today's behaviour" to reproduce, and that is stated rather than hidden. How fast the ring ladder climbs is this number. |
| 52 | `dungeon.roughStoneDropPct` | int | `5` | **Percent** chance a completed, **non-starter** dungeon run pays a rough stone once the player has already earned their first (WO-1373). Starter dungeons are excluded by a separate **layout `tier` gate** (`Assets/Resources/Data/Canonical/dungeon-layouts/<id>.json`, field `"tier"`; tier 1 = starter), not by this number, and the **guaranteed first stone is not on this axis** and cannot be rolled away. Clamped `0..100` at `DungeonController.PostFirstRoughStoneDropPct`. | **Not a PROD-022 hypothesis - and ⚠ A DELIBERATE DEPARTURE from "the default is today's behaviour", stated not hidden.** The build shipped **15**, as the compiled const `DungeonController.PostFirstRoughStoneDropRate = 0.15f`; the owner ruled `5` on 2026-09-09 (*"5% drop rate in dungeons not included the starter dungeons"*). **A row of `15` restores the previous rate exactly.** Whether a delve still pays for its lantern oil at 5% is the felt question this row answers. |

**⛔ `Warn` and `Fail` are emitted at every verbosity level and cannot be turned off.** CLAUDE.md §12
is binding: instrumentation is permanent, and a failure line that stops being logged turns a logged
failure back into a silent one. Only the success narration is dimmable.

### Independence

Every flag is **independently togglable and independently meaningful**. None implies or requires
another. Where a mitigation needs a value as well as an arm (#4), the value **is** the arm — a
sentinel default of `0` means "today", so there is no second coupled flag. #9 is the same shape from
the other end: its sentinel is `100`, because the quantity it scales is a percentage of what already
happens, and `100%` *is* today.

**#9 is a BALANCE knob, not a PROD-022 mitigation, and that is deliberate.** The owner ruled
(2026-09-02) that balance must move without a rebuild too. It rides this rail because the rail already
exists and works; building a parallel one for balance would be the second bespoke configuration
mechanism this page's last section explains we do not want. The invariant at the top of the page binds
it exactly as it binds the other eight.

---

## Precedence — how these compose with the existing `ff.*` flags

```
LOCAL PlayerPrefs "ff.tun.<key>"    (a human at the device)
    beats  REMOTE database row      (the owner at the console)
        beats  BUILD DEFAULT        (what this build hardcodes = today)
```

`FeatureFlags.Get` already resolves PlayerPrefs-over-default for the `ff.*` family. This system inserts
the **remote** layer between those two and leaves `ff.*` untouched. The prefix is `ff.tun.` and
**not** plain `ff.` on purpose: a tunable key and a `FeatureFlags` name must never be able to collide
in one PlayerPrefs namespace.

---

## Flipping one — worked example (the prime suspect)

```powershell
# See what is set. An absent row means the knob is at the build's default.
tools\command-centre.ps1 -Tunables

# Arm the prime suspect.
tools\command-centre.ps1 -Tunables -Key pi.awaitInitBeforeFirstLoad -Value 1

# ...owner felt-tests in Pi Browser, reports back...

# Put it back. CLEAR, not -Value 0 - see below.
tools\command-centre.ps1 -Tunables -Key pi.awaitInitBeforeFirstLoad -Clear
```

The balance knob works identically. To halve the mage's early drain sustain:

```powershell
tools\command-centre.ps1 -Tunables -Key combat.drainReturnPct -Value 50

# ...owner felt-tests, reports back...

# Back to the shipped 100%. CLEAR, not -Value 100.
tools\command-centre.ps1 -Tunables -Key combat.drainReturnPct -Clear
```

It is **not** a boot-time knob — `HeroAbilities.DrainReturnPct` is resolved at the moment each drain
lands — so it reaches a running client on the ordinary ~40 s path below, mid-session, with no relaunch.

Judge by the **marker on a fresh log** (`Builds\client-tunables.log`), never the exit code:
`TUNABLES_LIST_OK` / `TUNABLES_SET_OK` / `TUNABLES_CLEAR_OK` / `TUNABLES_FAIL`.

### ⭐ `-Clear` is not `-Value 0`

Clearing **removes the override**, so the knob answers whatever the build hardcodes — which for
`pi.requestTimeoutSeconds` is **20**, not 0. It is the one-word way back to today's behaviour, and it
is a separate verb for exactly that reason.

### From the phone

The same two writes exist on `POST /api/admin/ops` as `tunable.set` / `tunable.clear`, behind the same
two secrets (`ADMIN_DASH_KEY` + `ADMIN_OPS_KEY`) every other ops write uses.

**⭐ THE PHONE SURFACE IS NOW THE PRIMARY ONE (WO-1328, 2026-09-02).** *(The line that used to sit here
— "the Command Center console HTML has **not** been extended with buttons for them, the PowerShell
surface above is primary" — was true when written and is now retired. It was also, in the owner's
words, the ticket: "should be in command center so you dont need to be a rocket scientist... i have
been screaming this for months.")*

Open `https://<app>.vercel.app/api/admin/console`, type `ADMIN_DASH_KEY`, and tap **Balance** — it is
in the primary nav, not behind "More tools". Every knob is a card carrying its plain-English name, what
moving it actually does, its **current** value, the value the installed build ships with, and the WORD
`OVERRIDDEN` or `Shipped default`. **Save** writes an override; **Reset** deletes the row. Both verbs
are 112 px tall and both spell out that reset is not zero. The write key is asked for once per tab and
is never stored.

The page is **driven by a JSON manifest**, and that manifest is **not a fifth copy of the knob list**:

| Fact | Owner |
|---|---|
| key + kind + default | **DERIVED** from `RemoteTunables.Registry` by `tools/gen-tunable-manifest.mjs` into `api/_lib/tunable-manifest.generated.json` |
| may this key be written | the `TUNABLE_KEYS` allowlist in `api/_lib/tunables.js` |
| area (Skills / Tiers / Spells / Misc), label, plain English, safe range | `PRESENTATION` in `api/_lib/tunable-manifest.js` |

**Adding a lever later is a data edit, not a UI edit:** add the knob to the registry and the allowlist,
run `node tools/gen-tunable-manifest.mjs`, add one `PRESENTATION` entry, and the card appears on its own.
`test/tunables-manifest.test.js` re-derives the spine from `RemoteTunables.cs` on every run and goes RED
**naming which two sources disagree** — it caught two live drifts within a minute of them landing on the
day it was written.

**⛔ PRICES, ENTITLEMENTS, GRANTS AND PURCHASE AMOUNTS ARE PERMANENTLY OUT OF SCOPE** for that page and
for this rail. They are decided server-side in `api/_lib/purchase-catalog.js`; the game takes real money
on mainnet, so a value a phone could override would be an exploit, not a feature. The boundary is
printed on the page itself and asserted on the *shape* of the manifest, not on its current contents.

The PowerShell surface above still works and is unchanged.

### How long until it reaches a client

About **40 seconds** for a running client: 10 s edge cache + the 30 s client poll.

**Boot-time knobs (#1, #2, and #3 as it affects the first request) are read at frame zero from the
on-device cache**, so they take effect on the **next launch** of a client that has fetched the value at
least once. Since PROD-022's symptom is that the app relaunches every 30–60 s, that is usually the very
next relaunch. See "The cache" below.

---

## Reading a session's configuration out of the trace

Every session prints its whole configuration on one line, at boot and again whenever a payload changes
it. **Quote this line in any felt-test report** — a run whose configuration cannot be reconstructed
afterwards proves nothing.

Default build, nothing set (a `Step` line):

```
[Flow:Tunables] CONFIG (StructureContentWarmer.Boot): generation=1 tableProvenance=default rows=0 | pi.eagerStructureWarm=OFF  pi.awaitInitBeforeFirstLoad=OFF  pi.disableRemoteStructureArt=OFF  assets.maxConcurrentRequests=0  pi.requestTimeoutSeconds=20  assets.maxRequestAttempts=3  visuals.missLogCap=3  trace.assetVerbosity=2  combat.drainReturnPct=100 || EVERY knob is at its shipping default - this session is TODAY'S BEHAVIOUR, unchanged. Nothing was overridden by the database or by PlayerPrefs.
```

With the prime suspect armed (a `Warn` line — an overridden build is not the shipping build and must
not read as ordinary narration):

```
[Flow:Tunables] CONFIG (payload accepted, rows=1 unknown=0): generation=2 tableProvenance=remote rows=1 | pi.eagerStructureWarm=OFF  pi.awaitInitBeforeFirstLoad=ON(OVERRIDDEN, default OFF)  ... || 1 knob(s) are OVERRIDDEN. This session is NOT the shipping default configuration - quote this line in any felt-test report, because it is the only record of what produced the run.
```

Each knob additionally traces its own provenance once per distinct value:

```
[Flow:Tunables] KNOB pi.requestTimeoutSeconds = 20  provenance=default  (shipping default 20, generation=1). No database row and no local override - this is TODAY'S BEHAVIOUR, unchanged.
[Flow:Tunables] KNOB pi.awaitInitBeforeFirstLoad = ON  provenance=remote  (shipping default OFF, generation=2). This is an OVERRIDE of the shipping default.
```

`provenance` is one of `default` | `remote` | `remote-cached` | `local-playerprefs`. A reader never has
to infer whether a value came from the database.

---

## The cache — and why this diverges from `MaintenanceService`

`MaintenanceService` deliberately has **no** cache: a stale kill switch is a safety question, and the
owner ruled that an offline player falls back to "everything is open". **That ruling is about seals and
does not transfer here.**

The knobs that matter most to PROD-022 are read **during boot** (the Pi Addressables policy is decided
in `StructureContentWarmer.Boot`, at `AfterSceneLoad`). A value that only arrived after a network round
trip would be a launch too late, on every launch, forever. So `RemoteTunablesService` mirrors the last
accepted payload into `PlayerPrefs["tunables.cache.v1"]` and reads it back at **`BeforeSceneLoad`** —
which Unity guarantees runs before every `AfterSceneLoad` hook.

Safety properties of that cache, all of them load-bearing:

- It can only ever hold values that **came from** the database.
- A fresh payload **replaces it wholesale**, so it cannot resurrect a knob the owner cleared.
- A **404** (endpoint not deployed) **clears** it — an absent feature holds no knob.
- A corrupt cache is rejected by the same `Guard`-wrapped parse as a live payload, **discarded**, and
  every knob falls to its shipping default.

---

## Where this lives

| Layer | File |
|---|---|
| Registry / defaults / parse (**source of truth for defaults**) | `Assets/_Modules/Core/Ops/RemoteTunables.cs` |
| Transport, poll, cache | `Assets/_Modules/Core/Ops/RemoteTunablesService.cs` |
| Knobs 1–6, 8 consumed | `Assets/_Modules/Core/Addressables/StructureContentWarmer.cs` |
| Knobs 7–8 consumed | `Assets/_Modules/Village/VisualFactory.cs` |
| Knob 9 consumed | `Assets/_Modules/Village/Hero/HeroAbilities.cs` — `DrainReturnPct` / `HealFromDrain` |
| Knobs 15–18 consumed | `Assets/_Modules/Village/Vfx/NightStoreAuraSelector.cs` (decides; owns the clamps) → `Assets/_Modules/Village/Vfx/RealmStoreBeacon.cs` (the ONE spawn owner) |
| Table | `api/schema.sql` — `client_tunables` |
| Server read + validation + writer | `api/_lib/tunables.js` |
| Public GET | `api/client-tunables.js` |
| Phone write actions | `api/_lib/ops.js`, `api/admin/ops.js` (`tunable.set` / `tunable.clear`) |
| Operator CLI | `tools/client-tunables.mjs` |
| Operator surface | `tools/command-centre.ps1 -Tunables` |
| **Phone surface (WO-1328)** | `api/admin/console.js` — the **Balance** tab |
| **Manifest join** | `api/_lib/tunable-manifest.js` (areas + labels + safe ranges) |
| **Manifest spine, GENERATED** | `api/_lib/tunable-manifest.generated.json` via `tools/gen-tunable-manifest.mjs` |
| **Oracle** | `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` — `[tunable-defaults]`, registered in `DataRegression.RunAll` |
| **Oracle (manifest, WO-1328)** | `test/tunables-manifest.test.js` — `node --test`, no Unity, no network |

> ## ⚠ CORRECTED 2026-09-02 — IT IS **SIX** SOURCES NOW, NOT FOUR.
>
> This paragraph said **four** all evening, and WO-1328 landed the Command Center Balance tab in the
> middle of that evening, adding **two more**. A WO-1330 seat following the four-source rule literally
> would have shipped a knob **the owner's console cannot see** — a lever that exists, works, and is
> invisible to the one person who needs it. It only got caught because that seat checked the tree
> instead of trusting this line.
>
> **That is this repo's signature failure wearing yet another face:** the count was written down, the
> world moved, and the written count kept being obeyed. Do not restate the number anywhere else — and
> if you add a seventh source, correct THIS line in the same edit.

**If you change a default or add a knob, change ALL SIX of these in the same commit** — CLAUDE.md §15:

1. `RemoteTunables.Registry` — `Assets/_Modules/Core/Ops/RemoteTunables.cs` (**the source of truth for defaults**)
2. the `TUNABLE_KEYS` allowlist in `api/_lib/tunables.js` (a key absent here is a key the server **refuses every write to**)
3. this document
4. `ExpectedDefaults` in `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs`
5. `api/_lib/tunable-manifest.generated.json` — regenerate with `node tools/gen-tunable-manifest.mjs`
6. the hand-authored half of `api/_lib/tunable-manifest.js` — area, label, plain-English description, safe range (**a knob with no area does not render on the console**)

You will not forget, because two oracles disagree loudly rather than silently: the
`[tunable-defaults]` suite pins 1/2/3/4 against each other, and `test/tunables-manifest.test.js`
re-parses `RemoteTunables.cs` **from disk on every run** and asserts 5 is byte-identical to a fresh
derivation. Each reds naming which two sources disagree. Sources 1 and 5 are machine-derived from the
same file on purpose — only 3 and 6 are written by a human, and those are the two that rot.

### What the oracle pins

The invariant at the top of this page — **no row / unreachable backend ⇒ today's behaviour, byte for
byte** — is the one every offline player depends on, and a break in it is *invisible*: nothing
crashes, the build simply stops behaving the way this page says it does. So it is asserted, not
assumed. `[tunable-defaults]` drives **seven** failure paths (no table · `readOk:false` · malformed
JSON · empty body · corrupt device cache · values the server would refuse · garbage after a good
payload) and re-asserts **all nine knobs on each one**. It also proves the real consumers still
answer `3`, `20` and `100` with no table, that all three clamps hold, that the key domain matches
across the three sources, that the fetch still cannot block boot, and that no `Warn`/`Fail` was ever routed
through the verbosity knob (CLAUDE.md §12). Zero network, zero database.

### Why a new table and not `maintenance_toggles`

Asked and answered. `maintenance_toggles` is PK-`CHECK`-constrained to exactly six area ids; its shape
is boolean + operator prose; and its six-id domain is source-linted three ways (the `MaintenanceArea`
enum, the `AREAS` array, the SQL `CHECK`) by `MaintenanceTogglesRegression`. Putting knobs there would
force that `CHECK` open — defeating the lint that exists to keep the six honest — and would overload
`closed`/`message` as a value field. Different domain, different shape, different failure semantics.

The **pattern** is reused end to end (public unauthenticated GET, 10 s edge cache, fail-to-safe-ground-
state, writes only through the two-key admin endpoint and one operator CLI, one `command-centre` switch,
marker-judged). Only the table is new. There is no second bespoke configuration mechanism.
