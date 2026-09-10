// =============================================================================
// HudUiRegression — headless HUD/UI defect-class gate (data + logic only).
// -----------------------------------------------------------------------------
// Owner directive 2026-07-12: the device screenshot showed TMP tofu boxes (□)
// from non-ASCII glyphs (⟲/⟳-class) baked into code-built UI strings. This
// oracle locks that WHOLE defect class at gate time, plus three sibling
// UI-contract classes, without opening a scene (INSTRUMENTATION_STANDARD.md §4:
// real object in → assert real response → one marker).
//
// CHECKS
//   1. THE TOFU ORACLE — scan first-party runtime source (Assets/_Modules/**,
//      excluding Editor/, Tests/, DevTools/) for string literals that feed UI
//      text (only files referencing TMPro / UnityEngine.UI / UIElements /
//      ElarionUiKit). Every non-ASCII character found is verified against the
//      REAL font the kit ships with (ElarionUiKit.EnsureFont resolution order:
//      TMP_Settings.defaultFontAsset ?? Resources "Fonts & Materials/
//      LiberationSans SDF", incl. fallback fonts + dynamic-atlas source). A
//      character the font cannot render IS the □ the owner saw — FAIL naming
//      every file:line. No hand-maintained char allowlist: the font itself is
//      the truth (chars it has pass, chars it lacks fail), so ✦/×/— pass or
//      fail on EVIDENCE, never on a guess. Astral-plane chars (emoji 🏰 etc.)
//      always fail — no shipped UI font covers them.
//   2. UIDocument CONSTRUCTION FENCE — UXML/UIDocument surfaces are the known
//      does-not-work-in-builds path (CLAUDE.md §8 "UXML in builds does NOT
//      work — always use code-built UI"). The existing UIDocument surfaces are
//      baselined below; a NEW gameplay file that constructs (or RequireComponent-
//      declares) a UIDocument outside the baseline FAILS naming file:line.
//   3. KIT CONFORMANCE — reflection over DeNelle.Core: every load-bearing
//      public ElarionUiKit builder the HUD composes with must exist, and the
//      kit's canonical rarity glyphs (colorblind-owner shape channel) must be
//      non-empty AND font-renderable (a tofu'd rarity glyph deletes the only
//      non-color rarity signal).
//   4. RESOURCES PATH RESOLUTION — every string-literal Resources.Load path in
//      runtime code must resolve to a real asset under an Assets/**/Resources/
//      root (the wiard.jpg-typo class: referenced-but-missing silently blanks
//      an icon). Existing misses are baselined as tracked debt; a NEW missing
//      path FAILS naming file + path.
//   5. SAFE-AREA CORNER (WO-868) — the screen-anchored "Connect Wallet" /
//      "Sign in with Pi" corner button must stay INSIDE Screen.safeArea. Proven
//      defect (docs/ui-review/2026-08-04-seeker/01-title-screen.png, a 1:1 Seeker
//      screencap at 2670x1200): the button measured 300 x 112 px with its top
//      edge at y = -10 — CLIPPED off the top of the screen — because the holder
//      used a raw 16-px inset (~6 dp) and a 60-px height that the kit touch floor
//      then grew symmetrically by 26 px per side. This case pins both halves
//      headlessly: (a) SafeAreaInset's PURE math keeps the resulting rect inside
//      the safe area at 2340x1080 (cutout AND no-cutout) and at the captured
//      2670x1200, and (b) PiSignInController still routes through
//      SafeAreaInset.ApplyTopRight and still sizes its holder at the touch floor.
//   6. COMBAT HUD COMPOSITION (WO-867) — the 2026-08-04 Seeker review's combat-HUD
//      defects, each pinned to the data that PROVED it: the mirrored Blink plates
//      are whole atlas PAGES (so their measured crop rects need their PNG size +
//      spriteMode pinned); TargetNameplate.prefab authors every child at an
//      absolute offset for a 480x130 root plus an always-on ragged BossTarget
//      frame (so the composed-plate child names, the ComposeTargetPlate call and
//      the one cross-file anchor it derives from are pinned); CastBar1.prefab's
//      root sprite GUID is dangling and paints a WHITE quad unguarded; and the
//      right-edge touch sizes are RECOMPUTED from HudAreasHost/HudKitController's
//      own numbers at 2340x1080 rather than from a copied figure.
//   7. TOWN RESOURCE RAIL (WO-1221) — expanded Wood/Iron/Stone/Crystals must be
//      children of the occupancy-live gold chip, raised by SetActive from the tap,
//      measured through UiSurfaceProbe AFTER layout settles. Hollow "handler ran" /
//      "opener live=True" is a FAIL. Inactive or zero-size stack is a FAIL.
//
// ⚠ WHAT EACH CHECK ACTUALLY DOES (WO-1494, 2026-09-07 — classified by reading every
// body, not by trusting this header, which used to call the whole file "SOURCE-LINT
// family" flat. That was true when it was written and is now false in BOTH directions:
// two checks instantiate real HUD objects, and several of the prose descriptions above
// are written in the RUNTIME's voice — they say what the code under test is supposed to
// DO, and the check only proves the code saying so is still there).
//
//   MEASURED (a real object/asset in, its real answer read back — each verified by
//   reading the body, 2026-09-07, not by trusting the prose above it):
//     3  kit conformance — `typeof(ElarionUiKit).GetMethods(...)` over the live type
//                      (:470-473), plus the font oracle on the rarity glyphs.
//     5a safe-area corner — SafeAreaInset.TopRightScreenRect / .EdgeMarginPx, the real
//                      math, run at three surfaces (:623, :650).
//     9  gear-drawer clearance   — HudAreasHost.Create() instantiated, mount anchors read
//                      back, intersected against the drawer band the kit resolves.
//    10  stack-badge containment — the badge and medallion rects read off built objects;
//                      degenerate rects are a NAMED skip, never a pass.
//
//   HYBRID (a text scan FINDS the candidates; a real artefact returns the VERDICT):
//     1  tofu        — the scan for suspect glyphs is text; the verdict on each one is
//                      the shipped TMP font asset's own HasCharacter (with fallbacks and
//                      the dynamic-atlas source face).
//     4  Resources paths — the paths are Regexed out of source, then resolved against
//                      the real asset folders on disk.
//     6  combat-HUD composition — 6a reads each atlas page's REAL pixel size out of the
//                      PNG header (TryReadPngSize, :766) and its spriteMode out of the
//                      .meta; the rest of the case is source text over the kit files.
//                      No frame is rendered and no rect is laid out.
//
//   SOURCE LINT (text match / brace-matched method bodies over .cs; proves a call site,
//   guard or literal is present, absent or ordered — cannot see the built tree):
//     2  UIDocument fence, 5b PiSignInController routing, 7 resource-rail raise (it pins
//     that the runtime's UiSurfaceProbe verify still EXISTS — the probe measures at
//     runtime, this check does not), 8 harvest-chip clearance (authored-anchor
//     ARITHMETIC on source-parsed values, as its own banner says).
//
// Runs no PlayMode. Never throws.
// Marker: HUDUI_OK (Debug.Log) / HUDUI_FAIL (Debug.LogError → break-log.jsonl).
//
// FALSE-POSITIVE MITIGATIONS (deliberate, documented):
//   • Tofu scan only inside files that reference a UI text stack; skips comment
//     lines, attribute lines ([Header]/[Tooltip] render in the Inspector, not
//     TMP), log/exception lines (Debug.Log/FlowTrace/Guard/throw text goes to
//     the console, not a label), and trailing // comments on code lines.
//   • The font — with fallbacks and its dynamic-atlas source face — decides
//     tofu, not a char list; a glyph the shipped font genuinely has never fails.
//   • Checks 2 & 4 are BASELINE-vs-NEW (UiObsidianConformanceRegression
//     precedent): today's verified state is grandfathered as tracked debt so a
//     pre-existing miss can never false-fail the gate; only a NEW offender
//     fails. Resolved baseline entries surface as refresh notes, never failures.
//   • Resources matching is case-insensitive and extension-free (mirrors
//     Resources.Load semantics); non-literal (concatenated/variable) paths are
//     skipped — not assertable from source.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Core.UI;

namespace DeNelle.Editor
{
    public static class HudUiRegression
    {
        // =====================================================================
        // BASELINES (verified against the working tree 2026-07-12 — refresh in
        // the same breath as intentionally resolving/adding an entry).
        // =====================================================================

        // --- Check 2: sanctioned UIDocument surfaces (rel path, forward slash).
        // Constructing/declaring a UIDocument in any OTHER runtime file = FAIL.
        private static readonly HashSet<string> UiDocumentBaseline = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Assets/_Modules/Web3/JupiterSwapBootstrap.cs",
            "Assets/_Modules/Web3/JupiterSwapService.cs",
            "Assets/_Modules/Wallet/WalletConnectDialog.cs",
            "Assets/_Modules/Settings/MusicToggleBootstrap.cs",
            "Assets/_Modules/DevTools/DevBootstrap.cs",
            "Assets/_Modules/DevTools/DevPanelController.cs",
            "Assets/_Modules/Dungeons/UI/CraftingPanelController.cs",
            "Assets/_Modules/Dungeons/UI/DungeonHudController.cs",
            "Assets/_Modules/HUD/AdminOverlay.cs",
            "Assets/_Modules/Onboarding/SplashLoading.cs",
            "Assets/_Modules/Onboarding/PetSelectController.cs",
            "Assets/_Modules/Village/BuildMode/BuildStructureInfoPanel.cs",
            "Assets/_Modules/Village/Arena/ArenaDefensePaletteUI.cs",
            "Assets/_Modules/Village/Buildings/UI/TowerUpgradeButton.cs",
            "Assets/_Modules/Village/Buildings/UI/LevelUpSkillPopupBootstrap.cs",
            "Assets/_Modules/Village/Buildings/UI/LevelUpSkillPopup.cs",
            "Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs",
            // TalentTreePanel.cs REMOVED 2026-09-06 (WO-1430): the file is DELETED. It was a UI-Toolkit
            // screen superseded by HeroSkillTreePanelMvvm - DialogueCommandSink re-pointed "OpenTalents"
            // to PanelId.HeroSkillTree and removed the legacy route, so nothing ever opened it. Its own
            // header still carried an INTEGRATOR NOTE saying to wire the button; that was never done.
            // Left here it would have reported as a baseline entry that "no longer constructs UIDocument".
            "Assets/_Modules/Village/UI/SeatingEditorOverlay.cs",
            "Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs",
            "Assets/_Modules/Village/Tutorial/TutorialHudOverlay.cs",
            // WO-971 (2026-08-10): PetIntroduction.cs removed from this list because the
            // file is DELETED. It was a legacy-FTUE-only screen whose sole consumer was
            // TutorialDirector, and the owner ruled the original tutorial out entirely
            // ("remove the original", "only the new wolf one stays").
        };

