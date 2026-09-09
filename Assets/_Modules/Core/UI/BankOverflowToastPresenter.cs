// =============================================================================
// BankOverflowToastPresenter -- the PLAYER-FACING half of the bank-cap warn.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.UI
//
// WHY THIS FILE EXISTS AT ALL
//   Owner ruling 2026-08-04 (WO-901 Sec.5): overflow at the town bank cap is CLAMP AND
//   WARN -- the surplus is LOST and the player is TOLD. That makes the warn load-bearing:
//   it is the only thing standing between the player and silently vaporised resources.
//   TownBankCapacity.ClampGrant already emits an UNTHROTTLED [Flow:Bank] Warn on every
//   clamped grant (the captured-data half, Sec.12). This is the on-screen half.
//
// PRESENTATION IS A SEPARATE LAYER (ARCHITECTURE_PRINCIPLES Sec.2)
//   EconomyService / the income paths never build UI and never know a toast exists. They
//   raise TownBankCapacity.Overflowed; this observer renders it through the ONE established
//   toast seam (ElarionUiKit.ShowToast). No new reflection bridge, no static_gate allowlist
//   entry, no HudKitController coupling -- Core observing a Core event.
//
// ** WO-1207 -- THE ON-SCREEN WARN IS NOW OPT-IN PER CALL SITE. READ THIS BEFORE EDITING. **
//   Owner ruling 2026-08-25, verbatim: "they get a warn on harvest but no warn on battle
//   rewards cause one is choice".
//
//   COLLECTING is a choice the player TIMES -- she could have built storage, spent first, or
//   collected sooner -- so at-cap loss on that path is actionable and teaches the cap. A
//   BATTLE REWARD arrives whether she wants it or not and lands mid combat-resolution;
//   warning there scolds her for something she did not choose, and noise is how the
//   actionable warn gets ignored.
//
//   MECHANICALLY that means this presenter can no longer render EVERY Overflowed event: both
//   paths reach ClampGrant with the same BankGrantKind.EarnedIncome and the same "Grant"
//   source tag, so the event alone cannot tell them apart, and TownBankCapacity/BattleArena
//   are deliberately not being taught about toasts. So the SUBSCRIPTION stays (it is what
//   collects the truth) and the RENDER became opt-IN: a call site that has decided the
//   player can act on the loss opens a BeginWarnScope/WarnScope around its grant. Outside a
//   scope this observer records nothing on screen -- the [Flow:Bank] Warn in ClampGrant is
//   still unthrottled and unswallowable, so the loss is never undetectable, only unscolded.
//
//   OPT-IN IS THE POINT: a future income path cannot inherit a toast by accident. The one
//   opted-in call site today is EchoService.DumpSilos (the harvest collect).
//
//   ONE TOAST PER SCOPE. A dump can clamp wood AND iron AND food in a single tap. Rendering
//   per event would stack three scolds for one action, and the kit toast is single-slot
//   (a new call destroys the old), so the player would see only the last of them. The scope
//   COLLECTS the trims and emits ONE sentence-per-resource toast when it closes.
//
// COPY LAW (WO-901 Sec.4)
//   This surface owns the words "Storage" / "Bank" / current-max -- it is the WALLET.
//   It must NEVER say "collectors N/M full" (that is the pending pools, WO-900).
//   ASCII only, state text-encoded, never colour alone (the owner is red/green colourblind):
//   the tone accent is decoration, the sentence carries the whole message.
//   The two sentences below are the OWNER'S COPY -- reuse them, never reword them. WO-1207
//   explicitly forbids authoring new at-cap words: one voice for the cap.
//
// THROTTLING
//   Per RESOURCE, on storage-caps.json overflowWarnCooldownSeconds, so a hot income loop
//   (per-kill trickle, offline catch-up) cannot spam the screen -- and so a player who
//   collects again while still full is not scolded on every following collect. The FlowTrace
//   warn is deliberately NOT throttled -- the break-log must carry every event even when the
//   screen shows one.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Economy;

namespace DeNelle.Core.UI
{
    /// <summary>Renders <see cref="TownBankCapacity.Overflowed"/> as one harvest-result modal --
    /// but ONLY inside an opted-in <see cref="WarnScope"/> (WO-1207 ruling 3).</summary>
    public static class BankOverflowToastPresenter
    {
        private static readonly Dictionary<BankResource, float> _lastShownAt = new Dictionary<BankResource, float>();
        private static readonly HashSet<BankResource> _blockedConditionWarned = new HashSet<BankResource>();
        private static readonly List<BankOverflowStatus> _scopeTrims = new List<BankOverflowStatus>();
        private static bool _attached;
        private static int _scopeDepth;
        private static string _scopeSource;

        /// <summary>Player-facing toasts raised since the last <see cref="ResetDiagnostics"/>.
        /// The oracle seam: ElarionUiKit.ShowToast is a no-op outside play, so a suite cannot
        /// observe the card itself -- it observes the DECISION to show one.</summary>
        public static int ToastCount { get; private set; }

