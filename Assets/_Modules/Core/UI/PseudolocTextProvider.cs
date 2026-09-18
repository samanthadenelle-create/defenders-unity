#if UNITY_EDITOR || QA_SCENARIO_BUILD
// =============================================================================
// PseudolocTextProvider — WO-1861 Part A. The LEAK DETECTOR'S other half.
// -----------------------------------------------------------------------------
// ⛔ THE WHOLE FILE IS QUARANTINED. Its first real line is the `#if` above and its
// last line is the matching `#endif`; PseudolocHarnessRegression [pseudoloc-harness]
// check 1 FAILS if either stops being true. DeNelle.Core ships, so an unguarded
// class here would be reachable from a store APK — this file is not.
// QA_SCENARIO_BUILD is WO-1775's dev define (stamped ONLY by
// overnight-apk-build.ps1 -Scenario, never by -Tester, never by AndroidBuild.cs;
// DeviceScenarioKitRegression pins that and this suite pins the same for the
// pseudoloc names). UNITY_EDITOR covers the headless capture harness.
//
// WHAT IT IS FOR
// -----------------------------------------------------------------------------
// WO-1857 is moving every remaining hardcoded player-facing string onto a locale
// key. The cheap way to find what is LEFT is to make localized copy visually
// impossible to mistake for English: pseudolocalize it. Anything still reading as
// English afterwards either (a) never went through LocalText at all — a leak — or
// (b) is legitimately Latin in every language (a brand, a player name, an invite
// code) and belongs in docs/localization/PSEUDOLOC_ALLOWLIST.md.
//
// WHY NOT AN 11th LOCALE: see the ticket. Every RequiredLocales/EnabledLocales
// regression hardcodes the locale set; a synthetic locale trips all of them for no
// benefit. Pseudoloc rides the EXISTING provider seam, so from every other system's
// point of view the active locale never changed. Zero locale JSON touched, zero
// Unity String Table assets touched.
//
// =============================================================================
//  TWO HALVES, AND YOU NEED BOTH. READ THIS BEFORE "SIMPLIFYING" EITHER AWAY.
// -----------------------------------------------------------------------------
//  (1) THE DECORATOR (this class, as an ILocalTextProvider). Wraps whatever real
//      provider LocalizationBootstrap installed and transforms what it returns.
//      This is the seam the ticket names, and it is the RUNTIME/device path.
//
//  (2) THE POST-RESOLVE HOOK (LocalText.PseudolocHook). Necessary, not redundant,
//      and here is the PROOF rather than an assertion:
//        * LocalizationBootstrap.Install is [RuntimeInitializeOnLoadMethod(
//          AfterAssembliesLoaded)] (LocalizationBootstrap.cs:22) — it does NOT run
//          in edit mode.
//        * The only other InstallProvider callers are LocalText itself and
//          LocaleSmokeCapture, which installs in CaptureLocale and puts it back to
//          null in its own finally (LocaleSmokeCapture.cs:111).
//        * So during UICaptureLaunch's edit-mode sweep — WO-1860's 267-PNG catalog,
//          the exact thing this detector runs against — LocalText._provider is NULL
//          and every string comes from the Data/Canonical/en.json fallback inside
//          LocalText.TryGet. A decorator over a null provider transforms nothing,
//          and the whole capture would come out in plain English.
//      The hook sits at TryGet's table-resolved exits, so it covers the provider
//      path AND the JSON fallback path with one seam.
//
//  DOUBLE APPLICATION IS A PROVEN NO-OP, which is why the two can coexist without
//  an ordering rule: Transform's output contains no character in [A-Za-z] (every
//  Latin letter is mapped out, and nothing maps IN), so a second pass finds nothing
//  to change. The transform is idempotent by construction, and
//  [pseudoloc-harness] check 4 asserts it rather than trusting this paragraph.
//
// =============================================================================
//  WHY CYRILLIC, AND WHY *THESE* CYRILLIC LETTERS (measured, not chosen by taste)
// -----------------------------------------------------------------------------
//  Cyrillic is unmistakable from English to a human and to a vision model, and `ru`
//  is one of the six locales with a live Unity String Table whose font-glyph
//  coverage was already fixed this session — so the transform cannot be the thing
//  that produces tofu.
//
//  ⚠ TWO TRAPS, BOTH CLOSED BY MEASUREMENT.
//
//  TRAP 1 — HOMOGLYPHS DEFEAT THE WHOLE POINT. Cyrillic а е о р с х у А В Е К М Н
//  О Р С Т Х are visually IDENTICAL to Latin letters. A map built from those turns
//  "coop" into "соор", which no reviewer and no vision model can tell from English
//  — the detector would report a transformed screen that still LOOKS untransformed,
//  and every real leak beside it would be dismissed as more of the same. So: all
//  six English vowels (a e i o u) plus the 12 most frequent consonants are mapped
//  to letters with NO Latin look-alike (д э й ю ч и з ь я г л ц щ ж ы б ш). Only
//  the rarest letters (y p b v k j x q z) fall back to look-alikes, and an English
//  word containing none of the 17 distinct targets is a curiosity, not a case.
//
//  TRAP 2 — A LETTER CAN BE "CYRILLIC" AND STILL NOT BE IN THE ATLAS. Claiming the
//  ru font covers a character because the character is Cyrillic is a guess. So the
//  target set was measured against the live table instead:
//    Assets/Resources/Data/Canonical/ru.json contains 62 distinct Cyrillic code
//    points (read 2026-09-17) — U+0410..U+042F upper EXCEPT U+0424 (Ф) and U+042A
//    (Ъ), U+0430..U+044F lower EXCEPT U+044A (ъ), plus U+0451 (ё).
//  Every letter in the map below appears in that file in BOTH cases. Ф/ф, Ъ/ъ and ё
//  are deliberately UNUSED: their missing case is exactly the unproven glyph a
//  reviewer would then have to argue about. Do not add one.
//
//  DIGITS, PUNCTUATION, RICH TEXT AND FORMAT PLACEHOLDERS ARE PRESERVED VERBATIM.
//  Breaking a `{0}` or a `<color=#RRGGBB>` would cascade into formatting and markup
//  bugs that prove nothing about localization coverage — and a mangled tag would
//  render as literal garbage on every screen, drowning the very signal this exists
//  to surface.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DeNelle.Core.UI
{
    /// <summary>
    /// WO-1861: pseudolocalization decorator over the one real <see cref="ILocalTextProvider"/>,
    /// plus the static install/transform authority the editor capture harness drives.
    /// Dev-only — the whole file is compiled out of any build that is neither the editor nor
    /// a QA_SCENARIO_BUILD.
    /// </summary>
    public sealed class PseudolocTextProvider : ILocalTextProvider
    {
        /// <summary>PlayerPrefs flag, following FeatureFlags.Get's convention exactly
        /// (`ff.&lt;name&gt;`, 1 = on, 0 = off, absent = the default, which here is OFF).
        /// It is declared HERE rather than in FeatureFlags.cs on purpose: FeatureFlags is
        /// unguarded shipping code, and a property there would be a reachable door into a
        /// dev-only transform. FeatureFlags.cs carries a breadcrumb comment pointing here.
        /// DevScenarioIntent.ApplyFlag already writes this key from the launch extra
        /// `dotr.ff.pseudoloc=1` with no change to that file's parser.</summary>
        public const string PrefKey = "ff.pseudoloc";

        private const string Tag = "Pseudoloc";

        private readonly ILocalTextProvider _inner;

        // ---------------------------------------------------------------------
        //  THE LETTER MAP. 26 distinct targets, every one present in ru.json in
        //  both cases (see the banner). Ordered a..z; uppercase is derived with
        //  ToUpperInvariant so the two cases can never drift apart.
        // ---------------------------------------------------------------------
        //  ⛔ WRITTEN AS \uXXXX ESCAPES, NOT AS RAW CYRILLIC, DELIBERATELY. Every .cs in
        //  this repo is UTF-8 with NO BOM (verified 2026-09-17 across UICaptureLaunch.cs,
        //  DevScenarioIntent.cs and DeviceScenarioKitRegression.cs). In those files the
        //  non-ASCII lives only in COMMENTS, where a mis-decode is cosmetic. This is a
        //  LOAD-BEARING literal: a toolchain that read the file as the system codepage
        //  would silently substitute different glyphs, the transform would still "work",
        //  and the wrongness would only ever show up as tofu in a screenshot nobody could
        //  explain. Pure-ASCII source cannot have that failure at all.
        //  a=U+0434 b=U+0432 c=U+0446 d=U+0433 e=U+044D f=U+044B g=U+0431 h=U+044C
        //  i=U+0439 j=U+0435 k=U+043A l=U+043B m=U+0449 n=U+0438 o=U+044E p=U+043F
        //  q=U+043E r=U+044F s=U+0437 t=U+0448 u=U+0447 v=U+0445 w=U+0436 x=U+043C
        //  y=U+0443 z=U+0441
        private const string LowerLatin = "abcdefghijklmnopqrstuvwxyz";
        private const string LowerCyrillic =
            "двцгэыбьйеклщ" +
            "июпоязшчхжмус";

        private static readonly Dictionary<char, char> Map = BuildMap();

        /// <summary>True while a pseudoloc transform is installed. The oracle refuses to
        /// judge anything when this is false — a leak scan over untransformed English would
        /// flag every single label, which reads as catastrophe and means nothing.</summary>
        public static bool Active { get; private set; }

        private static bool _announced;

        public PseudolocTextProvider(ILocalTextProvider inner)
        {
            _inner = inner;
        }

        // =====================================================================
        //  THE TRANSFORM
        // =====================================================================
        /// <summary>
        /// Latin letters to Cyrillic; digits, punctuation, whitespace, <c>&lt;rich text&gt;</c>
        /// spans and <c>{placeholder}</c> spans copied through byte-for-byte. Idempotent: the
        /// result contains no <c>[A-Za-z]</c>, so applying it twice changes nothing.
        /// </summary>
        public static string Transform(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var sb = new StringBuilder(value.Length + 8);
            int i = 0;
            while (i < value.Length)
            {
                char c = value[i];

                // ---- rich text: <b>, </color>, <sprite name="coin">, <size=120%> -------
                // Copied WHOLE, tag name and attributes alike. A transformed tag name is not
                // a tag any more; it renders as literal angle-bracket garbage on every label
                // that has one, which buries the signal instead of producing it.
                if (c == '<')
                {
                    int close = value.IndexOf('>', i + 1);
                    if (close > i)
                    {
                        sb.Append(value, i, close - i + 1);
                        i = close + 1;
                        continue;
                    }
                    // An unmatched '<' is just a character. Fall through and map nothing --
                    // never swallow the rest of the string on malformed markup.
                }

                // ---- format placeholders: {0}, {Minimum}, {resource:N0} ------------------
                // LocalText.FormatFallback runs string.Format / FormatNamedArguments over the
                // pattern, and FormatNamedArguments matches "{" + property.Name + "}"
                // literally (LocalText.cs:242). Transforming the NAME breaks the substitution
                // silently -- the placeholder survives into the UI and reads as a new defect.
                if (c == '{')
                {
                    if (i + 1 < value.Length && value[i + 1] == '{')   // "{{" escape
                    {
                        sb.Append("{{");
                        i += 2;
                        continue;
                    }
                    int close = value.IndexOf('}', i + 1);
                    if (close > i)
                    {
                        sb.Append(value, i, close - i + 1);
                        i = close + 1;
                        continue;
                    }
                }
                if (c == '}' && i + 1 < value.Length && value[i + 1] == '}')   // "}}" escape
                {
                    sb.Append("}}");
                    i += 2;
                    continue;
                }

                sb.Append(MapChar(c));
                i++;
            }
            return sb.ToString();
        }

        /// <summary>One Latin letter to its Cyrillic target; anything else unchanged.</summary>
        public static char MapChar(char c)
        {
            if (c >= 'a' && c <= 'z') return Map[c];
            if (c >= 'A' && c <= 'Z') return Map[c];
            return c;
        }

        private static Dictionary<char, char> BuildMap()
        {
            var map = new Dictionary<char, char>(52);
            for (int i = 0; i < LowerLatin.Length; i++)
            {
                char latin = LowerLatin[i];
                char cyr = LowerCyrillic[i];
                map[latin] = cyr;
                map[char.ToUpperInvariant(latin)] = char.ToUpperInvariant(cyr);
            }
            return map;
        }

        // =====================================================================
        //  INSTALL / UNINSTALL
        // =====================================================================
        /// <summary>
        /// DEVICE / PLAYER path. Installs pseudoloc only when <see cref="PrefKey"/> is 1.
        /// Idempotent and safely re-callable, which matters because of a real ordering trap:
        /// LocalizationBootstrap.Install runs at AfterAssembliesLoaded while
        /// DevScenarioIntent (which is what WRITES ff.pseudoloc from a launch extra) runs at
        /// AfterSceneLoad. On the FIRST launch that passes dotr.ff.pseudoloc=1 the pref does
        /// not exist yet when the bootstrap asks, so DevScenarioIntent calls this again right
        /// after its ff loop and pseudoloc takes effect on that same launch instead of the
        /// next one.
        /// </summary>
        /// <param name="inner">The real provider, when the caller has it in hand
        /// (LocalizationBootstrap does). Null is fine — the post-resolve hook covers every
        /// path on its own; the decorator is additionally wrapped when an inner is supplied.</param>
        /// <returns>True when pseudoloc is active after this call.</returns>
        public static bool InstallIfEnabled(ILocalTextProvider inner = null)
        {
            if (PlayerPrefs.GetInt(PrefKey, 0) != 1) return false;
            InstallCore(inner);
            return true;
        }

        /// <summary>
        /// EDITOR / HARNESS path. Turns pseudoloc on for this process regardless of the pref.
        /// Deliberately NOT driven by PlayerPrefs in the editor: a stale ff.pseudoloc=1 in the
        /// editor registry would pseudolocalize the owner's own play-mode UI and every later
        /// capture run for the rest of the night, and nothing would say why. Always pair it
        /// with <see cref="ForceOff"/> in a finally.
        /// </summary>
        public static void ForceOn()
        {
            InstallCore(null);
        }

        private static void InstallCore(ILocalTextProvider inner)
        {
            LocalText.PseudolocHook = Transform;

            if (inner != null && !(inner is PseudolocTextProvider))
                LocalText.InstallProvider(new PseudolocTextProvider(inner));

            Active = true;
            if (!_announced)
            {
                _announced = true;
                // Warn, not Step. A pseudolocalized session must never be readable as a
                // normal one in a log a later seat cites -- the same reason
                // FeatureFlags.RaidTestBypassArmyGate warns on every bypass.
                DeNelle.Core.Diagnostics.FlowTrace.Warn(Tag,
                    "[Flow:Pseudoloc] PSEUDOLOCALIZATION IS ACTIVE. Every string resolved through " +
                    "LocalText is rewritten to Cyrillic. This session's screenshots, copy and any " +
                    "text assertion are NOT representative of a shipping build. Anything still " +
                    "reading as English is either a localization LEAK (WO-1857) or an entry in " +
                    "docs/localization/PSEUDOLOC_ALLOWLIST.md.");
            }
        }

        /// <summary>Removes the transform. Restores the plain provider chain when this class
        /// had wrapped one.</summary>
        public static void ForceOff()
        {
            LocalText.PseudolocHook = null;
            Active = false;
            _announced = false;
        }

        // =====================================================================
        //  ILocalTextProvider — the decorator half. Every member DELEGATES; only
        //  TryResolve's returned VALUE is touched. A decorator that answered
        //  IsReady / CurrentLocaleCode / AvailableLocales itself would change what
        //  the language selector shows and what LocalText.LanguageCode reports,
        //  and the locale is supposed to be untouched by pseudoloc.
        // =====================================================================
        public bool IsReady => _inner != null && _inner.IsReady;
        public bool UsesSystemLocale => _inner == null || _inner.UsesSystemLocale;
        public string CurrentLocaleCode => _inner != null ? _inner.CurrentLocaleCode : null;

        public IReadOnlyList<LocaleOption> AvailableLocales =>
            _inner != null ? _inner.AvailableLocales : Array.Empty<LocaleOption>();

        /// <summary>LocalText.InstallProvider subscribes to Changed (LocalText.cs:90). Forward
        /// the inner's event or a locale switch would stop repainting live UI.</summary>
        public event Action Changed
        {
            add { if (_inner != null) _inner.Changed += value; }
            remove { if (_inner != null) _inner.Changed -= value; }
        }

        public bool TryResolve(string key, object[] args, out string value)
        {
            value = null;
            if (_inner == null || !_inner.TryResolve(key, args, out value)) return false;
            value = Transform(value);
            return true;
        }

        public bool TrySelectLocale(string code) => _inner != null && _inner.TrySelectLocale(code);

        public void UseSystemLocale()
        {
            _inner?.UseSystemLocale();
        }
    }
}
#endif
