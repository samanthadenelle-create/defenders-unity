// =============================================================================
// GooglePlayStorefront - the ONE PanelId.RealmStore registrar in a GOOGLE_PLAY artifact.
// -----------------------------------------------------------------------------
// WO-1395 (2026-09-05). The WO was minted on the premise that this class and
// PackStoreBootstrap both register PanelId.RealmStore in the SAME build and race on
// static-init order. Proven false at source: DeNelle.Wallet.asmdef carries
// defineConstraints ["!GOOGLE_PLAY"] (WO-1282 Lane B, commit c06a66de5) and
// DeNelle.GooglePlay.asmdef carries ["GOOGLE_PLAY"], so exactly ONE of the two
// registrars is compiled into any artifact and they can never coexist. This is the
// Play build's whole store, not a second store beside the Night Market - the Night
// Market (PackStore, DeNelle.Wallet) does not exist in this artifact at all.
//
// WHAT WAS REAL in the WO's finding, and what this file now closes:
//   * a door-tagged open (PanelRouter.Open(RealmStore, "settings"|"vendor")) fell back
//     to the plain opener here because no Action<string> was registered, so the WO-1388
//     funnel's store_opened {door} was never recorded under Play. Both call shapes now
//     land on THIS one modal and the door is latched exactly as PackStoreBootstrap does.
//   * registration was untraced and the open was unguarded. Both are now [Flow:Store].
//   * a second registrar in the same build is now DETECTED (PanelRouter.IsRegistered
//     before we register -> FlowTrace.Fail) instead of silently replaced.
// Pinned by Assets/Editor/Regression/RealmStoreSingleRegistrarRegression.cs.
// =============================================================================

using System;
using DeNelle.Commerce;          // StoreFocusRequest - the rail-neutral door latch (WO-1388)
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Payments;
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;

namespace DeNelle.GooglePlay
{
    internal sealed class GooglePlayStorefront : MonoBehaviour
    {
        private static GooglePlayStorefront _active;
        private GooglePlayStorefrontVM _vm;
        private ElarionUiKit.ObsidianModal _modal;
        private TextMeshProUGUI _status;
        private PanelHandle _panelHandle;
        private bool _open;

        /// <summary>The door the funnel records for a plain (context-free) open. Mirrors
        /// PackStore.DoorHudCard: every remaining plain open is the HUD Night Market card.</summary>
        private const string DoorHudCard = "hud-card";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterRoute()
        {
            // WO-1395 - a registrar that finds the id already taken is the collision the WO
            // feared. It cannot happen while the asmdef constraints hold (see header); if it
            // ever does, say so instead of letting PanelRouter.Register replace it silently.
            if (PanelRouter.IsRegistered(PanelId.RealmStore))
                FlowTrace.Fail("Store",
                    "second PanelId.RealmStore registrar detected: GooglePlayStorefront found the id already " +
                    "registered at BeforeSceneLoad. Exactly one storefront may register this id per artifact " +
                    "(DeNelle.Wallet is !GOOGLE_PLAY, DeNelle.GooglePlay is GOOGLE_PLAY).");

            PanelRouter.Register(PanelId.RealmStore, Open);
            // The CONTEXT opener (WO-1388 door funnel): a caller that names its door lands on the
            // SAME modal as a plain open, with the door latched for store_opened {door}.
            PanelRouter.Register(PanelId.RealmStore, (Action<string>)OpenFromDoor);
            FlowTrace.Step("Store",
                "RealmStore registrar=GooglePlayStorefront skin=play channel=" + PaymentChannelResolver.ResolveStampedChannel() +
                " (plain + door context; DeNelle.Wallet/PackStore is compiled out of this GOOGLE_PLAY artifact).");
        }

        /// <summary>The door-naming open (PanelRouter context opener). Latches the door for the
        /// funnel, then opens exactly as the plain <see cref="Open"/>.</summary>
        private static void OpenFromDoor(string door)
        {
            FlowTrace.Step("Store", "GooglePlayStorefront.OpenFromDoor door='" + (door ?? "<null>") + "'.");
            StoreFocusRequest.RequestDoor(door);
            Open();
        }

