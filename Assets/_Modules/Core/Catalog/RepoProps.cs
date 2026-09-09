// =============================================================================
// RepoProps — the BEHAVIOR half of a CatalogEntry (catalog ⊥ repo). Pure data.
// Combat stats are EXTRACTED VERBATIM from DefenseTower (rules-from-code); a
// cosmetic swaps the entry's visualPrefabPath and NEVER these — the structural
// cosmetic-only guarantee. behaviorId is resolved to a component by Village
// (keeps Core free of Village refs).
// =============================================================================
using DeNelle.Core.Combat;

namespace DeNelle.Core.Catalog
{
    /// <summary>
    /// S4 — a Core-friendly, pure-data multi-resource cost (Wood/Food/Iron/Crystals).
    /// Lives in Core so <see cref="RepoProps"/> (which Core owns) can carry it without a
    /// Village reference; the Village boundary (BuildModeController) maps it 1:1 to
    /// <c>EconomyService.ResourceCost</c> and charges it through the persisted ledger.
    /// All four slots default to 0 = "no multi-cost" (fall back to crystals-only buildCost).
    /// JSON deserializes the optional "cost" object's wood/food/iron/crystals straight in.
    /// </summary>
    [System.Serializable]
    public struct ResourceCost
    {
        public int wood;
        public int food;
        public int iron;
        public int crystals;

        /// <summary>True when every slot is zero — no multi-resource cost was authored.</summary>
        public bool IsZero => wood == 0 && food == 0 && iron == 0 && crystals == 0;
    }

    /// <summary>
    /// PROD-014 slice (d) -- the CRYSTALS-FOR-REPAIR exchange rate, in crystals per unit of the
    /// material a repair is short of. Authored on the 'repair_default' catalog row ONLY.
    ///
    /// WHY IT IS A NAMED CARVE-OUT AND NOT A COST. WO-947 separates the baskets: regular
    /// structures are BUILT and UPGRADED with wood + iron, magical ones with crystals. The owner
    /// amended that ruling on 2026-08-24 -- REPAIR, and only repair, may be PAID IN CRYSTALS for
    /// anything. This struct is that amendment's whole surface: it is a conversion rate a player
    /// may opt into at a refusal, never a slot in any structure's authored basket. Nothing here
    /// can appear in repo.cost or repo.upgradeCost, and CostBasketSeparationRegression's
    /// [repair-carve-out] case fails if a second row ever authors one.
    ///
    /// A ZERO RATE MEANS "NOT CONVERTIBLE", NOT "FREE". The owner ruled exactly one number on
    /// 2026-08-26 -- 1.0 crystal per IRON, 60% above the measured 0.625 natural-exchange floor so
    /// a player holding iron still spends iron. No rate was ruled for wood or food, so those stay
    /// 0 and the shortfall in them simply cannot be paid in crystals. Choosing a number for them
    /// would be inventing economy policy, which is the exact reason this slice was blocked for two
    /// days.
    /// JSON deserializes the optional "repairCrystalsPer" object straight in.
    /// </summary>
    [System.Serializable]
    public struct RepairCrystalRate
    {
        public float perWood;
        public float perFood;
        public float perIron;

        /// <summary>True when no rate at all was authored -- repair cannot be paid in crystals.</summary>
        public bool IsZero => perWood <= 0f && perFood <= 0f && perIron <= 0f;
    }

    [System.Serializable]
    public sealed class RepoProps
    {
        /// <summary>What this contributes to the NavMesh once placed.</summary>
        public NavSurfaceKind navSurface = NavSurfaceKind.None;

        /// <summary>
        /// Crystals-only build cost (the legacy affordable rule reads this). Kept as a
        /// FALLBACK: when no multi-resource <see cref="cost"/> is supplied (all four
        /// slots zero), placement charges this many Crystals so older catalog rows and
        /// any back-compat path never regress. When <see cref="cost"/> is non-zero it
        /// takes precedence.
        /// </summary>
        public int buildCost = 0;

        /// <summary>
        /// S4 — the full multi-resource build cost (Wood/Food/Iron/Crystals). Pure-data,
        /// Core-friendly (no Village ref); the Village boundary maps it to
        /// EconomyService.ResourceCost and charges it through the persisted ResourceLedger.
        /// All-zero (the default) means "no multi-cost" → fall back to <see cref="buildCost"/>
        /// Crystals. JSON deserializes the optional "cost" object straight into this.
        /// </summary>
        public ResourceCost cost = new ResourceCost();

