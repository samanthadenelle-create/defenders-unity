// =============================================================================
// CircleErrorMapTests (EditMode) — WO-1870 Lane A.
// -----------------------------------------------------------------------------
// CircleScreenVM.PlayerFacingKey is the ONE place a server refusal becomes a sentence the
// player reads, so it is driven directly here — the ClanMembershipClient.ApplyResponseJson
// precedent (that file made its one piece of real logic public for exactly this reason).
//
// ⛔ THE EXPECTED SET IS DERIVED FROM THE SERVER, NOT TYPED HERE. The totality case reads
// every QUOTED CLAN_* code literal out of api/_lib/clan*.js and api/clan/**, and requires a
// case for each. A test that re-types the same list the code uses proves only that someone
// typed it twice — TouchFloorAuthoringRegression.cs:12-17's rule, applied to an error table.
//
// ⚠ WHY A MISS IS STILL NOT A CRASH: the mapping has a default branch keyed on the HTTP
// status, so an unmapped code still produces an actionable sentence. This suite is what
// keeps the table from quietly degrading into that fallback for codes we DO know about.
// =============================================================================

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using DeNelle.HUD;

namespace DeNelle.Tests.EditMode
{
    [TestFixture]
    public class CircleErrorMapTests
    {
        private static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        // ═══════════════════════════════════════════════════════════════════════
        // Totality, derived from the routes themselves
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void every_clan_code_the_server_can_answer_with_has_its_own_key()
        {
            var codes = ScrapeClanCodes();
            Assert.That(codes.Count, Is.GreaterThan(20),
                "the scrape found almost nothing — the paths moved, and a green test would be a lie");

            var unmapped = new List<string>();
            foreach (var code in codes)
            {
                // 500 is the harshest default: if the mapping falls through, this is what it
                // returns. A code that maps to the fallback is a code with no sentence.
                string key = CircleScreenVM.PlayerFacingKey(code, 500);
                if (key == "circle.error.server" && code != "SERVER_ERROR") unmapped.Add(code);
            }

            Assert.That(unmapped, Is.Empty,
                "these server codes reach the player as a generic server error: " + string.Join(", ", unmapped));
        }

        private static List<string> ScrapeClanCodes()
        {
            var found = new SortedSet<string>(System.StringComparer.Ordinal);
            var roots = new[]
            {
                Path.Combine(Repo, "api", "_lib"),
                Path.Combine(Repo, "api", "clan"),
            };
            var rx = new Regex("'(CLAN_[A-Z_]+)'");
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.GetFiles(root, "*.js", SearchOption.AllDirectories))
                {
                    if (!Path.GetFileName(file).StartsWith("clan") &&
                        !file.Replace('\\', '/').Contains("/api/clan/")) continue;
                    foreach (Match m in rx.Matches(File.ReadAllText(file)))
                    {
                        found.Add(m.Groups[1].Value);
                    }
                }
            }
            return new List<string>(found);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // The distinctions that are deliberate, and would be invisible if collapsed
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void a_malformed_code_and_a_code_nobody_holds_are_two_different_sentences()
        {
            // api/clan/join.js:7-10 separates these ON PURPOSE: "that is not a code" and
            // "no Circle has that code" are different things for a player to do next.
            Assert.That(CircleScreenVM.PlayerFacingKey("CLAN_BAD_CODE", 400),
                Is.EqualTo("circle.error.badCode"));
            Assert.That(CircleScreenVM.PlayerFacingKey("CLAN_NOT_FOUND", 404),
                Is.EqualTo("circle.error.noSuchCode"));
        }

        [Test]
        public void the_create_form_names_the_field_that_was_wrong()
        {
            Assert.That(CircleScreenVM.PlayerFacingKey("CLAN_BAD_NAME", 400), Is.EqualTo("circle.error.badName"));
            Assert.That(CircleScreenVM.PlayerFacingKey("CLAN_BAD_TAG", 400), Is.EqualTo("circle.error.badTag"));
            Assert.That(CircleScreenVM.PlayerFacingKey("CLAN_BAD_JOIN_POLICY", 400),
                Is.EqualTo("circle.error.badPolicy"));
            Assert.That(CircleScreenVM.PlayerFacingKey("CLAN_ALREADY_IN_CLAN", 409),
                Is.EqualTo("circle.error.alreadyInCircle"));
        }

