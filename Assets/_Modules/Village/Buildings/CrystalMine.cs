// =============================================================================
// CrystalMine - passive Crystal generator; awards crystals on every cleared wave.
// -----------------------------------------------------------------------------
// WO-856 (2026-08-04) - THE MINE NOW ACTUALLY PAYS OUT.
//
// What it does now:
//   * Pays from LEVEL 1. The old "return unless at max level" gate is gone - the
//     payout scales along an AUTHORED curve instead of switching on at the top.
//   * The per-wave yield is data, not C#: buildings.json "crystal-mine" authors
//     "crystalsPerWave": [1, 2, 4], indexed by (level - 1) and clamped into range.
//     A bare scalar (the pre-WO-856 shape, e.g. 1) READ-MIGRATES to a flat curve
//     so a hand-edit back to a number degrades instead of throwing.
//   * The LEVEL is READ, never owned. It comes from the PlacedStructure this
//     component sits on - the one per-instance level the save spine round-trips
//     (PlacedStructureData.level -> BaseLayoutLoader -> PlacedStructure.level).
//
// WHY there is no private level field here (WO-856 section 4, ARCHITECTURE_
// PRINCIPLES section 1 "one authority per concern"): the mine used to keep a
// private current-level field that persisted NOWHERE and could only be raised by
// a legacy Coins F-key prompt. It was a second, invisible level authority - the
// same failure mode ModifierService records for the windmill/farm split - and it
// is why a built mine could never reach the level its own payout gate demanded.
// The mine is a READER: it asks "what level am I?", it never answers it.
// [single-level-authority] in CrystalProductionRegression reflects for that field
// by name and FAILS the suite if one is ever reintroduced.
//
// RETIRED in WO-856 section 6 (do not restore): the Coins F-key upgrade path -
// TryUpgrade / OpenUpgradeUI / InjectUpgradePanel / ShowSimpleUpgradePrompt /
// ConfirmSimpleUpgrade, the _costL1toL2 / _costL2toL3 fields, the world-space
// prompt bubble and the MobileInteractButton registration. Its only effect was
// to increment the deleted private level field, it charged the WRONG currency lane
// (Coins is the shop/sell wallet; the mine costs Wood+Iron and yields Crystals),
// and keeping it would leave TWO independent systems able to level one building.
// The mine upgrades through the ONE canonical surface: the BuildMode selection
// panel's Upgrade verb, charging structures-catalog.json "mine_crystal"
// repo.upgradeCost (240W/150I then 560W/350I, deliberately ZERO crystals - see
// WO-856 section 5: charging crystals to unlock crystal income inverts the loop
// the mine exists to relieve).
//
// Scene setup: the mine is spawned by StructureFactory from the "mine_crystal"
// catalog row (behaviorId "CrystalMine"), which is where PlacedStructure comes
// from. A scene-baked mine with no PlacedStructure honestly reads level 1 and
// pays the L1 rung - never zero, never a throw.
// =============================================================================