        /// <summary>
        /// PROD-014 slice (d) -- crystals per unit of shortfall when a player chooses to pay a
        /// REPAIR in crystals. See <see cref="RepairCrystalRate"/> for why this is a named
        /// carve-out from WO-947 and not a cost slot. Authored on 'repair_default' ONLY; every
        /// other row leaves it zero, which means "this row does not price crystal repair" -- the
        /// rate is a property of the repair economy, not of the structure.
        /// All-zero (the default) = repair cannot be paid in crystals at all.
        /// JSON deserializes the optional "repairCrystalsPer" object straight into this.
        /// </summary>
        public RepairCrystalRate repairCrystalsPer = new RepairCrystalRate();

        /// <summary>
        /// THE ceiling on any placed structure's upgrade level -- the ONE number
        /// BuildModeController.MaxLevelFor clamps to and every regression that mirrors that clamp
        /// reads. Named here (Core.Catalog, beside the field it bounds) so the ceiling cannot be
        /// raised in the controller and left stale in three oracles, which is exactly what a
        /// hardcoded literal 3 in four files produced.
        /// <para>WAS 3, tied to StructureTierVisual's 1..3 accent ladder. RAISED TO 6 by WO-966
        /// (owner ruling 2026-08-15: the storage containers get SIX levels). The visual ladder is
        /// deliberately NOT extended: StructureTierVisual.Apply already clamps 1..3 internally, so
        /// levels 4-6 simply keep the gold T3 accent -- a look that tops out is a cosmetic gap, a
        /// level the player cannot buy is dead data. Raising this ceiling does not make anything
        /// upgradeable on its own: a row still opts in with its own repo.maxLevel.</para>
        /// </summary>
        public const int MaxStructureLevel = 6;

        /// <summary>
        /// S5 — the highest upgrade level this structure can reach (the CoC sink). 1 means
        /// "not upgradeable"; 3 is the canon wood→stone→reinforced / tower L1..L3 ceiling
        /// (matches StructureTierVisual's 1..3 tier clamp); the storage containers
        /// (lumberyard/foundry/silo) author 6 under WO-966. Bounded by
        /// <see cref="MaxStructureLevel"/>. Default 1 so a row that omits it stays single-tier.
        /// </summary>
        public int maxLevel = 1;

        /// <summary>
        /// S5 — per-step upgrade cost, indexed by the level being upgraded TO minus 2:
        /// <c>upgradeCost[0]</c> = the cost to go L1→L2, <c>upgradeCost[1]</c> = L2→L3, …
        /// (so a structure with maxLevel 3 authors two entries). Pure-data, multi-resource.
        /// When NULL / too short for the requested step, the Village boundary falls back to a
        /// data scaler (the build <see cref="cost"/> scaled by the level being left), so a row
        /// can opt into upgrades with just <see cref="maxLevel"/> and no explicit table.
        /// JSON deserializes the optional "upgradeCost" array straight into this.
        /// </summary>
        public ResourceCost[] upgradeCost = null;

        /// <summary>
        /// S5 visual — OPTIONAL per-level model swap, indexed the SAME way as
        /// <see cref="upgradeCost"/>: <c>upgradeVisualPath[0]</c> = the Resources-relative
        /// prefab shown at L2, <c>upgradeVisualPath[1]</c> = at L3, … The base
        /// <see cref="CatalogEntry.visualPrefabPath"/> is always L1. NULL / empty / too-short
        /// for the requested level means "keep the previous level's model" (StructureTierVisual
        /// still steps scale + the bronze/silver/gold accent), so a row opts into a distinct
        /// upgraded silhouette (archer tower → round keep) with just this array and the rest
        /// of the progression stays the graceful data/tint fallback. JSON deserializes the
        /// optional "upgradeVisualPath" string array straight into this.
        /// </summary>
        public string[] upgradeVisualPath = null;

        /// <summary>
        /// Optional per-tier local Euler correction, indexed like
        /// <see cref="upgradeVisualPath"/>. Each row is [x,y,z] degrees. Empty or
        /// missing rows retain the normal tier rule (prefab identity/native child pose).
        /// This is deliberately per tier: applying a base-model correction globally
        /// tips upgrade art from a different source family.
        /// </summary>
        public float[][] upgradeOrientationEuler = null;

