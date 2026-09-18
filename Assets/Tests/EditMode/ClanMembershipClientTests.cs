// =============================================================================
// ClanMembershipClientTests (EditMode) — WO-1858 acceptance criterion: prove the
// GET /api/clan/me wiring with a test, not just prose.
// -----------------------------------------------------------------------------
// No network, no scene: ApplyResponseJson is pure string-in / ClanRoomBinding-out, so
// these drive it directly with the server's own documented response shapes
// (api/clan/me.js header, WO-1845) rather than standing up a fake HTTP server.
//
// ⚠ ClanRoomBinding IS PROCESS-WIDE STATIC STATE. Every test resets it in TearDown so
// a stray binding from one case can never leak into the next (or into
// ClanChatVMTests's own FakeSource-driven cases, which never touch ClanRoomBinding
// at all and would otherwise be silently order-dependent on this file).
// =============================================================================

using NUnit.Framework;
using DeNelle.HUD;

namespace DeNelle.Tests.EditMode
{
    [TestFixture]
    public class ClanMembershipClientTests
    {
        private const string ClanId = "3f6b1c2e-4a5d-4f7b-9c8e-0d1a2b3c4d5e";

        [TearDown]
        public void ResetBinding() => ClanRoomBinding.Set(null);

        [Test]
        public void a_wallet_in_a_clan_binds_the_real_server_side_uuid()
        {
            // Exact 200 shape from api/clan/me.js when the wallet has a membership.
            string json = "{\"ok\":true,\"clan\":{\"clanId\":\"" + ClanId + "\",\"code\":\"ABCD\"," +
                          "\"name\":\"Test Clan\",\"tag\":\"TST\",\"joinPolicy\":\"open\"," +
                          "\"createdAt\":\"2026-01-01T00:00:00Z\",\"memberCount\":3}," +
                          "\"role\":\"owner\",\"joinedAt\":\"2026-01-01T00:00:00Z\"}";

            ClanMembershipClient.ApplyResponseJson(json);

            Assert.That(ClanRoomBinding.ClanId, Is.EqualTo(ClanId),
                "the clan's real clans.id must be bound before the embed can mount, per WO-1858");
        }

        [Test]
        public void a_wallet_with_no_clan_clears_the_binding_as_a_real_success_not_an_error()
        {
            ClanRoomBinding.Set(ClanId);

            // api/clan/me.js header: "NO CLAN IS A 200, NOT A 404" — this is the exact shape.
            ClanMembershipClient.ApplyResponseJson("{\"ok\":true,\"clan\":null}");

            Assert.That(ClanRoomBinding.ClanId, Is.Null,
                "no membership must clear a stale room rather than leaving a previous clan's id bound");
        }

        [Test]
        public void a_failed_or_malformed_response_holds_the_previous_binding()
        {
            // Same fail-closed-hold-previous rule HeartboundStatusClient.cs applies to a
            // failed stake refresh: a transport hiccup is "we could not ask," never "you
            // have no clan," so a working binding must survive it untouched.
            ClanRoomBinding.Set(ClanId);

            ClanMembershipClient.ApplyResponseJson("");
            Assert.That(ClanRoomBinding.ClanId, Is.EqualTo(ClanId), "empty body must not clear a working binding");

            ClanMembershipClient.ApplyResponseJson("not json at all");
            Assert.That(ClanRoomBinding.ClanId, Is.EqualTo(ClanId), "unparseable body must not clear a working binding");

            ClanMembershipClient.ApplyResponseJson("{\"ok\":false}");
            Assert.That(ClanRoomBinding.ClanId, Is.EqualTo(ClanId), "an explicit ok:false must not clear a working binding");

            ClanMembershipClient.ApplyResponseJson("{\"ok\":true}");
            Assert.That(ClanRoomBinding.ClanId, Is.EqualTo(ClanId),
                "ok:true with neither clan:null nor a clanId is an unrecognised shape, not a no-clan answer");
        }

        [Test]
        public void a_non_uuid_clan_id_is_refused_by_the_binding_itself()
        {
            // Defense in depth: even if this file's regex ever mis-captured something, the
            // final guard is ClanRoomBinding.Set's own UUID-shape check.
            string json = "{\"ok\":true,\"clan\":{\"clanId\":\"not-a-uuid\"}}";

            ClanMembershipClient.ApplyResponseJson(json);

            Assert.That(ClanRoomBinding.ClanId, Is.Null,
                "a malformed id must never reach the embed as a room");
        }
    }
}
