// =============================================================================
// ClanChatVMTests (EditMode) — §2c gate for the clan-chat slice, WO-1847 shape.
// -----------------------------------------------------------------------------
// ⛔ WHAT THIS FILE STOPPED TESTING, AND WHY THAT IS NOT LOST COVERAGE. Until WO-1847
// it locked ClanChatVM's message projection and phrase-chip rail (dividers, the
// never-blank fallback, the four ClanService write commands). Cherry owns the messages
// now — it renders, delivers and persists them — so none of that projection exists to
// test. Deleting those cases is the point of the ticket landing, not a regression in it.
//
// What is asserted instead is what the VM is now FOR: it refuses to open a room it
// cannot name, it never opens the wrong one, it carries Cherry's unread count without
// trusting it, and a report cannot be sent without a clan to attach it to.
//
// FAKE ISource — no scene, no network, no WebView, no ClanService.
// =============================================================================

using System;
using NUnit.Framework;
using DeNelle.HUD;

namespace DeNelle.Tests.EditMode
{
    [TestFixture]
    public class ClanChatVMTests
    {
        private const string Room = "3f6b1c2e-4a5d-4f7b-9c8e-0d1a2b3c4d5e";
        private const string Wallet = "7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU";

        private sealed class FakeSource : ClanChatVM.ISource
        {
            public string Wallet;
            public string Room;
            public string UrlToReturn = "https://example.invalid/clan-chat.html";

            public int Reports;
            public string LastClanId, LastMessageId;
            public bool NextReportSucceeds = true;
            public int BuildUrlCalls;
            public string LastBuiltRoom;

            public event Action Changed;

            public string WalletAddress => Wallet;
            public string RoomId => Room;

            public string BuildEmbedUrl(string roomId)
            {
                BuildUrlCalls++;
                LastBuiltRoom = roomId;
                return UrlToReturn;
            }

            public void ReportMessage(string clanId, string messageId, Action<bool> done)
            {
                Reports++;
                LastClanId = clanId;
                LastMessageId = messageId;
                done?.Invoke(NextReportSucceeds);
            }

            public void RaiseChanged() => Changed?.Invoke();
        }

        private static FakeSource Ready()
            => new FakeSource { Wallet = ClanChatVMTests.Wallet, Room = ClanChatVMTests.Room };

        // ── The refusals: the VM never opens a room it cannot name ────────────

        [Test]
        public void no_wallet_is_an_error_state_and_builds_no_url()
        {
            var src = new FakeSource { Wallet = null, Room = Room };
            var vm = new ClanChatVM(src, null);

            Assert.That(vm.CanEmbed, Is.False);
            Assert.That(vm.HasError, Is.True);
            Assert.That(vm.ErrorReason, Is.EqualTo(ClanChatVM.NoWallet));
            Assert.That(vm.EmbedUrl, Is.Null);
            Assert.That(src.BuildUrlCalls, Is.Zero, "no URL is built without an identity to open it as");
        }

        [Test]
        public void no_room_is_an_error_state_and_builds_no_url()
        {
            // ⛔ THE LOAD-BEARING CASE. The client has no remote clan client yet, so the
            // server-side clans.id is frequently unknown. Embedding anyway would open
            // whatever room the SDK defaults to — chat that looks like it works and reaches
            // nobody, or worse, two clans sharing one room.
            var src = new FakeSource { Wallet = Wallet, Room = null };
            var vm = new ClanChatVM(src, null);

            Assert.That(vm.CanEmbed, Is.False);
            Assert.That(vm.ErrorReason, Is.EqualTo(ClanChatVM.NoRoom));
            Assert.That(vm.EmbedUrl, Is.Null);
            Assert.That(src.BuildUrlCalls, Is.Zero);
        }

        [Test]
        public void a_source_that_returns_no_url_is_treated_as_no_room_not_as_success()
        {
            var src = Ready();
            src.UrlToReturn = null;
            var vm = new ClanChatVM(src, null);

            Assert.That(vm.HasError, Is.True, "the panel must not 'succeed' into loading nothing");
            Assert.That(vm.ErrorReason, Is.EqualTo(ClanChatVM.NoRoom));
        }

        // ── The happy path ────────────────────────────────────────────────────

        [Test]
        public void wallet_plus_room_yields_an_embed_url_scoped_to_that_room()
        {
            var src = Ready();
            var vm = new ClanChatVM(src, null);

            Assert.That(vm.CanEmbed, Is.True);
            Assert.That(vm.HasError, Is.False);
            Assert.That(vm.ErrorReason, Is.Null);
            Assert.That(vm.RoomId, Is.EqualTo(Room));
            Assert.That(vm.WalletAddress, Is.EqualTo(Wallet));
            Assert.That(vm.EmbedUrl, Is.EqualTo(src.UrlToReturn));
            Assert.That(src.LastBuiltRoom, Is.EqualTo(Room),
                "the room passed to the host page is the clan's own id and never a substitute");
        }

        [Test]
        public void the_vm_holds_no_message_list_at_all()
        {
            // Stated as a test because it is the ticket: Cherry owns the messages. A property
            // named Messages reappearing here means a native renderer is creeping back in.
            var t = typeof(ClanChatVM);
            foreach (var name in new[] { "Messages", "Chips", "SendPhrase", "SendCustom", "CustomTextMaxChars" })
            {
                Assert.That(t.GetMember(name).Length, Is.Zero,
                    name + " must not exist on the VM — Cherry renders, delivers and persists the " +
                    "messages, so a projection here would be a second always-stale copy.");
            }
        }