        /// <summary>The exact text of the most recent player-facing toast ("" if none).</summary>
        public static string LastToastMessage { get; private set; } = string.Empty;

        /// <summary>Test/teardown seam: clears the counters, the per-resource throttle and any
        /// half-open scope so one case can never colour the next.</summary>
        public static void ResetDiagnostics()
        {
            ToastCount = 0;
            LastToastMessage = string.Empty;
            _lastShownAt.Clear();
            _blockedConditionWarned.Clear();
            _scopeTrims.Clear();
            _scopeDepth = 0;
            _scopeSource = null;
        }

        /// <summary>Self-attaches once per play session (and re-attaches after a domain reload).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Attach()
        {
            if (_attached) return;
            _attached = true;
            _lastShownAt.Clear();
            TownBankCapacity.Overflowed += OnOverflow;
            FlowTrace.Step("Bank", "BankOverflowToastPresenter attached -- clamped grants inside an opted-in warn scope will surface on screen (WO-1207).");
        }

        /// <summary>Detach (tests / teardown). Idempotent.</summary>
        public static void Detach()
        {
            if (!_attached) return;
            TownBankCapacity.Overflowed -= OnOverflow;
            _attached = false;
        }

        // =====================================================================
        //  WO-1207 -- the OPT-IN scope. One toast per scope, or none at all.
        // =====================================================================

        /// <summary>
        /// Opens an opt-in warn scope around a grant the player CHOSE to take (the harvest
        /// collect). Every clamp raised while it is open is collected; closing it emits ONE
        /// toast naming every trimmed resource. Dispose it -- prefer <c>using</c>.
        /// <para>A grant made OUTSIDE a scope is silent on screen by design (ruling 3): the
        /// player did not time it, so a scold is noise she cannot act on. Do not open one
        /// "just in case" -- opting a path in is a design decision about whether the loss was
        /// avoidable, not a logging convenience.</para>
        /// </summary>
        /// <param name="sourceTag">Short name of the opted-in call site, for the trace.</param>
        public static WarnScope BeginWarnScope(string sourceTag) => new WarnScope(sourceTag);

        /// <summary>Disposable handle from <see cref="BeginWarnScope"/>. Nesting is counted, so
        /// only the OUTERMOST close emits -- an inner grant can never split the one toast.</summary>
        public struct WarnScope : IDisposable
        {
            private bool _open;

            internal WarnScope(string sourceTag)
            {
                _open = true;
                if (_scopeDepth == 0)
                {
                    _scopeTrims.Clear();
                    _scopeSource = string.IsNullOrEmpty(sourceTag) ? "?" : sourceTag;
                }
                _scopeDepth++;
            }

            /// <summary>Closes the scope; the outermost close emits the single toast.</summary>
            public void Dispose()
            {
                if (!_open) return;                 // idempotent: a double Dispose must not unbalance the depth
                _open = false;
                if (_scopeDepth > 0) _scopeDepth--;
                if (_scopeDepth == 0) EmitScopeToast();
            }
        }

        private static void OnOverflow(BankOverflowStatus s)
        {
            if (s.Lost <= 0) return;

            // Ruling 3 (WO-1207): NO SCOPE, NO SCOLD. The unthrottled [Flow:Bank] Warn in
            // ClampGrant has already recorded the loss, so nothing is hidden from triage --
            // this is a decision about whose attention the loss deserves, not about evidence.
            if (_scopeDepth <= 0) return;

            _scopeTrims.Add(s);
        }

