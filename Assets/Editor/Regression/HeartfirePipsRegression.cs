// =============================================================================
// HeartfirePipsRegression [heartfire-pips]
// WO-1419: the player-facing Heart plate uses real flame Images, never ASCII pips.
// Markers: HEARTFIRE_PIPS_OK / HEARTFIRE_PIPS_FAIL.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DeNelle.Core.State;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class HeartfirePipsRegression
    {
        private const string HudPath = "Assets/_Modules/HUD/Kit/HudKitController.cs";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            string hud = File.Exists(HudPath) ? File.ReadAllText(HudPath) : string.Empty;
            if (hud.Length == 0)
                failures.Add("[no-ascii-pips-on-plate] HudKitController.cs is missing or empty");

            // RED: restore `_heartfireLabel.text = flames + HeartfireMarksGap + label`.
            string repaint = Body(hud, "private void RepaintHeartfire(bool force)",
                "private void RepaintHeartfireFlameSlots(bool[] states)");
            if (repaint == null || repaint.Contains("_heartfireLabel.text = flames") ||
                !repaint.Contains("_heartfireLabel.text = label"))
                failures.Add("[no-ascii-pips-on-plate] the Heart plate still prefixes its label with FlameRow text");
            if (repaint == null || !repaint.Contains("if (force || countMoved)") ||
                !repaint.Contains("RepaintHeartfireFlameSlots"))
                failures.Add("[no-ascii-pips-on-plate] flame Images are not repainted on the label's count-change gate");

            // RED: remove the upper clamp in HeartfireCharges.FlameStates.
            bool[] two = HeartfireCharges.FlameStates(2, 3);
            bool[] zero = HeartfireCharges.FlameStates(0, 3);
            bool[] five = HeartfireCharges.FlameStates(5, 3);
            if (!Matches(two, true, true, false) || !Matches(zero, false, false, false) ||
                !Matches(five, true, true, true))
                failures.Add("[slot-count] FlameStates does not clamp and project [lit, lit, spent] correctly");

            // RED: change HeartfireFlameSpritePath to a missing Resources key.
            string spritePath;
            if (!TryStringConst(hud, "HeartfireFlameSpritePath", out spritePath) ||
                !string.Equals(spritePath, "ItemIcons/cons_emberfire_bomb", StringComparison.Ordinal) ||
                Resources.Load<Sprite>(spritePath) == null)
                failures.Add("[sprite-loads] chosen Heartfire flame sprite does not resolve through Resources.Load<Sprite>");

            // RED: set HeartfireFlameSpentAlpha to 0.75f (alpha delta drops below 0.5).
            float litAlpha, spentAlpha, spentGray;
            if (!TryFloatConst(hud, "HeartfireFlameLitAlpha", out litAlpha) ||
                !TryFloatConst(hud, "HeartfireFlameSpentAlpha", out spentAlpha) ||
                !TryFloatConst(hud, "HeartfireFlameSpentGray", out spentGray))
                failures.Add("[states-differ-in-greyscale] flame treatment constants are missing or non-literal");
            else
            {
                if (Math.Abs(litAlpha - spentAlpha) < 0.5f)
                    failures.Add("[states-differ-in-greyscale] lit/spent alpha delta is below 0.5");
                if (Math.Abs(litAlpha - 1.0f) > 0.001f || Math.Abs(spentAlpha - 0.25f) > 0.001f ||
                    Math.Abs(spentGray - 0.55f) > 0.001f)
                    failures.Add("[states-differ-in-greyscale] owner-selected 1.0/0.25 alpha and 0.55 grey treatment drifted");
            }
            string slotPainter = Body(hud, "private void RepaintHeartfireFlameSlots(bool[] states)",
                "private void RepaintHeartObjective(bool force)");
            if (slotPainter == null || !slotPainter.Contains("HeartfireFlameLitAlpha") ||
                !slotPainter.Contains("HeartfireFlameSpentAlpha") ||
                !slotPainter.Contains("HeartfireFlameSpentGray"))
                failures.Add("[states-differ-in-greyscale] slot Images do not consume the pinned treatment constants");

            // RED: delete ` + SpendTag` from HeartfireCharges.PlateLabel.
            string charged = HeartfireCharges.PlateLabel(3, 3);
            string spent = HeartfireCharges.PlateCombined(
                HeartfireCharges.PlateLabel(0, 3),
                HeartfireCharges.PlateRekindle(0, 3, 3d * 3600d + 12d * 60d));
            if (!string.Equals(charged, "Heartfire 3/3 (raids)", StringComparison.Ordinal) ||
                !string.Equals(spent, "Heartfire 0/3 (raids) - next in 3h 12m", StringComparison.Ordinal))
                failures.Add("[plate-copy-unchanged] WO-1415's byte-exact Heartfire plate words drifted");

            if (failures.Count == 0)
            {
                reason = "HEARTFIRE_PIPS_OK icon slots, sprite, greyscale states and plate copy hold";
                Debug.Log(reason);
                return true;
            }

            reason = "HEARTFIRE_PIPS_FAIL " + string.Join(" | ", failures);
            Debug.LogError(reason);
            return false;
        }

        private static bool Matches(bool[] actual, params bool[] expected)
        {
            if (actual == null || actual.Length != expected.Length) return false;
            for (int i = 0; i < expected.Length; i++)
                if (actual[i] != expected[i]) return false;
            return true;
        }

        private static string Body(string source, string from, string until)
        {
            int start = source.IndexOf(from, StringComparison.Ordinal);
            if (start < 0) return null;
            int end = source.IndexOf(until, start + from.Length, StringComparison.Ordinal);
            return end > start ? source.Substring(start, end - start) : null;
        }

        private static bool TryStringConst(string source, string name, out string value)
        {
            var match = Regex.Match(source, "private\\s+const\\s+string\\s+" + Regex.Escape(name) +
                "\\s*=\\s*\"([^\"]+)\"\\s*;");
            value = match.Success ? match.Groups[1].Value : null;
            return match.Success;
        }

        private static bool TryFloatConst(string source, string name, out float value)
        {
            var match = Regex.Match(source, @"private\s+const\s+float\s+" + Regex.Escape(name) +
                @"\s*=\s*([0-9]+(?:\.[0-9]+)?)f\s*;");
            return float.TryParse(match.Success ? match.Groups[1].Value : null,
                NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
