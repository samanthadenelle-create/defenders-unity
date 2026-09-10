// =============================================================================
// HovlVfxCatalog — ScriptableObject mapping a STRING KEY to a Hovl Studio prefab
// + pool/override config. WO-VFX-002.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Create via:  Assets → Create → Defenders / Hovl VFX Catalog
// Loaded at runtime from Resources/VFX/HovlVfxCatalog by VFXManager (see
// VFXManager.Hovl.cs → EnsureHovlCatalog). The Hovl prefabs are NOT under
// Resources/, so each row holds a SERIALIZED prefab reference authored in-editor;
// only this .asset lives in Resources/ (build-size guard — no whole pack dumped).
//
// AUTHORING (two ways):
//   • One-click: run  Defenders/VFX/Generate Hovl VFX Catalog
//     (DeNelle.Editor.HovlVfxCatalogGenerator) — authors the .asset from the
//     curated key→path table (mirrors VFXCatalogGenerator).
//   • By hand: add a row, type the Key, drag the Hovl prefab into Prefab, set
//     PoolSize / DefaultScale / DefaultLifetime / Recolorable / IsLoop.
//
// The 8–10 shortlist keys + their EXACT Hovl paths (from Docs/VFX/
// HovlStudio_Inventory.md §5) are documented in VFXManager.Hovl.cs and the
// generator's table. Any key not in the catalog simply no-ops (logged, throttled).
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Vfx;   // WO-1348 - the Command Center pick override layer
using UnityEngine;

namespace DeNelle.Village
{
    [CreateAssetMenu(
        menuName = "Defenders/Hovl VFX Catalog",
        fileName = "HovlVfxCatalog",
        order    = 56)]
    public sealed class HovlVfxCatalog : ScriptableObject
    {
        // ── Row ───────────────────────────────────────────────────────────────

        [System.Serializable]
        public struct Row
        {
            [Tooltip("String key callers pass to VFXManager.PlayKey(\"...\").\n" +
                     "e.g. Fireball_Projectile, Arcane_Impact, Collector_Full, Raid_Explosion.")]
            public string Key;

            [Tooltip("The Hovl Studio prefab to pool + play. Null = the key no-ops (logged).")]
            public GameObject Prefab;

            [Tooltip("How many instances to pre-warm in Awake (0 = lazy instantiate on first use).")]
            [Min(0)] public int PoolSize;

            [Tooltip("Uniform scale applied when a caller passes scale <= 0. 0 or 1 = native size.")]
            public float DefaultScale;

            [Tooltip("Lifetime (s) before a ONESHOT auto-returns when a caller passes lifetime <= 0. " +
                     "0 = auto-detect from the particle systems. Ignored for loops.")]
            [Min(0f)] public float DefaultLifetime;

            [Tooltip("True if this effect can be HDR-recoloured at runtime (StartColor tint). " +
                     "Hovl HS_Blend_CG effects are recolourable — one base serves many elements.")]
            public bool Recolorable;

            [Tooltip("True for looping auras/trails/shields — PlayKey returns a VFXHandle so the " +
                     "caller can Stop() it. False = oneshot that auto-returns to the pool.")]
            public bool IsLoop;
        }

        // ── Inspector array ─────────────────────────────────────────────────────

        [Tooltip("One row per key. Rows with a null Prefab no-op at call time.")]
        public Row[] Rows = System.Array.Empty<Row>();

        // ── Runtime lookup (built on first use) ─────────────────────────────────

        private Dictionary<string, Row> _map;

        /// <summary>Build / rebuild the fast key→row dictionary from the Rows array.</summary>
        public void BuildLookup()
        {
            _map = new Dictionary<string, Row>(Rows.Length);
            foreach (var r in Rows)
            {
                if (string.IsNullOrEmpty(r.Key)) continue;
                _map[r.Key] = r;   // last row wins on duplicate keys
            }
        }

