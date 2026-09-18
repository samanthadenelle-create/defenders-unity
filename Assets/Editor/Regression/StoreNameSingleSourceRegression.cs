// =============================================================================
// StoreNameSingleSourceRegression [store-name-single-source] (WO-1398) - the store
// has ONE player-facing name and every face that opens it renders that name.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression
// Markers:  STORE_NAME_SINGLE_SOURCE_OK / STORE_NAME_SINGLE_SOURCE_FAIL
//
// WHAT WAS FOUND (docs/qa/UI_SCREEN_GRAPH_2026-09-04.md:116,130,249 - dead end 7):
//   * the HUD card read "Night Market" (a literal) and opened PanelId.RealmStore;
//   * the gear-dock row read "Night Market" (a literal) and opened PanelId.RealmDeck
//     through a method named OpenRealmStore;
//   * the Realm deck's first card read "Realm Store" (a literal) and opened RealmStore;
//   * the Play skin titled itself "REALM STORE" (a literal);
//   * the store itself titled itself from canon-strings.json `storeWordmark`.
// One name for two screens, and two names for one screen, because the name was typed
// at every call site instead of read from the one row that owns it.
//
// THE FIX SHAPE THIS SUITE PINS: the NAME is canon-strings.json `storeWordmark`, read by
// HudStrings.KeyStoreWordmark (Core, for the HUD / deck / Play faces) and
// StoreStrings.KeyWordmark (Wallet, for the store panel). Two readers, ONE key, ONE file.
//
//   A  SOURCE SCAN: no .cs under Assets/_Modules carries the string LITERAL
//      "Night Market", "The Night Market", "NIGHT MARKET", "Realm Store" or
//      "REALM STORE" (comments stripped first; a comment may tell the history).
//   B  FACE SITES: the four faces that name the store read the canon key -
//      HudKitController.BuildNightMarketCard, PlayerDeckWorkspace's RealmStore route,
//      GooglePlayStorefront's modal title, PackStore's modal title.
//   C  ONE KEY: HudStrings.KeyStoreWordmark and StoreStrings.KeyWordmark are the SAME
//      key name; both canonical copies hold it, identical, non-empty, ASCII; and the two
//      runtime readers resolve it to the same words.
//   D  THE DOCK ROW says what it opens: "Realm" -> OpenRealmDeck; no OpenRealmStore
//      method survives in HudKitController (its body opened the deck).
//
// RED-FIRST MUTATION (verified by construction against the pre-fix tree): restore
// `BuildObsidianButton(root.transform, "Night Market",` in HudKitController and BOTH A
// (literal found) and B (hud-card site does not read the key) fail; restore
// `Route("Realm Store", ...` in PlayerDeckWorkspace and A + B fail again.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DeNelle.Core.UI;
using DeNelle.Wallet;

namespace DeNelle.Editor.Regression
{
    public static class StoreNameSingleSourceRegression
    {
        private const string Tag = "[store-name-single-source]";
        private const string ModulesRoot = "Assets/_Modules";
        private const string HudSrc = "Assets/_Modules/HUD/Kit/HudKitController.cs";
        private const string DeckSrc = "Assets/_Modules/HUD/PlayerDeckWorkspace.cs";
        private const string PlaySrc = "Assets/_Modules/GooglePlay/GooglePlayStorefront.cs";
        private const string StoreSrc = "Assets/_Modules/Wallet/PackStore.cs";
        private const string CanonRes = "Assets/Resources/Data/Canonical/canon-strings.json";
        private const string CanonStr = "Assets/StreamingAssets/Data/Canonical/canon-strings.json";

