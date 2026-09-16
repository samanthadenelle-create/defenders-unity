// =============================================================================
// Core State — SaveSchema.Validate tests (EditMode)
// -----------------------------------------------------------------------------
// core-state-port.md §2.1 / §2.5: SaveSchema.Validate is the C# port of the Zod
// persistedStateSchema.safeParse. It must:
//   - REJECT NaN / +/-Infinity numerics (the Number.isFinite refine), reporting
//     the first bad field path;
//   - apply the NonNegInt clamp  -> max(0, floor(n))     (resources, currencies,
//                                                         counts, int arrays);
//   - apply the FiniteInt clamp  -> floor(n) (may be -ve) (timestamps);
//   - finite-check volumes / joystick sensitivity but NOT clamp them.
//
// These tests exercise the clamp helpers directly and through Validate.
// =============================================================================

using System.Collections.Generic;
using NUnit.Framework;
using DeNelle.Core.State;

namespace DeNelle.Core.Tests
{
    [TestFixture]
    public class SaveSchemaValidateTest
    {
        // =====================================================================
        //  NonNegInt clamp — max(0, floor(n))
        // =====================================================================

        [Test]
        public void non_neg_int_floors_a_positive_fraction()
        {
            Assert.That(SaveSchema.NonNegInt(7.9, "x"), Is.EqualTo(7));
        }

        [Test]
        public void non_neg_int_clamps_a_negative_value_to_zero()
        {
            Assert.That(SaveSchema.NonNegInt(-12.0, "x"), Is.EqualTo(0));
            Assert.That(SaveSchema.NonNegInt(-0.5, "x"), Is.EqualTo(0));
        }

        [Test]
        public void non_neg_int_rejects_nan()
        {
            Assert.That(() => SaveSchema.NonNegInt(double.NaN, "voidshards"),
                Throws.TypeOf<SaveValidationException>());
        }

        [Test]
        public void non_neg_int_rejects_infinity()
        {
            Assert.That(() => SaveSchema.NonNegInt(double.PositiveInfinity, "stone"),
                Throws.TypeOf<SaveValidationException>());
            Assert.That(() => SaveSchema.NonNegInt(double.NegativeInfinity, "stone"),
                Throws.TypeOf<SaveValidationException>());
        }

        // =====================================================================
        //  FiniteInt clamp — floor(n), may be negative
        // =====================================================================

        [Test]
        public void finite_int_floors_and_keeps_negatives()
        {
            Assert.That(SaveSchema.FiniteInt(9.99, "t"), Is.EqualTo(9L));
            Assert.That(SaveSchema.FiniteInt(-3.2, "t"), Is.EqualTo(-4L),
                "FiniteInt floors toward negative infinity and keeps the sign.");
        }

        [Test]
        public void finite_int_rejects_non_finite_values()
        {
            Assert.That(() => SaveSchema.FiniteInt(double.NaN, "sentAt"),
                Throws.TypeOf<SaveValidationException>());
            Assert.That(() => SaveSchema.FiniteInt(double.PositiveInfinity, "sentAt"),
                Throws.TypeOf<SaveValidationException>());
        }

        // =====================================================================
        //  RequireFinite
        // =====================================================================

        [Test]
        public void require_finite_passes_through_a_finite_number()
        {
            Assert.That(SaveSchema.RequireFinite(42.5, "v"), Is.EqualTo(42.5));
        }

        [Test]
        public void require_finite_rejects_nan_and_infinity()
        {
            Assert.That(() => SaveSchema.RequireFinite(double.NaN, "musicVolume"),
                Throws.TypeOf<SaveValidationException>());
            Assert.That(() => SaveSchema.RequireFinite(double.PositiveInfinity, "musicVolume"),
                Throws.TypeOf<SaveValidationException>());
        }

        // =====================================================================
        //  Validate — happy path
        // =====================================================================

        [Test]
        public void validate_accepts_a_well_formed_payload()
        {
            var result = SaveSchema.Validate(TestSupport.MakeRichState());
            Assert.That(result.Ok, Is.True, result.Message);
            Assert.That(result.Data, Is.Not.Null);
        }

        [Test]
        public void validate_accepts_an_empty_partial_payload()
        {
            // Every field nullable — a near-empty save must still validate.
            var result = SaveSchema.Validate(new SaveSchema.PersistedState());
            Assert.That(result.Ok, Is.True, "an empty partial payload mirrors Zod .partial().");
        }

        [Test]
        public void validate_rejects_a_null_payload()
        {
            var result = SaveSchema.Validate(null);
            Assert.That(result.Ok, Is.False);
            Assert.That(result.FieldPath, Is.EqualTo("state"));
        }

        // =====================================================================
        //  Validate — rejects NaN / Infinity, reports the field path
        // =====================================================================

        [Test]
        public void validate_rejects_nan_best_wave()
        {
            var raw = new SaveSchema.PersistedState { BestWave = double.NaN };
            var result = SaveSchema.Validate(raw);

            Assert.That(result.Ok, Is.False, "a NaN bestWave must be rejected.");
            Assert.That(result.FieldPath, Is.EqualTo("bestWave"));
            Assert.That(result.Message, Does.Contain("bestWave"));
        }

        [Test]
        public void validate_rejects_infinity_voidshards()
        {
            var raw = new SaveSchema.PersistedState { Voidshards = double.PositiveInfinity };
            var result = SaveSchema.Validate(raw);

            Assert.That(result.Ok, Is.False);
            Assert.That(result.FieldPath, Is.EqualTo("voidshards"));
        }