        /// <summary>
        /// Try to get the row for a given key. Returns false when the key is not in the
        /// catalog (caller no-ops).
        /// <para>
        /// ⭐ WO-1348: THIS IS THE ONE PLACE EVERY VFX KEY BECOMES A PREFAB, so it is the one
        /// place the Command Center's remote pick is applied. Every consumer of this catalog -
        /// VFXManager.PlayKey, towers, enemies, auras, abilities - therefore resolves through
        /// the override layer without a single call-site edit.
        /// </para>
        /// <para>
        /// ⛔ THE INVARIANT: no row, no network, no parse, no option pool => the build-time pick,
        /// BYTE FOR BYTE. <see cref="VfxPickOverrides.Resolve"/> answers "no override" for every
        /// failure it can have, so the two lines below are pure additions in front of the
        /// existing lookup and cannot change today's answer when the table is empty.
        /// </para>
        /// <para>
        /// An override BORROWS the prefab of another tagged key. It deliberately does NOT borrow
        /// that key's loop-ness when this key already has a row: a call site that asked for a
        /// oneshot and received a loop would never see the effect returned to the pool. A
        /// loop/oneshot mismatch is REFUSED and traced rather than papered over.
        /// </para>
        /// </summary>
        public bool TryGet(string key, out Row row)
        {
            if (_map == null) BuildLookup();
            bool haveBase = _map.TryGetValue(key, out row);

            var pick = VfxPickOverrides.Resolve(key);
            if (!pick.HasOverride) return haveBase;

            if (!_map.TryGetValue(pick.SourceKey, out var source) || source.Prefab == null)
            {
                // The option named a key this BUILD's catalog cannot resolve. Fall back and SAY SO -
                // CLAUDE.md section 16: art that is picked but never shipped fails with no error on
                // screen, and that silence has cost this project three separate incidents.
                // Throttled, not Warn-per-call: TryGet runs on EVERY play, and a Warn on a hot path
                // floods the device logcat ring and evicts the boot window - destroying the very
                // evidence the trace exists to capture. 30 s matches RemoteTunables' bad-row line.
                DeNelle.Core.Diagnostics.FlowTrace.Throttle(VfxPickOverrides.Sys,
                    "fallback-nosource:" + key + "=" + pick.OptionId, 30f,
                    $"FELL BACK for key '{key}': option id {pick.OptionId} names catalog key " +
                    $"'{pick.SourceKey}', which has no row or no prefab in THIS build's HovlVfxCatalog. " +
                    $"source={pick.Source} || the build-time pick is rendering, unchanged. Adding a prefab " +
                    "to the pool is still a build - this feature changes WHICH shipped effect is used, " +
                    "never WHICH effects exist.");
                return haveBase;
            }

            if (haveBase && source.IsLoop != row.IsLoop)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Throttle(VfxPickOverrides.Sys,
                    "fallback-loopmismatch:" + key + "=" + pick.OptionId, 30f,
                    $"FELL BACK for key '{key}': option id {pick.OptionId} ('{pick.SourceKey}') is " +
                    $"{(source.IsLoop ? "a LOOP" : "a ONESHOT")} and '{key}' is consumed as " +
                    $"{(row.IsLoop ? "a LOOP" : "a ONESHOT")}. source={pick.Source} || honouring it would " +
                    "hand the call site an effect it cannot stop or cannot return to the pool, so the " +
                    "build-time pick is rendering, unchanged. Pick an option with matching loop-ness.");
                return haveBase;
            }

            var picked = haveBase ? row : source;   // creation case inherits the option's whole shape
            picked.Key = key;
            picked.Prefab = source.Prefab;
            if (!haveBase) picked.IsLoop = source.IsLoop;
            row = picked;

            VfxPickOverrides.TraceApplied(key, pick, source.Prefab.name, created: !haveBase);
            return true;
        }

        private void OnEnable() => BuildLookup();
        private void OnValidate() => BuildLookup();
    }
}
