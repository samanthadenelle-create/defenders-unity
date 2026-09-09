// =============================================================================
// WO1207HarvestTrimWarnRegression -- the oracle for "capacity must mean something".
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Namespace: DeNelle.Editor.Regression
// Contract: public static bool Run(out string reason). Registered in DataRegression.RunAll
// as [harvest-trim-warn]. The suite rolls into REGRESSION_OK <n>/<n> suites.
//
// THE RULING (owner, 2026-08-25, felt-testing build 2026.08.25.341262), verbatim:
//   "first time granted me alot but could have been if there was no storage for it,
//    i expect that, but id like to warn"
//   "otherwise the capacity means nothing"
//   "they get a warn on harvest but no warn on battle rewards cause one is choice"
//
// The loss STAYS -- this suite never asserts anything about the clamp, the caps, or
// TownBankCapacity. It asserts the two halves of the THIRD ruling, which is a SCOPE FENCE
// with a side in each direction:
//
//   COLLECTING is timed by the player. She could have built storage, spent first, or
//   collected sooner, so a trim there is actionable and it is the thing that teaches the
//   cap. It MUST speak.
//
//   A BATTLE REWARD is not timed by her and lands mid combat-resolution. A warning she
//   cannot act on is noise, and noise is how the actionable one gets ignored. It MUST NOT
//   speak. That direction is the half a later refactor quietly widens -- "warn everywhere,
//   it is only a toast" -- so it is pinned as hard as the positive case.
//
// WHY THE ASSERTIONS LOOK AT ToastCount AND NOT AT A CARD
//   ElarionUiKit.ShowToast is a no-op outside play (Application.isPlaying), so an EditMode
//   suite cannot observe the rendered card. BankOverflowToastPresenter.ToastCount /
//   LastToastMessage record the DECISION to show one, taken on the same line that calls
//   ShowToast -- so a change that stops rendering, starts rendering the wrong path, or
//   splits one dump into three scolds moves these counters. Every case below drives the
//   REAL TownBankCapacity.ClampGrant and the REAL presenter; nothing is simulated.
//
// WHAT EACH CASE PINS, AND WHAT BROKEN STATE MAKES IT FAIL
//   [harvest-trim-warns]     A trim inside the opted-in harvest scope raises exactly ONE
//                            player-facing warning that NAMES the resource and the amount
//                            lost. FAILS IF: the opt-in scope stops rendering, or the copy
//                            drops the resource/number the ruling asked for.
//   [at-cap-copy-not-overcap] That warning uses the AT-CAP sentence. At the cap the surplus
//                            really was destroyed, so loss language is correct; the WO-1191
//                            over-cap wording ("above storage ... yours to spend") describes
//                            a different state and must never appear here. FAILS IF: the two
//                            sentences are merged or the wrong branch is taken.
//   [one-toast-per-dump]     A dump that trims wood AND iron AND food raises ONE toast that
//                            names all three -- not three toasts. FAILS IF: rendering moves
//                            back per event (the kit toast is single-slot, so the player
//                            would see only the last scold of the three).
//   [no-repeat-scold]        Collecting again while still full does not scold again inside
//                            the cooldown. FAILS IF: the per-resource throttle is removed.
//   [battle-reward-silent]   A clamped grant OUTSIDE any opted-in scope raises NO player
//                            warning. FAILS IF: the render goes back to blanket-subscribing
//                            every Overflowed event -- the exact widening ruling 3 forbids.
//   [harvest-opts-in]        EchoService.DumpSilos still WRAPS its bank call in the scope,
//                            and the fenced reward path does NOT open one. The runtime cases
//                            above prove the mechanism; this proves the wiring, so removing
//                            the one opted-in call site cannot leave the suite green.
//   [copy-unchanged]         The owner's two sentences are still the presenter's words. The
//                            copy is hers; WO-1207 forbids authoring new at-cap words.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DeNelle.Core.Economy;
using DeNelle.Core.UI;

namespace DeNelle.Editor.Regression
{
    /// <summary>WO-1207: the harvest trim is TOLD, the battle reward is SILENT.</summary>
    public static class WO1207HarvestTrimWarnRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();

            bool wasAttached = false;
            try
            {
                // Drive the REAL presenter. RuntimeInitializeOnLoadMethod does not fire in
                // EditMode, so attach explicitly -- and detach in the finally so this suite
                // cannot leave a subscriber behind for whatever runs next.
                BankOverflowToastPresenter.Attach();
                wasAttached = true;

                CheckHarvestTrimWarns(failures, notes);
                CheckOneToastPerDump(failures, notes);
                CheckNoRepeatScold(failures, notes);
                CheckBattleRewardSilent(failures, notes);
            }
            finally
            {
                if (wasAttached) BankOverflowToastPresenter.Detach();
                BankOverflowToastPresenter.ResetDiagnostics();
            }

