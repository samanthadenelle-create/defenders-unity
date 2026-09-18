using System;
using System.Collections.Generic;
using DeNelle.Core.Catalog;
using DeNelle.Core.State;
using Newtonsoft.Json;
using UnityEngine;

namespace DeNelle.Village.World.Camps
{
    /// <summary>Shared structural contract. Combat roster/loadout validation is a separate match prerequisite.</summary>
    public sealed class OwnedTownLayoutSnapshot : IArenaBuildValidationAuthority
    {
        public const string Ruleset = "owned-town-layout-v3";
        public const string ConstructionRuleset = "owned-town-layout-v2";
        public const string LegacyRuleset = "owned-town-layout-v1";
        private readonly OwnedTownTemplateManifest _manifest;
        public OwnedTownLayoutSnapshot(OwnedTownTemplateManifest manifest) { _manifest = manifest; }

        public static bool TryCreate(OwnedBaseState property, HeroClassOpt hero,
            out ArenaBuildSnapshot snapshot, out string reason)
        {
            snapshot = null;
            if (!OwnedBaseProgression.Validate(property, out reason)) return false;
            var candidate = new ArenaBuildSnapshot {
                buildId = property.baseId, revision = property.revision, templateVersion = property.templateVersion,
                rulesetId = Ruleset, heroId = hero.ToString(), structures = property.Clone().structures
            };
            return candidate.TryValidateAndCopy(new OwnedTownLayoutSnapshot(OwnedTownTemplateManifest.Load()), out snapshot, out reason);
        }

        public bool Validate(ArenaBuildSnapshot snapshot, out string reason)
        {
            reason = null;
            if (_manifest == null || _manifest.templateVersion != OwnedTownTemplateManifest.Version ||
                snapshot.templateVersion != _manifest.templateVersion || (snapshot.rulesetId != Ruleset && snapshot.rulesetId != ConstructionRuleset && snapshot.rulesetId != LegacyRuleset))
            { reason = "The saved layout requires a matching shipped template and ruleset."; return false; }
            if (!Enum.TryParse<HeroClassOpt>(snapshot.heroId, out var hero) || hero == HeroClassOpt.None || !Enum.IsDefined(typeof(HeroClassOpt), hero))
            { reason = "The layout has an unknown hero identity."; return false; }
            // This contract deliberately cannot approve unvalidated combat payloads.
            if (snapshot.defenders.Count != 0 || snapshot.loadoutIds.Count != 0 || snapshot.squadIds.Count != 0)
            { reason = "Combat payload requires match roster validation."; return false; }
            bool construction = snapshot.rulesetId != LegacyRuleset;
            if (_manifest.entries.Count == 0 || snapshot.structures.Count < _manifest.entries.Count ||
                (!construction && snapshot.structures.Count != _manifest.entries.Count))
            { reason = "The captured structure census differs from the shipped template."; return false; }
            var byId = new Dictionary<string, OwnedTownTemplateManifest.Entry>(StringComparer.Ordinal);
            foreach (var entry in _manifest.entries)
            {
                var id = entry.structure?.inheritedPose?.templateStructureId;
                if (string.IsNullOrEmpty(id) || byId.ContainsKey(id))
                { reason = "The shipped template has missing or duplicate identities."; return false; }
                byId.Add(id, entry);
            }
            var used = new HashSet<string>(StringComparer.Ordinal);
            var boxes = new List<Bounds>();
            var moved = new List<bool>();
            foreach (var record in snapshot.structures)
            {
                if (record.constructionPending && snapshot.rulesetId != Ruleset)
                { reason = "Unfinished construction requires the current layout ruleset."; return false; }
                if (record.retired && !construction) { reason = "This layout requires the construction import ruleset."; return false; }
                var pose = record.inheritedPose;
                if (pose == null)
                {
                    if (!construction || !TryGridBounds(record, out var addedBounds, out reason)) return false;
                    boxes.Add(addedBounds); moved.Add(true);
                    continue;
                }
                string id = pose.templateStructureId;
                // Legacy records resolve only against the original authored path AND name.
                if (string.IsNullOrEmpty(id))
                    foreach (var entry in _manifest.entries)
                        if (entry.structure.inheritedPose.sourcePath == pose.sourcePath && entry.structure.inheritedPose.sourceName == pose.sourceName)
                        { id = entry.structure.inheritedPose.templateStructureId; break; }
                if (id == null || !used.Add(id) || !byId.TryGetValue(id, out var template))
                { reason = "A layout structure is missing, duplicated or unknown."; return false; }
                var baseline = template.structure;
                var catalog = CatalogRegistry.Get(record.placement.itemId);
                if (catalog?.repo == null || record.placement.itemId != baseline.placement.itemId ||
                    record.placement.level < baseline.placement.level ||
                    (record.placement.level != baseline.placement.level && (!construction || !template.movableTower ||
                        record.placement.level > BuildModeController.MaxLevelFor(catalog))))
                { reason = "The captured structure catalog identity or level changed without an upgrade rule."; return false; }
                // ⛔ WO-1872 — THE PERIMETER CAN BE CLEARED, AND IT STILL CANNOT BE SOLD.
                // This read `record.retired && !template.movableTower` flat, and that refused BOTH
                // the 158 fitted wall sections AND the Heart. After the capture standdown the walls
                // arrive as rubble the player is meant to clear, so a flat refusal would have made
                // the ruling unimplementable: nothing could ever remove them.
                //
                // The carve-out is exact, and it is exact because the CATALOG ID CANNOT CARRY IT:
                // the Heart (RaidSpire) and all ten Watchtowers share `tower_arcane_spire`
                // (Assets/Resources/OwnedTown/IronBastionTemplate.json, read 2026-09-18). The
                // manifest's own movableTower flag separates the towers; behaviorId separates the
                // walls; what is left - not movable, not a wall - is the Heart alone, and the Heart
                // is still refused. Which VERB retired it (cleared for salvage vs sold for a refund)
                // is enforced at the two entry points, OwnedTownConstructionService.TryQuoteSale and
                // .TryQuoteClear, because a retired record's condition is forced to 0 by
                // OwnedBaseProgression.ValidateStructures and this validator cannot tell them apart
                // after the fact.
                bool clearableWall = catalog?.repo?.behaviorId == "WallSegment";
                if (record.retired && !template.movableTower && !clearableWall)
                { reason = "The town's objective cannot be sold or cleared."; return false; }
                var original = baseline.inheritedPose;
                if (pose.sourceScene != original.sourceScene || pose.sx != original.sx || pose.sy != original.sy || pose.sz != original.sz ||
                    (!string.IsNullOrEmpty(pose.templateStructureId) && JsonConvert.SerializeObject(pose.parentFrame) != JsonConvert.SerializeObject(original.parentFrame)))
                { reason = "The captured structure shape or coordinate frame requires migration."; return false; }
                var resolved = pose.Clone(); resolved.parentFrame = original.parentFrame;
                bool changed = pose.x != original.x || pose.y != original.y || pose.z != original.z ||
                    pose.qx != original.qx || pose.qy != original.qy || pose.qz != original.qz || pose.qw != original.qw;
                if (changed && !template.movableTower)
                { reason = "Fitted perimeter structures cannot be moved by this layout ruleset."; return false; }
                var position = OwnedTownTemplateManifest.WorldMatrix(resolved).MultiplyPoint3x4(Vector3.zero);
                var origin = OwnedTownTemplateManifest.WorldMatrix(original).MultiplyPoint3x4(Vector3.zero);
                if (changed && (new Vector2(position.x, position.z).magnitude > 54f || Mathf.Abs(position.y - origin.y) > .25f))
                { reason = "A moved tower is outside the supported town plot or elevation."; return false; }
                boxes.Add(OwnedTownTemplateManifest.WorldBounds(template, resolved));
                moved.Add(changed);
            }
            if (used.Count != _manifest.entries.Count)
            { reason = "A captured identity was removed instead of retained in the layout."; return false; }
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    if (!moved[i] && !moved[j]) continue; // Authored intersections are part of the approved scene.
                    if (snapshot.structures[i].condition01 == 0f || snapshot.structures[j].condition01 == 0f) continue;
                    var a = boxes[i]; var b = boxes[j];
                    a.extents *= .98f; b.extents *= .98f;
                    if (a.Intersects(b)) { reason = "A moved tower overlaps another standing structure."; return false; }
                }
            return true;
        }

