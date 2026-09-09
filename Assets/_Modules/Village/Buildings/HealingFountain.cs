// =============================================================================
// HealingFountain — Healing Caravan. A placeable SUPPORT structure that
// heals the Heart of Elarion (Tree of Life) OUT OF BATTLE ONLY.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Cloned from CrystalMine (the proven placeable-structure pattern): self-resolves
// its refs in Start, a 3-level upgrade ladder paid in Coins, an [F]/Tap proximity
// upgrade UI, and a per-level reskin. What it does DIFFERENTLY:
//
//   • It HEALS the Heart. Each Update, while OUT OF BATTLE (calm hub / prepare),
//     it restores rate*dt HP to the HeartController (0-100 float). HeartController
//     .Heal clamps at 100 and no-ops at max, so the cap is free.
//   • The heal rate scales with level: L1 = 1.0, L2 = 2.0, L3 = 3.5 HP/s.
//   • OUT-OF-BATTLE GATE (reuse the established predicate): tick ONLY when the wave
//     loop is not Active, not in the last 5 s of Countdown, and no ATB/Arena battle
//     is in flight (DeNelle.Core.Combat.BattleLock). Null WaveManager = safe = calm.
//   • A GOLD (never green — owner is colorblind) heal-aura VFX loop is held above the
//     fountain while it is actively healing; it stops the instant a battle starts or
//     the Heart reaches full HP.
//
// SINGLETON: one Wellspring per village (RepoProps.singleton). Gated behind the
// Arcane Tower research perk 'arcane-wellspring' (BuildingPerkService) — the build
// palette filters healing_caravan out until that perk is owned.
//
// §12 INSTRUMENTATION: FlowTrace.Step when the heal gate flips (open<->closed);
// FlowTrace.Throttle (~1/s) on the active heal so a headless run can see the drip.
// =============================================================================