        // --- Check 4: referenced-but-missing Resources paths grandfathered as
        // tracked debt (each already has a runtime null-fallback or is a known
        // staged-art alias, e.g. ElarionUiKit's 'HudIcons/Wizard/wiard' typo
        // fallback whose primary 'wizard' resolves). A NEW missing path = FAIL.
        private static readonly HashSet<string> MissingResourceBaseline = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Arena/Backdrops/outerworld_backdrop",
            // 2026-08-14: "Dungeon/Exit/dungeon_texture" REMOVED from this baseline — it was a
            // STALE ASSERTION. The entry claimed the path was an unstaged player-build mirror of a
            // gitignored KayKit material, but commit 64ebf6658 ("the exit portal stands at
            // hero-scale") COMMITTED the real asset at Assets/Resources/Dungeon/Exit/dungeon_texture.png
            // (git-tracked, alongside pillar_decorated.fbx + wall_arched.fbx). A baseline that
            // excuses the absence of something that now exists hides the next real miss on that
            // path, so it goes. If the asset is ever removed again, this check FAILS loudly —
            // which is the correct behaviour, not a reason to re-add the excuse.
            "Audio/Ambient/Heartwood_Critical",
            "Audio/Ambient/Heartwood_Healthy",
            "Audio/Ambient/Heartwood_Strained",
            "Audio/Sfx/Heart_Fall",
            "Audio/Sfx/Heart_Hit",
            "Burst_rings",
            "DevPanelSettings",
            "Env/Fish",
            "HudIcons/Wizard/wiard",
            "Pets/Pet",
            "Pets/PetIdle",
            "RpgUi/params/widget-params",
            "Sfx/BuildDenied",
            "Sfx/EnemyHit",
            "Sfx/LevelUp",
            "Sfx/PetHarvest",
            "Sfx/TowerFire",
            "Sfx/TowerPlace",
            "Sfx/WaveStart",
            "Tech hud elements/Sprites/GreenUielements/Buttons/Button 1",
            "Tech hud elements/Sprites/GreenUielements/Icons/Icon 5",
            "Tech hud elements/Sprites/GreenUielements/Loading bar/Loading bar",
            "Tech hud elements/Sprites/GreenUielements/Shield/Shield 1",
            "Tech hud elements/Sprites/Healing Tabs/H1",
            "Tech hud elements/Sprites/Healing Tabs/H3",
            "Tech hud elements/Sprites/Healing Tabs/H4",
            "Tech hud elements/Sprites/Healing Tabs/H5",
            "Tech hud elements/Sprites/Loading 1/Loading 1",
            "Tech hud elements/Sprites/Menu Bars/Menu Bar 1",
            "Tech hud elements/Sprites/Play buttons/button 3",
            "Tech hud elements/Sprites/Profile tabs/P1/bg.png",
            "Tech hud elements/Sprites/Profile tabs/P1/fill.png",
            "Tech hud elements/Sprites/Profile tabs/P2/fill.png",
            "Tech hud elements/Sprites/Profile tabs/P3/fill.png",
            "Tech hud elements/Sprites/Sword icons/Sword icons",
            "UI/menu_bg",
            "UI/panel_bg",
            "VFX/Burst_rings",
        };

        // --- Check 3: load-bearing public static ElarionUiKit builders the HUD
        // composes with (verified from ElarionUiKit*.cs partials 2026-07-12).
        private static readonly string[] RequiredKitBuilders =
        {
            "BuildObsidianPanel", "BuildObsidianModal", "BuildConfirmModal",
            "ObsidianCloseButton", "ToastCard", "BuildModalCanvas", "Scrim",
            "Panel", "Header", "Button", "Label", "Rule", "Bar", "Slot", "Card",
            "Portrait", "EnsureFont", "RarityColor", "RarityGlyph",
        };

        // --- Check 1: a runtime file only enters the tofu scan when it touches
        // a UI text stack (mirror of UiObsidianConformanceRegression's KitToken
        // heuristic — a pure-logic file's strings never reach a label).
        private static readonly string[] UiStackTokens =
        {
            "TMPro", "TextMeshPro", "UnityEngine.UI", "UnityEngine.UIElements", "ElarionUiKit",
        };

        private static readonly Regex StringLiteralRx = new Regex("\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled);
        private static readonly Regex UnicodeEscapeRx = new Regex(@"\\u([0-9A-Fa-f]{4})", RegexOptions.Compiled);
        private static readonly Regex ResourcesLoadRx = new Regex(
            "Resources\\s*\\.\\s*Load(?:All)?\\s*(?:<[^>]+>)?\\s*\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\"\\s*[,)]",
            RegexOptions.Compiled);
        private static readonly Regex[] UiDocumentSmells =
        {
            new Regex(@"AddComponent\s*<\s*UIDocument\s*>", RegexOptions.Compiled),
            new Regex(@"new\s+GameObject\s*\([^;]*typeof\s*\(\s*UIDocument\s*\)", RegexOptions.Compiled),
            new Regex(@"RequireComponent\s*\(\s*typeof\s*\(\s*UIDocument\s*\)", RegexOptions.Compiled),
        };

        /// <summary>
        /// Headless HUD/UI defect-class gate: tofu glyphs, UIDocument fence,
        /// kit conformance, Resources path resolution. Logs HUDUI_OK /
        /// HUDUI_FAIL (LogError) and returns the same verdict. Never throws.
        /// </summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                string modulesDir = Path.Combine(Application.dataPath, "_Modules");
                if (!Directory.Exists(modulesDir))
                {
                    bool skipped = DeNelle.Editor.Regression.RegressionOutcome.Skip(out reason,
                        "HUD UI", "Assets/_Modules not found at " + modulesDir);
                    Debug.LogWarning(reason);
                    return skipped;
                }

                CheckTofu(modulesDir, failures, notes);
                CheckUiDocumentFence(modulesDir, failures, notes);
                CheckKitConformance(failures, notes);
                CheckResourcePaths(modulesDir, failures, notes);
                CheckSafeAreaCorner(failures, notes);
                CheckCombatHudComposition(failures, notes);
                CheckResourceRailRaise(modulesDir, failures, notes);
                CheckHarvestChipClearsResourcePanel(modulesDir, failures, notes);   // WO-1435
                CheckGearDrawerClearsNeighbours(failures, notes);                   // WO-1465
                CheckStackBadgeInsideMedallion(failures, notes);                    // WO-1468
            }
            catch (Exception ex)
            {
                // A broken oracle must never masquerade as a broken game — degrade
                // loudly (warning, not FAIL) and let the run pass.
                reason = "HUDUI SKIPPED — oracle exception: " + ex.GetType().Name + " " + ex.Message;
                Debug.LogWarning(reason);
                return true;
            }

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join(" ; ", notes.ToArray()) + "]" : "";
            if (failures.Count == 0)
            {
                // ⚠ WO-1494: the verdict names WHICH checks measured and which only read text.
                // A flat "all green" over a mixed suite is how a text match came to read as a
                // rendered pass; the classification lives in the marker line so a green log is
                // self-describing without opening this file.
                reason = "HUDUI_OK — MEASURED (real object in, real answer back): kit conformance by " +
                         "reflection, safe-area math, gear-drawer clearance and stack-badge containment " +
                         "off a live HudAreasHost. HYBRID (text finds it, a real artefact judges it): " +
                         "tofu glyphs vs the shipped font, Resources paths vs disk, atlas-page pixel " +
                         "sizes vs the PNG headers. SOURCE LINT (text only, cannot see the built tree): " +
                         "UIDocument fence, PiSignIn routing, resource-rail raise, harvest-chip " +
                         "clearance (authored-anchor arithmetic). All green" + noteStr;
                Debug.Log(reason);
                return true;
            }

            reason = "HUDUI_FAIL: " + failures.Count + " failure(s)\n - " +
                     string.Join("\n - ", failures.ToArray()) + noteStr;
            Debug.LogError(reason);
            return false;
        }

        /// <summary>Batchmode entry point (run-unity-method.ps1 -Method DeNelle.Editor.HudUiRegression.RunStandalone).</summary>
        public static void RunStandalone()
        {
            string reason;
            Run(out reason); // Run() already emits the HUDUI_OK / HUDUI_FAIL marker
        }

        // =====================================================================
        // CHECK 1 — THE TOFU ORACLE
        // =====================================================================
        private static void CheckTofu(string modulesDir, List<string> failures, List<string> notes)
        {
            var oracle = FontOracle.Resolve(notes);
            if (!oracle.Available)
            {
                // No resolvable UI font is itself a shipping defect: every
                // code-built TMP label would render blank (EnsureFont warns at
                // runtime; here it is gate-worthy).
                failures.Add("TOFU ORACLE — no TMP font resolvable (TMP_Settings.defaultFontAsset null AND " +
                             "'Fonts & Materials/LiberationSans SDF' absent from Resources): all code-built UI text would blank");
                return;
            }

            // codepoint -> occurrence list "Assets/...:line"
            var suspects = new Dictionary<int, List<string>>();
            int filesScanned = 0;

            foreach (var path in RuntimeSources(modulesDir))
            {
                string norm = path.Replace('\\', '/');
                // DevTools overlays are owner/dev-only surfaces (same carve-out
                // UiObsidianConformanceRegression grants OwnerDevToolsOverlay) —
                // their strings are not shipped player UI.
                if (norm.IndexOf("/_Modules/DevTools/", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                string text = SafeRead(path, notes);
                if (text == null) continue;

                bool touchesUiStack = false;
                for (int i = 0; i < UiStackTokens.Length && !touchesUiStack; i++)
                    if (text.IndexOf(UiStackTokens[i], StringComparison.Ordinal) >= 0) touchesUiStack = true;
                if (!touchesUiStack) continue;
                filesScanned++;

                var lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.TrimStart();
                    if (trimmed.Length == 0) continue;
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("/*")) continue;
                    // Attribute rows render in the Inspector, not in a TMP label.
                    if (trimmed.StartsWith("[")) continue;
                    // Console/flight-recorder strings never reach a label.
                    if (line.IndexOf("Debug.Log", StringComparison.Ordinal) >= 0) continue;
                    if (line.IndexOf("FlowTrace.", StringComparison.Ordinal) >= 0) continue;
                    if (line.IndexOf("Guard.", StringComparison.Ordinal) >= 0) continue;
                    if (line.IndexOf("Console.Write", StringComparison.Ordinal) >= 0) continue;
                    if (line.IndexOf("throw new", StringComparison.Ordinal) >= 0) continue;

                    int commentCut = CommentStartOutsideLiterals(line);
                    foreach (Match m in StringLiteralRx.Matches(line))
                    {
                        if (commentCut >= 0 && m.Index > commentCut) break; // trailing // comment
                        string body = m.Value.Substring(1, m.Value.Length - 2);
                        body = UnicodeEscapeRx.Replace(body, e =>
                            ((char)int.Parse(e.Groups[1].Value, NumberStyles.HexNumber)).ToString());

                        for (int k = 0; k < body.Length; k++)
                        {
                            char c = body[k];
                            int cp = c;
                            if (char.IsHighSurrogate(c) && k + 1 < body.Length && char.IsLowSurrogate(body[k + 1]))
                            {
                                cp = char.ConvertToUtf32(c, body[k + 1]);
                                k++;
                            }
                            if (cp <= 126) continue;

                            List<string> where;
                            if (!suspects.TryGetValue(cp, out where))
                                suspects[cp] = where = new List<string>();
                            if (where.Count < 8) where.Add(RelPath(norm) + ":" + (i + 1));
                            else if (where.Count == 8) where.Add("(+more)");
                        }
                    }
                }
            }

            int distinctChecked = 0, tofu = 0;
            foreach (var kv in suspects)
            {
                distinctChecked++;
                int cp = kv.Key;
                bool renderable = cp <= 0xFFFF && oracle.Has((char)cp);
                if (renderable) continue;
                tofu++;
                bool unpairedSurrogate = cp >= 0xD800 && cp <= 0xDFFF;
                string glyph = unpairedSurrogate ? "<surrogate>" : char.ConvertFromUtf32(cp);
                string why = unpairedSurrogate
                    ? "unpaired surrogate — corrupt literal, cannot render"
                    : cp > 0xFFFF
                    ? "astral-plane char (emoji) — no shipped UI font covers it"
                    : "missing from UI font '" + oracle.FontName + "' (incl. fallbacks) — renders as □ (tofu)";
                failures.Add("TOFU U+" + cp.ToString("X4") + " '" + glyph + "' " + why + " — at: " +
                             string.Join(", ", kv.Value.ToArray()));
            }
            notes.Add("tofu oracle: " + filesScanned + " UI file(s) scanned, " + distinctChecked +
                      " distinct non-ASCII char(s) font-verified, " + tofu + " tofu");
        }

        // =====================================================================
        // CHECK 2 — UIDocument CONSTRUCTION FENCE
        // =====================================================================
        private static void CheckUiDocumentFence(string modulesDir, List<string> failures, List<string> notes)
        {
            int baselineHits = 0;
            var seenBaseline = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in RuntimeSources(modulesDir))
            {
                string text = SafeRead(path, notes);
                if (text == null) continue;
                if (text.IndexOf("UIDocument", StringComparison.Ordinal) < 0) continue;

                string rel = RelPath(path.Replace('\\', '/'));
                var lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("/*")) continue;
                    bool smells = false;
                    for (int r = 0; r < UiDocumentSmells.Length && !smells; r++)
                        if (UiDocumentSmells[r].IsMatch(lines[i])) smells = true;
                    if (!smells) continue;

                    if (UiDocumentBaseline.Contains(rel)) { baselineHits++; seenBaseline.Add(rel); }
                    else failures.Add("UIDOCUMENT FENCE — new UIDocument surface outside the sanctioned baseline: " +
                                      rel + ":" + (i + 1) + " -> " + trimmed.Trim() +
                                      " (UXML-sourced HUDs do NOT render in builds — CLAUDE.md §8 / MASTER_CATALOG hud.md; " +
                                      "new screens go through ElarionUiKit uGUI; a deliberate code-built UITK surface " +
                                      "must be added to UiDocumentBaseline explicitly)");
                    break; // one report per file is enough
                }
            }

            var resolved = new List<string>();
            foreach (var b in UiDocumentBaseline)
                if (!seenBaseline.Contains(b)) resolved.Add(b);
            string extra = resolved.Count > 0
                ? "; " + resolved.Count + " baseline entr(ies) no longer construct UIDocument (refresh UiDocumentBaseline)"
                : "";
            notes.Add("UIDocument fence: " + baselineHits + " sanctioned surface hit(s)" + extra);
        }

        // =====================================================================
        // CHECK 3 — KIT CONFORMANCE (reflection over the REAL compiled kit)
        // =====================================================================
        private static void CheckKitConformance(List<string> failures, List<string> notes)
        {
            Type kit = typeof(ElarionUiKit);

            var publicStatics = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in kit.GetMethods(BindingFlags.Public | BindingFlags.Static))
                publicStatics.Add(m.Name);

            var missing = new List<string>();
            foreach (var required in RequiredKitBuilders)
                if (!publicStatics.Contains(required)) missing.Add(required);
            if (missing.Count > 0)
                failures.Add("KIT CONFORMANCE — ElarionUiKit is missing load-bearing public builder(s): " +
                             string.Join(", ", missing.ToArray()) +
                             " (HUD screens compose through these; a rename/removal breaks every consumer)");

            // Rarity glyphs = the colorblind owner's non-color rarity channel.
            // Each must be non-empty and renderable by the shipped font.
            if (publicStatics.Contains("RarityGlyph"))
            {
                var oracle = FontOracle.Resolve(notes);
                for (int i = 0; i <= 4; i++)
                {
                    string glyph = null;
                    try { glyph = ElarionUiKit.RarityGlyph(i); }
                    catch (Exception ex) { failures.Add("KIT CONFORMANCE — RarityGlyph(" + i + ") threw " + ex.GetType().Name); continue; }
                    if (string.IsNullOrEmpty(glyph))
                    {
                        failures.Add("KIT CONFORMANCE — RarityGlyph(" + i + ") is null/empty: rarity loses its " +
                                     "shape channel (owner is red/green colorblind — color alone is not a signal)");
                        continue;
                    }
                    if (!oracle.Available) continue;
                    foreach (char c in glyph)
                        if (c > 126 && !oracle.Has(c))
                            failures.Add("KIT CONFORMANCE — RarityGlyph(" + i + ") '" + glyph + "' char U+" +
                                         ((int)c).ToString("X4") + " missing from UI font '" + oracle.FontName +
                                         "' — the rarity glyph itself would tofu");
                }
            }
            notes.Add("kit conformance: " + RequiredKitBuilders.Length + " required builder(s) reflected on " + kit.FullName);
        }

        // =====================================================================
        // CHECK 4 — RESOURCES PATH RESOLUTION (the wiard.jpg-typo class)
        // =====================================================================
        private static void CheckResourcePaths(string modulesDir, List<string> failures, List<string> notes)
        {
            // Index every Assets/**/Resources tree: relative path without
            // extension + every relative folder (LoadAll targets), lowercased.
            var index = new HashSet<string>(StringComparer.Ordinal);
            var dirs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in Directory.GetDirectories(Application.dataPath, "Resources", SearchOption.AllDirectories))
            {
                foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                    dirs.Add(Rel(root, dir));
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    string rel = Rel(root, file);
                    int dot = rel.LastIndexOf('.');
                    int slash = rel.LastIndexOf('/');
                    if (dot > slash) rel = rel.Substring(0, dot);
                    index.Add(rel);
                }
            }

            int totalLiterals = 0, baselineMisses = 0;
            var seenBaseline = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in RuntimeSources(modulesDir))
            {
                string text = SafeRead(path, notes);
                if (text == null) continue;
                if (text.IndexOf("Resources", StringComparison.Ordinal) < 0) continue;

                // Drop comment-only lines, keep the rest as one blob so a call
                // split across lines still matches.
                var kept = new List<string>();
                foreach (var line in text.Split('\n'))
                {
                    string t = line.TrimStart();
                    if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*")) continue;
                    kept.Add(line);
                }
                string blob = string.Join("\n", kept.ToArray());

                foreach (Match m in ResourcesLoadRx.Matches(blob))
                {
                    totalLiterals++;
                    string p = m.Groups[1].Value;
                    string key = p.ToLowerInvariant().TrimEnd('/');
                    if (key.Length == 0) continue;                    // LoadAll("") = whole tree, always valid
                    if (index.Contains(key) || dirs.Contains(key)) continue;

                    if (MissingResourceBaseline.Contains(p)) { baselineMisses++; seenBaseline.Add(p); continue; }
                    failures.Add("RESOURCES MISS — '" + p + "' referenced in " + RelPath(path.Replace('\\', '/')) +
                                 " resolves to NO asset under any Resources root (wiard-typo class: the surface " +
                                 "silently blanks at runtime)");
                }
            }

            var resolved = new List<string>();
            foreach (var b in MissingResourceBaseline)
                if (!seenBaseline.Contains(b)) resolved.Add(b);
            string extra = resolved.Count > 0
                ? "; " + resolved.Count + " baseline path(s) resolved/gone (refresh MissingResourceBaseline)"
                : "";
            notes.Add("resources: " + totalLiterals + " literal path(s) checked, " + baselineMisses +
                      " known-missing tracked as debt" + extra);
        }

        // =====================================================================
        // CHECK 5 — SAFE-AREA CORNER (WO-868, the clipped "Connect Wallet")
        // =====================================================================

        // The corner button's authored size (PiSignInController.BuildButton): a fixed
        // 300-px width for the longest label, height AT the kit touch floor so
        // ClampMinTouch has nothing to grow (the growth is what pushed it off-screen).
        private const float CornerButtonWidthPx = 300f;

        // The oracle owns this floor DELIBERATELY — asserting the placed rect against
        // SafeAreaInset.EdgeMarginPx would be tautological (shrink the constant and the
        // assertion shrinks with it; proven by a negative-control run 2026-08-04 that
        // passed with the margin back at the defective 16 px). 32 device px is the
        // absolute minimum that is not "flush" on a ~2.7x-density phone; the measured
        // WO-868 defect was 16.
        private const float MinEdgeMarginPx = 32f;

        // Landscape 2340x1080 (the Seeker figure the UI review specs against) plus the
        // surface the 2026-08-04 screencap actually reported (2670x1200, 1:1).
        // Each row: label, screen w/h, safeArea. A landscape cutout eats one SHORT edge,
        // so the right-hand inset is the one that can clip this button.
        private static readonly object[][] SafeAreaCases =
        {
            //            label                          w     h     safeArea
            new object[] { "2340x1080 no cutout",        2340, 1080, new Rect(0f,   0f, 2340f, 1080f) },
            new object[] { "2340x1080 right cutout 84",  2340, 1080, new Rect(0f,   0f, 2256f, 1080f) },
            new object[] { "2340x1080 left cutout 84",   2340, 1080, new Rect(84f,  0f, 2256f, 1080f) },
            new object[] { "2340x1080 top+right gesture",2340, 1080, new Rect(0f,  48f, 2256f, 1010f) },
            new object[] { "2670x1200 captured surface", 2670, 1200, new Rect(0f,   0f, 2670f, 1200f) },
        };

        private static void CheckSafeAreaCorner(List<string> failures, List<string> notes)
        {
            // --- 5a. PURE MATH: the placed rect never leaves the safe area. --------
            var size = new Vector2(CornerButtonWidthPx, ElarionUiKit.MinTouchPx);
            int cases = 0;
            foreach (var row in SafeAreaCases)
            {
                string label = (string)row[0];
                int w = (int)row[1];
                int h = (int)row[2];
                var safe = (Rect)row[3];
                cases++;

                Rect r = SafeAreaInset.TopRightScreenRect(safe, w, h, size);

                if (r.xMin < safe.xMin || r.xMax > safe.xMax || r.yMin < safe.yMin || r.yMax > safe.yMax)
                    failures.Add("SAFE-AREA CORNER — the wallet/sign-in button rect " + RectStr(r) +
                                 " escapes Screen.safeArea " + RectStr(safe) + " at " + label +
                                 " (WO-868: this is the clipped 'Connect Wallet' defect — the corner must be " +
                                 "placed from Screen.safeArea, never a raw pixel inset)");

                // The rect must also clear the SCREEN edges — a device that reports no
                // cutout (safeArea == full screen) must still get a real breathing
                // margin, or the button reads flush like the raw -16 px did.
                float rightGap = w - r.xMax;
                float topGap = h - r.yMax;
                if (rightGap < MinEdgeMarginPx || topGap < MinEdgeMarginPx)
                    failures.Add("SAFE-AREA CORNER — at " + label + " the button sits " + rightGap.ToString("0.#") +
                                 " px from the right screen edge and " + topGap.ToString("0.#") +
                                 " px from the top; the floor is " + MinEdgeMarginPx.ToString("0.#") +
                                 " px (the measured WO-868 defect was 16 px, which reads as flush and lands in " +
                                 "the rounded-corner / cutout band)");

                // Touch floor: an on-screen-but-untappable button is the same defect.
                if (r.width < ElarionUiKit.MinTouchPx || r.height < ElarionUiKit.MinTouchPx)
                    failures.Add("SAFE-AREA CORNER — button rect " + RectStr(r) + " is below MinTouchPx (" +
                                 ElarionUiKit.MinTouchPx.ToString("0.#") + ") at " + label);
            }

            // The helper's own fixed-pixel margin may never be shrunk back toward flush.
            if (SafeAreaInset.EdgeMarginPx < MinEdgeMarginPx)
                failures.Add("SAFE-AREA CORNER — SafeAreaInset.EdgeMarginPx is " +
                             SafeAreaInset.EdgeMarginPx.ToString("0.#") + " px, below the " +
                             MinEdgeMarginPx.ToString("0.#") + " px floor; a shrunken margin re-creates the " +
                             "flush-to-edge corner WO-868 fixed (the defect measured 16 px)");

            // --- 5b. SOURCE LAW: the corner still routes through the helper. ------
            const string cornerSrc = "_Modules/Core/Platform/PiSignInController.cs";
            string cornerPath = Path.Combine(Application.dataPath, cornerSrc.Replace('/', Path.DirectorySeparatorChar));
            string src = File.Exists(cornerPath) ? SafeRead(cornerPath, notes) : null;
            if (src == null)
            {
                notes.Add("safe-area corner: Assets/" + cornerSrc + " unreadable — source law skipped");
            }
            else
            {
                if (src.IndexOf("SafeAreaInset.ApplyTopRight", StringComparison.Ordinal) < 0)
                    failures.Add("SAFE-AREA CORNER — Assets/" + cornerSrc + " no longer calls " +
                                 "SafeAreaInset.ApplyTopRight: the corner button is being placed by hand again " +
                                 "(WO-868 regressed — a raw inset ignores the device cutout)");

                if (Regex.IsMatch(src, @"anchoredPosition\s*=\s*new\s+Vector2\s*\(\s*-\s*\d"))
                    failures.Add("SAFE-AREA CORNER — Assets/" + cornerSrc + " re-introduces a hard-coded negative " +
                                 "anchoredPosition inset; the top-right corner is owned by SafeAreaInset");

                if (src.IndexOf("ElarionUiKit.MinTouchPx", StringComparison.Ordinal) < 0)
                    failures.Add("SAFE-AREA CORNER — Assets/" + cornerSrc + " no longer sizes the corner holder at " +
                                 "ElarionUiKit.MinTouchPx: a sub-floor holder gets grown SYMMETRICALLY by " +
                                 "ClampMinTouch and pushes back off the top of the screen (the measured -10 px)");
            }

            notes.Add("safe-area corner: " + cases + " resolution/cutout case(s) verified against SafeAreaInset (margin " +
                      SafeAreaInset.EdgeMarginPx.ToString("0.#") + " px, size " +
                      CornerButtonWidthPx.ToString("0.#") + "x" + ElarionUiKit.MinTouchPx.ToString("0.#") + ")");
        }

        // =====================================================================
        // CHECK 6 — COMBAT HUD COMPOSITION (WO-867)
        // ---------------------------------------------------------------------
        // Three defect classes from the 2026-08-04 Seeker review, each pinned to
        // the DATA that proved it rather than to a screenshot:
        //
        //   6a RAGGED / TORN EDGES — nameplate_party.png, nameplate_bar.png and
        //      target_core.png are whole ATLAS PAGES imported `spriteMode: 1`
        //      (Single). Drawn Image.Type.Simple they paint every unrelated element
        //      parked on the same texture: a loose grey rock chunk (party, x>=1137),
        //      a torn stone fringe (target_core, y 299..386 top-down) and a dead
        //      transparent tail (bar, x>=2347). ElarionUiKit.PlatePageSprite draws
        //      the MEASURED sub-rect instead. Those rects are hard numbers against
        //      the committed PNG size, so this check pins the PNG dimensions + the
        //      spriteMode: a re-import that changes either silently crops the WRONG
        //      region, and must fail here rather than on a device.
        //
        //   6b THE ENEMY PLATE AS ONE UNIT — TargetNameplate.prefab authors every
        //      child CENTRE-anchored at ABSOLUTE offsets for a 480x130 root, plus a
        //      GridLayoutGroup with a FIXED 374.3x31 cell, plus an always-active
        //      BossTarget ragged frame. Stretched to the HUD area those pieces drift
        //      apart (the "four disconnected pieces"). ComposeTargetPlate re-lays
        //      them in fixed-pixel bands. This check pins the child names it composes
        //      by, that it is still called, and the ONE cross-file constant it is
        //      derived from (HudKitController's TitleRow611 bottom anchor 0.72).
        //
        //   6c THE WHITE CAST BAR — CastBar1.prefab's root Image points at sprite
        //      guid c217dc4a3df342c42acde576290a6310, which resolves to NOTHING in
        //      this project; uGUI paints a null-sprite Image as a flat WHITE quad
        //      (03-town.png). BuildCastBar must run the no-silent-white guard.
        //
        //   6d TOUCH FLOOR on the right-edge controls — recomputed from the REAL
        //      source numbers (HudAreasHost's ActionRail rect + HudKitController's
        //      Pill611*/MedallionPerPillH) at 2340x1080, not from a copied figure.
        // =====================================================================

        /// <summary>Committed page geometry the kit's crop fractions were MEASURED against.
        /// Row: resources-relative png path, width, height.</summary>
        private static readonly object[][] AtlasPagePins =
        {
            new object[] { "Assets/Resources/RpgUi/hud/nameplate_party.png", 1280, 299 },
            new object[] { "Assets/Resources/RpgUi/hud/nameplate_bar.png",   2611, 116 },
            new object[] { "Assets/Resources/RpgUi/hud/target_core.png",     1427, 386 },
        };

        /// <summary>Children ComposeTargetPlate resolves BY NAME on the mirrored prefab. A rename
        /// in a re-mirror makes FindDeep return null and the plate silently un-composes.</summary>
        private static readonly string[] TargetPlateChildren =
        {
            "TargetName", "StatBars", "TargetIcon", "HealthBackground", "ManaBackground", "BossTarget",
        };

        /// <summary>The TitleRow611 bottom anchor in HudKitController that
        /// ElarionUiKit.TargetTitleReservePx is derived from — see ComposeTargetPlate.</summary>
        private const float TitleRow611BottomAnchor = 0.72f;

        private static void CheckCombatHudComposition(List<string> failures, List<string> notes)
        {
            string kitObsidian = ReadAsset("_Modules/Core/UI/ElarionUiKitObsidian.cs", notes);
            string kitNameplate = ReadAsset("_Modules/Core/UI/ElarionUiKitNameplate.cs", notes);
            string kitCore = ReadAsset("_Modules/Core/UI/ElarionUiKit.cs", notes);
            string hudKit = ReadAsset("_Modules/HUD/Kit/HudKitController.cs", notes);
            string areasHost = ReadAsset("_Modules/HUD/Kit/HudAreasHost.cs", notes);
            string echoFeedback = ReadAsset("_Modules/Village/Harvest/EchoUnlockFeedback.cs", notes);

            // --- 6a. ATLAS-PAGE PINS + the crop routing. --------------------------
            int pagesPinned = 0;
            foreach (var row in AtlasPagePins)
            {
                string rel = (string)row[0];
                int expW = (int)row[1], expH = (int)row[2];
                string full = Path.Combine(Application.dataPath, rel.Substring("Assets/".Length)
                                                                   .Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    failures.Add("ATLAS PAGE — " + rel + " is missing; ElarionUiKit.PlatePageSprite crops it by " +
                                 "a MEASURED pixel fraction and cannot verify the region without it");
                    continue;
                }
                int w, h;
                if (!TryReadPngSize(full, out w, out h))
                {
                    notes.Add("atlas page: could not read PNG header for " + rel + " — size pin skipped");
                    continue;
                }
                pagesPinned++;
                if (w != expW || h != expH)
                    failures.Add("ATLAS PAGE — " + rel + " is now " + w + "x" + h + " (pinned " + expW + "x" + expH +
                                 "). ElarionUiKit.PlatePageSprite's crop rect is a FRACTION measured against the " +
                                 "pinned size, so it now crops the WRONG region and the ragged/torn edge art can " +
                                 "reappear (WO-867). Re-measure the rect in _platePageRects and update this pin.");

                string meta = full + ".meta";
                if (File.Exists(meta))
                {
                    string m = SafeRead(meta, notes);
                    if (m != null && !Regex.IsMatch(m, @"^\s*spriteMode:\s*1\s*$", RegexOptions.Multiline))
                        failures.Add("ATLAS PAGE — " + rel + " is no longer imported spriteMode: 1 (Single). " +
                                     "PlatePageSprite's sub-rect math assumes the sprite covers the whole texture; " +
                                     "with Multiple, Resources.Load returns a sub-sprite and the crop is wrong.");
                }
            }
            if (kitNameplate != null && kitNameplate.IndexOf("PlatePageSprite", StringComparison.Ordinal) < 0)
                failures.Add("RAGGED EDGE — ElarionUiKitNameplate.cs no longer routes the hero/Heart plate through " +
                             "ElarionUiKit.PlatePageSprite: it draws the whole nameplate_party atlas page again, " +
                             "which paints the loose grey rock chunk at the right end of every plate (WO-867).");
            if (kitObsidian != null && kitObsidian.IndexOf("PlatePageSprite", StringComparison.Ordinal) < 0)
                failures.Add("RAGGED EDGE — ElarionUiKitObsidian.cs no longer defines/uses PlatePageSprite: the " +
                             "target_core torn stone fringe and the nameplate page artifacts come back (WO-867).");

            // --- 6b. THE ENEMY PLATE COMPOSES AS ONE UNIT. -----------------------
            const string targetPrefab = "Assets/Resources/RpgUi/prefabs/TargetNameplate.prefab";
            string prefabFull = Path.Combine(Application.dataPath,
                targetPrefab.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(prefabFull))
            {
                notes.Add("target plate: " + targetPrefab + " absent — the kit falls back to MODE 2 (constructed), " +
                          "which composes its own plate; prefab-child pins skipped");
            }
            else
            {
                string prefab = SafeRead(prefabFull, notes) ?? "";
                foreach (var child in TargetPlateChildren)
                    if (!Regex.IsMatch(prefab, @"m_Name:\s*" + Regex.Escape(child) + @"\s*$", RegexOptions.Multiline))
                        failures.Add("TARGET PLATE — " + targetPrefab + " no longer contains a '" + child +
                                     "' GameObject. ElarionUiKit.ComposeTargetPlate resolves it BY NAME; a miss " +
                                     "means that piece keeps its authored ABSOLUTE offset (sized for a 480x130 " +
                                     "root) and drifts out of the stretched plate — the WO-867 defect.");
            }

            if (kitObsidian != null)
            {
                if (kitObsidian.IndexOf("ComposeTargetPlate(pf, h", StringComparison.Ordinal) < 0)
                    failures.Add("TARGET PLATE — BuildTargetFrame's prefab path no longer calls ComposeTargetPlate: " +
                                 "the enemy nameplate reverts to four disconnected pieces (WO-867).");
                if (kitObsidian.IndexOf("DeactivateChild(pf.transform, \"bosstarget\")", StringComparison.Ordinal) < 0)
                    failures.Add("TARGET PLATE — ComposeTargetPlate no longer deactivates BossTarget. That child " +
                                 "ships m_IsActive:1 and draws the 397x412 nameplate_boss TORN frame at a fixed " +
                                 "off-plate offset — the ragged grey/gold shard over the enemy plate (WO-867).");
                if (kitObsidian.IndexOf("PlatePageSprite(RpgUiCatalog.HudTargetCore)", StringComparison.Ordinal) < 0)
                    failures.Add("TARGET PLATE — the plate face is no longer drawn from the CROPPED target_core " +
                                 "body. target_core.png is a 1427x386 COMPOSITE page (portrait socket + ornate " +
                                 "strip + plate body + a TORN STONE FRINGE at y 299..386); drawn whole and " +
                                 "stretched, the fringe is the ragged edge over the enemy plate (WO-867).");
                if (!Regex.IsMatch(kitObsidian, @"GetComponent<LayoutGroup>\s*\(\s*\)"))
                    failures.Add("TARGET PLATE — ComposeTargetPlate no longer disables the prefab's LayoutGroup. " +
                                 "StatBars carries a GridLayoutGroup with a FIXED 374.3x31 cell, which is why the " +
                                 "green/blue HP bar rendered as an island offset to the right of the plate (WO-867).");
            }

            if (hudKit != null)
            {
                // ComposeTargetPlate reserves (1 - 0.72) * TargetPlatePx of the plate top for the
                // name+Lv row HudKitController builds. That file belongs to another lane, so the
                // coupling is pinned here instead of duplicated there.
                var m = Regex.Match(hudKit, @"rowRt\.anchorMin\s*=\s*new\s+Vector2\s*\(\s*[\d.]+f\s*,\s*([\d.]+)f\s*\)");
                if (!m.Success)
                    notes.Add("target plate: could not locate HudKitController's TitleRow611 anchorMin — " +
                              "the TargetTitleReservePx derivation is unverified this run");
                else
                {
                    float bottom;
                    if (float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out bottom)
                        && Mathf.Abs(bottom - TitleRow611BottomAnchor) > 0.001f)
                        failures.Add("TARGET PLATE — HudKitController's TitleRow611 bottom anchor moved to " +
                                     bottom.ToString("0.###") + " (was " + TitleRow611BottomAnchor.ToString("0.###") +
                                     "). ElarionUiKit.TargetTitleReservePx (32 ref px) is DERIVED from it as " +
                                     "(1 - anchor) * TargetPlatePx; the HP band will now collide with the name/Lv " +
                                     "row. Re-derive TargetTitleReservePx and update this pin (WO-867).");
                }
            }

            // --- 6c. NO SILENT WHITE on the cast bar. ----------------------------
            if (kitObsidian != null)
            {
                int guardCalls = Regex.Matches(kitObsidian, @"GuardSpriteNullImages\s*\(\s*pf").Count;
                if (guardCalls < 2)
                    failures.Add("WHITE CAST BAR — only " + guardCalls + " prefab path(s) in ElarionUiKitObsidian.cs " +
                                 "call GuardSpriteNullImages(pf, ...). BuildTargetFrame AND BuildCastBar both bind " +
                                 "mirrored prefabs whose sprite GUIDs are DANGLING; an unguarded null-sprite Image " +
                                 "renders as a flat WHITE QUAD (docs/ui-review/2026-08-04-seeker/03-town.png).");
            }
            int dangling = CountDanglingPrefabSprites(notes);
            if (dangling > 0)
                notes.Add("mirrored RpgUi prefabs carry " + dangling + " dangling sprite ref(s) — each is a white " +
                          "quad unless its kit builder runs GuardSpriteNullImages");

            // --- 6d. TOUCH FLOOR, recomputed from the owning source numbers. -----
            Vector4 rail;
            if (areasHost != null && TryParseAreaRect(areasHost, "ActionRail", out rail))
            {
                // HudAreasHost canvas: 1080x1920 reference, ScreenMatchMode MatchWidthOrHeight 0.5.
                const float refW = 1080f, refH = 1920f, match = 0.5f;
                const float screenW = 2340f, screenH = 1080f;   // the Seeker figure the review specs against
                float scale = Mathf.Pow(screenW / refW, 1f - match) * Mathf.Pow(screenH / refH, match);
                float canvasW = screenW / scale, canvasH = screenH / scale;

                float railW = (rail.z - rail.x) * canvasW;
                float railH = (rail.w - rail.y) * canvasH;

                float y0 = ParseConst(hudKit, "Pill611Y0", 0.02f);
                float y1 = ParseConst(hudKit, "Pill611Y1", 0.30f);
                float perPill = ParseConst(hudKit, "MedallionPerPillH", 0.9f);
                float pillH = (y1 - y0) * railH;
                float medallion = perPill * pillH;

                notes.Add("touch measure @" + screenW + "x" + screenH + " (canvas " + canvasW.ToString("0.#") + "x" +
                          canvasH.ToString("0.#") + " ref units): ActionRail " + railW.ToString("0.#") + "x" +
                          railH.ToString("0.#") + ", attack pill height " + pillH.ToString("0.#") +
                          ", ability medallion " + medallion.ToString("0.#") + " (floor " +
                          ElarionUiKit.MinTouchPx.ToString("0.#") + ")");

                if (medallion < ElarionUiKit.MinTouchPx)
                {
                    // The arc geometry lives in HudKitController (another lane's file). The kit
                    // compensates with a fixed MinTouchPx hit-area child; that compensation is what
                    // this gate holds, because without it the medallions are genuinely untappable.
                    if (kitCore == null || kitCore.IndexOf("EnsureTouchFloorArea(slot)", StringComparison.Ordinal) < 0)
                        failures.Add("TOUCH FLOOR — ability medallions resolve to " + medallion.ToString("0.#") +
                                     " ref px at " + screenW + "x" + screenH + ", " +
                                     (ElarionUiKit.MinTouchPx - medallion).ToString("0.#") + " px UNDER MinTouchPx (" +
                                     ElarionUiKit.MinTouchPx.ToString("0.#") + "), and StyleAsRoundMedallion no " +
                                     "longer calls EnsureTouchFloorArea — nothing restores the tap target (WO-867).");
                    else
                        notes.Add("touch floor: medallion VISUAL is " +
                                  (ElarionUiKit.MinTouchPx - medallion).ToString("0.#") +
                                  " px under the floor (geometry owned by HudKitController/CombatArcLayout611); " +
                                  "the kit's EnsureTouchFloorArea hit-area child restores the 112 px tap target");
                }
                if (pillH < ElarionUiKit.MinTouchPx)
                    notes.Add("touch floor: the attack pill is " + pillH.ToString("0.#") +
                              " ref px tall (" + (ElarionUiKit.MinTouchPx - pillH).ToString("0.#") +
                              " under the floor) but " + ((1f - ParseConst(hudKit, "Pill611X0", 0.30f)) * railW)
                                  .ToString("0.#") + " px wide — reported, not failed: the tap target is comfortable");
            }
            else
            {
                notes.Add("touch measure: could not parse HudAreasHost's ActionRail rect — right-edge sizing unverified");
            }

            // --- 6e. The Echoes chip is docked, sized, and the floater is gone. --
            if (echoFeedback != null)
            {
                if (Regex.IsMatch(echoFeedback, @"ToastCard\s*\([^)]*ToastTone\.Info"))
                    failures.Add("ECHO CHIP — EchoUnlockFeedback.cs re-introduces the free-floating 'Echoes N/M' " +
                                 "ToastCard. It landed in the ~7 ref-px seam between the HudAreasHost Vitals band " +
                                 "(0.800..0.985) and HeartStatus band (0.700..0.792), in no band at all, and its " +
                                 "accentLeft strip is the stray gold rule (WO-867). The count rides the chip.");
                if (echoFeedback.IndexOf("ElarionUiKit.MinTouchPx", StringComparison.Ordinal) < 0)
                    failures.Add("ECHO CHIP — EchoUnlockFeedback.cs no longer sizes the Echoes chip at " +
                                 "ElarionUiKit.MinTouchPx; a sub-floor chip is grown symmetrically by ClampMinTouch " +
                                 "and drifts out of its docked band (WO-867).");
                if (echoFeedback.IndexOf("EchoChipBandCentreY", StringComparison.Ordinal) < 0)
                    failures.Add("ECHO CHIP — EchoUnlockFeedback.cs no longer docks the chip on the named right-column " +
                                 "band constant; it is free-floating again (WO-867).");
            }

            notes.Add("combat hud composition: " + pagesPinned + " atlas page(s) pinned, " +
                      TargetPlateChildren.Length + " target-plate child name(s) verified");
        }

        /// <summary>Read Assets/&lt;rel&gt; as text (null + note when absent/unreadable).</summary>
        private static string ReadAsset(string rel, List<string> notes)
        {
            string full = Path.Combine(Application.dataPath, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) return SafeRead(full, notes);
            notes.Add("combat hud composition: Assets/" + rel + " not found — its checks were skipped");
            return null;
        }

        /// <summary>PNG IHDR width/height without decoding the image.</summary>
        private static bool TryReadPngSize(string path, out int width, out int height)
        {
            width = height = 0;
            try
            {
                var head = new byte[24];
                using (var fs = File.OpenRead(path))
                    if (fs.Read(head, 0, 24) < 24) return false;
                if (head[0] != 0x89 || head[1] != 0x50 || head[2] != 0x4E || head[3] != 0x47) return false;
                width  = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                return width > 0 && height > 0;
            }
            catch { return false; }
        }

        /// <summary>Parse an <c>Add(HudArea.&lt;name&gt;, new Vector2(a,b), new Vector2(c,d));</c> row
        /// out of HudAreasHost source into (a,b,c,d).</summary>
        private static bool TryParseAreaRect(string src, string area, out Vector4 rect)
        {
            rect = default(Vector4);
            var m = Regex.Match(src, @"Add\s*\(\s*HudArea\." + Regex.Escape(area) +
                                     @"\s*,\s*new\s+Vector2\s*\(\s*([\d.]+)f?\s*,\s*([\d.]+)f?\s*\)\s*,\s*" +
                                     @"new\s+Vector2\s*\(\s*([\d.]+)f?\s*,\s*([\d.]+)f?\s*\)");
            if (!m.Success) return false;
            float a, b, c, d;
            if (!float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out a)) return false;
            if (!float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b)) return false;
            if (!float.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out c)) return false;
            if (!float.TryParse(m.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return false;
            rect = new Vector4(a, b, c, d);
            return true;
        }

        /// <summary>Read a <c>const float Name = 0.12f;</c> literal out of source (fallback on a miss).</summary>
        private static float ParseConst(string src, string name, float fallback)
        {
            if (src == null) return fallback;
            var m = Regex.Match(src, @"\b" + Regex.Escape(name) + @"\s*=\s*(-?[\d.]+)f");
            float v;
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return fallback;
        }

        /// <summary>Count sprite refs in the mirrored RpgUi prefabs whose GUID resolves to no .meta
        /// under Assets/Resources or Assets/Blink — each one renders as a white quad unguarded.</summary>
        private static int CountDanglingPrefabSprites(List<string> notes)
        {
            try
            {
                string prefabDir = Path.Combine(Application.dataPath, Path.Combine("Resources",
                                   Path.Combine("RpgUi", "prefabs")));
                if (!Directory.Exists(prefabDir)) return 0;

                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var guidRx = new Regex(@"^guid:\s*([0-9a-fA-F]{32})\s*$", RegexOptions.Multiline);
                foreach (var rootName in new[] { "Resources", "Blink" })
                {
                    string root = Path.Combine(Application.dataPath, rootName);
                    if (!Directory.Exists(root)) continue;
                    foreach (var meta in Directory.GetFiles(root, "*.meta", SearchOption.AllDirectories))
                    {
                        try
                        {
                            using (var sr = new StreamReader(meta))
                            {
                                for (int i = 0; i < 4; i++)
                                {
                                    string line = sr.ReadLine();
                                    if (line == null) break;
                                    var gm = guidRx.Match(line);
                                    if (gm.Success) { known.Add(gm.Groups[1].Value); break; }
                                }
                            }
                        }
                        catch { }
                    }
                }
                if (known.Count == 0) return 0;

                // NOTE: matched without a literal open-brace character so the repo's naive
                // brace-balance gate (CLAUDE.md §1) stays green on this file.
                var spriteRx = new Regex(@"m_Sprite:[^,]*,\s*guid:\s*([0-9a-fA-F]{32})");
                int miss = 0;
                foreach (var pf in Directory.GetFiles(prefabDir, "*.prefab", SearchOption.TopDirectoryOnly))
                {
                    string text = SafeRead(pf, notes);
                    if (text == null) continue;
                    foreach (Match m in spriteRx.Matches(text))
                        if (!known.Contains(m.Groups[1].Value)) miss++;
                }
                return miss;
            }
            catch { return 0; }
        }

        private static string RectStr(Rect r)
            => "[x " + r.xMin.ToString("0.#") + ".." + r.xMax.ToString("0.#") +
               ", y " + r.yMin.ToString("0.#") + ".." + r.yMax.ToString("0.#") + "]";

        // =====================================================================
        // Helpers
        // =====================================================================

        /// <summary>Runtime first-party sources: Assets/_Modules/**/*.cs minus Editor/ and Tests/.</summary>
        private static IEnumerable<string> RuntimeSources(string modulesDir)
        {
            string[] files;
            try { files = Directory.GetFiles(modulesDir, "*.cs", SearchOption.AllDirectories); }
            catch { yield break; }
            foreach (var path in files)
            {
                string norm = path.Replace('\\', '/');
                if (norm.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (norm.IndexOf("/Tests/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                string name = Path.GetFileName(path);
                if (name.EndsWith("Test.cs", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)) continue;
                yield return path;
            }
        }

        private static string SafeRead(string path, List<string> notes)
        {
            try { return File.ReadAllText(path); }
            catch (Exception ex)
            {
                notes.Add(Path.GetFileName(path) + " unreadable (" + ex.Message + ")");
                return null;
            }
        }

        /// <summary>Index of the first "//" on the line that is NOT inside a string literal, or -1.</summary>
        private static int CommentStartOutsideLiterals(string line)
        {
            bool inString = false;
            for (int i = 0; i < line.Length - 1; i++)
            {
                char c = line[i];
                if (c == '\\' && inString) { i++; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (!inString && c == '/' && line[i + 1] == '/') return i;
            }
            return -1;
        }

        private static string RelPath(string normalizedFullPath)
        {
            int idx = normalizedFullPath.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? normalizedFullPath.Substring(idx) : normalizedFullPath;
        }

        /// <summary>Lowercased forward-slash path of <paramref name="fullPath"/> relative to <paramref name="root"/>.</summary>
        private static string Rel(string root, string fullPath)
        {
            string rel = fullPath.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
            return rel.ToLowerInvariant();
        }

        // ---------------------------------------------------------------------
        // FontOracle — reflection bridge to the REAL TMP font the kit resolves
        // (this asmdef does not reference Unity.TextMeshPro; reflection keeps the
        // file self-contained). Mirrors ElarionUiKit.EnsureFont's order:
        // TMP_Settings.defaultFontAsset ?? Resources LiberationSans SDF.
        // ---------------------------------------------------------------------
        private sealed class FontOracle
        {
            public bool Available;
            public string FontName = "<none>";

            private object _font;
            private MethodInfo _has3, _has2, _has1;   // HasCharacter overloads
            private bool _tryAddDynamic;              // dynamic atlas may pull from the source face
            private readonly Dictionary<char, bool> _cache = new Dictionary<char, bool>();

            private static FontOracle _resolved;

            public static FontOracle Resolve(List<string> notes)
            {
                if (_resolved != null) return _resolved;
                var o = new FontOracle();
                _resolved = o;
                try
                {
                    Type settingsType = FindType("TMPro.TMP_Settings");
                    Type fontType = FindType("TMPro.TMP_FontAsset");
                    if (fontType == null)
                    {
                        notes.Add("tofu oracle: TMPro types not loaded — font check unavailable");
                        return o;
                    }

                    object font = null;
                    if (settingsType != null)
                    {
                        var prop = settingsType.GetProperty("defaultFontAsset", BindingFlags.Public | BindingFlags.Static);
                        try { if (prop != null) font = prop.GetValue(null, null); } catch { /* settings asset absent */ }
                    }
                    var uo = font as UnityEngine.Object;
                    if (uo == null)
                        font = Resources.Load("Fonts & Materials/LiberationSans SDF", fontType);
                    uo = font as UnityEngine.Object;
                    if (uo == null) return o; // Available stays false — caller fails the run

                    o._font = font;
                    o.FontName = uo.name;
                    o._has3 = fontType.GetMethod("HasCharacter", new[] { typeof(char), typeof(bool), typeof(bool) });
                    o._has2 = fontType.GetMethod("HasCharacter", new[] { typeof(char), typeof(bool) });
                    o._has1 = fontType.GetMethod("HasCharacter", new[] { typeof(char) });
                    if (o._has3 == null && o._has2 == null && o._has1 == null)
                    {
                        notes.Add("tofu oracle: TMP_FontAsset.HasCharacter not found — font check unavailable");
                        return o;
                    }

                    // Dynamic-atlas fonts may add glyphs from the source face at
                    // runtime; let HasCharacter consult it (tryAddCharacter) so a
                    // glyph the SOURCE font genuinely carries never false-fails.
                    try
                    {
                        var pm = fontType.GetProperty("atlasPopulationMode");
                        if (pm != null) o._tryAddDynamic = Convert.ToInt32(pm.GetValue(font, null)) == 1; // AtlasPopulationMode.Dynamic
                    }
                    catch { o._tryAddDynamic = false; }

                    o.Available = true;
                }
                catch (Exception ex)
                {
                    notes.Add("tofu oracle: font resolution failed (" + ex.GetType().Name + ") — font check unavailable");
                }
                return o;
            }

            /// <summary>True when the resolved font (searching fallbacks, and the dynamic
            /// source face when applicable) can render <paramref name="c"/>. Unprovable
            /// (reflection hiccup) counts as renderable — never false-fail.</summary>
            public bool Has(char c)
            {
                if (!Available) return true;
                bool has;
                if (_cache.TryGetValue(c, out has)) return has;
                try
                {
                    if (_has3 != null)      has = (bool)_has3.Invoke(_font, new object[] { c, true, _tryAddDynamic });
                    else if (_has2 != null) has = (bool)_has2.Invoke(_font, new object[] { c, true });
                    else                    has = (bool)_has1.Invoke(_font, new object[] { c });
                }
                catch { has = true; }
                _cache[c] = has;
                return has;
            }

            private static Type FindType(string fullName)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = null;
                    try { t = asm.GetType(fullName); } catch { }
                    if (t != null) return t;
                }
                return null;
            }
        }

        // =====================================================================
        // CHECK 7 — THE TOWN RESOURCE RAIL IS ACTUALLY RAISED (WO-1221)
        // ---------------------------------------------------------------------
        // WHAT SHIPPED, AND WHY A SOURCE PIN CATCHES IT: WO-1205 built the expanded
        // rail inert (`_resExpandedRow.SetActive(false)`) on a SECOND occupancy widget
        // (`resourceChips` / `_resDock`) that hud-areas.json never occupies. Register()
        // deactivates every widget; occupancy is the only thing that turns one on.
        // LateTick SetActive'd that WRAPPER (an empty full-ActionRail dock) and logged
        // "resource panel expanded (opener live=True)". Device capture inside the window
        // (tmp/resources-expanded-105803.png, 2670x1200) showed only gold 1034.
        //
        // A later pass added `_resExpandedRow.SetActive` inside SetResourcePanelOpen and
        // a UiSurfaceProbe poll. Owner felt-test 2026-08-27 still FAIL: the pixels were
        // still not children of the gold chip occupancy actually shows, so ApplyPosture
        // (PostureEvaluator.Update, same GameObject, AFTER HudKitController.Update) could
        // deactivate the unoccupied widget before render while the probe reported painted.
        //
        // Invariants, all of which were RED on the tree that logged "expanded" and painted
        // nothing. A test that only asserts "the handler ran" / "the string SetActive
        // appears" is the hollow token this check exists to replace:
        //   7a  SetResourcePanelOpen actually SetActive's the row.
        //   7b  post-expand verify measures through UiSurfaceProbe (rect/opacity/coverage)
        //       and emits VERIFIED PAINTED; the hollow `opener live=` success is gone.
        //   7c  the expanded stack is parented to the GOLD CHIP (tapGo), not a second
        //       `resourceChips` occupancy widget. Split-widget is the defect.
        //   7d  the tap handler calls SetResourcePanelOpen directly (not only a bool).
        //   7e  TickResourceExpandVerify FAILS on INACTIVE (does not skip it as unmeasurable).
        //   7f  occupancy json lists resourceChipsCollapsed on calm(town) actionRail.
        //   7g  hanging the stack below the gold chip at 2670x1200 intersects the viewport
        //       with height above UiSurfaceProbe.MinEdgePx — a zero-size / offscreen row
        //       fails here without PlayMode.
        //
        // SOURCE-LINT + pure layout math: reads .cs/.json text, runs no PlayMode.
        // =====================================================================
        private static void CheckResourceRailRaise(string modulesDir, List<string> failures, List<string> notes)
        {
            string path = Path.Combine(modulesDir, "HUD", "Kit", "HudKitController.cs");
            if (!File.Exists(path))
            {
                failures.Add("RESOURCE RAIL — the source this check lints is MISSING at " + path +
                             ". HudKitController.cs is tracked source, not an optional fixture: without it " +
                             "WO-1221 (expanded row parented to the gold chip, SetActive, UiSurfaceProbe) " +
                             "is not checked at all.");
                return;
            }

            string src;
            try { src = File.ReadAllText(path); }
            catch (Exception ex)
            {
                notes.Add("resource-rail raise: unreadable (" + ex.GetType().Name + ") — check skipped");
                return;
            }

            // 7a — the open-state owner must raise the row.
            int idx = src.IndexOf("private void SetResourcePanelOpen", StringComparison.Ordinal);
            if (idx < 0)
            {
                failures.Add("RESOURCE RAIL — SetResourcePanelOpen is gone. The tap has no owner for " +
                             "_resExpandedRow.SetActive, which is how WO-1221 painted zero pixels.");
            }
            else
            {
                int end = src.IndexOf("\n        private ", idx + 1, StringComparison.Ordinal);
                string body = end > idx ? src.Substring(idx, end - idx) : src.Substring(idx);
                if (body.IndexOf("_resExpandedRow.SetActive", StringComparison.Ordinal) < 0)
                    failures.Add("RESOURCE RAIL — SetResourcePanelOpen records the open flag but never calls " +
                                 "_resExpandedRow.SetActive. The row is built inert (WO-1205), so the tap " +
                                 "raises nothing: the player taps the gold chip and sees only gold, while a " +
                                 "handler-ran trace reports expanded. This is WO-1221 regressing.");
            }

            // 7b — the expand trace must stay falsifiable. The hollow success
            // "resource panel expanded (opener live=True)" is forbidden: openerLive is true
            // by construction on the expand branch and cannot report a blank rail.
            if (src.IndexOf("UiSurfaceProbe", StringComparison.Ordinal) < 0 ||
                src.IndexOf("expand VERIFIED PAINTED", StringComparison.Ordinal) < 0)
                failures.Add("RESOURCE RAIL — the post-expand MEASURED verify is gone (no UiSurfaceProbe / no " +
                             "'expand VERIFIED PAINTED' emit). The expand trace is back to asserting only that " +
                             "the handler ran, which is true by construction and cannot report a blank rail. " +
                             "See docs/reference/HOLLOW_ASSERTIONS_REGISTRY.md (WO-1221).");
            if (src.IndexOf("resource panel expanded (opener live=", StringComparison.Ordinal) >= 0)
                failures.Add("RESOURCE RAIL — the hollow success 'resource panel expanded (opener live=' is " +
                             "back. That line printed on tmp/resources-expanded-105803.png over a gold-only " +
                             "HUD. Split REQUESTED vs VERIFIED PAINTED; never claim painted from openerLive.");

            // 7c — expanded stack is a CHILD of the gold chip, not a second occupancy widget.
            // Register("resourceChips", WrapAsWidget(..., _resDock)) + RailBand on _resDock is
            // the split-widget defect: occupancy never lists resourceChips, so ApplyPosture
            // deactivates it.
            if (src.IndexOf("Register(\"resourceChips\"", StringComparison.Ordinal) >= 0)
                failures.Add("RESOURCE RAIL — Register(\"resourceChips\") is back. That widget is not in " +
                             "hud-areas.json; occupancy never turns it on and ApplyPosture turns it off. The " +
                             "expanded Wood/Iron/Stone/Crystals pixels must live on the gold chip " +
                             "(resourceChipsCollapsed), not on a second dock. This is the WO-1221 bounce.");
            if (src.IndexOf("_resExpandedRow.transform.SetParent(tapGo.transform", StringComparison.Ordinal) < 0)
                failures.Add("RESOURCE RAIL — _resExpandedRow is not parented to tapGo (the gold chip). " +
                             "A stack that is not a child of the occupancy-live chip cannot appear below it: " +
                             "tmp/resources-expanded-105803.png showed only gold 1034 after a tap that " +
                             "logged expanded. Parent the four rows to the gold chip.");

            // 7d — tap must raise directly. A bool flip that waits for LateTick to SetActive a
            // second widget is how "handler ran, nothing painted" shipped.
            if (src.IndexOf("SetResourcePanelOpen(!_resChipsExpanded)", StringComparison.Ordinal) < 0)
                failures.Add("RESOURCE RAIL — the gold-chip tap no longer calls SetResourcePanelOpen. A " +
                             "bool flip without a SetActive is the original WO-1221 consumer: the log says " +
                             "EXPAND and the screen stays gold-only.");

            // 7e — INACTIVE is a Fail, never a MEASURE_SKIPPED pass. MeasureRect reports
            // inactive as a skip (same bucket as batchmode); the consumer must promote it.
            if (src.IndexOf("resource panel expand INACTIVE", StringComparison.Ordinal) < 0)
                failures.Add("RESOURCE RAIL — TickResourceExpandVerify no longer FAILS when the expanded " +
                             "row is inactive in hierarchy. Inactive is the captured defect " +
                             "(tmp/resources-expanded-105803.png); treating it as a named skip makes the " +
                             "verify green over a gold-only HUD.");

            // 7f — occupancy lists the opener, not a phantom expanded widget.
            CheckResourceRailOccupancy(failures, notes);

            // 7g — hanging-below-gold layout at the captured 2670x1200 must intersect the
            // viewport with non-zero height. Zero-size / offscreen fails here without PlayMode.
            CheckResourceRailLayout2670(src, failures, notes);

            notes.Add("resource-rail raise: gold-chip child + SetActive + UiSurfaceProbe INACTIVE-fail " +
                      "+ occupancy + 2670x1200 hang-below-gold layout pinned (WO-1221) + the chip's " +
                      "AUTHORED band measured against ElarionUiKit.MinTouchPx (7h, WO-1660)");
        }

        /// <summary>7f — calm(town) actionRail occupies resourceChipsCollapsed (the gold chip
        /// the player taps). A copy that drops it hides the opener itself.</summary>
        private static void CheckResourceRailOccupancy(List<string> failures, List<string> notes)
        {
            string[] copies =
            {
                Path.Combine(Application.dataPath, "Resources", "Data", "Canonical", "hud-areas.json"),
                Path.Combine(Application.dataPath, "StreamingAssets", "Data", "Canonical", "hud-areas.json"),
            };

            int found = 0;
            for (int i = 0; i < copies.Length; i++)
            {
                if (!File.Exists(copies[i]))
                {
                    notes.Add("resource-rail occupancy: " + copies[i] + " missing — copy skipped");
                    continue;
                }
                string json;
                try { json = File.ReadAllText(copies[i]); }
                catch (Exception ex)
                {
                    notes.Add("resource-rail occupancy: unreadable " + Path.GetFileName(copies[i]) +
                              " (" + ex.GetType().Name + ")");
                    continue;
                }
                found++;
                if (json.IndexOf("resourceChipsCollapsed", StringComparison.Ordinal) < 0)
                    failures.Add("RESOURCE RAIL — " + copies[i] + " no longer lists resourceChipsCollapsed. " +
                                 "That is the occupancy-live gold chip; without it there is no opener and no " +
                                 "expanded child. WO-1221.");
            }
            if (found == 0)
                failures.Add("RESOURCE RAIL — neither hud-areas.json copy was readable, so occupancy of " +
                             "resourceChipsCollapsed is unchecked.");
        }

        // =====================================================================
        //  THE SHARED RIGHT-COLUMN GEOMETRY (WO-1435)
        // ---------------------------------------------------------------------
        // ⛔ EVERY NUMBER BELOW IS READ FROM SOURCE, NOT TYPED HERE. That is the whole point.
        // 7g used to carry its own copies — `actionRailY0 = 0.040f, actionRailY1 = 0.420f` and
        // `goldAnchorY0 = 0.82f` — and BOTH had gone stale: HudAreasHost.cs authors ActionRail at
        // 0.770..0.965 and BuildResourceChips seated the gold chip at min.y 0.45 (⚠ WO-1660 has
        // since replaced that fraction with a px band off RailChipHeightPx — see the two-form
        // model in ReadRightColumn; the fraction survives only as the revert-detecting fallback).
        // It also used
        // `Mathf.Lerp` for the CanvasScaler factor, which is wrong: MatchWidthOrHeight lerps in
        // LOG space (verified at source, Library/PackageCache/com.unity.ugui@a9ea81766fbd/Runtime/
        // UGUI/UI/Core/Layout/CanvasScaler.cs:328-331), a geometric mean — 1.243 here, not 1.549.
        // An oracle carrying a stale copy of the geometry it audits is the exact duplicated-state
        // failure CLAUDE.md documents four times over, and it is worse here than in a doc: this
        // one reports GREEN while measuring a layout the game does not have.
        //
        // RULING (WO-1435, recorded in-file as CLAUDE.md §15 requires): 7g is CORRECTED, not
        // rewritten — its invariant (the hang-below-gold stack is on-screen with real height) is
        // unchanged and still passes; only its inputs move from hardcoded copies to source reads.
        // Corrected, the four-row stack resolves 583..822 ref px from the canvas bottom — inside
        // the 965 ref px viewport, height 297 device px — so 7g stays GREEN.
        // =====================================================================

        /// <summary>The canvas-local (== reference px) height at a device resolution, using the
        /// LOG-weighted MatchWidthOrHeight factor Unity actually applies.</summary>
        private static float CanvasRefHeight(float screenW, float screenH, float refW, float refH,
                                             float match)
        {
            float logW = Mathf.Log(screenW / refW, 2f);
            float logH = Mathf.Log(screenH / refH, 2f);
            float scale = Mathf.Pow(2f, Mathf.Lerp(logW, logH, match));
            return screenH / scale;
        }

        /// <summary>Read an area mount's y band straight out of HudAreasHost.cs, e.g.
        /// <c>Add(HudArea.QueueStatus, new Vector2(0.780f, 0.510f), new Vector2(0.995f, 0.750f));</c>
        /// Returns false (and NOTES, never a silent default) if the authored form moved.</summary>
        private static bool ReadAreaBandY(string hostSrc, string area, out float y0, out float y1,
                                          List<string> notes)
        {
            y0 = 0f; y1 = 0f;
            var m = Regex.Match(hostSrc,
                @"Add\(\s*HudArea\." + Regex.Escape(area) +
                @"\s*,\s*new\s+Vector2\(\s*[-0-9.]+f?\s*,\s*([-0-9.]+)f\s*\)\s*,\s*new\s+Vector2\(\s*[-0-9.]+f?\s*,\s*([-0-9.]+)f\s*\)");
            if (!m.Success)
            {
                notes.Add("right-column geometry: HudArea." + area + " is no longer authored as a " +
                          "literal Vector2 pair in HudAreasHost.cs (it may have moved to a band table) " +
                          "— the layout half of this check is SKIPPED rather than run on a guess");
                return false;
            }
            return float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y0)
                && float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y1);
        }

        /// <summary>Gold chip anchor min.y within the ActionRail mount, read from the
        /// <c>CurrencyChip(pool, ... CurrencyKind.Gold, new Vector2(x, y), ...)</c> call.
        /// ⚠ WO-1660 RETIRED THIS AS THE PRIMARY READ — the chip's height is now authored in PX
        /// (<c>goldRt.sizeDelta = new Vector2(0f, RailChipHeightPx)</c>), because a fraction
        /// cannot know the tap floor and shipped 103.5 px against MinTouchPx 112. This stays as
        /// the FALLBACK model so a revert of the driver reds 7h instead of silently skipping.</summary>
        private static float ReadGoldChipMinY(string src, float fallback, List<string> notes)
        {
            var m = Regex.Match(src,
                @"CurrencyChip\(\s*pool\s*,\s*ElarionUiKit\.CurrencyKind\.Gold\s*,\s*new\s+Vector2\(\s*[-0-9.]+f\s*,\s*([-0-9.]+)f\s*\)");
            float v;
            if (m.Success &&
                float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            notes.Add("right-column geometry: the gold chip's anchor min.y was not parsed — using " +
                      fallback.ToString("0.##"));
            return fallback;
        }

        /// <summary>7g — pure canvas math at the captured Seeker resolution. The expanded
        /// stack hangs below the gold chip. If that hang is zero height or fully off the
        /// viewport, the player sees only gold — the captured defect.</summary>
        private static void CheckResourceRailLayout2670(string src, List<string> failures, List<string> notes)
        {
            float rowH = ReadConstFloat(src, "ResRowHeightPx", 56f, notes);
            float rowGap = ReadConstFloat(src, "ResRowGapPx", 5f, notes);
            const int rowCount = 4;
            float stackH = rowCount * rowH + (rowCount - 1) * rowGap;
            if (stackH < DeNelle.Core.Diagnostics.UiSurfaceProbe.MinEdgePx)
            {
                failures.Add("RESOURCE RAIL — expanded stack authored height is " + stackH.ToString("0.#") +
                             " ref px, below UiSurfaceProbe.MinEdgePx. Four Wood/Iron/Stone/Crystals rows " +
                             "would measure SURFACE_ZERO_SIZE on device (WO-1221).");
                return;
            }

            RightColumn col;
            if (!ReadRightColumn(src, rowCount, out col, failures, notes)) return;

            // ── 7h — THE AUTHORED BAND IS AT THE TAP FLOOR (WO-1660) ───────────────────────
            // ⛔ RED PROOF, so this pin is not one that was never seen fail. Against the
            // pre-WO-1660 tree — the fractional form `new Vector2(0.05f, 0.45f) .. (1f, 1f)` —
            // this resolves (1 - 0.45) * the ActionRail band at 2670x1200 = 103.5 ref px and
            // FAILS by 8.5. That is the same number the Seeker logged on APK 2026.09.10.363786
            // (Builds/device-frames/2026-09-10_1019_363786_logcat.txt, PID 8062, 10:17:26.858:
            // "authored 398.1x103.5 -> grown 398.1x112"), which is what makes this arithmetic a
            // model of the device rather than of itself. With the px authoring it reads 112.
            // ⛔ THE FLOOR IS READ FROM THE CONSTANT, NEVER RESTATED. And the remedy is never to
            // lower the floor or to lean on ClampMinTouch: the clamp grows the band symmetrically
            // about its centre into BOTH neighbours, which is the consequence, not the fix.
            float touchFloor = DeNelle.Core.UI.ElarionUiKit.MinTouchPx;
            if (col.GoldAuthoredH < touchFloor - 0.5f)
                failures.Add("RESOURCE RAIL — the collapsed gold chip's band is AUTHORED at " +
                             col.GoldAuthoredH.ToString("0.#") + " ref px at 2670x1200, " +
                             (touchFloor - col.GoldAuthoredH).ToString("0.#") + " px UNDER " +
                             "ElarionUiKit.MinTouchPx (" + touchFloor.ToString("0.#") + "). Only " +
                             "ClampMinTouch rescues it at runtime, symmetrically about the centre and " +
                             "into both neighbours — LayoutOracle ASSERT A's own rule text. Author the " +
                             "height in px off RailChipHeightPx, as every sibling rail element does " +
                             "(WO-1660).");

            // Screen-space y from the TOP (capture convention).
            float scale = 2670f / (col.CanvasW <= 0f ? 1f : col.CanvasW);
            var stack = Rect.MinMaxRect(0f, col.StackTop * scale, 2670f, col.StackBottom * scale);
            var viewport = new Rect(0f, 0f, 2670f, 1200f);
            if (stack.height < DeNelle.Core.Diagnostics.UiSurfaceProbe.MinEdgePx)
                failures.Add("RESOURCE RAIL — hanging-below-gold stack at 2670x1200 resolves to " +
                             stack.height.ToString("0") + " px tall (below MinEdgePx). Zero-size rail: " +
                             "the player taps gold and sees nothing (WO-1221).");
            else if (!stack.Overlaps(viewport))
                failures.Add("RESOURCE RAIL — hanging-below-gold stack at 2670x1200 is OFFSCREEN " +
                             "(rect y " + stack.y.ToString("0") + ".." + (stack.y + stack.height).ToString("0") +
                             " px). Wood/Iron/Stone/Crystals would paint outside the Seeker capture, " +
                             "which is how tmp/resources-expanded-105803.png showed only gold.");
        }

        /// <summary>Everything the right column resolves to at the owner's 2670x1200, all of it
        /// derived from source reads. Distances are REFERENCE PX FROM THE CANVAS TOP (the same
        /// convention HudRailClearance works in, so the two cannot disagree about sign).</summary>
        private struct RightColumn
        {
            public float CanvasW, CanvasH;
            public float ActionRailTop, QueueTop, QueueBottom;
            public float GoldTop, GoldBottom;
            /// <summary>WO-1660: the chip's height AS AUTHORED, before any ClampMinTouch rescue.
            /// This is the number ASSERT A measures and the one the device logged at 103.5.</summary>
            public float GoldAuthoredH;
            public float StackTop, StackBottom;
        }

        private static bool ReadRightColumn(string src, int rowCount, out RightColumn c,
                                            List<string> failures, List<string> notes)
        {
            c = default(RightColumn);
            string hostPath = Path.Combine(Application.dataPath, "_Modules", "HUD", "Kit", "HudAreasHost.cs");
            string hostSrc;
            try { hostSrc = File.ReadAllText(hostPath); }
            catch (Exception ex)
            {
                notes.Add("right-column geometry: HudAreasHost.cs unreadable (" + ex.GetType().Name +
                          ") — layout half skipped");
                return false;
            }

            float arY0, arY1, qsY0, qsY1;
            if (!ReadAreaBandY(hostSrc, "ActionRail", out arY0, out arY1, notes)) return false;
            if (!ReadAreaBandY(hostSrc, "QueueStatus", out qsY0, out qsY1, notes)) return false;

            // CanvasScaler as authored in HudAreasHost.Build: 1080x1920, MatchWidthOrHeight 0.5.
            const float screenW = 2670f, screenH = 1200f;
            c.CanvasH = CanvasRefHeight(screenW, screenH, 1080f, 1920f, 0.5f);
            c.CanvasW = screenW * (c.CanvasH / screenH);

            float rowH = ReadConstFloat(src, "ResRowHeightPx", 56f, notes);
            float rowGap = ReadConstFloat(src, "ResRowGapPx", 5f, notes);
            float railGap = ReadConstFloat(src, "RailGapPx", 6f, notes);
            c.ActionRailTop = (1f - arY1) * c.CanvasH;
            c.QueueTop = (1f - qsY1) * c.CanvasH;
            c.QueueBottom = (1f - qsY0) * c.CanvasH;
            float actionRailH = (arY1 - arY0) * c.CanvasH;

            // ⭐ WO-1660 — TWO AUTHORING FORMS, MODELLED EXPLICITLY, NEVER GUESSED.
            // ---------------------------------------------------------------------
            // FORM 1 (current): the chip point-anchors to the ActionRail band's TOP and hangs a
            //   fixed px band off it — `goldRt.sizeDelta = new Vector2(0f, RailChipHeightPx)`,
            //   with `RailChipHeightPx = ElarionUiKit.MinTouchPx`. Height is the constant; the
            //   clamp has nothing to grow, so no growth is folded in.
            // FORM 2 (pre-WO-1660, kept so a revert REDS 7h instead of skipping): the height was
            //   (1 - minY) * the ActionRail band, and ClampMinTouch grew a sub-floor side
            //   "symmetrically about the centre" to MinTouchPx (ElarionUiKit.cs:1172-1186). At
            //   2670x1200 that authored 103.5 ref px and resolved 112 with the top edge 4.25 px
            //   HIGHER than the anchors alone say — which is the WO-1660 device measurement.
            // ⛔ AND NEITHER-FORM IS A FAILURE, NOT A NOTE. ReadGoldChipMinY's regex still MATCHES
            // form 1's `new Vector2(0.05f, 1f)` and would hand back minY 1.0 -> a 0 px chip, i.e.
            // the oracle would model a HUD the game does not have and report on it. An oracle
            // carrying a stale model reports GREEN over a real defect — the failure this whole
            // block was corrected for in WO-1435, and the failure that let 103.5 ship.
            bool pxAuthored =
                Regex.IsMatch(src, @"goldRt\.sizeDelta\s*=\s*new\s+Vector2\(\s*[-0-9.]+f\s*,\s*RailChipHeightPx\s*\)") &&
                src.IndexOf("RailChipHeightPx = ElarionUiKit.MinTouchPx", StringComparison.Ordinal) >= 0;
            float goldH;
            if (pxAuthored)
            {
                goldH = DeNelle.Core.UI.ElarionUiKit.MinTouchPx;
                c.GoldTop = c.ActionRailTop;
            }
            else
            {
                float goldMinY = ReadGoldChipMinY(src, 0.45f, notes);
                if (goldMinY >= 0.99f)
                {
                    failures.Add("RESOURCE RAIL — the gold chip's band matches NEITHER authoring form: " +
                                 "no px `goldRt.sizeDelta = new Vector2(_, RailChipHeightPx)` and the " +
                                 "parsed anchor min.y is " + goldMinY.ToString("0.###") + " (a zero-height " +
                                 "fraction). The right-column model would be measuring a HUD that does not " +
                                 "exist, which is how a sub-floor band ships green. Re-point this parse at " +
                                 "the new authoring in the SAME change (WO-1660).");
                    return false;
                }
                goldH = (1f - goldMinY) * actionRailH;
                c.GoldTop = c.ActionRailTop - Mathf.Max(0f, DeNelle.Core.UI.ElarionUiKit.MinTouchPx - goldH) * 0.5f;
            }
            c.GoldAuthoredH = goldH;
            c.GoldBottom = c.GoldTop + Mathf.Max(goldH, DeNelle.Core.UI.ElarionUiKit.MinTouchPx);
            c.StackTop = c.GoldBottom + railGap;
            c.StackBottom = c.StackTop + rowCount * rowH + (rowCount - 1) * rowGap;
            return true;
        }

        // =====================================================================
        // CHECK 8 — THE HARVEST CHIP NEVER SITS ON THE RESOURCE PANEL (WO-1435)
        // ---------------------------------------------------------------------
        // WHAT SHIPPED (owner felt-test 2026-09-06, build 2026.09.06.358161, verbatim: *"can we
        // move harvest down when someone opens the resource window ... so it doesnt overlap"*):
        // the expanded resource panel's height is `kinds.Length * ResRowHeightPx + ...` — a
        // FUNCTION of its row count, growing downward out of the ActionRail mount — while the
        // Harvest/Collectors chip was pinned in the QueueStatus mount directly beneath it at a
        // CONSTANT `yFromTopPx = 0f`. Two things in one gutter, one variable, nothing reconciling.
        //
        // ⚠ THE NUMBERS BELOW ARE THE WO-1435 RECORD, AT THAT DATE'S AUTHORING. WO-1660 authored
        // the gold chip's band in px instead of as a fraction, which removes the ClampMinTouch
        // growth this model used to fold in — so the chip's top edge no longer sits 4.25 ref px
        // above the ActionRail top, and every distance quoted below shifts DOWN by that much.
        // The check itself is unaffected: every number it compares is DERIVED from source reads
        // (that is the whole point of WO-1435's correction), and the clearance it asserts is
        // RailGapPx at every row count either way. Deliberately not re-typed: a hand-recomputed
        // table is exactly the copy that goes stale next.
        //
        // ⛔ RED PROOF — the numbers this check produced against the pre-WO-1435 tree, at the
        // owner's 2670x1200 (canvas 965.4 ref px tall; CanvasScaler log-weighted factor 1.243;
        // the gold chip's ClampMinTouch growth folded in, see ReadRightColumn):
        //     four-row panel   147.6 .. 386.6 ref px from the canvas top
        //     Harvest chip     241.3 .. 353.3        (yFromTop 0f in a band whose top is 241.3)
        // The chip is ENTIRELY INSIDE the panel — 112 ref px of overlap on a shared right edge and
        // a shared 220 px width, i.e. total. Row pitch is 61, so the covered band 241.3..353.3
        // swallows the whole of row 2 (STONE, 269.6..325.6) and clips the edges of Iron and
        // Crystals — which is exactly what the owner's device shows: coins, wood, iron, a row
        // whose number is under a button, then crystals. This check FAILS with those numbers on
        // today's build and passes only once the offset derives.
        //
        // ⛔ AND IT CANNOT BE SATISFIED BY A BIGGER LITERAL. It runs the SAME geometry at THREE
        // row counts (3 / 4 / 6). A hand-picked constant that clears today's four rows fails at
        // six, which is the point: `kinds.Length` is not fixed, so a second hand-maintained number
        // is the identical bug one resource later — the duplicated-state failure CLAUDE.md
        // documents at §2, §5, §7 and §16. Pre-fix it is RED at every one of the three
        // (overlap 84.2 / 112.0 / 112.0 ref px at 3 / 4 / 6 rows); with the derivation in place
        // the chip lands at 331.6 / 392.6 / 514.6 and clears the panel by exactly RailGapPx each
        // time, still inside the 965.4 ref px canvas (bottom 443.6 / 504.6 / 626.6).
        //
        // Invariants:
        //   8a  the Collectors band carries HudRailClearance (the derivation), and the chip's
        //       literal argument is a resting BASE, not the position.
        //   8b  the derived chip clears the panel at 3, 4 and 6 rows — zero overlap, each stated
        //       with its own numbers.
        //   8c  the chip stays inside the canvas at every one of those row counts (deriving
        //       downward must not push it off the bottom of the screen).
        //   8d  the 220x112 box is untouched — width and the MinTouchPx-floor height are canon
        //       (three rail chips share one right edge; ElarionUiKit.FontFloor is a FLOOR and the
        //       "Tap to collec" fleet capture proved the fix for a tight label is fewer
        //       characters, never a smaller box). A "fix" that shrinks the chip fails here.
        //   8e  fixed pixels only (WO-841): the band must not become a fraction of its parent —
        //       a sub-MinTouchPx fraction is grown about its centre by ClampMinTouch INTO its
        //       neighbour, recreating this overlap by a second route.
        //
        // ⚠ WHAT THIS IS AND IS NOT: authored-anchor ARITHMETIC (the check-7g precedent), not a
        // laid-out measurement.
        // ⛔ CORRECTED 2026-09-06 (WO-1465/1468). This comment used to assert
        // "DeNelle.EditorRegression.asmdef does not reference DeNelle.HUD". THAT IS FALSE and has
        // been false for as long as anyone checked: Assets/Editor/Regression/
        // DeNelle.EditorRegression.asmdef lists "DeNelle.HUD" in its references array, second row.
        // The claim was load-bearing in the wrong direction - it is the stated reason several
        // cases settled for a source lint when they could have measured a real object, and it is
        // repeated at HudLabelFitRegression Case 0. The REAL constraint is the one below, and it
        // still holds: batchmode runs no layout pass (recorded at
        // HudKitController.TickResourceExpandVerify:
        // "UNMEASURABLE => NAMED SKIP, NEVER A PASS"). Every input is READ FROM SOURCE so it cannot
        // go stale; the live measurement is HudRailClearance's own FlowTrace line plus a headless
        // capture with the window open.
        //
        // THE BUILDERS CHIP IS NOT MODELLED HERE, AND THAT IS THE FINDING, NOT AN OMISSION: it
        // shares this band at the same `0f`, but its build call is commented out
        // (`// BuildQueueStatusChip(pool);`) and SessionShapeRegression Case7_OneDoor FAILS if that
        // byte-exact retirement line ever disappears. A chip that cannot be built cannot collide;
        // pinning geometry for it would be a hypothetical, not a pin. It is wired to the same
        // clearance in source so a future un-retirement is safe by construction.
        // =====================================================================
        private static void CheckHarvestChipClearsResourcePanel(string modulesDir,
            List<string> failures, List<string> notes)
        {
            string path = Path.Combine(modulesDir, "HUD", "Kit", "HudKitController.cs");
            string src;
            try { src = File.ReadAllText(path); }
            catch (Exception ex)
            {
                failures.Add("HARVEST CLEARANCE — HudKitController.cs unreadable (" + ex.GetType().Name +
                             "). WO-1435's overlap invariant is not checked at all.");
                return;
            }

            // ── 8a: the derivation must exist, and be attached to THIS chip's band ──────────
            bool hasComponent = src.IndexOf("internal sealed class HudRailClearance",
                                            StringComparison.Ordinal) >= 0;
            if (!hasComponent)
                failures.Add("HARVEST CLEARANCE — HudRailClearance is gone. The Harvest chip's y is " +
                             "back to a constant in a gutter shared with a variable-height panel, which " +
                             "is the WO-1435 defect: the owner's device buried the STONE row's number " +
                             "under the button, and stone is the resource WO-1434 proved fills its " +
                             "2,000 cap in 17 minutes at ~7,050/hour. The one number that would have " +
                             "shown her the problem is the one the button covered.");

            // The rule itself, so a component that exists but stopped deriving still fails.
            bool derives = src.IndexOf("Mathf.Max(BaseYFromTopPx, (mountTop - lowest) + GapPx)",
                                       StringComparison.Ordinal) >= 0;
            if (hasComponent && !derives)
                failures.Add("HARVEST CLEARANCE — HudRailClearance no longer computes " +
                             "max(base, (mountTop - sourceBottom) + gap). A clearance component that " +
                             "does not derive from the MEASURED source bottom is a constant wearing a " +
                             "component's name (WO-1435).");

            var call = Regex.Match(src,
                @"BuildRailChip\(\s*rrt\s*,\s*""CollectorsChip""\s*,\s*HudStrings\.Get\(HudStrings\.KeyCollectorsTitle\)\s*,\s*([-0-9.]+)f");
            if (!call.Success)
            {
                failures.Add("HARVEST CLEARANCE — the Collectors chip's BuildRailChip call was not " +
                             "found in its authored form, so its vertical offset cannot be audited. " +
                             "WO-1435 is unchecked.");
                return;
            }
            float baseY;
            if (!float.TryParse(call.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                                out baseY))
                baseY = 0f;

            bool attached = Regex.IsMatch(src,
                @"collectorsBand\.gameObject\.AddComponent<HudRailClearance>\(\)");
            if (!attached)
                failures.Add("HARVEST CLEARANCE — the Collectors chip's band no longer receives a " +
                             "HudRailClearance. Its " + baseY.ToString("0.#") + "f argument is then the " +
                             "chip's actual position rather than a resting base (WO-1435).");

            // ── 8d/8e: the box and the fixed-pixel law are untouched ────────────────────────
            if (src.IndexOf("RailChipWidthPx = 220f", StringComparison.Ordinal) < 0)
                failures.Add("HARVEST CLEARANCE — RailChipWidthPx is no longer 220. Narrowing the chip " +
                             "is a forbidden route out of this overlap: 220 == " +
                             "EchoUnlockFeedback.EchoChipWidthPx and three rail chips share one right " +
                             "edge. The 2026-08-22 fleet captured this chip reading \"Tap to collec\" in " +
                             "all 8 runs; the fix for a tight label is FEWER CHARACTERS, never a " +
                             "smaller box (WO-1144).");
            if (src.IndexOf("RailChipHeightPx = ElarionUiKit.MinTouchPx", StringComparison.Ordinal) < 0)
                failures.Add("HARVEST CLEARANCE — the rail chip height is no longer authored AT " +
                             "ElarionUiKit.MinTouchPx. Shortening the chip to dodge the panel drops the " +
                             "tap target below the touch floor, and ClampMinTouch then grows it back " +
                             "about its centre INTO its neighbour (WO-841/WO-868).");
            if (Regex.IsMatch(src, @"rt\.anchorMin\s*=\s*new\s+Vector2\(1f,\s*1f\);") == false)
                notes.Add("harvest clearance: RailBand's point anchor was not matched verbatim — the " +
                          "fixed-pixel (never fractional) rail-chrome law is worth re-reading (WO-841)");

            // ── 8b/8c: the geometry, at three row counts ───────────────────────────────────
            float chipH = DeNelle.Core.UI.ElarionUiKit.MinTouchPx;
            float railGap = ReadConstFloat(src, "RailGapPx", 6f, notes);
            int[] rowCounts = { 3, 4, 6 };
            for (int i = 0; i < rowCounts.Length; i++)
            {
                int n = rowCounts[i];
                RightColumn col;
                // A parse failure NOTES and stops the layout half (the check-7f/7g convention in
                // this file) rather than failing — an oracle that cannot read the geometry must
                // not invent a verdict in either direction. The source lints above still stand.
                if (!ReadRightColumn(src, n, out col, failures, notes)) return;

                // The rule, mirrored from HudRailClearance.Apply. It is stated once there and once
                // here on purpose: this half exists to FAIL when the source half is missing, so it
                // must be able to model both worlds.
                float chipTop = col.QueueTop + baseY;
                if (attached)
                    chipTop = Mathf.Max(chipTop, col.StackBottom + railGap);
                float chipBottom = chipTop + chipH;

                if (chipTop < col.StackBottom && chipBottom > col.StackTop)
                {
                    float overlap = Mathf.Min(chipBottom, col.StackBottom) - Mathf.Max(chipTop, col.StackTop);
                    failures.Add("HARVEST CLEARANCE — at " + n + " resource rows the Harvest chip (" +
                                 chipTop.ToString("0.#") + ".." + chipBottom.ToString("0.#") +
                                 " ref px from the canvas top) sits ON the expanded resource panel (" +
                                 col.StackTop.ToString("0.#") + ".." + col.StackBottom.ToString("0.#") +
                                 ") — " + overlap.ToString("0.#") + " ref px of overlap on a shared " +
                                 "220 px right edge, so the covered row's NUMBER is unreadable. This is " +
                                 "WO-1435 (owner felt-test 2026-09-06). Derive the chip's offset from " +
                                 "the panel's laid-out height; a bigger constant is the same bug at a " +
                                 "different row count.");
                }
                if (chipBottom > col.CanvasH)
                    failures.Add("HARVEST CLEARANCE — at " + n + " resource rows the derived chip bottom " +
                                 "(" + chipBottom.ToString("0.#") + " ref px) falls off the " +
                                 col.CanvasH.ToString("0.#") + " ref px canvas at 2670x1200. Deriving " +
                                 "downward must not push the tap target off-screen; HudRailClearance " +
                                 "clamps to the canvas and Warns, and the right column needs a rethink " +
                                 "at this row count.");
            }

            notes.Add("harvest clearance: derived offset + zero panel overlap at 3/4/6 rows + the " +
                      "220x112 box and fixed-pixel laws pinned (WO-1435)");
        }

        private static float ReadConstFloat(string src, string name, float fallback, List<string> notes)
        {
            var m = Regex.Match(src, @"\b" + Regex.Escape(name) + @"\s*=\s*([0-9]+(?:\.[0-9]+)?)f");
            if (!m.Success)
            {
                notes.Add("resource-rail layout: " + name + " not parsed — using " + fallback.ToString("0.#"));
                return fallback;
            }
            float v;
            if (!float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return fallback;
            return v;
        }

        // =====================================================================
        // CHECK 9  [gear-drawer-clearance]  THE OPEN GEAR DRAWER SHARES NO RECT
        //          WITH THE MOVEMENT STICK, AND CANNOT BE PAINTED OVER.
        //                                                       (WO-1465, 2026-09-06)
        // ---------------------------------------------------------------------
        // EVIDENCE this case was written from — Builds/ui-capture/
        // AdaptiveHudGearOpen_2670x1200.png, one frame, two defects:
        //   * the drawer's first row reads "...ERBOARD" — the Night Market card is painted OVER
        //     LEADERBOARD;
        //   * the PAUSE face lands on the analog-stick ring, so "pause" and "move" share a rect.
        //
        // ⭐ THIS IS A MEASUREMENT, NOT A SOURCE LINT, and it is measured against a REAL
        // HudAreasHost: the mounts are instantiated (HudAreasHost.Create) and their anchors read
        // back, then compared with the drawer band HudKitController itself resolves
        // (ResolveDockPanel / ResolveDockCell — the same numbers BuildSlideDock builds with, not
        // a copy). No layout pass is needed: every rect here is an ANCHOR in screen fractions.
        //
        // 9a  the PAUSE cell does not intersect the MoveCluster mount, at both authored aspects;
        // 9b  the whole open drawer does not intersect MoveCluster, the Heart objective, the
        //     hero vitals plate, or the Night Market card's band;
        // 9c  the occlusion half: if the Dock mount is an EARLIER sibling than the Minimap mount
        //     (it is — HudAreasHost adds Dock at line 155, Minimap at 170), then uGUI paints the
        //     card AFTER the drawer and a sibling shuffle inside the dock cannot help. In that
        //     case the drawer MUST carry its own sorting Canvas, and the source pin for it is
        //     required. When the mount order is ever reversed the pin stops being required — the
        //     case follows the real order rather than asserting a remembered one.
        //
        // RED, one line each: give DockPanelSeatAnchorX a `return 0f;` (9a/9b); delete the
        // RaiseDockAboveNeighbourMounts() call in BuildSlideDock (9c).
        //
        // ⚠ ToastZone is deliberately NOT in 9b. Toasts are a transient FEEDBACK overlay that
        // legitimately paints over the town HUD; listing it would make this case fail for a
        // reason WO-1465 is not about.
        // =====================================================================
        private static void CheckGearDrawerClearsNeighbours(List<string> failures, List<string> notes)
        {
            const string Tag = "GEAR DRAWER (WO-1465) —";
            // ASCII section name for the RegressionOutcome.PartialSkip token (Tag carries an
            // em dash, and a stand-down line has to be greppable in a log).
            const string DrawerSection = "GEAR DRAWER (WO-1465)";

            DeNelle.HUD.Kit.HudAreasHost host = null;
            try
            {
                try { host = DeNelle.HUD.Kit.HudAreasHost.Create(null); }
                catch (Exception ex)
                {
                    // HARNESS-CAPABILITY-ABSENT -> RegressionOutcome.PartialSkip (the three-way
                    // rule's visible third column). Instantiating a live HudAreasHost is a thing
                    // batch mode may refuse; that is not a product defect. But a bare notes.Add
                    // told the CALLER nothing and landed in the green column - the arithmetic bug
                    // RegressionOutcome exists to end. The token is what makes it subtractable.
                    notes.Add(DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(DrawerSection,
                        "HudAreasHost could not be instantiated headlessly (" + ex.GetType().Name +
                        ": " + ex.Message + ") - drawer clearance was not measured this run"));
                    return;
                }
                if (host == null)
                {
                    // FIXTURE-ABSENT, NOT A STAND-DOWN (the three-way rule,
                    // RegressionMarkerRegression: fixture-absent -> FAIL naming the missing path;
                    // harness-capability-absent -> RegressionOutcome.PartialSkip;
                    // content/art-absent -> assert THROUGH the proven fallback).
                    //
                    // This arm is NOT a harness capability. HudAreasHost.Create
                    // (Assets/_Modules/HUD/Kit/HudAreasHost.cs:98-105) news a GameObject,
                    // AddComponents the host, calls Build() and returns it - there is no path
                    // through it that yields null, and the throwing path is already caught above
                    // and stood down by name. So a null here is not "the editor cannot do this
                    // headlessly"; it means the HUD's own mount factory returned nothing, and
                    // every clearance below it is UNPROVEN. The old notes.Add + return answered
                    // OK out of a null guard having asserted nothing, which is exactly the
                    // hollow pass the marker names (CLAUDE.md sec.12: an unknown must never read
                    // as a pass). FAIL, naming what was missing.
                    failures.Add(Tag + " HudAreasHost.Create(null) returned NULL without throwing - " +
                                 "Assets/_Modules/HUD/Kit/HudAreasHost.cs:98 constructs and returns " +
                                 "unconditionally, so there is no legitimate shape for this. The area " +
                                 "mount table does not exist this run, so the pause/MoveCluster overlap " +
                                 "and every drawer clearance below are UNPROVEN - not clear, unmeasured.");
                    return;
                }

                Rect move = MountBand(host, DeNelle.HUD.Kit.HudArea.MoveCluster);
                if (move.width <= 0f || move.height <= 0f)
                {
                    // FIXTURE-ABSENT -> FAIL, naming the missing path. This is NOT the headless
                    // layout limitation the arm above is: MountBand reads rt.anchorMin/anchorMax
                    // (this file, MountBand), which are set when the mount is CONSTRUCTED and
                    // need no layout pass. A zero band therefore means one of two real faults -
                    // host.Mount(MoveCluster) returned null (MountBand answers `default`), or the
                    // mount exists with degenerate anchors. Either way the HUD's area table has
                    // no usable MoveCluster, which is the very rect this case measures against.
                    failures.Add(Tag + " the MoveCluster mount resolved to a ZERO band " + R(move) +
                                 " - host.Mount(HudArea.MoveCluster) is null, or its anchors are " +
                                 "degenerate. MountBand reads anchors, not laid-out rects, so this is " +
                                 "not a headless limitation: the area table has no usable MoveCluster, " +
                                 "and the pause/stick overlap the owner felt is UNPROVEN.");
                    return;
                }

                foreach (var a in new[] { new { N = "2670x1200 (the capture)", W = 2670f, H = 1200f },
                                          new { N = "1920x1080",               W = 1920f, H = 1080f } })
                {
                    Rect panel = DeNelle.HUD.Kit.HudKitController.ResolveDockPanel(a.W, a.H);
                    Rect pause = DeNelle.HUD.Kit.HudKitController.ResolveDockCell(
                        panel, DeNelle.HUD.Kit.HudKitController.DockPauseCellIndex, 2, 3);

                    // 9a — the one the owner felt: pause and move may not share a rect.
                    if (HudLayoutBands.Intersects(pause, move))
                        failures.Add(Tag + " at " + a.N + " the PAUSE cell " + R(pause) +
                                     " INTERSECTS the MoveCluster mount " + R(move) +
                                     " — tapping pause and moving the hero share a rect, which is the " +
                                     "captured AdaptiveHudGearOpen defect");

                    // 9b — nothing else in the left column is sat on either.
                    RequireClear(failures, Tag, a.N, "the open drawer", panel, "MoveCluster mount", move);
                    RequireClear(failures, Tag, a.N, "the open drawer", panel,
                                 "the Heart objective band", HudLayoutBands.HeartMount);
                    RequireClear(failures, Tag, a.N, "the open drawer", panel,
                                 "the hero vitals band", HudLayoutBands.VitalsMount);
                    RequireClear(failures, Tag, a.N, "the open drawer", panel, "the Night Market card",
                                 HudLayoutBands.ResolveNightMarketCard(a.W, a.H));

                    notes.Add(Tag + " " + a.N + ": drawer " + R(panel) + ", PAUSE " + R(pause) +
                              ", stick " + R(move));
                }

                // 9c — the occlusion half, driven by the REAL mount order.
                var dockMount = host.Mount(DeNelle.HUD.Kit.HudArea.Dock);
                var minimapMount = host.Mount(DeNelle.HUD.Kit.HudArea.Minimap);
                if (dockMount == null || minimapMount == null)
                    // FIXTURE-ABSENT -> FAIL. hud-areas.json declares both areas in every posture
                    // the drawer can open in, so a null mount is the area table not building what
                    // it declares - not an environment limitation. 9c's whole occlusion argument
                    // rests on the relative order of these two, so without them it is unproven.
                    failures.Add(Tag + " the Dock and/or Minimap mount is ABSENT (dock=" +
                                 (dockMount == null ? "null" : "present") + ", minimap=" +
                                 (minimapMount == null ? "null" : "present") + ") - hud-areas.json " +
                                 "declares both, so the area table did not build what it declares. The " +
                                 "paint order 9c depends on is UNPROVEN, so the Night Market card may " +
                                 "still occlude the open drawer.");
                else
                {
                    int dockIdx = dockMount.GetSiblingIndex();
                    int minimapIdx = minimapMount.GetSiblingIndex();
                    notes.Add(Tag + " mount paint order: dock=" + dockIdx + " minimap=" + minimapIdx);
                    if (dockIdx < minimapIdx)
                    {
                        // hud-areas.json puts "nightMarketCard" in the minimap area and "chatDock"
                        // in the dock area, so the card is painted after the drawer.
                        string hud = ReadHudSource();
                        if (hud == null)
                            // FIXTURE-ABSENT -> FAIL, naming the path. The file is tracked source
                            // at a fixed location; unreadable means the tree moved or the read
                            // threw, and the sorting pin - the ONLY thing standing between the
                            // open drawer and the card painted over it - was not checked.
                            failures.Add(Tag + " Assets/_Modules/HUD/Kit/HudKitController.cs could not be " +
                                         "read, so the drawer's sorting-Canvas pin was NOT checked. With the " +
                                         "Dock mount painting before the Minimap mount, that pin is the only " +
                                         "thing keeping the Night Market card off the open drawer.");
                        else
                        {
                            if (hud.IndexOf("RaiseDockAboveNeighbourMounts();", StringComparison.Ordinal) < 0)
                                failures.Add(Tag + " the Minimap mount (sibling " + minimapIdx + ") paints AFTER " +
                                             "the Dock mount (sibling " + dockIdx + "), so the Night Market card " +
                                             "occludes an opened menu — and BuildSlideDock no longer calls " +
                                             "RaiseDockAboveNeighbourMounts(). That is the captured '...ERBOARD'.");
                            if (hud.IndexOf("canvas.overrideSorting = true;", StringComparison.Ordinal) < 0)
                                failures.Add(Tag + " the drawer's sorting Canvas no longer sets overrideSorting — " +
                                             "without it the nested Canvas inherits the host order and the card wins again");
                            if (hud.IndexOf("GraphicRaycaster>() == null) go.AddComponent<GraphicRaycaster>()",
                                            StringComparison.Ordinal) < 0)
                                failures.Add(Tag + " the drawer's sorting Canvas has no GraphicRaycaster of its own — " +
                                             "a nested Canvas registers its graphics to ITSELF, so every menu row goes dead");
                        }
                    }
                    else notes.Add(Tag + " the Dock mount already paints after the Minimap mount — the sorting " +
                                         "Canvas is belt-and-braces at this mount order, so its pin is not required");
                }
            }
            finally
            {
                if (host != null && host.gameObject != null)
                    UnityEngine.Object.DestroyImmediate(host.gameObject);
            }
        }

        /// <summary>A mount's band in SCREEN fractions. Every HudAreasHost mount is anchored
        /// inside a full-stretch canvas root, so its anchors ARE screen fractions.</summary>
        private static Rect MountBand(DeNelle.HUD.Kit.HudAreasHost host, DeNelle.HUD.Kit.HudArea area)
        {
            var rt = host == null ? null : host.Mount(area);
            if (rt == null) return default;
            return Rect.MinMaxRect(rt.anchorMin.x, rt.anchorMin.y, rt.anchorMax.x, rt.anchorMax.y);
        }

        private static void RequireClear(List<string> failures, string tag, string aspect,
                                         string whoName, Rect who, string otherName, Rect other)
        {
            if (other.width <= 0f || other.height <= 0f) return;
            if (HudLayoutBands.Intersects(who, other))
                failures.Add(tag + " at " + aspect + " " + whoName + " " + R(who) +
                             " INTERSECTS " + otherName + " " + R(other));
        }

        private static string R(Rect r)
        {
            return "[x " + r.xMin.ToString("0.000") + ".." + r.xMax.ToString("0.000") +
                   ", y " + r.yMin.ToString("0.000") + ".." + r.yMax.ToString("0.000") + "]";
        }

        private static string ReadHudSource()
        {
            try
            {
                return File.ReadAllText(Path.Combine(Application.dataPath,
                    "_Modules/HUD/Kit/HudKitController.cs"));
            }
            catch { return null; }
        }

        // =====================================================================
        // CHECK 10  [stack-badge-inside-medallion]  THE CHARGE BADGE IS CONTAINED
        //           BY THE ROUND FACE IT BELONGS TO.        (WO-1468, 2026-09-06)
        // ---------------------------------------------------------------------
        // EVIDENCE: Builds/ui-capture/AdaptiveHudCombat_2670x1200.png (charge "0" outside the
        // frame) and the owner's device build 358574 (a "7", same place). Two aspects, one defect.
        //
        // ⛔ THE TEST THAT WOULD HAVE LIED. "The badge is inside the SLOT rect" is TRUE today —
        // ElarionUiKit.StyleAsStackBadge anchors it to the slot root's own top-right corner with
        // a 3 px inset — and "inside the ActionBarHousing rect" is true as well, because the
        // housing stretches the entire mount. Either one is green while the player sees the digit
        // outside the frame. What the eye calls "the frame" is the MEDALLION: the square
        // "MedallionBounds" child that StyleAsRoundMedallion inscribes in the top 80% of the
        // (wider-than-tall) cell. That is the rect measured here.
        //
        // ⭐ REAL OBJECTS, AND A SELF-PROVING RED. The case builds TWO slots through the shipping
        // kit calls: one seated by HudKitController.SeatStackBadgeInMedallion (must be contained)
        // and one left at the kit's default corner (must NOT be). If the unseated control ever
        // measures as contained, the oracle has stopped measuring anything and says so — that is
        // the RED proof, executed rather than asserted. If layout cannot resolve headlessly the
        // case is a NAMED SKIP, never a pass.
        //
        // RED, one line: delete the SeatStackBadgeInMedallion(...) call in BuildAdaptiveCombatDock.
        // =====================================================================
        private static void CheckStackBadgeInsideMedallion(List<string> failures, List<string> notes)
        {
            const string Tag = "STACK BADGE (WO-1468) —";
            // ASCII section name for the RegressionOutcome.PartialSkip token (Tag carries an
            // em dash, and a stand-down line has to be greppable in a log).
            const string BadgeSection = "STACK BADGE (WO-1468)";
            GameObject canvasGo = null;
            try
            {
                canvasGo = new GameObject("HudUiRegression_BadgeProbe", typeof(Canvas));
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var canvasRt = (RectTransform)canvasGo.transform;

                // A cell shaped like the shipping one: the ActionBar mount is 0.460 of 2670 wide
                // over six faces (~205 px) and 0.135 of 1200 tall (~162 px). Wider than tall is
                // the whole point — that is what puts the cell's corner outside the round face.
                var seated = BuildProbeSlot(canvasRt, seat: true);
                var unseated = BuildProbeSlot(canvasRt, seat: false);

                Canvas.ForceUpdateCanvases();
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRt);
                Canvas.ForceUpdateCanvases();

                Rect medallion, badge;
                string absent;
                if (!ProbeRects(seated, out medallion, out badge, out absent))
                {
                    // THE THREE-WAY RULE, both live arms of it. The old single line answered OK
                    // out of a negated call having asserted nothing (scanner arm C).
                    if (absent != null)
                        // FIXTURE-ABSENT -> FAIL, naming the missing path. A badge that was never
                        // built is not an unmeasurable run; it is the WO-1468 defect itself, and
                        // standing down on it is the oracle refusing to see its own subject.
                        failures.Add(Tag + " the probe hierarchy is INCOMPLETE - " + absent +
                                     ". The charge badge was not built or not seated, so the shipped " +
                                     "ITEM '0' outside the bar frame is UNPROVEN - not absent, unmeasured.");
                    else
                        // HARNESS-CAPABILITY-ABSENT -> PartialSkip. Both widgets exist; batch mode
                        // simply did not give them a layout pass, so there is nothing to measure
                        // and nothing to blame the product for.
                        notes.Add(DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(BadgeSection,
                            "MedallionBounds and StackBadge both exist but measured degenerate rects - " +
                            "headless batch ran no layout pass, so badge containment was not measured"));
                    return;
                }

                if (!Contains(medallion, badge))
                    failures.Add(Tag + " the SEATED charge badge " + RectPx(badge) + " is NOT contained by the " +
                                 "medallion face " + RectPx(medallion) + " — that is the captured ITEM '0' " +
                                 "sitting outside the bar frame");
                else
                    notes.Add(Tag + " seated badge " + RectPx(badge) + " inside medallion " + RectPx(medallion));

                Rect medallion2, badge2;
                string absent2;
                if (ProbeRects(unseated, out medallion2, out badge2, out absent2))
                {
                    if (Contains(medallion2, badge2))
                        failures.Add(Tag + " the UNSEATED control badge " + RectPx(badge2) + " also measures inside " +
                                     "the medallion " + RectPx(medallion2) + " — this case cannot go RED, so it is " +
                                     "not measuring the defect it was written for. Fix the oracle before trusting it.");
                    else
                        notes.Add(Tag + " RED proof holds: the kit's default corner seat " + RectPx(badge2) +
                                  " measures OUTSIDE the medallion " + RectPx(medallion2));
                }
                // The RED control, under the same three-way split. A missing widget on the CONTROL
                // is still a fixture fault (the control is what proves this case can go red at
                // all); degenerate rects on it are the harness.
                else if (absent2 != null)
                    failures.Add(Tag + " the UNSEATED control's hierarchy is INCOMPLETE - " + absent2 +
                                 ". The RED proof cannot be exercised, so a green result above proves " +
                                 "nothing about whether this case can still fail.");
                else
                    notes.Add(DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(BadgeSection,
                        "the unseated RED control measured degenerate rects (no headless layout pass) - " +
                        "the seated assertion ran, but its red-proof control did not"));
            }
            catch (Exception ex)
            {
                // HARNESS-CAPABILITY-ABSENT -> declared stand-down, not a silent green. Building a
                // live Canvas and forcing a layout is something batch mode may refuse outright;
                // that is not a product defect, but the run must SAY it asserted nothing here.
                notes.Add(DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(BadgeSection,
                    "the probe threw " + ex.GetType().Name + ": " + ex.Message +
                    " - badge containment was not measured this run"));
            }
            finally
            {
                if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
            }
        }

        private static ElarionUiKit.ActionSlotHandle BuildProbeSlot(RectTransform canvasRt, bool seat)
        {
            var cell = new GameObject(seat ? "SeatedCell" : "UnseatedCell", typeof(RectTransform));
            cell.transform.SetParent(canvasRt, false);
            var crt = (RectTransform)cell.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(205f, 162f);   // the shipping cell's shape (see the header)
            crt.anchoredPosition = seat ? new Vector2(-300f, 0f) : new Vector2(300f, 0f);

            var slot = ElarionUiKit.BuildActionSlot(cell.transform, Vector2.zero, Vector2.one);
            if (slot == null) return null;
            ElarionUiKit.StyleAsRoundMedallion(slot);
            ElarionUiKit.StyleAsStackBadge(slot);
            if (seat) DeNelle.HUD.Kit.HudKitController.SeatStackBadgeInMedallion(slot);
            return slot;
        }

        /// <summary>
        /// Probe the two rects. FALSE has TWO CAUSES AND THEY ARE NOT THE SAME EVENT, which is
        /// why <paramref name="absent"/> exists: a single bool forced every caller to treat a
        /// MISSING WIDGET (a product defect - StyleAsStackBadge / SeatStackBadgeInMedallion did
        /// not run, i.e. verbatim what WO-1468 pins) as if it were a HARNESS limitation, and the
        /// only honest thing left to do with that bool was stand down. That is the hollow pass.
        ///
        /// absent != null  -> a named child is not in the hierarchy. FIXTURE-ABSENT: the caller
        ///                    FAILs, naming it.
        /// absent == null  -> the children exist but their rects measured degenerate, which
        ///                    headless batch mode legitimately does (no layout pass).
        ///                    HARNESS-CAPABILITY-ABSENT: the caller PartialSkips, naming it.
        /// </summary>
        private static bool ProbeRects(ElarionUiKit.ActionSlotHandle slot, out Rect medallion,
                                       out Rect badge, out string absent)
        {
            medallion = default; badge = default; absent = null;
            if (slot == null || slot.root == null)
            {
                absent = "the probe slot itself (BuildProbeSlot returned no root)";
                return false;
            }
            var bounds = slot.root.transform.Find("MedallionBounds") as RectTransform;
            var plate = FindDeepRect(slot.root.transform, "StackBadge");
            if (bounds == null || plate == null)
            {
                absent = bounds == null && plate == null
                    ? "both 'MedallionBounds' and 'StackBadge' are missing under " + slot.root.name
                    : (bounds == null ? "'MedallionBounds' is missing under " + slot.root.name
                                      : "'StackBadge' is missing under " + slot.root.name +
                                        " - ElarionUiKit.StyleAsStackBadge did not build a badge");
                return false;
            }
            medallion = WorldRect(bounds);
            badge = WorldRect(plate);
            return medallion.width > 0.01f && medallion.height > 0.01f &&
                   badge.width > 0.01f && badge.height > 0.01f;
        }

        private static RectTransform FindDeepRect(Transform root, string name)
        {
            if (root == null) return null;
            if (string.Equals(root.name, name, StringComparison.Ordinal)) return root as RectTransform;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindDeepRect(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        private static readonly Vector3[] s_corners = new Vector3[4];

        private static Rect WorldRect(RectTransform rt)
        {
            rt.GetWorldCorners(s_corners);
            float xMin = Mathf.Min(s_corners[0].x, s_corners[2].x);
            float xMax = Mathf.Max(s_corners[0].x, s_corners[2].x);
            float yMin = Mathf.Min(s_corners[0].y, s_corners[2].y);
            float yMax = Mathf.Max(s_corners[0].y, s_corners[2].y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// <summary>Containment with a half-pixel tolerance (a corner-anchored plate legitimately
        /// touches the rim it is seated against).</summary>
        private static bool Contains(Rect outer, Rect inner)
        {
            const float Slack = 0.5f;
            return inner.xMin >= outer.xMin - Slack && inner.xMax <= outer.xMax + Slack &&
                   inner.yMin >= outer.yMin - Slack && inner.yMax <= outer.yMax + Slack;
        }

        private static string RectPx(Rect r)
        {
            return "[x " + r.xMin.ToString("0.0") + ".." + r.xMax.ToString("0.0") +
                   ", y " + r.yMin.ToString("0.0") + ".." + r.yMax.ToString("0.0") + "]";
        }

    }
}
