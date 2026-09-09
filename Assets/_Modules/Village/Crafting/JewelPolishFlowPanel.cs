using System;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using DeNelle.Village.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Village.Crafting
{
    /// <summary>Dedicated first-polish handoff and completion reveal. Jewelry recipes stay separate.</summary>
    public static class JewelPolishFlowPanel
    {
        private const string StartPanelName = "JewelPolishStart";
        private const string RevealPanelName = "JewelPolishReveal";
        private static GameObject s_canvas;
        private static PanelHandle s_handle;

        public static bool IsOpen => s_canvas != null;

        public static void ShowStart()
        {
            Close();
            var inventory = VillageInventory.Instance;
            if (inventory == null || inventory.Get(DungeonExclusiveItems.RoughStoneId) <= 0)
            {
                ElarionUiKit.ShowToast(JewelerDiscoveryText.NoStone.Resolve(), ElarionUiKit.ToastTone.Info);
                return;
            }

            var modal = ElarionUiKit.BuildObsidianModal(StartPanelName,
                JewelerDiscoveryText.PolishTitle.Resolve(), ElarionUiKit.ModalArchetype.Compact,
                Close, sortingOrder: 31040);
            if (!Adopt(modal, StartPanelName)) return;

            Transform body = modal.chrome.layout != null && modal.chrome.layout.body != null
                ? modal.chrome.layout.body : modal.chrome.content.transform;
            AddItemImage(body, "ItemIcons/ing_rough_stone");

            string duration = ElarionUi.Duration(Mathf.CeilToInt(JewelPolishCatalog.PolishSeconds));
            var copy = ElarionUiKit.Label(body, JewelerDiscoveryText.PolishBody.Resolve(new DurationArguments(duration)),
                0.33f, 0.67f, ElarionUi.Parchment, ElarionUi.FontBody,
                TextAlignmentOptions.Center, 0.06f, 0.94f);
            copy.enableWordWrapping = true;
            ElarionUiKit.FitBlock(copy, ElarionUi.FontFloorMobile, ElarionUi.FontBody);

            var begin = ElarionUiKit.Button(body, JewelerDiscoveryText.BeginPolish.Resolve(new DurationArguments(duration)),
                ElarionUiKit.ButtonKind.Gold, new Vector2(0.05f, 0.03f),
                new Vector2(0.95f, 0.25f), BeginPolish);
            MedievalUiSkin.ApplyButton(begin, primary: true);
            FlowTrace.Step("JewelPolish", "dedicated first-polish screen shown; duration=" + duration + ".");
        }

        public static void ShowReveal(string gemId)
        {
            Close();
            string gemName = MaterialCatalog.DisplayName(gemId);
            var modal = ElarionUiKit.BuildObsidianModal(RevealPanelName,
                JewelerDiscoveryText.RevealTitle.Resolve(), ElarionUiKit.ModalArchetype.Compact,
                Close, sortingOrder: 31050);
            if (!Adopt(modal, RevealPanelName)) return;

            Transform body = modal.chrome.layout != null && modal.chrome.layout.body != null
                ? modal.chrome.layout.body : modal.chrome.content.transform;
            AddItemImage(body, MaterialCatalog.IconPath(gemId));
            var copy = ElarionUiKit.Label(body, JewelerDiscoveryText.RevealBody.Resolve(new GemArguments(gemName)),
                0.35f, 0.66f, ElarionUi.Parchment, ElarionUi.FontBody,
                TextAlignmentOptions.Center, 0.06f, 0.94f, bold: true);
            copy.enableWordWrapping = true;
            ElarionUiKit.FitBlock(copy, ElarionUi.FontFloorMobile, ElarionUi.FontBody);
            var keep = ElarionUiKit.Button(body, JewelerDiscoveryText.KeepGem.Resolve(),
                ElarionUiKit.ButtonKind.Gold, new Vector2(0.05f, 0.03f),
                new Vector2(0.95f, 0.25f), Close);
            MedievalUiSkin.ApplyButton(keep, primary: true);
            FlowTrace.Step("JewelPolish", "gem reveal shown: '" + gemId + "'.");
        }

        private static bool Adopt(ElarionUiKit.ObsidianModal modal, string panelName)
        {
            if (modal == null || modal.canvas == null || modal.chrome == null) return false;
            s_canvas = modal.canvas;
            MedievalUiSkin.ApplyShell(modal.chrome, compact: true);
            s_handle = PanelManager.Register(panelName, Close, () => IsOpen);
            if (PanelManager.NotifyOpened(s_handle)) return true;
            Close();
            return false;
        }

        private static void AddItemImage(Transform body, string resourcePath)
        {
            Sprite sprite = !string.IsNullOrEmpty(resourcePath) ? Resources.Load<Sprite>(resourcePath) : null;
            if (sprite == null) return;
            GameObject art = ElarionUiKit.AddImage(body, "PolishItemImage",
                new Vector2(0.32f, 0.69f), new Vector2(0.68f, 0.96f),
                Color.white, rounded: false);
            var image = art != null ? art.GetComponent<Image>() : null;
            if (image == null) return;
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        private static void BeginPolish()
        {
            if (!JewelPolishService.TryStartPolish(out string failure))
            {
                ElarionUiKit.ShowToast(string.IsNullOrEmpty(failure) ? JewelerDiscoveryText.PolishFailed.Resolve() : failure,
                    ElarionUiKit.ToastTone.Danger);
                return;
            }
            Close();
            ElarionUiKit.ShowToast(JewelerDiscoveryText.PolishStarted.Resolve(), ElarionUiKit.ToastTone.Confirm);
            ObsidianQueueGate.RequestToggle();
        }

        public static void Close()
        {
            if (s_canvas != null) UnityEngine.Object.Destroy(s_canvas);
            s_canvas = null;
            if (s_handle != null) PanelManager.NotifyClosed(s_handle);
            s_handle = null;
        }
    }
}