        /// <summary>The literals that used to be typed at the faces. Matched as a WHOLE quoted
        /// string, so "Night Market card" (a diagnostic band/face name) is not a hit.</summary>
        private static readonly string[] ForbiddenLiterals =
        {
            "\"Night Market\"", "\"The Night Market\"", "\"NIGHT MARKET\"",
            "\"Realm Store\"", "\"REALM STORE\"",
        };

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("STORE_NAME_SINGLE_SOURCE_OK - " + reason);
            else Debug.LogError("STORE_NAME_SINGLE_SOURCE_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                CaseA_NoLiteralInModules(failures, notes);
                CaseB_FaceSitesReadTheKey(failures, notes);
                CaseC_OneKeyOneValue(failures, notes);
                CaseD_DockRowSaysWhatItOpens(failures, notes);
            }
            catch (Exception ex)
            {
                failures.Add(Tag + " THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes.ToArray()) + "]" : "";
            if (failures.Count == 0)
            {
                reason = "STORE NAME SINGLE SOURCE OK - no store-name literal in module code, the four " +
                         "store faces read canon storeWordmark through one key, both canonical copies " +
                         "agree, and the dock row says what it opens" + noteStr;
                return true;
            }
            reason = "store-name-single-source FAIL x" + failures.Count + ": " +
                     string.Join(" | ", failures.ToArray()) + noteStr;
            return false;
        }

        // =====================================================================
        //  A - no store-name literal survives in module code (comments excepted)
        // =====================================================================
        private static void CaseA_NoLiteralInModules(List<string> failures, List<string> notes)
        {
            if (!Directory.Exists(ModulesRoot)) { failures.Add(Tag + " " + ModulesRoot + " is not a directory"); return; }
            string[] files = Directory.GetFiles(ModulesRoot, "*.cs", SearchOption.AllDirectories);
            int hits = 0;
            foreach (string file in files)
            {
                string code = StripComments(File.ReadAllText(file));
                foreach (string literal in ForbiddenLiterals)
                {
                    int at = code.IndexOf(literal, StringComparison.Ordinal);
                    if (at < 0) continue;
                    hits++;
                    int line = 1;
                    for (int i = 0; i < at; i++) if (code[i] == '\n') line++;
                    failures.Add(Tag + " " + file.Replace('\\', '/') + ":" + line + " carries the literal " + literal +
                                 " - the store's name is canon-strings storeWordmark (HudStrings.KeyStoreWordmark / " +
                                 "StoreStrings.KeyWordmark), never typed at a call site (WO-1398)");
                }
            }
            notes.Add(files.Length + " module .cs files scanned, " + hits + " store-name literal hit(s)");
        }

