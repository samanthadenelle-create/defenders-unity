// =============================================================================
// SwapVMTests (EditMode) — §2c lock for the Jupiter swap VM. MONEY PATH.
// -----------------------------------------------------------------------------
// Over a fake ISwapBackend (no network / no wallet / no real transaction):
// asserts the quote display projection, the initialise baseline, and — the
// behaviour-critical part — the confirm guards VERBATIM:
//   * quote-null / mid-load -> NOT charged (ExecuteSwapAsync never called).
//   * no wallet             -> NOT charged, error status.
//   * execute THROWS        -> indeterminate Fail path: error status + re-enabled.
//   * execute FALSE         -> error status + re-enabled.
//   * execute TRUE          -> success (no re-enable).
//
// Runs SYNCHRONOUSLY: DebounceSeconds=0 + completed-task fakes make the quote /
// confirm paths finish on the calling thread, so we drive them via GetResult()
// (no async-test-runner dependency, no real 0.6s wait). The two confirm-failure
// paths log a guard proof via FlowTrace.Fail (Debug.LogError); each such test
// declares an explicit LogAssert.Expect for that error immediately before the
// call (a blanket ignoreFailingMessages doesn't reliably span the async
// continuation, so the errors escaped it). Warn-path guards don't fail the runner.
// =============================================================================
using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using DeNelle.Core.Backend;
using DeNelle.Web3;

namespace DeNelle.Tests.EditMode
{
    /// <summary>Fake swap backend — settable wallet + quote + execute outcome, with
    /// a call counter that proves whether the money path was actually hit.</summary>
    internal sealed class FakeSwapBackend : ISwapBackend
    {
        public string Wallet = "WALLET_ABC";
        public SwapQuote QuoteToReturn = new SwapQuote { SkrOut = 100m, Rate = 10m, PlatformFee = 0.2m, NetworkFee = 0.000005m };
        public bool ExecuteReturns = true;
        public bool ExecuteThrows;
        public int ExecuteCalls;
        public int CloseCalls;

        public string ConnectedWalletKey => Wallet;

        public Task<SwapQuote> GetQuoteAsync(SwapInputToken input, decimal inputAmount) =>
            Task.FromResult(QuoteToReturn);

        public Task<bool> ExecuteSwapAsync(SwapQuote quote, string userPublicKey)
        {
            ExecuteCalls++;
            if (ExecuteThrows) throw new InvalidOperationException("boom");
            return Task.FromResult(ExecuteReturns);
        }

        public void CloseSwapPanel() => CloseCalls++;
    }

    [TestFixture]
    public class SwapVMTests
    {
        private static SwapVM Vm(FakeSwapBackend b) =>
            new SwapVM(b) { DebounceSeconds = 0f };   // zero delay -> synchronous quote path

        // Drives the debounced quote to completion (synchronous with a zero delay).
        private static void PumpQuote(SwapVM vm, string amount)
        {
            vm.OnInputChanged(amount);
            vm.PendingQuoteTask.GetAwaiter().GetResult();
        }

        private static void Confirm(SwapVM vm) => vm.ConfirmAsync().GetAwaiter().GetResult();

        // ── Quote path ───────────────────────────────────────────────────────

        [Test]
        public void valid_input_fetches_and_projects_the_quote()
        {
            var b = new FakeSwapBackend();
            var vm = Vm(b);

            PumpQuote(vm, "10");

            Assert.That(vm.SkrOutText, Is.EqualTo("~ 100.00 SKR"));
            Assert.That(vm.RateText, Is.EqualTo("1 USDC = 10.0000 SKR"));
            Assert.That(vm.ConfirmEnabled, Is.True, "a wallet is connected -> confirm enabled");
            Assert.That(vm.StatusText, Is.EqualTo(string.Empty));
        }

        [Test]
        public void invalid_input_clears_quote_and_disables_confirm()
        {
            var vm = Vm(new FakeSwapBackend());
            vm.OnInputChanged("abc");
            Assert.That(vm.SkrOutText, Is.EqualTo("-"));
            Assert.That(vm.ConfirmEnabled, Is.False);
            Assert.That(vm.StatusText, Is.EqualTo("Enter a valid amount."));
        }

        [Test]
        public void quote_with_no_wallet_prompts_connect_and_disables_confirm()
        {
            var b = new FakeSwapBackend { Wallet = string.Empty };
            var vm = Vm(b);
            PumpQuote(vm, "10");
            Assert.That(vm.ConfirmEnabled, Is.False);
            Assert.That(vm.StatusText, Is.EqualTo("Connect your wallet to swap."));
        }