        [Test]
        public void every_auth_rail_refusal_reads_the_same_to_a_player()
        {
            // A player cannot act on "your nonce was replayed". They can act on "sign in".
            foreach (var code in new[]
            {
                "AUTH_HEADERS_MISSING", "AUTH_WALLET_MALFORMED", "AUTH_WALLET_MISMATCH",
                "AUTH_BAD_SIGNATURE", "AUTH_CRYPTO_UNAVAILABLE", "AUTH_NONCE_UNKNOWN",
                "AUTH_NONCE_WRONG_WALLET", "AUTH_NONCE_REPLAYED", "AUTH_NONCE_EXPIRED",
                "AUTH_SESSION_UNKNOWN", "AUTH_SESSION_EXPIRED", "AUTH_SESSION_WRONG_WALLET",
                "AUTH_SESSION_MALFORMED", "AUTH_WALLET_REQUIRED", "GUEST_HEADER_MISSING",
                "GUEST_MISMATCH", "GUEST_DISABLED", "GOOGLE_IDENTITY_DISABLED",
            })
            {
                Assert.That(CircleScreenVM.PlayerFacingKey(code, 401), Is.EqualTo("circle.error.notSignedIn"),
                    code + " must read as a sign-in prompt, not as cryptography");
            }
        }

        [Test]
        public void the_name_rails_own_codes_land_on_the_name_step_not_on_a_generic_error()
        {
            // The lead ruling reuses api/profile/username.js, so these are the codes the
            // claim step actually meets — and "that name is taken" is the common one.
            Assert.That(CircleScreenVM.PlayerFacingKey("USERNAME_TOO_SHORT", 200),
                Is.EqualTo("remnant.name.error.length"));
            Assert.That(CircleScreenVM.PlayerFacingKey("USERNAME_TOO_LONG", 200),
                Is.EqualTo("remnant.name.error.length"));
            Assert.That(CircleScreenVM.PlayerFacingKey("USERNAME_INVALID_CHARS", 200),
                Is.EqualTo("remnant.name.error.chars"));
            Assert.That(CircleScreenVM.PlayerFacingKey("USERNAME_TAKEN", 200),
                Is.EqualTo("remnant.name.error.taken"));
            Assert.That(CircleScreenVM.PlayerFacingKey("USERNAME_REJECTED", 200),
                Is.EqualTo("remnant.name.error.rejected"));
        }

        [Test]
        public void the_vault_signer_refusals_never_reach_the_player_as_a_wallet_address()
        {
            // api/clan/vault/register.js:21-29 names a signer wallet in its error body. The
            // client maps BOTH signer refusals to ONE key and renders no address — the
            // standing copy gate at register.js:130-146.
            Assert.That(CircleScreenVM.PlayerFacingKey("CLAN_VAULT_SIGNER_NO_SGT", 400),
                Is.EqualTo("circle.error.vault.signer"));
            Assert.That(CircleScreenVM.PlayerFacingKey("CLAN_VAULT_SIGNER_SGT_REUSED", 400),
                Is.EqualTo("circle.error.vault.signer"));
        }

        [Test]
        public void a_races_leave_is_named_as_a_race_and_not_as_a_refusal_to_leave()
        {
            Assert.That(CircleScreenVM.PlayerFacingKey("leader_must_transfer", 409),
                Is.EqualTo("circle.error.leaveRaced"));
        }

        [Test]
        public void the_status_fallbacks_are_actionable_including_the_transport_zero()
        {
            Assert.That(CircleScreenVM.PlayerFacingKey(null, 0), Is.EqualTo("circle.error.unreachable"),
                "status 0 is 'we could not ask' — never 'you have no Circle'");
            Assert.That(CircleScreenVM.PlayerFacingKey(null, 400), Is.EqualTo("circle.error.badRequest"));
            Assert.That(CircleScreenVM.PlayerFacingKey(null, 401), Is.EqualTo("circle.error.notSignedIn"));
            Assert.That(CircleScreenVM.PlayerFacingKey(null, 403), Is.EqualTo("circle.error.notSignedIn"));
            Assert.That(CircleScreenVM.PlayerFacingKey(null, 429), Is.EqualTo("circle.error.rateLimited"));
            Assert.That(CircleScreenVM.PlayerFacingKey(null, 500), Is.EqualTo("circle.error.server"));
            Assert.That(CircleScreenVM.PlayerFacingKey(string.Empty, 503), Is.EqualTo("circle.error.server"));
        }

        [Test]
        public void every_key_this_table_can_return_is_a_key_and_never_a_sentence()
        {
            // A sentence here would bypass the locale catalogs entirely — the localization
            // law. Every return must look like a key: dotted, ASCII, no spaces.
            var rx = new Regex("^[a-zA-Z][a-zA-Z0-9.]*$");
            foreach (var code in ScrapeClanCodes())
            {
                string key = CircleScreenVM.PlayerFacingKey(code, 400);
                Assert.That(rx.IsMatch(key), Is.True, code + " mapped to a non-key value: " + key);
                Assert.That(key.StartsWith("circle.") || key.StartsWith("remnant."), Is.True,
                    code + " mapped outside this screen's namespaces: " + key);
            }
        }
    }
}
