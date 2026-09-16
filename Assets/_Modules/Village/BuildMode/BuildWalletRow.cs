// =============================================================================
// BuildWalletRow — the Build HUD resource strip (Grok slice 3 / icon chips 2026-08-29).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// A left-aligned row of kit CurrencyChips for the Build HUD — Wood / Iron / Stone /
// Crystals / Gold — the SAME icon-first chips as the town HUD right-rail resource
// stack (HudKitController.BuildResourceChips). Letter badges (W/I/S/C/G) are retired:
// the owner asked for icon chips like the HUD resource bar; Food was depreciated and
// this slot is Stone (EconomyService.Food save field, canon §7 / WO-1163).
//
// Amounts come from LiveWalletSource's WalletVM; this View binds Changed and never
// names EconomyService. Meaning is icon-first with a word tag ONLY when the icon
// sprite fails (CurrencyChip colourblind law). ASCII CompactNumber; code-built uGUI.
// =============================================================================

using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.UI;

namespace DeNelle.Village
{
    /// <summary>
    /// Build HUD resource chips (Wood/Iron/Stone/Crystals/Gold) as kit CurrencyChips.
    /// </summary>
    public sealed class BuildWalletRow : MonoBehaviour
    {
        // Chip geometry — slightly wider than the old letter-badge chips so the
        // currency icon + CompactNumber seat like the HUD rail chips.
        private const float ChipWidthPx = 188f;
        private const float ChipHeightPx = 72f;
        private const float ChipSpacingPx = 10f;

        private static readonly ElarionUiKit.CurrencyKind[] Kinds =
        {
            ElarionUiKit.CurrencyKind.Wood,
            ElarionUiKit.CurrencyKind.Iron,
            ElarionUiKit.CurrencyKind.Stone,     // Stone (concept id via ConceptIdFor)
            ElarionUiKit.CurrencyKind.Crystal,
            ElarionUiKit.CurrencyKind.Gold,
        };

        // Word tags only paint when the icon sprite is missing (CurrencyChip rule).
        private static readonly string[] Tags =
        {
            "Wood", "Iron", "Stone", "Crystals", "Gold",
        };

        private ElarionUiKit.CurrencyChipHandle[] _chips;
        private bool _built;

        private LiveWalletSource _wallet;

        /// <summary>Build the chip row into <paramref name="parent"/> (a left-anchored band).</summary>
        public void Build(Transform parent)
        {
            if (_built) return;
            _built = true;

            if (_wallet == null) _wallet = LiveWalletSource.CreateDefault();
            _wallet.Changed -= Refresh;
            _wallet.Changed += Refresh;

            var rowGo = new GameObject("WalletChips",
                typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rowGo.transform.SetParent(parent, false);
            var rrt = (RectTransform)rowGo.transform;
            rrt.anchorMin = new Vector2(0f, 0.5f);
            rrt.anchorMax = new Vector2(0f, 0.5f);
            rrt.pivot = new Vector2(0f, 0.5f);
            rrt.anchoredPosition = new Vector2(24f, 0f);
            rrt.sizeDelta = new Vector2(0f, ChipHeightPx);
            var layout = rowGo.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = ChipSpacingPx;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;

            _chips = new ElarionUiKit.CurrencyChipHandle[Kinds.Length];
            for (int i = 0; i < Kinds.Length; i++)
            {
                var host = new GameObject("Chip_" + Tags[i],
                    typeof(RectTransform), typeof(LayoutElement));
                host.transform.SetParent(rowGo.transform, false);
                var hrt = (RectTransform)host.transform;
                hrt.sizeDelta = new Vector2(ChipWidthPx, ChipHeightPx);
                var le = host.GetComponent<LayoutElement>();
                le.preferredWidth = ChipWidthPx;
                le.preferredHeight = ChipHeightPx;
                le.minWidth = ChipWidthPx;
                le.minHeight = ChipHeightPx;

                // Kit CurrencyChip — same builder as the town HUD resource rail.
                _chips[i] = ElarionUiKit.CurrencyChip(
                    host.transform, Kinds[i],
                    Vector2.zero, Vector2.one,
                    primary: Kinds[i] == ElarionUiKit.CurrencyKind.Gold,
                    tag: Tags[i]);
            }

            FlowTraceSafe("Build wallet strip: " + Kinds.Length +
                " CurrencyChip(s) Wood/Iron/Stone/Crystals/Gold (HUD icon parity)");
            Refresh();
        }

        /// <summary>Re-read the live wallet DTO and update every chip's amount.</summary>
        public void Refresh()
        {
            if (_wallet == null || _chips == null) return;
            var entries = _wallet.Wallet.Entries;
            // LiveWalletSource order is wood/iron/stone/crystals/gold — match Kinds[].
            for (int i = 0; i < _chips.Length && i < entries.Count; i++)
            {
                if (_chips[i] != null)
                    _chips[i].SetAmount(entries[i].Amount, animate: false);
            }
        }

        private void OnEnable()
        {
            if (_wallet != null)
            {
                _wallet.Changed -= Refresh;
                _wallet.Changed += Refresh;
            }
            if (_built) Refresh();
        }

        private void OnDisable()
        {
            if (_wallet != null) _wallet.Changed -= Refresh;
        }

        private void OnDestroy()
        {
            if (_wallet != null) { _wallet.Changed -= Refresh; _wallet.Dispose(); _wallet = null; }
        }

        private static void FlowTraceSafe(string msg)
        {
            DeNelle.Core.Diagnostics.FlowTrace.Step("BuildHud", msg);
        }
    }
}