        /// <summary>
        /// S5 visual (WO-719 upgrade-tier albedo) - OPTIONAL per-level FORCED texture, indexed
        /// the SAME way as <see cref="upgradeVisualPath"/>: <c>upgradeTexturePath[0]</c> = the
        /// Resources-relative albedo forced onto the L2 tier model, <c>upgradeTexturePath[1]</c> =
        /// at L3. The base <see cref="CatalogEntry.visualTexturePath"/> is always L1. This is the
        /// per-tier twin of that field: when a <see cref="upgradeVisualPath"/> tier model is a Tripo
        /// FBX whose only Color map lives buried in its <c>.fbm</c> folder (does NOT survive a player
        /// build -> renders WHITE, exactly like the L1 spire before its fix), the reskin routes this
        /// flat Resources albedo through the fresh <c>TripoMaterialFixer.SetForcedTexture</c> so the
        /// upgraded model keeps its colour in the build. NULL / empty / too-short for the requested
        /// level -> falls back to the base <see cref="CatalogEntry.visualTexturePath"/> (which itself
        /// may be null = no forced texture). JSON deserializes "upgradeTexturePath" straight in.
        /// </summary>
        public string[] upgradeTexturePath = null;

        /// <summary>
        /// WO-948 — for a <c>CatalogType.Wall</c> row: the walls.json ladder level this row's
        /// LEVEL-1 placement corresponds to (0 = Wooden Fence, 1 = Stone, 2 = Steel, 3 = Spiked
        /// Steel). A placed wall at upgrade level L sits at walls.json level
        /// <c>wallTierBase + (L - 1)</c> — this is what lets the heart-mitigation derive
        /// (Village <c>WallDefense</c>) read a legacy placed <c>wall_stone</c> (base 1) and a
        /// wood wall upgraded to stone (base 0, L2) as the SAME ladder rung without an id list.
        /// Default 0 (a wall row that omits it is the wood base tier); ignored by every
        /// non-Wall row. JSON deserializes the optional "wallTierBase" straight in.
        /// </summary>
        public int wallTierBase = 0;

        /// <summary>Village resolves this string -> the actual behaviour component (Core stays pure).</summary>
        public string behaviorId = null;

        /// <summary>
        /// COLLECTOR IDENTITY (owner 2026-07-10 generic build-mode) — for a
        /// <c>behaviorId:"ResourceCollector"</c> row, the ResourceBuildingProgression id
        /// (<c>farm</c> / <c>lumbermill</c> / <c>forge</c>) the placed collector accrues for.
        /// Pure-data (no Village ref); StructureFactory.AttachBehavior passes it to
        /// <c>ResourceCollector.Configure</c>. Null/empty → falls back to the entry id.
        /// Ignored by every other behaviorId. JSON deserializes "collectorBuildingId" straight in.
        /// </summary>
        public string collectorBuildingId = null;

        /// <summary>
        /// WO-1163 — OTHER structure ids whose existence ALSO opens this faucet's WO-834
        /// harvest gate. Data-authored, so which building pays a resource is a CATALOG fact.
        ///
        /// <para>⛔ WHY THIS EXISTS. `CatalogIdsForBuilding` deliberately refuses the BARE id
        /// (see its own note: accepting `forge` would let the Forge STOREFRONT open the forge
        /// COLLECTOR's gate). That rule is right in general and WRONG for iron, because the
        /// owner ruled 2026-08-23 that iron IS the Armorer's resource — "It should be the
        /// armorer, the food is the farm, and the wood is the lumbermill, and there is none
        /// for crystals, since that is only available at lvl 6 echo". This field is how that
        /// ruling is expressed as DATA rather than as a special case in the gate.</para>
        ///
        /// <para>⛔ AND IT IS WHY THE PLAYER WAS STUCK. The picker printed "Iron - NEEDS:
        /// Forge" while `forge` already sat in her ever-built ledger — an instruction that
        /// could not be satisfied by obeying it. With the pairing authored here the cue names
        /// the building that actually opens the gate, so following it WORKS.</para>
        ///
        /// Null/empty = unchanged behaviour (collector row only). Never put a bare storefront
        /// id here casually: each entry is a deliberate ruling that owning that building pays
        /// this resource.
        /// </summary>
        public string[] satisfiedByStructureIds = null;

