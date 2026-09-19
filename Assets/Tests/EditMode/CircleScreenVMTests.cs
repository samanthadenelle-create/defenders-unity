// =============================================================================
// CircleScreenVMTests (EditMode) — WO-1870 Lane A.
// -----------------------------------------------------------------------------
// No scene, no network: CircleScreenVM holds no UnityEngine types and takes every body
// as a string through its ISource seam, so the whole screen is driven here with the
// routes' OWN documented response shapes (read at source from api/clan/me.js,
// api/_lib/clan-vigil.js, api/_lib/clan-ballot.js) rather than with invented JSON.
//
// The four owner rulings each have a case, because a ruling honoured only by prose is a
// ruling that comes back:
//   ruling 2 — the name step is entered FROM Loading and is the first thing a nameless
//              player sees; the length rule is the SERVER's.
//   ruling 3 — BallotOptionRowVM.EffectText is empty on every option, always.
//   ruling 4 — CanSubmitJoin is the code SHAPE and nothing else.
// =============================================================================

using System;
using System.Collections.Generic;
using NUnit.Framework;
using DeNelle.Core.UI;
using DeNelle.HUD;

namespace DeNelle.Tests.EditMode
{
    [TestFixture]
    public class CircleScreenVMTests
    {
        private const string Wallet = "7QxKq4kG9rCcJ7ENfZ1dW8hLmPz3TaY6bVuNdR2sXeFL";
        private const string Other = "9ZbHk2mN4pQrSt6UvWx8YzA1BcD3EfG5HjK7LmN9PqRs";
        private const string ClanId = "3f6b1c2e-4a5d-4f7b-9c8e-0d1a2b3c4d5e";

        // ── the fake rail ─────────────────────────────────────────────────────
        private sealed class FakeSource : CircleScreenVM.ISource
        {
            public string Wallet = CircleScreenVMTests.Wallet;
            public bool Guest;

            public string NamesBody = "{\"ok\":true,\"names\":[]}";
            public long NamesStatus = 200;
            public string MeBody = "{\"ok\":true,\"clan\":null}";
            public long MeStatus = 200;
            public string VigilBody = "{\"ok\":true,\"members\":[]}";
            public long VigilStatus = 200;
            public string LeaderboardBody = "{\"ok\":true,\"clans\":[]}";
            public long LeaderboardStatus = 200;
            public string BallotBody = "{\"ok\":true}";
            public long BallotStatus = 200;
            public string WriteBody = "{\"ok\":true}";
            public long WriteStatus = 200;

            public readonly List<string> Calls = new List<string>();
            public string LastCopied;
            public int ChatOpens;

            /// <summary>WO-1875. Default null so existing tests stay Unreachable on http=0.</summary>
            public string AttachWhy;
            public string SignInBody = "{\"ok\":true}";
            public long SignInStatus = 200;

#pragma warning disable 0067
            public event Action Changed;
#pragma warning restore 0067

            public string WalletAddress { get { return Wallet; } }
            public bool IsGuest { get { return Guest; } }
            public string LastAttachWhy { get { return AttachWhy; } }

            public void FetchMe(Action<string, long> done) { Calls.Add("me"); done(MeBody, MeStatus); }
            public void FetchVigil(Action<string, long> done) { Calls.Add("vigil"); done(VigilBody, VigilStatus); }
            public void FetchLeaderboard(int limit, Action<string, long> done)
            { Calls.Add("leaderboard"); done(LeaderboardBody, LeaderboardStatus); }
            public void FetchBallot(Action<string, long> done) { Calls.Add("ballot"); done(BallotBody, BallotStatus); }
            public void FetchDisplayNames(string[] playerIds, Action<string, long> done)
            { Calls.Add("names"); done(NamesBody, NamesStatus); }

            public void SetDisplayName(string name, Action<string, long> done)
            { Calls.Add("setName:" + name); done(WriteBody, WriteStatus); }
            public void Create(string name, string tag, string joinPolicy, Action<string, long> done)
            { Calls.Add("create:" + name + ":" + tag + ":" + joinPolicy); done(WriteBody, WriteStatus); }
            public void Join(string code, Action<string, long> done)
            { Calls.Add("join:" + code); done(WriteBody, WriteStatus); }
            public void Leave(Action<string, long> done) { Calls.Add("leave"); done(WriteBody, WriteStatus); }
            public void Promote(string t, Action<string, long> done)
            { Calls.Add("promote:" + t); done(WriteBody, WriteStatus); }
            public void Demote(string t, Action<string, long> done)
            { Calls.Add("demote:" + t); done(WriteBody, WriteStatus); }
            public void Kick(string t, Action<string, long> done)
            { Calls.Add("kick:" + t); done(WriteBody, WriteStatus); }
            public void ProposeBallot(int tier, string[] optionIds, Action<string, long> done)
            {
                Calls.Add("propose:" + tier + ":" + string.Join(",", optionIds ?? new string[0]));
                done(BallotBody, BallotStatus);
            }
            public void Vote(string ballotId, string optionId, Action<string, long> done)
            { Calls.Add("vote:" + optionId); done(BallotBody, BallotStatus); }
            public void RegisterVault(string a, int i, Action<string, long> done)
            { Calls.Add("vault:" + a + ":" + i); done(WriteBody, WriteStatus); }

