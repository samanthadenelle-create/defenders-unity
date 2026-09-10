// =============================================================================
// KnightCombatDockIconRegression — pins that the ACTIVE COMBAT dock's faces never
// paint the crossed-swords DEFAULT where an authored icon (or the owner's word
// placeholder) belongs.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Namespace: DeNelle.Editor.Regression
// Marker  : KNIGHT_COMBAT_DOCK_ICON_OK / KNIGHT_COMBAT_DOCK_ICON_FAIL
//           (suite tag [knight-combat-dock-icons])
//
// THE DEFECT THIS EXISTS FOR (WO-1695, owner Seeker frame
// Builds/device-frames/2026-09-10_1520_owner_icons.png, build 363866, battle arena,
// Knight "Grom Lv 2" — owner verbatim: "knight icons in bottom are default not the
// icons we created"):
//   * face 0 ATTACK  — wore icons/icon_combat (the crossed sword+axe). The producer
//     forces IconKey = "text:Dodge/\nAttack" for knight.q (HudModelProducers.cs:617-621,
//     owner placeholder 2026-07-11). The rail honours that prefix
//     (HudKitController OnAbilities, the `StartsWith("text:")` branch); the combat dock
//     did not, so it asked UiStyle for a concept named "text:Dodge/\nAttack", got null,
//     and ActionSlotHandle.SetIcon's never-blank law substituted
//     ConceptIconResolver.DefaultSprite().
//   * faces 2/3/4 EMPTY — called SetIcon(null) on an unassigned hot-swap slot, hitting
//     the SAME backstop, so an EMPTY face wore the identical attack glyph as a live one.
//
// WHY IT ASSERTS ON REAL OBJECTS. Every one of these failures is invisible in source
// (`SetIcon(null)` reads as "no icon") and invisible in the log (the substitution is
// silent by design). The only place the defect exists is the sprite that ends up on
// the Image, so the suite BUILDS the shipping dock through
// HudKitController.BuildCombatDockProbe and compares sprite REFERENCES against
// ConceptIconResolver.DefaultSprite(). Re-pointing a concept row, renaming art, or
// re-introducing the fallback are all caught the same way: by what the player sees.
//
// RED ON HEAD, one line each:
//   * face 0 — delete the `text:` branch in HudKitController.ApplyCombatPrimaryFaceArt
//     (that restores the exact `SetLabel(null); SetIcon(UiStyle.Icon(key))` HEAD shipped);
//   * faces 2/3/4 — replace the SetEmptyCombatDockFace(h) calls in OnAssignable with
//     `h.SetIcon(null)`.
// Either revert puts DefaultSprite() back on the face and this suite FAILS.
//
// NO HOLLOW PASS: if the dock cannot be built headlessly, or DefaultSprite() itself is
// null (so "is not the default" would be vacuously true for everything), the suite
// FAILS rather than passing quietly.
// =============================================================================

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DeNelle.Core.UI;
using DeNelle.HUD.Kit;

namespace DeNelle.Editor.Regression
{
    public static class KnightCombatDockIconRegression
    {
        /// <summary>The in-band prefix AbilityLoadoutProducer uses for a TEXT face.</summary>
        private const string TextPrefix = "text:";

        /// <summary>The owner's 2026-07-11 placeholder body, as the producer emits it.</summary>
        private const string KnightPrimaryPlaceholder = "text:Dodge/\nAttack";

