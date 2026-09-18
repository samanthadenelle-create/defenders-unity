// =============================================================================
// CircleScreenRegression  [circle-screen]  --  WO-1870 Lane C.
// Markers: CIRCLE_SCREEN_OK / CIRCLE_SCREEN_FAIL: <reason>
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.  Shape: public static bool Run(out string
// reason) -- registered into DeNelle.Editor.DataRegression.RunAll with ONE line by
// the LEAD (WO-1870 section 6):
//
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "circle-screen suite", () => { if (!DeNelle.Editor.Regression.CircleScreenRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[circle-screen] " + r); });
//
// NEVER THROWS. Source-lint + JSON-read only: no PlayMode, no scene, no network,
// so it runs inside the headless DataRegression batch.
//
// -----------------------------------------------------------------------------
//  ⛔ THIS SUITE IS **RED-FIRST** AND IS EXPECTED TO BE RED ON ARRIVAL.
// -----------------------------------------------------------------------------
// WO-1870 section 7 ("Sequencing"): Lane C writes this suite FIRST and runs it
// against an EMPTY Assets/_Modules/HUD/Circle/ -- "the failure names every missing
// file, which is the acceptance that the oracle can fail at all". A suite that
// cannot be seen to fail is not an oracle (RegressionMarkerRegression RULE 4).
//
// ⚠ CONSEQUENCE THE LEAD MUST HEAR BEFORE WIRING THE REGISTRATION LINE:
// registering this suite turns DataRegression.RunAll RED until Lane A (VM + source
// + the display-name server seam), Lane B (View + panel + doors) and the LEAD'S OWN
// locale merge (20 catalogs + 2 sidecar files) have all landed. That is deliberate,
// and it is the whole point of writing the oracle before the code. It is NOT a
// defect in this file.
//
// ⛔ IT REFERENCES **NO CIRCLE TYPE**. Not CircleScreenVM, not CircleScreenPanel,
// not PanelId.Circle. Every case reads FILES AS TEXT. That is what lets the suite
// compile and run today, months before Lane A declares a single symbol -- a suite
// that `using`-ed the seam would not compile until the thing it polices exists,
// which is the opposite of RED-first.
//
// -----------------------------------------------------------------------------
//  EVERY CASE RUNS. NOTHING SHORT-CIRCUITS.
// -----------------------------------------------------------------------------
// Failures are COLLECTED and reported together. RaidConfigIdResolveRegression
// returns on the first red because its cases are a chain; these nineteen are
// INDEPENDENT, and the lead needs to watch them go green one lane at a time. A
// first-failure return would report one missing file when eleven are missing.
//
// -----------------------------------------------------------------------------
//  STRIPPER DIRECTION IS DECIDED **PER NEEDLE**, NOT PER CASE
// -----------------------------------------------------------------------------
// WO-1870 section 7 says BANNED patterns run over StripCommentsAndStrings and
// REQUIRED patterns over StripComments. That is the right default and it is what
// RegressionSourceText.cs:25-34 states -- but applied blindly it produces HOLLOW
// PASSES here, because several of this suite's banned needles ARE string content:
//
//   * "/api/" and "BackendBase" ([circle-no-http-in-view]) -- a URL in the View is
//     a string literal. StripCommentsAndStrings blanks it, and the case passes over
//     the exact violation it exists to catch.
//   * a percent-bearing literal ([circle-no-stat-copy]) -- "+15% build speed" is
//     by definition inside quotes.
//   * the CLAN_* / DISPLAY_NAME_* codes ([circle-error-map-total]) -- quoted in the
//     JS that emits them AND quoted in the C# switch that maps them.
//
// So each needle below carries its own Direction, and the case reads the text that
// needle needs. Identifier-shaped needles (GameStateService, FindObjectsByType,
// MinimumStake, percent_staked) use the full stripper, which is what keeps THIS
// FILE'S OWN PROSE -- which names every banned token in order to forbid it -- from
// reading as a violation of itself.
//
// -----------------------------------------------------------------------------
//  WHAT A GREEN HERE WILL NOT PROVE (an unproven thing named as unproven is
//  useful; an unproven thing stated as fact costs someone a day -- CLAUDE.md 11B)
// -----------------------------------------------------------------------------
//  * That the screen LAYS OUT correctly. [circle-touch-floor] is arithmetic on
//    source-parsed anchors against ElarionUiKit.MinTouchPx, the
//    TouchFloorAuthoringRegression.cs:4-11 caveat verbatim. LayoutOracle.Audit on
//    the capture path is the authority on built rects.
//  * That the nine non-English Circle values are GOOD translations. This suite can
//    only prove the OLD group word is gone and the key is present and non-empty.
//  * Anything about the display-name migration. There ISN'T ONE: the lead ruled on
//    2026-09-18 that the Remnant name is the EXISTING player_profiles.username, so
//    section 7's [circle-migration-numbered] case was DELETED rather than left as a
//    shell. test/profile-usernames.test.js asserts the absence of any
//    api/migrations/*display_name*.sql and any api/profile/display-name*.js.
//  * That ClanChatPanel's Circle door sits outside the no-Circle branch. That is a
//    BOUNDED TEXT PROXY, named as a proxy where it is asserted.
//  * That the 7 GameStrings*.asset tables carry the keys. Those are TOOL-GENERATED
//    by LocalizationBuilder.BuildAll() (WO-1870 section 6) and no lane may hand-edit
//    them; LocalizationAuthorityRegression is what proves that ran.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Core.UI;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// WO-1870 Lane C oracle. Nineteen independent source/JSON cases covering the four
    /// owner rulings, the frozen locale key list, the API error map, the signing rail and
    /// the untouched gear dock. DataRegression-shaped; NEVER throws.
    /// </summary>
    public static class CircleScreenRegression
    {
        private const string Tag = "[circle-screen]";

        // ── artefacts, all repo-root relative (CLAUDE.md 0: the root is machine-dependent,
        //    so it is RESOLVED at runtime from Application.dataPath and never hardcoded) ──
        private const string VmRel        = "Assets/_Modules/HUD/Circle/CircleScreenVM.cs";
        private const string SourceRel    = "Assets/_Modules/HUD/Circle/CircleSource.cs";
        private const string WireRel      = "Assets/_Modules/HUD/Circle/CircleWire.cs";
        private const string PanelRel     = "Assets/_Modules/HUD/Circle/CircleScreenPanel.cs";
        private const string BootstrapRel = "Assets/_Modules/HUD/Circle/CircleScreenPanelBootstrap.cs";
        private const string CircleDirRel = "Assets/_Modules/HUD/Circle";
        private const string RouterRel    = "Assets/_Modules/Core/UI/PanelRouter.cs";
        private const string ChatPanelRel = "Assets/_Modules/HUD/ClanChatPanel.cs";
        private const string DockRel      = "Assets/_Modules/HUD/Kit/HudKitController.cs";
        private const string KitRel       = "Assets/_Modules/Core/UI/ElarionUiKit.cs";
        private const string PhrasesStreamRel = "Assets/StreamingAssets/Data/Canonical/chat-phrases.json";
        private const string PhrasesResRel    = "Assets/Resources/Data/Canonical/chat-phrases.json";
        private const string PlayPolicyRel    = "Assets/Editor/Localization/GooglePlayLocalizationVariantPolicy.json";

        /// <summary>LocalizationPolicy.json's supportedLocales, in its own order.</summary>
        private static readonly string[] Locales =
        {
            "en", "es", "pt-BR", "de", "fr", "ru", "ar", "ja", "ko", "zh-Hans",
        };

        /// <summary>The two catalog directories LocaleParityRegression demands parity across.</summary>
        private static readonly string[] CatalogDirs =
        {
            "Assets/StreamingAssets/Data/Canonical",
            "Assets/Resources/Data/Canonical",
        };

        // =====================================================================
        //  RULING 1 -- the group word, per locale, as WO-1859 minted it.
        //
        //  ⛔ WRITTEN AS \uXXXX ESCAPES ON PURPOSE. CLAUDE.md 1's NUL/brace gate and
        //  the repo's mount-sync hazard (CLAUDE.md 0) both bite hardest on non-ASCII
        //  source; an escape survives every encoding this tree has been round-tripped
        //  through. The comment beside each is what the escape spells.
        //
        //  ⚠ RUSSIAN IS MATCHED ON THE **STEM**. The catalog declines it -- the six
        //  values carry Osколка / Osколке / Osколок -- so a full-word needle would
        //  miss five of the six. Same reasoning for Arabic: the definite article is
        //  prefixed in some values and not others, so the bare noun is the needle.
        // =====================================================================
        private static readonly Dictionary<string, string> GroupWord = new Dictionary<string, string>
        {
            { "en",      "Remnant" },
            { "es",      "Remanente" },
            { "pt-BR",   "Remanescente" },
            { "de",      "\u00dcberrest" },                              // Ueberrest
            { "fr",      "Vestige" },
            { "ru",      "\u041e\u0441\u043a\u043e\u043b" },             // Oskol- (stem)
            { "ar",      "\u0628\u0642\u064a\u0629" },                   // baqiyya (bare noun)
            { "ja",      "\u6b8b\u515a" },                               // zanto
            { "ko",      "\uc794\ub2f9" },                               // jandang
            { "zh-Hans", "\u9057\u6c11" },                               // yimin
        };

        /// <summary>The six keys ruling 1 re-points. KEYS DO NOT CHANGE -- only values.</summary>
        private static readonly string[] RenamedKeys =
        {
            "common.remnant_chat",
            "clanChat.noWallet",
            "clanChat.noClan",
            "clanChat.unavailable",
            "clanChat.genericError",
            "clanChat.opening",
        };

        // =====================================================================
        //  THE FROZEN KEY LIST -- WO-1870 section 6, verbatim and in its order.
        //  Lane A references these, Lane B renders them, the lead authors them into
        //  the 20 catalogs. This array and the sidecar's keys[] are the SAME LIST;
        //  they were set-diffed against each other before either was committed.
        // =====================================================================
        private static readonly string[] FrozenKeys =
        {
            // -- the player's own Remnant name (ruling 2) --
            "remnant.name.claim.title", "remnant.name.field", "remnant.name.hint",
            "remnant.name.submit", "remnant.name.change",
            // MINTED 2026-09-18 on evidence, not on the plan: this suite's own
            // [circle-keys-referenced] simulation caught CircleScreenPanel.cs:72,315 rendering a
            // Cancel button for Lane A's CancelEditName() (hand-back A4.2) against a key no
            // catalog carried. LocalizedText resolves a missing key to the KEY ITSELF, so the
            // player would have read the literal "remnant.name.cancel" on a live button.
            "remnant.name.cancel",
            "remnant.name.error.length",
            // THREE KEYS BEYOND THE SECTION-6 FROZEN LIST, added 2026-09-18 after Lane A landed.
            // The lead ruled the Remnant name IS the existing player_profiles.username written by
            // POST /api/profile/username (reuse, never reinvent), and that rail has refusals the
            // plan's display-name route did not: USERNAME_TAKEN, USERNAME_INVALID_CHARS and
            // USERNAME_REJECTED. Collapsing them into circle.error.badRequest was rejected by
            // Lane A for the right reason - "that name is taken" is the COMMON failure on a
            // unique column, and a generic error there is an unexplainable dead end.
            "remnant.name.error.taken", "remnant.name.error.chars", "remnant.name.error.rejected",
            "remnant.name.fallback", "remnant.name.saved",
            // A FOURTH, from the owner's 2026-09-18 ruling: a Remnant's shown name is COMPOSED.
            // The player claims only a FIRST NAME (one token, the existing username rule) and the
            // game appends the Circle -- "Sally of Emberwatch", or just "Sally" with no Circle.
            // The join is a locale key, not a C# concatenation, because the word order is not
            // English's to keep: several of the ten locales put the group first.
            // And its PAIR, from the owner verbatim ("so bob the lonely becomes bob of RiverRun"):
            // a Remnant with no Circle reads as "{0} the Lonely". The two keys are the two halves
            // of one sentence, so neither may be authored without the other.
            "remnant.name.composed", "remnant.name.alone",

            // -- screen chrome --
            "circle.title", "common.circle", "circle.chat.door", "circle.tab.members",
            "circle.tab.leaderboard", "circle.tab.vault", "circle.tab.ballots",
            "circle.tab.unreachable",

            // -- header --
            "circle.header.code", "circle.header.members", "circle.header.role",
            "circle.role.leader", "circle.role.officer", "circle.role.member",
            "circle.policy.invite", "circle.policy.open",

            // -- create / join --
            "circle.create.title", "circle.create.name", "circle.create.tag",
            // ⛔ circle.create.openJoin IS DELIBERATELY ABSENT (lead decision, 2026-09-18).
            // The open-join toggle is OUT of v1: clans.join_policy accepts 'open' and
            // api/clan/create.js:7-10 stores it, but api/clan/join.js STILL REQUIRES A CODE, so the
            // toggle would set a policy that changes nothing. Shipping a control that does not do
            // what it says is worse than not shipping it. The VM keeps CreateOpenJoin /
            // ToggleCreateOpenJoin on the seam (Lane A already built them) -- LANE B SIMPLY DOES
            // NOT RENDER THE TOGGLE, and there is no label for it to render with.
            "circle.create.submit", "circle.create.hint",
            "circle.join.title", "circle.join.code", "circle.join.submit", "circle.join.hint",

            // -- members --
            "circle.members.empty", "circle.member.degraded", "circle.tenure.days",
            "circle.verb.promote", "circle.verb.demote", "circle.verb.kick",
            "circle.verb.leave", "circle.verb.copyCode", "circle.verb.chat",
            "circle.verb.refresh", "circle.leave.confirm", "circle.leave.cancel",

            // -- leaderboard --
            "circle.leaderboard.metric", "circle.leaderboard.members",
            "circle.leaderboard.you", "circle.leaderboard.empty",

            // -- vault --
            "circle.vault.none", "circle.vault.address", "circle.vault.multisig",
            "circle.vault.threshold", "circle.vault.verifiedSigners", "circle.vault.signers",
            "circle.vault.hardware", "circle.vault.hardware.yes", "circle.vault.hardware.no",
            "circle.vault.verifiedAt",
            // ⛔ circle.vault.register / .addressField / .indexField ARE DELIBERATELY ABSENT
            // (owner ruling via the lead, 2026-09-18): the Vault ships READ-ONLY in v1. The read is
            // complete either way -- the vault block rides GET /api/clan/vigil (section 0b) -- and
            // the WRITE is POST /api/clan/vault/register, which can refuse with
            // CLAN_VAULT_SIGNER_NO_SGT *naming a signer wallet in the error body*
            // (api/clan/vault/register.js:21-29), a surface that has never been player-facing and
            // sits under that route's standing copy gate (:130-146). The VM keeps
            // CanRegisterVault / VaultAddressInput / VaultIndexInput / SubmitRegisterVault on the
            // seam -- LANE B SIMPLY DOES NOT RENDER THE FORM.
            // Their error keys (circle.error.vault.*) STAY: a read can still meet a 403 or a 503.

            // -- vigil / ballots --
            "circle.vigil.weight", "circle.vigil.degraded", "circle.ballot.none",
            "circle.ballot.tier", "circle.ballot.locked", "circle.ballot.propose",
            "circle.ballot.vote", "circle.ballot.yourVote", "circle.ballot.voters",
            "circle.ballot.turnout", "circle.ballot.closes", "circle.ballot.closed",
            "circle.ballot.result.passed", "circle.ballot.result.failed",
            // TWO MINTED 2026-09-18 (lead decision) because the SERVER emits three reasons, not two:
            // api/_lib/clan-ballot.js:623-625 declares REASON_PASSED='passed',
            // REASON_NO_VOTES='no_votes', REASON_INSUFFICIENT_TURNOUT='insufficient_turnout',
            // emitted at :642 and :695. Without these, two real outcomes fell through to
            // .unknown -- which reads to the player as "something broke" rather than as the honest
            // result of a quiet week.
            "circle.ballot.result.noVotes", "circle.ballot.result.insufficientTurnout",
            "circle.ballot.result.unknown", "circle.ballot.perks",

            // -- toasts --
            "circle.toast.created", "circle.toast.joined", "circle.toast.left",
            "circle.toast.promoted", "circle.toast.demoted", "circle.toast.kicked",
            "circle.toast.voted", "circle.toast.proposed", "circle.toast.vaultRegistered",
            "circle.toast.codeCopied",

            // -- errors --
            "circle.error.notSignedIn", "circle.error.server", "circle.error.unreachable",
            "circle.error.badRequest", "circle.error.rateLimited", "circle.error.badName",
            "circle.error.badTag", "circle.error.badPolicy", "circle.error.alreadyInCircle",
            "circle.error.codeUnavailable", "circle.error.identityMissing",
            "circle.error.badCode", "circle.error.noSuchCode", "circle.error.notInCircle",
            "circle.error.leaveRaced", "circle.error.badTarget", "circle.error.forbidden",
            "circle.error.targetGone", "circle.error.targetRole", "circle.error.raced",
            "circle.error.ballot.badTier", "circle.error.ballot.badOptions",
            "circle.error.ballot.tierLocked", "circle.error.ballot.alreadyOpen",
            "circle.error.ballot.notFound", "circle.error.ballot.closed",
            "circle.error.ballot.weightUnavailable",
            "circle.error.vault.badAddress", "circle.error.vault.notMultisig",
            "circle.error.vault.badIndex", "circle.error.vault.tooManySigners",
            "circle.error.vault.notLeader", "circle.error.vault.signer",
            "circle.error.vault.already", "circle.error.vault.unreadable",
        };

        // =====================================================================
        //  THE BANNED SET, self-tested before it is trusted.
        //  ManageDumbViewRegression.cs:45-49's stance: a regex nobody has seen match
        //  is not an oracle.
        // =====================================================================

        /// <summary>Which normalised text a needle must be matched against. See the header.</summary>
        private enum Direction
        {
            /// <summary>Identifier-shaped. Full stripper: this file's own prose cannot self-trip.</summary>
            CodeOnly = 0,
            /// <summary>The violation IS string content (a URL, a percent sentence). Comments only.</summary>
            IncludeStrings = 1,
        }

        private sealed class BannedShape
        {
            public string Case;
            public string Name;
            public string Pattern;
            public bool IgnoreCase;
            public Direction Dir;
            /// <summary>A line the pattern MUST match. Drives [circle-self-test].</summary>
            public string Fixture;
            /// <summary>The one edit that turns the owning case RED on demand.</summary>
            public string Revert;
        }

        private static readonly BannedShape[] Banned =
        {
            // ---- [circle-vm-bound]: UiMvvmConformanceRegression.cs:63-71's banned set ----
            new BannedShape
            {
                Case = "[circle-vm-bound]", Name = "service-or-state-in-view",
                Pattern = @"\b(GameStateService|EconomyService\s*\.\s*Instance|FindAnyObjectByType|" +
                          @"FindObjectsByType|ResourceLedger|VillageInventory\s*\.\s*Instance)\b",
                Dir = Direction.CodeOnly,
                Fixture = "var s = GameStateService.Current;",
                Revert = "paste `var s = GameStateService.Current;` into CircleScreenPanel.Render",
            },

            // ---- [circle-no-http-in-view] ----
            // IncludeStrings: a URL and a base-address constant live inside quotes, so the
            // full stripper would blank the violation and hand back a hollow pass.
            new BannedShape
            {
                Case = "[circle-no-http-in-view]", Name = "http-in-view",
                Pattern = @"(UnityWebRequest|BackendRequestSigner|BackendBase|/api/)",
                Dir = Direction.IncludeStrings,
                Fixture = "var req = UnityWebRequest.Get(BackendBase + \"/api/clan/me\");",
                Revert = "paste a UnityWebRequest.Get(...) call into CircleScreenPanel",
            },

            // ---- [circle-no-stat-copy]: RULING 3 ----
            // The magnitude the server ships ("+15% build speed") is string content by
            // construction, so this needle MUST see strings.
            new BannedShape
            {
                Case = "[circle-no-stat-copy]", Name = "percent-bearing-copy",
                Pattern = @"[+-]?\s*\d+(\.\d+)?\s*%",
                Dir = Direction.IncludeStrings,
                Fixture = "label.text = \"+15% build speed\";",
                Revert = "put a percent magnitude in any View string",
            },
            new BannedShape
            {
                Case = "[circle-no-stat-copy]", Name = "effect-binding",
                Pattern = @"\.\s*EffectText\b",
                Dir = Direction.CodeOnly,
                Fixture = "tile.text = row.EffectText;",
                Revert = "bind row.EffectText into an option tile",
            },

            // ---- [circle-join-ungated]: RULING 4 ----
            new BannedShape
            {
                Case = "[circle-join-ungated]", Name = "stake-gate-symbol",
                Pattern = @"\b(MinimumStake|RequiresStake|CanJoinBecauseStaked|MinStakeToJoin)\b",
                Dir = Direction.CodeOnly,
                Fixture = "public bool RequiresStake { get; private set; }",
                Revert = "add a MinimumStake field to CircleScreenVM",
            },
            new BannedShape
            {
                Case = "[circle-join-ungated]", Name = "stake-wire-field-projected",
                Pattern = @"\b(percent_staked|vigil_contribution)\b",
                Dir = Direction.IncludeStrings,
                Fixture = "row.Stake = m.percent_staked;",
                Revert = "project percent_staked onto a MemberRowVM (also breaks Q6's copy gate)",
            },
        };

        // =====================================================================
        //  ENTRY POINT
        // =====================================================================

        /// <summary>
        /// Standalone runner, for a lane that wants this one suite on its own.
        /// The marker is already the PREFIX of <c>reason</c>, so this logs it bare -- the
        /// RaidConfigIdResolveRegression.cs:108,122,131 shape.
        /// </summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log(reason);
            else Debug.LogError(reason);
        }

        /// <summary>DataRegression-shaped contract. NEVER throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();

            try
            {
                // This suite deliberately reads files that do not exist yet and drives
                // refusal paths. Muted for the duration, restored in the finally --
                // never left muted (CLAUDE.md 12: flag off, never strip).
                DeNelle.Core.Diagnostics.FlowTrace.Mute("Regression", "Circle", "Clan", "CanonJson", "HUD");

                CaseSelfTest(failures, notes);
                CaseVmBound(failures, notes);
                CaseNoHttpInView(failures, notes);
                CaseNoLiterals(failures, notes);
                CaseNoStatCopy(failures, notes);
                CaseJoinUngated(failures, notes);
                CaseNameFirst(failures, notes);
                CaseNoGroupRemnant(failures, notes);
                CaseKeysParity(failures, notes);
                CaseKeysReferenced(failures, notes);
                CaseErrorMapTotal(failures, notes);
                CaseRoutesExist(failures, notes);
                CaseSigned(failures, notes);
                CaseInteractiveMint(failures, notes);
                CaseTouchFloor(failures, notes);
                CaseDoor(failures, notes);
                CaseDockUntouched(failures, notes);
                CaseInstrumented(failures, notes);
                // [circle-migration-numbered] is DELETED, not disabled. Section 7 wrote it for a
                // 0038 display-name migration that the lead ruled out of existence on 2026-09-18:
                // the Remnant name IS the existing player_profiles.username, so there is no new
                // column, no new migration and nothing to number. test/profile-usernames.test.js
                // already asserts the ABSENCE of any api/migrations/*display_name*.sql and any
                // api/profile/display-name*.js, so the coverage did not move -- it moved HOUSE.
                // A case kept as a shell that asserts nothing is the hollow pass
                // RegressionMarkerRegression RULE 4 exists to stop. 18 cases, not 19.
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                DeNelle.Core.Diagnostics.FlowTrace.AllOn();
            }

            if (failures.Count > 0)
            {
                reason = "CIRCLE_SCREEN_FAIL: " + failures.Count +
                         " case failure(s) -- WO-1870 is RED-FIRST, so until Lane A," +
                         "Lane B and the lead's locale merge land this is the EXPECTED state and each " +
                         "line below names what is still missing: " + string.Join(" | ", failures);
                return false;
            }

            reason = "CIRCLE_SCREEN_OK WO-1870 circle-screen: 18/18 cases green over " + FrozenKeys.Length +
                     " frozen key(s) x " + (Locales.Length * CatalogDirs.Length) + " catalog file(s); " +
                     string.Join("; ", notes);
            return true;
        }

        // =====================================================================
        //  1 -- [circle-self-test]
        // =====================================================================
        /// <summary>
        /// Every banned regex must match a fixture it is SUPPOSED to match, before any of
        /// them is trusted against real source. ManageDumbViewRegression.cs:45-49.
        /// </summary>
        private static void CaseSelfTest(List<string> failures, List<string> notes)
        {
            const string C = "[circle-self-test]";
            int proven = 0;

            for (int i = 0; i < Banned.Length; i++)
            {
                var b = Banned[i];
                bool hit;
                try
                {
                    hit = Regex.IsMatch(b.Fixture, b.Pattern,
                        b.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
                }
                catch (Exception ex)
                {
                    failures.Add(C + " pattern '" + b.Name + "' (" + b.Case + ") is not a valid regex: " +
                                 ex.GetType().Name + ": " + ex.Message);
                    continue;
                }

                if (!hit)
                {
                    failures.Add(C + " pattern '" + b.Name + "' (" + b.Case + ") did NOT match its own " +
                                 "fixture <" + b.Fixture + ">. It is an UNTESTED ORACLE: it would report " +
                                 "green against the real file while being structurally incapable of " +
                                 "matching anything. Revert recipe for the owning case: " + b.Revert);
                    continue;
                }
                proven++;
            }

            if (proven == 0 && Banned.Length > 0)
                failures.Add(C + " NOT ONE banned pattern proved itself. The whole banned half of this " +
                             "suite is blind and none of its greens mean anything.");

            if (proven == Banned.Length)
                notes.Add(C + " " + proven + "/" + Banned.Length + " banned pattern(s) proved against fixtures");
        }

        // =====================================================================
        //  2 -- [circle-vm-bound]
        // =====================================================================
        /// <summary>
        /// UiMvvmConformanceRegression.cs:53 runs HardFailOnNew with an EMPTY KnownBaseline:
        /// a new uGUI-constructing View earns its exemption ONLY by carrying IPanelViewModel
        /// or a .CreateDefault( call (:74-78). WO-1870 section 4 makes that the ui-mvvm rule
        /// for this ticket, and forbids adding an allow-list entry (:86-129) instead.
        /// </summary>
        private static void CaseVmBound(List<string> failures, List<string> notes)
        {
            const string C = "[circle-vm-bound]";
            string raw = ReadOrNull(PanelRel);
            if (raw == null) { failures.Add(Missing(C, PanelRel, "Lane B")); return; }

            string required = RegressionSourceText.StripComments(raw);
            if (required.IndexOf("CircleScreenVM.CreateDefault(", StringComparison.Ordinal) < 0)
            {
                failures.Add(C + " " + PanelRel + " does not call CircleScreenVM.CreateDefault(. That call " +
                             "IS the View's exemption from UiMvvmConformanceRegression's banned-symbol scan " +
                             "(:74-78); without it the gate hard-fails on the new View, and the only other " +
                             "way out is an allow-list entry that WO-1870 section 4 forbids.");
            }

            RunBannedFor(C, PanelRel, raw, failures);
            if (!HasFailure(failures, C)) notes.Add(C + " View binds the VM and names no service or finder");
        }

        // =====================================================================
        //  3 -- [circle-no-http-in-view]
        // =====================================================================
        private static void CaseNoHttpInView(List<string> failures, List<string> notes)
        {
            const string C = "[circle-no-http-in-view]";
            string raw = ReadOrNull(PanelRel);
            if (raw == null) { failures.Add(Missing(C, PanelRel, "Lane B")); return; }

            RunBannedFor(C, PanelRel, raw, failures);
            if (!HasFailure(failures, C)) notes.Add(C + " View builds no request and names no route");
        }

        // =====================================================================
        //  4 -- [circle-no-literals]  (localization law)
        // =====================================================================
        /// <summary>
        /// Every `.text =` in the View must source from LocalizedText(, LocalText.Format( or a
        /// VM property. A bare literal longer than 2 characters reaching a label is the exact
        /// violation the localization law was written for (memory
        /// localization_law_all_text_must_route_through_locale, which caught a HudKitController
        /// instance on 2026-09-17).
        ///
        /// <para>Read over StripComments: the needles the assignment is ALLOWED to contain are
        /// code, but the literal it is FORBIDDEN to contain is a string, so both halves of the
        /// judgement need the string bodies intact.</para>
        /// </summary>
        private static void CaseNoLiterals(List<string> failures, List<string> notes)
        {
            const string C = "[circle-no-literals]";
            string raw = ReadOrNull(PanelRel);
            if (raw == null) { failures.Add(Missing(C, PanelRel, "Lane B")); return; }

            string src = RegressionSourceText.StripComments(raw);
            var offenders = new List<string>();
            int assignments = 0;

            foreach (Match m in Regex.Matches(src, @"\.\s*text\s*=\s*([^;]{0,400});"))
            {
                assignments++;
                string rhs = m.Groups[1].Value;

                if (rhs.IndexOf("LocalizedText(", StringComparison.Ordinal) >= 0) continue;
                if (rhs.IndexOf("LocalText.Format(", StringComparison.Ordinal) >= 0) continue;
                if (rhs.IndexOf("LocalText.", StringComparison.Ordinal) >= 0) continue;
                // A VM-supplied string: MyDisplayName, row.TenureText, _vm.MemberCountText, ...
                if (Regex.IsMatch(rhs, @"\b(_?vm|_vm|row|tab|r|opt|member|leader)\s*\.")) continue;
                if (Regex.IsMatch(rhs, @"\bstring\.Empty\b")) continue;

                var lit = Regex.Match(rhs, "\"([^\"]*)\"");
                if (!lit.Success) continue;                       // not a literal at all
                if (lit.Groups[1].Value.Trim().Length <= 2) continue;   // "", " ", "#", "of"

                offenders.Add("<" + Collapse(m.Value) + ">");
            }

            if (offenders.Count > 0)
                failures.Add(C + " " + offenders.Count + " label assignment(s) in " + PanelRel +
                             " carry a hardcoded sentence instead of a locale key: " +
                             string.Join(" , ", offenders) + ". Localization law: every player-facing " +
                             "string routes through a key in all 10 catalogs. Revert recipe: set the " +
                             "title text to a hardcoded sentence.");
            else if (assignments == 0)
                failures.Add(C + " found ZERO `.text =` assignments in " + PanelRel + ". Either the View " +
                             "is a stub, or it sets label text through a helper this case cannot see -- " +
                             "both make this green meaningless, which is the hollow pass " +
                             "RegressionMarkerRegression RULE 4 exists to stop.");
            else
                notes.Add(C + " " + assignments + " label assignment(s), all key-sourced");
        }

        // =====================================================================
        //  5 -- [circle-no-stat-copy]  (RULING 3)
        // =====================================================================
        /// <summary>
        /// api/_lib/clan-ballot.js:192-241 ships ELEVEN perk options and every one of them is a
        /// stat edge or a unit unlock ("+15% build speed", "Unlocks an ancestor-summoned troop").
        /// The magnitudes ride the wire as options[].effect. Ruling 3 permits VISUAL and
        /// NARRATIVE outcomes only, and WO-1870 section 0c honours it by SUPPRESSION: the client
        /// renders title and description, never effect. This case is what makes that a rule
        /// rather than an intention.
        /// </summary>
        private static void CaseNoStatCopy(List<string> failures, List<string> notes)
        {
            const string C = "[circle-no-stat-copy]";

            string panelRaw = ReadOrNull(PanelRel);
            if (panelRaw == null) failures.Add(Missing(C, PanelRel, "Lane B"));
            else RunBannedFor(C, PanelRel, panelRaw, failures);

            string vmRaw = ReadOrNull(VmRel);
            if (vmRaw == null) { failures.Add(Missing(C, VmRel, "Lane A")); return; }

            // The VM may DECLARE EffectText (section 2 declares it so its emptiness is
            // deliberate and visible) and may assign it ONLY to string.Empty.
            string vm = RegressionSourceText.StripComments(vmRaw);
            foreach (Match m in Regex.Matches(vm, @"\bEffectText\s*=\s*([^;]{0,200});"))
            {
                string rhs = m.Groups[1].Value.Trim();
                if (rhs == "string.Empty" || rhs == "\"\"") continue;
                failures.Add(C + " " + VmRel + " assigns EffectText to <" + Collapse(rhs) + ">. Ruling 3: " +
                             "that field is declared ALWAYS EMPTY so the suppression of the server's stat " +
                             "magnitude is deliberate and pinned. Copying options[].effect into it ships " +
                             "'+15% build speed' to a player, against the owner's ruling and against the " +
                             "standing copy gate at api/clan/vault/register.js:130-146.");
            }

            if (vm.IndexOf("EffectText", StringComparison.Ordinal) < 0)
                failures.Add(C + " " + VmRel + " does not declare EffectText at all. WO-1870 section 2 " +
                             "declares it ON PURPOSE -- an absent field reads as an oversight, a declared " +
                             "always-empty one reads as a ruling. This case is the thing that keeps it so.");

            if (!HasFailure(failures, C))
                notes.Add(C + " ruling 3 held: no effect binding, no percent copy, EffectText empty-only");
        }

        // =====================================================================
        //  6 -- [circle-join-ungated]  (RULING 4)
        // =====================================================================
        /// <summary>
        /// api/clan/join.js:6-14's whole refusal set is CLAN_BAD_CODE | CLAN_NOT_FOUND |
        /// CLAN_ALREADY_IN_CLAN | CLAN_IDENTITY_MISSING | SERVER_ERROR -- no stake, no SGT, no
        /// Vigil. Joining is already un-gated; this case is what keeps it that way.
        /// </summary>
        private static void CaseJoinUngated(List<string> failures, List<string> notes)
        {
            const string C = "[circle-join-ungated]";

            string vmRaw = ReadOrNull(VmRel);
            string srcRaw = ReadOrNull(SourceRel);
            if (vmRaw == null) failures.Add(Missing(C, VmRel, "Lane A"));
            else RunBannedFor(C, VmRel, vmRaw, failures);
            if (srcRaw == null) failures.Add(Missing(C, SourceRel, "Lane A"));
            else RunBannedFor(C, SourceRel, srcRaw, failures);
            if (vmRaw == null) return;

            // CanSubmitJoin is computed from the CODE SHAPE ALONE: clans.code is
            // ^[A-HJ-NP-Z2-9]{6}$ (api/schema.sql:2305-2320), so the only inputs allowed
            // near that assignment are the draft string's length and character class.
            string vm = RegressionSourceText.StripComments(vmRaw);
            var assign = Regex.Match(vm, @"CanSubmitJoin\s*=\s*([^;]{0,300});");
            if (!assign.Success)
            {
                failures.Add(C + " " + VmRel + " never assigns CanSubmitJoin. Ruling 4 lives on that one " +
                             "expression -- if nothing computes it, nothing can be proven ungated.");
            }
            else if (Regex.IsMatch(assign.Groups[1].Value,
                     @"\b(Stake|stake|Vigil|vigil|Sgt|SGT|Weight)\b"))
            {
                failures.Add(C + " CanSubmitJoin is computed from <" + Collapse(assign.Groups[1].Value) +
                             ">, which names a stake/Vigil term. RULING 4: joining a Circle is NEVER gated " +
                             "on staking; stake adds Vigil WEIGHT only (clan-vigil.js:354). Revert recipe: " +
                             "add a Vigil term to CanSubmitJoin.");
            }

            if (!HasFailure(failures, C))
                notes.Add(C + " ruling 4 held: join is code-shape only, no stake symbol on either file");
        }

        // =====================================================================
        //  7 -- [circle-name-first]  (RULING 2)
        // =====================================================================
        /// <summary>
        /// "The 'Claim your Remnant name' step is THE FIRST THING A PLAYER WITHOUT A NAME SEES"
        /// (WO-1870 ruling 2 / section 2's CircleState comment). So the VM must declare
        /// NeedsName, and the View must render it BEFORE the tab row -- reachable with no Circle
        /// at all, which is the case the owner will hit on the phone.
        /// </summary>
        private static void CaseNameFirst(List<string> failures, List<string> notes)
        {
            const string C = "[circle-name-first]";

            string vmRaw = ReadOrNull(VmRel);
            if (vmRaw == null) failures.Add(Missing(C, VmRel, "Lane A"));
            else
            {
                string vm = RegressionSourceText.StripComments(vmRaw);
                if (!Regex.IsMatch(vm, @"\bNeedsName\b"))
                    failures.Add(C + " " + VmRel + " declares no CircleState.NeedsName. Ruling 2's whole " +
                                 "first-run step has no state to live in.");
                if (!Regex.IsMatch(vm, @"\bSubmitDisplayName\s*\("))
                    failures.Add(C + " " + VmRel + " exposes no SubmitDisplayName() verb, so the name step " +
                                 "can be shown but never completed.");
            }

            string panelRaw = ReadOrNull(PanelRel);
            if (panelRaw == null) { failures.Add(Missing(C, PanelRel, "Lane B")); return; }

            string panel = RegressionSourceText.StripComments(panelRaw);
            int needs = panel.IndexOf("NeedsName", StringComparison.Ordinal);
            if (needs < 0)
            {
                failures.Add(C + " " + PanelRel + " never mentions NeedsName -- the View cannot render a " +
                             "state it does not switch on, so a player with no display name would land " +
                             "straight on the tabs.");
                return;
            }

            // ORDER IS THE ASSERTION. The name step's branch must be authored before the tab
            // row's. Textual order in a single render method is the honest proxy available to a
            // source lint, and is named as one.
            int tabs = FirstIndexOfAny(panel, new[] { "circle.tab.members", "BuildTabRow", "TabRow(", "Tabs" });
            if (tabs >= 0 && tabs < needs)
                failures.Add(C + " the tab row is authored at char " + tabs + " of " + PanelRel +
                             ", BEFORE the NeedsName branch at char " + needs + ". Ruling 2 puts the name " +
                             "claim first. (PROXY: this is textual order in the View source, not a measured " +
                             "render order -- but the View is one code-built tree with one render path, so " +
                             "authoring order is the order.) Revert recipe: reorder so the tab row paints first.");

            if (!HasFailure(failures, C))
                notes.Add(C + " ruling 2 held: NeedsName declared, submittable, authored before the tabs");
        }

        // =====================================================================
        //  8 -- [circle-no-group-remnant]  (RULING 1)
        // =====================================================================
        /// <summary>
        /// Scans TWENTY-TWO files: the 20 catalogs, plus chat-phrases.json in BOTH mirrors and
        /// GooglePlayLocalizationVariantPolicy.json.
        ///
        /// <para>⚠ THE LAST TWO ARE NOT IN LocalizationPolicy.json's single `game` collection, so
        /// LocaleParityRegression CANNOT SEE THEM. This case is the only oracle in the tree that
        /// covers either, which is why they are named here rather than left to the parity suite.</para>
        ///
        /// <para>THREE ASSERTIONS, and the third is the one a blanket find-and-replace breaks:
        /// (a) none of the six renamed keys' VALUES still carries the locale group word;
        /// (b) common.remnant_chat is still PRESENT as a KEY (it is a literal needle at
        /// ClanFeatureGateRegression.cs:37 and in test/clan-chat-release-gate.test.js:22 --
        /// ruling 1 renames the VALUE and never the key); (c) chat-phrases.json phrase_4 STILL
        /// READS "Hello, Remnants." -- under ruling 1 a Remnant IS a player, so that line is now
        /// correct English and must survive the rename.</para>
        /// </summary>
        private static void CaseNoGroupRemnant(List<string> failures, List<string> notes)
        {
            const string C = "[circle-no-group-remnant]";
            int scanned = 0;

            // ---- (a) + (b): the 20 catalogs ----
            for (int d = 0; d < CatalogDirs.Length; d++)
            {
                for (int l = 0; l < Locales.Length; l++)
                {
                    string loc = Locales[l];
                    string rel = CatalogDirs[d] + "/" + loc + ".json";
                    string raw = ReadOrNull(rel);
                    if (raw == null) { failures.Add(C + " cannot read " + rel); continue; }
                    scanned++;

                    var flat = ReadFlatJson(raw);
                    string word = GroupWord.ContainsKey(loc) ? GroupWord[loc] : null;

                    for (int k = 0; k < RenamedKeys.Length; k++)
                    {
                        string key = RenamedKeys[k];
                        if (!flat.ContainsKey(key))
                        {
                            failures.Add(C + " " + rel + " no longer carries the key '" + key + "'. RULING 1 " +
                                         "renames the VALUE and NEVER the key -- common.remnant_chat in " +
                                         "particular is a literal needle at ClanFeatureGateRegression.cs:37 " +
                                         "and test/clan-chat-release-gate.test.js:22, both of which go red " +
                                         "the moment it is renamed.");
                            continue;
                        }
                        if (word == null) continue;
                        if (flat[key].IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                            failures.Add(C + " " + rel + " key '" + key + "' still carries the GROUP word " +
                                         "'" + word + "'. Ruling 1: the group is a CIRCLE; 'Remnant' now " +
                                         "means the PLAYER. Revert recipe: restore \"Remnant Chat\" in de.json.");
                    }
                }
            }

            // ---- (a) + (c): chat-phrases.json, both mirrors ----
            string[] phraseFiles = { PhrasesStreamRel, PhrasesResRel };
            for (int i = 0; i < phraseFiles.Length; i++)
            {
                string raw = ReadOrNull(phraseFiles[i]);
                if (raw == null) { failures.Add(C + " cannot read " + phraseFiles[i]); continue; }
                scanned++;

                string p3 = PhraseText(raw, "phrase_3");
                string p4 = PhraseText(raw, "phrase_4");

                if (p3 == null)
                    failures.Add(C + " " + phraseFiles[i] + " has no phrase_3 entry to check.");
                else if (p3.IndexOf("Remnant", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add(C + " " + phraseFiles[i] + " phrase_3 still reads <" + p3 + ">. That line " +
                                 "is GROUP sense (\"Welcome to the ...!\") and ruling 1 re-points it to Circle.");

                if (p4 == null)
                    failures.Add(C + " " + phraseFiles[i] + " has no phrase_4 entry.");
                else if (p4.IndexOf("Remnant", StringComparison.OrdinalIgnoreCase) < 0)
                    failures.Add(C + " " + phraseFiles[i] + " phrase_4 reads <" + p4 + "> and no longer says " +
                                 "Remnant. THIS IS THE NEGATIVE CASE: under ruling 1 a Remnant IS a player, " +
                                 "so \"Hello, Remnants.\" is now CORRECT English and must survive. A blanket " +
                                 "find-and-replace across the chat phrases is exactly what this line catches. " +
                                 "Revert recipe: rewrite phrase_4 to \"Hello, Circles.\"");
            }

            // ---- (a): the Google Play clanChat.noWallet override ----
            string policyRaw = ReadOrNull(PlayPolicyRel);
            if (policyRaw == null) failures.Add(C + " cannot read " + PlayPolicyRel);
            else
            {
                scanned++;
                string block = ReplacementValuesBlock(policyRaw, "clanChat.noWallet");
                if (block == null)
                    failures.Add(C + " " + PlayPolicyRel + " no longer carries a replacementRow for " +
                                 "clanChat.noWallet. That Play-channel override is invisible to " +
                                 "LocaleParityRegression (it is not in LocalizationPolicy.json's game " +
                                 "collection), so this case is the only thing that can see it drift.");
                else
                {
                    foreach (var kv in GroupWord)
                    {
                        if (block.IndexOf(kv.Value, StringComparison.OrdinalIgnoreCase) >= 0)
                            failures.Add(C + " " + PlayPolicyRel + "'s clanChat.noWallet override still " +
                                         "carries the group word '" + kv.Value + "' (" + kv.Key + "). The " +
                                         "Play channel would ship the retired branding while the canonical " +
                                         "catalogs shipped the new one.");
                    }
                }
            }

            if (!HasFailure(failures, C))
                notes.Add(C + " ruling 1 held across " + scanned + " file(s) (20 catalogs + 2 chat-phrases " +
                          "+ 1 Play override), phrase_4 preserved");
        }

        // =====================================================================
        //  9 -- [circle-keys-parity]
        // =====================================================================
        /// <summary>
        /// Every frozen key exists, non-empty, in all 10 StreamingAssets catalogs AND all 10
        /// Resources mirrors. LocaleParityRegression.cs:51-59 demands exact parity across both
        /// directories; this case is the WO-1870-specific half, so a missing key names ITSELF
        /// rather than showing up as an anonymous parity delta.
        /// </summary>
        private static void CaseKeysParity(List<string> failures, List<string> notes)
        {
            const string C = "[circle-keys-parity]";
            int files = 0, hits = 0;
            var missingByFile = new List<string>();

            for (int d = 0; d < CatalogDirs.Length; d++)
            {
                for (int l = 0; l < Locales.Length; l++)
                {
                    string rel = CatalogDirs[d] + "/" + Locales[l] + ".json";
                    string raw = ReadOrNull(rel);
                    if (raw == null)
                    {
                        failures.Add(C + " cannot read " + rel + " -- with a catalog unreadable this case " +
                                     "would 'pass' over nine files and prove nothing.");
                        continue;
                    }
                    files++;
                    var flat = ReadFlatJson(raw);

                    var missing = new List<string>();
                    var blank = new List<string>();
                    for (int k = 0; k < FrozenKeys.Length; k++)
                    {
                        string key = FrozenKeys[k];
                        if (!flat.ContainsKey(key)) { missing.Add(key); continue; }
                        if (flat[key].Trim().Length == 0) { blank.Add(key); continue; }
                        hits++;
                    }
                    if (missing.Count > 0)
                        missingByFile.Add(rel + " missing " + missing.Count + " (" + Head(missing, 4) + ")");
                    if (blank.Count > 0)
                        missingByFile.Add(rel + " BLANK " + blank.Count + " (" + Head(blank, 4) + ")");
                }
            }

            if (files == 0)
                failures.Add(C + " read ZERO catalogs. Nothing below this line means anything.");
            else if (missingByFile.Count > 0)
                failures.Add(C + " frozen-key parity broken: " + string.Join(" ; ", missingByFile) +
                             ". WO-1870 section 6 freezes " + FrozenKeys.Length + " key(s); Lane A " +
                             "references them, Lane B renders them, the lead merges them into all 20 files. " +
                             "Revert recipe: delete circle.error.badCode from ru.json, blank circle.title " +
                             "in ko.json.");
            else
                notes.Add(C + " " + hits + " key-hit(s) over " + files + " catalog file(s)");
        }

        // =====================================================================
        //  10 -- [circle-keys-referenced]
        // =====================================================================
        /// <summary>
        /// Two directions, and both matter. Every frozen key is NAMED by at least one file under
        /// Assets/_Modules/HUD/Circle/ (an authored key nothing renders is dead weight that the
        /// parity suite will police forever), and no circle.* / remnant.name.* key is referenced
        /// that the catalogs lack (a reference with no row resolves to the raw key on screen).
        /// </summary>
        private static void CaseKeysReferenced(List<string> failures, List<string> notes)
        {
            const string C = "[circle-keys-referenced]";
            string dir = RepoPath(CircleDirRel);

            string[] files;
            try { files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories) : new string[0]; }
            catch { files = new string[0]; }

            if (files.Length == 0)
            {
                failures.Add(C + " " + CircleDirRel + " holds no .cs file. EXPECTED RED until Lane A commit 1 " +
                             "(CircleScreenVM.cs) and Lane B (CircleScreenPanel.cs) land -- this is the " +
                             "WO-1870 section 7 acceptance that the oracle can fail at all.");
                return;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < files.Length; i++)
            {
                try { sb.Append(RegressionSourceText.StripComments(File.ReadAllText(files[i]))).Append('\n'); }
                catch { failures.Add(C + " could not read " + files[i]); }
            }
            string all = sb.ToString();

            var unreferenced = new List<string>();
            for (int k = 0; k < FrozenKeys.Length; k++)
                if (all.IndexOf(FrozenKeys[k], StringComparison.Ordinal) < 0) unreferenced.Add(FrozenKeys[k]);

            if (unreferenced.Count > 0)
                failures.Add(C + " " + unreferenced.Count + " frozen key(s) are authored but referenced by " +
                             "nothing under " + CircleDirRel + ": " + Head(unreferenced, 8) + ". Either the " +
                             "surface that renders them is still unwritten, or the key should never have " +
                             "been minted.");

            // The other direction: a reference the catalogs cannot answer.
            var en = ReadFlatJson(ReadOrNull(CatalogDirs[0] + "/en.json") ?? "{}");
            var ghosts = new List<string>();
            foreach (Match m in Regex.Matches(all, "\"((?:circle|remnant\\.name)\\.[A-Za-z0-9_.]+)\""))
            {
                string key = m.Groups[1].Value;
                if (en.ContainsKey(key)) continue;
                if (ghosts.Contains(key)) continue;
                ghosts.Add(key);
            }
            if (ghosts.Count > 0)
                failures.Add(C + " " + ghosts.Count + " key(s) referenced under " + CircleDirRel +
                             " have no row in en.json: " + Head(ghosts, 8) + ". LocalizedText resolves a " +
                             "missing key to the key itself, so the player reads 'circle.ghost' on screen. " +
                             "Revert recipe: reference circle.ghost from the VM.");

            if (!HasFailure(failures, C))
                notes.Add(C + " " + FrozenKeys.Length + " frozen key(s) referenced, no ghost references");
        }

        // =====================================================================
        //  11 -- [circle-error-map-total]
        // =====================================================================
        /// <summary>
        /// CircleScreenVM.PlayerFacingKey must name EVERY server code the routes can answer with,
        /// and carry a default branch.
        ///
        /// <para>⛔ THE EXPECTED SET IS DERIVED, NEVER TYPED. TouchFloorAuthoringRegression.cs:12-17
        /// states the rule: never assert a set by recomputing it from the same literal the code
        /// uses. So the codes are parsed OUT OF THE SERVER, from api/_lib/clan.js,
        /// clan-ballot.js, clan-vault*.js, wallet-auth.js and the two new
        /// api/profile/display-name*.js files.</para>
        ///
        /// <para>⚠ ONLY **QUOTED** TOKENS COUNT, and comment-only lines are dropped first. Those
        /// files carry heavy RCA prose that names codes in sentences; counting prose would
        /// inflate the required set and fail the VM for cases no route can ever emit. This is a
        /// deliberate narrowing and it is stated rather than implied.</para>
        ///
        /// <para>THE DERIVATION WAS RUN, NOT JUST WRITTEN (CLAUDE.md 11B: measuring something is not
        /// the same as measuring the right thing). Over the six server files it yields <b>46</b>
        /// codes with NO prose pollution, and Lane A's <c>CircleScreenVM.PlayerFacingKey</c> maps all
        /// 46 with a default branch -- simulated against the landed file on 2026-09-18, 0 unmapped.
        /// Three of the 46 belong to the report-message route the Circle screen never calls
        /// (CLAN_BAD_CLAN_ID, CLAN_BAD_MESSAGE_ID, CLAN_REPORT_NOT_MEMBER); they stay required,
        /// because all three map onto keys that already exist (circle.error.badRequest /
        /// circle.error.notInCircle) and a hand-typed exclusion list would be precisely the hearsay
        /// this derivation exists to avoid.</para>
        /// </summary>
        private static void CaseErrorMapTotal(List<string> failures, List<string> notes)
        {
            const string C = "[circle-error-map-total]";

            // ⛔ THE PROFILE HALF OF THIS SET CHANGED 2026-09-18, and the change is a LEAD RULING,
            // not a refactor: the Remnant name is the EXISTING player_profiles.username written by
            // the EXISTING POST /api/profile/username (WO-129). So there is no display-name route
            // and no 0038 migration, and the codes to cover are the USERNAME_* set
            // (TOO_SHORT | TOO_LONG | INVALID_CHARS | REJECTED | TAKEN) declared at
            // api/_lib/username-policy.js:11-13. api/profile/usernames.js is Lane A's one new
            // server file (the batch read).
            var sources = new List<string>
            {
                "api/_lib/clan.js", "api/_lib/clan-ballot.js", "api/_lib/wallet-auth.js",
                "api/profile/username.js", "api/_lib/username-policy.js", "api/profile/usernames.js",
            };
            // clan-vault*.js: GLOBBED, because the file is clan-vaults.js (plural) and a
            // hardcoded singular path would silently scan nothing.
            try
            {
                string libDir = RepoPath("api/_lib");
                if (Directory.Exists(libDir))
                    foreach (var f in Directory.GetFiles(libDir, "clan-vault*.js"))
                        sources.Add("api/_lib/" + Path.GetFileName(f));
            }
            catch { /* named below by the read count guard */ }

            var expected = new List<string>();
            int read = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                string raw = ReadOrNull(sources[i]);
                if (raw == null)
                {
                    failures.Add(C + " cannot read " + sources[i] +
                                 " -- the derived expected set is incomplete without it.");
                    continue;
                }
                read++;
                foreach (Match m in Regex.Matches(StripJsCommentLines(raw),
                    @"['""]((?:CLAN|USERNAME|PLAYER_ID)_[A-Z0-9_]+|BAD_PAYLOAD|SERVER_ERROR)['""]"))
                {
                    string code = m.Groups[1].Value;
                    if (!expected.Contains(code)) expected.Add(code);
                }
            }

            if (read == 0)
            {
                failures.Add(C + " read ZERO server files, so the expected code set is empty and this case " +
                             "is structurally incapable of failing.");
                return;
            }
            if (expected.Count == 0)
            {
                failures.Add(C + " parsed ZERO quoted error codes out of " + read + " server file(s). The " +
                             "emit shape moved and the derivation is blind -- re-read " +
                             "api/_lib/clan-http.js:217-219 before trusting any green here.");
                return;
            }

            string vmRaw = ReadOrNull(VmRel);
            if (vmRaw == null)
            {
                failures.Add(Missing(C, VmRel, "Lane A") + " (" + expected.Count +
                             " server code(s) were derived and have nowhere to be mapped)");
                return;
            }

            string vm = RegressionSourceText.StripComments(vmRaw);   // codes are QUOTED in the switch
            if (vm.IndexOf("PlayerFacingKey", StringComparison.Ordinal) < 0)
                failures.Add(C + " " + VmRel + " declares no PlayerFacingKey(string, long). WO-1870 section " +
                             "2 makes it PUBLIC and STATIC so the EditMode suite can drive the whole table " +
                             "directly (the ClanMembershipClient.cs:125-131 precedent).");

            if (!Regex.IsMatch(vm, @"\bdefault\s*:") && !Regex.IsMatch(vm, @"\bdefault\b\s*=>"))
                failures.Add(C + " " + VmRel + "'s error map has no default branch. An unmapped code would " +
                             "return null and the screen would refuse silently -- the no-silent-failures " +
                             "rule (CLAUDE.md 12.2).");

            var unmapped = new List<string>();
            for (int i = 0; i < expected.Count; i++)
                if (vm.IndexOf("\"" + expected[i] + "\"", StringComparison.Ordinal) < 0) unmapped.Add(expected[i]);

            if (unmapped.Count > 0)
                failures.Add(C + " " + unmapped.Count + " of " + expected.Count + " DERIVED server code(s) " +
                             "have no case in PlayerFacingKey: " + Head(unmapped, 10) + ". Every one of " +
                             "these is a refusal a route can really answer with, and an unmapped one reaches " +
                             "the player as the default sentence instead of the true reason. Revert recipe: " +
                             "delete the CLAN_VAULT_SIGNER_SGT_REUSED case.");
            else
                notes.Add(C + " " + expected.Count + " server code(s) derived from " + read +
                          " file(s), all mapped");
        }

        // =====================================================================
        //  12 -- [circle-routes-exist]
        // =====================================================================
        private static void CaseRoutesExist(List<string> failures, List<string> notes)
        {
            const string C = "[circle-routes-exist]";
            string raw = ReadOrNull(SourceRel);
            if (raw == null) { failures.Add(Missing(C, SourceRel, "Lane A")); return; }

            string src = RegressionSourceText.StripComments(raw);    // the URL IS a literal
            int found = 0;
            var dead = new List<string>();

            foreach (Match m in Regex.Matches(src, @"/api/(clan|profile)/([A-Za-z0-9/_-]+)"))
            {
                found++;
                string rel = "api/" + m.Groups[1].Value + "/" + m.Groups[2].Value.TrimEnd('/') + ".js";
                if (File.Exists(RepoPath(rel))) continue;
                if (dead.Contains(rel)) continue;
                dead.Add(rel);
            }

            if (found == 0)
                failures.Add(C + " " + SourceRel + " names no /api/clan or /api/profile route at all. A " +
                             "source that reaches nothing cannot feed the screen.");
            else if (dead.Count > 0)
                failures.Add(C + " " + dead.Count + " route(s) named by " + SourceRel + " have no handler " +
                             "file: " + Head(dead, 6) + ". WO-1870 section 0a is the reason this case " +
                             "exists: there is NO members-list route -- the roster comes from " +
                             "/api/clan/vigil -- and a client written from the obvious guess would call " +
                             "/api/clan/members and 404 forever. Revert recipe: point one call at " +
                             "/api/clan/members.");
            else
                notes.Add(C + " " + found + " route reference(s), every one resolves to a handler");
        }

        // =====================================================================
        //  13 -- [circle-signed]
        // =====================================================================
        /// <summary>
        /// BackendRequestSigner.cs:183-249: TryAttachAsync returning false means ABORT, DO NOT
        /// SEND. Every call site fails closed and traces (ClanMembershipClient.cs:89-94,
        /// ClanChatSource.cs:183-186). An unsigned clan request is a 401 the player reads as a
        /// broken screen.
        /// </summary>
        private static void CaseSigned(List<string> failures, List<string> notes)
        {
            const string C = "[circle-signed]";
            string raw = ReadOrNull(SourceRel);
            if (raw == null) { failures.Add(Missing(C, SourceRel, "Lane A")); return; }

            string src = RegressionSourceText.StripComments(raw);

            // ⛔ WRITTEN TO A SHARED SENDER, BECAUSE THAT IS WHAT LANE A SHIPPED. Every request in
            // CircleSource funnels SendGet / SendPost -> AttachAndSend, so the UnityWebRequest is
            // constructed in one method and signed in another. A lint asserting "attach within N
            // lines of construction", or counting attaches against sends, WOULD FAIL ON WORKING
            // CODE -- 16 call sites against ONE attach. The honest assertion is the one the shared
            // sender makes cheap: there is exactly one send, it lives in the signing method, and
            // the abort sits between the attach and it. One sender is the reason the ordering can
            // only ever be wrong in one place.
            var sender = null as MethodSpan;
            foreach (var h in SplitMethods(src))
                if (string.Equals(h.Name, "AttachAndSend", StringComparison.Ordinal)) sender = h;

            if (sender == null)
            {
                failures.Add(C + " " + SourceRel + " has no AttachAndSend method. Lane A funnels all 16 " +
                             "call sites through SendGet/SendPost -> AttachAndSend; without that single " +
                             "sender the signing order has to be re-proven at every call site.");
                return;
            }

            int sendsTotal = Count(src, "SendWebRequest");
            int sendsInSender = Count(sender.Body, "SendWebRequest");
            if (sendsTotal == 0)
                failures.Add(C + " " + SourceRel + " never calls SendWebRequest, so nothing is sent and " +
                             "this case cannot fail.");
            else if (sendsInSender != sendsTotal)
                failures.Add(C + " " + SourceRel + " has " + sendsTotal + " SendWebRequest call(s) but only " +
                             sendsInSender + " inside AttachAndSend. A send outside the one signing method " +
                             "is a request that bypasses BackendRequestSigner entirely -- a guaranteed 401, " +
                             "because every clan route is auth.mode=='wallet'. Revert recipe: move one " +
                             "SendWebRequest out of AttachAndSend.");

            // ORDER INSIDE THE SENDER: attach, then the false-return abort, then the send.
            int attachAt = sender.Body.IndexOf("TryAttachAsync", StringComparison.Ordinal);
            int sendAt = sender.Body.IndexOf("SendWebRequest", StringComparison.Ordinal);
            if (attachAt < 0)
                failures.Add(C + " AttachAndSend in " + SourceRel + " never calls " +
                             "BackendRequestSigner.TryAttachAsync.");
            else if (sendAt >= 0 && sendAt < attachAt)
                failures.Add(C + " AttachAndSend in " + SourceRel + " sends BEFORE it signs.");
            else
            {
                // BOTH SHIPPED GUARD SHAPES ARE ACCEPTED, and that was checked by running these two
                // patterns over the two files section 6 tells Lane A to copy -- not assumed:
                //   ClanChatSource.cs:183          if (!await BackendRequestSigner.TryAttachAsync(...))
                //   ClanMembershipClient.cs:89-91  bool safeToSend = await ...TryAttachAsync(...);
                //                                  if (!safeToSend)
                // Lane A used the SECOND shape. A pattern accepting only the first would have been
                // RED forever against correct code.
                // ⚠ BOTH PATTERNS RUN OVER THE WHOLE SENDER BODY, NOT OVER THE SPAN BETWEEN THE
                // ATTACH AND THE SEND. A first draft scanned that span and went RED against Lane A's
                // working code, for a reason worth writing down: in the hoisted shape the DECLARATION
                // sits BEFORE the TryAttachAsync token, so a window that starts at the attach can
                // never contain it. Ordering is already asserted separately above (attachAt <
                // sendAt); these two only answer "is there an abort at all".
                bool inlineGuard = Regex.IsMatch(sender.Body,
                    @"if\s*\(\s*!\s*(await\s+)?[^;{}]{0,120}TryAttachAsync");
                bool hoistedGuard = Regex.IsMatch(sender.Body,
                    @"(bool|var)\s+([A-Za-z_][A-Za-z0-9_]*)\s*=[\s\S]{0,200}?TryAttachAsync[\s\S]{0,300}?;" +
                    @"[\s\S]{0,300}?if\s*\(\s*!\s*\2\s*\)");
                if (!inlineGuard && !hoistedGuard)
                    failures.Add(C + " AttachAndSend in " + SourceRel + " signs and sends with NO branch on " +
                                 "a false return between them. BackendRequestSigner.cs:243-249: a " +
                                 "non-purchase POST with no live session returns false and the request is " +
                                 "NEVER SENT -- the client must surface circle.error.notSignedIn, never a " +
                                 "dead button. Revert recipe: delete the safeToSend guard.");
            }

            if (!HasFailure(failures, C))
                notes.Add(C + " one sender: " + sendsTotal + " send(s), all inside AttachAndSend, abort " +
                          "between attach and send");
        }

        // =====================================================================
        //  14 -- [circle-interactive-mint]
        // =====================================================================
        /// <summary>
        /// BackendRequestSigner.cs:243-249 + WO-1870 section 3: allowInteractiveSessionMint is a
        /// PER-ROW decision and it decides whether the phone demo works. READS pass false (the
        /// ClanMembershipClient.cs:89 precedent). Every WRITE is an explicit player press and
        /// passes true (the signer's own "for example, pressing Redeem" case at :178-180).
        ///
        /// <para>The two sets are DERIVED by reading each helper's own HTTP verb, never from a
        /// typed count -- TouchFloorAuthoringRegression.cs:29-31's rule.</para>
        /// </summary>
        private static void CaseInteractiveMint(List<string> failures, List<string> notes)
        {
            const string C = "[circle-interactive-mint]";
            string raw = ReadOrNull(SourceRel);
            if (raw == null) { failures.Add(Missing(C, SourceRel, "Lane A")); return; }

            string src = RegressionSourceText.StripComments(raw);

            // ⛔ THE FLAG IS READ AT THE CALL SITE, NOT IN A METHOD BODY, because that is where it
            // is written. Lane A funnels every request through SendGet / SendPost -> AttachAndSend,
            // so the verb and the flag are BOTH arguments of one call:
            //     SendGet ("clan/me",   MeUrl + ...,      false, done);   <- a READ
            //     SendPost("clan/join", JoinUrl, payload, true,  done);   <- a WRITE
            // A per-method-body parse would hunt for the literal inside Join / Promote / Kick and
            // not find one, because it is in the argument list -- correct code, red oracle. The
            // verb is still DERIVED (from which sender is called), never a typed count
            // (TouchFloorAuthoringRegression.cs:29-31).
            int reads = 0, writes = 0;
            var offenders = new List<string>();

            foreach (Match call in Regex.Matches(src, @"\bSend(Get|Post)\s*\("))
            {
                // Skip the two DECLARATIONS; only a call site carries a decided flag.
                int lineStart = src.LastIndexOf('\n', Math.Max(0, call.Index - 1)) + 1;
                string lead = src.Substring(lineStart, call.Index - lineStart);
                if (lead.IndexOf("private", StringComparison.Ordinal) >= 0 ||
                    lead.IndexOf("void", StringComparison.Ordinal) >= 0) continue;

                bool write = call.Groups[1].Value == "Post";
                string argText = ArgList(src, call.Index + call.Length - 1);
                if (argText == null)
                {
                    offenders.Add("Send" + call.Groups[1].Value + " at char " + call.Index +
                                  " has an unreadable argument list");
                    continue;
                }

                // The mint flag is the one standalone bool argument. A NAMED argument
                // (allowInteractiveSessionMint: true) counts too -- legal C#, and clearer at a
                // five-argument call site than a bare positional literal.
                var flags = Regex.Matches(argText, @"(?:^|,|:)\s*(true|false)\s*(?=,|$)");
                if (flags.Count != 1)
                {
                    offenders.Add("Send" + call.Groups[1].Value + " at char " + call.Index + " passes " +
                                  flags.Count + " bool argument(s); exactly one (the mint flag) is expected");
                    continue;
                }
                bool sendsTrue = flags[0].Groups[1].Value == "true";

                if (write)
                {
                    writes++;
                    if (!sendsTrue) offenders.Add("WRITE (SendPost) at char " + call.Index + " passes FALSE");
                }
                else
                {
                    reads++;
                    if (sendsTrue) offenders.Add("READ (SendGet) at char " + call.Index + " passes TRUE");
                }
            }

            if (reads + writes == 0)
                failures.Add(C + " no SendGet/SendPost call site was found in " + SourceRel + ", so neither " +
                             "set was derived and this green would mean nothing.");
            else if (offenders.Count > 0)
                failures.Add(C + " " + offenders.Count + " mint-flag fault(s) in " + SourceRel + ": " +
                             Head(offenders, 8) + ". A WRITE passing false means a player with no live " +
                             "session presses the button and the request is NEVER SENT " +
                             "(BackendRequestSigner.cs:243-249) -- a dead button on the phone, which is the " +
                             "demo this ticket exists for. A READ passing true raises an interactive session " +
                             "prompt on a passive refresh; reads pass false (ClanMembershipClient.cs:89). " +
                             "Revert recipe: flip the Create call site to false.");
            else
                notes.Add(C + " " + writes + " write call site(s) pass true, " + reads +
                          " read call site(s) pass false; verb derived from the sender");

        }

        // =====================================================================
        //  15 -- [circle-touch-floor]
        // =====================================================================
        /// <summary>
        /// ARITHMETIC ON SOURCE-PARSED CONSTANTS against ElarionUiKit.MinTouchPx, read AS A
        /// SYMBOL (TouchFloorAuthoringRegression.cs:29-31) so lowering the floor to "fix" a
        /// failure moves the assertion with it. The scaler model is parsed out of
        /// ElarionUiKit.cs, and every band is asserted at the SMALLEST captured reference
        /// height -- clear the floor at the worst aspect and it is clear at all three.
        ///
        /// <para>⛔ THE AUTHORING CONVENTION LANE B MUST FOLLOW, stated here because this case
        /// enforces it: every tappable band in CircleScreenPanel.cs is declared as a
        /// `private const float &lt;Name&gt;BandH = 0.0XXf;` fraction of the panel height. The
        /// kit's ClampMinTouch is a rescue, not a design -- ManageScreenPanel.cs:80-81 authors
        /// to the floor rather than leaving it to the clamp, and so does this screen.</para>
        ///
        /// <para>⚠ WHAT IT CANNOT PROVE: that the band lays out the way its anchors say.
        /// LayoutOracle.Audit on the capture path is that authority.</para>
        /// </summary>
        private static void CaseTouchFloor(List<string> failures, List<string> notes)
        {
            const string C = "[circle-touch-floor]";
            string raw = ReadOrNull(PanelRel);
            if (raw == null) { failures.Add(Missing(C, PanelRel, "Lane B")); return; }

            float refH = SmallestReferenceHeight(C, failures, notes);
            float floor = ElarionUiKit.MinTouchPx;        // THE SYMBOL, never a typed 112
            string src = RegressionSourceText.StripComments(raw);

            int bands = 0;
            var thin = new List<string>();
            foreach (Match m in Regex.Matches(src,
                @"const\s+float\s+([A-Za-z0-9_]*(?:BandH|RowH|CellH))\s*=\s*(-?\d+(?:\.\d+)?)f?\s*;"))
            {
                bands++;
                float frac = ParseF(m.Groups[2].Value, 0f);
                float px = frac * refH;
                if (px + 0.05f < floor)
                    thin.Add(m.Groups[1].Value + "=" + frac.ToString("0.####") + " -> " +
                             px.ToString("0.#") + "px");
            }

            if (bands == 0)
                failures.Add(C + " " + PanelRel + " declares no `const float *BandH/*RowH/*CellH` band " +
                             "constant, so no authored band could be measured. That is the authoring " +
                             "convention this case enforces (see the summary above) -- a View that hides " +
                             "its bands inline is a View whose touch floor nobody can check, and the " +
                             "device then prints CLAMP FIRED for every face.");
            else if (thin.Count > 0)
                failures.Add(C + " " + thin.Count + " authored band(s) in " + PanelRel + " resolve BELOW " +
                             "ElarionUiKit.MinTouchPx=" + floor.ToString("0.#") + " ref px at the smallest " +
                             "captured reference height " + refH.ToString("0.#") + ": " + Head(thin, 6) +
                             ". THE UNIT IS THE BUG: a band can pass its own comment in DEVICE pixels and " +
                             "fail in the reference pixels the floor is measured in " +
                             "(TouchFloorAuthoringRegression.cs:52-56). Revert recipe: set the tab-row " +
                             "band to 0.06f.");
            else
                notes.Add(C + " " + bands + " authored band(s) clear " + floor.ToString("0.#") +
                          "px at refH " + refH.ToString("0.#"));
        }

        // =====================================================================
        //  16 -- [circle-door]
        // =====================================================================
        /// <summary>
        /// PanelDoorRegression.cs:21-25,57-63: a MonoBehaviour under Assets/_Modules/ whose type
        /// name ends in Panel needs a door outside its own View/VM/Bootstrap loop.
        /// CircleScreenPanel satisfies D2 (the bootstrap) and D1 (ClanChatPanel names it).
        ///
        /// <para>PanelRouter.cs:37-190 is APPEND-ONLY with load-bearing values: Circle = 28 is
        /// appended and HonestFeedback = 27 keeps its value. A renumber silently re-points every
        /// stored ordinal.</para>
        /// </summary>
        private static void CaseDoor(List<string> failures, List<string> notes)
        {
            const string C = "[circle-door]";

            // ---- PanelId, append-only ----
            string routerRaw = ReadOrNull(RouterRel);
            if (routerRaw == null) failures.Add(C + " cannot read " + RouterRel);
            else
            {
                string router = RegressionSourceText.StripComments(routerRaw);
                if (!Regex.IsMatch(router, @"\bHonestFeedback\s*=\s*27\b"))
                    failures.Add(C + " " + RouterRel + " no longer has HonestFeedback = 27. PanelId is " +
                                 "APPEND-ONLY and its values are load-bearing; the member Circle appends " +
                                 "after must not have displaced anything.");
                if (!Regex.IsMatch(router, @"\bCircle\s*=\s*28\b"))
                    failures.Add(C + " " + RouterRel + " does not declare Circle = 28. EXPECTED RED until " +
                                 "Lane B appends it (WO-1870 section 6: that ONE member and its doc comment, " +
                                 "nothing renumbered).");
            }

            // ---- Door 1: ClanChatPanel, VISIBLE IN EVERY STATE ----
            string chatRaw = ReadOrNull(ChatPanelRel);
            if (chatRaw == null) failures.Add(C + " cannot read " + ChatPanelRel);
            else
            {
                string chat = RegressionSourceText.StripComments(chatRaw);
                int door = chat.IndexOf("PanelRouter.Open(PanelId.Circle)", StringComparison.Ordinal);
                if (door < 0)
                {
                    failures.Add(C + " " + ChatPanelRel + " does not call PanelRouter.Open(PanelId.Circle). " +
                                 "EXPECTED RED until Lane B adds it. This is DOOR 1 -- the one the owner " +
                                 "will find, because the gear drawer's Circle Chat row is exactly where she " +
                                 "already looked and found no join option. Revert recipe: delete the " +
                                 "ClanChatPanel door line.");
                }
                else
                {
                    // BOUNDED TEXT PROXY, named as one: the door must be visible in EVERY state, so it
                    // may not be authored inside the no-Circle branch. A source lint cannot evaluate a
                    // branch, so it asks whether the 600 characters preceding the call mention the
                    // no-Circle key at all.
                    int from = Math.Max(0, door - 600);
                    string before = chat.Substring(from, door - from);
                    if (before.IndexOf("NoClan", StringComparison.OrdinalIgnoreCase) >= 0)
                        failures.Add(C + " the PanelId.Circle door in " + ChatPanelRel + " sits within 600 " +
                                     "chars of a noClan reference, which reads as authored INSIDE the " +
                                     "no-Circle branch. WO-1870 section 5 puts it in the modal body VISIBLE " +
                                     "IN EVERY STATE -- a player already in a Circle still needs the members " +
                                     "list. (PROXY: a bounded text window, not a parsed branch. If Lane B " +
                                     "has a legitimate layout that trips it, the honest fix is to widen the " +
                                     "separation, not to weaken the case.)");
                }
            }

            // ---- Door 2: the bootstrap ----
            string bootRaw = ReadOrNull(BootstrapRel);
            if (bootRaw == null) { failures.Add(Missing(C, BootstrapRel, "Lane B")); }
            else
            {
                string boot = RegressionSourceText.StripComments(bootRaw);
                if (boot.IndexOf("[RuntimeInitializeOnLoadMethod]", StringComparison.Ordinal) < 0)
                    failures.Add(C + " " + BootstrapRel + " carries no [RuntimeInitializeOnLoadMethod]. That " +
                                 "attribute IS the PanelDoorRegression D2 root (the ManageScreenBootstrap.cs:27 " +
                                 "/ ClanChatPanelBootstrap.cs:21 shape).");

                int gate = boot.IndexOf("ClanFeatureGate.PlayerFacingEnabled", StringComparison.Ordinal);
                int spawn = boot.IndexOf("new GameObject(", StringComparison.Ordinal);
                if (gate < 0)
                    failures.Add(C + " " + BootstrapRel + " does not check ClanFeatureGate.PlayerFacingEnabled. " +
                                 "ClanChatPanelBootstrap.cs:34 makes that check the LITERAL FIRST LINE, before " +
                                 "any GameObject exists, and ClanFeatureGateRegression.cs:26,31-33 pins it.");
                else if (spawn >= 0 && spawn < gate)
                    failures.Add(C + " " + BootstrapRel + " constructs a GameObject BEFORE the feature-gate " +
                                 "check. With the gate off the panel would already exist -- the exact " +
                                 "ordering ClanFeatureGateRegression.cs:31-33 pins for the chat bootstrap.");
            }

            if (!HasFailure(failures, C))
                notes.Add(C + " PanelId appended, chat door always-visible (proxy), bootstrap gated first");
        }

        // =====================================================================
        //  17 -- [circle-dock-untouched]   <-- the ONE case that is green TODAY
        // =====================================================================
        /// <summary>
        /// THE GEAR DRAWER IS FULL, AND THAT WAS MEASURED (WO-1870 section 5).
        /// HudKitController.AddDockTab lays rows out as `const int columns = 2; const int rows =
        /// 3;` -- a 2x3 grid, SIX cells -- and all six are occupied: Chat, Leaderboard, Music,
        /// Settings, Realm, Pause. A seventh row computes row = 3 and paints BELOW DockInnerY0,
        /// outside the drawer panel, and it also moves the very cell WO-1465's clearance geometry
        /// is written around (HudUiRegression.CheckGearDrawerClearsNeighbours).
        ///
        /// <para>So no lane in WO-1870 touches this file, and ClanFeatureGateRegression.cs:37's
        /// literal needle stays byte-identical. Ruling 1 changes the VALUE behind
        /// common.remnant_chat and never the key -- which is precisely what lets that needle
        /// survive a rename.</para>
        ///
        /// <para>Every number below was counted in the file on 2026-09-18, not copied from the
        /// WO (CLAUDE.md 11B: a number copied from a doc is hearsay until re-read at source).</para>
        /// </summary>
        private static void CaseDockUntouched(List<string> failures, List<string> notes)
        {
            const string C = "[circle-dock-untouched]";
            string raw = ReadOrNull(DockRel);
            if (raw == null) { failures.Add(C + " cannot read " + DockRel); return; }

            // The needle is a QUOTED key inside a real statement, so comments-only stripping:
            // the full stripper would blank "common.remnant_chat" and the needle would never match.
            string src = RegressionSourceText.StripComments(raw);

            const string Needle = "new LocalizedText(\"common.remnant_chat\").Resolve(), OpenClanChat);";
            int needle = src.IndexOf(Needle, StringComparison.Ordinal);
            if (needle < 0)
            {
                failures.Add(C + " " + DockRel + " no longer contains the exact needle <" + Needle + ">. " +
                             "ClanFeatureGateRegression.cs:37 and test/clan-chat-release-gate.test.js:22 " +
                             "both grep for it, so this is a three-suite break. Revert recipe: add a " +
                             "seventh AddDockTab call.");
            }
            else
            {
                int gate = src.LastIndexOf("if (DeNelle.Core.Services.ClanFeatureGate.PlayerFacingEnabled)",
                                           needle, StringComparison.Ordinal);
                if (gate < 0)
                    failures.Add(C + " the Chat dock row in " + DockRel + " is no longer preceded by its " +
                                 "ClanFeatureGate.PlayerFacingEnabled check. ClanFeatureGateRegression.cs:40 " +
                                 "pins that ordering.");
            }

            if (src.IndexOf("const int rows = 3;", StringComparison.Ordinal) < 0)
                failures.Add(C + " AddDockTab in " + DockRel + " no longer reads `const int rows = 3;`. The " +
                             "drawer's 2x3 shape is what makes six the ceiling; growing it re-opens " +
                             "WO-1465's clearance geometry and HudUiRegression's 9a/9b.");
            if (src.IndexOf("const int columns = 2;", StringComparison.Ordinal) < 0)
                failures.Add(C + " AddDockTab in " + DockRel + " no longer reads `const int columns = 2;`.");

            int rows = Count(src, "AddDockTab(_slideDock.panel");
            if (rows != 6)
                failures.Add(C + " " + DockRel + " spawns " + rows + " gear-dock row(s); the drawer holds " +
                             "exactly SIX (Chat, Leaderboard, Music, Settings, Realm, Pause) and " +
                             "DockPauseCellIndex pins PAUSE at index 5, the bottom-right cell. A seventh " +
                             "computes row = 3 and paints outside the panel. WO-1870 adds NO dock row -- " +
                             "its doors are the chat-panel button and the bootstrap (section 5).");

            if (!HasFailure(failures, C))
                notes.Add(C + " gear dock intact: 6 rows, 2x3 grid, chat needle + gate ordering byte-identical");
        }

        // =====================================================================
        //  18 -- [circle-instrumented]
        // =====================================================================
        /// <summary>
        /// CLAUDE.md 12, and WO-1870 scope item 5: instrumented FROM THE FIRST LINE. FlowTrace at
        /// open / each fetch / each verb / each refusal; Guard.TryEach around every list build.
        /// A screen that fetches five routes and writes ten and leaves no trace is a screen whose
        /// first bug costs a day.
        /// </summary>
        private static void CaseInstrumented(List<string> failures, List<string> notes)
        {
            const string C = "[circle-instrumented]";

            string srcRaw = ReadOrNull(SourceRel);
            if (srcRaw == null) failures.Add(Missing(C, SourceRel, "Lane A"));
            else
            {
                string src = RegressionSourceText.StripComments(srcRaw);
                var silent = new List<string>();
                int traced = 0;
                foreach (var h in SplitMethods(src))
                {
                    if (h.Body.IndexOf("UnityWebRequest", StringComparison.Ordinal) < 0 &&
                        h.Body.IndexOf("TryAttachAsync", StringComparison.Ordinal) < 0) continue;
                    if (h.Body.IndexOf("FlowTrace.", StringComparison.Ordinal) >= 0) { traced++; continue; }
                    silent.Add(h.Name);
                }
                if (traced == 0 && silent.Count == 0)
                    failures.Add(C + " no network helper was found in " + SourceRel + ", so nothing could be " +
                                 "checked for instrumentation.");
                else if (silent.Count > 0)
                    failures.Add(C + " " + silent.Count + " network helper(s) in " + SourceRel + " carry NO " +
                                 "FlowTrace call: " + Head(silent, 8) + ". CLAUDE.md 12 is a hard gate -- no " +
                                 "code edit on a non-trivial bug until captured data proves the cause, which " +
                                 "requires the data to have been captured. Revert recipe: strip the " +
                                 "FlowTrace.Step from Join.");
                else
                    notes.Add(C + " " + traced + " network helper(s) traced");
            }

            string vmRaw = ReadOrNull(VmRel);
            if (vmRaw == null) { failures.Add(Missing(C, VmRel, "Lane A")); return; }

            string vm = RegressionSourceText.StripComments(vmRaw);
            int guards = Count(vm, "Guard.Try");
            // Five list builds: Members, Leaderboard, VaultRows, BallotTiers, BallotOptions
            // (WO-1870 section 2). Each is a fan-out over server rows and each can meet one bad
            // row; Guard.TryEach logs it and skips instead of blanking the tab.
            if (guards < 5)
                failures.Add(C + " " + VmRel + " has " + guards + " Guard.Try/TryEach call(s); WO-1870 " +
                             "section 2 builds FIVE row lists (Members, Leaderboard, VaultRows, " +
                             "BallotTiers, BallotOptions) and CLAUDE.md 12.2 wants every list build wrapped " +
                             "so one bad row logs and is skipped rather than silently blanking a tab.");
            else
                notes.Add(C + " " + guards + " Guard wrapper(s) over the VM's list builds");
        }


        // =====================================================================
        //  SHARED MACHINERY
        // =====================================================================

        /// <summary>
        /// Repo root, RESOLVED AT RUNTIME. CLAUDE.md 0: the root is machine-dependent (C:\ on one
        /// seat, D:\ on another) and a hardcoded absolute path is how a doc sends a seat to a
        /// directory that does not exist. Application.dataPath's parent is the root on every seat.
        /// </summary>
        private static string RepoPath(string relative)
        {
            string root;
            try { root = Path.GetDirectoryName(Application.dataPath); }
            catch { root = null; }
            if (string.IsNullOrEmpty(root)) return relative;
            return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ReadOrNull(string relative)
        {
            try
            {
                string p = RepoPath(relative);
                return File.Exists(p) ? File.ReadAllText(p) : null;
            }
            catch { return null; }
        }

        private static string Missing(string caseTag, string rel, string lane)
        {
            return caseTag + " " + rel + " does not exist. EXPECTED RED until " + lane + " lands it " +
                   "(WO-1870 section 6). This suite was written FIRST on purpose: the failure naming " +
                   "every missing file IS the acceptance that the oracle can fail at all.";
        }

        private static bool HasFailure(List<string> failures, string caseTag)
        {
            for (int i = 0; i < failures.Count; i++)
                if (failures[i].StartsWith(caseTag, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Runs every banned shape owned by one case against one file, per-needle direction.</summary>
        private static void RunBannedFor(string caseTag, string rel, string raw, List<string> failures)
        {
            string codeOnly = null, withStrings = null;
            for (int i = 0; i < Banned.Length; i++)
            {
                var b = Banned[i];
                if (!string.Equals(b.Case, caseTag, StringComparison.Ordinal)) continue;

                string text;
                if (b.Dir == Direction.CodeOnly)
                {
                    if (codeOnly == null) codeOnly = RegressionSourceText.StripCommentsAndStrings(raw);
                    text = codeOnly;
                }
                else
                {
                    if (withStrings == null) withStrings = RegressionSourceText.StripComments(raw);
                    text = withStrings;
                }

                Match hit;
                try
                {
                    hit = Regex.Match(text, b.Pattern,
                        b.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
                }
                catch (Exception ex)
                {
                    failures.Add(caseTag + " pattern '" + b.Name + "' failed to run: " + ex.Message);
                    continue;
                }
                if (!hit.Success) continue;

                failures.Add(caseTag + " " + rel + " matches banned shape '" + b.Name + "' at char " +
                             hit.Index + ": <" + Collapse(hit.Value) + ">. Revert recipe: " + b.Revert +
                             ". (Matched over " + (b.Dir == Direction.CodeOnly
                                ? "code only -- comments and string bodies blanked, so this file's own prose " +
                                  "cannot be the match"
                                : "code + string literals -- this needle's violation IS string content, and " +
                                  "the full stripper would have blanked it into a hollow pass") + ".)");
            }
        }

        /// <summary>
        /// The kit's CanvasScaler model, PARSED rather than typed, so re-tuning the scaler
        /// re-tunes this suite instead of silently invalidating it.
        /// TouchFloorAuthoringRegression.cs:34-56 derives it the same way and for the same reason.
        /// </summary>
        private static float SmallestReferenceHeight(string caseTag, List<string> failures, List<string> notes)
        {
            const float Fallback = 965.4f;    // 2670x1200 (the Seeker) under the shipped scaler
            string kit = ReadOrNull(KitRel);
            if (kit == null)
            {
                failures.Add(caseTag + " cannot read " + KitRel + ", so the reference height could not be " +
                             "derived and every band would be judged against a number nobody measured.");
                return Fallback;
            }

            var res = Regex.Match(kit,
                @"referenceResolution\s*=\s*new\s+Vector2\(\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\)");
            var match = Regex.Match(kit, @"matchWidthOrHeight\s*=\s*(-?\d+(?:\.\d+)?)f?\s*;");
            if (!res.Success || !match.Success)
            {
                failures.Add(caseTag + " could not parse referenceResolution / matchWidthOrHeight out of " +
                             KitRel + "; the kit's scaler setup moved and this arithmetic is only as good " +
                             "as that model.");
                return Fallback;
            }

            float refW = ParseF(res.Groups[1].Value, 1080f);
            float refHRes = ParseF(res.Groups[2].Value, 1920f);
            float m = Mathf.Clamp01(ParseF(match.Groups[1].Value, 0.5f));

            var aspects = new[] { new[] { 1920, 1080 }, new[] { 2340, 1080 }, new[] { 2670, 1200 } };
            float smallest = float.MaxValue;
            for (int i = 0; i < aspects.Length; i++)
            {
                float w = aspects[i][0], h = aspects[i][1];
                float scale = Mathf.Pow(w / Mathf.Max(1f, refW), 1f - m) *
                              Mathf.Pow(h / Mathf.Max(1f, refHRes), m);
                if (scale <= 0.0001f) continue;
                float rh = h / scale;
                if (rh < smallest) smallest = rh;
            }
            if (smallest >= float.MaxValue) return Fallback;
            return smallest;
        }

        // ---- flat JSON ------------------------------------------------------

        /// <summary>
        /// Reads a FLAT {"key":"value"} catalog. Deliberately minimal: DeNelle.HUD has no
        /// Newtonsoft (WO-1870 section 3) and JsonUtility cannot materialise a dictionary, so
        /// every catalog reader in this tree is a scanner. Handles \" and \\ escapes and unescapes
        /// \n / \" / \\ so a value's real text is what gets searched.
        /// </summary>
        private static Dictionary<string, string> ReadFlatJson(string raw)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(raw)) return map;

            foreach (Match m in Regex.Matches(raw,
                "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                string key = Unescape(m.Groups[1].Value);
                string val = Unescape(m.Groups[2].Value);
                if (!map.ContainsKey(key)) map[key] = val;
            }
            return map;
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\n", "\n").Replace("\\t", "\t")
                    .Replace("\\\"", "\"").Replace("\\/", "/").Replace("\\\\", "\\");
        }

        /// <summary>chat-phrases.json is an ARRAY of objects, so the flat reader cannot see it.</summary>
        private static string PhraseText(string raw, string id)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var m = Regex.Match(raw,
                "\"id\"\\s*:\\s*\"" + Regex.Escape(id) + "\"[^}]*?\"text\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Unescape(m.Groups[1].Value) : null;
        }

        /// <summary>
        /// The "values" object of one replacementRow in GooglePlayLocalizationVariantPolicy.json.
        /// Returned as RAW TEXT: the caller only asks whether a group word survives anywhere in
        /// it, and a text window answers that without a full JSON parser.
        /// </summary>
        private static string ReplacementValuesBlock(string raw, string key)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            int at = raw.IndexOf("\"key\": \"" + key + "\"", StringComparison.Ordinal);
            if (at < 0) at = raw.IndexOf("\"key\":\"" + key + "\"", StringComparison.Ordinal);
            if (at < 0) return null;

            int vals = raw.IndexOf("\"values\"", at, StringComparison.Ordinal);
            if (vals < 0) return null;
            int open = raw.IndexOf('{', vals);
            if (open < 0) return null;
            int close = raw.IndexOf('}', open);
            if (close < 0) return null;
            return raw.Substring(open, close - open + 1);
        }

        // ---- JS / C# text ---------------------------------------------------

        /// <summary>
        /// Drops comment-only lines from a JS file. NOT a full stripper: the codes this suite
        /// derives are QUOTED string content and must survive, while the RCA prose that names
        /// them in sentences must not inflate the derived set.
        /// </summary>
        private static string StripJsCommentLines(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var sb = new StringBuilder(raw.Length);
            var lines = raw.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal)) continue;
                if (t.StartsWith("*", StringComparison.Ordinal)) continue;
                if (t.StartsWith("/*", StringComparison.Ordinal)) continue;
                sb.Append(lines[i]).Append('\n');
            }
            return sb.ToString();
        }

        private sealed class MethodSpan { public string Name; public string Body; }

        /// <summary>
        /// Splits C# source into rough method spans by brace depth. A parser-free approximation,
        /// and honest about it: it is used only to attribute an HTTP verb and a FlowTrace call to
        /// the SAME helper, which does not need a real syntax tree. A helper it fails to isolate
        /// reports as ambiguous rather than passing silently.
        /// </summary>
        private static List<MethodSpan> SplitMethods(string src)
        {
            var spans = new List<MethodSpan>();
            if (string.IsNullOrEmpty(src)) return spans;

            foreach (Match m in Regex.Matches(src,
                @"(?:public|private|protected|internal)\s+(?:static\s+|async\s+|override\s+|sealed\s+)*" +
                @"[A-Za-z_<>,\[\]\.\s]+?\s+([A-Za-z_][A-Za-z0-9_]*)\s*\([^)]*\)\s*\{"))
            {
                // The signature regex ENDS on the body's opening brace, so its last character IS
                // that brace and no search is needed. Taking the index directly (rather than
                // searching for a brace character) also keeps this file balanced under the
                // CLAUDE.md section 1 one-liner, which counts every brace in the file including
                // the ones inside char literals -- a lone unpaired brace literal reads as a
                // mismatch there while tools/gate_brace.py, the exact port of
                // CompileGate.BraceBalanced, calls the file clean. Both gates green is cheaper
                // than explaining that divergence to every future reader.
                int open = m.Index + m.Length - 1;
                if (open < 0 || open >= src.Length) continue;
                int depth = 0, i = open;
                for (; i < src.Length; i++)
                {
                    if (src[i] == '{') depth++;
                    else if (src[i] == '}') { depth--; if (depth == 0) break; }
                }
                if (i >= src.Length) continue;
                spans.Add(new MethodSpan { Name = m.Groups[1].Value, Body = src.Substring(open, i - open + 1) });
            }
            return spans;
        }

        /// <summary>
        /// The argument text of a call whose OPENING PAREN is at <paramref name="openParen"/>,
        /// with nested parens balanced. Needed because a SendGet/SendPost argument list contains
        /// its own concatenations and calls -- <c>MeUrl + "?playerId=" + Escape(WalletAddress)</c> --
        /// so a regex stopping at the first close paren would truncate before ever reaching the
        /// mint flag. Returns null on an unterminated list rather than a partial string, so
        /// [circle-interactive-mint] reports an unreadable call site instead of judging one.
        /// </summary>
        private static string ArgList(string src, int openParen)
        {
            if (src == null || openParen < 0 || openParen >= src.Length) return null;
            if (src[openParen] != '(') return null;
            int depth = 0;
            for (int i = openParen; i < src.Length; i++)
            {
                char ch = src[i];
                if (ch == '(') depth++;
                else if (ch == ')')
                {
                    depth--;
                    if (depth == 0) return src.Substring(openParen + 1, i - openParen - 1);
                }
            }
            return null;
        }

        private static int FirstIndexOfAny(string src, string[] needles)
        {
            int best = -1;
            for (int i = 0; i < needles.Length; i++)
            {
                int at = src.IndexOf(needles[i], StringComparison.Ordinal);
                if (at < 0) continue;
                if (best < 0 || at < best) best = at;
            }
            return best;
        }

        private static int Count(string src, string needle)
        {
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(needle)) return 0;
            int n = 0, i = 0;
            while ((i = src.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        private static string Collapse(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            string one = Regex.Replace(s, @"\s+", " ").Trim();
            return one.Length <= 160 ? one : one.Substring(0, 157) + "...";
        }

        private static string Head(List<string> items, int max)
        {
            if (items == null || items.Count == 0) return "(none)";
            int n = Math.Min(max, items.Count);
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++) { if (i > 0) sb.Append(", "); sb.Append(items[i]); }
            if (items.Count > n) sb.Append(", +").Append(items.Count - n).Append(" more");
            return sb.ToString();
        }

        private static float ParseF(string s, float fallback)
        {
            return float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }
    }
}