        [Test]
        public void validate_rejects_a_nan_int_array_entry()
        {
            // PetBonds is a double[] on the wire — a NaN entry must be rejected
            // with the offending index in the field path.
            var raw = new SaveSchema.PersistedState
            {
                PetBonds = new List<double> { 0, double.NaN, 0 },
            };
            var result = SaveSchema.Validate(raw);

            Assert.That(result.Ok, Is.False, "a NaN petBonds entry must be rejected.");
            Assert.That(result.FieldPath, Does.Contain("petBonds"));
        }

        [Test]
        public void validate_rejects_nan_inside_a_pending_build_finish_at()
        {
            var raw = new SaveSchema.PersistedState
            {
                PendingBuilds = new List<PendingTowerBuild>
                {
                    new PendingTowerBuild { Slot = 0, Ability = 1, FinishAt = double.NaN },
                },
            };
            var result = SaveSchema.Validate(raw);

            Assert.That(result.Ok, Is.False);
            Assert.That(result.FieldPath, Does.Contain("pendingBuilds.0.finishAt"));
        }

        [Test]
        public void validate_rejects_nan_volume()
        {
            var raw = new SaveSchema.PersistedState { MusicVolume = double.NaN };
            var result = SaveSchema.Validate(raw);

            Assert.That(result.Ok, Is.False, "a NaN musicVolume must be rejected.");
            Assert.That(result.FieldPath, Is.EqualTo("musicVolume"));
        }

        // =====================================================================
        //  Validate — applies the NonNegInt / FiniteInt clamps in place
        // =====================================================================

        [Test]
        public void validate_clamps_a_negative_currency_to_zero()
        {
            var raw = new SaveSchema.PersistedState { Stone = -50, Voidshards = -1 };
            var result = SaveSchema.Validate(raw);

            Assert.That(result.Ok, Is.True);
            Assert.That((int)result.Data.Stone.Value, Is.EqualTo(0),
                "a negative stone clamps to 0 (NonNegInt).");
            Assert.That((int)result.Data.Voidshards.Value, Is.EqualTo(0));
        }

        [Test]
        public void validate_floors_a_fractional_currency()
        {
            var raw = new SaveSchema.PersistedState { Iron = 12.8, BestWave = 4.6 };
            var result = SaveSchema.Validate(raw);

            Assert.That(result.Ok, Is.True);
            Assert.That((int)result.Data.Iron.Value, Is.EqualTo(12), "NonNegInt floors iron.");
            Assert.That((int)result.Data.BestWave.Value, Is.EqualTo(4));
        }

        [Test]
        public void validate_clamps_resource_balance_fields()
        {
            var raw = new SaveSchema.PersistedState
            {
                Resources = new ResourceBalance { Crystals = -10, Stone = 80, Coins = 15 },
            };
            var result = SaveSchema.Validate(raw);

            Assert.That(result.Ok, Is.True);
            Assert.That(result.Data.Resources.Value.Crystals, Is.EqualTo(0),
                "resources.crystals clamps via NonNegInt.");
        }

        [Test]
        public void validate_clamps_negative_entries_in_int_arrays()
        {
            var raw = new SaveSchema.PersistedState
            {
                PetBonds = new List<double> { -3, 2, -1 },
                Towers = new List<double> { 5.9, -2, 0 },
            };
            var result = SaveSchema.Validate(raw);

            Assert.That(result.Ok, Is.True);
            Assert.That(result.Data.PetBonds, Is.EqualTo(new List<double> { 0, 2, 0 }),
                "petBonds entries clamp via NonNegInt.");
            Assert.That(result.Data.Towers, Is.EqualTo(new List<double> { 5, 0, 0 }),
                "towers entries floor + clamp via NonNegInt.");
        }

        [Test]
        public void validate_floors_a_fractional_pending_build_finish_at()
        {
            var raw = new SaveSchema.PersistedState
            {
                PendingBuilds = new List<PendingTowerBuild>
                {
                    new PendingTowerBuild { Slot = 2, Ability = 1, FinishAt = 1716000000123.7 },
                },
            };
            var result = SaveSchema.Validate(raw);

            Assert.That(result.Ok, Is.True);
            var pb = result.Data.PendingBuilds[0];
            Assert.That(pb.Slot, Is.EqualTo(2), "slot is preserved (NonNegInt no-op on a valid int).");
            Assert.That(pb.Ability, Is.EqualTo(1));
            Assert.That(pb.FinishAt, Is.EqualTo(1716000000123.0),
                "finishAt floors via FiniteInt.");
        }

        [Test]
        public void validate_leaves_volumes_unclamped_when_out_of_the_0_100_band()
        {
            // The React schema only finite-checks volumes — it does NOT clamp them.
            var raw = new SaveSchema.PersistedState { MusicVolume = 250, SfxVolume = -20 };
            var result = SaveSchema.Validate(raw);

            Assert.That(result.Ok, Is.True);
            Assert.That(result.Data.MusicVolume.Value, Is.EqualTo(250.0),
                "an out-of-band volume is accepted as-is (no clamp on load).");
            Assert.That(result.Data.SfxVolume.Value, Is.EqualTo(-20.0));
        }

        [Test]
        public void validate_clamps_pet_level_and_xp()
        {
            var raw = new SaveSchema.PersistedState
            {
                Pets = new List<PetData>
                {
                    new PetData { Id = "p1", Level = -4, Xp = -100 },
                },
            };
            var result = SaveSchema.Validate(raw);

            Assert.That(result.Ok, Is.True);
            Assert.That(result.Data.Pets[0].Level, Is.EqualTo(0), "pet.level clamps via NonNegInt.");
            Assert.That(result.Data.Pets[0].Xp, Is.EqualTo(0));
        }
    }
}