        public static bool Run(out string report)
        {
            var failures = new List<string>();
            var notes = new List<string>();

            ConceptIconResolver.ClearCache();

            Sprite defaultSprite = ConceptIconResolver.DefaultSprite();
            if (defaultSprite == null)
            {
                report = "KNIGHT_COMBAT_DOCK_ICON_FAIL: ConceptIconResolver.DefaultSprite() is null, so " +
                         "'this face is not the crossed-swords default' is vacuously true for every face " +
                         "below and nothing would be pinned. Fix the concept-icons 'default' row / its art " +
                         "before trusting a green here.";
                return false;
            }

            // ── 1. The producer still emits the placeholder this face has to honour ──────────
            // If the owner retires the word placeholder, this NOTE tells the next reader that the
            // text: branch is now unexercised by the Knight — it is not a failure either way.
            string producerPath = Path.Combine(Application.dataPath,
                "_Modules/Village/HUD/HudModelProducers.cs");
            bool placeholderLive = File.Exists(producerPath) &&
                                   File.ReadAllText(producerPath).Contains("\"text:Dodge/");
            notes.Add(placeholderLive
                ? "knight.q still carries the owner's 2026-07-11 word placeholder (producer emits text:Dodge/Attack)"
                : "knight.q no longer carries the word placeholder - the primary face now paints its authored icon");

            // ── 2. The authored knight.q art is reachable, so retiring the placeholder is safe ─
            Sprite knightQ = ConceptIconResolver.Resolve("knight.q");
            if (knightQ == null)
                failures.Add("[knight-combat-dock-icons] concept 'knight.q' resolves NO sprite. concept-icons.json " +
                             "authored knight.q -> abilities/charge_knight on 2026-07-06 (commit ef4b3cde5); with " +
                             "that row broken the primary face has nothing to fall back to when the word " +
                             "placeholder is retired, and lands on the crossed-swords default again.");
            else if (ReferenceEquals(knightQ, defaultSprite))
                failures.Add("[knight-combat-dock-icons] concept 'knight.q' resolves the icon_combat DEFAULT " +
                             "itself - its concept row is gone or its art is missing.");
            else
                notes.Add("knight.q -> " + knightQ.name);

            // ── 3. THE WIRING, not just the helpers ─────────────────────────────────────────
            // ⚠ WITHOUT THIS SECTION THE SUITE IS A LIE. Everything else here calls the probe
            // seams DIRECTLY, so inlining the old `SetLabel(null); SetIcon(UiStyle.Icon(key))`
            // back into OnAbilities — or swapping SetEmptyCombatDockFace(h) back to
            // h.SetIcon(null) in OnAssignable — would leave the helpers correct, the faces broken,
            // and this suite GREEN. That is exactly the "a claim without evidence" failure
            // CLAUDE.md §11B names. Source-read precedent: MageProtectionDockRegression (which
            // reads HudKitController.cs the same way) and CopyHygieneRegression's call counting.
            string hudPath = Path.Combine(Application.dataPath, "_Modules/HUD/Kit/HudKitController.cs");
            if (!File.Exists(hudPath))
            {
                failures.Add("[knight-combat-dock-icons] HudKitController.cs not found at " + hudPath +
                             " - the wiring cannot be pinned, so this is a FAIL, not a skip.");
            }
            else
            {
                string hud = File.ReadAllText(hudPath);
                if (Count(hud, "ApplyCombatPrimaryFaceArt(primary, q.IconKey)") != 1)
                    failures.Add("[knight-combat-dock-icons] the combat dock's PRIMARY face no longer routes " +
                                 "through ApplyCombatPrimaryFaceArt(primary, q.IconKey). If OnAbilities went back " +
                                 "to `SetLabel(null); SetIcon(UiStyle.Icon(q.IconKey))`, the owner's text: " +
                                 "placeholder resolves nothing and the face paints icons/icon_combat again - the " +
                                 "shipped WO-1695 defect, with the helper below still passing.");
                if (hud.Contains("primary.SetIcon(string.IsNullOrEmpty(q.IconKey)"))
                    failures.Add("[knight-combat-dock-icons] the pre-WO-1695 primary-face line is back in " +
                                 "OnAbilities (`primary.SetIcon(string.IsNullOrEmpty(q.IconKey) ...`).");
                if (Count(hud, "SetEmptyCombatDockFace(h)") != 2)
                    failures.Add("[knight-combat-dock-icons] OnAssignable's combat-dock branch no longer calls " +
                                 "SetEmptyCombatDockFace(h) for BOTH unassigned cases (missing slot and " +
                                 "unequipped slot). An unassigned face reverts to SetIcon(null) -> the " +
                                 "icon_combat default under the caption EMPTY.");
            }

            // ── 4. Build the SHIPPING combat dock and read the faces out of the tree ─────────
            GameObject canvasGo = null;
            try
            {
                canvasGo = new GameObject("KnightCombatDockIconProbe", typeof(Canvas));
                canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

                var kitGo = new GameObject("Kit", typeof(RectTransform));
                kitGo.transform.SetParent(canvasGo.transform, false);
                var kit = kitGo.AddComponent<HudKitController>();

                var pool = new GameObject("Pool", typeof(RectTransform));
                pool.transform.SetParent(kitGo.transform, false);

                ElarionUiKit.ActionSlotHandle[] faces = kit.BuildCombatDockProbe(pool.transform);
                if (faces == null || faces.Length < 6)
                {
                    failures.Add("[knight-combat-dock-icons] BuildCombatDockProbe returned " +
                                 (faces == null ? "null" : faces.Length + " face(s)") +
                                 " - the dock the player touches did not construct, so nothing below was " +
                                 "measured. Never read this as 'no faces to check'.");
                }
                else
                {
                    CheckPrimaryTextFace(faces[0], defaultSprite, failures, notes);
                    CheckPrimaryIconFace(faces[0], knightQ, defaultSprite, failures, notes);
                    CheckEmptyFace(faces[2], defaultSprite, failures, notes);
                }
            }
            catch (System.Exception ex)
            {
                failures.Add("[knight-combat-dock-icons] the combat dock threw while building headlessly: " +
                             ex.GetType().Name + ": " + ex.Message + ". A dock that cannot be built cannot be " +
                             "pinned - this is a FAIL, not a skip.");
            }
            finally
            {
                if (canvasGo != null) Object.DestroyImmediate(canvasGo);
            }

            var sb = new System.Text.StringBuilder();
            sb.Append(failures.Count == 0 ? "KNIGHT_COMBAT_DOCK_ICON_OK" : "KNIGHT_COMBAT_DOCK_ICON_FAIL");
            for (int i = 0; i < notes.Count; i++) sb.Append("\n    ").Append(notes[i]);
            for (int i = 0; i < failures.Count; i++) sb.Append("\n  ").Append(failures[i]);
            report = sb.ToString();
            return failures.Count == 0;
        }

