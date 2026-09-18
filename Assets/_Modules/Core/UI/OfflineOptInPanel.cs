// =============================================================================
// OfflineOptInPanel — PROD-010. The opt-in prompt for offline mode, and the
// progress it shows while the one-time CDN pull runs.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core (Core/UI). Code-built uGUI on the Obsidian kit — no UXML
// (UXML does not render in builds, learned the hard way).
//
// OWNER SPEC 2026-08-19: opt IN to offline mode; on yes, a first-time CDN pull of
// everything needed; that pull needs Wi-Fi; afterwards the game falls back to local
// when there is no network.
//
// ⛔ THE SIZE IS SHOWN, ALWAYS, BEFORE ANYTHING DOWNLOADS.
// This is ~88 MB. An earlier plan for this ticket promised the player "10 seconds",
// which was only ever true while PROD-009 was going to shrink the download; the owner
// retired PROD-009 ("PROD 10 kills 10 and 09"), so the honest figure is the whole set:
// roughly 141 s at 5 Mbps and 471 s at 1.5 Mbps. A prompt that hides the number and
// then holds someone for eight minutes has lied to them. The number comes from
// GetDownloadSizeAsync - MEASURED, never typed - so it stays true when content changes.
//
// ASCII-only strings: non-ASCII renders as tofu in TMP.
// Never meaning by colour alone: every state is worded, not just tinted (the owner is
// red/green colourblind).
// =============================================================================