        private static void Open()
        {
            using var _ = FlowTrace.Enter("Store", "GooglePlayStorefront.Open");
            if (_active == null)
            {
                bool built = Guard.Try("Store", "build the Google Play storefront host", () =>
                {
                    var canvas = ElarionUiKit.BuildModalCanvas("GooglePlayStoreHost", 31000);
                    _active = canvas.AddComponent<GooglePlayStorefront>();
                    _active.Build();
                });
                if (!built || _active == null)
                {
                    FlowTrace.Fail("Store", "GooglePlayStorefront.Open: host build failed - the store did NOT open.");
                    return;
                }
                FlowTrace.Step("Store", "GooglePlayStorefront: host spawned (first open).");
            }
            // WO-1388 funnel step 1 - store_opened {door}, the one emit site in THIS artifact
            // (PackStore.TrackStoreOpened is the one in a DAPP_STORE artifact; never both compiled).
            TrackStoreOpened();
            _active.SetOpen(true);
        }

        /// <summary>store_opened {door}: a named door comes from the latch; a plain open is the HUD card.</summary>
        private static void TrackStoreOpened()
        {
            string named = StoreFocusRequest.ConsumeDoor();
            string door = named ?? DoorHudCard;
            FlowTrace.Step("Store", "funnel store_opened door=" + door + (named == null ? " (inferred)" : " (named)") + " [play].");
            Guard.Try("Store", "track store_opened",
                () => DeNelle.Core.Analytics.EventTracker.Track("store_opened", new { door }));
        }

        private void Awake()
        {
            _vm = GooglePlayStorefrontVM.CreateDefault(SetStatus);
            _panelHandle = PanelManager.Register("Google Play Realm Store", Close, () => _open);
        }

        private void OnDestroy()
        {
            if (_active == this) _active = null;
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
        }

