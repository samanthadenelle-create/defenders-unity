// WO-1265 opened the gate FALSE until a signed-wallet backend, moderation and
// two-wallet proof existed. WO-1851 (clan WO-8, 2026-09-17) flips it to TRUE — that
// acceptance is complete (WO-1848's two-wallet integration gate is green) — while
// this suite keeps proving the ORDERING invariant: the gate check must still be the
// literal first line of both entry points, whatever its value. Marker:
// CLAN_FEATURE_GATE_OK/FAIL.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class ClanFeatureGateRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            string root = Application.dataPath;
            string gate = Read(root, "_Modules/Core/Services/ClanFeatureGate.cs", failures);
            string bootstrap = Read(root, "_Modules/HUD/ClanChatPanelBootstrap.cs", failures);
            string hud = Read(root, "_Modules/HUD/Kit/HudKitController.cs", failures);

            Require(gate, "public const bool PlayerFacingEnabled = true;", failures,
                "WO-1851 opens the gate now that WO-1265's backend acceptance is complete");
            Require(bootstrap, "if (!ClanFeatureGate.PlayerFacingEnabled) return;", failures,
                "direct ClanChatPanel bootstrap bypasses the release gate");
            Require(hud, "if (DeNelle.Core.Services.ClanFeatureGate.PlayerFacingEnabled)", failures,
                "the HUD dock can expose Chat while the prototype is gated");

            if (bootstrap.IndexOf("if (!ClanFeatureGate.PlayerFacingEnabled) return;", StringComparison.Ordinal) >
                bootstrap.IndexOf("new GameObject(\"ClanChatPanel\")", StringComparison.Ordinal))
                failures.Add("[clan-feature-gate] bootstrap checks the gate only after constructing the panel");
            // WO-1857: the Chat door's label is now a LocalizedText resolve (common.remnant_chat),
            // not a bare "Chat" literal - the needle pins the resolved key so an IndexOf miss can't
            // silently read as "found at position -1, which is always before the gate check".
            int chatDoorIdx = hud.IndexOf("new LocalizedText(\"common.remnant_chat\").Resolve(), OpenClanChat);", StringComparison.Ordinal);
            if (chatDoorIdx < 0)
                failures.Add("[clan-feature-gate] the Chat door's AddDockTab call (common.remnant_chat -> OpenClanChat) was not found");
            else if (hud.IndexOf("if (DeNelle.Core.Services.ClanFeatureGate.PlayerFacingEnabled)", StringComparison.Ordinal) > chatDoorIdx)
                failures.Add("[clan-feature-gate] HUD checks the gate only after adding the Chat door");

            if (failures.Count == 0)
            {
                Debug.Log("CLAN_FEATURE_GATE_OK");
                reason = "clan/chat gate is open (WO-1851) and the gate check is still the literal first line of both entry points";
                return true;
            }
            reason = "clan-feature-gate: " + string.Join("; ", failures);
            Debug.LogError("CLAN_FEATURE_GATE_FAIL: " + reason);
            return false;
        }

        private static string Read(string root, string relative, List<string> failures)
        {
            string path = Path.Combine(root, relative);
            if (!File.Exists(path)) { failures.Add("[clan-feature-gate] missing " + relative); return string.Empty; }
            try { return File.ReadAllText(path); }
            catch (Exception ex) { failures.Add("[clan-feature-gate] unreadable " + relative + ": " + ex.Message); return string.Empty; }
        }

        private static void Require(string text, string needle, List<string> failures, string why)
        {
            if (text.IndexOf(needle, StringComparison.Ordinal) < 0)
                failures.Add("[clan-feature-gate] missing '" + needle + "' - " + why);
        }
    }
}