        /// <summary>
        /// Flat GOLD cost to restore this structure after it has been DESTROYED, dependent
        /// on nothing else. 0/absent = no gold restore path (the WO-753 default applies).
        ///
        /// <para>⛔ A NAMED EXCEPTION TO WO-753, NOT A CONTRADICTION OF IT. WO-753 ruled that
        /// destroyed items never "rebuild" — you build fresh at full cost. That stands for
        /// everything except a TIER-ONE PRODUCER, by owner ruling 2026-08-23. Recorded here so
        /// the next seat reads it as a deliberate carve-out instead of "fixing" it back.</para>
        ///
        /// <para>⛔ WHY THE CARVE-OUT EXISTS, in the numbers that forced it: every tier-one
        /// producer is priced in the resource it PRODUCES. The armorer costs 280 IRON and is the
        /// iron producer; the lumber mill costs 160 WOOD and is the wood producer; the farm costs
        /// 240 wood. So losing the building loses the resource, and the resource is what buys the
        /// building back — the economy closes on itself with no way out. That is not a balance
        /// question, it is a soft-lock.</para>
        ///
        /// <para>Gold is the escape because it is UNCAPPED by design
        /// (<c>TownBankCapacity.UncappableResources</c>) and drops from kills, so it is always
        /// reachable WITHOUT owning another building. 150 sizes to roughly one wave of clearing
        /// at the live curve (kill rewards 3-120, median 18).</para>
        ///
        /// <para>⚠ FIRST placement is a different path and is already FREE — <c>freeBuildsUsed</c>
        /// (save v32) grants one free placement per catalog id, burns on commit, and never
        /// refunds. This field covers RESTORE only.</para>
        /// </summary>
        public int restoreGoldCost = 0;

        /// <summary>
        /// Phase 2 (owner): true = at most ONE of these may exist in the village (pet-house,
        /// forge, mill, arcane-tower, Heart). Pure-data flag only for now — the enforce /
        /// auto-find-existing wiring is a follow-up; this just carries the intent so build /
        /// designer mode and the placement validator can consult it later. Default false
        /// (most structures — towers, walls, decorations — are freely repeatable).
        /// </summary>
        public bool singleton = false;

        /// <summary>
        /// StructureSingleton v2 (owner only-ever-one ruling) - legacy baked scene-root
        /// GameObject names that REPRESENT this catalog row (singleton twin standdown /
        /// resurface + IsBuilt). A row with repo.singleton=true plus this list is FULLY
        /// enforced with zero code: an active baked twin counts as "built", a placed /
        /// recorded instance stands the twins down, and a post-sell empty state resurfaces
        /// them. Null/empty = none. JSON deserializes "bakedTwins" straight in.
        /// </summary>
        public string[] bakedTwins = null;

        /// <summary>
        /// WO-818 (owner mapping table 2026-08-01) - the KayKit NPC body SLUG for this
        /// structure's speaker (drillmaster / vendor). Resolved by the Village NPC
        /// injectors as <c>Resources.Load("NPCs/KayKit/" + npcModel)</c> FIRST, falling
        /// back to the legacy People-pack prefab chain, then the capsule placeholder.
        /// Pure data (no Village ref), modeled on <see cref="bakedTwins"/>. Null/empty
        /// (the default) = no KayKit body authored -> the People chain is used as before.
        /// OWNER-ONLY creative pick: a swap is a one-word JSON retag, never a code pick.
        /// JSON deserializes "npcModel" straight in.
        /// </summary>
        public string npcModel = null;