        /// <summary>
        /// ONE toast for the whole scope: the owner's sentence per trimmed resource, joined.
        /// Repeat clamps of the SAME resource inside one scope are summed into one sentence --
        /// the player lost one total, not two events. Resources still inside their per-resource
        /// cooldown are dropped here, which is what stops a repeated scold on every following
        /// collect while the store stays full.
        /// </summary>
        private static void EmitScopeToast()
        {
            if (_scopeTrims.Count == 0) return;

            float now = Time.unscaledTime;
            float cooldown = StorageCapsCatalog.OverflowWarnCooldownSeconds;

            // Merge per resource, preserving first-seen order (deterministic sentence order).
            var order = new List<BankResource>();
            var merged = new Dictionary<BankResource, BankOverflowStatus>();
            for (int i = 0; i < _scopeTrims.Count; i++)
            {
                var s = _scopeTrims[i];
                if (merged.TryGetValue(s.Resource, out var prev))
                {
                    s.Lost = prev.Lost + s.Lost;
                    s.Requested = prev.Requested + s.Requested;
                    s.Granted = prev.Granted + s.Granted;
                    merged[s.Resource] = s;
                }
                else
                {
                    order.Add(s.Resource);
                    merged[s.Resource] = s;
                }
            }
            _scopeTrims.Clear();

            var sentences = new List<string>();
            int spoken = 0;
            int throttled = 0;
            for (int i = 0; i < order.Count; i++)
            {
                var s = merged[order[i]];
                // One alert for one continuing blocked-storage condition. A code grant can
                // leave a wallet over cap for a long time; passive pet/auto-collect ticks must
                // not reopen this modal every cooldown. If the player spent below the cap,
                // s.Current proves the old condition cleared and a later fill may alert once.
                if (s.Current < s.Max) _blockedConditionWarned.Remove(s.Resource);
                if (_blockedConditionWarned.Contains(s.Resource))
                {
                    throttled++;
                    continue;
                }
                if (_lastShownAt.TryGetValue(s.Resource, out float last) && now - last < cooldown)
                {
                    throttled++;
                    continue;
                }
                _lastShownAt[s.Resource] = now;
                _blockedConditionWarned.Add(s.Resource);
                sentences.Add(SentenceFor(s));
                spoken++;
            }

            if (spoken == 0)
            {
                FlowTrace.Step("Bank",
                    $"bank-cap toast SUPPRESSED for [{_scopeSource ?? "?"}] -- all {throttled} trimmed resource(s) inside the "
                    + $"{cooldown:0.#}s per-resource cooldown (no repeated scold on a following collect).");
                _scopeSource = null;
                return;
            }

            string msg = string.Join(" ", sentences.ToArray());

            ToastCount++;
            LastToastMessage = msg;

            // WO-1434 -- STAMP THE SCOPE ONTO THE ROWS. BankOverflowStatus.Source is the
            // ClampGrant sourceTag, and every one of these rows came through
            // EconomyService.GrantSpendable, so it reads "Grant" -- MEASURED on the owner's
            // Seeker 2026-09-06 12:51:25: "BANK FULL [Grant] Wood: requested 28800 ...". The
            // scope is the ONLY place that knows these are the Echo silo's rows, which is why
            // HarvestOverflowModal's `Source.IndexOf("DumpSilos")` branch (added for exactly
            // this case) had never once fired and the silo rows were shown to the player as
            // "lost". They are not lost: DumpSilos settles against the APPLIED basket and the
            // refused units stay in the silo (proven on the same device -- pool 57600 survived
            // three consecutive dumps). Presentation stays a separate layer: this observer
            // records WHERE the row came from; the modal decides the words.
            var stamped = order.ConvertAll(r =>
            {
                var s = merged[r];
                if (!string.IsNullOrEmpty(_scopeSource)) s.Source = _scopeSource;
                return s;
            });
            HarvestOverflowModal.Present(stamped);
            FlowTrace.Step("Bank",
                $"bank-cap harvest-result modal for [{_scopeSource ?? "?"}] naming {spoken} trimmed resource(s)"
                + (throttled > 0 ? $" ({throttled} suppressed by cooldown)" : "") + ".");
            _scopeSource = null;
        }

        /// <summary>
        /// The OWNER'S COPY -- one sentence, chosen by state. WO-1207 forbids new at-cap words;
        /// this method exists so the single-resource and the multi-resource toast speak with the
        /// same voice, not so the words can be edited in one place.
        /// </summary>
        private static string SentenceFor(BankOverflowStatus s)
        {
            // WO-1191 -- TWO SITUATIONS, TWO SENTENCES. `FOUNDATIONAL_RULINGS.md` section 7 governs;
            // cite it, never restate it.
            //
            //   s.OverCap == false : the ordinary full-or-nearly-full bank. Something the player
            //                        earned did not fit and is gone. Existing words, unchanged.
            //
            //   s.OverCap == true  : the balance is ALREADY above capacity. That state is created on
            //                        purpose, and after a player has PAID to reach it the old
            //                        sentence -- "storage FULL, N lost, build a bigger container" --
            //                        reads as a penalty for the purchase. Nothing of theirs is being
            //                        taken: every unit above the cap is still in the wallet and still
            //                        spendable, the earned faucet is merely paused, and it restarts by
            //                        itself. So the copy states the amount, states that it is theirs,
            //                        and names the ONE condition that resumes income -- and it is NOT
            //                        a Danger toast, because nothing went wrong.
            //
            // Both lines are ASCII, state text-encoded in words, never carried by colour alone (the
            // owner is red/green colourblind) -- the tone accent is decoration in both branches.
            //
            // *** THE EXACT PLAYER WORDING BELOW IS A PROPOSAL AWAITING THE OWNER (WO-1191). ***
            // The BEHAVIOUR -- two branches, no loss language above the cap, the resume condition
            // named with the measured number -- is the part this file is asserting.
            if (s.OverCap)
            {
                return $"{s.ResourceName} is above storage - {s.Current} of {s.Max}. All of it is yours to spend. "
                     + $"Harvests and rewards add no {s.ResourceName.ToLowerInvariant()} until you are back under {s.Max}.";
            }

            // ASCII, text-encoded, names the resource AND the amount lost AND the fix.
            return $"{s.ResourceName} storage FULL - {s.Lost} lost. Build or upgrade a {s.ContainerName}, or spend {s.ResourceName.ToLowerInvariant()}.";
        }
    }
}
