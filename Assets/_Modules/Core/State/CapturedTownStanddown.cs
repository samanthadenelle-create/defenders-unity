using DeNelle.Core.Catalog;

namespace DeNelle.Core.State
{
    /// <summary>
    /// What KIND of captured body a census entry is. ⛔ THIS ENUM EXISTS BECAUSE THE CATALOG ID
    /// CANNOT ANSWER THE QUESTION. Read 2026-09-18 out of
    /// <c>Assets/Resources/OwnedTown/IronBastionTemplate.json</c>: the Heart
    /// (<c>RaidSpire</c> / <c>iron_bastion.spire.0</c>) and all ten Watchtowers
    /// (<c>iron_bastion.tower.0..9</c>) carry the SAME <c>placement.itemId</c>,
    /// <c>tower_arcane_spire</c>. A filter keyed on the catalog id — or on
    /// <c>CatalogEntry.repo.behaviorId</c> — therefore cannot tell the town's Heart from a defense
    /// tower, and would raze the Heart along with the garrison. The only two things that DO know are
    /// the live component type at census time (<c>RaidCaptureCensus</c>) and the manifest's
    /// <c>movableTower</c> flag (which is false for BOTH the walls and the Heart, so it cannot carry
    /// the distinction either). The kind is therefore recorded explicitly at the one place that can
    /// see it, and every rule below is a pure function of the kind.
    /// </summary>
    public enum CapturedStructureKind
    {
        /// <summary>A <c>WallSegment</c> — the fitted perimeter. Defensive.</summary>
        Wall = 0,
        /// <summary>A <c>DefenseTower</c> — the garrison watchtowers. Defensive.</summary>
        DefenseTower = 1,
        /// <summary>The <c>RaidSpire</c> — the Heart of the taken town. NOT defensive.</summary>
        Heart = 2
    }

    /// <summary>
    /// WO-1872 — THE CAPTURE STANDDOWN. Owner, verbatim (2026-09-18): <i>"When the playere converts
    /// to a town We wan them to create their own town so we shuold not give them two fortified
    /// sections of walls with defense structures"</i>, refined at 15:40 to <i>"they arrive that way
    /// cause you repair the current camp"</i> and <i>"I think we should load a destroyed camp and
    /// then clear the rubble and give them resources to make player designed layouts"</i>.
    ///
    /// <para>⛔ WHY THE TOWN ARRIVED FORTIFIED, proved at source 2026-09-18, not inferred:
    /// <c>RaidCaptureCensus</c> freezes each body's VICTORY-TIME HP fraction
    /// (<c>RaidCaptureCensus.cs:47-49</c>, applied at <c>:71</c>), and
    /// <c>OwnedTownSnapshotImporter.cs:97-102</c> replays that fraction onto the baked twin in
    /// <c>OwnedTown_IronBastion.unity</c> via <c>RestoreOwnedTownCondition</c>. A three-star clear
    /// does NOT require breaking the whole perimeter — razing the spire alone wins
    /// (<c>RaidVictoryController.cs:271</c>) — so every wall and tower the player never attacked
    /// converted at condition <c>1</c> and arrived STANDING. That is the owner's "you repair the
    /// current camp": nothing repaired anything, the untouched sections simply carried their full
    /// health across. The fix is here, at the census → template mapping, and it needs no scene
    /// edit: the importer's existing <c>condition == 0</c> branch ALREADY razes each body
    /// (<c>WallSegment.cs:612</c> Collapse, <c>DefenseTower.cs:325-330</c> broken + Destructible,
    /// <c>RaidSpire.cs:293</c> Raze), which is the WO-753 rubble seam this ticket reuses.</para>
    ///
    /// <para>Pure and Unity-free on purpose so the regression can feed it the three kinds directly,
    /// with no scene, no catalog and no manifest load.</para>
    /// </summary>
    public static class CapturedTownStanddown
    {
        /// <summary>A razed body. The importer's <c>condition == 0</c> branch turns this into rubble.</summary>
        public const float RazedCondition = 0f;

        /// <summary>
        /// The Heart converts STANDING, whatever the raid did to it. Owner ruling point 1: "The Heart
        /// and non-defensive dressing still convert." ⚠ This is a FORCE, not a pass-through, and the
        /// reason is measured: razing the spire is itself a win condition
        /// (<c>RaidVictoryController.cs:271</c>), so on the most common three-star route the Heart's
        /// measured condition is exactly <c>0</c>. Passing that through would convert a town with
        /// NOTHING standing in it — no Heart to read the place by, and (before the ruin carve-out in
        /// <c>OwnedBaseProgression.TryInspectPristineTown</c>) no repairable structure either.
        /// </summary>
        public const float HeartCondition = 1f;