        /// <summary>
        /// WO-707 (owner taxonomy 2026-07-13) — STORAGE CONTAINER capacity. The three
        /// dedicated container buildings (Lumberyard wood / Foundry iron / Silo grain)
        /// hold the village's stock; trade buildings are pure vendor/upgrade shops and
        /// carry NO storage field. 0 (default) = not a container.
        /// <para>WIRED 2026-08-04 (WO-857 / WO-901 Phase F) — this is no longer data-only. The ONE
        /// reader is <c>DeNelle.Core.Economy.TownBankCapacity</c>: the town bank ceiling for a
        /// resource is <c>baseCap + sum(storageCapacity of every BUILT container of that resource,
        /// scaled by its placed level)</c>, and <c>EconomyService.Grant</c> clamps every income
        /// source against it. Nothing else may compute capacity from this field —
        /// TownBankCapRegression case [one-reader] fails the build if it does. The per-container
        /// visible fill is DERIVED (never stored) via <c>TownBankCapacity.Apportion</c>.</para>
        /// <para>Still open: the WO-672 damage-to-stores / raid-steal loop has no consumer.</para>
        /// JSON deserializes "storageCapacity" straight in.
        /// </summary>
        public int storageCapacity = 0;

        /// <summary>
        /// WO-707 — which resource this container stores ("wood" / "iron" / "food").
        /// Enum-by-name string kept loose on purpose (pure data, no Village ref),
        /// matching the projectileStyle convention. Null (default) = none.
        /// JSON deserializes "storageResource" straight in.
        /// </summary>
        public string storageResource = null;

        /// <summary>
        /// WO-707 targeting ruling (owner, same session): CONTAINERS ONLY are enemy
        /// raid targets — trade/shop buildings never are (a raid can never soft-lock a
        /// vendor/talk-route). A row is a container iff it authors a positive
        /// <see cref="storageCapacity"/>.
        /// <para>WIRED 2026-08-04 for CAPACITY only (WO-857 / WO-901 Phase F):
        /// <c>DeNelle.Core.Economy.TownBankCapacity</c> reads this to decide which BUILT
        /// structures raise the town bank cap.</para>
        /// <para>STILL OPEN — TARGETING: wire this seam into the ff.enemystructureaware sweep
        /// so shops are excluded and the container set is the loot-target set.
        /// ⚠ NARROWED 2026-09-06 (WO-1439): this used to read "Enemy.SweepForNearestStructure
        /// currently scores ANY live IDamageableStructure via ISiegeLootTarget". It no longer
        /// scores ANY — the sweep now also rejects SAME-FACTION targets through
        /// CombatFactionRules.MayAttack. What is still open is the SHOP-vs-CONTAINER
        /// distinction, a different axis from friend-or-foe and untouched by that change.
        /// STILL OPEN — RAID STEAL: nothing debits the town wallet on an enemy action today
        /// (a siege break only voids a collector's un-banked pending). When that lands it must move
        /// the ONE authoritative total, never a per-container balance (WO-842); the what-if seam
        /// for it already exists as <c>TownBankCapacity.Preview</c>.</para>
        /// </summary>
        public bool IsStorageContainer => storageCapacity > 0;

        /// <summary>
        /// COLLECTOR RESERVE capacity - the number of units a resource COLLECTOR (behaviorId
        /// "ResourceCollector") holds in its pending buffer before it reads FULL, AT LEVEL 1
        /// WITH ONE ECHO.
        /// <para>
        /// WO-859 (2026-08-04): this field is now authored in HOURS, not units.
        /// <c>ResourceCollector.ComputeCapacity</c> reads it when &gt;0 and multiplies it by
        /// <c>ThroughputScale</c> - how much the collector produces now versus at level 1 with one
        /// echo (level yield + interval + the echo GlobalHarvestMultiplier; the harvestRate talent
        /// is excluded, matching EchoService.SiloCapacity). So HOURS-TO-FULL IS CONSTANT across
        /// level and echo count, and the number to think in when tuning is
        /// <c>capacity / yieldPerHour(L1)</c>. Current authoring = 8 HOURS: farm 7500 (936/h),
        /// lumbermill 5760 (720/h), forge 3456 (432/h) - a twice-a-day check-in rhythm, sitting
        /// just above the Echo silo's 4h so collectors read as the primary faucet.
        /// </para>
        /// <para>
        /// STALE COMMENT REMOVED (WO-859 sec.6): this doc used to read "a farm at ~150/min fills 1000
        /// in ~7 min" and the old scale was a flat +50% per level. Both were wrong. Post-WO-855 a
        /// farm makes 936/HOUR, not ~150/min - the authoring intent was off by ~9.6x - and because
        /// capacity grew x3 from L1-&gt;L5 while throughput grew x5.6, UPGRADING A COLLECTOR MADE IT
        /// FILL SOONER. Do not reintroduce a flat level multiplier here or in C#.
        /// </para>
        /// The STEWARD collectorCap talent still multiplies on top. 0 (default) = not authored ->
        /// the collector falls back to its legacy ~2h-of-production formula. DESIGNER-TUNABLE DATA
        /// - never hardcode the number in C#. Distinct from <see cref="storageCapacity"/> (that
        /// flags a raidable stock CONTAINER; this sizes a collector's pending buffer). JSON
        /// deserializes "capacity" straight in.
        /// </summary>
        public int capacity = 0;

