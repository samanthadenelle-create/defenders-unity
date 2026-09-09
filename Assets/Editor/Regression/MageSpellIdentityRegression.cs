using System;
using System.Collections.Generic;
using System.IO;
using DeNelle.Village;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>Pins distinct learned-Mage animation and cast-VFX identities.</summary>
    public static class MageSpellIdentityRegression
    {
        private static readonly (string id, string anim, string vfx)[] Expected =
        {
            ("mage.shell", "shell", "ShieldBuff_Cast"),
            ("mage.drain", "drain", "EnemyCast_Cast"),
            ("mage.poison", "poison", "Posion_Cast"),
            ("mage.frost-nova", "frost", "Freezing_Projectile"),
            ("mage.manaweave", "manaweave", "EnhamcingBuff_Cast"),
            ("mage.void-rift", "voidrift", "RangedSpell-Powerful(Longcast)_Cast"),
            ("mage.blink", "blink", "Dash_Blink"),
            ("mage.cataclysm", "cataclysm", "SpecialAbilityMage_Cast"),
            ("mage.thunder", "thunder", "Thunderbolt_Cast"),
            ("mage.heal", "mend", "NoneMageHealingCast_Cast"),
            ("mage.meteor", "meteor", "MageMeoteorAOE_Cast"),
            ("mage.siphon", "siphon", "Arcane_Cast"),
            ("mage.wither", "wither", "PosionCloud_Cast"),
        };

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var seenAnim = new HashSet<string>(StringComparer.Ordinal);
            var seenVfx = new HashSet<string>(StringComparer.Ordinal);

            foreach (var expected in Expected)
            {
                AbilityDef def = AbilityCatalog.FindById(expected.id);
                if (def == null)
                {
                    failures.Add(expected.id + " is missing");
                    continue;
                }
                if (!string.Equals(def.CastAnim, expected.anim, StringComparison.Ordinal))
                    failures.Add($"{expected.id} castAnim='{def.CastAnim}' expected '{expected.anim}'");
                if (!string.Equals(def.VfxCast, expected.vfx, StringComparison.Ordinal))
                    failures.Add($"{expected.id} vfxCast='{def.VfxCast}' expected '{expected.vfx}'");
                if (!seenAnim.Add(expected.anim)) failures.Add("duplicate castAnim " + expected.anim);
                if (!seenVfx.Add(expected.vfx)) failures.Add("duplicate vfxCast " + expected.vfx);
            }

            string heroPath = Path.Combine(Application.dataPath, "_Modules/Village/Hero/HeroAbilities.cs");
            string factoryPath = Path.Combine(Application.dataPath, "Editor/HeroAnimatorFactory.cs");
            string hero = File.Exists(heroPath) ? File.ReadAllText(heroPath) : string.Empty;
            string factory = File.Exists(factoryPath) ? File.ReadAllText(factoryPath) : string.Empty;
            if (hero.Contains("if (_animator != null && _hasCastParam) _animator.SetTrigger(AnimCast);"))
                failures.Add("raw generic Cast still fires before ActorAnimator variant selection");
            if (!hero.Contains("PlayCastVfxKey(def, origin, animVariant);"))
                failures.Add("cast VFX still follows hotbar slot rather than resolved ability identity");
            if (!factory.Contains("for (int v = 1; v < spellClips.Length; v++)"))
                failures.Add("animator factory still caps learned-spell states at Q/W/E/R");

            reason = failures.Count == 0
                ? $"MAGE_SPELL_IDENTITY_OK {Expected.Length} non-primary spells have distinct animations and cast VFX"
                : "MAGE_SPELL_IDENTITY_FAIL x" + failures.Count + " :: " + string.Join(" | ", failures);
            return failures.Count == 0;
        }
    }
}