            CheckHarvestOptsIn(failures, notes);
            CheckPassivePetReturnsStaySilent(failures, notes);
            CheckCopyUnchanged(failures, notes);

            if (failures.Count == 0)
            {
                reason = "HARVEST TRIM WARN OK -- a trimmed harvest raises exactly one at-cap warning naming "
                       + "resource and amount, a multi-resource dump raises ONE toast for the whole dump, a "
                       + "repeat collect inside the cooldown does not scold again, and a clamped grant outside "
                       + "the opted-in scope stays silent (owner ruling 2026-08-25)"
                       + (notes.Count > 0 ? $" [notes: {string.Join("; ", notes)}]" : "");
                return true;
            }

            reason = $"HARVEST TRIM WARN FAIL x{failures.Count}: " + string.Join(" | ", failures)
                   + (notes.Count > 0 ? $" [notes: {string.Join("; ", notes)}]" : "");
            return false;
        }

        private static void CheckPassivePetReturnsStaySilent(List<string> failures, List<string> notes)
        {
            string autoPath = Path.Combine(Application.dataPath,
                "_Modules/Village/Buildings/Progression/AutoHarvestService.cs");
            string collectorPath = Path.Combine(Application.dataPath,
                "_Modules/Village/Buildings/Progression/ResourceCollectorService.cs");
            string presenterPath = Path.Combine(Application.dataPath,
                "_Modules/Core/UI/BankOverflowToastPresenter.cs");
            string auto = File.Exists(autoPath) ? File.ReadAllText(autoPath) : "";
            string collector = File.Exists(collectorPath) ? File.ReadAllText(collectorPath) : "";
            string presenter = File.Exists(presenterPath) ? File.ReadAllText(presenterPath) : "";
            if (auto.IndexOf("CollectAll(showResult: false)", StringComparison.Ordinal) < 0)
                failures.Add("[passive-pet-return-silent] AutoHarvestService does not use the silent CollectAll path; " +
                             "every passive return can reopen Harvest Result");
            if (collector.IndexOf("public static int CollectAll(bool showResult = true)", StringComparison.Ordinal) < 0 ||
                collector.IndexOf("echo.DumpSilos(showResult)", StringComparison.Ordinal) < 0)
                failures.Add("[passive-pet-return-silent] CollectAll does not carry its presentation choice through " +
                             "collector rows and the Echo silo dump");
            if (presenter.IndexOf("_blockedConditionWarned.Contains", StringComparison.Ordinal) < 0 ||
                presenter.IndexOf("s.Current < s.Max", StringComparison.Ordinal) < 0)
                failures.Add("[one-alert-per-blocked-condition] overflow warnings are only time-throttled; an " +
                             "unchanged over-cap code grant will reopen the modal forever");
            if (failures.Count == 0) notes.Add("passive pet returns stay silent; one alert per continuing cap block");
        }

        // =====================================================================
        //  Fixture helpers -- a REAL clamp, at the REAL cap, through the REAL event.
        // =====================================================================

        /// <summary>Forces a genuine over-the-cap earned grant on <paramref name="r"/> and returns
        /// what the cap took. Weighed against a wallet sitting exactly AT max, so the clamp is
        /// certain and the state is the ordinary full bank (never the WO-1191 over-cap state).</summary>
        private static int TrimAtCap(BankResource r, int requested, string sourceTag)
        {
            int max = TownBankCapacity.MaxOf(r);
            TownBankCapacity.ClampGrant(r, max, requested, sourceTag, out int lost);
            return lost;
        }

        /// <summary>The capped axes this suite drives. Read from IsCapped rather than named, so a
        /// future cap/uncap ruling moves the fixture instead of quietly skipping a case.</summary>
        private static List<BankResource> CappedAxes()
        {
            var list = new List<BankResource>();
            foreach (BankResource r in Enum.GetValues(typeof(BankResource)))
                if (TownBankCapacity.IsCapped(r)) list.Add(r);
            return list;
        }

        // =====================================================================
        //  [harvest-trim-warns] + [at-cap-copy-not-overcap]
        // =====================================================================
        private static void CheckHarvestTrimWarns(List<string> failures, List<string> notes)
        {
            var capped = CappedAxes();
            if (capped.Count == 0)
            {
                failures.Add("[harvest-trim-warns] NO capped resource exists -- the town bank cap is the "
                           + "premise of this ruling; with nothing capped a harvest can never be trimmed and "
                           + "the warn is unreachable");
                return;
            }

            BankOverflowToastPresenter.ResetDiagnostics();
            var r = capped[0];
            const int requested = 40;

            int lost;
            using (BankOverflowToastPresenter.BeginWarnScope("EchoService.DumpSilos"))
            {
                lost = TrimAtCap(r, requested, "Grant");
            }

            if (lost != requested)
                failures.Add($"[harvest-trim-warns] fixture did not trim: asked {requested} at a FULL {r} store, "
                           + $"lost {lost} -- the case never reached the state it claims to pin");

            int shown = BankOverflowToastPresenter.ToastCount;
            if (shown != 1)
                failures.Add($"[harvest-trim-warns] a trimmed HARVEST raised {shown} player-facing warning(s), expected exactly 1 -- "
                           + "owner ruling 2026-08-25: \"id like to warn ... otherwise the capacity means nothing\"");

            string msg = BankOverflowToastPresenter.LastToastMessage ?? string.Empty;
            string resName = TownBankCapacity.DisplayName(r);
            if (msg.IndexOf(resName, StringComparison.OrdinalIgnoreCase) < 0)
                failures.Add($"[harvest-trim-warns] the warning does not NAME the resource ('{resName}'): \"{msg}\"");
            if (msg.IndexOf(requested.ToString(), StringComparison.Ordinal) < 0)
                failures.Add($"[harvest-trim-warns] the warning does not state the amount LOST ({requested}): \"{msg}\"");

            // [at-cap-copy-not-overcap] -- at the cap the surplus really was destroyed. The
            // WO-1191 over-cap sentence describes a DIFFERENT state (nothing taken, income
            // merely paused) and reads as a penalty if it lands here.
            if (msg.IndexOf("yours to spend", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("is above storage", StringComparison.OrdinalIgnoreCase) >= 0)
                failures.Add("[at-cap-copy-not-overcap] the AT-CAP trim spoke the WO-1191 OVER-CAP words "
                           + $"(\"{msg}\") -- two different situations, two different sentences");
            if (msg.IndexOf("storage FULL", StringComparison.Ordinal) < 0)
                failures.Add($"[at-cap-copy-not-overcap] the at-cap warning is not the owner's existing at-cap sentence: \"{msg}\"");
        }

        // =====================================================================
        //  [one-toast-per-dump]
        // =====================================================================
        private static void CheckOneToastPerDump(List<string> failures, List<string> notes)
        {
            var capped = CappedAxes();
            if (capped.Count < 2)
            {
                notes.Add($"[one-toast-per-dump] only {capped.Count} capped resource exists, so a multi-resource "
                        + "dump cannot be constructed -- the single-resource path is still pinned above");
                return;
            }

            BankOverflowToastPresenter.ResetDiagnostics();

            int trimmed = 0;
            using (BankOverflowToastPresenter.BeginWarnScope("EchoService.DumpSilos"))
            {
                for (int i = 0; i < capped.Count; i++)
                    if (TrimAtCap(capped[i], 25 + i, "Grant") > 0) trimmed++;
            }

            if (trimmed != capped.Count)
                failures.Add($"[one-toast-per-dump] fixture trimmed only {trimmed} of {capped.Count} capped resources");

            int shown = BankOverflowToastPresenter.ToastCount;
            if (shown != 1)
                failures.Add($"[one-toast-per-dump] {capped.Count} resources trimmed in ONE dump raised {shown} toast(s), expected 1 -- "
                           + "the kit toast is single-slot, so per-event rendering shows the player only the LAST scold");

            string msg = BankOverflowToastPresenter.LastToastMessage ?? string.Empty;
            for (int i = 0; i < capped.Count; i++)
            {
                string name = TownBankCapacity.DisplayName(capped[i]);
                if (msg.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                    failures.Add($"[one-toast-per-dump] the single toast does not name trimmed resource '{name}': \"{msg}\"");
            }
        }

        // =====================================================================
        //  [no-repeat-scold]
        // =====================================================================
        private static void CheckNoRepeatScold(List<string> failures, List<string> notes)
        {
            var capped = CappedAxes();
            if (capped.Count == 0) return;   // already failed by name in CheckHarvestTrimWarns

            if (StorageCapsCatalog.OverflowWarnCooldownSeconds <= 0f)
            {
                failures.Add("[no-repeat-scold] overflowWarnCooldownSeconds is 0 -- every following collect at a "
                           + "full store would scold again, which is the noise the ruling is guarding against");
                return;
            }

            BankOverflowToastPresenter.ResetDiagnostics();
            var r = capped[0];

            using (BankOverflowToastPresenter.BeginWarnScope("EchoService.DumpSilos"))
                TrimAtCap(r, 30, "Grant");
            int afterFirst = BankOverflowToastPresenter.ToastCount;

            using (BankOverflowToastPresenter.BeginWarnScope("EchoService.DumpSilos"))
                TrimAtCap(r, 30, "Grant");
            int afterSecond = BankOverflowToastPresenter.ToastCount;

            if (afterFirst != 1)
                failures.Add($"[no-repeat-scold] the FIRST collect raised {afterFirst} warning(s), expected 1");
            if (afterSecond != afterFirst)
                failures.Add($"[no-repeat-scold] a second collect inside the {StorageCapsCatalog.OverflowWarnCooldownSeconds:0.#}s "
                           + $"cooldown scolded again ({afterSecond} total) -- a repeated scold on every following collect");
        }

        // =====================================================================
        //  [battle-reward-silent] -- ruling 3, the half a refactor widens
        // =====================================================================
        private static void CheckBattleRewardSilent(List<string> failures, List<string> notes)
        {
            var capped = CappedAxes();
            if (capped.Count == 0) return;   // already failed by name in CheckHarvestTrimWarns

            BankOverflowToastPresenter.ResetDiagnostics();
            var r = capped[capped.Count - 1];

            // No scope: this is the shape of BattleArena's win reward -- the same
            // BankGrantKind.EarnedIncome, the same "Grant" source tag, no opt-in.
            int lost = TrimAtCap(r, 50, "Grant");

            if (lost <= 0)
                failures.Add("[battle-reward-silent] fixture did not trim, so the silence proves nothing "
                           + "-- the case never reached the state it claims to pin");

            int shown = BankOverflowToastPresenter.ToastCount;
            if (shown != 0)
                failures.Add($"[battle-reward-silent] a clamped BATTLE-REWARD grant raised {shown} player-facing warning(s), expected 0 -- "
                           + "owner ruling 2026-08-25 verbatim: \"they get a warn on harvest but no warn on battle rewards cause one is choice\". "
                           + "A warning she cannot act on is noise, and noise is how the actionable one gets ignored");
        }

        // =====================================================================
        //  [harvest-opts-in] -- the wiring, not just the mechanism
        // =====================================================================
        private static void CheckHarvestOptsIn(List<string> failures, List<string> notes)
        {
            string echo = Path.Combine(Application.dataPath, "_Modules/Village/Harvest/EchoService.cs");
            if (!File.Exists(echo))
            {
                failures.Add("[harvest-opts-in] EchoService.cs is MISSING -- the one opted-in harvest call site is gone");
            }
            else
            {
                string src = File.ReadAllText(echo);
                if (src.IndexOf("BeginWarnScope", StringComparison.Ordinal) < 0)
                    failures.Add("[harvest-opts-in] EchoService.DumpSilos no longer opens a BankOverflowToastPresenter warn scope -- "
                               + "the harvest trim would be silent again, which is the WO-1207 defect verbatim");
                if (src.IndexOf("GrantSpendable", StringComparison.Ordinal) < 0)
                    failures.Add("[harvest-opts-in] EchoService.DumpSilos no longer banks through GrantSpendable -- the scope "
                               + "may no longer wrap the grant it is supposed to be listening to");
            }

            // The fenced direction: the reward path must not have grown an opt-in.
            string arena = Path.Combine(Application.dataPath, "_Modules/Village/Arena/BattleArena.cs");
            if (File.Exists(arena))
            {
                string src = File.ReadAllText(arena);
                if (src.IndexOf("BeginWarnScope", StringComparison.Ordinal) >= 0)
                    failures.Add("[harvest-opts-in] BattleArena opened a bank-overflow warn scope -- ruling 3 says a battle "
                               + "reward NEVER warns; it arrives whether the player wants it or not");
            }
            else
            {
                notes.Add("[harvest-opts-in] BattleArena.cs not found at the canonical path -- the negative half was not checked");
            }
        }

        // =====================================================================
        //  [copy-unchanged] -- the words are the owner's
        // =====================================================================
        private static void CheckCopyUnchanged(List<string> failures, List<string> notes)
        {
            string presenter = Path.Combine(Application.dataPath, "_Modules/Core/UI/BankOverflowToastPresenter.cs");
            if (!File.Exists(presenter))
            {
                failures.Add("[copy-unchanged] BankOverflowToastPresenter.cs is MISSING -- the clamp would lose resources with no on-screen warning");
                return;
            }

            string src = File.ReadAllText(presenter);
            if (src.IndexOf("storage FULL - {s.Lost} lost. Build or upgrade a {s.ContainerName}, or spend", StringComparison.Ordinal) < 0)
                failures.Add("[copy-unchanged] the AT-CAP sentence was reworded -- the copy is the owner's; WO-1207 says reuse it, never author new at-cap words");
            if (src.IndexOf("All of it is yours to spend.", StringComparison.Ordinal) < 0)
                failures.Add("[copy-unchanged] the WO-1191 OVER-CAP sentence was reworded or removed -- above the cap nothing is taken and the copy must not call it a loss");
        }
    }
}