        /// <summary>Placement conditions, evaluated at the free cursor.</summary>
        public PlacementRules placement = new PlacementRules();

        /// <summary>
        /// WO-764 — the per-item Y-height MULTIPLIER against the ONE global base ceiling
        /// (StructureFactory.YHeightVariable = 4 m). The skinned model is fit-to-HEIGHT so its
        /// world-bounds Y == <c>YHeightVariable * heightMul</c>. DEFAULT 1.0 = every building
        /// normalizes to the base ceiling (uniform, script-built town — the owner-locked model).
        /// Change the base in ONE place and the whole town re-scales together.
        /// <para>THE CADENCE (owner ruling 2026-08-05, "I want all of the other structures to stay
        /// within that cadence... relatively the same size... all scaled to the same point"). ONE
        /// FAMILY, NOT ONE NUMBER - the full rationale per group lives in the catalog's top-level
        /// <c>_heightCadence</c> key, which is the authority; this is the summary:
        /// <list type="bullet">
        /// <item>1.00 (4.0 m) - BUILDING BASE. Houses, production, vendors, storage, collectors,
        /// civic. The width reference: House_Medieval_Medium fits to 5.562 m across.</item>
        /// <item>1.20 (4.8 m) - TOWER, and the ANCHOR the rest of the family is expressed against.
        /// Owner-ruled and MEASURED: 2.778 m across = 49.9% of a house diameter, i.e. the ruling's
        /// "half as wide as the diameter of any of the houses". The WHOLE tower class sits here as
        /// of 2026-08-05 (tower_wall_wizard and tower_arcane_spire came off the old 1.25).</item>
        /// <item>0.75 (3.0 m) - SIEGE ENGINE (catapult, wall-walk sky ballista). Machines, not
        /// architecture; deliberately under the house line.</item>
        /// <item>1.25 (5.0 m) - LANDMARK, exactly one row (<c>arcane-tower</c>, the Cathedral of
        /// Magic). The town's single apex.</item>
        /// <item>0.35 (1.4 m) - DECORATION (<c>deco_torch</c>). Unauthored it inherited the 1.0
        /// building base, i.e. a wall torch as tall as a house.</item>
        /// </list></para>
        /// <para>NOT A CADENCE VALUE - <c>collector_farm</c> = 1.4. This multiplier fits BOUNDS, so
        /// a spindly silhouette reads SMALLER than a boxy one at the same number; the farm's windmill
        /// blades inflate its Y bounds and 1.4 is the owner felt-report compensation that puts its
        /// BODY back on the 4 m line. Never "normalize" it to 1.0. The same caveat applies to any
        /// cross-row comparison: equal heightMul does NOT mean equal apparent size.</para>
        /// <para>HEIGHT AND FOOTPRINT ARE ONE NUMBER, by design. The fit is a UNIFORM scale, so this
        /// multiplier moves the base footprint by the same factor, and
        /// StructureFactory.MeasureUprightFootprintMetres measures the real estate off the
        /// height-fitted model, never off the authored placement.footprint (that is only the
        /// prefab-missing fallback). There is no width dial and none is needed. Corollary for
        /// SAVE COMPAT: the grid claim is ceil(measured / 3 m), so RAISING a multiplier can grow a
        /// claim and make an existing saved town reload with OVERLAPPING claims - always state the
        /// before/after cell claim when you change one. (Lowering only shrinks a claim, which is
        /// overlap-safe, but for WALLS a narrower segment opens pathable GAPS in already-placed
        /// runs, which is why wall_wood/wall_stone/gate_stone were deliberately left at 1.0.)</para>
        /// JSON deserializes "heightMul" straight in. SUPERSEDES the
        /// deprecated absolute <see cref="visualHeight"/> below.
        /// </summary>
        public float heightMul = 1.0f;