        [Test]
        public void null_quote_reports_rate_unavailable()
        {
            var b = new FakeSwapBackend { QuoteToReturn = null };
            var vm = Vm(b);
            PumpQuote(vm, "10");
            Assert.That(vm.StatusIsError, Is.True);
            Assert.That(vm.StatusText, Is.EqualTo("Could not fetch rate. Check connection."));
        }

        [Test]
        public void initialise_baseline_projects_enter_amount_and_clears()
        {
            var vm = Vm(new FakeSwapBackend());
            vm.BeginInitialise(20);
            vm.ApplyInitialiseBaseline();
            Assert.That(vm.SkrOutText, Is.EqualTo("-"));
            Assert.That(vm.StatusText, Is.EqualTo("Enter an amount to see the rate."));
            Assert.That(vm.ConfirmEnabled, Is.False);
        }

        // ── MONEY PATH — confirm guards (the load-bearing assertions) ─────────

        [Test]
        public void confirm_with_no_quote_is_ignored_and_NOT_charged()
        {
            var b = new FakeSwapBackend();
            var vm = Vm(b);   // no quote fetched yet

            Confirm(vm);

            Assert.That(b.ExecuteCalls, Is.EqualTo(0),
                "no quote -> ExecuteSwapAsync must NOT run (player not charged)");
        }

        [Test]
        public void confirm_with_no_wallet_is_blocked_and_NOT_charged()
        {
            var b = new FakeSwapBackend();
            var vm = Vm(b);
            PumpQuote(vm, "10");         // a quote is present

            b.Wallet = string.Empty;     // wallet disconnects before confirm
            Confirm(vm);

            Assert.That(b.ExecuteCalls, Is.EqualTo(0),
                "no connected wallet -> swap BLOCKED, ExecuteSwapAsync never called (NOT charged)");
            Assert.That(vm.StatusIsError, Is.True);
            Assert.That(vm.StatusText, Is.EqualTo("Connect your wallet to swap."));
        }

        [Test]
        public void confirm_that_throws_takes_the_indeterminate_Fail_path()
        {
            var b = new FakeSwapBackend { ExecuteThrows = true };
            var vm = Vm(b);
            PumpQuote(vm, "10");

            // The indeterminate-throw guard logs its proof via FlowTrace.Fail (Debug.LogError).
            LogAssert.Expect(UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("ExecuteSwapAsync THREW"));
            Confirm(vm);   // must NOT throw (guarded internally)

            Assert.That(b.ExecuteCalls, Is.EqualTo(1), "the execute was attempted (outcome indeterminate)");
            Assert.That(vm.StatusIsError, Is.True);
            Assert.That(vm.StatusText, Is.EqualTo("Swap failed. Please try again."));
            Assert.That(vm.ConfirmEnabled, Is.True, "confirm is RE-ENABLED after an indeterminate throw");
        }

        [Test]
        public void confirm_that_returns_false_reports_failure_and_re_enables()
        {
            var b = new FakeSwapBackend { ExecuteReturns = false };
            var vm = Vm(b);
            PumpQuote(vm, "10");

            // The execute-returned-FALSE guard logs its proof via FlowTrace.Fail (Debug.LogError).
            LogAssert.Expect(UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("ExecuteSwapAsync returned FALSE"));
            Confirm(vm);

            Assert.That(b.ExecuteCalls, Is.EqualTo(1));
            Assert.That(vm.StatusIsError, Is.True);
            Assert.That(vm.StatusText, Is.EqualTo("Swap failed. Please try again."));
            Assert.That(vm.ConfirmEnabled, Is.True, "a failed swap re-enables confirm to retry");
        }

        [Test]
        public void confirm_success_executes_once_and_does_not_re_enable()
        {
            var b = new FakeSwapBackend { ExecuteReturns = true };
            var vm = Vm(b);
            PumpQuote(vm, "10");

            Confirm(vm);

            Assert.That(b.ExecuteCalls, Is.EqualTo(1));
            Assert.That(vm.ConfirmEnabled, Is.False,
                "a successful swap leaves confirm disabled (no accidental double-charge)");
            Assert.That(vm.StatusIsError, Is.False);
        }

        [Test]
        public void close_routes_to_backend_panel_close()
        {
            var b = new FakeSwapBackend();
            var vm = Vm(b);
            vm.Close();
            Assert.That(b.CloseCalls, Is.EqualTo(1));
        }

        [Test]
        public void confirm_raises_changed_for_repaint()
        {
            var b = new FakeSwapBackend();
            var vm = Vm(b);
            PumpQuote(vm, "10");

            int fires = 0; vm.Changed += () => fires++;
            Confirm(vm);
            Assert.That(fires, Is.GreaterThanOrEqualTo(1), "confirm updates display state -> Changed");
        }
    }
}
