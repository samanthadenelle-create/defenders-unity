// =============================================================================
// OutpostHub — central claimed outpost with small local defense grid + troop
// recruitment that fully integrates with the Economy class.
// -----------------------------------------------------------------------------
// Priority 2 + 3 for the pet resource farming & outpost expansion.
//
// - Builds a central visual hub + 4 radial "build grid" posts (small defensive
//   footprint the player can reinforce).
// - Recruit AI defensive troops (Tank / DPS / Healer roles) that guard the posts.
// - Every recruitment costs resources via EconomyService.TrySpend(ResourceCost).
// - Recruited defenders have light ongoing upkeep (periodic small drain via
//   Economy.Grant with negative values) so economy and defense are linked.
// - Outposts further out (danger / distance) cost more to recruit but defenders
//   are stronger (scaling).
// - Defenders help protect nearby harvest sites and the outpost itself.
// - Reuses IDamageableStructure, Economy as source of truth, existing patterns
//   from ClaimableCamp / Outpost / CampGuards.
//
// Spawned by ClaimableCamp after building an outpost type, or directly for
// dev / future player-placed hubs. All code-built visuals.
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.World;
using DeNelle.Core.Combat;
using DeNelle.Village; // for EconomyService + ResourceCost
using DeNelle.Core.UI;

namespace DeNelle.Village.World
{
    public enum DefenderRole { Tank, DPS, Healer }

