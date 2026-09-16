using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Village
{
    /// <summary>Passive semantic identity for owner-authored castle geometry.</summary>
    [DisallowMultipleComponent]
    public sealed class AuthoredCastleStorefront : MonoBehaviour
    {
        [SerializeField] private string canonicalId;
        [SerializeField] private string legacyName;
        [SerializeField] private bool preserveAuthoredVisual = true;
        [SerializeField] private bool repairTripoMaterials = true;
        [SerializeField] private Texture2D forcedAlbedo;

        public string CanonicalId => canonicalId;
        public string LegacyName => legacyName;
        public bool PreserveAuthoredVisual => preserveAuthoredVisual;
        public bool RepairTripoMaterials => repairTripoMaterials;
        public Texture2D ForcedAlbedo => forcedAlbedo;

        public static bool IsAuthoredBarracksRecord(DeNelle.Core.State.PlacedStructureData data) =>
            data.itemId == "barracks" && data.authoredSourceId == "barracks" && data.authoredPose != null;

        /// <summary>True only when an explicit saved record binds this authored root.</summary>
        public static bool IsBoundAuthoredRoot(Transform target, string canonicalId)
        {
            if (target == null || canonicalId != "barracks") return false;
            var marker = target.GetComponent<AuthoredCastleStorefront>();
            if (marker == null || !marker.PreserveAuthoredVisual || marker.CanonicalId != canonicalId ||
                Find(marker.LegacyName, includeInactive: true) != target) return false;
            var state = DeNelle.Core.State.GameStateService.Instance?.State;
            if (state?.BaseLayout == null) return false;
            int matches = 0;
            for (int i = 0; i < state.BaseLayout.Count; i++)
            {
                var record = state.BaseLayout[i];
                if (record.itemId != canonicalId) continue;
                if (!IsAuthoredBarracksRecord(record)) return false;
                matches++;
            }
            return matches == 1;
        }

        public void SetMaterialPolicy(bool repair, Texture2D albedo)
        {
            repairTripoMaterials = repair;
            forcedAlbedo = albedo;
        }

        public void Configure(string id, string legacyName)
        {
            canonicalId = id;
            this.legacyName = legacyName;
        }

        // Queries run on Unity's main thread. Reuse scratch lists rather than allocating
        // FindObjectsByType arrays on each singleton/card query. No scene state is cached.
        private static readonly List<GameObject> Roots = new List<GameObject>(64);
        private static readonly List<Transform> Transforms = new List<Transform>(256);

        /// <summary>
        /// Resolve a unique semantic host in the active scene. An inactive marked host
        /// still owns its identity: an active legacy namesake must never replace it.
        /// This query never activates, skins, moves, or configures an object.
        /// </summary>
        public static Transform Find(string legacyName, bool includeInactive = false)
        {
            if (string.IsNullOrEmpty(legacyName)) return null;
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return null;

            Roots.Clear();
            scene.GetRootGameObjects(Roots);
            Transform marked = null;
            Transform legacy = null;
            int markedCount = 0, legacyCount = 0;
            for (int r = 0; r < Roots.Count; r++)
            {
                Transforms.Clear();
                Roots[r].GetComponentsInChildren(true, Transforms);
                for (int i = 0; i < Transforms.Count; i++)
                {
                    var candidate = Transforms[i];
                    var identity = candidate.GetComponent<AuthoredCastleStorefront>();
                    if (identity != null && string.Equals(identity.LegacyName, legacyName, StringComparison.Ordinal))
                    {
                        marked = candidate;
                        markedCount++;
                    }
                    if (candidate.name == legacyName && (includeInactive || candidate.gameObject.activeInHierarchy))
                    {
                        legacy = candidate;
                        legacyCount++;
                    }
                }
            }
            Roots.Clear();
            Transforms.Clear();
            if (markedCount > 1)
            {
                FlowTrace.Fail("Hub", $"Authored storefront identity '{legacyName}' has {markedCount} marked hosts in '{scene.name}'; refusing ambiguous lookup.");
                return null;
            }
            if (markedCount == 1)
                return includeInactive || marked.gameObject.activeInHierarchy ? marked : null;
            if (legacyCount > 1)
            {
                FlowTrace.Fail("Hub", $"Legacy storefront name '{legacyName}' has {legacyCount} hosts in '{scene.name}'; refusing ambiguous lookup.");
                return null;
            }
            return legacy;
        }

        /// <summary>Classify existing geometry as an authored twin, never a placement.</summary>
        public static bool MatchesAncestor(Transform target, string canonicalId)
        {
            if (target == null || string.IsNullOrEmpty(canonicalId) ||
                target.gameObject.scene != SceneManager.GetActiveScene()) return false;
            for (var current = target; current != null; current = current.parent)
            {
                var identity = current.GetComponent<AuthoredCastleStorefront>();
                if (identity != null && string.Equals(identity.CanonicalId, canonicalId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
