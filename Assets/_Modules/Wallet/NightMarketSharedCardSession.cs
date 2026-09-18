using System;
using System.Collections.Generic;
using DeNelle.Core.UI;
using UnityEngine;

namespace DeNelle.Wallet
{
    /// <summary>Presentation-neutral bridge from the resolved Night Market card truth to shared cards.</summary>
    public static class NightMarketCardAdapter
    {
        public const int OffersPerPage = CardCollectionPaging.MaxVisibleCards;

        public static GenericCardModel Adapt(StorePackCardModel source, Action primaryAction)
        {
            string stateWords = !string.IsNullOrWhiteSpace(source.StateWord) ? source.StateWord :
                (!string.IsNullOrWhiteSpace(source.NotSellableReason) ? source.NotSellableReason : "Available");
            GenericCardState state = stateWords.IndexOf("owned", StringComparison.OrdinalIgnoreCase) >= 0
                ? GenericCardState.Owned
                : (!string.IsNullOrWhiteSpace(source.NotSellableReason) ? GenericCardState.Unavailable : GenericCardState.Available);
            string price = source.PriceMajor ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(source.PriceMinor)) price += " (" + source.PriceMinor + ")";
            return new GenericCardModel
            {
                StableId = source.Sku ?? string.Empty,
                ArtworkKey = source.ArtResource ?? string.Empty,
                Title = source.Name ?? string.Empty,
                Purpose = source.Contents ?? string.Empty,
                Badge = source.Badge ?? string.Empty,
                ContentsOrCost = price,
                StateWords = stateWords,
                ActionLabel = state == GenericCardState.Available ? "View offer" : stateWords,
                State = state,
                PrimaryAction = primaryAction
            };
        }

        // WO-1866: this method has no live caller in the repo today (grep-confirmed) — it builds a
        // fresh CardCollectionModel.Title from StoreStrings.Get on every invocation, so it already
        // re-resolves the current locale each time it runs; the coverage-gap bug this WO fixes only
        // bites a value that is resolved ONCE and then held past a runtime locale switch with no
        // path back to it. If a future caller keeps the returned model alive across screens rather
        // than rebuilding it per-open, that caller (not this pure builder) is where a
        // LocalText.Changed subscriber (see LocalizedLabel) belongs.
        public static CardCollectionModel Collection(IReadOnlyList<GenericCardModel> cards) =>
            new CardCollectionModel
            {
                CollectionId = "night-market-offers",
                // WO-1398: the ONE canon name (storeWordmark), never typed here.
                Title = StoreStrings.Get(StoreStrings.KeyWordmark),
                Subtitle = "Choose an offer to inspect its full contents and current channel price.",
                IconKey = "UI/NightMarket/night-market-wordmark",
                Cards = cards ?? Array.Empty<GenericCardModel>()
            };

        public static IReadOnlyList<GenericCardModel> Page(IReadOnlyList<GenericCardModel> cards, int page)
        {
            if (cards == null || cards.Count == 0) return Array.Empty<GenericCardModel>();
            int first = CardCollectionPaging.FirstIndex(page, cards.Count);
            int count = Math.Min(OffersPerPage, cards.Count - first);
            var result = new List<GenericCardModel>(count);
            for (int i = 0; i < count; i++) result.Add(cards[first + i]);
            return result;
        }
    }

    /// <summary>One long-lived pause lease for browsing plus nested offer/payment presentation.</summary>
    public sealed class NightMarketSharedCardSession : MonoBehaviour
    {
        private FocusedModalHost _host;
        private string _focusedSku;
        private int _confirmationDepth;

        public bool IsOpen => _host != null && _host.IsOpen;
        public int NavigationDepth => _host != null ? _host.NavigationDepth : 0;

        /// <summary>
        /// Opens the CARD BROWSER — the in-game offer list rendered by
        /// <see cref="FocusedModalHost"/>. Everything it touches is a Unity UI object.
        /// <para>
        /// ⛔ THIS IS NOT A WEB BROWSER AND THERE IS NO ROUND TRIP. The name cost a P0 triage a
        /// wrong first hypothesis on 2026-09-06 (WO-1441 §2): a device log showed
        /// <c>NightMarketSharedCardSession:OpenBrowser()</c> twice, fourteen seconds apart, with
        /// nothing coming back, and the ticket was written around "the app left for a browser and
        /// never returned" — a deep link, a custom scheme, an intent filter, a return leg to
        /// instrument. None of that exists. Both lines were STACK FRAMES under
        /// <c>[Flow:Pause] WorldHold ACQUIRE 'focused-card-modal'</c>, i.e. the player opening the
        /// Night Market twice; "nothing came back" because nothing ever left. The real defect was
        /// elsewhere entirely (no backend session was ever minted).
        /// </para>
        /// <para>
        /// ⚠ The method is deliberately NOT renamed: it is public API and a rename is churn that
        /// buys nothing. This note is the fix — if you arrived here hunting a deep link, stop.
        /// </para>
        /// </summary>
        public bool OpenBrowser()
        {
            if (_host == null)
            {
                _host = GetComponent<FocusedModalHost>();
                if (_host == null) _host = gameObject.AddComponent<FocusedModalHost>();
            }
            _focusedSku = null;
            _confirmationDepth = 0;
            return _host.OpenUnderExistingPanel();
        }

        public void ShowOffer(string sku)
        {
            if (!IsOpen || string.IsNullOrEmpty(sku) || string.Equals(_focusedSku, sku, StringComparison.Ordinal)) return;
            if (_focusedSku == null) _host.Push();
            _focusedSku = sku;
        }

        public IDisposable EnterConfirmation()
        {
            if (!IsOpen) OpenBrowser();
            _host.Push();
            _confirmationDepth++;
            return new ConfirmationLease(this);
        }

        public void Close()
        {
            _focusedSku = null;
            _confirmationDepth = 0;
            _host?.Close();
        }

        private void ExitConfirmation()
        {
            if (_confirmationDepth <= 0 || !IsOpen) return;
            _confirmationDepth--;
            _host.Pop();
        }

        private sealed class ConfirmationLease : IDisposable
        {
            private NightMarketSharedCardSession _owner;
            public ConfirmationLease(NightMarketSharedCardSession owner) { _owner = owner; }
            public void Dispose() { var owner = _owner; _owner = null; owner?.ExitConfirmation(); }
        }

        private void OnDisable() => Close();
        private void OnDestroy() => Close();
    }
}
