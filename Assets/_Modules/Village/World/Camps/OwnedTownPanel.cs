using System;
using System.Linq;
using DeNelle.Core;
using DeNelle.Core.State;
using DeNelle.Core.Tutorial;
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Village.World.Camps
{
    public sealed class OwnedTownPanel : MonoBehaviour
    {
        private GameObject _ui;
        private string _selected;
        private string _feedback;
        private bool _repairMode;
        private string _repairSelected;
        private RepairHighlight _highlight;
        private ElarionUiKit.ConfirmModal _saleConfirm;
        private static string Text(string suffix) => LocalText.Get("ownedTown." + suffix);
        private OwnedBaseState Property => GameStateService.Instance?.State?.OwnedBase;

        public void SelectStructure(string instanceId)
        {
            _selected = instanceId; _feedback = null; _repairMode = false; Show();
        }
        public void HideForBuild()
        {
            Close();
            if (_highlight != null) _highlight.SetSelected(false);
        }

        public void Show()
        {
            if (_ui != null) Destroy(_ui);
            var property = Property;
            if (property == null) return;
            _ui = ElarionUiKit.BuildModalCanvas("OwnedTownPanel", 650);
            var chrome = ElarionUiKit.BuildObsidianPanel(_ui.transform, Text("title"),
                new Vector2(.54f, .08f), new Vector2(.97f, .9f), Close, withBackdrop: false, withClose: false);
            var body = chrome.content.transform;
            bool revealed = (property.milestoneFlags & OwnedBaseMilestones.OwnershipRevealed) != 0;
            bool repaired = (property.milestoneFlags & OwnedBaseMilestones.EssentialRepairCompleted) != 0;
            bool pristine = property.structures.All(s => s.retired || s.condition01 >= 1f);
            bool repairScreen = revealed && !pristine && (!repaired || _repairMode);
            string stage = !revealed ? "revealHint" : !repaired ? (pristine ? "inspectHint" : "repairHint") :
                _repairMode && !pristine ? "repairChoiceHint" : property.reenteredLayoutRevision == 0 ? "designHint" : "readyHint";
            ElarionUiKit.Label(body, _feedback ?? Text(stage), .72f, repairScreen ? .81f : .87f,
                ElarionUi.Parchment, 25, TextAlignmentOptions.TopLeft, .08f, .92f);
            var commands = new GameObject("TownCommands", typeof(RectTransform));
            commands.transform.SetParent(body, false);
            var rect = (RectTransform)commands.transform;
            rect.anchorMin = new Vector2(.04f, .12f); rect.anchorMax = new Vector2(.96f, .70f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            if (repairScreen) { rect.anchorMin = new Vector2(.04f, .08f); rect.anchorMax = new Vector2(.96f, .56f); }
            var column = ElarionUiKit.BuildButtonColumn(rect, gapPx: 12f);
            if (OwnedTownDesignService.IsBusy || OwnedTownConstructionService.IsBusy) return;
            if (!revealed) Add(column, "begin", Reveal);
            else if (!repaired && pristine) Add(column, "inspect", Inspect);
            else if (!repaired || (_repairMode && !pristine)) ShowRepairControls(body, column, property, repaired);
            else
            {
                var manifest = OwnedTownTemplateManifest.Load();
                var towers = property.structures.Where(s => s.condition01 > 0 && manifest != null && manifest.IsEditableStructure(s)).ToList();
                if (!towers.Any(s => s.instanceId == _selected)) _selected = towers.FirstOrDefault()?.instanceId;
                if (_selected == null && _highlight != null) _highlight.SetSelected(false);
                if (_selected != null)
                {
                    Highlight(property.structures.Find(s => s.instanceId == _selected));
                    var selectionRow = new GameObject("TownSelectionRow", typeof(RectTransform), typeof(LayoutElement));
                    selectionRow.transform.SetParent(column, false);
                    selectionRow.GetComponent<LayoutElement>().minHeight = ElarionUiKit.MinTouchPx;
                    selectionRow.GetComponent<LayoutElement>().preferredHeight = ElarionUiKit.CanonCtaHeight;
                    ElarionUiKit.BuildObsidianButton(selectionRow.transform, Text("nextTower"),
                        ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                        Vector2.zero, new Vector2(.46f, 1f), () => {
                        int index = towers.FindIndex(s => s.instanceId == _selected);
                        _selected = towers[(index + 1) % towers.Count].instanceId; _feedback = null; Show();
                    });
                    ElarionUiKit.BuildObsidianButton(selectionRow.transform, Text("upgrade"),
                        ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                        new Vector2(.49f, 0f), new Vector2(.74f, 1f), OpenUpgrade);
                    ElarionUiKit.BuildObsidianButton(selectionRow.transform, Text("sell"),
                        ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                        new Vector2(.77f, 0f), Vector2.one, RequestSale);
                    foreach (var label in selectionRow.GetComponentsInChildren<TMP_Text>(true))
                    { label.enableAutoSizing = true; label.fontSizeMin = 18f; label.fontSizeMax = 28f; }
                    var moveRow = new GameObject("MoveTowerRow", typeof(RectTransform), typeof(LayoutElement));
                    moveRow.transform.SetParent(column, false);
                    var rowSize = moveRow.GetComponent<LayoutElement>();
                    rowSize.minHeight = ElarionUiKit.MinTouchPx;
                    rowSize.preferredHeight = ElarionUiKit.CanonCtaHeight;
                    ElarionUiKit.BuildObsidianButton(moveRow.transform, Text("moveWest"),
                        ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                        Vector2.zero, new Vector2(.48f, 1f), () => Move(Vector3.left * 3f));
                    ElarionUiKit.BuildObsidianButton(moveRow.transform, Text("moveEast"),
                        ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                        new Vector2(.52f, 0f), Vector2.one, () => Move(Vector3.right * 3f));
                }
                var buildRow = new GameObject("TownBuildAndVisitRow", typeof(RectTransform), typeof(LayoutElement));
                buildRow.transform.SetParent(column, false);
                buildRow.GetComponent<LayoutElement>().minHeight = ElarionUiKit.MinTouchPx;
                buildRow.GetComponent<LayoutElement>().preferredHeight = ElarionUiKit.CanonCtaHeight;
                bool visit = property.reenteredLayoutRevision > 0 || (property.milestoneFlags & OwnedBaseMilestones.LayoutChoiceCompleted) != 0;
                ElarionUiKit.BuildObsidianButton(buildRow.transform, Text("build"),
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                    Vector2.zero, new Vector2(visit ? .28f : 1f, 1f), () => BuildModeController.EnsureExists().Enter());
                if (visit)
                    ElarionUiKit.BuildObsidianButton(buildRow.transform, Text(property.reenteredLayoutRevision > 0 ? "practice" : "reenter"),
                        ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                        new Vector2(.32f, 0f), Vector2.one, () => {
                            if (property.reenteredLayoutRevision == 0) SceneRouter.GoOwnedTown();
                            else if (!DeNelle.Village.Arena.OwnedTownPracticeController.TryEnter(out var refusal)) Fail(refusal);
                        });
                if (!pristine)
                {
                    var exits = new GameObject("TownRepairAndExitRow", typeof(RectTransform), typeof(LayoutElement));
                    exits.transform.SetParent(column, false);
                    exits.GetComponent<LayoutElement>().minHeight = ElarionUiKit.MinTouchPx;
                    exits.GetComponent<LayoutElement>().preferredHeight = ElarionUiKit.CanonCtaHeight;
                    ElarionUiKit.BuildObsidianButton(exits.transform, Text("repairMore"),
                        ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                        Vector2.zero, new Vector2(.48f, 1f), () => { _repairMode = true; _feedback = null; Show(); });
                    ElarionUiKit.BuildObsidianButton(exits.transform, Text("castle"),
                        ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                        new Vector2(.52f, 0f), Vector2.one, SceneRouter.GoCastle);
                    foreach (var label in exits.GetComponentsInChildren<TMP_Text>(true))
                    {
                        label.enableAutoSizing = true;
                        label.fontSizeMin = 18f;
                        label.fontSizeMax = 28f;
                    }
                }
            }
            if (!revealed || !repaired || pristine) Add(column, "castle", SceneRouter.GoCastle);
            foreach (var button in commands.GetComponentsInChildren<Button>(true))
                foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
                {
                    label.enableAutoSizing = true; label.fontSizeMin = 18f; label.fontSizeMax = 28f;
                    label.margin = new Vector4(12f, 0f, 12f, 0f);
                }
        }

        private static void Add(Transform column, string key, Action action) =>
            ElarionUiKit.AddColumnButton(column, Text(key), ElarionUiKit.ObsidianButtonColor.Gray, action);

        private void Reveal()
        {
            if (!OwnedBaseProgression.TryCompleteMilestone(Property, OwnedBaseMilestones.OwnershipRevealed, out var next, out var reason) ||
                !GameStateService.Instance.TryCommitOwnedBaseRevision(next, out reason)) { Fail(reason); return; }
            TutorialSignals.Raise(DeNelle.Core.Tutorial.TutorialSignals.OwnedTownRevealed); _feedback = null; Show();
        }

        private void Repair()
        {
            if (!OwnedTownRepairService.TryRepair(_repairSelected, out var reason)) { Fail(reason); return; }
            TutorialSignals.Raise(DeNelle.Core.Tutorial.TutorialSignals.OwnedTownRepaired); SceneRouter.GoOwnedTown();
        }

        private void ShowRepairControls(Transform body, Transform column, OwnedBaseState property, bool canChoose)
        {
            var damaged = property.structures.Where(s => !s.retired && s.condition01 < 1f).ToList();
            if (!canChoose || !damaged.Any(s => s.instanceId == _repairSelected))
            {
                if (!OwnedTownRepairService.TryChooseFirstRepair(property, out var first, out _, out var failure))
                { _feedback = failure; return; }
                _repairSelected = first.instanceId;
            }
            var record = damaged.Find(s => s.instanceId == _repairSelected);
            if (record == null || !OwnedTownRepairService.TryQuote(record, out var quote, out _)) return;
            var debit = OwnedBaseProgression.RepairWalletDebit(property.repairSupplies, quote);
            var supplyCost = new DeNelle.Core.Catalog.ResourceCost {
                wood = quote.wood - debit.wood, iron = quote.iron - debit.iron, stone = quote.stone - debit.stone
            };
            string name = DeNelle.Core.Catalog.CatalogRegistry.Get(record.placement.itemId)?.displayName ?? record.placement.itemId;
            ElarionUiKit.Label(body, name + "  " + Mathf.RoundToInt(record.condition01 * 100f) + "%", .83f, .89f,
                ElarionUi.Gold, 24, TextAlignmentOptions.Left, .08f, .92f);
            ElarionUiKit.Label(body, LocalText.Format("ownedTown.repairPayment", CostText(supplyCost), CostText(debit)), .58f, .69f,
                ElarionUi.Parchment, 20, TextAlignmentOptions.TopLeft, .08f, .92f);
            Highlight(record);
            if (canChoose) Add(column, "nextRepair", () => {
                int index = damaged.FindIndex(s => s.instanceId == _repairSelected);
                _repairSelected = damaged[(index + 1) % damaged.Count].instanceId; _feedback = null; Show();
            });
            Add(column, "repair", Repair);
            if (canChoose) Add(column, "backToDesign", () => { _repairMode = false; _feedback = null; Show(); });
        }

        private static string CostText(DeNelle.Core.Catalog.ResourceCost cost)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (cost.wood > 0) parts.Add(cost.wood + " " + DeNelle.Core.Economy.TownBankCapacity.DisplayName(DeNelle.Core.Economy.BankResource.Wood));
            if (cost.stone > 0) parts.Add(cost.stone + " " + DeNelle.Core.Economy.TownBankCapacity.DisplayName(DeNelle.Core.Economy.BankResource.Stone));
            if (cost.iron > 0) parts.Add(cost.iron + " " + DeNelle.Core.Economy.TownBankCapacity.DisplayName(DeNelle.Core.Economy.BankResource.Iron));
            if (cost.crystals > 0) parts.Add(cost.crystals + " " + DeNelle.Core.Economy.TownBankCapacity.DisplayName(DeNelle.Core.Economy.BankResource.Crystals));
            return parts.Count == 0 ? "0" : string.Join(" · ", parts);
        }

        private void Highlight(OwnedBaseStructure record)
        {
            if (record == null) return;
            if (record.inheritedPose == null)
            {
                if (!OwnedTownLayoutSnapshot.TryGridBounds(record, out var bounds, out _)) return;
                if (_highlight == null) _highlight = RepairHighlight.Create(transform);
                _highlight.FitTo(bounds); _highlight.SetSelected(true); return;
            }
            var manifest = OwnedTownTemplateManifest.Load();
            var entry = manifest?.entries.Find(e => e.structure.inheritedPose.templateStructureId == record.inheritedPose.templateStructureId);
            if (entry == null) return;
            if (_highlight == null) _highlight = RepairHighlight.Create(transform);
            _highlight.FitTo(OwnedTownTemplateManifest.WorldBounds(entry, record.inheritedPose));
            _highlight.SetSelected(true);
        }

        private void Inspect()
        {
            if (!OwnedBaseProgression.TryInspectPristineTown(Property, out var next, out var reason) ||
                !GameStateService.Instance.TryCommitOwnedBaseRevision(next, out reason)) { Fail(reason); return; }
            TutorialSignals.Raise(DeNelle.Core.Tutorial.TutorialSignals.OwnedTownRepaired); _feedback = null; Show();
        }

        private void Move(Vector3 offset)
        {
            var record = Property.structures.Find(s => s.instanceId == _selected);
            if (record == null || !OwnedTownScenePose.TryResolveStructure(gameObject.scene, record, out var target, out var reason))
            { Fail(Text("selectTower")); return; }
            if (!OwnedTownDesignService.TryBeginMove(_selected, target.position + offset, (saved, failure) => {
                if (this == null || !isActiveAndEnabled) return;
                if (saved) TutorialSignals.Raise(DeNelle.Core.Tutorial.TutorialSignals.OwnedTownDesigned);
                _feedback = saved ? Text("saved") : failure; Show();
            }, out reason)) { Fail(reason); return; }
            _feedback = Text("checkingMove"); Show();
        }

        private void RequestSale()
        {
            if (_saleConfirm != null) return;
            string instanceId = _selected;
            if (!OwnedTownConstructionService.TryQuoteSale(instanceId, out var refund, out var reason)) { Fail(reason); return; }
            OpenSaleConfirmation(instanceId, Property.revision, refund);
        }

        private void OpenSaleConfirmation(string instanceId, int revision, DeNelle.Core.Catalog.ResourceCost refund)
        {
            _saleConfirm = ElarionUiKit.BuildConfirmModal("OwnedTownSaleConfirm", Text("sell"),
                LocalText.Format("ownedTown.saleQuote", CostText(refund)), Text("sell"), Text("keepStructure"),
                onConfirm: () => {
                    CloseSale();
                    if (!OwnedTownConstructionService.TrySell(instanceId, revision, out var credited, out var failure))
                    { Fail(failure); return; }
                    TutorialSignals.Raise(TutorialSignals.OwnedTownDesigned);
                    _feedback = LocalText.Format("ownedTown.saleComplete", CostText(credited)); Show();
                }, onCancel: CloseSale);
        }

        private void CloseSale()
        {
            if (_saleConfirm?.canvas != null) Destroy(_saleConfirm.canvas);
            _saleConfirm = null;
        }

        private void OpenUpgrade()
        {
            var property = Property;
            var record = property?.structures.Find(s => s.instanceId == _selected && !s.retired);
            if (record == null) { Fail(Text("selectTower")); return; }
            if (!PanelRouter.Open(PanelId.BuildingUpgrade, OwnedTownJobKey.Compose(property.baseId, record.instanceId)))
                Fail("The upgrade page could not open. Please try again.");
        }

        private void Fail(string reason) { _feedback = reason; Show(); }
        private void Close() { CloseSale(); if (_ui != null) Destroy(_ui); _ui = null; }
        private void OnDestroy() { Close(); if (_highlight != null) Destroy(_highlight.gameObject); }
    }
}