using UnityEngine;
using UnityEngine.UIElements;
using DeNelle.Core.State;
using DeNelle.Core.Catalog;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    [DisallowMultipleComponent]
    public sealed class HealingFountain : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Levels")]
        [SerializeField] private GameObject _level1Prefab;
        [SerializeField] private GameObject _level2Prefab;
        [SerializeField] private GameObject _level3Prefab;

        [Tooltip("When true, skip building the placeholder/prefab visual — an external " +
                 "fountain mesh is the body. Gameplay unchanged.")]
        [SerializeField] private bool _useExternalVisual = false;

        [Header("Upgrade costs (Coins)")]
        [SerializeField] private int _costL1toL2 = 400;
        [SerializeField] private int _costL2toL3 = 1000;

        [Header("Proximity")]
        [SerializeField] private float _promptHeight  = 3.5f;
        [SerializeField, Min(1f)] private float _activateRadius = 3.5f;

        [Header("UI (optional UIDocument for upgrade panel)")]
        [SerializeField] private GameObject _upgradeUiRoot;

        // ── Constants ─────────────────────────────────────────────────────────

        public const int MaxLevel = 3;
        private const float CheckInterval = 0.15f;

        /// <summary>Per-level heal rate in HP/s (index = level-1). L1=1.0, L2=2.0, L3=3.5.</summary>
        private static readonly float[] HealRatePerLevel = { 1.0f, 2.0f, 3.5f };

        /// <summary>The Hovl VFX catalog key for the gold heal aura (loop, recolorable).
        /// Doc-only path — the .asset row is authored in-editor later:
        ///   Assets/Hovl Studio/RPG VFX Bundle/Random effect prefabs/Buff heal.prefab
        /// Until the row exists PlayKey no-ops (fallback), so this compiles/runs regardless.</summary>
        // Owner VfxManualPicks: HealingFountain_Aura (Druid aura). Fountain_Heal_Aura remains
        // a catalog alias so older rows still resolve after regenerate.
        private const string AuraKey = "HealingFountain_Aura";

        /// <summary>HDR gold tint for the aura — colorblind-safe (NEVER green). Applied as
        /// ParticleSystem StartColor via VFXManager.PlayKey when the aura row is Recolorable.</summary>
        private static readonly Color GoldAura = new Color(1.0f, 0.82f, 0.28f, 1.0f);

        // ── Runtime ───────────────────────────────────────────────────────────

        private int _currentLevel = 1;
        private int _maxLevel = MaxLevel;

        private HeartController _heart;
        private WaveManager _wave;

        private Transform _hero;
        private HeroLocomotion _heroLoco;
        private bool _heroFound;
        private bool _isInRange;
        private bool _uiOpen;
        private bool _awaitingSimpleConfirm;
        private GameObject _promptGo;
        private float _nextCheck;
        private GameObject _currentVisual;

        // Heal-gate + aura state.
        private bool _healingActive;          // true while the aura loop is held + HP is dripping in
        private bool _lastGateOpen;           // last-seen gate state, for the FlowTrace.Step edge log
        private bool _gateInitialised;        // so the first frame always logs the initial gate state
        private VFXHandle _auraHandle;        // the held gold aura loop (Stop() to dim)

        public int CurrentLevel => _currentLevel;
        public bool IsMaxLevel => _currentLevel >= _maxLevel;
        public float HealRate => HealRatePerLevel[Mathf.Clamp(_currentLevel - 1, 0, HealRatePerLevel.Length - 1)];

        // ── Catalog wiring ─────────────────────────────────────────────────────

        /// <summary>
        /// Reads level ceiling from the catalog RepoProps (StructureFactory calls this on attach).
        /// Heal rate is fixed per-level design canon (1.0/2.0/3.5); costs default to the Coin
        /// ladder 400/1000. Null-safe — a missing repo leaves the defaults intact.
        /// </summary>
        public void Configure(CatalogEntry entry)
        {
            var repo = entry != null ? entry.repo : null;
            if (repo == null) return;
            if (repo.maxLevel >= 1) _maxLevel = Mathf.Min(repo.maxLevel, HealRatePerLevel.Length);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Start()
        {
            ResolveHeart();
            ResolveWave();
            ResolveHero();
            ApplyVisual();

            if (_upgradeUiRoot != null) _upgradeUiRoot.SetActive(false);
        }

        private void OnDisable()
        {
            StopAura();
            if (_uiOpen) CloseUpgradeUI();
            MobileInteractButton.Release(this);
        }

        private void Update()
        {
            // 1) HEAL TICK — runs regardless of hero proximity (a support structure heals
            //    passively). The out-of-battle gate + cap-at-100 keep it correct.
            HealTick();

            // 2) PROXIMITY / UPGRADE UI — mirrors CrystalMine.
            if (!_heroFound) { ResolveHero(); return; }
            if (_hero == null) { _heroFound = false; return; }

            if (MobileInteractButton.Suppressed)
            {
                MobileInteractButton.Release(this);
                if (_promptGo != null) HidePrompt();
                return;
            }

            // A fully-attuned Caravan is passive scenery/healing, not an upgrade
            // interaction. The old path kept advertising "Upgrade Wellspring" at L3,
            // then opened the giant world-space max-level bubble on tap.
            if (!CanOfferUpgrade(_currentLevel, _maxLevel))
            {
                MobileInteractButton.Release(this);
                if (_promptGo != null) HidePrompt();
                if (_uiOpen) CloseUpgradeUI();
                _isInRange = false;
                return;
            }

            if (_uiOpen)
            {
                if (_awaitingSimpleConfirm)
                    MobileInteractButton.Request(this, "Confirm Upgrade", ConfirmSimpleUpgrade);
                else
                    MobileInteractButton.Release(this);
                return;
            }

            if (Time.time >= _nextCheck)
            {
                _nextCheck = Time.time + CheckInterval;
                float sqr = (_hero.position - transform.position).sqrMagnitude;
                bool nowIn = sqr <= _activateRadius * _activateRadius;
                if (nowIn != _isInRange)
                {
                    _isInRange = nowIn;
                    if (_isInRange) ShowPrompt();
                    else            HidePrompt();
                }
            }

            if (_isInRange)
                MobileInteractButton.Request(this, "Upgrade Wellspring", OpenUpgradeUI);
            else
                MobileInteractButton.Release(this);

            if (_promptGo != null && MobileInteractButton.IsActive) HidePrompt();
        }

        // ── Heal gate + tick ──────────────────────────────────────────────────

        /// <summary>
        /// The out-of-battle heal drip. Gate (reused predicate): heal ONLY when the wave loop
        /// is not Active, not within 5 s of a Countdown wave, and no ATB/Arena battle is live.
        /// Null WaveManager is treated as out-of-battle (safe). Holds the gold aura while
        /// healing; stops it on battle-start or when the Heart is full.
        /// </summary>
        private void HealTick()
        {
            if (_heart == null) { ResolveHeart(); }

            bool gateOpen = IsOutOfBattle();

            // §12 — log the gate on the first frame and on every open<->closed edge.
            if (!_gateInitialised || gateOpen != _lastGateOpen)
            {
                _gateInitialised = true;
                _lastGateOpen = gateOpen;
                FlowTrace.Step("Fountain",
                    $"heal-gate {(gateOpen ? "OPEN (out-of-battle)" : "CLOSED (battle/near-wave)")} " +
                    $"— L{_currentLevel} rate {HealRate:0.0} HP/s");
            }

            bool atMax = _heart != null && _heart.Hp >= 100f;
            bool shouldHeal = gateOpen && _heart != null && !atMax;

            if (shouldHeal)
            {
                float amount = HealRate * Time.deltaTime;
                _heart.Heal(amount);   // clamps at 100, no-ops at max — cap is free
                StartAura();
                FlowTrace.Throttle("Fountain", "tick", 1f,
                    $"healing Heart +{HealRate:0.0} HP/s (L{_currentLevel}) — Hp now {_heart.Hp:0.0}/100");
            }
            else
            {
                // Battle started, no Heart, or Heart full — dim the aura.
                StopAura();
            }

            _healingActive = shouldHeal;
        }

        /// <summary>
        /// The reused out-of-battle predicate. Safe defaults: a null WaveManager (never
        /// resolved / no wave loop in this scene) counts as calm/out-of-battle.
        /// </summary>
        private bool IsOutOfBattle()
        {
            if (BattleLock.IsInBattle()) return false;
            if (_wave == null) ResolveWave();
            if (_wave == null) return true;   // no wave loop → calm

            if (_wave.Phase == WavePhase.Active) return false;
            if (_wave.Phase == WavePhase.Countdown && _wave.CountdownRemaining <= 5f) return false;
            return true;
        }

        // ── Gold heal aura (Hovl VFX loop) ──────────────────────────────────────

        private void StartAura()
        {
            if (_auraHandle != null) return;   // already holding the loop
            _auraHandle = VFXManager.PlayKey(
                AuraKey,
                transform.position + Vector3.up * 1.2f,
                Quaternion.identity,
                transform,          // parent so the aura tracks the fountain
                GoldAura,           // HDR gold tint (colorblind-safe)
                1.0f);
            // PlayKey returns null when the catalog row isn't authored yet — that's fine,
            // healing still runs; the aura simply no-ops until the .asset row exists.
        }

        private void StopAura()
        {
            if (_auraHandle == null) return;
            _auraHandle.Stop();
            _auraHandle = null;
        }

        // ── Upgrade ───────────────────────────────────────────────────────────

        /// <summary>
        /// Upgrade the fountain one level, paid in Coins. Returns false at max level or when
        /// coins are insufficient. Raises the heal rate for the next tick immediately.
        /// </summary>
        public bool TryUpgrade()
        {
            if (_currentLevel >= _maxLevel)
            {
                Debug.Log("[HealingFountain] Already at max level.");
                return false;
            }

            int cost = _currentLevel == 1 ? _costL1toL2 : _costL2toL3;

            var svc = GameStateService.Instance;
            if (svc?.State == null)
            {
                Debug.LogWarning("[HealingFountain] GameStateService unavailable.");
                return false;
            }

            var res = svc.State.Resources;
            if (res.Coins < cost)
            {
                Debug.Log($"[HealingFountain] Upgrade requires {cost} Coins — have {res.Coins}.");
                return false;
            }

            var r = res;
            r.Coins -= cost;
            svc.State.Resources = r;
            svc.Save();

            _currentLevel++;
            ApplyVisual();

            Debug.Log($"[HealingFountain] Upgraded to Level {_currentLevel} " +
                      $"(heal {HealRate:0.0} HP/s). Coins remaining: {r.Coins}.");
            return true;
        }

        public static bool CanOfferUpgrade(int currentLevel, int maxLevel)
        {
            return currentLevel >= 1 && currentLevel < maxLevel;
        }

        private void ConfirmSimpleUpgrade()
        {
            _awaitingSimpleConfirm = false;
            TryUpgrade();
            CloseUpgradeUI();
        }

        // ── Upgrade UI ────────────────────────────────────────────────────────

        private void OpenUpgradeUI()
        {
            HidePrompt();
            _uiOpen = true;
            if (_heroLoco != null) _heroLoco.enabled = false;

            if (_upgradeUiRoot != null)
            {
                _upgradeUiRoot.SetActive(true);
                InjectUpgradePanel();
            }
            else
            {
                ShowSimpleUpgradePrompt();
            }
        }

        private void CloseUpgradeUI()
        {
            _uiOpen = false;
            _awaitingSimpleConfirm = false;
            if (_upgradeUiRoot != null) _upgradeUiRoot.SetActive(false);
            if (_heroLoco != null) _heroLoco.enabled = true;
            MobileInteractButton.Release(this);
        }

        private void InjectUpgradePanel()
        {
            var doc = _upgradeUiRoot?.GetComponent<UIDocument>();
            if (doc?.rootVisualElement == null) return;

            var root = doc.rootVisualElement;
            root.Clear();

            var panel = new VisualElement();
            panel.style.position       = Position.Absolute;
            panel.style.top            = 20; panel.style.left  = 20;
            panel.style.right          = 20; panel.style.bottom = 20;
            panel.style.backgroundColor = new StyleColor(new Color(0.05f, 0.02f, 0.12f, 0.94f));
            panel.style.borderTopLeftRadius     = 10;
            panel.style.borderTopRightRadius    = 10;
            panel.style.borderBottomLeftRadius  = 10;
            panel.style.borderBottomRightRadius = 10;
            panel.style.paddingTop = panel.style.paddingBottom =
            panel.style.paddingLeft = panel.style.paddingRight = 20;
            root.Add(panel);

            var title = new Label("Healing Caravan");
            title.style.fontSize = 22;
            title.style.color = new StyleColor(new Color(1.0f, 0.86f, 0.45f));
            title.style.marginBottom = 12;
            panel.Add(title);

            var levelLbl = new Label($"Current Level: {_currentLevel} / {_maxLevel}");
            levelLbl.style.fontSize = 15;
            levelLbl.style.color = new StyleColor(new Color(0.95f, 0.90f, 0.75f));
            panel.Add(levelLbl);

            var yieldLbl = new Label($"Heals the Heart {HealRate:0.0} HP/s — out of battle only.");
            yieldLbl.style.fontSize = 13;
            yieldLbl.style.color = new StyleColor(new Color(1.0f, 0.82f, 0.28f));
            yieldLbl.style.marginTop = 6;
            yieldLbl.style.marginBottom = 16;
            panel.Add(yieldLbl);

            if (!IsMaxLevel)
            {
                int cost = _currentLevel == 1 ? _costL1toL2 : _costL2toL3;
                int coins = (int)(GameStateService.Instance?.State?.Resources.Coins ?? 0);
                bool canAfford = coins >= cost;

                var costLbl = new Label($"Upgrade cost: {cost} Coins  (you have {coins})");
                costLbl.style.fontSize = 14;
                costLbl.style.color = new StyleColor(canAfford
                    ? new Color(1f, 0.85f, 0.3f)
                    : new Color(1f, 0.3f, 0.3f));
                costLbl.style.marginBottom = 12;
                panel.Add(costLbl);

                var upgradeBtn = new Button(() =>
                {
                    if (TryUpgrade()) CloseUpgradeUI();
                    else InjectUpgradePanel();
                })
                { text = canAfford ? "Upgrade" : "Need more Coins" };
                upgradeBtn.SetEnabled(canAfford);
                StyleButton(upgradeBtn, new Color(0.28f, 0.20f, 0.05f));
                upgradeBtn.style.marginBottom = 8;
                panel.Add(upgradeBtn);
            }
            else
            {
                var maxLbl = new Label("Fully attuned — restoring the Heart at full flow.");
                maxLbl.style.fontSize = 14;
                maxLbl.style.color = new StyleColor(new Color(1.0f, 0.86f, 0.45f));
                maxLbl.style.marginBottom = 12;
                panel.Add(maxLbl);
            }

            var closeBtn = new Button(CloseUpgradeUI) { text = "X  Close" };
            StyleButton(closeBtn, new Color(0.22f, 0.16f, 0.05f));
            panel.Add(closeBtn);
        }

        private static void StyleButton(Button btn, Color bgColor)
        {
            btn.style.paddingTop = btn.style.paddingBottom = 8;
            btn.style.paddingLeft = btn.style.paddingRight = 20;
            btn.style.fontSize = 14;
            btn.style.backgroundColor = new StyleColor(bgColor);
            btn.style.color = new StyleColor(new Color(1.0f, 0.95f, 0.85f));
            btn.style.borderTopLeftRadius     = btn.style.borderTopRightRadius    = 6;
            btn.style.borderBottomLeftRadius  = btn.style.borderBottomRightRadius = 6;
        }

        private void ShowSimpleUpgradePrompt()
        {
            if (IsMaxLevel)
            {
                _promptGo = BuildBubble("Max Level — Wellspring at full flow",
                    _promptHeight + 0.5f,
                    new Color(0.14f, 0.10f, 0.02f, 0.96f),
                    new Color(1.0f, 0.82f, 0.35f));
            }
            else
            {
                int cost = _currentLevel == 1 ? _costL1toL2 : _costL2toL3;
                _promptGo = BuildBubble($"[ Tap / F ] Confirm Upgrade — {cost} Coins",
                    _promptHeight + 0.5f,
                    new Color(0.14f, 0.10f, 0.02f, 0.96f),
                    new Color(1.0f, 0.82f, 0.35f));
                _awaitingSimpleConfirm = true;
            }
        }

        // ── Visual ────────────────────────────────────────────────────────────

        private void ApplyVisual()
        {
            if (_currentVisual != null) Destroy(_currentVisual);
            if (_useExternalVisual) return;

            GameObject prefab = _currentLevel switch
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
                _currentVisual = BuildPlaceholder(_currentLevel);
            }
        }

        private GameObject BuildPlaceholder(int level)
        {
            var go = new GameObject($"HealingFountainVisual_L{level}");
            go.transform.SetParent(transform, false);

            // Gold basin tiers (colorblind-safe — luminance steps, never green).
            Color tint = level switch
            {
                1 => new Color(0.55f, 0.44f, 0.16f),
                2 => new Color(0.78f, 0.62f, 0.22f),
                _ => new Color(1.00f, 0.82f, 0.32f),
            };

            float r = 0.7f + level * 0.15f;
            AddBasinRing(go, tint, new Vector3(0f, 0.15f, 0f), new Vector3(r * 2f, 0.3f, r * 2f));
            AddBasinRing(go, tint, new Vector3(0f, 0.55f, 0f), new Vector3(r * 1.2f, 0.5f, r * 1.2f));
            if (level >= 2)
                AddBasinRing(go, tint, new Vector3(0f, 1.0f, 0f), new Vector3(r * 0.7f, 0.4f, r * 0.7f));
            if (level >= 3)
                AddBasinRing(go, tint, new Vector3(0f, 1.35f, 0f), new Vector3(r * 0.35f, 0.5f, r * 0.35f));

            return go;
        }

        private void AddBasinRing(GameObject parent, Color tint, Vector3 localPos, Vector3 localScale)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Basin";
            DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(parent.transform, false);
            ring.transform.localPosition = localPos;
            ring.transform.localScale    = localScale;

            var rend = ring.GetComponent<Renderer>();
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

        // ── Prompt bubble ─────────────────────────────────────────────────────

        private void ShowPrompt()
        {
            string label = IsMaxLevel
                ? "Wellspring — Active"
                : $"[ Tap / F ]  Upgrade Wellspring  (L{_currentLevel}->{_currentLevel + 1})";

            _promptGo = BuildBubble(label, _promptHeight,
                new Color(0.12f, 0.09f, 0.02f, 0.96f),
                new Color(1.0f, 0.80f, 0.30f));
        }

        private void HidePrompt()
        {
            if (_promptGo != null) Destroy(_promptGo);
            _promptGo = null;
        }

        private GameObject BuildBubble(string text, float localY, Color bgColor, Color outlineColor)
        {
            var go = new GameObject("HealingFountainPrompt");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * localY;

            float charsApprox = Mathf.Max(text.Length, 8);
            float w = Mathf.Clamp(charsApprox * 0.10f + 0.4f, 1.2f, 4.0f);
            float h = 0.38f;

            var outline = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DestroyImmediate(outline.GetComponent<Collider>());
            outline.transform.SetParent(go.transform, false);
            outline.transform.localPosition = new Vector3(0f, 0f, 0.012f);
            outline.transform.localScale    = new Vector3(w + 0.06f, h + 0.06f, 1f);
            ApplyFlat(outline.GetComponent<Renderer>(), outlineColor);

            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DestroyImmediate(bg.GetComponent<Collider>());
            bg.transform.SetParent(go.transform, false);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.006f);
            bg.transform.localScale    = new Vector3(w, h, 1f);
            ApplyFlat(bg.GetComponent<Renderer>(), bgColor);

            var txtGo = new GameObject("Text");
            txtGo.transform.SetParent(go.transform, false);
            txtGo.transform.localScale = Vector3.one * 0.055f;
            var tm = txtGo.AddComponent<TextMesh>();
            tm.text = text; tm.fontSize = 96; tm.characterSize = 0.30f;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.color = new Color(1.00f, 0.95f, 0.82f);

            var billboard = go.AddComponent<PromptBillboard>();
            billboard.Camera = Camera.main;
            return go;
        }

        private static void ApplyFlat(Renderer renderer, Color colour)
        {
            if (renderer == null) return;
            Shader s = Shader.Find("Universal Render Pipeline/Unlit")
                       ?? Shader.Find("Unlit/Color")
                       ?? Shader.Find("Sprites/Default");
            if (s == null) return;
            var mat = new Material(s) { color = colour };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     colour);
            renderer.sharedMaterial = mat;
        }

        // ── Ref resolution ─────────────────────────────────────────────────────

        private void ResolveHeart()
        {
            if (_heart != null) return;
            _heart = FindFirstObjectByType<HeartController>();
        }

        private void ResolveWave()
        {
            var found = FindObjectsByType<WaveManager>(FindObjectsSortMode.None);
            _wave = found.Length > 0 ? found[0] : null;
        }

        private void ResolveHero()
        {
            if (_heroFound) return;
            var loco = FindFirstObjectByType<HeroLocomotion>();
            if (loco == null) return;
            _hero = loco.transform;
            _heroLoco = loco;
            _heroFound = true;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1.0f, 0.82f, 0.30f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _activateRadius);
        }
#endif
    }
}