        public static bool SupportsConstruction(CatalogEntry entry) => entry?.repo != null &&
            (entry.repo.behaviorId == "DefenseTower" || entry.repo.behaviorId == "WallSegment") &&
            entry.repo.placement?.mustSitOn != PlacementSurface.WallWalk;

        public static string PlacementBlockReason(CatalogEntry entry) =>
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == OwnedTownScenePose.SceneName && !SupportsConstruction(entry)
                ? "Available in your castle. This town supports ground defenses and walls." : null;

        public static bool TryGridBounds(OwnedBaseStructure record, out Bounds bounds, out string reason)
        {
            bounds = default; reason = null;
            var entry = CatalogRegistry.Get(record.placement.itemId);
            // These factory behaviors have durable condition replay. Other catalog families need their own adapter.
            if (!SupportsConstruction(entry) ||
                record.placement.level > BuildModeController.MaxLevelFor(entry))
            { reason = "This structure has no supported town construction/condition adapter."; return false; }
            var p = record.placement;
            if (p.wallMounted || Mathf.Abs(p.worldY) > .25f || Mathf.Abs(p.yawOffset) > 45f ||
                p.cellX < 0 || p.cellX >= 36 || p.cellZ < 0 || p.cellZ >= 36)
            { reason = "New construction is outside the supported ground grid."; return false; }
            var size = StructureFactory.MeasureClaimFootprintXZ(entry);
            float angle = (p.yawSteps * 90f + p.yawOffset) * Mathf.Deg2Rad;
            float c = Mathf.Abs(Mathf.Cos(angle)), s = Mathf.Abs(Mathf.Sin(angle));
            float width = Mathf.Ceil((size.x * c + size.y * s) / 3f) * 3f;
            float depth = Mathf.Ceil((size.x * s + size.y * c) / 3f) * 3f;
            var center = new Vector3(-45f + (p.cellX + .5f) * 3f, p.worldY + 4f, -45f + (p.cellZ + .5f) * 3f);
            bounds = new Bounds(center, new Vector3(width, 8f, depth));
            float farX = Mathf.Abs(center.x) + width * .5f, farZ = Mathf.Abs(center.z) + depth * .5f;
            if (new Vector2(farX, farZ).magnitude > 54f)
            { reason = "The construction footprint extends outside the town plot."; return false; }
            return true;
        }
    }
}
