using DeNelle.Core.Diagnostics;
using DeNelle.Core.Ops;
using DeNelle.Village;
using DeNelle.Village.Crafting;
using UnityEngine;

namespace DeNelle.Dungeons
{
    /// <summary>A one-use emergency field craft attached to a composed dungeon oil cache.</summary>
    [DisallowMultipleComponent]
    public sealed class ComposedOilStill : MonoBehaviour
    {
        private const float Radius = 2.8f;

        /// <summary>
        /// WO-1805 Lane C: the SHIPPING fraction of a flask one field distillation returns. Public
        /// and named so the rail row dungeon.lanternStillRefillPct can be pinned against it by
        /// DungeonLanternTeachRegression instead of against a second copy of 40 (CLAUDE.md
        /// sections 2 / 5 - a number written twice is a number that rots).
        /// </summary>
        public const float RefillFraction = 0.40f;
        private Transform _hero;
        private Lantern _lantern;
        private bool _used;

        public void Configure(Transform hero, Lantern lantern)
        {
            _hero = hero;
            _lantern = lantern;
        }

        private void Update()
        {
            if (_used || _hero == null || _lantern == null || MobileInteractButton.Suppressed)
            {
                MobileInteractButton.Release(this);
                return;
            }

            Vector3 delta = _hero.position - transform.position;
            if (delta.sqrMagnitude <= Radius * Radius)
                MobileInteractButton.Request(this, "Distill oil (flask + cloth)", TryDistill);
            else
                MobileInteractButton.Release(this);
        }

        private void TryDistill()
        {
            var inv = VillageInventory.Instance;
            if (inv == null || inv.Get("ing_oil_flask") < 1 || inv.Get("tattered-cloth") < 1)
            {
                FlowTrace.Step("DungeonOil", "field still refused: need 1 Oil Flask + 1 Tattered Cloth.");
                return;
            }
            // WO-1805 Lane C: the still's yield is a rail row that ships at 40 - identical to the
            // RefillFraction const above, so an empty client_tunables table distils exactly what
            // this build has always distilled. The const stays the authority the oracle pins.
            float fraction = RefillFraction;
            Guard.Try("DungeonOil", "resolve field-still refill pct", () =>
            {
                fraction = Mathf.Clamp(
                    RemoteTunables.Int(RemoteTunables.KeyDungeonLanternStillRefillPct), 1, 100) / 100f;
            });

            if (!_lantern.AddOilFraction(fraction))
            {
                FlowTrace.Step("DungeonOil", "field still preserved: flask is already full.");
                return;
            }

            // Counts were proven before either debit, so this pair is atomic in normal play.
            inv.TryConsume("ing_oil_flask", 1);
            inv.TryConsume("tattered-cloth", 1);
            _used = true;
            MobileInteractButton.Release(this);
            int pct = Mathf.RoundToInt(fraction * 100f);
            FlowTrace.Step("DungeonOil",
                $"field still consumed 1 Oil Flask + 1 Tattered Cloth; restored {pct}% oil (station spent).");
        }

        private void OnDisable() => MobileInteractButton.Release(this);
    }
}