        /// <summary>The owner's word placeholder must render as WORDS, never as a substituted glyph.</summary>
        private static void CheckPrimaryTextFace(ElarionUiKit.ActionSlotHandle face, Sprite defaultSprite,
                                                 List<string> failures, List<string> notes)
        {
            if (face == null) { failures.Add("[knight-combat-dock-icons] combat dock face 0 is null"); return; }

            HudKitController.ApplyCombatPrimaryFaceArtProbe(face, KnightPrimaryPlaceholder);

            string shown = face.label != null && face.label.gameObject.activeSelf ? face.label.text : null;
            if (string.IsNullOrEmpty(shown))
                failures.Add("[knight-combat-dock-icons] the PRIMARY face dropped the owner's in-band '" +
                             TextPrefix + "' text mode: IconKey '" + KnightPrimaryPlaceholder.Replace("\n", "\\n") +
                             "' rendered no words. This is the shipped defect - the face then falls to " +
                             "ConceptIconResolver.DefaultSprite() (icons/icon_combat) and the Knight's ATTACK " +
                             "button wears a generic crossed-swords glyph (owner frame " +
                             "Builds/device-frames/2026-09-10_1520_owner_icons.png).");
            else if (shown.Replace("\n", "").Replace("\r", "") != "Dodge/Attack")
                failures.Add("[knight-combat-dock-icons] the PRIMARY text face reads '" + shown.Replace("\n", "\\n") +
                             "', not the owner's 'Dodge/Attack'.");
            else
                notes.Add("primary text face renders the owner placeholder words");

            if (face.icon != null && face.icon.enabled && ReferenceEquals(face.icon.sprite, defaultSprite))
                failures.Add("[knight-combat-dock-icons] the PRIMARY face is showing the icon_combat DEFAULT " +
                             "sprite UNDER the text mode - the words and the wrong glyph at once.");
        }

