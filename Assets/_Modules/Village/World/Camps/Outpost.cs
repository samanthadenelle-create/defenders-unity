// =============================================================================
// Outpost - the player-built structure placed on a claimed camp. Auto-harvests a
// resource trickle into the wallet and is itself a damageable structure.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.World.Camps
//
// ISOLATION:
//   * Implements the EXISTING DeNelle.Core.Combat.IDamageableStructure interface
//     (read-only reuse - no edit) so the outpost can later be razed by the same
//     contact-damage path everything else uses. Interface only; no spawner edit.
//   * Auto-harvest banks into GameState via the SAME public path MineNode uses:
//     GameStateService.Instance.State.{Iron|Wood|Stone|AetherCrystals} += amount.
//     READ from MineNode.BankYield - we call the same public State fields, we do
//     NOT edit MineNode or GameStateService.
//   * Region/danger scales yield (deadlier region = richer) mirroring MineNode's
//     +25% per danger tier dial, via ZoneManager.DangerTierAt (read-only).
//
// Code-built primitive visual (a small post + roof). LogWarning, never error.
// ASCII-only strings. Canon: village is Elarion.
// =============================================================================
using UnityEngine;
using DeNelle.Core.Combat;
using DeNelle.Core.World;
using DeNelle.Core.Diagnostics;   // TGVRU — FlowTrace/Guard on the outpost visual build

namespace DeNelle.Village.World.Camps
{
    /// <summary>The buildable outpost kinds offered after a claim.</summary>
    public enum OutpostType
    {
        None = 0,
        Watchtower = 1,     // banks Iron (a guard post that levies metal)
        LumberOutpost = 2,  // banks Wood
        FarmOutpost = 3,    // banks Stone (homestead quarry trickle)
    }