        /// <summary>
        /// FOOTPRINT CEILING in metres for the height-fitted model — the widest HORIZONTAL
        /// world-bounds extent (max of X and Z) this row is allowed to occupy. &gt;0 arms it;
        /// <b>DEFAULT 0 = DISARMED = exactly today's behaviour</b>, so a row that does not author
        /// it is byte-identical to before this field existed. It only ever scales DOWN, never up,
        /// and it scales UNIFORMLY — the model keeps its proportions, it just stops eating the
        /// plaza. Applied AFTER the height fit (VisualFactory.Fit), so it is a CAP on that fit and
        /// not a second competing fit.
        /// <para>WHY A SECOND AXIS EXISTS AT ALL — and why <see cref="heightMul"/> could not do
        /// this job. Fit-to-height is a SINGLE-AXIS promise over a UNIFORM scale: the model is
        /// scaled by <c>target / boundsY</c>, so whatever the footprint happens to be, it comes
        /// along at the same factor. That is fine while every model's fitted pose is roughly
        /// building-shaped. It breaks the moment a model's fitted pose is FLAT, because a small
        /// Y divisor makes the scale explode and the footprint explodes with it. MEASURED, on
        /// device, 2026-08-20 (logs/device/2026-08-20-portal.log, [Flow:Xform] + [Flow:VisualFactory]
        /// "skinned"): <c>Structures/farm</c> is natively 0.977 x 1.000 x 0.391 m, and the row's
        /// authored orientation euler (-90,0,0) — applied PRE-fit since the GROK_BRIEF 2026-08-19
        /// change — stands the 0.391 axis up. Fit therefore divided a 5.6 m target by 0.391 and
        /// produced <c>scale=(14.34)</c> and a fitted footprint of <b>14.00 x 14.34 m</b>, against
        /// a family that measures 2.8-5.8 m across. The owner's report was "farm seems to be much
        /// larger than anything else"; the number behind that felt-report is 3.5x.</para>
        /// <para>NO OTHER ROW CAN BE FIXED BY DIALING HEIGHT INSTEAD. Both directions on
        /// <see cref="heightMul"/> are wrong here: lowering it shrinks the BUILDING as well as the
        /// footprint (that is literally the "shrunk farm" the owner already rejected, commit
        /// 31b41d19), and raising it makes the footprint worse. Height and footprint were ONE
        /// number by design; this is the deliberate, opt-in second number for the case where that
        /// design has no answer.</para>
        /// <para>SAVE COMPAT, same rule as <see cref="heightMul"/>: BuildModeController claims
        /// <c>ceil(measured / 3 m)</c> cells from StructureFactory.MeasureUprightFootprintXZ, which
        /// measures the fitted model, so arming this key SHRINKS a claim. Shrinking is overlap-safe
        /// (a saved town reloads with a smaller claim, never an overlapping one) — collector_farm
        /// goes 5x5 cells -&gt; 2x2. RAISING an already-armed cap can grow a claim; state the
        /// before/after cell claim when you change one, exactly as for heightMul. Never arm this on
        /// a WALL row: a narrower segment opens pathable GAPS in already-placed runs.</para>
        /// <para>Read by <c>StructureFactory.OptsFor</c> into <c>SkinOptions.MaxFootprint</c>, so it
        /// reaches Create, ReskinForLevel, the placement GHOST and MeasureUprightFootprintXZ through
        /// the one shared options builder — the ghost cannot disagree with the placed structure.
        /// JSON deserializes "maxFootprint" straight in.</para>
        /// </summary>
        public float maxFootprint = 0f;

        /// <summary>
        /// DEPRECATED (WO-764) — the legacy ABSOLUTE visual height (metres) the model was fit to.
        /// Superseded by <see cref="heightMul"/> (base × multiplier) and NO LONGER READ by
        /// StructureFactory.EffectiveVisualHeight. Retained only so any older serialized JSON that
        /// still carries a "visualHeight" key deserializes without error. Do NOT author new rows
        /// against it — use <see cref="heightMul"/>.
        /// </summary>
        public float visualHeight = 0f;