        /// <summary>
        /// The salvage share, as an INT PERCENT, used only when the tunable rail cannot answer.
        /// The live value is <c>RemoteTunables.KeyTownCaptureSalvagePct</c> (owner ruling point 3:
        /// "a tunable fraction of that structure's build cost", default 0.5). Expressed as a percent
        /// because <c>RemoteTunables</c> carries Int and Bool knobs only — there is no Float accessor.
        /// </summary>
        public const int SalvagePctFallback = 50;

        /// <summary>Walls and defense towers are defensive; the Heart is not.</summary>
        public static bool IsDefensive(CapturedStructureKind kind) =>
            kind == CapturedStructureKind.Wall || kind == CapturedStructureKind.DefenseTower;

        /// <summary>
        /// THE FILTER. The condition a captured body converts at: defensive bodies arrive RAZED
        /// wherever they fell (the owner's "load a destroyed camp"), the Heart arrives standing.
        /// The measured fraction is accepted only to be reported by the caller's trace — no kind
        /// currently passes it through, and that is deliberate: a rule that sometimes forwards the
        /// measurement is a rule nobody can state.
        /// </summary>
        public static float ConditionOnCapture(CapturedStructureKind kind, float measuredCondition01)
        {
            if (IsDefensive(kind)) return RazedCondition;
            return HeartCondition;
        }

        /// <summary>
        /// Owner ruling point 3 — what clearing one ruin pays. A floor of the structure's build cost
        /// per resource, so the salvage can never round UP into more than the thing cost. Negative
        /// authored costs and an out-of-range percent both degrade to "pays nothing", never to a
        /// credit: a console typo must not become a resource printer.
        /// </summary>
        public static ResourceCost Salvage(ResourceCost buildCost, int salvagePct)
        {
            int pct = salvagePct < 0 ? 0 : salvagePct > 100 ? 100 : salvagePct;
            return new ResourceCost {
                wood = Share(buildCost.wood, pct),
                iron = Share(buildCost.iron, pct),
                stone = Share(buildCost.stone, pct),
                crystals = Share(buildCost.crystals, pct)
            };
        }

        /// <summary>floor(amount * pct / 100) with a long intermediate, so a large authored cost
        /// cannot overflow into a negative credit.</summary>
        private static int Share(int amount, int pct)
        {
            if (amount <= 0 || pct <= 0) return 0;
            long share = (long)amount * pct / 100L;
            return share > int.MaxValue ? int.MaxValue : (int)share;
        }

        /// <summary>
        /// True when this record is a structure a REPAIR can still bring back: standing (condition
        /// above zero) and below full. ⛔ THE `> 0f` HALF IS WHAT BREAKS A SOFT-LOCK, not a tidy-up.
        /// After the standdown the captured town holds ~168 razed bodies, all of them "below 1", and
        /// the repair lesson gate (<c>OwnedBaseProgression.TryInspectPristineTown</c>) plus the town
        /// panel's <c>pristine</c> test both used "below 1" as their definition of damaged. The player
        /// would have been told to REPAIR the rubble — the exact thing the owner's ruling removes —
        /// and, because <c>OwnedBaseConstruction.CanEdit</c> requires the repair milestone before any
        /// construction edit, she could not have cleared a single ruin until she had repaired one.
        /// Rubble is not damage; it is WO-753's "destroyed is lost, rebuild it".
        ///
        /// <para>⚠ It deliberately does NOT change what <c>OwnedBaseProgression.TryRepair</c> accepts.
        /// Refusing a zero-condition repair there would be the tidier rule and it is NOT this
        /// ticket's: <c>Assets/Editor/Regression/OwnedTownRepairPaymentProof.cs:40</c> repairs a
        /// fixture named "ruin-1" at condition 0 on purpose, so that refusal is a separate ruling with
        /// its own oracle.</para>
        /// </summary>
        public static bool IsRepairableDamage(OwnedBaseStructure record) =>
            record != null && !record.retired && record.condition01 > 0f && record.condition01 < 1f;

        /// <summary>
        /// True when this saved record is CLEARABLE RUBBLE rather than a standing structure: a
        /// captured (inherited) body at zero condition that has not already been cleared. The verbs
        /// are kept disjoint on purpose — rubble is CLEARED (salvage), a standing structure is SOLD
        /// (refund) — because once a record is retired, <c>OwnedBaseProgression.ValidateStructures</c>
        /// forces its condition to 0 and nothing downstream could tell the two apart after the fact.
        /// </summary>
        public static bool IsClearableRubble(OwnedBaseStructure record) =>
            record != null && !record.retired && !record.constructionPending &&
            record.inheritedPose != null && record.condition01 <= 0f;
    }
}