    /// <summary>
    /// A claimed-camp outpost: auto-harvests a small resource trickle into the
    /// wallet and implements <see cref="IDamageableStructure"/> for future raids.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Outpost : MonoBehaviour, IDamageableStructure
    {
        public OutpostType Type { get; private set; } = OutpostType.Watchtower;
        public RegionId Region { get; private set; } = RegionId.Goldfields;
        public int ThreatLevel { get; private set; }

        [Header("Harvest")]
        [Tooltip("Base units banked per harvest tick (before the region danger bonus).")]
        public int BaseYieldPerTick = 2;

        [Tooltip("Seconds between auto-harvest ticks.")]
        public float HarvestInterval = 6f;

        [Header("Structure (IDamageableStructure)")]
        [Tooltip("Outpost hit points. Reaching 0 razes it (defend follow-up).")]
        public float MaxHp = 120f;

        private float _hp;
        private float _tick;

        // -- IDamageableStructure ---------------------------------------------
        public bool IsAlive => _hp > 0f;

        /// <summary>
        /// WO-1439 — an outpost only ever exists on a camp the PLAYER has claimed and built
        /// on (see ClaimableCamp), so it is Friendly by construction. Raiding tribes are
        /// Hostile and keep attacking it exactly as before.
        /// </summary>
        public CombatFaction Faction => CombatFaction.Friendly;

        public void ApplyContactDamage(float amount)
        {
            if (_hp <= 0f) return;
            _hp = Mathf.Max(0f, _hp - Mathf.Abs(amount));
            if (_hp <= 0f)
            {
                Debug.LogWarning($"[Outpost] {Type} outpost in {Region} was razed.");
                Destroy(gameObject);   // the camp remains claimed; outpost can be rebuilt.
            }
        }

        /// <summary>
        /// Configure the outpost. By default builds the primitive placeholder visual;
        /// pass <paramref name="buildPrimitiveVisual"/>=false when the ClaimableCamp is
        /// generating a REAL catalog-piece fortification (OutpostFoundationGenerator)
        /// on the same root — the foundation IS the visual then, and the placeholder
        /// post/cap would just clutter the centre. Harvest + IDamageableStructure logic
        /// is unchanged either way.
        /// </summary>
        public void Init(OutpostType type, RegionId region, int threat, bool buildPrimitiveVisual = true)
        {
            Type = type == OutpostType.None ? OutpostType.Watchtower : type;
            Region = region;
            ThreatLevel = threat;
            _hp = MaxHp;
            if (buildPrimitiveVisual) BuildVisual();
        }

        /// <summary>Which wallet field this outpost feeds.</summary>
        public MineResource Yields =>
            Type switch
            {
                OutpostType.LumberOutpost => MineResource.Wood,
                OutpostType.FarmOutpost   => MineResource.Stone,
                _                          => MineResource.Iron,   // Watchtower / default
            };

        /// <summary>Effective yield per tick including the region danger bonus
        /// (+25% per danger tier - mirrors MineNode's dial). Read-only.</summary>
        public int EffectiveYield
        {
            get
            {
                int tier = Mathf.Max(0, ZoneManager.DangerTierAt(transform.position));
                return Mathf.RoundToInt(BaseYieldPerTick * (1f + 0.25f * tier));
            }
        }

        private void Update()
        {
            if (!IsAlive) return;
            _tick += Time.deltaTime;
            if (_tick < HarvestInterval) return;
            _tick = 0f;
            BankTrickle(EffectiveYield);
        }

        // Route through EconomyService (the central Economy class) so outpost passive
        // trickle keeps Wood/Iron in-session in sync and fires OnChanged for any HUD
        // listeners. Grant also handles the persisted Food/Crystals side. Matches the
        // path now used by MineNode (pet + player harvest). Fallback to direct only if
        // EconomyService is absent very early in bootstrap.
        private void BankTrickle(int amount)
        {
            if (amount <= 0) return;

            var econ = EconomyService.Instance;
            if (econ != null)
            {
                switch (Yields)
                {
                    case MineResource.Iron:          econ.Grant(iron: amount); break;
                    case MineResource.Wood:          econ.Grant(wood: amount); break;
                    case MineResource.Stone:          econ.Grant(stone: amount); break;
                    case MineResource.AetherCrystal: econ.Grant(crystals: amount); break;
                }
                return;
            }

            // Fallback (rare): keep persistence even if EconomyService not yet up.
            var state = DeNelle.Core.State.GameStateService.Instance?.State;
            if (state == null)
            {
                Debug.LogWarning("[Outpost] EconomyService null and GameStateService.State null - harvest tick dropped.");
                return;
            }
            switch (Yields)
            {
                case MineResource.Iron:          state.Iron += amount;           break;
                case MineResource.Wood:          state.Wood += amount;           break;
                case MineResource.Stone:
                {
                    var bal = state.Resources;
                    bal.Stone += amount;
                    state.Resources = bal;
                    break;
                }
                case MineResource.AetherCrystal:
                {
                    // Crystals are unified onto Resources.Crystals (the single wallet).
                    var bal = state.Resources;
                    bal.Crystals += amount;
                    state.Resources = bal;
                    break;
                }
            }
            DeNelle.Core.State.GameStateService.Instance?.ResourcesChanged?.Invoke();
        }

        // -- Code-built primitive visual (no prefab hard-dependency) ----------
        private void BuildVisual()
        {
            // G — guard the primitive build; a throw here previously caught-WITHOUT-Fail (a silent
            // LogWarning that never reached the break-log). Route through Guard.Try so a thrown
            // visual self-reports loudly, then V-verify the outpost actually rendered.
            bool ok = Guard.Try("Outpost", $"build {Type} outpost visual", () =>
            {
                Color tint = Type switch
                {
                    OutpostType.LumberOutpost => new Color(0.45f, 0.30f, 0.15f),
                    OutpostType.FarmOutpost   => new Color(0.55f, 0.50f, 0.25f),
                    _                          => new Color(0.35f, 0.40f, 0.50f),
                };

                // Post.
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = "Post";
                StripCollider(post);
                post.transform.SetParent(transform, false);
                post.transform.localScale = new Vector3(0.4f, 1.4f, 0.4f);
                post.transform.localPosition = new Vector3(0f, 1.4f, 0f);
                Paint(post, tint);

                // Roof / banner cap.
                var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cap.name = "Cap";
                StripCollider(cap);
                cap.transform.SetParent(transform, false);
                cap.transform.localScale = new Vector3(1.4f, 0.4f, 1.4f);
                cap.transform.localPosition = new Vector3(0f, 2.9f, 0f);
                Paint(cap, tint * 1.2f);
            });

            if (!ok)
            {
                // R/U — was a silent catch->LogWarning. Fail-loud: a damageable outpost with no
                // visible post/cap is the invisible-blocker class (an unseen, hittable structure).
                FlowTrace.Fail("Outpost",
                    $"BuildVisual: {Type} outpost in {Region} threw building its post/cap primitive — " +
                    "outpost may be hittable but invisible.");
                return;
            }

            VerifyOutpostRenders();
        }

        // V — confirm the built outpost actually RENDERS (>=1 enabled renderer carrying a mesh).
        // A damageable IDamageableStructure with no visible mesh is the owner's invisible-blocker /
        // grey-foundation class: the hero can hit/raze it with NOTHING on screen. Belt-and-braces —
        // a Fail self-report to the break-log; the outpost stays (still damageable/harvesting).
        private void VerifyOutpostRenders()
        {
            int enabledWithMesh = 0;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                bool hasMesh = r.sharedMaterial != null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh == null) hasMesh = false;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh == null) hasMesh = false;
                if (hasMesh) enabledWithMesh++;
            }

            if (enabledWithMesh == 0)
                FlowTrace.Fail("Outpost",
                    $"INVISIBLE OUTPOST: {Type} outpost in {Region} at {transform.position} has 0 enabled " +
                    "renderers with a mesh — a damageable structure the hero can raze with nothing on screen.");
            else
                FlowTrace.Step("Outpost",
                    $"{Type} outpost in {Region} renders ({enabledWithMesh} enabled renderer(s) with a mesh).");
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        private static void Paint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            // Use a URP-safe lit shader if present; fall back to whatever default is.
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader != null) r.material = new Material(shader);
            r.material.color = c;
        }
    }
}