    /// <summary>
    /// Central hub for an outpost/settlement with a tiny defensive build area
    /// and the ability to recruit role-based AI troops that cost + upkeep resources.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OutpostHub : MonoBehaviour, IDamageableStructure
    {
        [Header("Economy & Scaling")]
        public RegionId Region = RegionId.Goldfields;
        public int ThreatLevel = 1;

        [Tooltip("Base recruitment costs (will be scaled by threat/distance).")]
        public ResourceCost BaseTankCost = new ResourceCost(stone: 25, iron: 12);
        public ResourceCost BaseDpsCost  = new ResourceCost(stone: 15, iron: 8, wood: 5);
        public ResourceCost BaseHealerCost = new ResourceCost(stone: 30, crystals: 4);

        [Header("Defense Grid")]
        public int MaxDefenders = 4;                 // one per radial post
        public float GuardRadius = 9f;

        [Header("Upkeep")]
        [Tooltip("Resources drained from Economy every upkeep interval per living defender (keeps economy honest).")]
        public ResourceCost UpkeepPerDefenderPerTick = new ResourceCost(stone: 1);

        [Tooltip("Seconds between upkeep drains.")]
        public float UpkeepInterval = 18f;

        [Header("Visual")]
        public float HubRadius = 3.2f;

        // ── Runtime ──────────────────────────────────────────────────────────
        private float _hp = 180f;
        private float _upkeepTick;
        private readonly List<OutpostDefender> _defenders = new List<OutpostDefender>(4);
        private readonly Vector3[] _postOffsets = new Vector3[]
        {
            new Vector3( 3.8f, 0,  0),
            new Vector3(-3.8f, 0,  0),
            new Vector3( 0,   0,  3.8f),
            new Vector3( 0,   0, -3.8f),
        };

        public bool IsAlive => _hp > 0f;

        /// <summary>
        /// WO-1439 — the hub is spawned by ClaimableCamp after the player pays for it, so it
        /// is the player's asset. Constant Friendly.
        /// </summary>
        public CombatFaction Faction => CombatFaction.Friendly;

        public int CurrentDefenderCount => _defenders.Count;
        public IReadOnlyList<OutpostDefender> Defenders => _defenders;

        public void ApplyContactDamage(float amount)
        {
            if (_hp <= 0f) return;
            _hp = Mathf.Max(0f, _hp - Mathf.Abs(amount));
            if (_hp <= 0f)
            {
                Debug.LogWarning($"[OutpostHub] Hub in {Region} was overrun.");
                // Defenders are lost with the hub (or could be told to retreat).
                foreach (var d in _defenders) if (d != null) Destroy(d.gameObject);
                _defenders.Clear();
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            BuildHubVisual();
            // Optional: auto-spawn a couple of cheap defenders on creation for feel.
            // Real flow is player recruitment via the UI below.
        }

        private void Update()
        {
            // Simple proximity recruitment UI (code-built Canvas because UXML doesn't
            // work reliably in player builds per project rules).
            bool playerNear = IsPlayerNear();
            UpdateRecruitUI(playerNear);

            // Upkeep — every defender slowly costs the economy (Food primarily).
            _upkeepTick += Time.deltaTime;
            if (_upkeepTick >= UpkeepInterval)
            {
                _upkeepTick = 0f;
                PayUpkeep();
            }

            // Cull dead defenders from our list.
            _defenders.RemoveAll(d => d == null || !d.IsAlive);
        }

        private bool IsPlayerNear()
        {
            var player = GameObject.FindWithTag("Player");
            if (player == null) return false;
            return Vector3.Distance(player.transform.position, transform.position) < 12f;
        }

        // ── Recruitment (Economy integrated) ─────────────────────────────────
        private GameObject _recruitUI;

        private void UpdateRecruitUI(bool show)
        {
            if (show && _recruitUI == null)
            {
                CreateRecruitUI();
            }
            else if (!show && _recruitUI != null)
            {
                Destroy(_recruitUI);
                _recruitUI = null;
            }
        }

        private void CreateRecruitUI()
        {
            _recruitUI = new GameObject("OutpostRecruitUI");
            _recruitUI.transform.SetParent(transform, false);

            var canvas = _recruitUI.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;

            var scaler = _recruitUI.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            var rect = _recruitUI.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(3.2f, 1.8f);
            rect.localPosition = new Vector3(0f, 4.5f, 0f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * 0.012f;

            // Background panel
            var bg = new GameObject("Panel", typeof(UnityEngine.UI.Image));
            bg.transform.SetParent(_recruitUI.transform, false);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            var bgImage = bg.GetComponent<UnityEngine.UI.Image>();
            bgImage.sprite = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
            bgImage.type = UnityEngine.UI.Image.Type.Sliced;
            bgImage.color = Color.white;

            // Title
            CreateText(_recruitUI.transform, "Recruit Defenders", new Vector2(0f, 0.65f), 18, Color.white);

            // Buttons for the three roles
            CreateRecruitButton(_recruitUI.transform, "Tank", DefenderRole.Tank, new Vector2(-0.9f, 0f));
            CreateRecruitButton(_recruitUI.transform, "DPS",  DefenderRole.DPS,  new Vector2(0f, 0f));
            CreateRecruitButton(_recruitUI.transform, "Healer", DefenderRole.Healer, new Vector2(0.9f, 0f));

            // Current count
            CreateText(_recruitUI.transform, () => $"{CurrentDefenderCount}/{MaxDefenders} guards", new Vector2(0f, -0.6f), 14, Color.yellow);
        }

        private void CreateRecruitButton(Transform parent, string label, DefenderRole role, Vector2 anchoredPos)
        {
            var btnGO = new GameObject($"Btn_{role}", typeof(UnityEngine.UI.Button), typeof(UnityEngine.UI.Image));
            btnGO.transform.SetParent(parent, false);
            var rect = btnGO.GetComponent<RectTransform>();
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(0.85f, 0.42f);

            var img = btnGO.GetComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.2f, 0.25f, 0.2f, 0.9f);

            var btn = btnGO.GetComponent<UnityEngine.UI.Button>();
            btn.onClick.AddListener(() => TryRecruit(role));

            var txtGO = new GameObject("Label", typeof(TMPro.TextMeshProUGUI));
            txtGO.transform.SetParent(btnGO.transform, false);
            var tmp = txtGO.GetComponent<TMPro.TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 11;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.color = Color.white;
            var tRect = txtGO.GetComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero; tRect.anchorMax = Vector2.one; tRect.sizeDelta = Vector2.zero;
            MedievalUiSkin.ApplyButton(btn, primary: role == DefenderRole.DPS);
        }

        private void CreateText(Transform parent, string text, Vector2 anchored, int size, Color c)
        {
            var go = new GameObject("Text", typeof(TMPro.TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TMPro.TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.color = c;
            var r = go.GetComponent<RectTransform>();
            r.anchoredPosition = anchored;
            r.sizeDelta = new Vector2(2.8f, 0.35f);
        }

        private void CreateText(Transform parent, Func<string> textFunc, Vector2 anchored, int size, Color c)
        {
            var go = new GameObject("LiveText", typeof(TMPro.TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TMPro.TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.color = c;
            var r = go.GetComponent<RectTransform>();
            r.anchoredPosition = anchored;
            r.sizeDelta = new Vector2(2.8f, 0.35f);

            // Simple live updater
            var updater = go.AddComponent<LiveTextUpdater>();
            updater.TextFunc = textFunc;
            updater.Target = tmp;
        }

        private void TryRecruit(DefenderRole role)
        {
            if (_defenders.Count >= MaxDefenders)
            {
                Debug.Log("[OutpostHub] Max defenders reached.");
                return;
            }

            var cost = GetRecruitCost(role);
            var econ = EconomyService.Instance;
            if (econ == null || !econ.TrySpend(cost))
            {
                Debug.LogWarning($"[OutpostHub] Cannot afford {role} recruit cost.");
                return;
            }

            SpawnDefender(role);
        }

        private ResourceCost GetRecruitCost(DefenderRole role)
        {
            ResourceCost baseCost = role switch
            {
                DefenderRole.Tank => BaseTankCost,
                DefenderRole.DPS => BaseDpsCost,
                _ => BaseHealerCost
            };

            // Scaling: further / more dangerous outposts cost more to defend.
            float scale = 1f + (ThreatLevel * 0.15f) + (Vector3.Distance(transform.position, Vector3.zero) / 140f);
            return new ResourceCost(
                wood: Mathf.RoundToInt(baseCost.Wood * scale),
                stone: Mathf.RoundToInt(baseCost.Stone * scale),
                iron: Mathf.RoundToInt(baseCost.Iron * scale),
                crystals: Mathf.RoundToInt(baseCost.Crystals * scale)
            );
        }

        private void SpawnDefender(DefenderRole role)
        {
            int idx = _defenders.Count % _postOffsets.Length;
            Vector3 postPos = transform.position + _postOffsets[idx];

            var go = new GameObject($"Defender_{role}_{idx}");
            go.transform.position = postPos;
            go.transform.SetParent(transform, false);

            var defender = go.AddComponent<OutpostDefender>();
            defender.Role = role;
            defender.GuardPost = postPos;
            defender.GuardRadius = GuardRadius;
            defender.Hub = this;

            // Slight stat variation by role and world scaling.
            float worldScale = 1f + ThreatLevel * 0.1f;
            defender.MaxHp = (role == DefenderRole.Tank ? 55 : 32) * worldScale;
            defender.Damage = (role == DefenderRole.DPS ? 14 : role == DefenderRole.Tank ? 9 : 4) * worldScale;
            defender.AttackRange = role == DefenderRole.Healer ? 7f : 4.5f;

            _defenders.Add(defender);

            Debug.Log($"[OutpostHub] Recruited {role} defender (cost paid via Economy). Total: {_defenders.Count}");
        }

        private void PayUpkeep()
        {
            if (_defenders.Count == 0) return;

            var econ = EconomyService.Instance;
            if (econ == null) return;

            // Small drain per defender — keeps the economy loop honest (you pay to keep your army).
            for (int i = 0; i < _defenders.Count; i++)
            {
                // Use negative grant (Grant accepts negative in the internal Max(0,...) path? No — we use a negative cost via TrySpend pattern or direct.
                // EconomyService doesn't have a direct "negative grant" public API for upkeep, so we do a small TrySpend with the upkeep cost.
                // If the player is broke the defenders can still exist but future recruitment is blocked.
                var upkeep = UpkeepPerDefenderPerTick;
                // For simplicity we silently deduct from the persisted side via the service if possible.
                // In a fuller version we would have an "Upkeep" event or a dedicated method.
                if (econ.Stone >= upkeep.Stone && econ.Wood >= upkeep.Wood)
                {
                    econ.TrySpend(upkeep); // best effort
                }
            }
        }

        private void BuildHubVisual()
        {
            // Central platform + post (similar aesthetic to Outpost but more "hub" like).
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            platform.name = "HubPlatform";
            platform.transform.SetParent(transform, false);
            platform.transform.localScale = new Vector3(HubRadius * 2f, 0.3f, HubRadius * 2f);
            Strip(platform);
            Paint(platform, new Color(0.28f, 0.26f, 0.24f));

            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = "HubPost";
            post.transform.SetParent(transform, false);
            post.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            post.transform.localScale = new Vector3(0.6f, 2.2f, 0.6f);
            Strip(post);
            Paint(post, new Color(0.45f, 0.42f, 0.38f));

            // Small roof / banner.
            var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cap.name = "HubCap";
            cap.transform.SetParent(transform, false);
            cap.transform.localPosition = new Vector3(0f, 4.0f, 0f);
            cap.transform.localScale = new Vector3(HubRadius * 1.6f, 0.4f, HubRadius * 1.6f);
            Strip(cap);
            Paint(cap, new Color(0.55f, 0.2f, 0.18f));

            // Mark the 4 guard posts visually (small rings).
            for (int i = 0; i < _postOffsets.Length; i++)
            {
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = $"GuardPost_{i}";
                ring.transform.SetParent(transform, false);
                ring.transform.localPosition = _postOffsets[i];
                ring.transform.localScale = new Vector3(1.1f, 0.12f, 1.1f);
                Strip(ring);
                Paint(ring, new Color(0.6f, 0.55f, 0.2f));
            }
        }

        private static void Strip(GameObject go)
        {
            var c = go.GetComponent<Collider>(); if (c) Destroy(c);
        }

        private static void Paint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sh) r.material = new Material(sh);
            r.material.color = c;
        }

        // Helper for live text in the recruit UI.
        private sealed class LiveTextUpdater : MonoBehaviour
        {
            public Func<string> TextFunc;
            public TMPro.TextMeshProUGUI Target;
            private void Update()
            {
                if (Target != null && TextFunc != null) Target.text = TextFunc();
            }
        }
    }
}