            public void CopyToClipboard(string text) { LastCopied = text; }
            public void OpenChat() { ChatOpens++; }
            public void SignIn(Action<string, long> done)
            {
                Calls.Add("signin");
                done(SignInBody, SignInStatus);
            }
        }

        private static string NamesFor(params string[] pairs)
        {
            // pairs: id, name, id, name...
            var sb = new System.Text.StringBuilder("{\"ok\":true,\"names\":[");
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"playerId\":\"").Append(pairs[i])
                  .Append("\",\"username\":\"").Append(pairs[i + 1]).Append("\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string MeInClan(string role)
        {
            return "{\"ok\":true,\"clan\":{\"clanId\":\"" + ClanId + "\",\"code\":\"ABCDEF\"," +
                   "\"name\":\"The Long Watch\",\"tag\":\"TLW\",\"joinPolicy\":\"open\"," +
                   "\"createdAt\":\"2026-01-01T00:00:00Z\",\"memberCount\":2}," +
                   "\"role\":\"" + role + "\",\"joinedAt\":\"2026-02-02T00:00:00Z\"}";
        }

        private static CircleScreenVM Build(FakeSource src)
        {
            return new CircleScreenVM(src, () => { });
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Identity
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void no_wallet_is_a_state_not_a_round_trip()
        {
            var src = new FakeSource { Wallet = null };
            var vm = Build(src);

            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.NoWallet));
            Assert.That(src.Calls, Is.Empty,
                "every clan route is wallet-only, so a missing identity must not cost a guaranteed 401");
        }