        /// <summary>A normal concept key must paint THAT concept's art, never the default.</summary>
        private static void CheckPrimaryIconFace(ElarionUiKit.ActionSlotHandle face, Sprite knightQ,
                                                 Sprite defaultSprite, List<string> failures, List<string> notes)
        {
            if (face == null || knightQ == null) return;

            HudKitController.ApplyCombatPrimaryFaceArtProbe(face, "knight.q");

            if (face.icon == null)
            {
                failures.Add("[knight-combat-dock-icons] combat dock face 0 has no icon Image at all.");
                return;
            }
            if (ReferenceEquals(face.icon.sprite, defaultSprite))
                failures.Add("[knight-combat-dock-icons] the PRIMARY face painted the icon_combat DEFAULT for the " +
                             "AUTHORED concept 'knight.q'. The concept row resolves art (" + knightQ.name +
                             ") but the face did not use it.");
            else if (!ReferenceEquals(face.icon.sprite, knightQ))
                failures.Add("[knight-combat-dock-icons] the PRIMARY face painted '" +
                             (face.icon.sprite != null ? face.icon.sprite.name : "<null>") +
                             "' for concept 'knight.q', expected the concept-icons row's art '" + knightQ.name + "'.");
            else if (!face.icon.enabled)
                failures.Add("[knight-combat-dock-icons] the PRIMARY face resolved 'knight.q' art but left the icon " +
                             "DISABLED - it is still in text mode, so the player sees no icon.");
            else
                notes.Add("primary icon face paints knight.q -> " + knightQ.name);
        }

        /// <summary>Non-overlapping occurrences of <paramref name="needle"/> in
        /// <paramref name="haystack"/> — the same counting idiom CopyHygieneRegression uses.</summary>
        private static int Count(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return 0;
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0)
            { n++; i += needle.Length; }
            return n;
        }

        /// <summary>An unassigned hot-swap face must read as EMPTY, not as a live attack.</summary>
        private static void CheckEmptyFace(ElarionUiKit.ActionSlotHandle face, Sprite defaultSprite,
                                           List<string> failures, List<string> notes)
        {
            if (face == null) { failures.Add("[knight-combat-dock-icons] combat dock face 2 is null"); return; }

            HudKitController.SetEmptyCombatDockFaceProbe(face);

            if (face.icon != null && face.icon.enabled && ReferenceEquals(face.icon.sprite, defaultSprite))
                failures.Add("[knight-combat-dock-icons] an UNASSIGNED hot-swap face is painting the icon_combat " +
                             "DEFAULT - three of these sat under the caption EMPTY in the owner's arena frame " +
                             "(2026-09-10_1520_owner_icons.png), reading as three live attack buttons. The " +
                             "authored empty treatment is the dimmed '+' plate (SetEmptyMedallion, WO-611/917).");
            else
                notes.Add("empty hot-swap face does not paint the crossed-swords default");

            if (face.label == null || !face.label.gameObject.activeSelf ||
                (face.label.text ?? string.Empty).Trim() != "+")
                failures.Add("[knight-combat-dock-icons] an UNASSIGNED hot-swap face lost the authored '+' plate " +
                             "(WO-611 + WO-917 Phase B: 'an unassigned slot is a dimmed + plate, not a blank').");

            if (face.button != null && face.button.interactable)
                failures.Add("[knight-combat-dock-icons] an UNASSIGNED hot-swap face is INTERACTABLE. Its onClick is " +
                             "HudCommands.AssignableCast(i), so a tap would dispatch a cast for a slot that holds " +
                             "no ability. SetEmptyMedallion re-enables the button for the RAIL's toast - the dock " +
                             "must switch it back off AFTER that call.");
        }
    }
}
