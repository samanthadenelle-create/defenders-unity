// =============================================================================
// DefenseReportPanel — the re-openable record of attacks on YOUR town (WO-1026).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.UI
//
// A DUMB SKIN over DefenseReportLedger. Master-detail on the FrameQuest grammar:
// the dark left well carries the report LIST, the right well carries the SELECTED
// report's detail.
//
// ⛔ BOTH WELLS ARE OBSIDIAN. THE DETAIL SIDE IS NOT PARCHMENT (WO-1515, 2026-09-06).
//    FrameQuest is a twoToneBody frame, so the kit paints TwoToneParchmentFill under
//    bodyRight; this screen covers BOTH zones with its own opaque plate and colours its
//    ink for dark (`_onParchment == false`). Ink choice and surface are ONE decision --
//    change one and you must change the other, or you ship the tan slab again. See
//    StyleObsidianWell for the RCA. Pinned by DefenseReportLayoutRegression.
//
// CONSTRUCTION LAW (non-negotiable here as everywhere):
//   • UXML DOES NOT WORK IN BUILDS — code-built uGUI via ElarionUiKit only.
//   • ASCII ONLY in every TMP string. LiberationSans-SDF tofus anything else, so:
//     "->" not an arrow, "..." not an ellipsis, "40%" not a fancy percent.
//   • ⛔ NEVER CONVEY MEANING BY COLOUR ALONE — the owner is red/green colourblind.
//     Every state on this screen is a SENTENCE: "OVERRUN - the Heart fell",
//     "DESTROYED", "damaged 40%", "Nothing was taken." Tints are decoration on top
//     of text that already says it. A greyscale screenshot must lose no information.
//   • ⛔ FIXED-PIXEL ROW BANDS VIA sizeDelta, **NOT** VIA LayoutElement.
//     This line said "via LayoutElement" until 2026-09-07 and that sentence IS the
//     WO-1585 defect, sitting in this file's own construction law. The kit scroll
//     column does not control child height (the documented PartyShop collapse), and
//     uGUI's layout group therefore reads `child.sizeDelta[axis]` and IGNORES the
//     LayoutElement outright (HorizontalOrVerticalLayoutGroup.cs:224-229). Rows and
//     the map plate band both shipped at the RectTransform default. Full RCA, with
//     the numbers measured off the owner's frame, at the top of DefenseMapPlate.cs.
//
// ⛔ IT NEVER READS WaveDamageReport, AND THAT IS LOAD-BEARING.
//    The panel renders the PERSISTED RECORD and nothing else. A panel that re-scanned
//    the live town could never render a report from a week ago (the town has changed)
//    and could never render a model-(c) ghost's report at all — which would quietly
//    turn the (c) source swap back into a rewrite. SiegeSpawnAuthorityRegression fails
//    the gate if a WaveDamageReport reference appears in this file.
//
// THE DOOR IS DELIBERATELY NOT MINTED HERE. CLAUDE.md §7 caps the calm(town) action
// bar at SIX visible faces and spends paragraphs on why; adding a seventh to reach
// this screen would silently undo that ruling. The panel ships REGISTERED and openable
// (PanelRouter.Open(PanelId.DefenseReport) + the DevPanel). Picking the town door — a
// badge on the Heart interaction, or a Manage-screen tab — is an owner call, recorded
// in the WO-1026 result.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Defense;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Village.UI
{
    /// <summary>Lists the retained defence reports and renders the selected one.</summary>
    [DisallowMultipleComponent]
    public sealed class DefenseReportPanel : MonoBehaviour
    {
        // ── The list row band is DERIVED, never a hand-typed number (WO-1515) ────
        // The old fixed 132px band carried a TWO-LINE label ("HELD\nHollow Host - 6h ago").
        // FitSingleLine is a WIDTH fit: it never shrinks to make a hard "\n" fit vertically, so
        // the second line overflowed its band and painted across the NEXT row's gold bezel --
        // exactly what the owner's 20:03 frame shows. The row is ONE fitted line now, and the
        // band is computed from the font it actually renders at, so the two can never disagree.
        private const float RowFontMax = 44f;          // one line, comfortably under FontBody
        private const float RowFontMin = 30f;          // FitSingleLine clamps up to the kit floor
        private const float RowLineBoxMul = 1.25f;     // TMP line box ~1.25em
        private const float RowPadPx = 26f;            // bezel inset, top + bottom

        /// <summary>DERIVED row band: one line box at <see cref="RowFontMax"/> plus the bezel
        /// inset, floored at the kit touch minimum. Never a literal.</summary>
        private static readonly float ListRowPx =
            Mathf.Max(ElarionUiKit.MinTouchPx, RowFontMax * RowLineBoxMul + RowPadPx);

        /// <summary>Gap between two row bands. PITCH = <see cref="ListRowPx"/> + this, and the
        /// layout oracle asserts the pitch clears the rendered line box at every capture aspect.</summary>
        private const float ListRowGapPx = 10f;

        /// <summary>MakeScrollZone padding on the DETAIL column (each edge). Named because the
        /// plate band's derived width is the viewport minus twice this.</summary>
        private const int DetailPadPx = 28;

        // ⛔ THE PLATE BAND IS NO LONGER A LITERAL (WO-1585). It is derived from the MEASURED
        //    detail viewport by DefenseMapPlate.DeriveHeightPx, and written to the band's
        //    sizeDelta by DefenseMapPlate.BuildBand -- the RCA for why a LayoutElement alone was
        //    ignored lives at the top of that file, sourced from the uGUI layout group itself.

        /// <summary>The well plate. FULLY opaque on purpose -- see <see cref="StyleObsidianWell"/>.
        /// ElarionUiKit.ObsidianFill is a=0.98, which lets 2% of whatever is behind it through;
        /// behind the DETAIL well the kit paints TwoToneParchmentFill, so 2% of tan is 2% too much.</summary>
        private static readonly Color WellFill = new Color(0.02f, 0.02f, 0.025f, 1f);

        private GameObject _ui;
        private RectTransform _listContent;
        private RectTransform _detailContent;

        /// <summary>The detail scroll VIEWPORT — the visible well. The plate band's height is
        /// derived from this rect, so the diagram is sized by the screen it is on rather than by
        /// a number typed once at some other aspect (WO-1585).</summary>
        private RectTransform _detailViewport;

        /// <summary>The band the plate lives in, held for the §12 rect dump below.</summary>
        private RectTransform _plateBand;
        private PanelHandle _panelHandle;
        private bool _onParchment;

        private List<DefenseOutcomeRecord> _rows = new List<DefenseOutcomeRecord>();
        private string _selectedId;

        /// <summary>The built map plate, held ONLY so its path segments can be re-solved after
        /// the layout pass gives the plate a real pixel size (see DefenseMapPlate.Plate.Relayout
        /// -- without this the polyline renders as a row of 2px stubs).</summary>
        private DefenseMapPlate.Plate _plate;

        /// <summary>True while the screen is up (built on open, destroyed on close).</summary>
        public bool IsOpen => _ui != null;

        private void Awake()
        {
            _panelHandle = PanelManager.Register("Defense Report", Close, () => IsOpen);
            PanelRouter.Register(PanelId.DefenseReport, Open);
        }

        private void OnDestroy()
        {
            if (_ui != null) Destroy(_ui);
            _ui = null;
            PanelRouter.Unregister(PanelId.DefenseReport, Open);
        }

        // ── Open / Close ─────────────────────────────────────────────────────────

        /// <summary>Opens the screen on the newest report.</summary>
        public void Open()
        {
            Close();

            _rows = DefenseReportLedger.NewestFirst();
            // WO-1515 sec.2B/2D - THE CHIP'S OWN DOOR HAS TO CLEAR THE CHIP.
            // Select() (the row tap) was the ONLY caller of MarkRead, so a player who opened
            // this screen through the new HUD chip, read the report it landed on, and closed
            // it again would have found the chip still there - a door that cannot be answered.
            // The landing record is therefore selected AND marked read here. Re-select the
            // newest whenever the remembered id is gone (trimmed by MaxRetained) or a newer
            // report has landed unread since the last open; otherwise the panel keeps the
            // player's last choice, which is what the row tap is for.
            if (_rows.Count > 0 &&
                (string.IsNullOrEmpty(_selectedId) || DefenseReportLedger.TryGet(_selectedId) == null || !_rows[0].Read))
                _selectedId = _rows[0].Id;

            if (!string.IsNullOrEmpty(_selectedId) && DefenseReportLedger.MarkRead(_selectedId))
                _rows = DefenseReportLedger.NewestFirst();   // the [NEW] tag on that row is stale now

            BuildChrome();
            // The wells have to have a REAL rect before Render derives the plate band from the
            // detail viewport; a fresh canvas reports zero until the first canvas update.
            Canvas.ForceUpdateCanvases();
            Render();

            if (!PanelManager.NotifyOpened(_panelHandle))
                return;   // rejected (e.g. mid-battle) — NotifyOpened already invoked Close

            FlowTrace.Step("Siege",
                $"report panel opened id={_selectedId} reports={_rows.Count} unread={DefenseReportLedger.UnreadCount()}.");
        }

        private void Close()
        {
            _listContent = null;
            _detailContent = null;
            _detailViewport = null;
            _plateBand = null;
            _plate = null;
            if (_ui != null) Destroy(_ui);
            _ui = null;
            PanelManager.NotifyClosed(_panelHandle);
        }

        // ── Chrome (presentation only — the frame IS the chrome) ─────────────────

        private void BuildChrome()
        {
            _ui = ElarionUiKit.BuildModalCanvas("DefenseReportPanelUI", 31000);
            var canvas = _ui.GetComponent<Canvas>();
            if (canvas != null) canvas.overrideSorting = true;
            ElarionUiKit.Scrim(_ui.transform, onTapClose: Close);

            var chrome = ElarionUiKit.BuildObsidianPanel(_ui.transform, "Attacks On Your Town",
                new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f), Close,
                frameName: RpgUiCatalog.FrameQuest, medallionIcon: "quest");

            var layout = chrome.layout;
            Transform listZone = layout != null && layout.bodyLeft != null
                ? (Transform)layout.bodyLeft
                : FallbackZone(chrome.content.transform, "ListWell",
                    new Vector2(0.035f, 0.22f), new Vector2(0.295f, 0.885f));
            Transform detailZone = layout != null && layout.bodyRight != null
                ? (Transform)layout.bodyRight
                : FallbackZone(chrome.content.transform, "DetailWell",
                    new Vector2(0.320f, 0.22f), new Vector2(0.965f, 0.885f));
            // This report is one dark modal, not a parchment sheet pasted beside a
            // black list. A matching authored card surface covers both inherited wells.
            StyleObsidianWell(listZone, "ReportListWell");
            StyleObsidianWell(detailZone, "ReportDetailWell");
            _onParchment = false;

            // Padding clears the bezel drawn by StyleObsidianWell so no line of text is ever
            // laid across the gold border; spacing IS the row gap the derived pitch is built from.
            _listContent = ElarionUiKit.MakeScrollZone(listZone, spacing: ListRowGapPx, padding: 22).content;
            var detailZoneHandle = ElarionUiKit.MakeScrollZone(detailZone, spacing: 12f, padding: DetailPadPx);
            _detailContent = detailZoneHandle.content;
            _detailViewport = detailZoneHandle.viewport;
        }

        // ── Render ───────────────────────────────────────────────────────────────

        private void Render()
        {
            RebuildList();
            RebuildDetail();

            // ⚠ TWO PASSES, NOT ONE (WO-1585). The column reads each child's sizeDelta.y during
            //   CalculateLayoutInputVertical, but a Paragraph's ContentSizeFitter WRITES its
            //   sizeDelta.y later in the same pass (SetLayoutVertical). One rebuild therefore
            //   sums pre-fit paragraph heights, and the content column ends up shorter than what
            //   it holds -- which is a scroll range that cannot reach the last rows, and a rect
            //   dump that measures a layout the player never sees.
            Settle();
            Settle();

            // The plate's rect only becomes real after the rebuilds above, so the path geometry
            // AND every label box is solved HERE rather than at build time.
            _plate?.Relayout();

            TraceDetailRects();
        }

        private void Settle()
        {
            Canvas.ForceUpdateCanvases();
            if (_listContent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(_listContent);
            if (_detailContent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(_detailContent);
        }

        /// <summary>
        /// §12 INSTRUMENTATION, PERMANENT. Dumps the measured world rects of the detail column's
        /// text rows against the plate band, so "the labels draw over the sentences" is a line in
        /// the log rather than a thing someone has to see. It NAMES any intersection, which is
        /// the only form of this finding that is actionable.
        /// </summary>
        private void TraceDetailRects()
        {
            if (!FlowTrace.Enabled || _detailContent == null) return;

            Rect band = _plateBand != null ? WorldRect(_plateBand) : new Rect();
            var sb = new System.Text.StringBuilder();
            sb.Append("detail rects: viewport=").Append(RectStr(WorldRect(_detailViewport)))
              .Append(" content=").Append(RectStr(WorldRect(_detailContent)))
              .Append(" band=").Append(RectStr(band));

            int overlaps = 0;
            for (int i = 0; i < _detailContent.childCount; i++)
            {
                var child = _detailContent.GetChild(i) as RectTransform;
                if (child == null || child == _plateBand || !child.gameObject.activeSelf) continue;
                Rect r = WorldRect(child);
                bool hit = band.width > 0.5f && band.height > 0.5f && r.width > 0.5f && r.height > 0.5f
                           && r.xMin < band.xMax && band.xMin < r.xMax
                           && r.yMin < band.yMax && band.yMin < r.yMax;
                if (hit)
                {
                    overlaps++;
                    sb.Append("\n  OVERLAP row[").Append(i).Append("] ").Append(RectStr(r))
                      .Append(" intersects the plate band -- a sentence and the diagram are on the "
                            + "same pixels.");
                }
            }

            if (overlaps > 0) FlowTrace.Warn("DefenseReport", sb.ToString());
            else FlowTrace.Step("DefenseReport", sb.Append(" | no text row intersects the band.").ToString());
        }

        private static readonly Vector3[] _corners = new Vector3[4];

        private static Rect WorldRect(RectTransform rt)
        {
            if (rt == null) return new Rect();
            rt.GetWorldCorners(_corners);
            float x0 = Mathf.Min(_corners[0].x, _corners[2].x), x1 = Mathf.Max(_corners[0].x, _corners[2].x);
            float y0 = Mathf.Min(_corners[0].y, _corners[2].y), y1 = Mathf.Max(_corners[0].y, _corners[2].y);
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        private static string RectStr(Rect r)
            => "[x " + r.xMin.ToString("0") + ".." + r.xMax.ToString("0")
             + " y " + r.yMin.ToString("0") + ".." + r.yMax.ToString("0")
             + " h " + r.height.ToString("0") + "]";

        private void RebuildList()
        {
            ClearChildren(_listContent);
            if (_listContent == null) return;

            if (_rows.Count == 0)
            {
                Paragraph(_listContent, "No attacks recorded.", ElarionUi.FontLabel, ElarionUi.ParchmentDim, false);
                return;
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                if (r == null) continue;
                string id = r.Id;
                bool selected = id == _selectedId;

                // The row label carries EVERY state in words: verdict, who, when, unread.
                // A greyscale capture of this list loses nothing.
                // ⛔ ONE LINE. No "\n" -- a hard break survives NoWrap, so a two-line label
                //    overflows the derived band and paints over the next row (WO-1515).
                string label = OutcomeWord(r.Outcome) + "  -  " + Safe(r.Attacker.DisplayName, "Unknown force")
                             + "  -  " + RelativeTime(r.EndedAtUnixMs)
                             + (r.Read ? string.Empty : "  [NEW]");

                var host = new GameObject("ReportRow", typeof(RectTransform), typeof(LayoutElement));
                host.transform.SetParent(_listContent, false);
                // ⛔ sizeDelta IS THE BAND (WO-1585). MakeScrollZone's column runs
                //    childControlHeight:false, and uGUI's HorizontalOrVerticalLayoutGroup then
                //    reads child.sizeDelta[axis] and IGNORES the LayoutElement entirely
                //    (HorizontalOrVerticalLayoutGroup.cs:224-229). The row shipped at the
                //    RectTransform default of 100 px -- UNDER ElarionUiKit.MinTouchPx (112) --
                //    which the owner's 2026-09-07 Seeker frame measures as a 135.5 device-px
                //    pitch at 2670x1200 (scaler 1.2431 => 109 canvas = 100 + ListRowGapPx),
                //    where the derived band would read 151.7. The LayoutElement below stays as
                //    advice for any host that DOES control child height; it is not the mechanism.
                ((RectTransform)host.transform).sizeDelta = new Vector2(0f, ListRowPx);
                var le = host.GetComponent<LayoutElement>();
                le.preferredHeight = ListRowPx;
                le.minHeight = ListRowPx;
                le.flexibleHeight = 0f;   // the band is the band; the column may not stretch it

                var rowBtn = ElarionUiKit.BuildObsidianButton(host.transform, label,
                    ElarionUiKit.ObsidianButtonStyle.Style1,
                    selected ? ElarionUiKit.ObsidianButtonColor.Yellow
                             : ElarionUiKit.ObsidianButtonColor.Gray,
                    Vector2.zero, Vector2.one, () => Select(id));

                // Armed EXPLICITLY at this screen's own font band rather than left to the kit
                // default, so the band above and the type below are derived from one number.
                if (rowBtn != null)
                {
                    var caption = rowBtn.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (caption != null) ElarionUiKit.FitSingleLine(caption, RowFontMin, RowFontMax);
                }
            }
        }

        private void Select(string reportId)
        {
            _selectedId = reportId;
            DefenseReportLedger.MarkRead(reportId);
            _rows = DefenseReportLedger.NewestFirst();
            Render();
        }

        private void RebuildDetail()
        {
            _plate = null;        // destroyed with the cleared children below
            _plateBand = null;
            ClearChildren(_detailContent);
            if (_detailContent == null) return;

            Color inkTitle = _onParchment ? ElarionUiKit.ParchmentInk : ElarionUi.Gilt;
            Color inkBody = _onParchment ? ElarionUiKit.ParchmentInk : ElarionUi.Parchment;
            Color inkDim = _onParchment ? ElarionUiKit.ParchmentInkDim : ElarionUi.ParchmentDim;

            var r = DefenseReportLedger.TryGet(_selectedId);
            if (r == null)
            {
                Paragraph(_detailContent,
                    "Your town has not been attacked yet. When it is, the report lands here: who came, " +
                    "where they broke through, and what it cost you.",
                    ElarionUi.FontBody, inkDim, false);
                return;
            }

            // Headline: the verdict is a WORD, the sentence under it carries the meaning,
            // the score is a label. Never hue-only (owner is colourblind).
            Paragraph(_detailContent, OutcomeWord(r.Outcome), ElarionUi.FontHead, inkTitle, true);
            Paragraph(_detailContent, OutcomeSentence(r.Outcome), ElarionUi.FontBody, inkBody, false);
            if (r.HasDefenseScore)
                Paragraph(_detailContent,
                    "Score " + r.DefenseScore + " / 100  -  "
                    + DefenseReportBuilder.DefenseScoreWord(r.DefenseScore),
                    ElarionUi.FontLabel, inkDim, false);
            Spacer(_detailContent, 10f);

            // Attacker: the panel renders THESE STRINGS. It never composes a name from
            // the Source enum -- that keeps model (c) a source swap. The one sanctioned
            // Source read is the small chip: a LABEL LOOKUP.
            Section(_detailContent, "Who came", inkTitle);
            Paragraph(_detailContent,
                Safe(r.Attacker.DisplayName, "Unknown force") + "  (" + SourceChip(r.Attacker.Source) + ")",
                ElarionUi.FontBody, inkBody, false);
            Paragraph(_detailContent,
                "Wave " + r.WaveId + "  -  strength " + r.Attacker.PowerRating
                + "  -  lasted " + DurationLabel(r.DurationSeconds),
                ElarionUi.FontLabel, inkDim, false);
            for (int i = 0; i < r.Attacker.Units.Count; i++)
            {
                var u = r.Attacker.Units[i];
                if (u == null) continue;
                string lvl = Mathf.Max(1, u.Level) > 1 ? "  (level " + Mathf.Max(1, u.Level) + ")" : "";
                Paragraph(_detailContent, "-  x" + u.Count + "  " + PrettyId(u.DefId) + lvl,
                    ElarionUi.FontLabel, inkBody, false);
            }

            Section(_detailContent, "Your town", inkTitle);
            Paragraph(_detailContent,
                r.Defender.StructureCount + " structures,  "
                + r.Defender.WallCount + " wall sections,  "
                + r.Defender.TowerCount + " towers.  "
                + (r.Defender.HeroPresent ? "Hero was in town." : "Hero was away."),
                ElarionUi.FontLabel, inkBody, false);
            // Layout hash is a fingerprint for "did a redesign change the outcome", not a
            // player-facing string. Keep it in the log; putting it on the plate made the
            // report read like a debug dump (owner 2026-09-09).
            FlowTrace.Step("DefenseReport",
                "layoutHash=" + Safe(r.Defender.LayoutHash, "unknown") + " id=" + r.Id);

            // THE DIAGNOSIS -- not "what did I lose" but "what do I move".
            Section(_detailContent, "What to change", inkTitle);
            var diagnosis = Diagnose(r);
            for (int i = 0; i < diagnosis.Count; i++)
                Paragraph(_detailContent, diagnosis[i], ElarionUi.FontBody, inkBody, i == 0);

            // The plate is DECORATION over facts already stated in words.
            BuildMapPlate(_detailContent, r, inkDim);

            Section(_detailContent, "Where they got in", inkTitle);
            if (r.Breaches.Count == 0)
            {
                Paragraph(_detailContent, "Nothing crossed your inner ring. The line held.",
                    ElarionUi.FontBody, inkBody, false);
            }
            else
            {
                for (int i = 0; i < r.Breaches.Count; i++)
                {
                    var b = r.Breaches[i];
                    if (b == null) continue;
                    string ord = i == 0 ? "1st" : (i + 1) + (i == 1 ? "nd" : i == 2 ? "rd" : "th");
                    Paragraph(_detailContent,
                        "-  " + ord + ": " + Safe(b.DisplayName, "Open ground")
                        + "  at " + DurationLabel(b.AtSeconds)
                        + "  by " + PrettyId(b.AttackerDefId)
                        + "  (" + DefenseMapPlate.Compass(b.WorldX - r.Defender.CoreX,
                                                           b.WorldZ - r.Defender.CoreZ)
                        + " of the Heart)",
                        ElarionUi.FontLabel, inkBody, i == 0);
                }
            }

            Section(_detailContent, "What broke", inkTitle);
            if (r.Rows.Count == 0)
            {
                Paragraph(_detailContent, "Nothing was damaged.", ElarionUi.FontBody, inkBody, false);
            }
            else
            {
                RenderBandGroup(r, DefenseBand.Front, "Front line (they meet this first)", inkBody, inkDim);
                RenderBandGroup(r, DefenseBand.Second, "Second line", inkBody, inkDim);
                RenderBandGroup(r, DefenseBand.Core, "Core (the Heart's ring)", inkBody, inkDim);
            }

            Section(_detailContent, "What they took", inkTitle);
            Paragraph(_detailContent, StakesLead(r.ResourcesLost), ElarionUi.FontBody, inkBody, false);
            Paragraph(_detailContent, StakesRule(r.ResourcesLost), ElarionUi.FontLabel, inkDim, false);
        }

        // ── ⭐ THE LEGIBILITY LAYER ──────────────────────────────────────────────

        /// <summary>
        /// The plate, plus its legend and its text twin. The plate is DECORATION over facts
        /// already stated in words: <see cref="DefenseMapPlate.DescribeMarks"/> prints the
        /// headline marks as sentences, and the legend spells every glyph out. If the plate
        /// fails to build, the screen is still complete.
        /// </summary>
        private void BuildMapPlate(RectTransform host, DefenseOutcomeRecord r, Color inkDim)
        {
            // Text twin FIRST, so it is present regardless of what the plate does.
            var described = DefenseMapPlate.DescribeMarks(r);
            for (int i = 0; i < described.Count; i++)
                Paragraph(host, described[i], ElarionUi.FontLabel, inkDim, false);

            // ⭐ THE BAND IS DERIVED FROM THE MEASURED WELL AND WRITTEN TO sizeDelta.
            //    Both halves matter and the second one is the WO-1585 defect: the kit scroll
            //    column runs childControlHeight:false, so a LayoutElement alone is INVISIBLE to
            //    it (RCA at the top of DefenseMapPlate). DefenseMapPlate.BuildBand is the one
            //    seam that does it right, and DefenseReportLayoutRegression measures that seam.
            float wellW = _detailViewport != null ? _detailViewport.rect.width : 0f;
            float wellH = _detailViewport != null ? _detailViewport.rect.height : 0f;
            float bandPx = DefenseMapPlate.DeriveHeightPx(Mathf.Max(0f, wellW - 2f * DetailPadPx), wellH);
            FlowTrace.Step("DefenseReport",
                $"plate band derived: viewport={wellW:F0}x{wellH:F0} pad={DetailPadPx} -> band={bandPx:F0} "
                + $"(min={DefenseMapPlate.PlateMinPx:F0} max={DefenseMapPlate.PlateMaxPx:F0} "
                + $"wellFraction={DefenseMapPlate.PlateWellFraction:F2}).");

            _plateBand = DefenseMapPlate.BuildBand(host, r, bandPx, out _plate);
            if (_plate == null)
            {
                // No diagram, so no band: an empty reserved strip would push the legend and the
                // breach list off the well for nothing. Collapsed rather than Destroy()ed --
                // Destroy is deferred to end of frame, so a destroyed band would still hold its
                // height through the layout passes below.
                if (_plateBand != null)
                {
                    _plateBand.sizeDelta = new Vector2(0f, 0f);
                    _plateBand.gameObject.SetActive(false);
                }
                _plateBand = null;
                Paragraph(host, "(map unavailable -- the positions above still describe it)",
                    ElarionUi.FontLabel, inkDim, false);
                return;
            }

            for (int i = 0; i < DefenseMapPlate.Legend.Length; i++)
                Paragraph(host, DefenseMapPlate.Legend[i], ElarionUi.FontLabel, inkDim, false);
        }

        /// <summary>
        /// Renders one FRONT / SECOND / CORE band, or nothing when it is empty. An empty band is
        /// deliberately silent rather than printed as "none": three headers with two "none"s
        /// under them buries the one band that actually matters.
        /// </summary>
        private void RenderBandGroup(DefenseOutcomeRecord r, DefenseBand band, string header,
            Color inkBody, Color inkDim)
        {
            var rows = new List<StructureOutcome>();
            for (int i = 0; i < r.Rows.Count; i++)
                if (r.Rows[i] != null && r.Rows[i].Band == band) rows.Add(r.Rows[i]);
            if (rows.Count == 0) return;

            Paragraph(_detailContent, header, ElarionUi.FontLabel, inkDim, true);
            for (int i = 0; i < rows.Count; i++)
            {
                var l = rows[i];
                // State in WORDS: "DESTROYED" / "damaged 40%" — never a coloured bar alone.
                string state = l.Destroyed
                    ? "DESTROYED"
                    : "damaged " + Mathf.RoundToInt(l.DamageFraction * 100f) + "%";
                string text = "-  " + Safe(l.DisplayName, "Structure") + "  -  " + state;
                // THE row that matters: they came through HERE, versus a row that merely took
                // splash damage. Identical-looking in a flat list, opposite instructions.
                if (l.BreachOrdinal == 1) text += "  -  THEY CAME THROUGH HERE";
                else if (l.BreachOrdinal > 1) text += "  -  breach #" + l.BreachOrdinal;
                if (l.LootStolen > 0) text += "  -  " + l.LootStolen + " carried off";
                Paragraph(_detailContent, text, ElarionUi.FontLabel, inkBody, false);

                string hold = HoldLine(l);
                if (!string.IsNullOrEmpty(hold))
                    Paragraph(_detailContent, "      " + hold, ElarionUi.FontLabel, inkDim, false);
                // Cost is OMITTED, never faked — HasCost carries that, same as the live banner.
                if (l.HasCost)
                    Paragraph(_detailContent, "      repair: " + CostLine(l), ElarionUi.FontLabel, inkDim, false);
            }
        }

        /// <summary>
        /// ⭐ THE HOLD-TIME SENTENCE — the highest-signal line in the report.
        /// "held 40s" and "fell in 4s" are the same row with opposite instructions.
        ///
        /// <para>⛔ An UNKNOWN hold time prints NOTHING. It must never render as "fell in 0s":
        /// a fabricated duration would point the player at the wrong structure, which is
        /// strictly worse than telling them nothing. Pre-existing damage says so explicitly,
        /// because that row's timing belongs to an earlier fight.</para>
        /// </summary>
        private static string HoldLine(StructureOutcome l)
        {
            if (l.WasAlreadyDamaged)
                return "was already damaged before this attack -- hold time is from an earlier fight";
            if (!l.HasHoldTime) return string.Empty;

            int s = Mathf.RoundToInt(l.HoldTimeSeconds);
            if (l.Destroyed)
                return s <= 5
                    ? "fell in " + s + "s -- it barely slowed them"
                    : "held " + s + "s before it fell";
            return "held " + s + "s and survived";
        }

        /// <summary>
        /// The report's headline, in sentences the player can act on. Built ONLY from recorded
        /// fields — it never re-scans the town, so it reads identically for an old report or
        /// (later) a model-(c) ghost's.
        /// <para>Every claim here is one the data actually supports. Where the data is thin the
        /// diagnosis says less rather than guessing: an invented cause is exactly the thing that
        /// makes a player move the wrong tower and conclude the report lies.</para>
        /// </summary>
        private static List<string> Diagnose(DefenseOutcomeRecord r)
        {
            var outLines = new List<string>();

            // 1. The approach + the first breach — where to look.
            var first = r.Breaches.Count > 0 ? r.Breaches[0] : null;
            if (first != null)
            {
                outLines.Add("They got in "
                    + DefenseMapPlate.Compass(first.WorldX - r.Defender.CoreX,
                                              first.WorldZ - r.Defender.CoreZ)
                    + " of the Heart, " + Mathf.RoundToInt(first.AtSeconds) + "s in"
                    + (string.IsNullOrEmpty(first.DisplayName) || first.DisplayName == "Open ground"
                        ? ", across open ground." : ", past " + first.DisplayName + "."));
            }
            else if (r.Outcome == DefenseOutcome.Overrun)
            {
                outLines.Add("The Heart fell without a recorded ring crossing.");
            }
            else
            {
                outLines.Add("Your ring held -- nothing got inside it.");
            }

            // 2. The weakest structure BY TIME. This is the "what do I move" line, and it is
            //    only offered when a real measurement exists.
            StructureOutcome weakest = null;
            for (int i = 0; i < r.Rows.Count; i++)
            {
                var l = r.Rows[i];
                if (l == null || !l.Destroyed || !l.HasHoldTime) continue;
                if (weakest == null || l.HoldTimeSeconds < weakest.HoldTimeSeconds) weakest = l;
            }
            if (weakest != null)
                outLines.Add("Weakest point: " + Safe(weakest.DisplayName, "a structure")
                    + " fell in " + Mathf.RoundToInt(weakest.HoldTimeSeconds) + "s ("
                    + LineWord(weakest.Band) + ").");

            // 3. Did the front line do its job? Only stated when there IS a front line.
            int frontLost = 0, frontTotal = 0, behindLost = 0;
            for (int i = 0; i < r.Rows.Count; i++)
            {
                var l = r.Rows[i];
                if (l == null) continue;
                if (l.Band == DefenseBand.Front) { frontTotal++; if (l.Destroyed) frontLost++; }
                else if (l.Destroyed) behindLost++;
            }
            if (r.Defender.FrontRadius <= 0f)
                outLines.Add("You have no wall ring, so nothing meets them before your buildings do.");
            else if (frontLost > 0 && behindLost == 0)
                outLines.Add("Your front line absorbed all of it -- " + frontLost
                    + " lost there and nothing behind it was touched.");
            else if (frontTotal == 0 && behindLost > 0)
                outLines.Add("They reached past your front line without breaking it -- check for a gap, not a weak wall.");

            return outLines;
        }

        private static string LineWord(DefenseBand l)
        {
            switch (l)
            {
                case DefenseBand.Front: return "front line";
                case DefenseBand.Core: return "core";
                default: return "second line";
            }
        }

        // ── Copy helpers (every state is a SENTENCE — colourblind law) ───────────

        /// <summary>WO-1515 door lane: the switch MOVED to Core
        /// (DeNelle.Core.HudModel.DefenseReportChipModel.OutcomeWord). The HUD chip says the same
        /// word as this list and this heading, and two copies of a three-case switch is exactly
        /// how those three surfaces start disagreeing. This stays as the panel's local name.</summary>
        private static string OutcomeWord(DefenseOutcome o) =>
            DeNelle.Core.HudModel.DefenseReportChipModel.OutcomeWord(o);

        private static string OutcomeSentence(DefenseOutcome o)
        {
            switch (o)
            {
                case DefenseOutcome.Overrun: return "The Heart fell. They took the town.";
                case DefenseOutcome.Breached: return "You won, but they got inside your ring.";
                default: return "They never reached your inner ring.";
            }
        }

        /// <summary>The ONE sanctioned read of AttackerSource in presentation: a LABEL LOOKUP,
        /// not a branch in the layout. Everything else on this screen renders the record's own
        /// strings, which is what keeps model (c) a source swap.</summary>
        private static string SourceChip(AttackerSource s)
        {
            switch (s)
            {
                case AttackerSource.GhostSnapshot: return "echo of a rival town";
                case AttackerSource.LivePvp: return "live rival";
                default: return "raiders";
            }
        }

        private static string CostLine(StructureOutcome l)
        {
            var parts = new List<string>();
            if (l.RepairWood > 0) parts.Add(l.RepairWood + " wood");
            if (l.RepairIron > 0) parts.Add(l.RepairIron + " iron");
            if (l.RepairFood > 0) parts.Add(l.RepairFood + " stone");
            if (l.RepairCrystals > 0) parts.Add(l.RepairCrystals + " crystals");
            return parts.Count == 0 ? "free" : string.Join(", ", parts);
        }

        /// <summary>
        /// WHAT THE ATTACK TOOK -- an EXPLICIT statement either way, never a blank the player
        /// reads as a bug.
        ///
        /// <para>* THIS IS THE ONLY PLACE A THEFT IS EVER ANNOUNCED, and it renders the ledger
        /// VERBATIM. The ledger IS the debit (DefenseReportBuilder.ApplyStakes spends exactly these
        /// buckets), so this screen cannot tell the player a different number than the wallet lost
        /// -- there is nothing to re-derive here. An unexplained shrinking number is the resented
        /// version of this mechanic; a report that names it is the loop working.</para>
        ///
        /// <para>! THE COPY MUST TEACH THE RULE, because the rule is what turns the loss into
        /// "damn, I should improve my defenses" instead of "the game erased something I paid for".
        /// It has three jobs: name what was taken, name that a RESERVE was protected and a CAP
        /// held, and name what can NEVER be touched. Crystals and purchases are called out BY NAME
        /// -- a player who is told her crystals are safe does not go looking to see whether they
        /// are.</para>
        ///
        /// <para>Owner ruling 2026-08-27: LOOTABLE = wood, iron, stone, gold. UNTOUCHABLE =
        /// crystals, SKR, purchased goods, equipped gear. "Stone" is the balance internally named
        /// Food, and it is rendered with the player-facing word.</para>
        ///
        /// <para>Every state is carried by TEXT. The owner is colourblind: this must read the same
        /// in greyscale, so nothing here depends on a tint.</para>
        /// </summary>
        private static string StakesLine(StakesLedger s)
            => StakesLead(s) + "\n" + StakesRule(s);

        private static string StakesLead(StakesLedger s)
        {
            if (s == null || s.IsEmpty) return "Nothing was taken.";

            var parts = new List<string>();
            if (s.Wood > 0) parts.Add(s.Wood + " wood");
            if (s.Iron > 0) parts.Add(s.Iron + " iron");
            if (s.Food > 0) parts.Add(s.Food + " stone");
            if (s.Coins > 0) parts.Add(s.Coins + " gold");
            // Crystals/Magic can NEVER be taken. They are listed only so that if one ever appeared
            // it would be VISIBLE on screen rather than silently hidden by a renderer that "knows"
            // it cannot happen.
            if (s.Crystals > 0) parts.Add(s.Crystals + " crystals");
            if (s.Magic > 0) parts.Add(s.Magic + " magic");
            return "They carried off " + string.Join(", ", parts) + ".";
        }

        private static string StakesRule(StakesLedger s)
        {
            if (s == null || s.IsEmpty)
                return "Your reserve held. Crystals, purchases and equipped gear are never at risk.";
            return "A protected reserve was left. One attack cannot take more than its cap. "
                 + "Crystals, purchases and equipped gear are never at risk.";
        }

        private static string DurationLabel(float seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            if (s < 60) return s + "s";
            int m = s / 60;
            int r = s % 60;
            return r == 0 ? m + "m" : m + "m " + r + "s";
        }

        /// <summary>Player-facing unit name from a stored def id. Presentation of the
        /// opaque id, never a Source branch -- model (c) still swaps the producer.</summary>
        private static string PrettyId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "unknown";
            var sb = new System.Text.StringBuilder(id.Length + 4);
            bool cap = true;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (c == '_' || c == '-') { sb.Append(' '); cap = true; continue; }
                sb.Append(cap ? char.ToUpperInvariant(c) : c);
                cap = false;
            }
            return sb.ToString();
        }

        private static void Section(Transform parent, string title, Color ink)
        {
            Spacer(parent, 8f);
            Paragraph(parent, title, ElarionUi.FontLabel, ink, true);
            var rule = new GameObject("Rule", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            rule.transform.SetParent(parent, false);
            var img = rule.GetComponent<Image>();
            img.color = new Color(ink.r, ink.g, ink.b, 0.35f);
            img.raycastTarget = false;
            var rt = (RectTransform)rule.transform;
            rt.sizeDelta = new Vector2(0f, 2f);
            var le = rule.GetComponent<LayoutElement>();
            le.preferredHeight = 2f;
            le.minHeight = 2f;
            le.flexibleHeight = 0f;
        }

        private static void Spacer(Transform parent, float px)
        {
            var go = new GameObject("Gap", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(0f, px);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = px;
            le.minHeight = px;
            le.flexibleHeight = 0f;
        }

        private static string RelativeTime(double whenUnixMs)
        {
            if (whenUnixMs <= 0) return "recently";
            double deltaMs = TimeSource.NowUnixMs() - whenUnixMs;
            if (deltaMs < 0) deltaMs = 0;
            double mins = deltaMs / 60000.0;
            if (mins < 1) return "just now";
            if (mins < 60) return Mathf.RoundToInt((float)mins) + " min ago";
            double hours = mins / 60.0;
            if (hours < 24) return Mathf.RoundToInt((float)hours) + "h ago";
            return Mathf.RoundToInt((float)(hours / 24.0)) + "d ago";
        }

        private static string Safe(string s, string fallback)
            => string.IsNullOrEmpty(s) ? fallback : s;

        // ── Builders (layout plumbing only — chrome comes from the kit) ──────────

        private static void Paragraph(Transform parent, string text, int size, Color color, bool bold)
        {
            var go = new GameObject("Para", typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            ElarionUiKit.EnsureFont(t);
            t.text = text ?? string.Empty;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.textWrappingMode = TMPro.TextWrappingModes.Normal;
            t.raycastTarget = false;
            if (bold) t.fontStyle = FontStyles.Bold;
            var fit = go.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        private static Transform FallbackZone(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return go.transform;
        }

        /// <summary>
        /// ⭐ THE PLATE AND THE BEZEL ARE TWO IMAGES, AND THAT SEPARATION IS THE WHOLE FIX.
        ///
        /// <para>RCA (owner device frame Logs/device/screens/owner-defense-report-20260906-200350.png,
        /// build 2026.09.07.358574): the DETAIL pane rendered as a flat TAN slab with near-invisible
        /// grey text. The old body of this method built ONE image, seeded it with ObsidianFill, and
        /// then OVERWROTE that fill -- `img.sprite = frame; img.color = Color.white` -- turning the
        /// plate into the bezel. `card-frame-empty` is a hollow border with a TRANSPARENT centre, so
        /// nothing dark was left. Behind the detail zone the kit paints
        /// `ZoneBacking(layout.bodyRight, TwoToneParchmentFill)` (ElarionUiKit, the FrameQuest
        /// twoToneBody branch) -- that tan read straight through the hole, under text this panel had
        /// already coloured for a DARK surface (`_onParchment == false` -> Gilt / Parchment /
        /// ParchmentDim). Light ink on tan is the unreadable pane in the frame.</para>
        ///
        /// <para>The LEFT well took the identical call and looked fine, which is the proof: its
        /// backing is the kit's dark TwoToneWellFill, so the same hole exposed black. One code path,
        /// two surfaces, one broken -- the fill was never doing the work it was credited with.</para>
        ///
        /// <para>So: an OPAQUE plate first, the bezel as its own later sibling on top. A future edit
        /// cannot collapse them back into one image without deleting a line that says why.</para>
        /// </summary>
        private static void StyleObsidianWell(Transform zone, string name)
        {
            if (zone == null) return;

            // 1. THE PLATE — opaque obsidian, no sprite swap, ever.
            var plate = ElarionUiKit.AddImage(zone, name, Vector2.zero, Vector2.one,
                WellFill, rounded: true);
            var plateImg = plate != null ? plate.GetComponent<Image>() : null;
            if (plateImg != null)
            {
                plateImg.color = WellFill;   // survives ApplyRounded seeding a sprite
                plateImg.raycastTarget = false;
            }

            // 2. THE BEZEL — decoration, drawn over the plate. Absent art is not a failure:
            //    the well is already a complete dark surface without it.
            var frame = Resources.Load<Sprite>("UI/ElarionMedieval/frames/card-frame-empty");
            if (frame == null)
            {
                FlowTrace.Warn("Siege", "report well '" + name
                    + "' has no card-frame-empty bezel art; the obsidian plate stands alone.");
                return;
            }
            var bezel = ElarionUiKit.AddImage(zone, name + "Bezel", Vector2.zero, Vector2.one,
                Color.white, rounded: false);
            var bezelImg = bezel != null ? bezel.GetComponent<Image>() : null;
            if (bezelImg != null)
            {
                bezelImg.sprite = frame;
                bezelImg.type = Image.Type.Simple;
                bezelImg.color = Color.white;
                bezelImg.raycastTarget = false;
            }
        }

        private static void ClearChildren(RectTransform host)
        {
            if (host == null) return;
            for (int i = host.childCount - 1; i >= 0; i--)
            {
                var c = host.GetChild(i);
                if (c == null) continue;
                // Deactivate FIRST: Destroy is deferred to end of frame, so a cleared row would
                // otherwise still occupy its band through this frame's layout passes (and show up
                // in the rect dump as a phantom overlap).
                c.gameObject.SetActive(false);
                Destroy(c.gameObject);
            }
        }
    }
}