        /// <summary>
        /// WO-928 (2026-08-08, owner felt-test "the L3 Archer Tower is lying on its side") — KEEP the
        /// skinned model's OWN authored root rotation instead of flattening it to identity.
        /// <para>THE DEFAULT (false) IS THE KNOWN-GOOD BEHAVIOUR AND MUST STAY THE DEFAULT.
        /// VisualFactory.Skin resets an instantiated model's root to identity (DEF-232). That is
        /// RIGHT for almost every structure here: most Tripo building FBXs instantiate at euler
        /// (270,0,0) and the reset is exactly what CANCELS that. A handful of assets are the
        /// opposite case — their native 270 IS the upright correction — and for those the reset both
        /// lays the model down AND makes VisualFactory.Fit measure the SHORT axis to reach the height
        /// target, so it ships sideways *and* oversized (one defect, not two).</para>
        /// <para>ELIGIBILITY, read off the data rather than guessed: a row that authors a NON-ZERO
        /// manual <see cref="CatalogEntry.orientation"/> is PERMANENTLY INELIGIBLE. Thirteen rows
        /// carry a manual (-90,0,0) which StructureFactory.Create applies on top of the identity-reset
        /// root; preserve the native 270 as well and the two COMPOSE to 180 — upside down. Only a row
        /// whose correction lives in the ASSET, with nothing to apply from the row, may opt in.</para>
        /// <para>SCOPE IS THE ROW, DELIBERATELY. The same flag was set for the whole structure CLASS
        /// (on SkinOptions.Structure) earlier the same day and laid the entire town down; see the ⛔
        /// block there for the captured trace. The single reader is StructureFactory.OptsFor.</para>
        /// <para>VERIFYING A CHANGE HERE REQUIRES A RETURN-TO-TOWN PASS, NOT A FIRST LOAD: the first
        /// town load seats buildings from the bake/injector path, and only re-entry (exit to a dungeon
        /// and come back) rebuilds them through BaseLayoutLoader → StructureFactory.Create →
        /// VisualFactory.Skin, the ONLY route that reads this field. That is why the class-wide flip
        /// shipped unnoticed.</para>
        /// JSON deserializes "preservePrefabRotation" straight in.
        /// </summary>
        public bool preservePrefabRotation = false;

        // --- Combat stats (Tower defs) — copied straight off DefenseTower's public fields ---
        public float         range     = 0f;
        public float         damage    = 0f;
        public float         fireRate  = 0f;     // shots per second
        public bool          canHitAir = false;  // ground = false · wall-walk = true
        // ANTI-AIR SPECIALIST (owner 2026-07-08 — the Ballista counters the flying dragon):
        // when true the DefenseTower behaviour acquires ONLY flying targets (ICombatLayered.Layer
        // == Flying) and ignores all ground traffic. Implies canHitAir. Default false = every
        // existing tower keeps its ground-or-mixed behaviour. Read by StructureFactory → DefenseTower.AirOnly.
        public bool          airOnly   = false;
        public DamageElement element   = DamageElement.None;

        /// <summary>
        /// TOWER IDENTITY (owner 2026-07-08 "ballista shoots arrows not round pellet") —
        /// OPTIONAL projectile VISUAL style for tower behaviours. Enum-by-name string kept
        /// loose on purpose (pure data, no Village ref): "pellet" (legacy sphere, the
        /// default when null/empty/unknown), "bolt" (elongated shaft + tip, oriented along
        /// velocity — ballista/archer), "spell" (glowing orb + cast/impact VFX — arcane).
        /// Visual ONLY: damage/targeting/travel logic never read it. JSON deserializes the
        /// optional "projectileStyle" string straight in.
        /// </summary>
        public string projectileStyle = null;

        // --- AoE / debuff stats (WO-113 ArcaneTower) — OPTIONAL, additive ---
        // Only the ArcaneTower behaviour reads these; every other behaviorId ignores
        // them. All default 0 so existing rows are unaffected and the component falls
        // back to its own serialized defaults when a row omits them. JSON deserializes
        // "aoeRadius" / "slowSeconds" / "splashFraction" straight in.
        public float aoeRadius      = 0f;   // splash radius (m) around the impact; 0 = use component default
        public float slowSeconds    = 0f;   // Slow debuff duration (s) on every blast victim; 0 = no slow
        public float splashFraction = 0f;   // 0-1 fraction of damage to non-primary victims; 0 = use default
    }
}
