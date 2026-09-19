// =============================================================================
// CircleWire — the [Serializable] DTOs behind every Circle screen read (WO-1870, Lane A).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// ⛔ WHY JsonUtility AND NOT Newtonsoft. DeNelle.HUD.asmdef references DeNelle.Core /
// DeNelle.Data / Unity.Localization / UniTask / UI / TMP only — there is NO Newtonsoft
// reference and asmdefs are not edited by this lane. So every body is picked apart with
// UnityEngine.JsonUtility over public-field DTOs, exactly the constraint ClanChatSource.cs
// records for its own hand-built payload.
//
// ⛔ THE ONE JsonUtility TRAP, WRITTEN DOWN SO IT IS NOT REDISCOVERED: a JSON `null` for an
// OBJECT field does not come back as null — JsonUtility materialises a DEFAULT-CONSTRUCTED
// instance. So "no clan", "no vault" and "no ballot" are decided by an EMPTY IDENTIFIER
// (clanId / vault_address / ballot_id), NEVER by a null check. Every consumer in
// CircleScreenVM keys on emptiness for that reason.
//
// ⛔ AND THE SECOND: JsonUtility cannot deserialise a DICTIONARY. That is why the batch
// username read this lane added (api/profile/usernames.js) answers an ARRAY of
// {playerId, username} rows and not a {"<id>":"<name>"} map — a map would parse to nothing
// at all, silently, and every member row would fall back to a truncated address forever.
//
// FIELD NAMES ARE THE WIRE'S, NOT C#'s. The clan membership routes answer camelCase
// (clanId/memberCount) and the vigil/ballot routes answer snake_case (vault_address/
// ballot_id) — both were read at source (api/clan/me.js, api/_lib/clan-vigil.js,
// api/_lib/clan-ballot.js) and are reproduced verbatim, because JsonUtility matches by
// exact field name and a "tidied" name binds nothing.
// =============================================================================

using System;
using DeNelle.Core.Diagnostics;
using UnityEngine;

namespace DeNelle.HUD
{
    /// <summary>
    /// Parsing helpers + the response DTOs for every route the Circle screen reads.
    /// Pure data: nothing here knows about panels, state machines or player-facing copy.
    /// </summary>
    public static class CircleWire
    {
        private const string Sys = "Circle";