using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DeNelle.Village
{
    [DisallowMultipleComponent]
    public sealed class CrystalMine : MonoBehaviour
    {
        // -- Inspector --------------------------------------------------------

        [Header("Levels")]
        [Tooltip("Visual prefab shown at Level 1. Null -> procedural crystal-blue placeholder.")]
        [SerializeField] private GameObject _level1Prefab;
        [Tooltip("Visual prefab shown at Level 2.")]
        [SerializeField] private GameObject _level2Prefab;
        [Tooltip("Visual prefab shown at Level 3.")]
        [SerializeField] private GameObject _level3Prefab;

        [Tooltip("WO-110: when true, skip building the placeholder/prefab visual - an external " +
                 "crystal mesh (+ CrystalVisual spin/pulse) is the mine's body. Gameplay unchanged.")]
        [SerializeField] private bool _useExternalVisual = false;

        // -- Constants --------------------------------------------------------

        /// <summary>Null-safe fallback rung used when buildings.json omits/garbles
        /// "crystalsPerWave" (the historical hard-coded +1). Never zero: a producer
        /// that produces nothing is the WO-856 defect, not a safe default.</summary>
        private const int DefaultCrystalsPerWave = 1;

        // -- Runtime ----------------------------------------------------------

        private WaveManager _wave;
        private GameObject _currentVisual;
        // WO-1653 - THE CURVE CACHE IS STATIC, AND THAT IS THE POINT.
        // The yield curve is authored DATA (buildings.json), identical for every mine in
        // the world, so a per-instance cache was per-instance only by accident. Making it
        // static is what lets CrystalsPerWaveAt below be reachable WITHOUT a MonoBehaviour,
        // which is what the Manage detail card needs to state "Crystals / wave 2 -> 4"
        // without writing the curve read a second time. Exactly the shape WO-1567 used when
        // ResourceCollector.ThroughputScale's private per-hour formula became the public
        // ResourceBuildingProgression.ProductionPerHour: ONE producer, two readers.
        // NOTE: this is a CURVE cache, never a LEVEL. The mine still READS its level off
        // PlacedStructure and owns none - [single-level-authority] in
        // CrystalProductionRegression fails if a component-local level field returns.
        private static int[] s_curve;          // cached data-driven yield curve (buildings.json)
        private PlacedStructure _placed;
        private bool _placedResolved;

        /// <summary>
        /// The mine's upgrade level, READ from the PlacedStructure record the save spine
        /// round-trips (never owned here - WO-856 section 4). Clamped into the authored
        /// curve's range so an out-of-band level can neither index off the end nor pay 0.
        /// A mine with no PlacedStructure (scene-baked) honestly reads level 1.
        /// </summary>
        private int CurrentLevel
        {
            get
            {
                if (!_placedResolved)
                {
                    _placed = GetComponentInParent<PlacedStructure>();
                    _placedResolved = true;
                }
                int level = _placed != null ? _placed.level : 1;
                return Mathf.Clamp(level, 1, Mathf.Max(1, Curve().Length));
            }
        }

        // -- Lifecycle --------------------------------------------------------

        private void Start()
        {
            ResolveWave();
            ApplyVisual();
        }

        private void OnEnable()
        {
            SubscribeToWave();
        }

        private void OnDisable()
        {
            UnsubscribeFromWave();
        }

        // -- Wave crystal yield -----------------------------------------------

        private void OnWaveCleared(int waveId)
        {
            // WO-856: NO level gate. The mine pays from L1 - the curve is the progression.
            var economy = CrystalEconomy.Instance;
            if (economy == null)
            {
                Debug.LogWarning("[CrystalMine] CrystalEconomy service not found - no crystal awarded.");
                return;
            }

            int level = CurrentLevel;
            int yield = CrystalsPerWaveAt(level);
            economy.AddCrystals(yield);
            Debug.Log($"[CrystalMine] Wave {waveId} cleared - +{yield} Crystals awarded (mine L{level}).");
        }

        /// <summary>
        /// The per-wave crystal yield at <paramref name="level"/>, read from buildings.json
        /// (the <c>crystal-mine</c> entry's <c>crystalsPerWave</c> key) so the payout curve is
        /// DATA, not a C# literal. Indexed by <c>level - 1</c> and clamped into range.
        ///
        /// <para>⭐ WO-1653 - THE ONE PRODUCER OF "HOW MUCH THIS MINE PAYS", and PUBLIC STATIC
        /// for exactly one reason: the Manage detail card has to state the mine's
        /// current -&gt; next yield ("Crystals / wave  2 -&gt; 4") and the only alternative was
        /// to read the curve a second time in the VM. Two readers of one authored curve is
        /// the duplicated state CLAUDE.md sections 2/5/16 record three times over. The wave
        /// payout at <see cref="OnWaveCleared"/> and the Manage card now call THIS.</para>
        /// </summary>
        public static int CrystalsPerWaveAt(int level)
        {
            int[] curve = Curve();
            int idx = Mathf.Clamp(level - 1, 0, curve.Length - 1);
            return curve[idx];
        }

        /// <summary>
        /// The authored yield curve, parsed once and cached. Uses the same
        /// <see cref="DeNelle.Core.CanonicalJson"/> loader path as <see cref="BuildingCatalog"/>
        /// (Resources-first, StreamingAssets fallback).
        ///
        /// READ-MIGRATION (WO-856 section 5, mirrors the CLAUDE.md section 7 read-migrate
        /// discipline): the key may be an ARRAY (the authored curve) or a bare SCALAR (the
        /// pre-WO-856 shape). A scalar degrades to a FLAT curve of that value - so a hand-edit
        /// back to a number keeps paying instead of throwing. Missing / unparseable / empty
        /// falls back to a flat <see cref="DefaultCrystalsPerWave"/> curve, warned not swallowed
        /// (CLAUDE.md section 12: no silent failures).
        /// </summary>
        private static int[] Curve()
        {
            if (s_curve != null) return s_curve;

            int[] parsed = null;
            try
            {
                string json = DeNelle.Core.CanonicalJson.Read("Data/Canonical/buildings.json");
                if (!string.IsNullOrEmpty(json))
                {
                    var arr = JObject.Parse(json)["buildings"] as JArray;
                    if (arr != null)
                    {
                        foreach (var tok in arr)
                        {
                            if (!(tok is JObject o) || (string)o["id"] != "crystal-mine") continue;
                            parsed = ParseCurve(o["crystalsPerWave"]);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CrystalMine] Could not read crystalsPerWave from buildings.json - " +
                                 $"falling back to a flat {DefaultCrystalsPerWave}/wave curve. {ex.Message}");
            }

            if (parsed == null || parsed.Length == 0)
                parsed = new[] { DefaultCrystalsPerWave };

            s_curve = parsed;
            return s_curve;
        }

        /// <summary>Array -> the curve verbatim; scalar -> a flat curve (read-migration);
        /// anything else -> null (the caller falls back + warns). Negative rungs clamp to 0.</summary>
        private static int[] ParseCurve(JToken token)
        {
            if (token == null) return null;

            if (token is JArray rungs)
            {
                if (rungs.Count == 0) return null;
                var curve = new int[rungs.Count];
                for (int i = 0; i < rungs.Count; i++) curve[i] = Mathf.Max(0, (int)rungs[i]);
                return curve;
            }

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                // READ-MIGRATION: a bare scalar is a FLAT curve, never a throw.
                return new[] { Mathf.Max(0, (int)token) };
            }

            return null;
        }

        // -- Visual -----------------------------------------------------------

        private void ApplyVisual()
        {
            if (_currentVisual != null) Destroy(_currentVisual);

            // WO-110: an external crystal mesh (+ CrystalVisual) is the body - don't build our own.
            if (_useExternalVisual) return;

            int level = CurrentLevel;
            GameObject prefab = level switch
            {
                1 => _level1Prefab,
                2 => _level2Prefab,
                _ => _level3Prefab,
            };

            if (prefab != null)
            {
                _currentVisual = Instantiate(prefab, transform);
                _currentVisual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            }
            else
            {
                _currentVisual = BuildPlaceholder(level);
            }
        }

        private GameObject BuildPlaceholder(int level)
        {
            // Octahedron-ish cluster from overlapping cubes - minimal but readable.
            var go = new GameObject($"CrystalMineVisual_L{level}");
            go.transform.SetParent(transform, false);

            Color tint = level switch
            {
                1 => new Color(0.35f, 0.20f, 0.65f),   // dim purple
                2 => new Color(0.50f, 0.30f, 0.90f),   // brighter violet
                _ => new Color(0.70f, 0.50f, 1.00f),   // glowing aether
            };

            float h = 0.8f + level * 0.4f;
            AddCrystalShard(go, tint, new Vector3(0f,   h * 0.5f, 0f), new Vector3(0.4f, h, 0.4f));
            AddCrystalShard(go, tint, new Vector3(0.3f, h * 0.3f, 0.1f), new Vector3(0.25f, h * 0.7f, 0.25f), 15f);
            AddCrystalShard(go, tint, new Vector3(-0.25f, h * 0.25f, -0.1f), new Vector3(0.22f, h * 0.6f, 0.22f), -10f);
            if (level >= 2)
                AddCrystalShard(go, tint, new Vector3(0.0f, h * 0.2f, -0.3f), new Vector3(0.2f, h * 0.5f, 0.2f), 20f);
            if (level >= 3)
                AddCrystalShard(go, tint, new Vector3(-0.15f, h * 0.35f, 0.3f), new Vector3(0.18f, h * 0.55f, 0.18f), -25f);

            return go;
        }

        private void AddCrystalShard(GameObject parent, Color tint, Vector3 localPos, Vector3 localScale, float yRot = 0f)
        {
            var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.name = "Shard";
            DestroyImmediate(shard.GetComponent<Collider>());
            shard.transform.SetParent(parent.transform, false);
            shard.transform.localPosition = localPos;
            shard.transform.localScale    = localScale;
            shard.transform.localRotation = Quaternion.Euler(0f, yRot, 15f);

            var rend = shard.GetComponent<Renderer>();
            if (rend != null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard")
                         ?? Shader.Find("Sprites/Default");
                var mat = new Material(s);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                else mat.color = tint;
                rend.sharedMaterial = mat;
            }
        }

        // -- Wave subscription ------------------------------------------------

        private void SubscribeToWave()
        {
            // bug-triage P1: resolve FIRST, then detach+attach on the manager we actually use
            // (RemoveListener is idempotent). The old order unsubscribed from a stale _wave
            // before ResolveWave overwrote it -> leaked listeners / duplicated crystal yield.
            ResolveWave();
            if (_wave == null) return;
            _wave.OnWaveCleared.RemoveListener(OnWaveCleared);
            _wave.OnWaveCleared.AddListener(OnWaveCleared);
        }

        private void UnsubscribeFromWave()
        {
            if (_wave != null) _wave.OnWaveCleared.RemoveListener(OnWaveCleared);
        }

        private void ResolveWave()
        {
            var found = FindObjectsByType<WaveManager>();
            _wave = found.Length > 0 ? found[0] : null;
        }
    }
}