        [Test]
        public void a_guest_identity_is_not_a_wallet_for_this_screen()
        {
            var src = new FakeSource { Guest = true };
            var vm = Build(src);

            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.NoWallet));
            Assert.That(src.Calls, Is.Empty);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // RULING 2 — the name comes first
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void a_player_with_no_name_sees_the_claim_step_before_anything_else()
        {
            var src = new FakeSource();     // names answers an EMPTY list
            var vm = Build(src);

            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.NeedsName));
            Assert.That(vm.HasDisplayName, Is.False);
            Assert.That(src.Calls, Is.EqualTo(new List<string> { "names" }),
                "the name lookup is the FIRST read, and /me is not asked until it answers");
            Assert.That(vm.MyNameFallbackText, Is.EqualTo("7QxK...XeFL"),
                "and until a name exists the player is shown a truncated id, never a blank");
        }

        [Test]
        public void a_named_player_goes_straight_on_to_the_circle()
        {
            var src = new FakeSource { NamesBody = NamesFor(Wallet, "SallyTheWise") };
            var vm = Build(src);

            Assert.That(vm.HasDisplayName, Is.True);
            Assert.That(vm.MyDisplayName, Is.EqualTo("SallyTheWise"));
            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.NotInCircle));
            Assert.That(src.Calls, Is.EqualTo(new List<string> { "names", "me" }));
        }

        [Test]
        public void a_name_lookup_that_could_not_be_answered_is_never_read_as_a_nameless_player()
        {
            // The nastiest form of the hold-previous rule: telling an OFFLINE player to claim
            // a name they already hold, and then offering to rename them.
            var src = new FakeSource { NamesStatus = 0, NamesBody = null };
            var vm = Build(src);

            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.Unreachable));
            Assert.That(vm.ErrorKey, Is.EqualTo("circle.error.unreachable"));
            Assert.That(src.Calls.Contains("me"), Is.False, "and the screen does not push on past it");

            // And the check is re-armed: the very next refresh asks again rather than
            // skipping the name for the life of the VM.
            src.NamesStatus = 200;
            src.NamesBody = NamesFor(Wallet, "SallyTheWise");
            vm.RefreshAll();
            Assert.That(vm.HasDisplayName, Is.True);
            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.NotInCircle));
        }

        [Test]
        public void a_named_player_can_back_out_of_a_rename_but_a_nameless_one_cannot_skip_the_door()
        {
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "SallyTheWise"),
                MeBody = MeInClan("member"),
            };
            var vm = Build(src);
            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.InCircle));

            vm.EditDisplayName();
            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.NeedsName));
            vm.CancelEditName();
            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.InCircle),
                "a rename the player thought better of must have a way out");

            var nameless = Build(new FakeSource());
            Assert.That(nameless.State, Is.EqualTo(CircleScreenVM.CircleState.NeedsName));
            nameless.CancelEditName();
            Assert.That(nameless.State, Is.EqualTo(CircleScreenVM.CircleState.NeedsName),
                "for a player with no name the claim step is not a detour — it is the door");
        }

        [Test]
        public void the_shown_name_is_composed_from_the_first_name_and_the_circle()
        {
            // The player types ONE token; the game supplies the rest. Both connectives are
            // locale keys, so with no catalogue loaded LocalText answers "[[missing:<key>]]" —
            // which is exactly what proves the VM went through the table instead of
            // concatenating an English word in C#.
            string withCircle = CircleScreenVM.ComposeDisplayName("Bob", "RiverRun");
            string alone = CircleScreenVM.ComposeDisplayName("Bob", null);

            Assert.That(withCircle, Is.EqualTo(LocalText.Format("remnant.name.composed", "Bob", "RiverRun")));
            Assert.That(alone, Is.EqualTo(LocalText.Format("remnant.name.alone", "Bob")));
            Assert.That(withCircle, Is.Not.EqualTo(alone),
                "holding a Circle and holding none are two different names, never the same string");
            Assert.That(alone, Is.Not.EqualTo("Bob"),
                "a Circle-less Remnant is 'Bob the Lonely', never the bare first name");
            Assert.That(CircleScreenVM.ComposeDisplayName("Bob", "   "), Is.EqualTo(alone),
                "whitespace is not a Circle");
            Assert.That(CircleScreenVM.ComposeDisplayName(null, "RiverRun"), Is.EqualTo(string.Empty));
        }

        [Test]
        public void joining_and_leaving_recompose_every_name_on_screen()
        {
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "Bob", Other, "Maren"),
                MeBody = MeInClan("leader"),
                VigilBody = "{\"ok\":true,\"members\":[" +
                            "{\"wallet\":\"" + Wallet + "\",\"role\":\"leader\",\"tenure_seconds\":0,\"degraded\":false}," +
                            "{\"wallet\":\"" + Other + "\",\"role\":\"member\",\"tenure_seconds\":0,\"degraded\":false}]}",
            };
            var vm = Build(src);

            Assert.That(vm.MyDisplayName, Is.EqualTo("Bob"), "the RAW token is what the rename field edits");
            Assert.That(vm.MyDisplayNameText,
                Is.EqualTo(CircleScreenVM.ComposeDisplayName("Bob", "The Long Watch")),
                "and the composed text is what the header shows — two different properties on purpose");
            Assert.That(vm.Members[1].DisplayNameText,
                Is.EqualTo(CircleScreenVM.ComposeDisplayName("Maren", "The Long Watch")),
                "every row on one roster shares one Circle, so the second half is applied once");

            // Now leave: the same two people must stop naming a Circle they are not in.
            src.MeBody = "{\"ok\":true,\"clan\":null}";
            vm.RefreshAll();
            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.NotInCircle));
            Assert.That(vm.MyDisplayNameText, Is.EqualTo(CircleScreenVM.ComposeDisplayName("Bob", null)),
                "a stale Circle left on a name is a member row that names the wrong Circle");
        }

        [Test]
        public void a_member_with_no_claimed_name_is_never_composed()
        {
            // "7QxK...XeFL of The Long Watch" reads as a bug, not as a member. The truncated
            // id stands alone, with no connective and no epithet.
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "Bob"),
                MeBody = MeInClan("member"),
                VigilBody = "{\"ok\":true,\"members\":[{\"wallet\":\"" + Other +
                            "\",\"role\":\"member\",\"tenure_seconds\":0,\"degraded\":false}]}",
            };
            var vm = Build(src);

            Assert.That(vm.Members[0].HasDisplayName, Is.False);
            Assert.That(vm.Members[0].DisplayNameText, Is.EqualTo("9ZbH...PqRs"),
                "the wallet-truncated fallback stands on its own");
            Assert.That(vm.DisplayNameFor(Other), Is.EqualTo("9ZbH...PqRs"));
        }

        [Test]
        public void the_name_rule_is_the_servers_rule_not_the_work_orders()
        {
            // api/_lib/username-policy.js:16-21 — 3..16 of [A-Za-z0-9_], UNCHANGED. The player
            // types a FIRST NAME only, so a single token is the whole requirement and the
            // button must never promise what the route will refuse.
            Assert.That(CircleScreenVM.IsValidName("ab"), Is.False, "below MIN_LEN");
            Assert.That(CircleScreenVM.IsValidName("Bob"), Is.True);
            Assert.That(CircleScreenVM.IsValidName("Sally_The_Wise1"), Is.True);
            Assert.That(CircleScreenVM.IsValidName("SallyTheVeryWisest"), Is.False, "above MAX_LEN");
            Assert.That(CircleScreenVM.IsValidName("Sally the Wise"), Is.False,
                "a space is not a first name — the epithet and the Circle are the GAME's half");
            Assert.That(CircleScreenVM.IsValidName("Sally!"), Is.False, "punctuation is not allowed");
        }

        [Test]
        public void claiming_a_name_moves_the_screen_on_without_a_second_lookup()
        {
            var src = new FakeSource { WriteBody = "{\"success\":true,\"username\":\"Hornsent\",\"wasRename\":false}" };
            var vm = Build(src);
            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.NeedsName));

            vm.SetNameDraft("Hornsent");
            Assert.That(vm.CanSubmitName, Is.True);
            vm.SubmitDisplayName();

            Assert.That(vm.MyDisplayName, Is.EqualTo("Hornsent"));
            Assert.That(vm.HasDisplayName, Is.True);
            Assert.That(vm.ToastKey, Is.EqualTo("remnant.name.saved"));
            Assert.That(src.Calls.Contains("setName:Hornsent"), Is.True);
            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.NotInCircle),
                "the screen continues to the Circle itself once the name exists");
        }

        [Test]
        public void a_taken_name_is_a_200_business_failure_and_still_reads_as_a_refusal()
        {
            // api/profile/username.js answers 200 { success:false, error:'USERNAME_TAKEN' } —
            // NOT the clan routes' { ok:false, code }. One reader covers both envelopes.
            var src = new FakeSource { WriteBody = "{\"success\":false,\"error\":\"USERNAME_TAKEN\"}" };
            var vm = Build(src);

            vm.SetNameDraft("Hornsent");
            vm.SubmitDisplayName();

            Assert.That(vm.HasDisplayName, Is.False, "a refused rename must not look like a claimed name");
            Assert.That(vm.ErrorKey, Is.EqualTo("remnant.name.error.taken"));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // The header
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void a_clan_body_fills_the_header_and_the_role_is_a_word_not_a_colour()
        {
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "SallyTheWise"),
                MeBody = MeInClan("leader"),
            };
            var vm = Build(src);

            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.InCircle));
            Assert.That(vm.CircleId, Is.EqualTo(ClanId));
            Assert.That(vm.CircleName, Is.EqualTo("The Long Watch"));
            Assert.That(vm.CircleTag, Is.EqualTo("TLW"));
            Assert.That(vm.CircleCode, Is.EqualTo("ABCDEF"));
            Assert.That(vm.JoinPolicyLabelKey, Is.EqualTo("circle.policy.open"));
            Assert.That(vm.MyRoleLabelKey, Is.EqualTo("circle.role.leader"));
            Assert.That(vm.IsLeader, Is.True);
            Assert.That(vm.IsOfficer, Is.False);
        }

        [Test]
        public void no_clan_is_a_real_success_and_a_transport_failure_is_not()
        {
            var src = new FakeSource { NamesBody = NamesFor(Wallet, "SallyTheWise") };
            var vm = Build(src);
            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.NotInCircle),
                "{ok:true, clan:null} is a deliberate answer, not an error");

            // Now the same screen loses the network: the state must say UNREACHABLE and the
            // player must NOT be told they have no Circle.
            src.MeStatus = 0;
            src.MeBody = null;
            vm.RefreshAll();

            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.Unreachable));
            Assert.That(vm.ErrorKey, Is.EqualTo("circle.error.unreachable"));
        }

        [Test]
        public void copying_the_code_goes_through_the_seam_and_toasts()
        {
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "SallyTheWise"),
                MeBody = MeInClan("member"),
            };
            var vm = Build(src);

            vm.CopyCode();

            Assert.That(src.LastCopied, Is.EqualTo("ABCDEF"));
            Assert.That(vm.ToastKey, Is.EqualTo("circle.toast.codeCopied"));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // RULING 4 — joining is never stake-gated
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void the_join_code_shape_alone_decides_whether_join_is_pressable()
        {
            var src = new FakeSource { NamesBody = NamesFor(Wallet, "SallyTheWise") };
            var vm = Build(src);

            vm.SetJoinCode("abcdef");
            Assert.That(vm.JoinCode, Is.EqualTo("ABCDEF"), "the VM upper-cases what the player typed");
            Assert.That(vm.CanSubmitJoin, Is.True);

            vm.SetJoinCode("ABC");
            Assert.That(vm.CanSubmitJoin, Is.False, "six characters, no more and no fewer");

            vm.SetJoinCode("ABCDE0");
            Assert.That(vm.CanSubmitJoin, Is.False, "0, 1, I, L and O are excluded from the alphabet");

            vm.SetJoinCode("ABCDEF");
            vm.SubmitJoin();
            Assert.That(src.Calls.Contains("join:ABCDEF"), Is.True,
                "nothing but the code stands between the player and the Circle");
        }

        [Test]
        public void create_is_pressable_on_the_schemas_own_lengths()
        {
            var src = new FakeSource { NamesBody = NamesFor(Wallet, "SallyTheWise") };
            var vm = Build(src);

            vm.SetCreateName("The Long Watch");
            Assert.That(vm.CanSubmitCreate, Is.False, "a tag is required too");
            vm.SetCreateTag("tlw");
            Assert.That(vm.CreateTag, Is.EqualTo("TLW"));
            Assert.That(vm.CanSubmitCreate, Is.True);

            vm.SetCreateTag("TOOLONG");
            Assert.That(vm.CanSubmitCreate, Is.False, "clans.tag is 1..5");

            vm.SetCreateTag("TLW");
            vm.ToggleCreateOpenJoin();
            vm.SubmitCreate();
            Assert.That(src.Calls.Contains("create:The Long Watch:TLW:open"), Is.True);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Members — fed by /api/clan/vigil, not by /me
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void members_come_from_the_vigil_roster_and_carry_the_right_verbs()
        {
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "SallyTheWise", Other, "HornsentTheWicked"),
                MeBody = MeInClan("leader"),
                VigilBody = "{\"ok\":true,\"vigil_weight\":1.5,\"member_count\":2,\"degraded\":false," +
                            "\"members\":[" +
                            "{\"wallet\":\"" + Wallet + "\",\"role\":\"leader\",\"tenure_seconds\":172800,\"degraded\":false}," +
                            "{\"wallet\":\"" + Other + "\",\"role\":\"member\",\"tenure_seconds\":86400,\"degraded\":true}]}",
            };
            var vm = Build(src);

            Assert.That(src.Calls.Contains("vigil"), Is.True,
                "/me carries only a memberCount — the roster is the vigil route's");
            Assert.That(vm.Members.Count, Is.EqualTo(2));

            var me = vm.Members[0];
            Assert.That(me.IsMe, Is.True);
            Assert.That(me.DisplayNameText, Is.EqualTo("SallyTheWise"));
            Assert.That(me.CanKick, Is.False, "a leader cannot kick themselves — that is a 400 CLAN_SELF_TARGET");
            Assert.That(me.CanPromote, Is.False);

            var them = vm.Members[1];
            Assert.That(them.DisplayNameText, Is.EqualTo("HornsentTheWicked"));
            Assert.That(them.RoleLabelKey, Is.EqualTo("circle.role.member"));
            Assert.That(them.CanPromote, Is.True);
            Assert.That(them.CanDemote, Is.False, "there is nothing to demote a member to");
            Assert.That(them.CanKick, Is.True);
            Assert.That(them.DegradedLabelKey, Is.EqualTo("circle.member.degraded"),
                "degraded is a WORD on the row — the owner is red/green colourblind");

            them.Kick();
            Assert.That(src.Calls.Contains("kick:" + Other), Is.True);
        }

        [Test]
        public void a_member_without_a_name_falls_back_to_a_short_id_never_a_blank()
        {
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "SallyTheWise"),
                MeBody = MeInClan("member"),
                VigilBody = "{\"ok\":true,\"members\":[{\"wallet\":\"" + Other +
                            "\",\"role\":\"member\",\"tenure_seconds\":0,\"degraded\":false}]}",
            };
            var vm = Build(src);

            Assert.That(vm.Members.Count, Is.EqualTo(1));
            Assert.That(vm.Members[0].HasDisplayName, Is.False);
            Assert.That(vm.Members[0].DisplayNameText, Is.EqualTo("9ZbH...PqRs"));
        }

        [Test]
        public void an_unreachable_tab_says_so_rather_than_blanking()
        {
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "SallyTheWise"),
                MeBody = MeInClan("member"),
                VigilStatus = 0,
                VigilBody = null,
            };
            var vm = Build(src);

            Assert.That(vm.MembersEmptyKey, Is.EqualTo("circle.tab.unreachable"));
            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.InCircle),
                "a failed tab read must not throw the player out of their Circle");
        }

        [Test]
        public void the_vigil_route_answers_not_in_clan_as_a_404_and_the_vm_reads_it_as_a_state()
        {
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "SallyTheWise"),
                MeBody = MeInClan("member"),
                VigilStatus = 404,
                VigilBody = "{\"ok\":false,\"code\":\"CLAN_NOT_IN_CLAN\",\"ref\":\"abcd\"}",
            };
            var vm = Build(src);

            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.NotInCircle));
            Assert.That(vm.ErrorKey, Is.Null, "the same fact in a different shape is not an error");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Vault — read-only, rides the vigil body
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void the_vault_block_rides_the_vigil_body_and_renders_words_not_colours()
        {
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "SallyTheWise"),
                MeBody = MeInClan("leader"),
                VigilBody = "{\"ok\":true,\"members\":[],\"vault\":{\"vault_address\":\"" + Other +
                            "\",\"multisig_address\":\"" + Wallet + "\",\"vault_index\":3,\"threshold\":2," +
                            "\"signer_count\":3,\"verified_signer_count\":2,\"signer_wallets\":[\"" + Wallet +
                            "\"],\"hardware_backed\":true,\"verified_at\":\"2026-03-03T00:00:00Z\"}}",
            };
            var vm = Build(src);
            vm.SelectTab(CircleScreenVM.CircleTab.Vault);

            Assert.That(vm.HasVault, Is.True);
            Assert.That(vm.VaultRows.Count, Is.EqualTo(6));
            Assert.That(vm.VaultRows[0].LabelKey, Is.EqualTo("circle.vault.address"));
            Assert.That(vm.VaultRows[2].ValueText, Is.EqualTo("2 / 3"));
            Assert.That(vm.VaultSignerShortTexts.Count, Is.EqualTo(1));
            Assert.That(vm.CanRegisterVault, Is.False, "a registered vault cannot be registered again");
        }

        [Test]
        public void only_a_leader_without_a_vault_is_offered_the_register_form()
        {
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "SallyTheWise"),
                MeBody = MeInClan("member"),
                VigilBody = "{\"ok\":true,\"members\":[]}",
            };
            var vm = Build(src);
            Assert.That(vm.CanRegisterVault, Is.False,
                "the route answers 403 CLAN_VAULT_NOT_LEADER, so the form is never offered");
            Assert.That(vm.VaultEmptyKey, Is.EqualTo("circle.vault.none"));

            src.MeBody = MeInClan("leader");
            vm.RefreshAll();
            Assert.That(vm.CanRegisterVault, Is.True);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // RULING 3 — ballots carry narrative, never a stat magnitude
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void ballot_options_render_the_narrative_and_never_the_effect()
        {
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "SallyTheWise"),
                MeBody = MeInClan("leader"),
                // The server really does put a magnitude on the wire — this body is its
                // shape, verbatim, so the suppression is proven against the real thing.
                BallotBody = "{\"ok\":true,\"vigil_weight\":2.0,\"vigil_degraded\":false," +
                             "\"epoch\":{\"index\":4,\"ends_at\":\"2026-04-04T00:00:00Z\"}," +
                             "\"tiers\":[{\"tier\":1,\"threshold\":1.0,\"unlocked\":true}," +
                             "{\"tier\":2,\"threshold\":9.0,\"unlocked\":false}]," +
                             "\"ballot\":{\"ballot_id\":\"b-1\",\"tier\":1,\"closed\":false," +
                             "\"options\":[{\"option_id\":\"build_speed_1\",\"title\":\"Steady Hands\"," +
                             "\"effect\":\"+5% build speed\",\"description\":\"The old builders return.\"," +
                             "\"weight\":2.0,\"voters\":1}],\"my_vote\":\"build_speed_1\"," +
                             "\"turnout\":{\"voters\":1,\"member_count\":2,\"clears\":false}}}",
            };
            var vm = Build(src);
            vm.SelectTab(CircleScreenVM.CircleTab.Ballots);

            Assert.That(vm.HasBallot, Is.True);
            Assert.That(vm.BallotOptions.Count, Is.EqualTo(1));
            var option = vm.BallotOptions[0];
            Assert.That(option.TitleText, Is.EqualTo("Steady Hands"));
            Assert.That(option.DescriptionText, Is.EqualTo("The old builders return."));
            Assert.That(option.EffectText, Is.EqualTo(string.Empty),
                "RULING 3: the client never carries a stat magnitude, even when the server sends one");
            Assert.That(option.IsMyVote, Is.True);
            Assert.That(option.MyVoteLabelKey, Is.EqualTo("circle.ballot.yourVote"));
            Assert.That(vm.BallotTiers.Count, Is.EqualTo(2));
            Assert.That(vm.BallotTiers[1].LockedLabelKey, Is.EqualTo("circle.ballot.locked"));
            Assert.That(vm.BallotTiers[0].CanPropose, Is.False, "a ballot is already open");
        }

        [Test]
        public void a_closed_ballot_explains_WHY_it_closed_and_the_two_failures_are_not_one()
        {
            // The server answers exactly three reasons (api/_lib/clan-ballot.js:623-625).
            // "nobody voted" and "not enough of you voted" are different things to tell a
            // Circle: one is a nudge to vote, the other says the votes cast were real but too
            // few. One shared "failed" sentence throws away the only actionable half.
            Assert.That(CircleScreenVM.ResultKeyFor("passed"), Is.EqualTo("circle.ballot.result.passed"));
            Assert.That(CircleScreenVM.ResultKeyFor("no_votes"), Is.EqualTo("circle.ballot.result.noVotes"));
            Assert.That(CircleScreenVM.ResultKeyFor("insufficient_turnout"),
                Is.EqualTo("circle.ballot.result.insufficientTurnout"));
            Assert.That(CircleScreenVM.ResultKeyFor("some_reason_added_later"),
                Is.EqualTo("circle.ballot.result.failed"),
                "an unfamiliar reason is still a failure the server reported — only the why is unknown");
            Assert.That(CircleScreenVM.ResultKeyFor(null), Is.EqualTo("circle.ballot.result.unknown"));
            Assert.That(CircleScreenVM.ResultKeyFor(string.Empty), Is.EqualTo("circle.ballot.result.unknown"));

            // And the same mapping through a real closed-ballot body, so the wiring is
            // pinned and not just the helper.
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "Bob"),
                MeBody = MeInClan("member"),
                BallotBody = "{\"ok\":true,\"tiers\":[]," +
                             "\"ballot\":{\"ballot_id\":\"b-9\",\"tier\":1,\"closed\":true," +
                             "\"options\":[{\"option_id\":\"o1\",\"title\":\"Steady Hands\"}]}," +
                             "\"result\":{\"closed\":true,\"passed\":false," +
                             "\"reason\":\"insufficient_turnout\",\"winning_option\":\"o1\"}}",
            };
            var vm = Build(src);
            vm.SelectTab(CircleScreenVM.CircleTab.Ballots);

            Assert.That(vm.HasResult, Is.True);
            Assert.That(vm.ResultPassed, Is.False);
            Assert.That(vm.ResultReasonKey, Is.EqualTo("circle.ballot.result.insufficientTurnout"),
                "a failed ballot must not be flattened to the generic failure key");
            Assert.That(vm.ResultWinningOptionTitleText, Is.EqualTo("Steady Hands"),
                "the winning option is named by its TITLE, never by its magnitude");
        }

        [Test]
        public void no_ballot_is_an_empty_state_keyed_on_an_empty_id_not_on_a_null()
        {
            // JsonUtility materialises a null object as a DEFAULT INSTANCE, so "no ballot"
            // can only ever be decided by an EMPTY ballot_id.
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "SallyTheWise"),
                MeBody = MeInClan("member"),
                BallotBody = "{\"ok\":true,\"ballot\":null,\"result\":null,\"epoch\":null," +
                             "\"tiers\":[{\"tier\":1,\"threshold\":1.0,\"unlocked\":true,\"options\":[" +
                             "{\"option_id\":\"build_speed_1\",\"title\":\"Steady Hands\"," +
                             "\"effect\":\"+5% build speed\",\"description\":\"The old builders return.\"}," +
                             "{\"option_id\":\"wall_hp_1\",\"title\":\"Deep Footings\"," +
                             "\"effect\":\"+5% wall HP\",\"description\":\"The stones remember.\"}]}]}",
            };
            var vm = Build(src);
            vm.SelectTab(CircleScreenVM.CircleTab.Ballots);

            Assert.That(vm.HasBallot, Is.False);
            Assert.That(vm.BallotsEmptyKey, Is.EqualTo("circle.ballot.none"));
            Assert.That(vm.BallotTiers[0].CanPropose, Is.True);
            Assert.That(vm.BallotTiers[0].CatalogOptions.Count, Is.EqualTo(2),
                "the tier row publishes the slate a proposal would open");
        }

        [Test]
        public void proposing_sends_the_servers_own_tier_catalogue_and_never_an_empty_slate()
        {
            // api/_lib/clan-ballot.js:265 refuses an empty optionIds array with 400
            // CLAN_BALLOT_BAD_OPTIONS, and :266-271 requires every id to belong to the tier.
            // A propose that sent [] would be a button that fails every single time.
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "SallyTheWise"),
                MeBody = MeInClan("leader"),
                BallotBody = "{\"ok\":true,\"ballot\":null," +
                             "\"tiers\":[{\"tier\":1,\"threshold\":1.0,\"unlocked\":true,\"options\":[" +
                             "{\"option_id\":\"build_speed_1\",\"title\":\"Steady Hands\"}," +
                             "{\"option_id\":\"wall_hp_1\",\"title\":\"Deep Footings\"}]}," +
                             "{\"tier\":2,\"threshold\":9.0,\"unlocked\":true}]}",
            };
            var vm = Build(src);
            vm.SelectTab(CircleScreenVM.CircleTab.Ballots);

            vm.BallotTiers[0].Propose();
            Assert.That(src.Calls.Contains("propose:1:build_speed_1,wall_hp_1"), Is.True,
                "the client proposes exactly what the server published for that tier");

            // Tier 2 arrived with NO catalogue, so it is not offered and is refused locally
            // rather than sent as a guaranteed 400.
            Assert.That(vm.BallotTiers[1].CanPropose, Is.False);
            int before = src.Calls.Count;
            vm.ProposeTier(2);
            Assert.That(src.Calls.Count, Is.EqualTo(before), "no request is sent for an empty slate");
            Assert.That(vm.ErrorKey, Is.EqualTo("circle.error.ballot.badOptions"));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Leaving, leaderboard, chat
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void leaving_takes_two_presses_and_the_first_one_calls_nothing()
        {
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "SallyTheWise"),
                MeBody = MeInClan("member"),
            };
            var vm = Build(src);
            int before = src.Calls.Count;

            vm.RequestLeave();
            Assert.That(vm.LeaveConfirmArmed, Is.True);
            Assert.That(src.Calls.Count, Is.EqualTo(before), "arming the confirm must call NOTHING");

            vm.CancelLeave();
            Assert.That(vm.LeaveConfirmArmed, Is.False);
            vm.ConfirmLeave();
            Assert.That(src.Calls.Contains("leave"), Is.False, "a cancelled confirm stays cancelled");

            vm.RequestLeave();
            vm.ConfirmLeave();
            Assert.That(src.Calls.Contains("leave"), Is.True);
            Assert.That(vm.ToastKey, Is.EqualTo("circle.toast.left"));
        }

        [Test]
        public void the_leaderboard_marks_your_own_circle_with_a_word()
        {
            var src = new FakeSource
            {
                NamesBody = NamesFor(Wallet, "SallyTheWise"),
                MeBody = MeInClan("member"),
                LeaderboardBody = "{\"ok\":true,\"clans\":[" +
                                  "{\"rank\":1,\"clanId\":\"" + ClanId + "\",\"name\":\"The Long Watch\"," +
                                  "\"tag\":\"TLW\",\"metric\":42.5,\"memberCount\":2}," +
                                  "{\"rank\":2,\"clanId\":\"other-id\",\"name\":\"Ashfall\",\"tag\":\"ASH\"," +
                                  "\"metric\":10,\"memberCount\":1}]}",
            };
            var vm = Build(src);
            vm.SelectTab(CircleScreenVM.CircleTab.Leaderboard);

            Assert.That(vm.Leaderboard.Count, Is.EqualTo(2));
            Assert.That(vm.Leaderboard[0].RankText, Is.EqualTo("#1"));
            Assert.That(vm.Leaderboard[0].MetricText, Is.EqualTo("42.5"));
            Assert.That(vm.Leaderboard[0].IsMine, Is.True);
            Assert.That(vm.Leaderboard[0].MineLabelKey, Is.EqualTo("circle.leaderboard.you"));
            Assert.That(vm.Leaderboard[1].MineLabelKey, Is.EqualTo(string.Empty));
        }

        [Test]
        public void the_chat_door_goes_through_the_seam_so_the_view_never_finds_a_panel()
        {
            var src = new FakeSource { NamesBody = NamesFor(Wallet, "SallyTheWise") };
            var vm = Build(src);

            vm.OpenChat();

            Assert.That(src.ChatOpens, Is.EqualTo(1));
        }

        [Test]
        public void the_four_tabs_exist_in_order_and_exactly_one_is_active()
        {
            var src = new FakeSource { NamesBody = NamesFor(Wallet, "SallyTheWise") };
            var vm = Build(src);

            Assert.That(vm.Tabs.Count, Is.EqualTo(4));
            Assert.That(vm.Tabs[0].LabelKey, Is.EqualTo("circle.tab.members"));
            Assert.That(vm.Tabs[1].LabelKey, Is.EqualTo("circle.tab.leaderboard"));
            Assert.That(vm.Tabs[2].LabelKey, Is.EqualTo("circle.tab.vault"));
            Assert.That(vm.Tabs[3].LabelKey, Is.EqualTo("circle.tab.ballots"));
            Assert.That(vm.Tabs[0].IsActive, Is.True);

            vm.Tabs[3].Activate();
            Assert.That(vm.ActiveTab, Is.EqualTo(CircleScreenVM.CircleTab.Ballots));
            Assert.That(vm.Tabs[3].IsActive, Is.True);
            Assert.That(vm.Tabs[0].IsActive, Is.False);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // WO-1875 — missing session is NotSignedIn, never Unreachable
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void F_a_missing_session_is_not_signed_in_and_sign_in_is_the_enabled_verb()
        {
            var src = new FakeSource { AttachWhy = "missing", NamesStatus = 0, NamesBody = null };
            var vm = Build(src);

            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.NotSignedIn));
            Assert.That(vm.ErrorKey, Is.EqualTo("circle.error.notSignedIn"));
            Assert.That(vm.CanSignIn, Is.True, "SignIn is the enabled verb on this face");
            Assert.That(CircleScreenVM.SignInFaceKey, Is.EqualTo("circle.signIn.face"));
            Assert.That(src.Calls.Contains("me"), Is.False);

            var expired = Build(new FakeSource { AttachWhy = "expired", NamesStatus = 0, NamesBody = null });
            Assert.That(expired.State, Is.EqualTo(CircleScreenVM.CircleState.NotSignedIn),
                "why=expired is the same session gap as missing");
        }

        [Test]
        public void G_a_successful_sign_in_leaves_not_signed_in()
        {
            var src = new FakeSource
            {
                AttachWhy = "missing",
                NamesStatus = 0,
                NamesBody = null,
                SignInStatus = 200,
                SignInBody = "{\"ok\":true}",
            };
            var vm = Build(src);
            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.NotSignedIn));

            src.AttachWhy = null;
            src.NamesStatus = 200;
            src.NamesBody = NamesFor(Wallet, "SallyTheWise");
            src.MeBody = MeInClan("member");
            vm.SignIn();

            Assert.That(src.Calls.Contains("signin"), Is.True);
            Assert.That(vm.State, Is.Not.EqualTo(CircleScreenVM.CircleState.NotSignedIn));
            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.InCircle));
            Assert.That(vm.CanSignIn, Is.False);
        }

        [Test]
        public void H_status_zero_without_a_session_gap_is_unreachable_not_not_signed_in()
        {
            var src = new FakeSource { AttachWhy = null, NamesStatus = 0, NamesBody = null };
            var vm = Build(src);

            Assert.That(vm.State, Is.EqualTo(CircleScreenVM.CircleState.Unreachable));
            Assert.That(vm.ErrorKey, Is.EqualTo("circle.error.unreachable"));
            Assert.That(vm.CanSignIn, Is.False);

            var other = new FakeSource { AttachWhy = "other", NamesStatus = 0, NamesBody = null };
            var otherVm = Build(other);
            Assert.That(otherVm.State, Is.EqualTo(CircleScreenVM.CircleState.Unreachable));
            Assert.That(otherVm.ErrorKey, Is.EqualTo("circle.error.unreachable"));
        }

        [Test]
        public void disposing_detaches_and_silences_the_view()
        {
            var src = new FakeSource { NamesBody = NamesFor(Wallet, "SallyTheWise") };
            var vm = Build(src);
            int raised = 0;
            vm.Changed += () => raised++;

            vm.Dispose();
            vm.RefreshAll();

            Assert.That(raised, Is.EqualTo(0), "a disposed VM must never re-render a dead View");
        }
    }
}
