// =============================================================================
// HeartVigilDressingController -- Circle vigil-weight dressing on the Heart (WO-1874).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// THIRD AXIS on the Heart. Threat (HeartState crystal hue) and HP (HeartAuraController
// size/luminance/motion) are hands off. This dresses roots / Heartfire / wisps / crown
// by density and motion, NEVER hue. No ancestor silhouette. No second VFX pool.
//
// Reads VigilCeremonyLedger only. Never HUD types.
// =============================================================================

using System.Collections;
using DeNelle.Core.Circle;
using DeNelle.Core.Diagnostics;
using UnityEngine;

namespace DeNelle.Village
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HeartController))]
    public sealed class HeartVigilDressingController : MonoBehaviour
    {
        private const string Sys = "VigilCeremony";
        private const string TreeAuraKey = AmbientAuraPolicy.WithheldAmbientAuraKey;
        private const float SequenceSeconds = 3.0f;

        [SerializeField] private HeartController _heart;

        private VFXHandle _foot;
        private VFXHandle _trunk;
        private VFXHandle _crown;
        private Coroutine _sequence;
        private int _appliedTier = -1;
        private Vector3 _footPos;
        private Vector3 _trunkPos;
        private Vector3 _crownPos;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedStatic;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedStatic;
            AttachToHearts();
        }

        private static void OnSceneLoadedStatic(
            UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode mode)
            => AttachToHearts();

        private static void AttachToHearts()
        {
            var hearts = Object.FindObjectsByType<HeartController>();
            foreach (var heart in hearts)
            {
                if (heart == null) continue;
                if (heart.GetComponent<HeartVigilDressingController>() == null)
                    heart.gameObject.AddComponent<HeartVigilDressingController>();
            }
        }

        private void Reset() => _heart = GetComponent<HeartController>();

        private void Awake()
        {
            if (_heart == null) _heart = GetComponent<HeartController>();
        }

        private void OnEnable()
        {
            var ledger = VigilCeremonyLedger.Shared;
            ledger.DressingChanged += OnDressingChanged;
            ledger.SequenceRequested += OnSequenceRequested;
        }

        private void OnDisable()
        {
            var ledger = VigilCeremonyLedger.Shared;
            ledger.DressingChanged -= OnDressingChanged;
            ledger.SequenceRequested -= OnSequenceRequested;
        }

        private void Start()
        {
            if (_heart == null) _heart = GetComponent<HeartController>();
            Seat();
            int tier = VigilCeremonyLedger.Shared.CurrentDressingTier;
            string heartName = _heart != null ? _heart.name : name;
            FlowTrace.Step(Sys, "HeartVigilDressing attached on '" + heartName + "' tier=" + tier);
            ApplyDressing(tier, sequenced: false);
        }

        private void OnDestroy()
        {
            StopSequence();
            StopHandle(ref _foot);
            StopHandle(ref _trunk);
            StopHandle(ref _crown);
        }

        private void OnDressingChanged()
        {
            int tier = VigilCeremonyLedger.Shared.CurrentDressingTier;
            FlowTrace.Step(Sys, "dressing changed on '" + name + "' to tier " + tier);
            ApplyDressing(tier, sequenced: false);
        }

        private void OnSequenceRequested()
        {
            int tier = VigilCeremonyLedger.Shared.CurrentDressingTier;
            FlowTrace.Step(Sys, "sequence requested on '" + name + "' tier=" + tier);
            StopSequence();
            _sequence = StartCoroutine(PlaySequence(tier));
        }

        private IEnumerator PlaySequence(int tier)
        {
            bool vfxOk = VFXManager.Instance != null;
            if (!vfxOk)
            {
                FlowTrace.Warn(Sys, "VFXManager missing — fail-open the ceremony plate.");
                VigilCeremonyLedger.Shared.NotifySequenceCompleted();
                ApplyDressing(tier, sequenced: false);
                yield break;
            }

            Seat();
            ApplyDressing(0, sequenced: true);

            float third = SequenceSeconds / 3f;
            if (tier >= 1)
            {
                Guard.Try(Sys, "vigil sequence roots", () => EnsureFoot(EmberEmission(tier), EmberScale(tier), 1f));
                yield return new WaitForSecondsRealtime(third);
            }
            if (tier >= 3)
            {
                Guard.Try(Sys, "vigil sequence trunk", () => EnsureTrunk(BeaconEmission(tier), 0.85f, BeaconSpeed(tier)));
                yield return new WaitForSecondsRealtime(third);
            }
            else
            {
                yield return new WaitForSecondsRealtime(third);
            }
            if (tier >= 4)
            {
                Guard.Try(Sys, "vigil sequence canopy", () => EnsureCrown(CrownEmission(tier), CrownScale(tier), 1.1f));
                yield return new WaitForSecondsRealtime(third);
            }
            else
            {
                yield return new WaitForSecondsRealtime(third);
            }

            ApplyDressing(tier, sequenced: true);
            VigilCeremonyLedger.Shared.NotifySequenceCompleted();
            _sequence = null;
        }

        private void ApplyDressing(int tier, bool sequenced)
        {
            int clamped = VigilCeremonyWords.ClampTier(tier);
            if (!sequenced && clamped == _appliedTier) return;
            _appliedTier = clamped;
            Seat();

            Guard.Try(Sys, "apply vigil dressing", () =>
            {
                if (clamped <= 0)
                {
                    StopHandle(ref _foot);
                    StopHandle(ref _trunk);
                    StopHandle(ref _crown);
                    return;
                }

                if (VFXManager.Instance == null)
                {
                    FlowTrace.Warn(Sys, "VFXManager missing while applying dressing tier " + clamped);
                    return;
                }

                if (clamped >= 1)
                    EnsureFoot(EmberEmission(clamped), EmberScale(clamped), 1f);
                else
                    StopHandle(ref _foot);

                if (clamped >= 3)
                    EnsureTrunk(BeaconEmission(clamped), 0.85f, BeaconSpeed(clamped));
                else
                    StopHandle(ref _trunk);

                if (clamped >= 4)
                    EnsureCrown(CrownEmission(clamped), CrownScale(clamped), clamped >= 5 ? 1.15f : 1.05f);
                else
                    StopHandle(ref _crown);
            });
        }

        private static float EmberEmission(int tier) => tier <= 1 ? 0.35f : (tier == 2 ? 0.75f : 0.9f);
        private static float EmberScale(int tier) => tier <= 1 ? 0.55f : (tier == 2 ? 0.75f : 0.9f);
        private static float BeaconEmission(int tier) => tier >= 5 ? 1.1f : 0.8f;
        private static float BeaconSpeed(int tier) => tier >= 3 ? 1.55f : 1f;
        private static float CrownEmission(int tier) => tier >= 5 ? 1.25f : 1.0f;
        private static float CrownScale(int tier) => tier >= 5 ? 1.2f : 1.1f;

        private void EnsureFoot(float emission, float scale, float speed)
            => _foot = EnsureAt(ref _foot, _footPos, emission, scale, speed);

        private void EnsureTrunk(float emission, float scale, float speed)
            => _trunk = EnsureAt(ref _trunk, _trunkPos, emission, scale, speed);

        private void EnsureCrown(float emission, float scale, float speed)
            => _crown = EnsureAt(ref _crown, _crownPos, emission, scale, speed);

        private VFXHandle EnsureAt(ref VFXHandle handle, Vector3 world, float emission, float scale, float speed)
        {
            if (handle == null || !handle.IsAlive)
            {
                handle = VFXManager.PlayKey(TreeAuraKey, world, Quaternion.identity, transform, null, scale);
                if (handle == null)
                {
                    FlowTrace.Warn(Sys, "PlayKey('" + TreeAuraKey + "') returned null at " + world);
                    return null;
                }
            }
            else
            {
                handle.SetPosition(world);
            }

            var mod = handle.Modulator;
            if (mod != null)
            {
                mod.SetEmissionScale(emission);
                mod.SetScaleMul(scale);
                mod.SetSimulationSpeed(speed);
            }
            return handle;
        }

        private void Seat()
        {
            if (TryComputeCrown(out Vector3 crown, out Vector3 canopy, out Vector3 foot))
            {
                _footPos = foot;
                _trunkPos = Vector3.Lerp(foot, canopy, 0.45f);
                _crownPos = crown;
                return;
            }
            Vector3 origin = transform.position;
            _footPos = origin;
            _trunkPos = origin + Vector3.up * 1.5f;
            _crownPos = origin + Vector3.up * 3.5f;
        }

        private bool TryComputeCrown(out Vector3 crown, out Vector3 canopy, out Vector3 foot)
        {
            crown = default; canopy = default; foot = default;
            var rends = GetComponentsInChildren<Renderer>(false);
            bool have = false; Bounds b = default;
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null || r is ParticleSystemRenderer) continue;
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!have) { b = r.bounds; have = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!have) return false;
            canopy = new Vector3(b.center.x, Mathf.Lerp(b.center.y, b.max.y, 0.5f), b.center.z);
            crown = new Vector3(b.center.x, Mathf.Lerp(b.center.y, b.max.y, 0.8f), b.center.z);
            foot = new Vector3(b.center.x, b.min.y, b.center.z);
            return true;
        }

        private void StopSequence()
        {
            if (_sequence == null) return;
            StopCoroutine(_sequence);
            _sequence = null;
        }

        private static void StopHandle(ref VFXHandle handle)
        {
            if (handle == null) return;
            handle.Stop(immediate: true);
            handle = null;
        }
    }
}
