// =============================================================================
// VfxAuraDifferentiationRegression [vfx-aura-diff] — locks the owner's 2026-07-24
// arcane-aura differentiation + the "Cathedral of Learning" rename + the archer
// perma-fireworks one-shot fix. Before this, the harvest NODES, the combat ARCANE
// SPIRE (ArcaneTower) and the MAGIC BUILDING (id "arcane-tower") ALL resolved to the
// SAME "Magic circle sun loop" prefab (via Arcane_Aura / Poi_NodeAura). The owner
// asked for each aura to be SUBTLER and DISTINCT. This asserts, as a build gate:
//
//   (a) the node / cathedral / arcane-spire aura keys are THREE DISTINCT values,
//       none of which is the old shared "Arcane_Aura" / "Poi_NodeAura";
//   (b) each of the three keys is a CATALOGUED key (present in the Hovl generator
//       Map or the owner's VfxManualPicks overlay — i.e. it will resolve after a
//       catalog regen);
//   (c) UpgradeStructureComplete_Aura is isLoop==false in VfxManualPicks.json (the
//       upgrade fireworks are a fire-and-forget ONE-SHOT, not a perma-loop);
//   (d) the 'arcaneTower' canon-string == "Cathedral of Learning" AND the id
//       'arcane-tower' structures-catalog displayName == "Cathedral of Learning",
//       while the id 'arcane-tower' itself is UNCHANGED (identifier untouched).
//       Checked in BOTH the Resources and StreamingAssets canonical copies.
//
// Source-lint (edit-mode, no PlayMode) — mirrors TowerWallLosRegression. Wired into
// DeNelle.Editor.DataRegression.RunAll. NEVER throws (a missing file => a listed fail).
// =============================================================================
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class VfxAuraDifferentiationRegression
    {
        // Old shared keys that all pointed at the single "Magic circle sun loop" prefab.
        private const string OldNodeKey  = "Poi_NodeAura";
        private const string OldArcKey   = "Arcane_Aura";
        private const string Expected    = "Cathedral of Learning";

        public static bool Run(out string reason)
        {
            string assets = Application.dataPath;
            string poi       = Path.Combine(assets, "_Modules/Village/Vfx/PoiCalloutSystem.cs");
            string spire     = Path.Combine(assets, "_Modules/Village/Buildings/ArcaneTower.cs");
            string factory   = Path.Combine(assets, "_Modules/Village/Catalog/StructureFactory.cs");
            string hubInject = Path.Combine(assets, "_Modules/Village/HubStructureVisualInjector.cs");
            string generator = Path.Combine(assets, "Editor/HovlVfxCatalogGenerator.cs");
            string manual    = Path.Combine(assets, "Editor/VfxManualPicks.json");
            string canonRes  = Path.Combine(assets, "Resources/Data/Canonical/canon-strings.json");
            string canonSa   = Path.Combine(assets, "StreamingAssets/Data/Canonical/canon-strings.json");
            string catRes    = Path.Combine(assets, "Resources/Data/Canonical/structures-catalog.json");
            string catSa     = Path.Combine(assets, "StreamingAssets/Data/Canonical/structures-catalog.json");

            var fails = new List<string>();

            // ── (a) three DISTINCT aura keys (none the old shared prefab keys) ─────
            string nodeKey  = Extract(poi,       @"NodeAuraKey\s*=\s*""([^""]+)""", "PoiCalloutSystem.NodeAuraKey", fails);
            string spireKey = Extract(spire,     @"ArcaneAura\.Ensure\(\s*gameObject\s*,\s*""([^""]+)""", "ArcaneTower.Awake spire aura", fails);
            string cathKey  = Extract(factory,   @"ArcaneAura\.Ensure\(\s*root\s*,\s*""([^""]+)""", "StructureFactory arcane-tower aura", fails);
            string cathKey2 = Extract(hubInject, @"ArcaneAura\.Ensure\(\s*target\.gameObject\s*,\s*""([^""]+)""", "HubStructureVisualInjector cathedral aura", fails);

            // OWNER RETAG 2026-08-06 — this check was REWRITTEN, not weakened.
            //
            // It used to BLACKLIST the name "Poi_NodeAura" for nodes. That was only ever correct
            // while that key's "Magic circle sun loop" prefab was SHARED with the Arcane Spire and
            // the Cathedral - sharing was the actual defect, the name was just its symptom.
            // WO-788 (2026-07-30) moved the Cathedral to "Cathedral_Aura" (Magic circle ELECTRO
            // loop) and the Spire to "Aura_HeartPulse" (Buff white twist), so the sun loop is now
            // UNIQUE to nodes and the blacklist had outlived its premise.
            //
            // The owner then felt-tested the 07-24 "subtle drifting motes" pick on the Seeker and
            // retagged it: "there is no vfx s i can see on nodes ... should be something more
            // meaningful like an aura so i can really see." The motes DID play - they are simply
            // imperceptible in a bright midday field. Nodes went back to Poi_NodeAura, which this
            // oracle then failed, i.e. the test was enforcing a superseded ruling against a
            // deliberate owner decision.
            //
            // So assert the INVARIANT that actually matters - the three auras must be MUTUALLY
            // DISTINCT - rather than forbidding one name. A future regression that points two of
            // them at the same key still fails, which is the whole point.
            if (nodeKey != null && spireKey != null && nodeKey == spireKey)
                fails.Add($"node and arcane-spire share the aura key '{nodeKey}' — the two must be DISTINCT");
            if (nodeKey != null && cathKey != null && nodeKey == cathKey)
                fails.Add($"node and cathedral share the aura key '{nodeKey}' — the two must be DISTINCT");
            if (spireKey != null && (spireKey == OldNodeKey || spireKey == OldArcKey))
                fails.Add($"arcane-spire aura key is still the old shared '{spireKey}' — the combat spire must have a DISTINCT aura");
            if (cathKey != null && (cathKey == OldNodeKey || cathKey == OldArcKey))
                fails.Add($"cathedral aura key is still the old shared '{cathKey}' — the magic building must have a DISTINCT aura");

            // The two Cathedral surfaces (catalog-placed + baked hub landmark) must agree.
            if (cathKey != null && cathKey2 != null && cathKey != cathKey2)
                fails.Add($"the two Cathedral aura surfaces disagree: StructureFactory='{cathKey}' vs HubStructureVisualInjector='{cathKey2}'");

            // node / cathedral / spire must be THREE distinct values.
            if (nodeKey != null && spireKey != null && cathKey != null)
            {
                var set = new HashSet<string> { nodeKey, spireKey, cathKey };
                if (set.Count != 3)
                    fails.Add($"the node/cathedral/spire auras are NOT three distinct keys " +
                              $"(node='{nodeKey}', cathedral='{cathKey}', spire='{spireKey}')");
            }

            // ── (b) each chosen key is CATALOGUED (generator Map OR manual overlay) ─
            string genTxt    = File.Exists(generator) ? File.ReadAllText(generator) : "";
            string manualTxt = File.Exists(manual)    ? File.ReadAllText(manual)    : "";
            if (string.IsNullOrEmpty(genTxt))    fails.Add($"HovlVfxCatalogGenerator.cs missing ({generator})");
            if (string.IsNullOrEmpty(manualTxt)) fails.Add($"VfxManualPicks.json missing ({manual})");
            RequireCatalogued(nodeKey,  "node",      genTxt, manualTxt, fails);
            RequireCatalogued(spireKey, "spire",     genTxt, manualTxt, fails);
            RequireCatalogued(cathKey,  "cathedral", genTxt, manualTxt, fails);

            // ── (c) UpgradeStructureComplete_Aura is a ONE-SHOT (isLoop==false) ────
            if (!string.IsNullOrEmpty(manualTxt))
            {
                var m = Regex.Match(manualTxt,
                    @"""key""\s*:\s*""UpgradeStructureComplete_Aura"".*?""isLoop""\s*:\s*(true|false)",
                    RegexOptions.Singleline);
                if (!m.Success)
                    fails.Add("UpgradeStructureComplete_Aura not found in VfxManualPicks.json (cannot verify the fireworks one-shot fix)");
                else if (m.Groups[1].Value != "false")
                    fails.Add("UpgradeStructureComplete_Aura isLoop==true — the upgrade fireworks loop forever (owner 'perma-fireworks' bug); must be false (fire-and-forget one-shot)");
            }

            // ── (d) rename to "Cathedral of Learning" in BOTH copies; id unchanged ────
            RequireCanonString(canonRes, "canon-strings (Resources)", fails);
            RequireCanonString(canonSa,  "canon-strings (StreamingAssets)", fails);
            RequireCatalogDisplayName(catRes, "structures-catalog (Resources)", fails);
            RequireCatalogDisplayName(catSa,  "structures-catalog (StreamingAssets)", fails);

            if (fails.Count == 0)
            {
                Debug.Log("VFX_AURA_DIFF_OK");
                reason = $"VFX AURA DIFF OK — node='{nodeKey}', cathedral='{cathKey}', spire='{spireKey}' " +
                         "are three distinct catalogued auras; UpgradeStructureComplete_Aura is a one-shot; " +
                         "'arcaneTower'/'arcane-tower' display == \"Cathedral of Learning\" (id unchanged)";
                return true;
            }
            reason = "vfx-aura-diff: " + string.Join("; ", fails);
            Debug.LogError("VFX_AURA_DIFF_FAIL: " + reason);
            return false;
        }

        /// <summary>Read a file and return the first regex group-1 capture, or null (+ a listed fail).</summary>
        private static string Extract(string path, string pattern, string label, List<string> fails)
        {
            if (!File.Exists(path)) { fails.Add($"{label}: source file missing ({path})"); return null; }
            var m = Regex.Match(File.ReadAllText(path), pattern);
            if (!m.Success) { fails.Add($"{label}: could not find the aura key (pattern miss)"); return null; }
            return m.Groups[1].Value;
        }

        /// <summary>Assert a chosen key literal appears in the generator Map or the manual overlay.</summary>
        private static void RequireCatalogued(string key, string label, string genTxt, string manualTxt, List<string> fails)
        {
            if (string.IsNullOrEmpty(key)) return;   // already reported as a miss above
            string quoted = "\"" + key + "\"";
            if (!genTxt.Contains(quoted) && !manualTxt.Contains(quoted))
                fails.Add($"{label} aura key '{key}' is NOT catalogued (absent from HovlVfxCatalogGenerator Map AND VfxManualPicks) — it will no-op at runtime");
        }

        /// <summary>Assert canon-strings maps 'arcaneTower' to "Cathedral of Learning".</summary>
        private static void RequireCanonString(string path, string label, List<string> fails)
        {
            if (!File.Exists(path)) { fails.Add($"{label}: file missing ({path})"); return; }
            var m = Regex.Match(File.ReadAllText(path), @"""arcaneTower""\s*:\s*""([^""]+)""");
            if (!m.Success) fails.Add($"{label}: no 'arcaneTower' canon-string key found");
            else if (m.Groups[1].Value != Expected)
                fails.Add($"{label}: 'arcaneTower' == \"{m.Groups[1].Value}\", expected \"{Expected}\"");
        }

        /// <summary>Assert the id 'arcane-tower' entry exists (id untouched) and its displayName is
        /// "Cathedral of Learning".</summary>
        private static void RequireCatalogDisplayName(string path, string label, List<string> fails)
        {
            if (!File.Exists(path)) { fails.Add($"{label}: file missing ({path})"); return; }
            string txt = File.ReadAllText(path);
            if (!txt.Contains("\"id\": \"arcane-tower\""))
            {
                fails.Add($"{label}: id \"arcane-tower\" not found — the identifier must remain unchanged");
                return;
            }
            // Grab the displayName belonging to the arcane-tower row.
            // ⚠ WO-2005 (2026-09-06): this used to require displayName to be the VERY NEXT key after
            // id (`"id"\s*:\s*"arcane-tower"\s*,\s*"displayName"`). That asserted FIELD ORDER, which is
            // not an invariant of anything — it broke the moment the Manage-redesign lane inserted
            // `manageFilters` and `manageArtKey` between them, on a change that could not possibly
            // affect what this suite is about. The intent was always "the arcane-tower row displays
            // Cathedral of Learning". It now allows intervening keys but stays inside the SAME row by
            // refusing to cross a `"id":` boundary, so it cannot drift onto a neighbour's displayName.
            var m = Regex.Match(txt,
                @"""id""\s*:\s*""arcane-tower""\s*,(?:(?!""id""\s*:).)*?""displayName""\s*:\s*""([^""]+)""",
                RegexOptions.Singleline);
            if (!m.Success)
                fails.Add($"{label}: could not read the displayName following id \"arcane-tower\"");
            else if (m.Groups[1].Value != Expected)
                fails.Add($"{label}: id 'arcane-tower' displayName == \"{m.Groups[1].Value}\", expected \"{Expected}\"");
        }
    }
}
