using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DeNelle.Core.State
{
    /// <summary>Village supplies catalog IDs, plot bounds, footprints, elevation and budget rules.</summary>
    public interface IArenaBuildValidationAuthority
    {
        bool Validate(ArenaBuildSnapshot snapshot, out string reason);
    }

    /// <summary>Origin-neutral data for AI practice and future player snapshots. No wagers or rewards.</summary>
    [Serializable]
    public sealed class ArenaBuildSnapshot
    {
        public const int CurrentSchemaVersion = 1;
        public int schemaVersion = CurrentSchemaVersion;
        public string buildId;
        public int revision;
        public string rulesetId;
        public string templateVersion;
        public List<OwnedBaseStructure> structures = new List<OwnedBaseStructure>();
        public List<PlacedDefenderData> defenders = new List<PlacedDefenderData>();
        public string heroId;
        public List<string> loadoutIds = new List<string>();
        public List<string> squadIds = new List<string>();

        public ArenaBuildSnapshot Clone()
        {
            var next = (ArenaBuildSnapshot)MemberwiseClone();
            next.structures = structures == null ? null : new List<OwnedBaseStructure>(structures.Count);
            if (structures != null) foreach (var item in structures) next.structures.Add(item?.Clone());
            next.defenders = defenders == null ? null : new List<PlacedDefenderData>(defenders);
            next.loadoutIds = loadoutIds == null ? null : new List<string>(loadoutIds);
            next.squadIds = squadIds == null ? null : new List<string>(squadIds);
            return next;
        }

        // Validated output is detached both from the authoring source and from the authority's
        // temporary input. Retain it per match; do not consult a mutable live layout after start.
        public bool TryValidateAndCopy(IArenaBuildValidationAuthority authority,
            out ArenaBuildSnapshot snapshot, out string reason)
        {
            snapshot = null;
            if (schemaVersion != CurrentSchemaVersion || revision < 1 ||
                !OwnedBaseProgression.Token(buildId) || !OwnedBaseProgression.Token(rulesetId) || !OwnedBaseProgression.Token(heroId))
                return OwnedBaseProgression.Fail("Invalid arena schema, revision, or identity.", out reason);
            if (!OwnedBaseProgression.ValidateStructures(structures, out reason)) return false;
            if (defenders == null || defenders.Count > 4096 || loadoutIds == null || loadoutIds.Count > 64 || squadIds == null || squadIds.Count > 4096)
                return OwnedBaseProgression.Fail("Arena collections exceed transport bounds or are missing.", out reason);
            foreach (var d in defenders)
                if (!OwnedBaseProgression.Token(d.itemId) || d.yawSteps < 0 || d.yawSteps > 3)
                    return OwnedBaseProgression.Fail("Invalid defender placement.", out reason);
            foreach (var id in loadoutIds) if (!OwnedBaseProgression.Token(id)) return OwnedBaseProgression.Fail("Invalid loadout ID.", out reason);
            foreach (var id in squadIds) if (!OwnedBaseProgression.Token(id)) return OwnedBaseProgression.Fail("Invalid squad ID.", out reason);
            if (authority == null) return OwnedBaseProgression.Fail("Catalog and plot validation authority is required.", out reason);
            // Pass a disposable clone, but publish our own detached validated data. Authority is a
            // trusted rule reader; it must not mutate the supplied model.
            var candidate = Clone();
            var probe = candidate.Clone();
            try { if (!authority.Validate(probe, out reason)) return false; }
            catch (Exception ex) { return OwnedBaseProgression.Fail("Arena validation failed: " + ex.GetType().Name, out reason); }
            if (JsonConvert.SerializeObject(candidate) != JsonConvert.SerializeObject(probe))
                return OwnedBaseProgression.Fail("Arena validation must not mutate the snapshot.", out reason);
            snapshot = candidate; reason = null; return true;
        }
    }
}
