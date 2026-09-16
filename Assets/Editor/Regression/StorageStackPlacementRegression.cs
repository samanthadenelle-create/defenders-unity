using System;
using System.Collections.Generic;
using DeNelle.Village.Buildings.Progression;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class StorageStackPlacementRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            // Owner 2026-09-13 replaced GenericContainer with the exact saved pallet.
            // A non-centred source pivot exercises mesh-bottom contact, not pivot guessing.
            var deck = new Bounds(new Vector3(3, .15f, -2), new Vector3(1.4f, .3f, 1.8f));
            var prop = new Bounds(new Vector3(2, 4, -3), new Vector3(2, .8f, 1));
            for (int i = 0; i < 14; i++)
            {
                if (!StorageStackView.TryDeckSeat(deck, prop, i, out var position, out float scale))
                { failures.Add("valid deck refused"); continue; }
                var seated = new Bounds(position + prop.center * scale, prop.size * scale);
                if (seated.min.x < deck.min.x || seated.max.x > deck.max.x ||
                    seated.min.z < deck.min.z || seated.max.z > deck.max.z)
                    failures.Add("fill footprint escaped pallet at " + i);
                if (Math.Abs(seated.min.y - (deck.max.y + (i / 4) * seated.size.y)) > .0001f)
                    failures.Add("mesh-bottom contact drift at " + i);
            }
            if (StorageStackView.TryDeckSeat(deck, new Bounds(), 0, out _, out _))
                failures.Add("empty prop geometry accepted");

            reason = failures.Count == 0
                ? "STORAGE_STACK_PLACEMENT_OK pallet contents stay on deck with mesh-bottom contact"
                : "STORAGE_STACK_PLACEMENT_FAIL x" + failures.Count + " :: " + string.Join(" | ", failures);
            return failures.Count == 0;
        }
    }
}