        // ── Embed events ──────────────────────────────────────────────────────

        [Test]
        public void mounting_clears_the_error_state_and_sets_is_mounted()
        {
            var vm = new ClanChatVM(Ready(), null);
            vm.OnError("mount_failed");
            Assert.That(vm.IsMounted, Is.False);
            Assert.That(vm.HasError, Is.True);

            vm.OnMounted();
            Assert.That(vm.IsMounted, Is.True);
            Assert.That(vm.HasError, Is.False, "a successful mount retires the previous failure");
        }

        [Test]
        public void an_error_drops_the_mount_and_records_a_reason_never_blank()
        {
            var vm = new ClanChatVM(Ready(), null);
            vm.OnMounted();

            vm.OnError(null);
            Assert.That(vm.IsMounted, Is.False);
            Assert.That(vm.ErrorReason, Is.EqualTo("unknown"),
                "a blank reason still has to name something — an unexplained blank panel is the " +
                "failure the error state exists to prevent");
        }

        [Test]
        public void unread_count_follows_cherry_and_clamps_a_negative()
        {
            var vm = new ClanChatVM(Ready(), null);
            Assert.That(vm.UnreadCount, Is.Zero);

            vm.OnUnread(7);
            Assert.That(vm.UnreadCount, Is.EqualTo(7));

            vm.OnUnread(-3);
            Assert.That(vm.UnreadCount, Is.Zero, "the count comes from a third-party embed and is clamped, not trusted");

            vm.OnUnread(0);
            Assert.That(vm.UnreadCount, Is.Zero);
        }

        // ── Report ────────────────────────────────────────────────────────────

        [Test]
        public void reporting_routes_the_bound_room_and_the_message_id_to_the_source()
        {
            var src = Ready();
            var vm = new ClanChatVM(src, null);
            bool? outcome = null;

            vm.ReportMessage("cherry_msg_1", ok => outcome = ok);

            Assert.That(src.Reports, Is.EqualTo(1));
            Assert.That(src.LastClanId, Is.EqualTo(Room), "the clan is the VM's own room, never the caller's word for it");
            Assert.That(src.LastMessageId, Is.EqualTo("cherry_msg_1"));
            Assert.That(outcome, Is.True);
        }

        [Test]
        public void reporting_without_a_room_or_a_message_id_is_refused_locally()
        {
            // clan_id is NOT NULL on clan_reports, so a report with no clan is a guaranteed
            // 400. Refusing here spends no signature and no round trip on a known failure.
            var noRoom = new FakeSource { Wallet = Wallet, Room = null };
            var vm1 = new ClanChatVM(noRoom, null);
            bool? r1 = null;
            vm1.ReportMessage("cherry_msg_1", ok => r1 = ok);
            Assert.That(noRoom.Reports, Is.Zero);
            Assert.That(r1, Is.False, "the callback still fires — a silent no-op would hang the UI affordance");

            var src = Ready();
            var vm2 = new ClanChatVM(src, null);
            foreach (var bad in new[] { null, "", "   " })
            {
                bool? r2 = null;
                vm2.ReportMessage(bad, ok => r2 = ok);
                Assert.That(r2, Is.False, "blank message id refused: " + (bad ?? "<null>"));
            }
            Assert.That(src.Reports, Is.Zero);
        }

        [Test]
        public void reporting_reports_a_server_refusal_as_a_failure()
        {
            var src = Ready();
            src.NextReportSucceeds = false;
            var vm = new ClanChatVM(src, null);
            bool? outcome = null;

            vm.ReportMessage("cherry_msg_1", ok => outcome = ok);
            Assert.That(outcome, Is.False, "a refused report must never read back as recorded");
        }

        [Test]
        public void reporting_with_no_callback_does_not_throw()
        {
            var src = Ready();
            var vm = new ClanChatVM(src, null);
            Assert.DoesNotThrow(() => vm.ReportMessage("cherry_msg_1"));
            Assert.That(src.Reports, Is.EqualTo(1));
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        [Test]
        public void changed_fires_on_source_change_and_stops_after_dispose()
        {
            var src = Ready();
            var vm = new ClanChatVM(src, null);
            int fired = 0;
            vm.Changed += () => fired++;

            src.RaiseChanged();
            Assert.That(fired, Is.EqualTo(1));

            vm.Dispose();
            src.RaiseChanged();
            Assert.That(fired, Is.EqualTo(1), "a disposed VM must not keep repainting a destroyed panel");
        }

        [Test]
        public void a_room_arriving_later_promotes_the_vm_out_of_its_error_state()
        {
            // The expected live sequence: the panel opens before the clan id is known, then a
            // remote clan read lands and the room appears.
            var src = new FakeSource { Wallet = Wallet, Room = null };
            var vm = new ClanChatVM(src, null);
            Assert.That(vm.ErrorReason, Is.EqualTo(ClanChatVM.NoRoom));

            src.Room = Room;
            src.RaiseChanged();

            Assert.That(vm.CanEmbed, Is.True);
            Assert.That(vm.HasError, Is.False);
            Assert.That(vm.EmbedUrl, Is.EqualTo(src.UrlToReturn));
        }

        [Test]
        public void close_invokes_the_supplied_action()
        {
            int closed = 0;
            var vm = new ClanChatVM(Ready(), () => closed++);
            vm.Close();
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(vm.Title, Is.EqualTo("Clan Chat"));
        }
    }
}