        // =====================================================================
        //  B - the four faces that name the store read the key
        // =====================================================================
        private static void CaseB_FaceSitesReadTheKey(List<string> failures, List<string> notes)
        {
            string hud = ReadSrc(HudSrc, failures);
            if (hud != null)
            {
                string card = Between(hud, "private void BuildNightMarketCard(", "private void OpenNightMarket(");
                if (card == null)
                    failures.Add(Tag + " BuildNightMarketCard..OpenNightMarket slice not found in " + HudSrc);
                else if (card.IndexOf("HudStrings.StoreFaceLabel(\"hud-card\")", StringComparison.Ordinal) < 0)
                    failures.Add(Tag + " the HUD card does not read HudStrings.StoreFaceLabel(\"hud-card\") - its label " +
                                 "is not the canon storeWordmark");
            }

            string deck = ReadSrc(DeckSrc, failures);
            if (deck != null)
            {
                bool found = false;
                foreach (string raw in deck.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (line.IndexOf("Route(", StringComparison.Ordinal) < 0) continue;
                    if (line.IndexOf("PanelId.RealmStore", StringComparison.Ordinal) < 0) continue;
                    found = true;
                    if (line.IndexOf("HudStrings.StoreFaceLabel(\"realm-deck\")", StringComparison.Ordinal) < 0)
                        failures.Add(Tag + " the Realm deck's store route does not read HudStrings.StoreFaceLabel" +
                                     "(\"realm-deck\"): '" + line + "'");
                }
                if (!found) failures.Add(Tag + " no Route(...PanelId.RealmStore...) in " + DeckSrc);
            }

            string play = ReadSrc(PlaySrc, failures);
            if (play != null && play.IndexOf("HudStrings.StoreFaceLabel(\"play-skin\")", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " the Play skin's modal title does not read HudStrings.StoreFaceLabel(\"play-skin\")");

            string store = ReadSrc(StoreSrc, failures);
            if (store != null && store.IndexOf("BuildObsidianModal(\"PackStoreUI\", StoreStrings.Get(StoreStrings.KeyWordmark)",
                                                 StringComparison.Ordinal) < 0)
                failures.Add(Tag + " PackStore's modal title no longer reads StoreStrings.Get(StoreStrings.KeyWordmark)");
        }

        // =====================================================================
        //  C - one key, one value, in both canonical copies, through both readers
        // =====================================================================
        private static void CaseC_OneKeyOneValue(List<string> failures, List<string> notes)
        {
            if (!string.Equals(HudStrings.KeyStoreWordmark, StoreStrings.KeyWordmark, StringComparison.Ordinal))
                failures.Add(Tag + " HudStrings.KeyStoreWordmark '" + HudStrings.KeyStoreWordmark + "' != StoreStrings." +
                             "KeyWordmark '" + StoreStrings.KeyWordmark + "' - two keys is two names again");
            if (!string.Equals(HudStrings.KeyStoreWordmark, "storeWordmark", StringComparison.Ordinal))
                failures.Add(Tag + " the shared key is no longer 'storeWordmark' - canon-strings.json names it so");

            string res = ReadCanonKey(CanonRes, HudStrings.KeyStoreWordmark);
            string str = ReadCanonKey(CanonStr, HudStrings.KeyStoreWordmark);
            if (string.IsNullOrEmpty(res)) failures.Add(Tag + " " + CanonRes + " has no non-empty '" + HudStrings.KeyStoreWordmark + "'");
            if (string.IsNullOrEmpty(str)) failures.Add(Tag + " " + CanonStr + " has no non-empty '" + HudStrings.KeyStoreWordmark + "'");
            if (res != null && str != null && !string.Equals(res, str, StringComparison.Ordinal))
                failures.Add(Tag + " storeWordmark DIFFERS between the copies: '" + res + "' vs '" + str + "'");
            if (res != null)
                foreach (char c in res)
                    if (c > 127) { failures.Add(Tag + " storeWordmark carries non-ASCII U+" + ((int)c).ToString("X4") + " - TMP renders tofu"); break; }

            string viaHud = HudStrings.Get(HudStrings.KeyStoreWordmark);
            string viaStore = StoreStrings.Get(StoreStrings.KeyWordmark);
            bool hudMissing = viaHud.IndexOf("[[missing:", StringComparison.Ordinal) >= 0;
            bool storeMissing = viaStore.IndexOf("[[missing:", StringComparison.Ordinal) >= 0;
            if (hudMissing || storeMissing)
                notes.Add("runtime loader did not resolve storeWordmark headlessly (hud=" + viaHud + ", store=" + viaStore +
                          ") - the authored copies were compared instead");
            else if (!string.Equals(viaHud, viaStore, StringComparison.Ordinal))
                failures.Add(Tag + " HudStrings resolves '" + viaHud + "' but StoreStrings resolves '" + viaStore +
                             "' - the HUD card and the store title would disagree");
            else
                notes.Add("both readers resolve storeWordmark to '" + viaHud + "'");
        }

        // =====================================================================
        //  D - the dock row is labelled for what it opens
        // =====================================================================
        private static void CaseD_DockRowSaysWhatItOpens(List<string> failures, List<string> notes)
        {
            string hud = ReadSrc(HudSrc, failures);
            if (hud == null) return;
            string code = StripComments(hud);
            // WO-1857: the label is now a LocalizedText resolve (hud.gearDock.realm), not a bare
            // "Realm" literal - the needle pins the resolved key plus the handler.
            if (code.IndexOf("new LocalizedText(\"hud.gearDock.realm\").Resolve(), OpenRealmDeck)", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " the gear dock has no \"Realm\" -> OpenRealmDeck row - the deck launcher's row " +
                             "must say what it opens, never the store's name");
            if (code.IndexOf("void OpenRealmStore(", StringComparison.Ordinal) >= 0)
                failures.Add(Tag + " HudKitController declares OpenRealmStore again - that name opened PanelId.RealmDeck " +
                             "and lied about its target; the deck opener is OpenRealmDeck");
            // Terminator = the next method after OpenRealmDeck (a brace in a string here would
            // upset the CLAUDE.md section 1 raw brace count).
            string deckOpener = Between(code, "void OpenRealmDeck(", "private void OpenClanChat(");
            if (deckOpener == null)
                failures.Add(Tag + " OpenRealmDeck is not declared in " + HudSrc);
            else if (deckOpener.IndexOf("PanelRouter.Open(PanelId.RealmDeck)", StringComparison.Ordinal) < 0)
                failures.Add(Tag + " OpenRealmDeck does not open PanelId.RealmDeck");
        }

        // -- helpers ------------------------------------------------------------

        private static string ReadSrc(string relPath, List<string> failures)
        {
            if (!File.Exists(relPath)) { failures.Add(Tag + " cannot read " + relPath); return null; }
            return File.ReadAllText(relPath);
        }

        private static string Between(string src, string from, string to)
        {
            int a = src.IndexOf(from, StringComparison.Ordinal);
            if (a < 0) return null;
            int b = src.IndexOf(to, a, StringComparison.Ordinal);
            return b < 0 ? null : src.Substring(a, b - a);
        }

        private static string ReadCanonKey(string relPath, string key)
        {
            try
            {
                if (!File.Exists(relPath)) return null;
                var o = JObject.Parse(File.ReadAllText(relPath));
                var tok = o[key];
                return tok != null && tok.Type == JTokenType.String ? (string)tok : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Source with line and block comments blanked (newlines kept so line numbers
        /// stay true) and string literals KEPT - this suite hunts literals, so it must see them.
        /// A "//" or "/*" inside a string literal is not a comment and is skipped as such.</summary>
        private static string StripComments(string source)
        {
            var sb = new StringBuilder(source.Length);
            int i = 0;
            while (i < source.Length)
            {
                char c = source[i];
                char n = i + 1 < source.Length ? source[i + 1] : '\0';
                if (c == '\'')
                {
                    // A char literal ('"', '\\', '\'') must not open a string or a comment.
                    sb.Append(c); i++;
                    while (i < source.Length && source[i] != '\n')
                    {
                        char s = source[i];
                        if (s == '\\' && i + 1 < source.Length) { sb.Append(s).Append(source[i + 1]); i += 2; continue; }
                        sb.Append(s); i++;
                        if (s == '\'') break;
                    }
                    continue;
                }
                if (c == '"')
                {
                    bool verbatim = i > 0 && source[i - 1] == '@';
                    sb.Append(c); i++;
                    while (i < source.Length)
                    {
                        char s = source[i];
                        if (!verbatim && s == '\\' && i + 1 < source.Length) { sb.Append(s).Append(source[i + 1]); i += 2; continue; }
                        if (s == '"' && verbatim && i + 1 < source.Length && source[i + 1] == '"') { sb.Append("\"\""); i += 2; continue; }
                        sb.Append(s); i++;
                        if (s == '"') break;
                        if (!verbatim && s == '\n') break;
                    }
                    continue;
                }
                if (c == '/' && n == '/')
                {
                    while (i < source.Length && source[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && n == '*')
                {
                    i += 2;
                    while (i < source.Length && !(source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/'))
                    { if (source[i] == '\n') sb.Append('\n'); i++; }
                    i += 2;
                    continue;
                }
                sb.Append(c); i++;
            }
            return sb.ToString();
        }
    }
}