        private void Build()
        {
            // WO-1398: the Play skin titles itself with the store's ONE canon name (storeWordmark),
            // the same words the HUD card that opened it rendered - never a typed literal.
            _modal = ElarionUiKit.BuildObsidianModal("GooglePlayRealmStore", HudStrings.StoreFaceLabel("play-skin"),
                new Vector2(.08f, .04f), new Vector2(.92f, .96f), Close,
                frameName: RpgUiCatalog.FrameCore, medallionIcon: "shop");
            var body = _modal.chrome.layout != null && _modal.chrome.layout.body != null
                ? _modal.chrome.layout.body.transform : _modal.chrome.content.transform;
            // The subtitle sits in its OWN band above the list well (WO-1743). It used to be at
            // .92-.99 with the row loop starting at .90, and the first clamp-grown row painted
            // over it — that half-covered sentence is the top of the device capture.
            var subtitle = ElarionUiKit.Label(body, "Secure purchases through Google Play",
                SubtitleY0, SubtitleY1,
                ElarionUi.ParchmentDim, ElarionUi.FontLabel, TextAlignmentOptions.Center, .02f, .98f);
            ElarionUiKit.FitSingleLine(subtitle);

            // ── WO-1743 — THE ROWS ARE A KIT SCROLL ZONE, NOT HAND-PLACED FRACTIONS ──────────
            // The shipped Play build (2026.09.09.362625) drew this panel ILLEGIBLE on the owner's
            // Seeker — logs/device/store-listing.png: every row's frame painted over its
            // neighbours, "Secure purchases through Google Play" half-covered, CLOSE sitting on a
            // product row. It was NOT a billing defect (the "Unavailable" text is a separate Play
            // Console configuration matter); it was this loop.
            //
            // WHAT IT USED TO DO, and why each half of it failed:
            //   float top = .90f, height = .095f;  y1 = top - i * height;  y0 = y1 - .082f;
            //
            //   (1) THE TOUCH FLOOR ATE THE GAP. At the device's 2670x1200 against the kit's
            //       1080x1920 reference (BuildModalCanvas, match 0.5) the canvas resolves to
            //       ~2148x965 REFERENCE px, and this modal's body measures ~542 ref px tall. So
            //       the pitch was .095*542 = ~51 px on ~44 px rows. ClampMinTouch arms
            //       UiKitMinTouchGuard on every kit button, and its LateUpdate
            //       (ElarionUiKit.cs:1179-1184) grows anything under MinTouchPx=112
            //       SYMMETRICALLY ABOUT ITS CENTRE — so each 44 px row became 112 px and spilled
            //       ~34 px into the row above AND the row below. The kit says this in its own
            //       words at ElarionUiKitObsidian.cs:574-580: "the root cause of stacked/
            //       overlapping menu buttons ... was every menu HAND-PLACING fraction-anchored
            //       buttons: the MinTouchPx(112) floor grows each button, and on a short modal
            //       body those grown rects overlap". This panel was still doing exactly that.
            //
            //   (2) THE COLUMN RAN OFF THE BOTTOM OF THE BODY. packs.json ships 18 storeVisible
            //       packs, so the loop reached y1 = .90 - 17*.095 = -0.715. Rows 10..18 anchored
            //       BELOW the body entirely and passed straight through the old fixed Restore
            //       (.20-.285) and Deletion (.105-.19) bands — index 6 lands at .33, index 7 at
            //       .235, which is precisely where RESTORE PURCHASES appears interleaved between
            //       "Cord of Timber" and the next pack in the device capture. That interleave is
            //       the proof, not an inference.
            //
            // WHY A SCROLL VIEW AND NOT SMALLER ROWS: 18 packs + Restore + Deletion = 20 controls.
            // At the 112 px touch floor with an 8 px gap that is 20*112 + 19*8 = 2392 ref px of
            // content in a ~542 px body. There is no row height that is both legible and legally
            // tappable and fits — shrinking rows would just re-arm the clamp that caused this. So
            // the list becomes the kit's FIT-OR-SCROLL zone (§1.14, ElarionUiKit.MakeScrollZone):
            // a RectMask2D clips it, so a row can NEVER again paint over the subtitle, the status
            // line or the chrome CLOSE, however many packs the catalog grows to.
            //
            // WHY RESTORE + DELETION ARE THE TAIL OF THE LIST AND NOT A PINNED FOOTER: pinning
            // them costs 2*112 + 18 = 242 ref px, which would leave 542 - 38 (subtitle) - 54
            // (status) - 27 (hint) - 242 = ~181 px of scroll well — under two rows visible. As
            // list tail rows they keep the full touch floor, stay reachable by scrolling, and the
            // well shows ~3.4 rows instead. Their labels are self-describing sentences, so the
            // distinction never rests on colour (the owner is red/green colourblind).
            var listZone = ElarionUiKit.AddImage(body, "StoreListZone",
                new Vector2(.02f, ListZoneY0), new Vector2(.98f, ListZoneY1),
                new Color(0f, 0f, 0f, 0f), rounded: false);
            var scroll = ElarionUiKit.MakeScrollZone(listZone.transform, RowGapPx, ScrollPadPx);

            var rows = _vm.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                AddListRow(scroll.content, row.Label,
                    row.Available ? ElarionUiKit.ObsidianButtonColor.Green : ElarionUiKit.ObsidianButtonColor.Gray,
                    row.Available ? () => _vm.Purchase(row.Sku) : (System.Action)null);
            }

            AddListRow(scroll.content, "Restore purchases",
                ElarionUiKit.ObsidianButtonColor.Yellow, _vm.Restore);
            AddListRow(scroll.content, "Request account and data deletion",
                ElarionUiKit.ObsidianButtonColor.Gray, _vm.RequestDeletion);

