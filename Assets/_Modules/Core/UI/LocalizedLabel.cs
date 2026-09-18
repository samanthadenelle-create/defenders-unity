// WO-1866 Part A — the durable half of the coverage-gap fix. LocalText.Changed is the ONLY push
// signal any already-built widget has to re-pull text after a runtime locale switch
// (LocalizedText.Resolve() / LocalText.Get() are pull-only and nothing re-invokes them on their
// own). Before this file, exactly two call sites hand-wired that subscription
// (SettingsController.OnLocalizedTextChanged, HudKitController.RefreshLocalizedHudCopy); every
// other one-shot-resolved label had no subscriber and was architecturally unable to update after
// a switch. This component is the reusable subscriber so the NEXT hardcoded-string-turned-key
// does not reintroduce the same bug: attach it once to a persistent TMP_Text and it keeps
// retexting itself for the rest of its lifetime, mirroring the OnEnable/OnDisable pattern already
// used by SettingsController and HudKitController.
using TMPro;
using UnityEngine;

namespace DeNelle.Core.UI
{
    /// <summary>A self-updating localized label. Attach to any persistent, already-built TMP_Text
    /// via <see cref="Attach"/> so it keeps retexting itself on every future locale switch.</summary>
    public sealed class LocalizedLabel : MonoBehaviour
    {
        private TMP_Text _label;
        private string _key;

        /// <summary>Attaches (or reconfigures, if one is already present) a LocalizedLabel on the
        /// label's own GameObject, resolves it immediately, and leaves it subscribed to
        /// <see cref="LocalText.Changed"/> for as long as the label exists.</summary>
        public static LocalizedLabel Attach(TMP_Text label, string key)
        {
            if (label == null || string.IsNullOrEmpty(key))
                return null;

            var owner = label.GetComponent<LocalizedLabel>();
            if (owner == null)
                owner = label.gameObject.AddComponent<LocalizedLabel>();

            owner._label = label;
            owner._key = key;
            owner.Refresh();
            return owner;
        }

        private void OnEnable()
        {
            LocalText.Changed -= Refresh;
            LocalText.Changed += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            LocalText.Changed -= Refresh;
        }

        private void Refresh()
        {
            if (_label == null || string.IsNullOrEmpty(_key))
                return;
            _label.text = LocalText.Get(_key);
        }
    }
}