using System;
using System.Collections;
using UnityEngine;
using TMPro;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.UI
{
    /// <summary>PROD-010 opt-in prompt + download progress. One at a time.</summary>
    public sealed class OfflineOptInPanel : MonoBehaviour
    {
        private static OfflineOptInPanel s_active;

        private ElarionUiKit.ObsidianModal _modal;
        private TextMeshProUGUI _body;
        private TextMeshProUGUI _progress;
        private TextMeshProUGUI _localSaveNote;
        private bool _downloading;

        // ── WO-1128 §3.4 — the local-save truth, on the screen that offers the mode ──
        // The owner asked "but then the save would only persist locally right?" and this
        // panel had no answer on it: the body said "Download everything now so the game
        // works without a connection? / Checking download size..." and stopped. A player
        // deciding to rely on offline mode deserves the same answer she got.
        //
        // It is a STANDING label, not a line appended to the body text: the body is
        // rewritten four times by the size/verdict/download arms below, and copy that
        // lives inside a string one of those arms overwrites is copy that disappears at
        // the exact moment it matters (the moment the player is choosing). Its own rect
        // cannot be overwritten by any of them.
        //
        // Keys, not sentences (CLAUDE.md §7): both live in BOTH canon-strings.json copies,
        // byte-identical and ASCII-only. HudStrings is the Core.UI canon resolver; the keys
        // are deliberately NOT in HudStrings.AllKeys, because that array is what
        // HudLabelFitRegression measures against CHIP and BAR-FACE boxes — this is modal
        // body copy that wraps in a rect of its own and has no such geometry to fail.
        private const string KeyLocalSaveTitle = "offlineLocalSaveTitle";
        private const string KeyLocalSaveNote  = "offlineLocalSaveNote";

        // MODAL ARBITER. This builds a 31010-band modal with a scrim, so without a handle
        // PanelManager.AnyOpen stays FALSE while the panel covers the screen: the world
        // interact button stays live underneath, the Android back button has nothing to
        // close, and BattleLock cannot reject it. ONE handle per panel lifetime.
        private PanelHandle _panelHandle;

        private void Awake()
        {
            _panelHandle = PanelManager.Register("Play Offline", DeclineAndClose, IsShowing);
        }

        /// <summary>Arbiter visibility probe (NotifyOpened verifies with it).</summary>
        private bool IsShowing() => _modal != null && _modal.canvas != null && _modal.canvas.activeInHierarchy;

        /// <summary>
        /// Show the prompt. No-op when one is already up, when the player has already
        /// pulled for this build, or when there is nothing to download.
        /// </summary>
        public static void Show()
        {
            if (s_active != null) return;

            // Browser builds already stream their content from the web host and rely on the
            // browser's cache policy. The mobile/desktop offline pull is not a meaningful WebGL
            // operation. Keep this runtime guard even though Settings compiles its button out,
            // so a future caller cannot reopen the misleading download flow.
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                FlowTrace.Warn("OfflineContent", "opt-in prompt skipped - offline pull is not available in WebGL");
                return;
            }

            if (OfflineContentService.PulledForThisBuild)
            {
                FlowTrace.Step("OfflineContent", "opt-in prompt skipped - already pulled for this build");
                return;
            }

            var go = new GameObject("OfflineOptInPanel");
            var p = go.AddComponent<OfflineOptInPanel>();   // Awake registers the arbiter handle
            DontDestroyOnLoad(go);
            p.Build();

            // The arbiter CAN REJECT (battle-lock) - and on rejection it has already invoked
            // our close hook and torn this panel down. Never force-show over that: an 88 MB
            // download prompt on top of a live battle is the worst possible moment for one.
            if (!PanelManager.NotifyOpened(p._panelHandle))
            {
                FlowTrace.Warn("OfflineContent",
                    "opt-in prompt REJECTED by the modal arbiter (battle-lock) - not shown.");
                return;
            }

            s_active = p;
        }

        private void Build()
        {
            // TALLER MODAL (was 0.24-0.76). Landscape vertical is scarce, so the local-save
            // note is given ROOM rather than squeezed into the gap between the body and the
            // buttons: the alternative was shrinking the body font toward the legibility
            // floor, and the standing rule is that the words get shorter, never the type.
            // The CTA band keeps its share (0.10-0.26 of a now-larger box, so the buttons
            // grew), and nothing here touches CanonCtaWidth/CanonCtaHeight.
            _modal = ElarionUiKit.BuildObsidianModal(
                // Reuses the Settings row's own key rather than minting a synonym: the row that
                // opens this modal and the modal's heading name the SAME feature (WO-1857).
                "OfflineOptInUI", new LocalizedText("settings.offline.play").Resolve(),
                new Vector2(0.10f, 0.14f), new Vector2(0.90f, 0.86f),
                onClose: DeclineAndClose, sortingOrder: 31010);
            MedievalUiSkin.ApplyShell(_modal.chrome, compact: true);

            var content = _modal.chrome.content.transform;

            _body = ElarionUiKit.Label(content,
                new LocalizedText("offline.optIn.prompt").Resolve() + "\n\n" +
                new LocalizedText("offline.optIn.checking_size").Resolve(),
                0.56f, 0.84f, ElarionUi.Parchment, 34, TextAlignmentOptions.Top,
                0.06f, 0.94f);

            // The consequence is WORDED, in its own block, above the two buttons — never
            // signalled by tint (the owner is red/green colourblind, so colour carries no
            // meaning here; the dim parchment is hierarchy only).
            _localSaveNote = ElarionUiKit.Label(content,
                HudStrings.Get(KeyLocalSaveTitle) + "\n" + HudStrings.Get(KeyLocalSaveNote),
                0.39f, 0.54f, ElarionUi.ParchmentDim, 26, TextAlignmentOptions.Top,
                0.06f, 0.94f);

            _progress = ElarionUiKit.Label(content, "",
                0.33f, 0.38f, ElarionUi.ParchmentDim, 28, TextAlignmentOptions.Center);

            var download = ElarionUiKit.Button(content,
                new LocalizedText("offline.optIn.download_now").Resolve(), ElarionUiKit.ButtonKind.Gold,
                new Vector2(0.06f, 0.18f), new Vector2(0.48f, 0.32f), OnDownload);

            var later = ElarionUiKit.Button(content,
                new LocalizedText("offline.optIn.not_now").Resolve(), ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.52f, 0.18f), new Vector2(0.94f, 0.32f), DeclineAndClose);
            MedievalUiSkin.ApplyButton(download, primary: true);
            MedievalUiSkin.ApplyButton(later, primary: false);

            // PERMANENT (§12): a capture of this screen can then be read as "the player was
            // told the save is device-only" rather than argued about from the source.
            FlowTrace.Step("OfflineContent",
                _localSaveNote != null
                    ? "opt-in prompt built with the local-only save note: " + _localSaveNote.text
                    : "opt-in prompt built WITHOUT the local-only save note label - the player is being " +
                      "offered offline mode with its save consequence unstated (WO-1128 s3.4).");

            StartCoroutine(ShowSize());
        }

        private IEnumerator ShowSize()
        {
            // The KEY COUNT comes back alongside the byte total on purpose. "0 bytes" is only
            // "already downloaded" if the set resolved to something; on 2026-08-19 it resolved
            // to NOTHING and this screen said "already downloaded" to every player. The panel
            // does not get to make that call by eye - OfflineContentService.ClassifySize does.
            long sizeBytes = -1; int sizeKeys = 0;
            yield return OfflineContentService.GetDownloadSize((bytes, keyCount) =>
            {
                sizeBytes = bytes; sizeKeys = keyCount;

                if (_body == null) return;

                var verdict = OfflineContentService.ClassifySize(keyCount, bytes);

                if (verdict == OfflineSizeVerdict.CannotMeasure)
                {
                    // UNKNOWN, which is NOT the same as zero. The service returns -1 when it could
                    // not work out what to download at all, and stamping someone offline-ready on
                    // that is precisely the silent false promise that shipped on 2026-08-19 (the
                    // content set was keyed by GROUP name, matched nothing, and every player was
                    // told "already downloaded"). Say so honestly and record NOTHING.
                    FlowTrace.Fail("OfflineContent",
                        $"size UNKNOWN (bytes={bytes}, keys={keyCount}) - NOT marking offline-ready. Telling " +
                        "the player they are covered when we could not even measure the set is how they " +
                        "find out on a plane.");
                    _body.text = new LocalizedText("offline.optIn.size_unknown").Resolve();
                    _progress.text = "";
                    return;
                }

                if (verdict == OfflineSizeVerdict.AlreadyCached)
                {
                    // Nothing left to fetch, and we know that is genuine because the set resolved
                    // to real keys. The offline-ready stamp is STILL taken through the pull path
                    // (below) rather than set here: the service owns that stamp and only writes it
                    // after a measured verification. A screen that could stamp on its own reading
                    // is exactly the door the 2026-08-19 defect walked through.
                    FlowTrace.Step("OfflineContent",
                        $"size = 0 across {keyCount} key(s) - already fully cached; running the verified " +
                        "pull path so the offline-ready stamp comes from evidence, not from this label.");
                    _body.text = new LocalizedText("offline.optIn.already_cached").Resolve();
                    _progress.text = "";
                    return;
                }

                float mb = bytes / (1024f * 1024f);
                // State the minutes, not just the megabytes: megabytes mean nothing to most
                // people and the whole point is that they are not surprised by the wait.
                string estimate = EstimateText(bytes);
                // The megabyte figure is formatted HERE, in C#, and handed over as a string: the
                // JSON fallback formatter and the Unity Smart-String formatter do not have to agree
                // on ":F0" for the number to come out right (WO-1857).
                string mbText = mb.ToString("F0", System.Globalization.CultureInfo.CurrentCulture);
                _body.text =
                    new LocalizedText("offline.optIn.prompt").Resolve() + "\n\n" +
                    LocalText.Format("offline.optIn.size_detail", mbText, estimate) + "\n\n" +
                    new LocalizedText("offline.optIn.wifi_note").Resolve();
            });

            // ALREADY CACHED -> take the offline-ready stamp through the SAME verified path a
            // real download takes. With 0 bytes outstanding this costs one more size query and
            // no network traffic, and it means there is exactly ONE place in the codebase that
            // can mark a player offline-ready: the branch that has just measured the evidence.
            if (OfflineContentService.ClassifySize(sizeKeys, sizeBytes) == OfflineSizeVerdict.AlreadyCached
                && !OfflineContentService.PulledForThisBuild)
            {
                yield return OfflineContentService.DownloadAllForOffline(
                    (pct, doneBytes, totalBytes) => { },
                    (ok, reason) => FlowTrace.Step("OfflineContent",
                        $"already-cached stamp path finished ok={ok} ({reason})"));
            }
        }

        /// <summary>Honest range, both ends. One number would read as a promise.</summary>
        private static string EstimateText(long bytes)
        {
            float bits = bytes * 8f;
            int fast = Mathf.RoundToInt(bits / 5_000_000f);    // 5 Mbps
            int slow = Mathf.RoundToInt(bits / 1_500_000f);    // 1.5 Mbps
            string F(int s) => s >= 90
                ? LocalText.Format("offline.optIn.minutes", Mathf.RoundToInt(s / 60f))
                : LocalText.Format("offline.optIn.seconds", s);
            return LocalText.Format("offline.optIn.estimate", F(fast), F(slow));
        }

        private void OnDownload()
        {
            if (_downloading) return;
            _downloading = true;
            _body.text = new LocalizedText("offline.optIn.downloading").Resolve();
            StartCoroutine(RunDownload());
        }

        private IEnumerator RunDownload()
        {
            yield return OfflineContentService.DownloadAllForOffline(
                // MEASURED BYTES, shown as well as the percent. Two reasons, both about trust:
                // a percent alone gives the player no way to tell a slow download from a stuck
                // one, and the megabyte figure is the promise made on the previous screen, so
                // watching it climb toward the stated total is the receipt for that promise.
                // The fraction is byte-weighted inside the service (GetDownloadStatus), so with
                // the per-family / per-asset re-pack it advances continuously across many small
                // bundles instead of stepping once per key.
                (pct, doneBytes, totalBytes) =>
                {
                    if (_progress == null) return;
                    float doneMb  = doneBytes  / (1024f * 1024f);
                    float totalMb = totalBytes / (1024f * 1024f);
                    _progress.text = totalBytes > 0
                        ? $"{Mathf.RoundToInt(pct * 100f)}%   {doneMb:F0} of {totalMb:F0} MB"
                        : $"{Mathf.RoundToInt(pct * 100f)}%";
                },
                (ok, reason) =>
                {
                    _downloading = false;
                    if (_body == null) return;
                    // The message comes FROM THE SERVICE, which is the only thing that knows
                    // whether bytes actually landed. The panel used to author both outcomes
                    // itself from a bare bool - and a bool that was true whenever the handles
                    // finished, downloaded or not, is how "Everything is already downloaded"
                    // got shown to players who had downloaded nothing.
                    _body.text = reason;
                    _progress.text = "";
                    if (!ok) FlowTrace.Warn("OfflineContent", $"offline pull reported FAILURE to the player: {reason}");
                });
        }

        private void DeclineAndClose()
        {
            if (_downloading) return;   // do not let a stray tap abandon a live download
            OfflineContentService.SetOptedIn(false);
            Close();
        }

        private void Close()
        {
            PanelManager.NotifyClosed(_panelHandle);   // no-op if the arbiter already swapped us out
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
            if (s_active == this) s_active = null;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (s_active == this) s_active = null;
        }
    }
}