            // The overflow affordance is WORDS, never a cut-off glyph and never a colour — the
            // same §1.14 hint DungeonTreasurePanel uses. The strip is RESERVED whether or not it
            // has anything to say, so the well's height (and the row pitch inside it) is identical
            // at three rows and at thirty.
            int total = rows.Count + 2;
            var hint = ElarionUiKit.Label(body,
                total > 0 ? total + " items -- scroll the list for more" : "",
                HintY0, HintY1, ElarionUi.ParchmentDim, ElarionUi.FontMicro,
                TextAlignmentOptions.Center, .03f, .97f);
            ElarionUiKit.FitSingleLine(hint);

            _status = ElarionUiKit.Label(body, "", StatusY0, StatusY1, ElarionUi.ParchmentDim,
                ElarionUi.FontBody, TextAlignmentOptions.Center, .03f, .97f);

            FlowTrace.Step("Store", "GooglePlayStorefront: built " + rows.Count +
                " pack rows + 2 account rows in a kit scroll zone at " + RowHeightPx +
                "px each (touch floor " + ElarionUiKit.MinTouchPx + ").");
            ElarionUiKit.DumpZoneLayout(listZone.transform, "google-play-store");
            _modal.canvas.SetActive(false);
        }

        // ── Body bands, as fractions of the modal body. Disjoint by construction: the list well
        //    ends where the hint strip begins and the hint strip ends where the status line does,
        //    so no band can be grown into another by a font or a touch floor. ───────────────────
        private const float SubtitleY0 = .93f, SubtitleY1 = 1.00f;
        private const float ListZoneY0 = .175f, ListZoneY1 = .92f;
        private const float HintY0 = .12f, HintY1 = .17f;
        private const float StatusY0 = .01f, StatusY1 = .11f;

        /// <summary>Row height in reference px. This is the kit touch floor itself, so
        /// UiKitMinTouchGuard has nothing left to grow and can never spill a row into its
        /// neighbour again — the defect WO-1743 fixed. Never lower it.</summary>
        private const float RowHeightPx = ElarionUiKit.MinTouchPx;
        private const float RowGapPx = 8f;
        private const int ScrollPadPx = 6;

        /// <summary>
        /// One row of the scrolling store list.
        /// <para>⛔ THE sizeDelta IS THE HEIGHT, AND IT IS NOT OPTIONAL. MakeScrollZone's content
        /// column runs <c>childControlHeight = false</c> (deliberately — kit rows carry no
        /// ILayoutElement, and a height-controlling group reads preferred-height 0 and collapses
        /// the whole column; see the comment in ElarionUiKitObsidian.MakeScrollZone). The
        /// VerticalLayoutGroup also RESETS each child's anchors to (0,1), so the 0..1 anchors
        /// BuildObsidianButton was handed collapse to zero height without this line.</para>
        /// <para>⛔ AND IT IS SET ON THE COLUMN'S DIRECT CHILD, NOT ON <c>btn.transform</c>. In the
        /// kit's prefab mode BuildObsidianButton returns a Button found by FindDeep, which may be
        /// NESTED inside the instantiated prefab — sizing that would size a grandchild and leave
        /// the layout child at height 0.</para>
        /// </summary>
        private static void AddListRow(RectTransform column, string label,
            ElarionUiKit.ObsidianButtonColor color, System.Action onClick)
        {
            if (column == null) return;
            ElarionUiKit.BuildObsidianButton(column, label,
                ElarionUiKit.ObsidianButtonStyle.Style1, color,
                Vector2.zero, Vector2.one, onClick);
            if (column.childCount == 0) return;
            var child = column.GetChild(column.childCount - 1) as RectTransform;
            if (child != null) child.sizeDelta = new Vector2(0f, RowHeightPx);
        }

        private void SetOpen(bool open)
        {
            if (_modal == null || _modal.canvas == null) return;
            _open = open;
            _modal.canvas.SetActive(open);
            if (open && !PanelManager.NotifyOpened(_panelHandle))
            {
                _open = false;
                _modal.canvas.SetActive(false);
            }
            else if (!open) PanelManager.NotifyClosed(_panelHandle);
        }

        private void Close() => SetOpen(false);
        private void SetStatus(string value) { if (_status != null) _status.text = value ?? string.Empty; }
    }
}