        /// <summary>
        /// Parse one response body, or null when the body is empty / unparseable. NEVER throws:
        /// a malformed body is "we could not read the answer", which the VM renders as its
        /// unreachable state rather than as an empty Circle (the ClanMembershipClient.cs:27-31
        /// hold-previous rule, applied to every read on this screen).
        /// </summary>
        public static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception ex)
            {
                FlowTrace.Warn(Sys, "response body did not parse as " + typeof(T).Name + ": " +
                                    ex.GetType().Name + " — holding the previous state.");
                return null;
            }
        }

        /// <summary>
        /// The refusal code carried by a body, or null when the body is not a refusal.
        /// Covers BOTH envelopes this screen can meet: the clan routes'
        /// {ok:false, error:CODE, code:CODE} (api/_lib/clan-http.js:217-219) and the older
        /// profile route's business-failure 200 {success:false, error:CODE}
        /// (api/profile/username.js:22-24). One reader, so neither shape needs a second path.
        /// </summary>
        public static string RefusalCode(string json)
        {
            var env = Parse<Envelope>(json);
            if (env == null) return null;
            if (env.ok || env.success) return null;
            if (!string.IsNullOrEmpty(env.code)) return env.code;
            if (!string.IsNullOrEmpty(env.error)) return env.error;
            return null;
        }

        /// <summary>True when the body is a well-formed success on either envelope.</summary>
        public static bool IsOk(string json)
        {
            var env = Parse<Envelope>(json);
            return env != null && (env.ok || env.success);
        }

        // ── The envelope both rails share ─────────────────────────────────────
        [Serializable]
        public class Envelope
        {
            public bool ok;
            public bool success;
            public string error;
            public string code;
        }

        // ── GET /api/clan/me (api/clan/me.js:46-59) ───────────────────────────
        [Serializable]
        public class MeResponse
        {
            public bool ok;
            public ClanDto clan;
            public string role;
            public string joinedAt;
        }

        [Serializable]
        public class ClanDto
        {
            public string clanId;
            public string code;
            public string name;
            public string tag;
            public string joinPolicy;
            public string createdAt;
            public int memberCount;
        }

        // ── POST /api/clan/create | join | leave ──────────────────────────────
        [Serializable]
        public class CreateResponse
        {
            public bool ok;
            public string clanId;
            public string code;
            public string name;
            public string tag;
            public string role;
        }

        [Serializable]
        public class LeaveResponse
        {
            public bool ok;
            public string newLeader;
            public bool clanDeleted;
        }

        // ── GET /api/clan/vigil (api/_lib/clan-vigil.js:336-359, vault :376-393) ─
        [Serializable]
        public class VigilResponse
        {
            public bool ok;
            public double vigil_weight;
            public int member_count;
            public bool degraded;
            public double collective_vigil_weight;
            public bool hardware_backed;
            public bool collective_degraded;
            public VaultDto vault;
            public VigilMemberDto[] members;
        }

        [Serializable]
        public class VigilMemberDto
        {
            public string wallet;
            public string role;
            public long tenure_seconds;
            public bool degraded;
            // ⚠ THE TWO STAKE FIELDS THE SERVER ALSO SENDS ARE DELIBERATELY NOT DECLARED
            // HERE. api/clan/vault/register.js:130-146 is a standing copy gate on any NEW
            // player-facing surface about staking or token ownership, and WO-1870's owner
            // ruling 4 says joining is never stake-gated. A field that does not exist on
            // this DTO cannot be projected into a row by accident later.
        }

        [Serializable]
        public class VaultDto
        {
            public string vault_address;
            public string multisig_address;
            public int vault_index;
            public int threshold;
            public int signer_count;
            public int verified_signer_count;
            public string[] signer_wallets;
            public bool hardware_backed;
            public bool degraded;
            public string verified_at;
        }

        [Serializable]
        public class VaultRegisterResponse
        {
            public bool ok;
            public VaultDto vault;
            public bool hardware_backed;
            public string message;
        }

        // ── GET /api/clan/leaderboard (api/_lib/clan.js:982-989) ──────────────
        [Serializable]
        public class LeaderboardResponse
        {
            public bool ok;
            public LeaderboardRowDto[] clans;
        }

        [Serializable]
        public class LeaderboardRowDto
        {
            public int rank;
            public string clanId;
            public string name;
            public string tag;
            public double metric;
            public int memberCount;
        }

        // ── GET /api/clan/ballot/current (api/_lib/clan-ballot.js:802-874) ────
        [Serializable]
        public class BallotResponse
        {
            public bool ok;
            public string headline;
            public EpochDto epoch;
            public BallotTierDto[] tiers;
            public double vigil_weight;
            public bool vigil_degraded;
            public BallotDto ballot;
            public BallotResultDto result;
            public PerkDto[] perks;
            public CeremonyDto ceremony;
        }

        /// <summary>
        /// WO-1874. The settled-epoch ceremony plate. JsonUtility default-constructs a
        /// missing object, so "no ceremony" is an empty word / epoch_index 0, never a
        /// null check. ⛔ No effect field — ruling 3 still holds.
        /// </summary>
        [Serializable]
        public class CeremonyDto
        {
            public int epoch_index;
            public string word;
            public string line;
            public string circle_name;
            public string perk_title;
            public string perk_description;
            public bool passed;
            public string next_ends_at;
        }

        [Serializable]
        public class EpochDto
        {
            public int index;
            public string started_at;
            public string ends_at;
            public string anchor;
            public long seconds;
        }

        [Serializable]
        public class BallotTierDto
        {
            public int tier;
            public double threshold;
            public bool unlocked;

            // ⚠ THE CATALOGUE RIDES THE TIER ROW, and the client NEEDS it: propose refuses an
            // empty slate with 400 CLAN_BALLOT_BAD_OPTIONS
            // (api/_lib/clan-ballot.js:265 — "if (!Array.isArray(raw) || raw.length === 0)"),
            // and every id must belong to THIS tier's catalogue (:266-271). The server sends
            // that catalogue on every tier row (tierWire, api/_lib/clan-ballot.js:885-887), so
            // the client proposes from what the server itself published.
            public BallotOptionDto[] options;
        }

        [Serializable]
        public class BallotDto
        {
            public string ballot_id;
            public int tier;
            public bool closed;
            public string opened_at;
            public string closes_at;
            public string closed_at;
            public BallotOptionDto[] options;
            public string my_vote;
            public TurnoutDto turnout;
        }

        [Serializable]
        public class BallotOptionDto
        {
            public string option_id;
            public string title;
            public string description;
            public double weight;
            public int voters;
            // ⛔ RULING 3. The server's options[] ALSO carries a stat magnitude
            // (api/_lib/clan-ballot.js:192-241 — "+15% build speed" and ten more like it).
            // It is not declared on this DTO, so the client physically cannot render it and
            // the suppression cannot be undone by a one-line View change. Re-authoring that
            // catalogue into visual/narrative outcomes is a separate SERVER ticket.
        }

        [Serializable]
        public class TurnoutDto
        {
            public int voters;
            public int member_count;
            public int required_voters;
            public double required_ratio;
            public double ratio;
            public double weighted_turnout;
            public bool clears;
        }

        [Serializable]
        public class BallotResultDto
        {
            public bool closed;
            public bool passed;
            public string reason;
            public string winning_option;
        }

        [Serializable]
        public class PerkDto
        {
            public int tier;
            public string activated_at;
            public string expires_at;
            public string title;
            public string description;
            // perk_id is deliberately absent — ruling 3 again: a perk is shown as its
            // narrative title, never as the identifier of a stat grant.
            // effect is deliberately absent — the VM must never bind a magnitude.
        }

        // ── Profile identity (the REUSED username rail, see CircleSource header) ─
        [Serializable]
        public class UsernamesResponse
        {
            public bool ok;
            public UsernameRowDto[] names;
        }

        [Serializable]
        public class UsernameRowDto
        {
            public string playerId;
            public string username;
        }

        [Serializable]
        public class UsernameSetResponse
        {
            public bool success;
            public string username;
            public bool wasRename;
            public string error;
        }
    }
}
