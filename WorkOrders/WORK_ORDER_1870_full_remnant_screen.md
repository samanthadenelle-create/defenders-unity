# WO-1870 — The full in-game Remnant screen (create / join / members / leaderboard / vault / ballots)

**Status:** FIXED — tester APK `2026.09.18.375785` built 18:11 (APK_OK 447MB, R2_PARITY_OK 207), Firebase release 5jrcip5ivq10o; Seeker install pending (phone not on USB at 18:20); owner felt-verify closes. PRIOR: FIXED PENDING DEVICE BUILD — lead gated 2026-09-18 (`Builds/cg1870c` COMPILE_GATE_OK 17:59, `Builds/reg1870b` REGRESSION_OK 584/584 18:03); rides the next tester APK, owner felt-verify closes. PRIOR: IMPLEMENTED PENDING LEAD GATE (2026-09-18 - Lanes A, B and C have all handed back; nothing is compiled, gated or committed, so every hand-back below is a CLAIM. The lead still owes: the `DataRegression.cs` registration line (section 6), `LocalizationBuilder.BuildAll()` for the 7 tables, the locale merge of the Lane C sidecar into the 20 catalogs, and `COMPILE_GATE_OK` + a fresh `REGRESSION_OK` + `UI_CAPTURE_OK` with the PNGs opened.) Previously READY TO IMPLEMENT (plan appended 2026-09-18 by the read-only design lane; every route, column and field cited below was opened at source). Owner rulings of 2026-09-18 ~15:10 (Circle/Remnant naming, display_name, narrative-only perks, join never stake-gated) are folded in. Previously SPEC IN PROGRESS — minted 2026-09-18 14:40 (banner bumped 1870 -> 1871 in the same edit). Owner ruling 2026-09-18: **"Full Remnant screen"** (chosen over the minimal create/join door and over an API-only workaround). Prize-path work: the Remnant chat two-device test and the hackathon video need a player to be able to found and join a Remnant from the phone.

**Owner, verbatim (2026-09-18):** "i trioed to do the chat but there isnt a join chat option or join a remnant"

## The measured gap (read at source 2026-09-18)

- The chat panel's only no-Remnant state is a sentence: `ClanChatPanel.cs:50-56` `KeyNoClan = "clanChat.noClan"` -> en.json `:514` `"You're not in a Remnant yet."`. `ClanChatPanel.cs:383-386` deliberately avoids a "join a clan" imperative and offers NO door.
- The 09-17 status sweep found **no Create / Join / Members / Leaderboard UI anywhere in `Assets/_Modules`** (WO-1859 rename record `:98-119`): only the chat panel, 5 `clanChat.*` keys, 3 titles and 2 chat-wheel phrases exist.
- The SERVER side is built: `api/clan/` holds `create.js`, `join.js` (POST `{playerId, code}`, code uppercase-normalised, `CLAN_BAD_CODE` vs no-such-code are distinct, `:4-16`), `leave.js`, `me.js` (`{ok, clan:{clanId, code, name, tag, joinPolicy, createdAt, memberCount}, role, joinedAt}` per `ClanMembershipClient.cs:14-15`), `promote.js`, `demote.js`, `kick.js`, `leaderboard.js`, `vigil.js`, `report-message.js`, plus `ballot/` and `vault/` directories. Migrations 0030-0037 (tables, reports, rate limit, join policy, messages, ballots, vaults) are applied on live Neon as of 2026-09-18 14:03 (`MIGRATIONS_OK applied=3 skipped=34`).
- Open-join is stored (`clans.join_policy`) but `join.js` still requires a code (WO-1851 `:133-138`).

## Scope (owner-ruled: FULL)

One `PanelId.Remnant` screen, code-built uGUI through the existing kit (never UXML), MVVM strict (VM holds every state + verb, View is a skin), reached from the SAME dock entry that opens Remnant chat today and from the chat panel's no-Remnant state:

1. **Not in a Remnant:** Create (name + tag, join policy invite/open) and Join by code, with the server's distinct errors surfaced in plain language.
2. **In a Remnant:** header (name, tag, my code with a copy/share affordance, member count, my role), **Members** list with roles and the leader/officer verbs promote / demote / kick, **Leave** (confirm), **Leaderboard**, **Vault**, **Ballots** — each tab bound to its existing `api/clan/*` route; a tab whose route returns not-ok shows a traced empty state, never a blank.
3. **Chat** stays where it is (WO-1858 WebView panel); this screen links to it.
4. Every string is a locale key across all 10 catalogs + 7 tables (localization law, WO-1857 shape); ASCII-only TMP; colour never carries meaning alone (owner is red/green colourblind); touch floor per `HudDockSlotLayout`.
5. Instrumented from the first line: `FlowTrace` at open / each fetch / each verb / each refusal; `Guard.TryEach` around every list build.

## Not in scope
Cherry chat internals; server-side open-join enforcement (separate ticket); admin review of `clan_reports`.

## Plan
The design lane appends the implementation plan below (VM field list verbatim, API contract per route read from `api/clan/*.js`, pin list with file:line, file-disjoint lane split, RED-first suite spec, revert recipe per case). Until it does, this WO is a SPEC, not READY.

---

## Owner rulings (2026-09-18, ~15:10) -- the plan below is designed to these

1. **NAMING.** A **Remnant is the PLAYER**, shown by a chosen display name (owner's examples, verbatim: "Sally the Wise", "Hornsent the Wicked"). The **GROUP** -- today's clan, currently worded "Remnant" in every locale string -- is now a **Circle**. So: "Form or join a Circle", "Circle Chat", and its members are Remnants. Every existing group-sense "Remnant" string is re-pointed to Circle (Lane C, 10 catalogs + 7 tables); **the C# key NAMES (`clanChat.*`, `common.remnant_chat`) do not change** -- only their values.
2. **DISPLAY NAME.** A nullable `display_name` on the identity row (NOT wallet-scoped -- Google Play players have no wallet), set once and changeable, 1-24 chars, written by a signed `POST /api/profile/display-name` (authenticate(), rate-limited, **no profanity filter for the demo**). Members, chat and leaderboard show the display name with a wallet-truncated fallback. Migration + endpoint + client call = Lane A; the "Claim your Remnant name" step = Lane B, and it is **the first thing a player without a name sees**.
3. **PERKS.** Ballots may wake **VISUAL + NARRATIVE outcomes only** -- the tree changes, ancestors appear as allies, titles/cosmetics. **No stat edge, no troop types.** The Play build carries nothing crypto and the pack ruling says prestige never buys a better army.
4. **Joining a Circle is NEVER gated on staking.** Stake only adds Vigil weight.

---

# Plan (design lane, 2026-09-18 -- everything below was read at source; a route or field not opened is not in this plan)

## 0. Five measurements that move the ticket

**0a. There is no members-list route.** `GET /api/clan/me` returns `memberCount` only (`api/clan/me.js:6-8`). The ONLY roster the server renders is `GET /api/clan/vigil` -> `members:[{wallet, role, percent_staked, tenure_seconds, vigil_contribution, degraded}]` (`api/clan/vigil.js:6-8`, built by `api/_lib/clan-vigil.js:351-358`). **The Members tab is fed by `/api/clan/vigil`, not by `/me`.** `/me` supplies the header only.

**0b. There IS a vault READ, and it is not under `api/clan/vault/`.** That directory holds only `register.js` (POST). The vault's readable shape rides the SAME `/api/clan/vigil` body: `vault: vaultToWire(...)` -> `{vault_address, multisig_address, vault_index, threshold, signer_count, verified_signer_count, signer_wallets, percent_staked, tenure_seconds, collective_vigil_weight, hardware_backed, degraded, verified_at}` or `null` (`api/_lib/clan-vigil.js:350`, `:376-393`). `api/clan/vault/register.js:129-139` says so in its own words: "the client reads /api/clan/vigil, which always derives it". **So Vault ships in v1 read-only; only the leader-only REGISTER write is an open question (Q3).**

**0c. THE SERVER'S ENTIRE PERK CATALOGUE CONTRADICTS RULING 3, and this was read at source.** `api/_lib/clan-ballot.js:192-241` -- `PERK_OPTIONS` -- is eleven options across five tiers and **every one of them is a stat edge or a unit unlock**: `build_speed_1/2/3` ("+5% / +10% / +15% build speed"), `harvest_yield_1/2/3` ("+3% / +6% / +10% harvest yield"), `wall_hp_1/2` ("+5% / +10% wall HP"), `new_building` ("Unlocks a new building type"), `turret_variant` ("Unlocks a defensive turret variant"), `ancestor_troop` ("Unlocks an ancestor-summoned troop"). Those magnitudes ride the wire as `options[].effect` (`clan-ballot.js:838-848`).
> **What saves v1: the perks are INERT.** `clan_perks` is WRITTEN by `clan-ballot.js:603` and read by `clan-ballot.js:507` and **by nothing else in the repo** -- grepping `Assets/_Modules` for `clan_perks`, `clan_build_speed`, `clan_harvest_yield`, `clan_wall_hp`, `clan_new_building`, `clan_turret_variant`, `clan_ancestor_troop` returns **zero hits**. No stat is granted today and none is wired.
> **So the ruling is honoured by SUPPRESSION, and the plan says so out loud:** the Ballots tab renders `options[].title` and `options[].description` (narrative) and **NEVER renders `options[].effect`** -- `BallotOptionRowVM.EffectText` is declared, always left empty by the VM, and the regression suite pins that the View contains no binding for it. The `perk_id`s are shown as narrative titles only. **UNUSED SERVER FIELDS, named as required:** `options[].effect` (all 11), `result.perk_id`, `perks[].perk_id` beyond its narrative title. Re-authoring `PERK_OPTIONS` into visual/narrative outcomes (the tree changes, ancestors appear as allies, titles/cosmetics) is a **SEPARATE server ticket** -- it edits `api/_lib/clan-ballot.js`, which no lane here touches (Q5).

**0d. There is no rail-agnostic "identity" table with an `identity_kind` column.** Read at source: `identity_kind` exists only on `auth_sessions` (`api/schema.sql:262`, migration `20260830_0013_auth_sessions_identity_kind.sql:42`) and on `account_deletion_requests` (`api/schema.sql:1391`) -- neither is a per-player identity row. `wallet_identity` (`api/schema.sql:2274-2281`) IS wallet-scoped and is therefore **excluded by the ruling** (every clan wallet column FKs onto it, so a Google Play player has no row). **The one player-scoped profile row is `player_profiles` (`api/schema.sql:999-1007`), whose primary key is `wallet TEXT` but is DOCUMENTED at `:1000` as "base58 address (= player_data.player_id)"** -- and `player_data.player_id` (`:59-60`) is the rail-agnostic id that also holds `play-` and `guest-` ids. So `player_profiles` is the identity row under a legacy column name, and that is where `display_name` goes. See Q2: it already carries a nullable `username` with case-insensitive uniqueness and a profanity policy, which is NOT what the ruling asked for.

**0e. Joining is already un-gated on stake, and nothing needs to change to keep it that way.** `api/clan/join.js` calls `joinClan(sql, wallet, body.code)` and its whole refusal set is `CLAN_BAD_CODE | CLAN_NOT_FOUND | CLAN_ALREADY_IN_CLAN | CLAN_IDENTITY_MISSING | SERVER_ERROR` (`join.js:6-14`) -- no stake, no SGT, no Vigil. Stake enters only as `percent_staked` inside `/api/clan/vigil`'s weight maths (`clan-vigil.js:354`). The regression suite pins this (`[circle-join-ungated]`).

Consequence: the screen makes **five** reads -- `/me` (header), `/vigil` (members + vault), `/leaderboard`, `/ballot/current`, `/profile/get` (display name) -- and **ten** writes.

## 1. Names, decided here so no lane guesses

Ruling 1 renames the GROUP. So the screen, the route and the key namespace are **Circle**, and "Remnant" survives only as the word for a PLAYER:

| Thing | Name |
|---|---|
| Panel id | `PanelId.Circle = 28` (appended; `PanelRouter.cs:37-190` is append-only, last member `HonestFeedback = 27`) |
| ViewModel | `DeNelle.HUD.CircleScreenVM` -- `Assets/_Modules/HUD/Circle/CircleScreenVM.cs` |
| View | `DeNelle.HUD.CircleScreenPanel` -- `Assets/_Modules/HUD/Circle/CircleScreenPanel.cs` |
| Locale namespace | `circle.*` (the group), `remnant.name.*` (the player's display name) |
| Dock row key | `common.circle` = "Circle" (NEW). **`common.remnant_chat` keeps its KEY** -- it is a literal needle in `ClanFeatureGateRegression.cs:37` -- and only its VALUE becomes "Circle Chat" |
| `PanelManager` handle | `"Circle"` (the chat panel already holds `"Remnant Chat"`, `ClanChatPanel.cs:86` -- distinct handles so the modal arbiter closes one when the other opens) |

## 2. `CircleScreenVM` -- the field list, VERBATIM

New file `Assets/_Modules/HUD/Circle/CircleScreenVM.cs`, assembly `DeNelle.HUD`, namespace `DeNelle.HUD`. It implements `DeNelle.Core.UI.Mvvm.IPanelViewModel` (`Assets/_Modules/Core/UI/Mvvm/IPanelViewModel.cs:13-26`: `Title`, `Changed`, `Close()`, `Dispose()`) AND exposes `public static CircleScreenVM CreateDefault(Action onClose)`.

> **Both are load-bearing, not style.** `UiMvvmConformanceRegression.cs:53` has `HardFailOnNew = true` with `KnownBaseline` EMPTY; a new uGUI-constructing View is exempt from the banned-symbol scan ONLY if it carries `IPanelViewModel` or a `.CreateDefault(` call (`:74-78`). The View earns its exemption by binding this VM and nothing else. **That is the ui-mvvm rule for this ticket.**

Pure C#, no `UnityEngine` types (the `ClanChatVM.cs:14-15` rule), so the EditMode suite drives it with a fake `ISource`, no scene and no network.

```csharp
public sealed class CircleScreenVM : IPanelViewModel, IDisposable
{
    // ---- the service seam (faked in tests; the VM never builds a request) ----
    public interface ISource
    {
        event Action Changed;                    // wallet / ClanRoomBinding moved underneath us
        string WalletAddress { get; }            // BackendRequestSigner.CurrentPlayerId(), or null
        bool IsGuest { get; }                    // BackendRequestSigner.IsGuestIdentity(...)

        // reads  (done: body, httpStatus; httpStatus 0 == transport failure)
        void FetchMe(Action<string, long> done);
        void FetchVigil(Action<string, long> done);
        void FetchLeaderboard(int limit, Action<string, long> done);
        void FetchBallot(Action<string, long> done);
        void FetchDisplayNames(string[] playerIds, Action<string, long> done);   // ruling 2

        // writes (every one is an explicit player press)
        void SetDisplayName(string name, Action<string, long> done);             // ruling 2
        void Create(string name, string tag, string joinPolicy, Action<string, long> done);
        void Join(string code, Action<string, long> done);
        void Leave(Action<string, long> done);
        void Promote(string targetPlayerId, Action<string, long> done);
        void Demote(string targetPlayerId, Action<string, long> done);
        void Kick(string targetPlayerId, Action<string, long> done);
        void ProposeBallot(int tier, string[] optionIds, Action<string, long> done);
        void Vote(string ballotId, string optionId, Action<string, long> done);
        void RegisterVault(string vaultAddress, int vaultIndex, Action<string, long> done);

        void CopyToClipboard(string text);       // the invite code's copy/share affordance
        void OpenChat();                         // routes to ClanChatPanel.Toggle()
    }

    // ---- top-level screen state (the View switches on this and nothing else) ----
    public enum CircleState
    {
        NoWallet    = 0,  // no identity, or a guest id -- every clan route is auth.mode=='wallet'
        NeedsName   = 1,  // RULING 2: no display_name yet. THE FIRST THING SUCH A PLAYER SEES.
        Loading     = 2,  // /me in flight, nothing known yet
        NotInCircle = 3,  // /me answered {ok:true, clan:null} -- a REAL success, not an error
        InCircle    = 4,  // /me answered a clan object
        Unreachable = 5,  // /me could not be asked; the previous state is HELD, never cleared
    }
    public CircleState State { get; private set; }
    // ⛔ THE ENUM ORDER IS NOT A PRECEDENCE. `HasDisplayName` is only known AFTER the
    // display-names lookup for self, so the first fetch on open is /api/profile/display-names
    // with the caller's own id; the VM enters NeedsName FROM Loading when that answer carries
    // no name, and from nowhere else. Stated here because the numeric order reads the other way.

    public enum CircleTab { Members = 0, Leaderboard = 1, Vault = 2, Ballots = 3 }
    public CircleTab ActiveTab { get; private set; }
    public sealed class TabVM { public CircleTab Id; public string LabelKey; public bool IsActive; public Action Activate; }
    public IReadOnlyList<TabVM> Tabs { get; private set; }

    // ---- IPanelViewModel ----
    public string Title { get; }                  // LocalizedText("circle.title").Resolve()
    public event Action Changed;
    public void Close();
    public void Dispose();

    // ---- RULING 2: the player's own Remnant name (claimed BEFORE anything else) ----
    public string MyDisplayName { get; private set; }        // null until claimed
    public bool HasDisplayName { get; private set; }
    public string MyNameFallbackText { get; private set; }   // VM-truncated "7Qx...4fL"
    public string NameDraft { get; private set; }            // SetNameDraft(string)
    public bool CanSubmitName { get; private set; }          // trimmed length 1..24 (ruling 2)
    public string NamePromptKey { get; private set; }        // remnant.name.claim.title
    public string NameHintKey { get; private set; }          // remnant.name.hint  ("Sally the Wise")
    public string NameFieldKey { get; private set; }         // remnant.name.field
    public string NameSubmitKey { get; private set; }        // remnant.name.submit
    public void SetNameDraft(string s);
    public void SubmitDisplayName();                         // POST /api/profile/display-name
    public void EditDisplayName();                           // changeable: re-enters NeedsName

    // ---- HEADER (from GET /api/clan/me) ----
    public string CircleId { get; private set; }             // clans.id UUID -- also the chat room id
    public string CircleName { get; private set; }           // clans.name, 1..32, ASCII-folded by the VM
    public string CircleTag { get; private set; }            // clans.tag, 1..5
    public string CircleCode { get; private set; }           // clans.code, ^[A-HJ-NP-Z2-9]{6}$
    public string JoinPolicy { get; private set; }           // 'invite' | 'open'
    public string JoinPolicyLabelKey { get; private set; }   // circle.policy.invite | circle.policy.open
    public int MemberCount { get; private set; }
    public string MemberCountText { get; private set; }      // LocalText.Format("circle.header.members", n)
    public string MyRole { get; private set; }               // 'leader' | 'officer' | 'member'
    public string MyRoleLabelKey { get; private set; }       // circle.role.* -- a WORD, colour carries nothing
    public string CreatedAtIso { get; private set; }
    public string JoinedAtIso { get; private set; }
    public bool IsLeader { get; private set; }
    public bool IsOfficer { get; private set; }

    // ---- NOT-IN-CIRCLE form state ----
    public string CreateName { get; private set; }           // SetCreateName(string)
    public string CreateTag { get; private set; }            // SetCreateTag(string)
    public bool CreateOpenJoin { get; private set; }         // ToggleCreateOpenJoin() -> 'open'|'invite'
    public bool CanSubmitCreate { get; private set; }        // name 1..32 && tag 1..5 (clans_name_len/clans_tag_len)
    public string CreateHintKey { get; private set; }        // circle.create.hint
    public string JoinCode { get; private set; }             // SetJoinCode(string) -- upper-cased by the VM
    public bool CanSubmitJoin { get; private set; }          // 6 chars from [A-HJ-NP-Z2-9]
    public string JoinHintKey { get; private set; }          // circle.join.hint
    // RULING 4: there is NO stake precondition anywhere in this block. The VM exposes no
    // CanJoinBecauseStaked / MinimumStake field, and none may be added.

    // ---- MEMBERS rows (GET /api/clan/vigil members[] + the display-name lookup) ----
    public sealed class MemberRowVM
    {
        public string PlayerId;            // full wallet -- the verb payloads' target
        public string DisplayNameText;     // RULING 2: the display name, or the truncated fallback
        public bool HasDisplayName;
        public string Role;                // 'leader' | 'officer' | 'member'
        public string RoleLabelKey;        // circle.role.* -- a WORD, never a colour
        public string TenureText;          // VM-formatted from tenure_seconds via circle.tenure.days
        public bool Degraded;              // vigil member degraded -- rendered as a WORD
        public string DegradedLabelKey;    // circle.member.degraded (empty when false)
        public bool IsMe;
        public bool CanPromote;            // IsLeader && Role=='member'
        public bool CanDemote;             // IsLeader && Role=='officer'
        public bool CanKick;               // (IsLeader && !IsMe) || (IsOfficer && Role=='member' && !IsMe)
        public Action Promote;
        public Action Demote;
        public Action Kick;
        // NO stake field. percent_staked / vigil_contribution are DELIBERATELY NOT projected
        // here -- see Q6 and api/clan/vault/register.js:139-146's standing copy gate.
    }
    public IReadOnlyList<MemberRowVM> Members { get; private set; }
    public string MembersEmptyKey { get; private set; }      // circle.members.empty | circle.tab.unreachable

    // ---- LEADERBOARD rows (GET /api/clan/leaderboard clans[]) ----
    // ⛔ THIS BOARD RENDERS NO PLAYER AT ANY DEPTH -- api/_lib/clan.js:943 "NEVER SELECT A
    // WALLET COLUMN HERE". So ruling 2's "leaderboard shows the display name" cannot apply to
    // THIS board; it applies to the separate player board (api/leaderboard/get.js), which is
    // out of this WO's scope. Recorded as Q4 rather than silently ignored.
    public sealed class LeaderRowVM
    {
        public int Rank;                   // server ROW_NUMBER
        public string RankText;            // VM-formatted "#3"
        public string CircleId;
        public string Name;
        public string Tag;
        public string MetricText;          // VM-formatted from `metric`
        public int MemberCount;
        public string MemberCountText;     // LocalText.Format("circle.leaderboard.members", n)
        public bool IsMine;                // CircleId == this.CircleId -- marked with a WORD
        public string MineLabelKey;        // circle.leaderboard.you (empty when not mine)
    }
    public IReadOnlyList<LeaderRowVM> Leaderboard { get; private set; }
    public string LeaderboardEmptyKey { get; private set; }

    // ---- VAULT rows (GET /api/clan/vigil `vault`, api/_lib/clan-vigil.js:376-393) ----
    public bool HasVault { get; private set; }               // vault.vault_address non-empty
    public sealed class VaultRowVM { public string LabelKey; public string ValueText; }
    public IReadOnlyList<VaultRowVM> VaultRows { get; private set; }
        // rows, in order, all VM-formatted:
        //   circle.vault.address         -> truncated vault_address
        //   circle.vault.multisig        -> truncated multisig_address
        //   circle.vault.threshold       -> "2 of 3"  (threshold, signer_count)
        //   circle.vault.verifiedSigners -> verified_signer_count / signer_count
        //   circle.vault.hardware        -> circle.vault.hardware.yes | .no   (WORD, never a colour)
        //   circle.vault.verifiedAt      -> verified_at
    public IReadOnlyList<string> VaultSignerShortTexts { get; private set; }  // signer_wallets, VM-truncated
    public bool CanRegisterVault { get; private set; }       // IsLeader && !HasVault (else 403 CLAN_VAULT_NOT_LEADER)
    public string VaultAddressInput { get; private set; }    // SetVaultAddress(string)
    public int VaultIndexInput { get; private set; }         // SetVaultIndex(int), 0..255
    public string VaultEmptyKey { get; private set; }        // circle.vault.none | circle.tab.unreachable

    // ---- BALLOT rows (GET /api/clan/ballot/current, api/_lib/clan-ballot.js:802-874) ----
    public double VigilWeight { get; private set; }
    public bool VigilDegraded { get; private set; }
    public string VigilWeightText { get; private set; }
    public string VigilDegradedKey { get; private set; }     // circle.vigil.degraded (empty when false)
    public int EpochIndex { get; private set; }
    public string EpochEndsAtIso { get; private set; }

    public sealed class BallotTierVM
    {
        public int Tier;                   // 1..5
        public string TierText;            // LocalText.Format("circle.ballot.tier", n)
        public double Threshold;
        public string ThresholdText;
        public bool Unlocked;              // tiers[].unlocked
        public string LockedLabelKey;      // circle.ballot.locked (empty when unlocked)
        public bool CanPropose;            // Unlocked && !HasBallot
        public Action Propose;
    }
    public IReadOnlyList<BallotTierVM> BallotTiers { get; private set; }

    public bool HasBallot { get; private set; }              // ballot.ballot_id non-empty
    public string BallotId { get; private set; }
    public int BallotTier { get; private set; }
    public bool BallotClosed { get; private set; }
    public string BallotOpenedAtIso { get; private set; }
    public string BallotClosesAtIso { get; private set; }
    public string MyVoteOptionId { get; private set; }       // ballot.my_vote, or null

    public sealed class BallotOptionRowVM
    {
        public string OptionId;
        public string TitleText;           // SERVER-SUPPLIED narrative title ("Steady Hands") -- see Q5
        public string DescriptionText;     // SERVER-SUPPLIED narrative line -- rendered
        /// <summary>RULING 3. ALWAYS EMPTY. api/_lib/clan-ballot.js:192-241 puts a stat
        /// magnitude in options[].effect ("+5% build speed"); this VM never copies it and the
        /// View has no binding for it. Declared so the field's absence is deliberate and
        /// pinned by [circle-no-stat-copy], not an oversight.</summary>
        public string EffectText;          // ALWAYS string.Empty
        public double Weight;
        public string WeightText;
        public int Voters;
        public string VotersText;          // LocalText.Format("circle.ballot.voters", n)
        public bool IsMyVote;
        public string MyVoteLabelKey;      // circle.ballot.yourVote (empty when not mine)
        public bool CanVote;               // !BallotClosed
        public Action Vote;
    }
    public IReadOnlyList<BallotOptionRowVM> BallotOptions { get; private set; }

    public string TurnoutText { get; private set; }          // "4 of 7 voted", from ballot.turnout
    public bool TurnoutClears { get; private set; }
    public bool HasResult { get; private set; }              // result.closed
    public bool ResultPassed { get; private set; }
    public string ResultReasonKey { get; private set; }      // circle.ballot.result.<reason>, fallback .unknown
    public string ResultWinningOptionTitleText { get; private set; }   // the TITLE, never the effect
    public sealed class PerkRowVM { public int Tier; public string TitleText; public string ActivatedAtIso; public string ExpiresAtIso; }
    public IReadOnlyList<PerkRowVM> Perks { get; private set; }   // narrative titles only, no effect
    public string BallotsEmptyKey { get; private set; }      // circle.ballot.none | circle.tab.unreachable

    // ---- VERBS, as commands ----
    public void SelectTab(CircleTab tab);
    public void RefreshAll();                // /me, then the active tab's read
    public void SubmitCreate();
    public void SubmitJoin();
    public void CopyCode();                  // ISource.CopyToClipboard(CircleCode) -> circle.toast.codeCopied
    public void RequestLeave();              // arms the confirm; calls NOTHING
    public void CancelLeave();
    public void ConfirmLeave();              // POST /api/clan/leave
    public void OpenChat();                  // ISource.OpenChat()
    public void SubmitRegisterVault();
    public bool LeaveConfirmArmed { get; private set; }
    public string LeaveConfirmKey { get; private set; }      // circle.leave.confirm
    public bool IsBusy { get; private set; }                 // one in-flight write at a time; disables every verb

    // ---- ERROR / TOAST, always KEYS, never sentences ----
    public string ErrorKey { get; private set; }   // the current blocking refusal, or null
    public string ToastKey { get; private set; }   // the last transient outcome, or null
    public void ClearToast();
    /// <summary>server code + http status -> the ONE player-facing key. Public so the EditMode
    /// suite drives the whole table directly -- the ClanMembershipClient.ApplyResponseJson
    /// precedent (ClanMembershipClient.cs:125-131).</summary>
    public static string PlayerFacingKey(string serverCode, long httpStatus);
}
```

## 3. API contract table -- read from `api/clan/*` and `api/profile/*` at source

Headers on EVERY row: `X-Wallet` + `X-Nonce` + `X-Signature`, **or** `X-Session` + `X-Wallet` (the WO-1157 session rail). The client never builds them -- `BackendRequestSigner.TryAttachAsync(req, playerId, bodyRaw, allowInteractiveSessionMint)` writes `X-Wallet`/`X-Nonce`/`X-Signature` at `BackendRequestSigner.cs:284-286` and `X-Session`/`X-Wallet` at `:430-431`. `Content-Type: application/json` on every POST (`ClanChatSource.cs:170`). Base URL `BackendRequestSigner.BackendBase` (`BackendRequestSigner.cs:52`).

**`allowInteractiveSessionMint` is a per-row decision and it decides whether the phone demo works.** `BackendRequestSigner.cs:243-249`: a non-purchase POST with no live session returns `false` and **the request is never sent**. Reads pass `false` (the `ClanMembershipClient.cs:89` precedent). Every WRITE below is an explicit player press and passes `true` -- the doc comment's own "for example, pressing Redeem" case (`:178-180`). A `false` return is surfaced as `circle.error.notSignedIn`, **never as a dead button**.

Every clan refusal body has one shape: `{ok:false, error:<CODE>, code:<CODE>, ref:<id>}` (`api/_lib/clan-http.js:217-219`).

| # | Route | Method | Body / query | OK shape (source) | Error code -> player-facing key |
|---|---|---|---|---|---|
| 1 | `/api/clan/me` | GET | `?playerId=<wallet>` | `200 {ok:true, clan:null}` or `200 {ok:true, clan:{clanId,code,name,tag,joinPolicy,createdAt,memberCount}, role, joinedAt}` (`me.js:6-8`) | 401 any auth -> `circle.error.notSignedIn`; 500 `SERVER_ERROR` -> `circle.error.server`; transport -> `circle.error.unreachable` (HOLD previous, never clear -- `ClanMembershipClient.cs:27-31`) |
| 2 | `/api/clan/create` | POST | `{playerId, name, tag, joinPolicy?}`; `joinPolicy` is `invite` (default) or `open` (`create.js:5-10`) | `200 {ok:true, clanId, code, name, tag, role:leader}` (`create.js:11`) | 400 `CLAN_BAD_NAME`->`circle.error.badName`; `CLAN_BAD_TAG`->`circle.error.badTag`; `CLAN_BAD_JOIN_POLICY`->`circle.error.badPolicy`; `BAD_PAYLOAD`/`PLAYER_ID_MISSING`->`circle.error.badRequest`; 409 `CLAN_ALREADY_IN_CLAN`->`circle.error.alreadyInCircle`; 429 `CLAN_RATE_LIMITED`->`circle.error.rateLimited`; 500 `CLAN_CODE_UNAVAILABLE`->`circle.error.codeUnavailable`; `CLAN_IDENTITY_MISSING`->`circle.error.identityMissing`; `SERVER_ERROR`->`circle.error.server` (`create.js:12-15`) |
| 3 | `/api/clan/join` | POST | `{playerId, code}`; uppercase-normalised server-side (`join.js:16-17`). **No stake precondition exists on this route (ruling 4).** | `200 {ok:true, clanId, name, tag, role:member}` (`join.js:6`) | 400 `CLAN_BAD_CODE`->`circle.error.badCode`; 404 `CLAN_NOT_FOUND`->`circle.error.noSuchCode` -- **two different sentences on purpose** (`join.js:7-10`); 409 `CLAN_ALREADY_IN_CLAN`->`circle.error.alreadyInCircle`; 429->`circle.error.rateLimited`; 500 `CLAN_IDENTITY_MISSING`/`SERVER_ERROR` (`join.js:11-14`) |
| 4 | `/api/clan/leave` | POST | `{playerId}` | `200 {ok:true}` / `{ok:true,newLeader}` / `{ok:true,clanDeleted:true}` (`leave.js:6-8`) | 404 `CLAN_NOT_IN_CLAN`->`circle.error.notInCircle`; 409 `leader_must_transfer` -- **the RACE label only now** (`leave.js:15-20`) -> `circle.error.leaveRaced`; 429 `CLAN_RATE_LIMITED` (+`Retry-After`, `retry_after`) -> `circle.error.rateLimited`; 500 -> `circle.error.server` |
| 5 | `/api/clan/promote` | POST | `{playerId:<caller>, targetWallet:<target>}` -- **prefer `target`/`targetWallet`, NOT bare `wallet`** (`clan-http.js:224-236`) | `200 {ok:true, wallet, role:officer}` (`promote.js:9`) | 400 `CLAN_BAD_TARGET`/`CLAN_SELF_TARGET`->`circle.error.badTarget`; 403 `CLAN_FORBIDDEN`->`circle.error.forbidden`; 404 `CLAN_NOT_IN_CLAN`/`CLAN_TARGET_NOT_IN_CLAN`->`circle.error.targetGone`; 409 `CLAN_TARGET_ROLE`->`circle.error.targetRole`; `CLAN_RACED`->`circle.error.raced`; 429->`circle.error.rateLimited` (`promote.js:10-16`) |
| 6 | `/api/clan/demote` | POST | as #5 | `200 {ok:true, wallet, role:member}` (`demote.js:8`) | identical table to #5 (`demote.js:9-15`) |
| 7 | `/api/clan/kick` | POST | as #5 | `200 {ok:true}` (`kick.js:8`) | as #5; **a self-kick is 400 `CLAN_SELF_TARGET`, not a leave** (`kick.js:18-20`); the copy is plain and non-punitive by the route's rule (`kick.js:22-23`) -> `circle.toast.kicked` |
| 8 | `/api/clan/leaderboard` | GET | `?limit=50`, clamped 1..MAX (`clan.js:913-917`) | `200 {ok:true, clans:[{rank, clanId, name, tag, metric, memberCount}]}` (`leaderboard.js:6`; built `clan.js:982-989`) | 401->`circle.error.notSignedIn`; 500->`circle.error.server`. **The metric is a NAMED PLACEHOLDER** -- member_count x days_since_created (`leaderboard.js:22-27`) -- so the VM labels it `circle.leaderboard.metric` and **never calls it Vigil** |
| 9 | `/api/clan/vigil` | GET | `?playerId=<wallet>` | `200 {ok:true, vigil_weight, member_count, degraded, collective_vigil_weight, hardware_backed, collective_degraded, vault:{...}|null, members:[{wallet,role,percent_staked,tenure_seconds,vigil_contribution,degraded}]}` (`vigil.js:6-8`; `clan-vigil.js:336-359`, vault `:376-393`) | 400 `PLAYER_ID_MISSING`/`PLAYER_ID_BAD_SHAPE`->`circle.error.badRequest`; **404 `CLAN_NOT_IN_CLAN`, unlike the 200 from /me** (`vigil.js:14-20`) -> the VM reads it as `NotInCircle`, not an error; 500 = a DATABASE failure only -> `circle.error.server`. **An RPC failure is always a 200** (`vigil.js:22-26`) -> `degraded:true` is a WORD on the row, never a failure. **UNUSED by the client: `percent_staked`, `vigil_contribution` (Q6).** |
| 10 | `/api/clan/ballot/current` | GET | `?playerId=<wallet>` | `200 {ok:true, headline, epoch:{index,started_at,ends_at,anchor,seconds}|null, tiers:[{tier,threshold,unlocked,...}], vigil_weight, vigil_degraded, ballot:{ballot_id,tier,closed,opened_at,closes_at,closed_at,options:[{option_id,title,effect,description,weight,voters}],my_vote,turnout:{voters,member_count,required_voters,required_ratio,ratio,weighted_turnout,clears}}|null, result:{closed,passed,reason,winning_option,perk_id}|null, perks:[{tier,perk_id,activated_at,expires_at}]}` (`ballot/current.js:6-8`; built `clan-ballot.js:802-874`) | 400 `PLAYER_ID_*`->`circle.error.badRequest`; 404 `CLAN_NOT_IN_CLAN`->`NotInCircle`; 500->`circle.error.server`. **This read CLOSES an expired ballot** (`ballot/current.js:14-17`) -- a refresh after `closes_at` is how a result appears. **UNUSED by the client (ruling 3): `options[].effect`, `result.perk_id`, `perks[].perk_id`.** |
| 11 | `/api/clan/ballot/propose` | POST | `{playerId, tier:1..5, optionIds:[...]}` -- `playerId` IS part of the body (`ballot/propose.js:17-23`) | `200` = the same body as #10, carrying the new ballot (`propose.js:7`) | 400 `CLAN_BALLOT_BAD_TIER`->`circle.error.ballot.badTier`; `CLAN_BALLOT_BAD_OPTIONS`->`circle.error.ballot.badOptions`; 403 `CLAN_BALLOT_TIER_LOCKED`->`circle.error.ballot.tierLocked`; 404 `CLAN_NOT_IN_CLAN`; 409 `CLAN_BALLOT_ALREADY_OPEN`->`circle.error.ballot.alreadyOpen`; 429 `CLAN_RATE_*` -- **the undeclared-action fallback, 3/hour** (`propose.js:25-32`) -> `circle.error.rateLimited`; 503 `CLAN_BALLOT_WEIGHT_UNAVAILABLE`->`circle.error.ballot.weightUnavailable` (ask again, `propose.js:34-39`) |
| 12 | `/api/clan/ballot/vote` | POST | `{playerId, ballotId, optionId}` (`ballot/vote.js:6`) | `200` = the same body as #10 with `my_vote` set (`vote.js:7`) | 400 `CLAN_BALLOT_BAD_BALLOT_ID`/`CLAN_BALLOT_BAD_OPTIONS`->`circle.error.ballot.badOptions`; 404 `CLAN_BALLOT_NOT_FOUND`->`circle.error.ballot.notFound`; 409 `CLAN_BALLOT_CLOSED`->`circle.error.ballot.closed`; 429->`circle.error.rateLimited`; 503 `CLAN_BALLOT_WEIGHT_UNAVAILABLE`->`circle.error.ballot.weightUnavailable` -- **never a zero-weight vote** (`vote.js:22-29`) |
| 13 | `/api/clan/vault/register` | POST | `{playerId, vaultAddress, vaultIndex?:0..255}` (`vault/register.js:6-7`) | `200 {ok:true, vault:{vault_address,multisig_address,vault_index,threshold,signer_count,signer_wallets,time_lock,config_authority,verified_at}, hardware_backed:true|null, message}` (`register.js:8-10`, `:139-146`) | 400 `CLAN_VAULT_BAD_ADDRESS`->`circle.error.vault.badAddress`; `CLAN_VAULT_NOT_A_MULTISIG`->`circle.error.vault.notMultisig`; `CLAN_VAULT_BAD_INDEX`->`circle.error.vault.badIndex`; `CLAN_VAULT_TOO_MANY_SIGNERS`->`circle.error.vault.tooManySigners`; 403 `CLAN_VAULT_NOT_LEADER`->`circle.error.vault.notLeader`; `CLAN_VAULT_SIGNER_NO_SGT`/`CLAN_VAULT_SIGNER_SGT_REUSED`->`circle.error.vault.signer` -- **this route names a wallet in the body, deliberately and leader-only** (`register.js:21-29`); 404 `CLAN_NOT_IN_CLAN`; 409 `CLAN_VAULT_ALREADY_REGISTERED`->`circle.error.vault.already`; 503 `CLAN_VAULT_UNREADABLE`/`CLAN_VAULT_SIGNER_UNVERIFIABLE`->`circle.error.vault.unreadable`. **`message` is the ONE server copy string this feature may carry and may not be extended by a syllable (`register.js:130-138`).** |
| 14 | `/api/clan/report-message` | POST | `{playerId, clanId, messageId}` | `200 {ok:true}` | **ALREADY WIRED -- do not touch.** `ClanChatSource.cs:144-173` owns it |
| 15 | `/api/profile/display-name` | POST | `{playerId, displayName}` | `200 {ok:true, playerId, displayName, wasRename}` | **NEW, Lane A (ruling 2).** 400 `DISPLAY_NAME_TOO_SHORT`/`DISPLAY_NAME_TOO_LONG` -> `remnant.name.error.length`; `BAD_PAYLOAD`/`PLAYER_ID_MISSING` -> `circle.error.badRequest`; 401 -> `circle.error.notSignedIn`; 429 -> `circle.error.rateLimited`; 500 -> `circle.error.server`. 1..24 chars, **no profanity filter**, **no uniqueness**, changeable |
| 16 | `/api/profile/display-names` | GET | `?playerId=<caller>&playerIds=a,b,c` (capped at 64). **`playerId` is the CALLER and is required** -- `authenticate()` resolves identity from a claimed id and cannot be called without one (`wallet-auth.js:1141-1144`), so a query carrying only `playerIds` would 400 `PLAYER_ID_MISSING`. Authed, not public, to match every other clan-adjacent read | `200 {ok:true, names:{ "<playerId>": "<displayName>" }}` -- absent ids are simply absent | **NEW, Lane A.** 400 `BAD_PAYLOAD`; 401 -> `circle.error.notSignedIn`; 500 -> `circle.error.server`. This is why Members can show names without N round trips, and why **neither `api/profile/get.js` nor `api/_lib/clan-vigil.js` needs editing** |

**Schema (`api/schema.sql`).** `clans` `:2305-2320` (`id, code, name, tag, join_policy, created_at, created_by_wallet`; code matches `^[A-HJ-NP-Z2-9]{6}$`, name 1..32, tag 1..5, `join_policy IN (invite, open)`). `clan_members` `:2322-2332` (`clan_id, wallet, role, joined_at`; `role IN (leader, officer, member)`). `clan_reports` `:2362+` (`id, reporter_wallet, message_id, clan_id, reported_at`). `clan_ballots` `:2430+` (`id, clan_id, proposed_by_wallet, tier 1..5, options JSONB, opened_at, closes_at, closed_at, winning_option`). `clan_ballot_votes` `:2449+` (`ballot_id, wallet, option_id, voted_at, weight`, PK `(ballot_id, wallet)`). `clan_perks` `:2458+` (`clan_id, tier, perk_id, activated_at, expires_at`, PK `(clan_id, tier)` = one perk per tier). `clan_vaults` `:2499+` (`clan_id` PK, `vault_address` UNIQUE, `multisig_address`, `vault_index` 0..255, `signer_wallets` JSONB, `threshold >= 1`, `verified_at`, `first_seen_staked_at`).

**The display-name migration (Lane A).** `api/migrations/20260918_0038_player_profiles_display_name.sql` -- the next number after `20260918_0037_bug_reports_status.sql`:

```sql
ALTER TABLE player_profiles ADD COLUMN IF NOT EXISTS display_name        TEXT NULL;
ALTER TABLE player_profiles ADD COLUMN IF NOT EXISTS display_name_set_at TIMESTAMPTZ NULL;
-- Postgres has NO `ADD CONSTRAINT IF NOT EXISTS`, so a bare ADD CONSTRAINT makes the whole
-- migration non-rerunnable -- the exact failure `20260917_0032_clan_rate_limit.sql:24` records
-- this repo paying for. Lane A copies the guarded idiom from
-- `api/migrations/20260917_0033_clan_join_policy_check.sql` VERBATIM (a DO block that checks
-- pg_constraint first), rather than inventing a second shape:
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'player_profiles_display_name_len') THEN
        ALTER TABLE player_profiles ADD CONSTRAINT player_profiles_display_name_len
            CHECK (display_name IS NULL OR char_length(display_name) BETWEEN 1 AND 24) NOT VALID;
    END IF;
END $$;
```

The same block is appended to `api/schema.sql` under `:1007` as a DESCRIPTION -- that file describes the current shape, `api/migrations/` is the only thing ever applied (`schema.sql:2291-2293` states the rule).

**Why `player_profiles` and not `wallet_identity`:** ruling 2 says not wallet-scoped, and `wallet_identity` (`schema.sql:2274-2281`) is exactly that -- a Google Play player has no row there, because `touchWalletIdentity` is called on the wallet rail ONLY (`wallet-auth.js:1156-1161`), and every clan wallet column FKs onto it. `player_profiles` (`schema.sql:999-1007`) has a PK column *named* `wallet` but documented at `:1000` as "= player_data.player_id", which is the rail-agnostic id that also carries `play-` and `guest-` ids (`schema.sql:59-60`). **No uniqueness index is added** -- the `username_ci` unique index (`schema.sql:1010-1012`) belongs to the OLD field, and ruling 2 asks for neither uniqueness nor a profanity gate. See Q2.

**Parsing.** `DeNelle.HUD.asmdef` references Core / Data / Unity.Localization / UniTask / UnityEngine.UI / TMP / LeanTouch / unity-webview -- **no Newtonsoft**, and asmdefs may not be edited. So the client parses with `UnityEngine.JsonUtility` over `[Serializable]` DTOs. Two consequences the lane must handle explicitly: JsonUtility materialises a missing or null object as a default-constructed instance, so "no ballot" keys on `string.IsNullOrEmpty(ballot.ballot_id)` and "no vault" on `string.IsNullOrEmpty(vault.vault_address)`, **never on a null check**; and `{ok:true, clan:null}` keeps the existing `ClanMembershipClient.ApplyResponseJson` regex path, which is already tested.

**One truth for the room.** After `create`, `join` and `leave`, the client re-fetches `/api/clan/me` and feeds that body through `ClanMembershipClient.ApplyResponseJson` (`ClanMembershipClient.cs:132`), so `ClanRoomBinding` -- which Circle Chat reads -- moves with the screen. **No second binding is introduced.**

## 4. The pin list -- what the implementation must not break

| file:line | The pinned fact |
|---|---|
| `Assets/Editor/Regression/UiMvvmConformanceRegression.cs:53` | `HardFailOnNew = true`, `KnownBaseline` EMPTY. A new uGUI-constructing View that names `GameStateService`, `EconomyService.Instance`, `FindObjectsByType`/`FindAnyObjectByType`, `ResourceLedger`, `VillageInventory.Instance` or a gameplay catalog (`:63-71`) and does NOT carry `IPanelViewModel` or `.CreateDefault(` (`:74-78`) **FAILS the gate**. **THIS IS THE ui-mvvm RULE FOR THIS TICKET.** `CircleScreenPanel` binds `CircleScreenVM.CreateDefault(...)`, implements nothing else, and **no allow-list entry (`:86-129`) may be added for it** |
| `Assets/Editor/Regression/ClanFeatureGateRegression.cs:24` | `ClanFeatureGate.cs` must still contain the literal `public const bool PlayerFacingEnabled = true;` |
| `Assets/Editor/Regression/ClanFeatureGateRegression.cs:26,31-33` | `ClanChatPanelBootstrap.cs:34`'s `if (!ClanFeatureGate.PlayerFacingEnabled) return;` must stay the literal first line, BEFORE `new GameObject("ClanChatPanel")` (`ClanChatPanelBootstrap.cs:58`) |
| `Assets/Editor/Regression/ClanFeatureGateRegression.cs:37,40` | `HudKitController.cs` must still contain the exact needle `new LocalizedText("common.remnant_chat").Resolve(), OpenClanChat);` and the gate check `if (DeNelle.Core.Services.ClanFeatureGate.PlayerFacingEnabled)` must precede it. **This is why ruling 1 renames the VALUE of `common.remnant_chat` and never its KEY, and why no lane edits this file -- see section 5** |
| `Assets/Editor/Regression/PanelDoorRegression.cs:21-25,57-63` | A `MonoBehaviour` under `Assets/_Modules/` whose type name ends in `Panel` needs a door outside its own View/VM/Bootstrap loop. `CircleScreenPanel` satisfies **D2** (`CircleScreenPanelBootstrap` carrying `[RuntimeInitializeOnLoadMethod]`, the `ManageScreenBootstrap.cs:27` / `ClanChatPanelBootstrap.cs:21` shape) AND **D1** (`HudKitController` and `ClanChatPanel` name it in real code) |
| `Assets/_Modules/HUD/Kit/HudKitController.cs:5821-5837` | `AddDockTab` is a **2x3 grid, six cells** (`const int columns = 2; const int rows = 3;`), and all six are occupied (`:5621-5639`). `DockPauseCellIndex` (`:5759-5769`) pins PAUSE at index 5, the bottom-right cell, and `HudUiRegression.CheckGearDrawerClearsNeighbours` (`:1903-1909`) is WO-1465 geometry written around that drawer. **A seventh row paints outside the panel.** This is why section 5 adds no dock row and why `HudKitController.cs` is on every lane's do-not-touch list |
| `Assets/_Modules/Core/UI/PanelRouter.cs:37-190` | `PanelId` is **append-only, values load-bearing**. Last member `HonestFeedback = 27`; `Circle = 28` is appended, nothing renumbered |
| `Assets/_Modules/Core/UI/ElarionUiKit.cs:347` | `MinTouchPx = 112f`. Every tappable row / chip / tab face is AUTHORED to clear it, never left for `ClampMinTouch` -- the `ManageScreenPanel.cs:80-81,112-118,311,346,658,665` discipline ("authored to the floor, not left to ClampMinTouch") |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:1079-1090` | The registration idiom copied exactly: `PanelManager.Register(name, Close, () => IsOpen)` + `PanelRouter.Register(PanelId.X, (Action)Open)` in `Awake`, unregistered in `OnDestroy` (`:1092-1101`) |
| `Assets/_Modules/HUD/ClanChatPanel.cs:86` | `PanelManager.Register("Remnant Chat", ...)` -- the chat panel handle. The new screen registers `"Circle"`, so the modal arbiter closes one when the other opens |
| `Assets/_Modules/HUD/ClanChatPanel.cs:49-60` | `ClanChatStrings` keys `clanChat.noWallet|noClan|unavailable|genericError|opening` and their `LocalizedText` fields. **Ruling 1 changes their VALUES only**; the five key names and the `PlayerFacingError` switch must keep resolving unchanged |
| `Assets/_Modules/HUD/ClanMembershipClient.cs:132-163` | `ApplyResponseJson` is the ONE parser of `/api/clan/me` and the ONE writer of `ClanRoomBinding`. The new screen CALLS it; it does not fork it. The hold-previous-on-failure rule (`:27-31`) is pinned by `Assets/Tests/EditMode/ClanMembershipClientTests.cs:56` |
| `Assets/_Modules/Core/Backend/BackendRequestSigner.cs:183-249` | `TryAttachAsync` returning `false` means **ABORT, do not send**. Every call site fails closed and traces (`ClanMembershipClient.cs:89-94`, `ClanChatSource.cs:183-186`) |
| `api/_lib/clan.js:943` | "NEVER SELECT A WALLET COLUMN HERE" -- the clan leaderboard renders no player at any depth. Ruling 2 display names therefore cannot reach this board (Q4) |
| `api/clan/vault/register.js:130-146` | The standing copy gate: no NEW copy about Seeker ownership, Genesis Tokens, staking, investment or legal status anywhere in this feature without the owner reviewing it first. This is why Members shows no stake number (Q6) and why ruling 3 is honoured by suppression (0c) |
| `Assets/Editor/Localization/LocalizationPolicy.json` | `baseLocale: en`; `supportedLocales` = `en, es, pt-BR, de, fr, ru, ar, ja, ko, zh-Hans` (10); one collection `game`: `source` `Assets/StreamingAssets/Data/Canonical/en.json`, `mirror` `Assets/Resources/Data/Canonical/en.json`, `unityShared` `Assets/Localization/Tables/GameStrings Shared Data.asset`, `unityTablePattern` `Assets/Localization/Tables/GameStrings_{locale}.asset` (7 table assets on disk: `GameStrings.asset`, `_en`, `_de`, `_es`, `_fr`, `_pt-BR`, `_ru`). `LocaleParityRegression.cs:51-59` demands exact key parity across all ten, in BOTH directories, with non-empty values |
| `Assets/Editor/Regression/DataRegression.cs:894` | Suite registration lines are **the committer's**, never a lane's. This plan supplies the line; the lead pastes it |

## 5. The door ruling (decided here so no lane has to guess)

**THE GEAR DRAWER IS FULL, AND THAT WAS MEASURED, NOT ASSUMED.** `HudKitController.AddDockTab` (`:5821-5837`) lays rows out as `const int columns = 2; const int rows = 3;` -- a **2x3 grid, six cells** -- with `column = i % 2`, `row = i / 2`. All six are occupied today: Chat, Leaderboard, Music, Settings, Realm, Pause (`:5621-5639`), and `DockPauseCellIndex` (`:5759-5769`) states in prose that PAUSE is "the bottom-RIGHT cell of the 2x3 grid" at index 5. **A seventh row would compute `row = 3` and paint below `DockInnerY0`, outside the drawer panel**, and it would also move the very cell WO-1465's clearance geometry is written around (`HudUiRegression.cs:1903-1909`, `CheckGearDrawerClearsNeighbours`).

So there is **no new dock row**, and `ClanFeatureGateRegression.cs:37`'s literal needle -- `new LocalizedText("common.remnant_chat").Resolve(), OpenClanChat);` -- is not touched by anything in this WO. Ruling 1 changes only the VALUE behind `common.remnant_chat`, from "Remnant Chat" to "Circle Chat" (Lane C). The doors are:

* **Door 1 (the one the owner will find, because it is where she already looked).** `ClanChatPanel` gains a button in its modal body, **visible in every state, not only the no-Circle one**, labelled `circle.chat.door` and calling `PanelRouter.Open(PanelId.Circle)`. Gear drawer -> Circle Chat -> "Your Circle" is two taps from the HUD and needs no new cell. The existing `clanChat.noClan` sentence is untouched (its VALUE is re-worded by Lane C) and the button sits under it.
* **Door 2.** `CircleScreenPanelBootstrap` -- `[RuntimeInitializeOnLoadMethod]`, the gate check as its literal first line, the `ClanChatPanelBootstrap.cs:21-34` / `ManageScreenBootstrap.cs:27` shape. This is the `PanelDoorRegression` D2 root.
* **Return door.** The Circle screen's own Chat link calls `ClanChatPanel.Toggle()` through the `ISource.OpenChat()` seam, so the View never runs a `FindAnyObjectByType` itself -- that symbol is banned at `UiMvvmConformanceRegression.cs:66`.
* `common.circle` is still minted (the screen title and the chat-panel button both use the word), it just does not become a dock row.

**If the owner wants a dock row anyway (Q1), there are exactly two shapes and both cost more than this one:** (b) re-point the Chat row at the Circle screen and let the screen reach chat -- that MOVES the pinned needle, so the `ClanFeatureGateRegression.cs:37` edit is lead-owned and travels in the same commit; or (c) grow the drawer to 2x4, which re-opens WO-1465's clearance geometry and `HudUiRegression`'s 9a/9b. Neither is taken here without her word.

## 6. File-disjoint lane split

### Lane A -- VM + client calls + the display-name server seam + EditMode tests

**Owns (creates / edits):**
* `Assets/_Modules/HUD/Circle/CircleScreenVM.cs` (new) -- the class in section 2, plus the nested `ISource` and row types.
* `Assets/_Modules/HUD/Circle/CircleSource.cs` (new) -- the live `ISource`: the 15 signed calls (10 writes, 5 reads), `UnityWebRequest` + `BackendRequestSigner.TryAttachAsync`, modelled line-for-line on `ClanChatSource.SendReport`/`AttachAndSend` (`ClanChatSource.cs:149-206`) and `ClanMembershipClient.RefreshAsync` (`:63-123`). Re-feeds `/me` bodies to `ClanMembershipClient.ApplyResponseJson`. `FlowTrace` at open / each fetch / each verb / each refusal; `Guard.Try` around every list build.
* `Assets/_Modules/HUD/Circle/CircleWire.cs` (new) -- the `[Serializable]` JsonUtility DTOs for `/me`, `/vigil`, `/leaderboard`, `/ballot/current`, `/vault/register`, `/profile/display-names`, and the refusal envelope.
* `api/migrations/20260918_0038_player_profiles_display_name.sql` (new)
* `api/schema.sql` -- the DESCRIPTION block appended under `:1007` ONLY. No other line.
* `api/profile/display-name.js` (new) -- POST, `authenticate()`, rate-limited, 1..24 chars, no profanity filter, no uniqueness.
* `api/profile/display-names.js` (new) -- GET batch lookup, capped at 64 ids.
* `test/profile-display-name.test.js` (new) -- the server-side half, in the existing `test/` idiom.
* `Assets/Tests/EditMode/CircleScreenVMTests.cs` (new)
* `Assets/Tests/EditMode/CircleErrorMapTests.cs` (new)

**Must NOT touch:** `ClanChatPanel.cs`, `ClanChatVM.cs`, `ClanChatSource.cs`, `ClanChatPanelBootstrap.cs`, `ClanMembershipClient.cs`, `HudKitController.cs`, `PanelRouter.cs`, anything under `Assets/Editor/`, any locale `.json`, any `.asmdef`, `api/clan/**`, `api/_lib/clan*.js`, `api/profile/get.js`, `api/profile/username.js`, `DataRegression.cs`.

**Commit 1 of Lane A is the integration seam and lands FIRST**, so B can compile against it: the full `CircleScreenVM` type with every member of section 2 declared, `CreateDefault` returning a VM whose `State` is `Loading`, and the nested row types. Bodies may be stubs in that commit; **the SHAPE may not change afterwards without telling B.**

### Lane B -- View / panel / routing / dock + chat doors

**Owns:**
* `Assets/_Modules/HUD/Circle/CircleScreenPanel.cs` (new) -- code-built uGUI through `ElarionUiKit.BuildObsidianModal` (the `ClanChatPanel.cs:161-165` call shape); the NeedsName step first, then the four-face tab row and the shared CLOSE; every band authored at or above `ElarionUiKit.MinTouchPx`; `HudDockSlotLayout`-consistent geometry. Binds `CircleScreenVM` ONLY; re-renders on `Changed`; resolves every string from the VM keys via `LocalizedText` / `LocalText.Format`; ASCII-only TMP; role / state / hardware / vote / degraded facts rendered as WORDS (the owner is red/green colourblind).
* `Assets/_Modules/HUD/Circle/CircleScreenPanelBootstrap.cs` (new)
* `Assets/_Modules/Core/UI/PanelRouter.cs` -- **append `Circle = 28` and its doc comment ONLY**
* `Assets/_Modules/HUD/ClanChatPanel.cs` -- the always-visible `circle.chat.door` button and its key constant (section 5)

**Lane B does NOT touch `HudKitController.cs`.** The gear drawer is a full 2x3 grid (section 5) and `ClanFeatureGateRegression.cs:37`'s needle stays byte-identical, so that file is on the do-not-touch list below rather than in the owned list.

**Must NOT touch:** `HudKitController.cs`, `CircleScreenVM.cs`, `CircleSource.cs`, `CircleWire.cs`, anything under `Assets/Tests/`, anything under `Assets/Editor/`, any locale `.json`, `api/**`, `ClanFeatureGate.cs`, `ClanChatVM.cs`, `ClanChatSource.cs`, `ClanMembershipClient.cs`, `DataRegression.cs`.

### Lane C -- locale keys sidecar (including the ruling-1 rename) + regression suite

**Owns:**
* `Assets/StreamingAssets/Data/Canonical/{en,es,pt-BR,de,fr,ru,ar,ja,ko,zh-Hans}.json` (10)
* `Assets/Resources/Data/Canonical/{the same 10}.json` (10 mirrors)
* `Assets/{StreamingAssets,Resources}/Data/Canonical/chat-phrases.json` (2) -- `phrase_3` only
* `Assets/Editor/Localization/GooglePlayLocalizationVariantPolicy.json` -- the `clanChat.noWallet` override values only
* `Assets/Editor/Regression/CircleScreenRegression.cs` (new)

**Must NOT touch:** any `.cs` under `Assets/_Modules/`, `Assets/Tests/**`, `Assets/Editor/Regression/DataRegression.cs`, `Assets/Editor/Localization/LocalizationPolicy.json`, `api/**`, and **the 7 `GameStrings*.asset` tables** -- those are TOOL-GENERATED by `Assets/Editor/LocalizationBuilder.cs:95-96` `BuildAll()`, which needs a Unity session. **The lead runs `BuildAll()` after Lane C lands**, and `LocalizationAuthorityRegression`'s shared-table check is what proves it ran.

**RULING 1 -- the exact rename set, grepped from the catalogs.** Six keys carry group-sense "Remnant" today and each exists in all 10 catalogs and both directories. Keys unchanged, values re-pointed to Circle:

| Key | en today (`Assets/StreamingAssets/Data/Canonical/en.json`) | en after |
|---|---|---|
| `common.remnant_chat` | `:355` "Remnant Chat" | "Circle Chat" |
| `clanChat.noWallet` | `:513` "Connect your wallet to use Remnant chat." | "Connect your wallet to use Circle chat." |
| `clanChat.noClan` | `:514` "You're not in a Remnant yet." | "You are not in a Circle yet." |
| `clanChat.unavailable` | `:515` "Remnant chat is not available in this build." | "Circle chat is not available in this build." |
| `clanChat.genericError` | `:516` "Remnant chat could not load. Try again in a moment." | "Circle chat could not load. Try again in a moment." |
| `clanChat.opening` | `:517` "Opening Remnant chat..." | "Opening Circle chat..." |

The nine non-English catalogs carry the same six keys with the localised group word WO-1859 minted -- read at source in `WorkOrders/WORK_ORDER_1859_rename_clan_to_remnant.md:133-224`: es "Remanente", pt-BR "Remanescente", de "Ueberrest", fr "Vestige", ru "Osколok" (Cyrillic, declined), ar "al-baqiyya", ja "zanto", ko "jandang", zh-Hans "yimin". **Lane C re-points the group word in all nine** and leaves every other key untouched.

**AND THREE MORE SURFACES THE `en.json` GREP ALONE WOULD HAVE MISSED** -- found by grepping BOTH Canonical directories and re-reading the WO-1859 record (`:98-119`, `:225-261`):

| Surface | What carries the group word | Ruling-1 action |
|---|---|---|
| `Assets/{StreamingAssets,Resources}/Data/Canonical/chat-phrases.json:13` | `phrase_3` = "Welcome to the Remnant!" -- GROUP sense | -> "Welcome to the Circle!" (both mirrors) |
| `chat-phrases.json:14` | `phrase_4` = "Hello, Remnants." -- **PLAYER sense** | **LEAVE IT.** Under ruling 1 a Remnant IS a player, so this line is now correct English. Pinned as a negative case so a blanket find-and-replace cannot break it |
| `Assets/Editor/Localization/GooglePlayLocalizationVariantPolicy.json` | the `clanChat.noWallet` Play-channel override, 10 locale values (WO-1859 `:237-248`) | re-point the group word in all 10 |
| `Assets/Localization/Tables/GameStrings_{en,es,pt-BR,de,fr,ru}.asset` | the same 5 `clanChat.*` entries, ids 44124330363760645-649 (WO-1859 `:250-261`) | **regenerated by the lead's `LocalizationBuilder.BuildAll()`**, never hand-edited by a lane |
| `site/clan-chat.html` `<title>` | the host page title (WO-1859 `:98-104`) | out of this WO's scope -- `site/**` is nobody's lane here; raised as Q8 |

⚠ **`chat-phrases.json` and `GooglePlayLocalizationVariantPolicy.json` are NOT in `LocalizationPolicy.json`'s single `game` collection**, so `LocaleParityRegression` cannot see drift in either. `[circle-no-group-remnant]` therefore scans **22 files** -- the 20 catalogs plus those two -- and is the only oracle that covers them.

**The frozen key list (A references these, B renders them, C authors them -- so none can drift).**

*Player name (ruling 2):* `remnant.name.claim.title`, `remnant.name.field`, `remnant.name.hint`, `remnant.name.submit`, `remnant.name.change`, `remnant.name.error.length`, `remnant.name.fallback`, `remnant.name.saved`.

*Screen chrome:* `circle.title`, `common.circle`, `circle.chat.door`, `circle.tab.members`, `circle.tab.leaderboard`, `circle.tab.vault`, `circle.tab.ballots`, `circle.tab.unreachable`.

*Header:* `circle.header.code`, `circle.header.members`, `circle.header.role`, `circle.role.leader`, `circle.role.officer`, `circle.role.member`, `circle.policy.invite`, `circle.policy.open`.

*Create / join:* `circle.create.title`, `circle.create.name`, `circle.create.tag`, `circle.create.openJoin`, `circle.create.submit`, `circle.create.hint`, `circle.join.title`, `circle.join.code`, `circle.join.submit`, `circle.join.hint`.

*Members:* `circle.members.empty`, `circle.member.degraded`, `circle.tenure.days`, `circle.verb.promote`, `circle.verb.demote`, `circle.verb.kick`, `circle.verb.leave`, `circle.verb.copyCode`, `circle.verb.chat`, `circle.verb.refresh`, `circle.leave.confirm`, `circle.leave.cancel`.

*Leaderboard:* `circle.leaderboard.metric`, `circle.leaderboard.members`, `circle.leaderboard.you`, `circle.leaderboard.empty`.

*Vault:* `circle.vault.none`, `circle.vault.address`, `circle.vault.multisig`, `circle.vault.threshold`, `circle.vault.verifiedSigners`, `circle.vault.signers`, `circle.vault.hardware`, `circle.vault.hardware.yes`, `circle.vault.hardware.no`, `circle.vault.verifiedAt`, `circle.vault.register`, `circle.vault.addressField`, `circle.vault.indexField`.

*Vigil / ballots:* `circle.vigil.weight`, `circle.vigil.degraded`, `circle.ballot.none`, `circle.ballot.tier`, `circle.ballot.locked`, `circle.ballot.propose`, `circle.ballot.vote`, `circle.ballot.yourVote`, `circle.ballot.voters`, `circle.ballot.turnout`, `circle.ballot.closes`, `circle.ballot.closed`, `circle.ballot.result.passed`, `circle.ballot.result.failed`, `circle.ballot.result.unknown`, `circle.ballot.perks`.

*Toasts:* `circle.toast.created`, `circle.toast.joined`, `circle.toast.left`, `circle.toast.promoted`, `circle.toast.demoted`, `circle.toast.kicked`, `circle.toast.voted`, `circle.toast.proposed`, `circle.toast.vaultRegistered`, `circle.toast.codeCopied`.

*Errors:* `circle.error.notSignedIn`, `circle.error.server`, `circle.error.unreachable`, `circle.error.badRequest`, `circle.error.rateLimited`, `circle.error.badName`, `circle.error.badTag`, `circle.error.badPolicy`, `circle.error.alreadyInCircle`, `circle.error.codeUnavailable`, `circle.error.identityMissing`, `circle.error.badCode`, `circle.error.noSuchCode`, `circle.error.notInCircle`, `circle.error.leaveRaced`, `circle.error.badTarget`, `circle.error.forbidden`, `circle.error.targetGone`, `circle.error.targetRole`, `circle.error.raced`, `circle.error.ballot.badTier`, `circle.error.ballot.badOptions`, `circle.error.ballot.tierLocked`, `circle.error.ballot.alreadyOpen`, `circle.error.ballot.notFound`, `circle.error.ballot.closed`, `circle.error.ballot.weightUnavailable`, `circle.error.vault.badAddress`, `circle.error.vault.notMultisig`, `circle.error.vault.badIndex`, `circle.error.vault.tooManySigners`, `circle.error.vault.notLeader`, `circle.error.vault.signer`, `circle.error.vault.already`, `circle.error.vault.unreadable`.

**The integration seam between A and B** is the `CircleScreenVM` public surface exactly as declared in section 2, landed in Lane A commit 1. B binds only what is listed there. If B needs a field section 2 does not have, it files it back to A -- **B never adds a computed property to the View.**

**Lead-only (no lane touches it).** `Assets/Editor/Regression/DataRegression.cs` gains exactly this line, beside the existing clan-feature-gate registration at `:894`:

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "circle-screen suite", () => { if (!DeNelle.Editor.Regression.CircleScreenRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[circle-screen] " + r); });
```

## 7. RED-first suite spec

**Suite:** `CircleScreenRegression` -- `Assets/Editor/Regression/CircleScreenRegression.cs`
**Tag:** `[circle-screen]`
**Markers:** `CIRCLE_SCREEN_OK` on pass, `CIRCLE_SCREEN_FAIL: <reason>` on fail -- the `ClanFeatureGateRegression.cs:45,50` shape. Source-lint only, no PlayMode, so it runs inside the headless `DataRegression.RunAll` batch. Never throws (the registration line is Guard-wrapped).

Text is normalised with `RegressionSourceText` first: BANNED patterns over `StripCommentsAndStrings`, REQUIRED patterns over `StripComments` -- the `ManageDumbViewRegression.cs:38-43` rule, because this suite's own prose names every banned token and must not read as a violation.

| Case | Asserts | Revert recipe (must go RED, revert immediately) |
|---|---|---|
| `[circle-self-test]` | every banned regex below matches an in-file fixture it MUST match, before any is trusted against real source (`ManageDumbViewRegression.cs:45-49`) | delete one pattern from the fixture; the case must name it as an untested oracle |
| `[circle-vm-bound]` | `CircleScreenPanel.cs` contains `CircleScreenVM.CreateDefault(` and none of `GameStateService`, `EconomyService.Instance`, `FindAnyObjectByType`, `FindObjectsByType`, `ResourceLedger`, `VillageInventory.Instance` | paste `var s = GameStateService.Current;` into `CircleScreenPanel.Render` |
| `[circle-no-http-in-view]` | `CircleScreenPanel.cs` contains none of `UnityWebRequest`, `BackendRequestSigner`, `BackendBase`, `/api/` | paste a `UnityWebRequest.Get(...)` into the View |
| `[circle-no-literals]` | every `.text =` in `CircleScreenPanel.cs` sources from `LocalizedText(`, `LocalText.Format(` or a VM property; no bare literal longer than 2 chars reaches one | set the title text to a hardcoded sentence |
| `[circle-no-stat-copy]` | **RULING 3.** `CircleScreenPanel.cs` contains no binding to `EffectText` and no percent-bearing literal; `CircleScreenVM.cs` assigns `EffectText` only to `string.Empty` | bind `row.EffectText` into an option tile |
| `[circle-join-ungated]` | **RULING 4.** `CircleScreenVM.cs` and `CircleSource.cs` contain none of `MinimumStake`, `RequiresStake`, `percent_staked`, `vigil_contribution` on any join path, and `CanSubmitJoin` is computed from the code shape alone | add a Vigil term to `CanSubmitJoin` |
| `[circle-name-first]` | **RULING 2.** `CircleScreenVM.cs` declares `CircleState.NeedsName` and `CircleScreenPanel.cs` renders it before any tab row; the name step is reachable with no Circle | reorder so the tab row paints first |
| `[circle-no-group-remnant]` | **RULING 1.** across **22 files** -- the 20 catalogs plus `chat-phrases.json` x2 and `GooglePlayLocalizationVariantPolicy.json` (neither of the last two is in `LocalizationPolicy.json`, so nothing else can see them) -- none of the six renamed keys, `phrase_3`, or the Play `clanChat.noWallet` override still carries the locale group word WO-1859 minted; `common.remnant_chat` is still PRESENT as a KEY; and **`phrase_4` still reads "Hello, Remnants."** because under ruling 1 a Remnant is a player | restore "Remnant Chat" in `de.json`; separately, rewrite `phrase_4` to "Hello, Circles." -- the case must fail on BOTH |
| `[circle-keys-parity]` | every key in the section-6 frozen list exists, non-empty, in all 10 StreamingAssets catalogs AND all 10 Resources mirrors | delete `circle.error.badCode` from `ru.json`; blank `circle.title` in `ko.json`; add `circle.probe` to `en.json` alone |
| `[circle-keys-referenced]` | every frozen key is named by at least one file under `Assets/_Modules/HUD/Circle/`, and no `circle.*` or `remnant.name.*` key is referenced that the catalogs lack | reference `circle.ghost` from the VM |
| `[circle-error-map-total]` | `CircleScreenVM.PlayerFacingKey` names **every** server code the routes can answer with and has a default branch. The expected set is DERIVED, not typed: the suite parses the `CLAN_*` / `DISPLAY_NAME_*` / `PLAYER_ID_*` / `BAD_PAYLOAD` / `SERVER_ERROR` identifiers out of `api/_lib/clan.js`, `api/_lib/clan-ballot.js`, `api/_lib/clan-vault*.js`, `api/_lib/wallet-auth.js` and the two new `api/profile/display-name*.js` files, and requires a case for each (`TouchFloorAuthoringRegression.cs:12-17`: never assert a set by recomputing it from the same literal the code uses) | delete the `CLAN_VAULT_SIGNER_SGT_REUSED` case |
| `[circle-routes-exist]` | every URL literal in `CircleSource.cs` resolves to a real file under `api/clan/` or `api/profile/` | point one call at `/api/clan/members` |
| `[circle-signed]` | every `UnityWebRequest` construction in `CircleSource.cs` is followed by `BackendRequestSigner.TryAttachAsync`, and a false return aborts before `SendWebRequest` | delete one `TryAttachAsync` guard |
| `[circle-interactive-mint]` | **every** write helper in `CircleSource.cs` passes `true` as the 4th argument to `TryAttachAsync` and **every** read helper passes `false` or omits it -- the two sets are DERIVED by parsing the HTTP verb of each helper, never from a typed count (the `TouchFloorAuthoringRegression.cs:29-31` rule) | flip `Create` to false |
| `[circle-touch-floor]` | every authored band constant in `CircleScreenPanel.cs` resolves at or above `ElarionUiKit.MinTouchPx` against the CanvasScaler model, with the floor READ AS A SYMBOL, not a typed number (`TouchFloorAuthoringRegression.cs:19-31`) | set the tab-row band to `0.06f` |
| `[circle-door]` | `PanelId.Circle` is appended not renumbered; `ClanChatPanel` names `PanelRouter.Open(PanelId.Circle)` OUTSIDE its no-Circle branch; `CircleScreenPanelBootstrap` carries `[RuntimeInitializeOnLoadMethod]` with the gate check as its literal first statement | delete the `ClanChatPanel` door line |
| `[circle-dock-untouched]` | `HudKitController.cs` still contains the exact `ClanFeatureGateRegression.cs:37` needle, the gate check still precedes it, `AddDockTab` still reads `const int rows = 3;`, and `SpawnInScene`/`BuildSlideTab` still adds exactly six rows | add a seventh `AddDockTab` call |
| `[circle-instrumented]` | every fetch and every verb in `CircleSource.cs` carries a `FlowTrace.` call, and every list build in the VM sits inside `Guard.Try` | strip the `FlowTrace.Step` from `Join` |
| `[circle-migration-numbered]` | exactly one new file matches `api/migrations/20260918_0038_*.sql` and no existing migration file changed | renumber it 0037 |

**Sequencing.** Lane C writes the suite and the catalogs FIRST and runs it RED against an empty `Assets/_Modules/HUD/Circle/` -- the failure names every missing file, which is the acceptance that the oracle can fail at all. Then A, then B, then re-run. The lead runs `LocalizationBuilder.BuildAll()` in Unity and pastes the `DataRegression.cs` line before the gate.

## 8. Open owner decisions

1. **Dock label.** The gear dock will carry two rows: the existing `common.remnant_chat` (value becomes "Circle Chat") and a new `common.circle` above it. Proposed value: **"Circle"**. Is that the word, or "Your Circle" / "The Circle"? (If she would rather have ONE row that opens the screen and lets the screen's own Chat link reach the chat, that is a smaller dock but it re-points the pinned needle at `ClanFeatureGateRegression.cs:37` -- then the oracle edit is lead-owned and travels in the same commit.)

2. **`display_name` vs the `username` that already exists.** `player_profiles` already carries a nullable `username` with a case-insensitive UNIQUE index (`schema.sql:1010-1012`), a profanity policy (`api/_lib/username-policy.js`, used by `api/profile/username.js:32`) and a rename record (`renamed_at`). Ruling 2 asks for a 1..24-char, non-unique, non-filtered, changeable name. Those are two different fields with two different rules. This plan adds `display_name` as a SECOND column and leaves `username` alone. Confirm that -- or rule that `display_name` REPLACES `username`, in which case the uniqueness index and the profanity gate come off and `api/profile/username.js` is retired, which is a bigger, separate ticket.

3. **Vault: read-only, or read plus the leader register form in v1?** The READ ships either way (the `/api/clan/vigil` vault block is complete -- section 0b). The WRITE is `POST /api/clan/vault/register`, which needs the leader to type a Squads multisig address and can refuse with `CLAN_VAULT_SIGNER_NO_SGT` **naming a signer wallet in the error body** (`vault/register.js:21-29`) -- a surface that has never been player-facing. Ship the register form in v1, or read-only with the form deferred?

4. **Ruling 2 says the leaderboard shows the display name, but the CLAN leaderboard renders no player at any depth** -- `api/_lib/clan.js:943` "NEVER SELECT A WALLET COLUMN HERE", and its rows are `{rank, clanId, name, tag, metric, memberCount}`. So display names cannot appear on the board this screen shows. Did she mean the separate PLAYER board (`api/leaderboard/get.js` / `LeaderboardPanel`)? That is a different screen and a different ticket -- confirm and it gets its own WO.

5. **Ruling 3 versus the shipped perk catalogue.** All eleven server ballot options are stat edges or unit unlocks (section 0c, `clan-ballot.js:192-241`). This plan honours the ruling by never rendering `options[].effect` -- the client shows only the narrative `title` and `description`, and no perk is wired to anything. But the underlying catalogue still SAYS "+15% build speed" on the wire. Two choices: (a) ship v1 with the effect suppressed and raise a follow-up server ticket to re-author `PERK_OPTIONS` into visual/narrative outcomes -- **recommended**; or (b) hold the Ballots tab out of v1 entirely until the catalogue is re-authored. Also: `title`/`description` are SERVER-SUPPLIED ENGLISH with no locale key (`clan-ballot.js:252-256` falls back to the raw option id). Ship the English for those two fields in v1, or mint a `circle.ballot.option.<id>` key per option in all 10 catalogs first?

6. **Do member rows show stake?** `/api/clan/vigil` returns `percent_staked` and `vigil_contribution` per member. `api/clan/vault/register.js:130-146` forbids any NEW copy about Seeker ownership, Genesis Tokens or staking without her explicit review, and ruling 4 says joining is never gated on stake. **This plan shows tenure and role only -- no stake number anywhere** -- unless she rules otherwise.

7. **Ruling 2 says chat shows the display name, and this plan cannot deliver that half.** Members and the Circle screen are covered by `/api/profile/display-names`. **Chat is Cherry-rendered** -- the message list, the author labels and the profile are all inside the embed (`ClanChatPanel.cs:4-10`, `ClanChatVM.cs:6-12`), and "Cherry chat internals" is this WO's explicit non-scope (line 25). Getting a display name into chat means either the embed URL carries it (`ClanChatVM.ISource.BuildEmbedUrl`) or Cherry's own profile does. Neither is designed here. Rule: (a) accept chat names are Cherry's for v1, or (b) raise a follow-up WO for the embed-URL handoff.

8. **`site/clan-chat.html`.** Its `<title>` still says the group word (WO-1859 `:98-104`) and `site/**` is not in any lane here. Fold it into Lane C, or leave it to a separate web-surface ticket?

9. **Open-join.** `clans.join_policy` accepts `open` and `create.js:7-10` stores it, but `join.js` still requires a code, and server-side enforcement is out of this WO's scope. So the create form's open-join toggle stores a policy that changes nothing yet. Ship the toggle with honest wording, or hide it until the server honours it?

---

### Lane C hand-back (2026-09-18, edit-only lane — a CLAIM, to be verified by the lead)

**Status of the WO itself is UNCHANGED (`READY TO IMPLEMENT`).** Lane C is one of three; Lanes A and B
are outstanding, so flipping the WO to DONE would be false. The Lane C deliverables below are complete.

#### Files (3 written, 0 committed — this lane never gates, commits or `git add`s)

| Path | What |
|---|---|
| `Assets/Editor/Regression/CircleScreenRegression.cs` | NEW. The `[circle-screen]` oracle, 19 cases, section 7 verbatim. 187/187 braces, no NULs. |
| `Logs/debug/scratch/sweep-sidecar-wo1870.json` | NEW. `keys[]` (124, English only) + `renames[]` + `excluded[]` + `_openQuestions`. |
| `site/clan-chat.html` | EDITED, ONE LINE: `<title>Remnant Chat</title>` -> `<title>Circle Chat</title>`. |

#### Counts, each measured rather than asserted

* **124 new keys** in the sidecar. Set-diffed against the suite's own `FrozenKeys` array:
  `sidecar 124 / suite 124 / in-suite-not-sidecar [] / in-sidecar-not-suite [] / order identical True`.
  The two lists cannot drift without the diff catching it.
* **120 catalog value renames** — 6 keys x 10 locales x 2 mirror directories. Every `old` value in the
  sidecar was re-read off disk and compared byte-for-byte: **0 mismatches**.
* **+1 `chat-phrases.json` rename** (`phrase_3`, applied to BOTH mirrors — verified byte-identical
  today with `diff`), **+10 `GooglePlayLocalizationVariantPolicy.json` override values**, **+1 site
  title (applied by this lane)**. Total rename surface **132 values across 23 files**.
* **1 explicit exclusion:** `phrase_4` = "Hello, Remnants." is PLAYER sense and stays. Pinned
  POSITIVELY by `[circle-no-group-remnant]`, so a blanket find-and-replace fails the suite.

#### The suite, case by case — what is green NOW and what is RED and why

Nothing short-circuits: all 19 cases run and every failure is collected into one
`CIRCLE_SCREEN_FAIL:` line, so the lead watches them go green one lane at a time instead of seeing one
missing file at a time (section 7 "Sequencing" asks for exactly that).

| Case | State today | Turns green with |
|---|---|---|
| `[circle-self-test]` | **GREEN** (runs against in-file fixtures only) | — |
| `[circle-dock-untouched]` | **GREEN** — needle, gate ordering, `const int rows = 3;`, `const int columns = 2;` and **6** `AddDockTab(_slideDock.panel` call sites all counted in `HudKitController.cs` today, not copied from this doc | — (it must STAY green) |
| `[circle-migration-numbered]` | RED | Lane A (`20260918_0038_player_profiles_display_name.sql`) |
| `[circle-vm-bound]` | RED | Lane B |
| `[circle-no-http-in-view]` | RED | Lane B |
| `[circle-no-literals]` | RED | Lane B |
| `[circle-no-stat-copy]` | RED | Lane A **and** B |
| `[circle-join-ungated]` | RED | Lane A |
| `[circle-name-first]` | RED | Lane A **and** B |
| `[circle-no-group-remnant]` | RED | **the LEAD's locale merge** (22 files) |
| `[circle-keys-parity]` | RED | **the LEAD's locale merge** (20 catalogs) |
| `[circle-keys-referenced]` | RED | Lane A + B + the lead's merge |
| `[circle-error-map-total]` | RED | Lane A (VM map **and** the two `api/profile/display-name*.js` files) |
| `[circle-routes-exist]` | RED | Lane A |
| `[circle-signed]` | RED | Lane A |
| `[circle-interactive-mint]` | RED | Lane A |
| `[circle-touch-floor]` | RED | Lane B |
| `[circle-door]` | RED | Lane B (`PanelId.Circle = 28`, the chat-panel door, the bootstrap) |
| `[circle-instrumented]` | RED | Lane A |

⚠ **THE REGISTRATION LINE IS A FORK IN THE ROAD AND BOTH BRANCHES COST SOMETHING — the lead has to
pick one, and neither is free.** Read at source, not assumed:

* **Commit WITH the line** (`DataRegression.cs:894`, the line section 6 supplies): the whole data gate
  goes **RED** until A, B and the locale merge land. That is section 7's design.
* **Commit WITHOUT the line:** `RegressionMarkerRegression` **RULE 2** (`RegressionMarkerRegression.cs:73-77`,
  opened 2026-09-18) says *"Every file under `Assets/Editor/Regression` that exposes
  `public static bool Run(out string <name>)` is referenced in `DataRegression.RunAll`. An unregistered
  oracle is a file that never runs."* `CircleScreenRegression` exposes exactly that signature, so an
  unregistered commit turns **a different suite** red immediately.
* **The third door, if neither is wanted yet:** RULE 2 allows a suite to opt out *"ONLY by saying so in
  its own header (see `StandaloneOptOutTokens`) — the way `RepairProbeRegression` does."* That is a
  one-line header edit, and it is the lead's call, not this lane's, so it was NOT taken.

**Marker shape corrected during verification.** The suite originally logged `CIRCLE_SCREEN_OK` /
`CIRCLE_SCREEN_FAIL:` only from `RunAll()`. Under the lead's registration line the caller does
`failures.Add(r)` / `log.AppendLine("[circle-screen] " + r)`, so **neither marker would ever have
reached the DataRegression log** — and the marker is what the gate is judged by, never the exit code
(CLAUDE.md §8, memory `gates-report-success-without-proving-it`). Both markers are now the **prefix of
the `reason` string** (the `RaidConfigIdResolveRegression.cs:108,131` shape), and `RunAll` logs `reason`
bare. That also keeps them genuine emitters under `RegressionMarkerRegression` RULE 1(b) — a literal
assigned to an identifier that reaches a sink in the same file.

#### Decisions this lane made, and the reasoning, so the lead can overrule cheaply

1. **Zero Circle types are referenced.** Section 7 says source-lint only, so every case reads files as
   TEXT and the suite compiles and runs today — months before Lane A declares a symbol. A suite that
   `using`-ed the seam would not compile until the thing it polices exists, which is the opposite of
   RED-first. The brief's "compile against the seam interface" is therefore moot here.
2. **Stripper direction is per NEEDLE, not per case.** Section 7's blanket
   banned->`StripCommentsAndStrings` rule would have produced **hollow passes** on three needles whose
   violation IS string content: `/api/` + `BackendBase` (`[circle-no-http-in-view]`), the
   percent-bearing literal (`[circle-no-stat-copy]`), and the quoted `CLAN_*` codes
   (`[circle-error-map-total]`). `RegressionSourceText.cs:29-34` states this rule in its own words.
   Each needle now carries its own `Direction` and the header says so.
3. **`[circle-error-map-total]` derives from QUOTED tokens only,** with comment-only lines dropped
   first. Those server files carry heavy RCA prose that names codes in sentences; counting prose would
   inflate the required set and fail the VM for refusals no route can emit. Deliberate narrowing,
   stated in the case's own summary. `clan-vault*.js` is **globbed**, because the real file is
   **`clan-vaults.js` (plural)** and the hardcoded singular path the plan implies would have silently
   scanned nothing.
   **THE DERIVATION WAS RUN, NOT JUST WRITTEN** (§11B — measuring is not the same as measuring the
   right thing). Over the four server files that exist today it yields **41 codes, zero prose
   pollution**: `clan.js` 18, `clan-ballot.js` 8, `clan-vaults.js` 10, `wallet-auth.js` 5
   (`BAD_PAYLOAD`, `CLAN_RATE_LIMITED`, `PLAYER_ID_BAD_SHAPE`, `PLAYER_ID_MISSING`, `SERVER_ERROR`).
   **That 41 is the target Lane A's `PlayerFacingKey` must cover**, plus `DISPLAY_NAME_*` from its own
   two new files. Three of the 41 belong to the `report-message` route the Circle screen never calls
   (`CLAN_BAD_CLAN_ID`, `CLAN_BAD_MESSAGE_ID`, `CLAN_REPORT_NOT_MEMBER`); they stay required, because
   all three map onto keys that already exist (`circle.error.badRequest` / `circle.error.notInCircle`)
   and a hand-typed exclusion list would be exactly the hearsay the derivation exists to avoid.

7. **TWO REGEXES WERE WRONG AND WERE CAUGHT BY RUNNING THEM AGAINST THE PRECEDENT, NOT BY READING
   THEM.** This is the item that would have cost Lane A a day, so it is written out in full:
   * `[circle-signed]`'s abort check originally accepted only the INLINE guard
     `if (!await ...TryAttachAsync(...))` — which `ClanChatSource.cs:183` uses. But
     `ClanMembershipClient.cs:89-91`, the OTHER file section 6 tells Lane A to copy line-for-line,
     uses the HOISTED shape `bool safeToSend = await ...TryAttachAsync(...); if (!safeToSend)`. The
     case would have been **RED forever against correct code**. Both shapes are now accepted and both
     were verified to match their own file (`inline: chat=True memb=False`, `hoisted: chat=False
     memb=True`).
   * `[circle-interactive-mint]`'s verb detection missed `UnityWebRequest.PostWwwForm(...)`
     (`Post\b` fails before the `W`) and its mint-flag check missed the NAMED argument form
     `allowInteractiveSessionMint: true`. Both widened and re-run: `Post`/`PostWwwForm`/`Put` all
     detected, positional AND named `true` both detected, `false` and an omitted argument both
     correctly not.
   * **A CONSTRAINT ON LANE A FELL OUT OF THAT CHECK, and it must be read before the file is
     written:** `ClanChatSource` splits the request build (`SendReport`, `:167`) from the attach
     (`AttachAndSend`, `:174,183`). If Lane A copies that split, the HTTP verb and the mint flag live
     in different methods and **no reader can tell which of fifteen calls is a read and which is a
     write** — the per-row flag becomes unauditable and `[circle-interactive-mint]` reports the helper
     as ambiguous. **Each of the 15 helpers must build its own request AND call `TryAttachAsync` in
     the SAME method.** The case says so in its own failure text.
4. **Two cases are PROXIES and say so in their own reason strings** — `[circle-door]`'s
   "outside the no-Circle branch" (a bounded 600-char text window, because a source lint cannot
   evaluate a branch) and `[circle-name-first]`'s ordering (textual authoring order in a single
   code-built render path).
5. **`[circle-migration-numbered]` proves only half.** "No existing migration changed" needs git and no
   suite under `Assets/Editor/` shells out to it. Recorded as unproven rather than faked.
6. **`[circle-touch-floor]` declares the authoring convention it enforces:** every tappable band in
   `CircleScreenPanel.cs` is a `private const float <Name>BandH|RowH|CellH = 0.0XXf;` fraction. Lane B
   needs to know that before it writes the View, so it is written into the case's summary. The floor is
   read as the SYMBOL `ElarionUiKit.MinTouchPx` and the scaler model is parsed out of
   `ElarionUiKit.cs`, per `TouchFloorAuthoringRegression.cs:29-31,34-56`.

#### Open items the lead must rule on (all recorded in the sidecar's `_openQuestions`)

* **`circle.ballot.result.<reason>` is short by two.** Read at source: `api/_lib/clan-ballot.js:623-625`
  declares `REASON_PASSED='passed'`, `REASON_NO_VOTES='no_votes'`,
  `REASON_INSUFFICIENT_TURNOUT='insufficient_turnout'` (emitted at `:642` and `:695`). The frozen list
  names only `passed | failed | unknown`, so **two real server reasons fall to `.unknown`**, which reads
  as an error rather than the honest outcome. Not minted here because it is **beyond the frozen list**.
  Recommendation: add `circle.ballot.result.noVotes` + `.insufficientTurnout` (2 keys x 20 files) — and
  if so, the sidecar `keys[]` **and** the suite's `FrozenKeys` array change together.
* **Q3 (vault) and Q9 (open-join) can make `[circle-keys-referenced]` fail on purpose.**
  `circle.vault.register/.addressField/.indexField` and `circle.create.openJoin` are minted so Lane B
  *can* build those controls. If the owner rules them out of v1, the keys are authored-and-unreferenced
  and that case goes red — the lead then drops them from both lists, or has Lane B render the control
  disabled. Flagged now rather than discovered at the gate.
* **Non-English "Circle" words are PROPOSALS** (`proposed: true` in the sidecar): es/pt-BR `Círculo`,
  de `Zirkel`, fr `Cercle`, ru `Круг`, ar `الحلقة`, ja `サークル`, ko `서클`, zh-Hans `圈子`. The oracle
  asserts only the ABSENCE of the retired WO-1859 group word, so it does not depend on which word she
  picks.

#### site/ sweep (the brief's item 4)

`grep -rn -i "remnant" site/` returned **exactly one hit**: `site/clan-chat.html:6`, the `<title>`.
**There is no heading** carrying the word — the page is a bare SDK host with no visible `<h1>`. Applied.
Checked first that nothing greps it: `test/clan-chat-embed.test.js` reads the file (`:39`, `:102`) but
asserts only the ABSENCE of the three app-trusted auth options, and `test/clan-chat-release-gate.test.js:22`
greps `HudKitController` for `common.remnant_chat`, not this page. After the edit the site sweep returns
zero hits.

#### Gates run by this lane

```
$ python tools/gate_brace.py Assets/Editor/Regression/CircleScreenRegression.cs
GATE_BRACE_SUMMARY bad=0 of 1

$ <CLAUDE.md section 1 one-liner + the NUL guard>
Braces balanced (187) OK  |  no NUL bytes
```

Both were made to agree deliberately: `SplitMethods` originally searched for an opening-brace *literal*,
which the section-1 one-liner counts and `CompileGate.BraceBalanced` does not — 188/187 at the one-liner,
clean at the gate, the exact divergence CLAUDE.md section 1 warns about. The signature regex already ends
on that brace, so the index is taken directly and no brace literal is needed at all.

**Not run by this lane, by instruction:** Unity, the compile gate, the regression batch, `git add`,
`git commit`. No Lane A or Lane B file, no `HudKitController.cs`, no `DataRegression.cs`, no locale JSON,
no Unity table, no scene, and none of the other live lanes' files were touched.

---

### Lane C hand-back, REVISION 2 (2026-09-18, after Lane A landed + three owner rulings arrived mid-lane)

Everything above stands except where this revision names it. **Read this section, not the first one, for
the current counts and case list.**

#### What changed and why

**1. The lead ruling: the Remnant name IS the existing `player_profiles.username`.** Confirmed at source
before acting — `api/_lib/username-policy.js:16-20` reads `MIN_LEN = 3`, `MAX_LEN = 16`,
`ALLOWED_RE = /^[A-Za-z0-9_]+$/`, and the file is **unmodified in `git status`**, so that is the live
rule today, not a plan. Ruling 2's "1..24, unfiltered" was never authored.

**2. Three owner rulings arrived while this lane was running, and all three are folded in.**
* The player claims a **FIRST NAME only** — one token, no spaces.
* A Remnant **in** a Circle reads `"{0} of {1}"` → *Sally of Emberwatch* (`remnant.name.composed`).
* A Remnant **with no** Circle reads `"{0} the Lonely"` (`remnant.name.alone`), owner verbatim:
  *"so bob the lonely becomes bob of RiverRun"*.
  Both are **locale keys, not C# concatenations** — the word order is not English's to keep, and the two
  are the two halves of one sentence, so neither may be authored without the other. The `alone` epithet
  is noted for translators as **gentle** — an invitation, not a taunt; it is the first thing a
  Circle-less player reads.
  An earlier coordinator instruction ("3..24, single spaces allowed") was **superseded** by the
  first-name ruling before anything was written to it; the hint English is the single-token form.

**3. Five keys added beyond the section-6 frozen list → the count is now 129, not 124.**

| Key | English | Why it is not in section 6 |
|---|---|---|
| `remnant.name.error.taken` | "That name is taken. Try another." | `USERNAME_TAKEN` — a unique-index refusal the display-name route never had. Lane A A1.3 is right that collapsing it into `circle.error.badRequest` leaves the player at a dead end; it is the COMMON failure on a unique column. |
| `remnant.name.error.chars` | "Letters, digits and underscores only, 3 to 16 characters." | `USERNAME_INVALID_CHARS` + the two length codes. States the whole rule so the player never guesses which half they broke. |
| `remnant.name.error.rejected` | "That name is not allowed. Try another." | `USERNAME_REJECTED` — the denylist at `username-policy.js:25-29`. Deliberately does not echo the name or name the list. |
| `remnant.name.composed` | `"{0} of {1}"` | Owner ruling, above. |
| `remnant.name.alone` | `"{0} the Lonely"` | Owner ruling, above. |

Two existing keys were **re-worded to the live policy**: `remnant.name.hint` is now
**"Your first name, e.g. Sally"** (the plan's "Sally the Wise" as literal input would be
`USERNAME_INVALID_CHARS` — a hint that teaches the player to fail), and `remnant.name.error.length` now
says **3 to 16**, not 1 to 24.

Re-verified after the change: sidecar **129** / suite `FrozenKeys` **129**, set-diff empty both ways,
**order identical**, and **0 non-ASCII characters** across all 129 English values.

#### 4. Four suite cases rewritten to Lane A's real shape — and every one was SIMULATED against the landed file, not reasoned about

| Case | What it asserted | What it asserts now |
|---|---|---|
| `[circle-interactive-mint]` | the mint flag inside each helper's method body | **the flag at each of the 16 CALL SITES.** Lane A funnels everything through `SendGet`/`SendPost` → `AttachAndSend`, so the verb and the flag are both arguments of one call. A per-body parse would hunt inside `Join`/`Promote`/`Kick` and find nothing — correct code, red oracle. A paren-balancing `ArgList` helper was added because the argument lists contain their own calls (`MeUrl + "?playerId=" + Escape(...)`), which a first-close-paren regex truncates before reaching the flag. **Simulated: 10 writes all true, 6 reads all false, 0 offenders** — matching Lane A's stated `:117,123,130,137,145,280` false / ten true. |
| `[circle-signed]` | attach-count vs send-count, and proximity | **one sender:** no `SendWebRequest` outside `AttachAndSend`, attach before send, abort between. Counting 1 attach against 16 call sites would have failed working code outright. **Simulated: sendsTotal 1 / inAttachAndSend 1 / attach@193 < send@613 / hoisted guard found → PASS.** ⚠ A first draft scanned only the span *between* attach and send and went **RED against correct code**: in the hoisted shape `bool safeToSend = await ...TryAttachAsync(...)` the declaration sits BEFORE the attach token, so a window starting at the attach can never contain it. Caught by running it, not by reading it. |
| `[circle-migration-numbered]` | exactly one `20260918_0038_*.sql` | **DELETED, not disabled.** There is no migration to number. A case kept as a shell that asserts nothing is the hollow pass `RegressionMarkerRegression` RULE 4 exists to stop. Coverage did not vanish — it moved house: `test/profile-usernames.test.js` already asserts the absence of any `api/migrations/*display_name*.sql` and any `api/profile/display-name*.js`. **The suite is 18 cases, not 19.** |
| `[circle-error-map-total]` | derived from `api/profile/display-name*.js` | derived from **`api/profile/username.js` + `api/_lib/username-policy.js` + `api/profile/usernames.js`**, and the token prefix set swapped `DISPLAY_NAME_` → `USERNAME_`. **Simulated: 46 codes derived, `PlayerFacingKey` maps ALL 46, default branch present, 0 unmapped.** |

#### 5. Revised case list — SIX are green against the tree as it stands today

Simulated case by case against Lane A's landed files (`CircleScreenVM.cs`, `CircleSource.cs`) and the
live tree. **Not compiled** — that is the lead's gate.

| Case | State | Evidence / what it still waits on |
|---|---|---|
| `[circle-self-test]` | **GREEN** | in-file fixtures only |
| `[circle-dock-untouched]` | **GREEN** | needle + gate ordering + `rows = 3` + `columns = 2` + **6** `AddDockTab` sites, all counted in `HudKitController.cs` |
| `[circle-join-ungated]` | **GREEN** | no stake symbol in either file; `CanSubmitJoin = IsValidCode(JoinCode)` — code shape alone |
| `[circle-routes-exist]` | **GREEN** | all 15 URL consts resolve to a real handler (each `api/...js` confirmed present) |
| `[circle-signed]` | **GREEN** | simulated, above |
| `[circle-interactive-mint]` | **GREEN** | simulated, above |
| `[circle-error-map-total]` | **GREEN** | 46 derived, 46 mapped, default branch present |
| `[circle-instrumented]` | **GREEN** | `AttachAndSend` carries FlowTrace; VM has **9** `Guard.Try` wrappers (floor is 5) |
| `[circle-no-stat-copy]` (VM half) | **GREEN** | `EffectText` declared, never assigned a value |
| `[circle-name-first]` (VM half) | **GREEN** | `NeedsName` + `SubmitDisplayName()` both present |
| `[circle-no-group-remnant]` | RED | **the lead's locale merge** (22 files) |
| `[circle-keys-parity]` | RED | **the lead's locale merge** (20 catalogs x 129 keys) |
| `[circle-keys-referenced]` | RED | the merge + Lane B |
| `[circle-vm-bound]` | RED | **Lane B** |
| `[circle-no-http-in-view]` | RED | **Lane B** |
| `[circle-no-literals]` | RED | **Lane B** |
| `[circle-touch-floor]` | RED | **Lane B** |
| `[circle-door]` | RED | **Lane B** (`PanelId.Circle = 28`, the chat-panel door, the bootstrap) |

So the suite's remaining reds are **Lane B plus the locale merge**. Nothing is waiting on Lane A.

#### 6. Still outstanding for the lead

* **The registration-line fork is unchanged** and still needs a decision — see the first hand-back: with
  the line the data gate is red until B + the merge land; without it `RegressionMarkerRegression` RULE 2
  (`RegressionMarkerRegression.cs:73-77`) goes red because the file exposes `Run(out string)` and is
  unregistered; the only sanctioned third door is a `StandaloneOptOutTokens` header opt-out, which is the
  lead's call and was not taken by this lane.
* **`circle.ballot.result.<reason>` is still short by two** (`no_votes`, `insufficient_turnout` —
  `api/_lib/clan-ballot.js:623-625`). Unchanged by anything above and still needs a ruling.
* **Q3 / Q9 keys are authored ahead of their controls.** If the owner rules the vault register form or the
  open-join toggle out of v1, `[circle-keys-referenced]` fails on the unreferenced keys — drop them from
  BOTH lists, or have Lane B render the control disabled.
* **Nine non-English "Circle" words are still PROPOSALS** (`proposed: true`). The oracle asserts only the
  absence of the retired word, so it does not depend on which she picks.
* **`.meta` files are absent** for the new `.cs` — Unity mints them on the next editor open. An edit-only
  seat inventing GUIDs is how a duplicate-GUID import break starts.

#### 7. Gates, re-run after every change above

```
$ python tools/gate_brace.py Assets/Editor/Regression/CircleScreenRegression.cs
GATE_BRACE_SUMMARY bad=0 of 1

$ <CLAUDE.md section 1 one-liner + the NUL guard>
Braces balanced OK  |  no NUL bytes
```

**Concurrency note for the reconciler:** the Edit tool reported this WO changed on disk three times while
this lane was working (Lane A appending its own hand-back). Every Lane C edit applied cleanly and appended
at the tail; the lead reconciles by explicit path as usual.

---

# Lane A hand-back (2026-09-18, edit-only lane — nothing gated, nothing committed, nothing staged)

**Status of this lane:** code complete and self-verified as far as an edit-only seat can go. The WO itself stays **READY TO IMPLEMENT** because Lanes B and C are still outstanding; this section is a CLAIM for the lead to verify, not a close.

## A1. The lead ruling that overrides the plan, recorded where it binds

**The Remnant name IS the existing `player_profiles.username`, written by the existing `POST /api/profile/username` (WO-129).** Per the lead's 2026-09-18 ruling (reuse, never reinvent):

* **NOT created:** `api/migrations/20260918_0038_player_profiles_display_name.sql`, the `display_name` / `display_name_set_at` columns, the `api/schema.sql` description block, `api/profile/display-name.js`, `test/profile-display-name.test.js`. **Row 15 of the section-3 API table is REPLACED by `api/profile/username.js`.**
* **Both live gates STAY** — the case-insensitive unique index (`api/schema.sql:1010-1012`) and the profanity/format gate (`api/_lib/username-policy.js`, called at `api/profile/username.js:67`). A demo does not remove a live safety gate.
* **Consequences the plan did not anticipate, all read at source and all handled:**
  1. **The real name rule is 3..16 chars of `[A-Za-z0-9_]`** (`username-policy.js:16-21`), **not** ruling 2's "1..24, unfiltered". `CircleScreenVM.NameMinLength/NameMaxLength/IsValidName` mirror the SERVER, so no button promises what the route refuses. **⛔ THE POLICY FILE IS UNCHANGED AND STAYS UNCHANGED** — a relaxation to allow spaces was considered and then **CANCELLED** by the owner refinement in A1.6; `api/_lib/username-policy.js` was never edited (`git diff` on it is empty), so the unique index, the profanity gate and both existing callers (`api/profile/username.js`, `api/_lib/patron-name.js`) are byte-identical to HEAD.
  6. **⛔ THE SHOWN NAME IS COMPOSED, AND THE PLAYER ONLY TYPES THE FIRST TOKEN** (owner, 2026-09-18). The player picks a **FIRST NAME** — one token, which is exactly what the untouched 3..16 `[A-Za-z0-9_]` rule already enforces — and the GAME supplies the rest: **"Bob of RiverRun"** when they hold a Circle, **"Bob the Lonely"** when they hold none. This is why the policy did not need to change: the space was never the player's to type. `CircleScreenVM.ComposeDisplayName(firstName, circleName)` is the one pure function that does it, and **neither connective is hardcoded** — `remnant.name.composed` = `"{0} of {1}"` and `remnant.name.alone` = `"{0} the Lonely"`, so a locale that words it differently has somewhere to put its wording. **⚠ A nameless member is NEVER composed** — "7QxK...XeFL of The Long Watch" reads as a bug, so the wallet-truncated fallback stands alone with no connective and no epithet.
  2. **`username.js` does not speak the clan envelope.** Its body is `{wallet, username}` (not `{playerId, displayName}`) and its business failures are **HTTP 200 `{success:false, error:'USERNAME_TAKEN'|...}`**, not `{ok:false, code}`. `CircleWire.RefusalCode` reads BOTH envelopes, so the VM needs no second path — pinned by `a_taken_name_is_a_200_business_failure_and_still_reads_as_a_refusal`.
  3. **FIVE NEW locale keys are required that the section-6 frozen list does not contain** (Lane C has since minted all five — its hand-back's "Five keys added beyond the frozen list" table is the same set). Two are the composed name (A1.6) — **`remnant.name.composed` = `"{0} of {1}"`** and **`remnant.name.alone` = `"{0} the Lonely"`**; both take positional args and must keep `{0}`/`{1}` in every locale. The other three are refusals the username rail has and the plan's `display-name` route did not: **`remnant.name.error.taken`**, **`remnant.name.error.chars`**, **`remnant.name.error.rejected`** (`remnant.name.error.length` is already frozen). **⚠ LANE C MUST MINT THESE IN ALL 10 CATALOGS + BOTH MIRRORS, or `[circle-keys-referenced]` is RED.** Do not collapse them into `circle.error.badRequest`: "that name is taken" is the COMMON failure on a unique column, and a generic error there is an unexplainable dead end.
  4. **`api/profile/username.js` authenticates via `verifyAndConsume` → `verifyWallet`, which DOES accept `X-Session`** (`api/_lib/wallet-auth.js:562-583`, read at source). So a session-authed wallet player can claim a name from the phone — this was checked precisely because a nonce-only route would have 401'd every rename silently. **Proven, not assumed.** ⚠ It is **wallet-shaped ids only** (no `play-` rail), which is the same narrowing every clan route already has (`api/_lib/clan-http.js` requires `auth.mode === 'wallet'`), so the Circle screen is wallet-only end to end and `CircleState.NoWallet` names that without a round trip.
  5. **No existing client caller was found to reuse.** `api/profile/username.js`'s header names a `ProfileService (Core)` client; `grep -rln "profile/username" Assets/ --include=*.cs` and `grep -rln ProfileService Assets/_Modules/` both returned **nothing**. So `CircleSource.SetDisplayName` writes the signed POST itself, on the `ClanChatSource.SendReport`/`AttachAndSend` shape.

**Row 16 (the batch read) WAS needed and was added.** Grepped first: the only `player_profiles` readers are `api/profile/get.js` (ONE wallet, plus a `leaderboard_scores` join for headline stats), `api/leaderboard/get.js`, `api/showcase/top.js` and `api/admin/db.js` — **no batch read of usernames by playerIds existed**.

## A2. Files created (all new; no existing file was edited by this lane)

| File | Lines | What it is |
|---|---|---|
| `Assets/_Modules/HUD/Circle/CircleWire.cs` | 1-338 | `[Serializable]` JsonUtility DTOs for `/me`, `/vigil` (+ vault), `/leaderboard`, `/ballot/current`, `/vault/register`, `/profile/usernames`, `/profile/username`, plus `Parse<T>` / `RefusalCode` / `IsOk`. Never throws. |
| `Assets/_Modules/HUD/Circle/CircleScreenVM.cs` | 1-1658 | The whole screen: section 2's surface verbatim, the state machine, every verb, `ComposeDisplayName`, `ResultKeyFor`, `PlayerFacingKey`. No `UnityEngine` types. |
| `Assets/_Modules/HUD/Circle/CircleSource.cs` | 1-482 | The live `ISource`: 5 signed reads + 10 signed writes (+1 room-refresh read = **16** call sites), `UnityWebRequest` + `BackendRequestSigner.TryAttachAsync`, clipboard, chat door. |
| `api/profile/usernames.js` | 1-134 | **The one new server file.** `GET ?playerId=<caller>&playerIds=a,b,c`, `authenticate()`, capped at 64, de-duplicated, `SELECT wallet, username FROM player_profiles WHERE wallet = ANY(...)`. |
| `test/profile-usernames.test.js` | 1-192 | Node source-lint in the `test/clan-chat-embed.test.js` idiom. **12/12 pass.** |
| `Assets/Tests/EditMode/CircleScreenVMTests.cs` | 1-822 | **32** cases driving the VM with a fake `ISource` and the routes' own body shapes, including the three composed-name cases (composed / alone / never-composed fallback). |
| `Assets/Tests/EditMode/CircleErrorMapTests.cs` | 1-190 | **9** cases over the refusal table, with the expected set **DERIVED** by scraping quoted `CLAN_*` literals out of `api/_lib/clan*.js` + `api/clan/**` (36 codes found), never typed. |

**`.meta` files are deliberately absent** — Unity mints them on the next editor open; an edit-only seat inventing GUIDs is how a duplicate-GUID import break starts.

## A3. ⛔ THE A→B SEAM, VERBATIM. Lane B compiles against exactly this and nothing else.

```csharp
public sealed class CircleScreenVM : IPanelViewModel, IDisposable
{
    public interface ISource
    {
        event Action Changed;
        string WalletAddress { get; }
        bool IsGuest { get; }
        void FetchMe(Action<string, long> done);
        void FetchVigil(Action<string, long> done);
        void FetchLeaderboard(int limit, Action<string, long> done);
        void FetchBallot(Action<string, long> done);
        void FetchDisplayNames(string[] playerIds, Action<string, long> done);
        void SetDisplayName(string name, Action<string, long> done);
        void Create(string name, string tag, string joinPolicy, Action<string, long> done);
        void Join(string code, Action<string, long> done);
        void Leave(Action<string, long> done);
        void Promote(string targetPlayerId, Action<string, long> done);
        void Demote(string targetPlayerId, Action<string, long> done);
        void Kick(string targetPlayerId, Action<string, long> done);
        void ProposeBallot(int tier, string[] optionIds, Action<string, long> done);
        void Vote(string ballotId, string optionId, Action<string, long> done);
        void RegisterVault(string vaultAddress, int vaultIndex, Action<string, long> done);
        void CopyToClipboard(string text);
        void OpenChat();
    }

    public enum CircleState { NoWallet = 0, NeedsName = 1, Loading = 2, NotInCircle = 3, InCircle = 4, Unreachable = 5 }
    public enum CircleTab   { Members = 0, Leaderboard = 1, Vault = 2, Ballots = 3 }

    public static CircleScreenVM CreateDefault(Action onClose);
    public CircleScreenVM(ISource source, Action onClose);

    public string Title { get; }                      // LocalizedText("circle.title").Resolve()
    public event Action Changed;
    public void Close();
    public void Dispose();

    public CircleState State { get; }
    public CircleTab ActiveTab { get; }
    public sealed class TabVM { public CircleTab Id; public string LabelKey; public bool IsActive; public Action Activate; }
    public IReadOnlyList<TabVM> Tabs { get; }

    public string MyDisplayName { get; }      // the RAW first name — seeds the rename field
    public string MyDisplayNameText { get; }  // ADDED — the COMPOSED name the header renders
    public bool HasDisplayName { get; }
    public string MyNameFallbackText { get; }  public string NameDraft { get; }  public bool CanSubmitName { get; }
    public string NamePromptKey { get; }  public string NameHintKey { get; }
    public string NameFieldKey { get; }   public string NameSubmitKey { get; }
    public void SetNameDraft(string s);  public void SubmitDisplayName();
    public void EditDisplayName();       public void CancelEditName();   // ADDED — see A4

    public string CircleId { get; }  public string CircleName { get; }  public string CircleTag { get; }
    public string CircleCode { get; }  public string JoinPolicy { get; }  public string JoinPolicyLabelKey { get; }
    public int MemberCount { get; }  public string MemberCountText { get; }
    public string MyRole { get; }  public string MyRoleLabelKey { get; }
    public string CreatedAtIso { get; }  public string JoinedAtIso { get; }
    public bool IsLeader { get; }  public bool IsOfficer { get; }

    public string CreateName { get; }  public string CreateTag { get; }  public bool CreateOpenJoin { get; }
    public bool CanSubmitCreate { get; }  public string CreateHintKey { get; }
    public string JoinCode { get; }  public bool CanSubmitJoin { get; }  public string JoinHintKey { get; }
    public void SetCreateName(string s);  public void SetCreateTag(string s);  public void ToggleCreateOpenJoin();
    public void SetJoinCode(string s);    public void SetVaultAddress(string s);  public void SetVaultIndex(int i);

    public sealed class MemberRowVM {
        public string PlayerId; public string DisplayNameText; public bool HasDisplayName;
        public string Role; public string RoleLabelKey; public string TenureText;
        public bool Degraded; public string DegradedLabelKey; public bool IsMe;
        public bool CanPromote; public bool CanDemote; public bool CanKick;
        public Action Promote; public Action Demote; public Action Kick; }
    public IReadOnlyList<MemberRowVM> Members { get; }  public string MembersEmptyKey { get; }

    public sealed class LeaderRowVM {
        public int Rank; public string RankText; public string CircleId; public string Name; public string Tag;
        public string MetricText; public int MemberCount; public string MemberCountText;
        public bool IsMine; public string MineLabelKey; }
    public IReadOnlyList<LeaderRowVM> Leaderboard { get; }  public string LeaderboardEmptyKey { get; }

    public bool HasVault { get; }
    public sealed class VaultRowVM { public string LabelKey; public string ValueText; }
    public IReadOnlyList<VaultRowVM> VaultRows { get; }
    public IReadOnlyList<string> VaultSignerShortTexts { get; }
    public bool CanRegisterVault { get; }  public string VaultAddressInput { get; }
    public int VaultIndexInput { get; }     public string VaultEmptyKey { get; }

    public double VigilWeight { get; }  public bool VigilDegraded { get; }
    public string VigilWeightText { get; }  public string VigilDegradedKey { get; }
    public int EpochIndex { get; }  public string EpochEndsAtIso { get; }

    public sealed class BallotChoiceVM { public string OptionId; public string TitleText; public string DescriptionText; }
    public sealed class BallotTierVM {
        public int Tier; public string TierText; public double Threshold; public string ThresholdText;
        public bool Unlocked; public string LockedLabelKey; public bool CanPropose; public Action Propose;
        public IReadOnlyList<BallotChoiceVM> CatalogOptions; }   // ADDED — the slate a proposal opens
    public IReadOnlyList<BallotTierVM> BallotTiers { get; }

    public bool HasBallot { get; }  public string BallotId { get; }  public int BallotTier { get; }
    public bool BallotClosed { get; }  public string BallotOpenedAtIso { get; }
    public string BallotClosesAtIso { get; }  public string MyVoteOptionId { get; }

    public sealed class BallotOptionRowVM {
        public string OptionId; public string TitleText; public string DescriptionText;
        public string EffectText;               // RULING 3 — ALWAYS string.Empty
        public double Weight; public string WeightText; public int Voters; public string VotersText;
        public bool IsMyVote; public string MyVoteLabelKey; public bool CanVote; public Action Vote; }
    public IReadOnlyList<BallotOptionRowVM> BallotOptions { get; }

    public string TurnoutText { get; }  public bool TurnoutClears { get; }
    public bool HasResult { get; }  public bool ResultPassed { get; }
    public string ResultReasonKey { get; }  public string ResultWinningOptionTitleText { get; }
    public sealed class PerkRowVM { public int Tier; public string TitleText; public string ActivatedAtIso; public string ExpiresAtIso; }
    public IReadOnlyList<PerkRowVM> Perks { get; }  public string BallotsEmptyKey { get; }

    public void SelectTab(CircleTab tab);  public void RefreshAll();
    public void SubmitCreate();  public void SubmitJoin();  public void CopyCode();
    public void RequestLeave();  public void CancelLeave();  public void ConfirmLeave();
    public void Promote(string targetPlayerId);  public void Demote(string targetPlayerId);
    public void Kick(string targetPlayerId);     public void ProposeTier(int tier);  public void Vote(string optionId);
    public void SubmitRegisterVault();  public void OpenChat();
    public bool LeaveConfirmArmed { get; }  public string LeaveConfirmKey { get; }  public bool IsBusy { get; }

    public string ErrorKey { get; }  public string ToastKey { get; }  public void ClearToast();
    public static string PlayerFacingKey(string serverCode, long httpStatus);

    // helpers the View may READ but must never re-implement
    public string NameFor(string playerId);          // the RAW first name, or the truncated id
    public string DisplayNameFor(string playerId);   // ADDED — COMPOSED, or the truncated id alone
    public static string ComposeDisplayName(string firstName, string circleName);  // ADDED — pure
    public static string ResultKeyFor(string reason);   // ADDED — the ballot outcome table (A4.4)
    public static string Truncate(string value);
    public static string FormatNumber(double value);
    public static bool IsValidName(string s);
    public static bool IsValidCode(string code);
    public const int NameMinLength = 3;   public const int NameMaxLength = 16;
    public const int CircleNameMaxLength = 32;  public const int CircleTagMaxLength = 5;  public const int CircleCodeLength = 6;
}
```

**Additions to section 2, all additive (nothing was removed or renamed):** `MyDisplayNameText` + `ComposeDisplayName` + `DisplayNameFor` (A1.6 — **Lane B binds `MyDisplayNameText` and `MemberRowVM.DisplayNameText`, never the raw `MyDisplayName`**, and composes nothing itself), `BallotChoiceVM` + `BallotTierVM.CatalogOptions` (A4.1), `CancelEditName` (A4.2), `NameFor`, `Truncate`, `FormatNumber`, `IsValidName`, `IsValidCode`, the five length constants, and the setters `SetCreateName` / `SetCreateTag` / `ToggleCreateOpenJoin` / `SetJoinCode` / `SetVaultAddress` / `SetVaultIndex` (named in section 2's prose but not in its code block). `ProposeTier(int)` and `Vote(string optionId)` are the public forms behind `BallotTierVM.Propose` and `BallotOptionRowVM.Vote`.

## A4. Three findings this lane made AFTER the plan was written, and what each cost

**A4.1 — ⛔ THE PLAN'S PROPOSE VERB WOULD HAVE FAILED EVERY SINGLE TIME, and it was nearly shipped that way.** Section 2's `ISource.ProposeBallot(int tier, string[] optionIds, ...)` gave no rule for the slate, and a first draft of this lane sent `new string[0]` on the assumption "the server picks the options for the tier". **That assumption was never read at source and it is FALSE.** `api/_lib/clan-ballot.js:265` — `if (!Array.isArray(raw) || raw.length === 0) return { ok:false, code: BAD_OPTIONS }` — and `:266-271` requires every id to be DISTINCT and to belong to THAT tier's catalogue. An empty slate is a **400 `CLAN_BALLOT_BAD_OPTIONS`**, every time: a Propose button that never works. **The fix, read at source:** the catalogue already rides the tier row — `tierWire` (`api/_lib/clan-ballot.js:885-887`) sends `tiers[].options[] = {option_id, title, effect, description}` on every `/ballot/current`. So `CircleWire.BallotTierDto` now declares `options[]` (**without** `effect` — ruling 3 holds), `BallotTierVM.CatalogOptions` publishes the slate for the View, and `ProposeTier` sends exactly the ids the server published. `CanPropose` also requires a non-empty catalogue, so a tier that arrived without one is not offered instead of failing on press. Pinned by `proposing_sends_the_servers_own_tier_catalogue_and_never_an_empty_slate`.

**A4.4 — ⚠ TWO MORE KEYS LANE C MUST MINT, and they arrived after its 129-key sidecar was verified.** `ResultReasonKey` now maps the server's reason VALUE instead of only its emptiness. The server answers exactly three reasons — `passed`, `no_votes`, `insufficient_turnout` (declared `api/_lib/clan-ballot.js:623-625`, produced at `:642` and `:695`, read at source) — and the section-6 frozen list has only `passed` / `failed` / `unknown`, so the two failing reasons were collapsing into one sentence. **`circle.ballot.result.noVotes`** and **`circle.ballot.result.insufficientTurnout`** are therefore NEW required keys; `circle.ballot.result.failed` stays as the catch-all for an unfamiliar future reason, and `.unknown` for an empty one. **Why it matters and is not pedantry:** "nobody voted" is a nudge to vote, "not enough of you voted" says the votes cast were real but too few — collapsing them discards the only half a member can act on. **Sidecar count moves 129 → 131.** Pinned by `a_closed_ballot_explains_WHY_it_closed_and_the_two_failures_are_not_one`, which drives all five branches plus a real closed-ballot body. The mapping lives in `public static string ResultKeyFor(string reason)` — an ADDITIVE seam member.

**A4.2 — `CancelEditName()` was added because `EditDisplayName()` had no way back.** A named player who tapped "change" and reconsidered was held in `NeedsName` until they submitted a valid name or closed the whole screen. Cancel restores `InCircle`/`NotInCircle` — and **deliberately does nothing for a player who has no name yet**, for whom the claim step is the door, not a detour.

**A4.3 — the name lookup now distinguishes "no name" from "could not ask".** A failed `/api/profile/usernames` (transport 0, 401, 500) used to fall through into `NeedsName`, which would have told an **offline player to claim a name they already hold** and then offered to rename them — the `ClanMembershipClient.cs:27-31` hold-previous rule, in its worst form. It now enters `Unreachable` **and re-arms the check**, so the next `RefreshAll` asks again instead of skipping the name for the life of the VM. Pinned by `a_name_lookup_that_could_not_be_answered_is_never_read_as_a_nameless_player`.

## A4b. Notes for the other lanes

* **Lane B:** the View binds `CircleScreenVM.CreateDefault(...)` and switches on `State` alone. `NeedsName` renders BEFORE any tab row (`[circle-name-first]`). Every fact that could be a colour is already a KEY on the VM (`RoleLabelKey`, `DegradedLabelKey`, `MineLabelKey`, `MyVoteLabelKey`, `LockedLabelKey`, `circle.vault.hardware.yes|.no`) — render the word. The View needs no HTTP, no `FindAnyObjectByType` (the chat door is `vm.OpenChat()`), and no formatting: `Truncate` / `FormatNumber` / every `*Text` are already done.
* **Lane C — three suite cases in section 7 must change, and here is exactly why:**
  1. **`[circle-migration-numbered]` must be INVERTED or dropped.** No migration was added. Assert instead that **no** `api/migrations/*display_name*.sql` and **no** `api/profile/display-name*.js` exist — `test/profile-usernames.test.js` already asserts precisely that, so the cheapest move is to drop the case and cite the node test.
  2. **`[circle-error-map-total]`'s source set swaps** the two `api/profile/display-name*.js` files for **`api/profile/username.js` + `api/_lib/username-policy.js` + `api/profile/usernames.js`**. The `USERNAME_*` set is `TOO_SHORT | TOO_LONG | INVALID_CHARS | REJECTED | TAKEN`.
  3. **`[circle-keys-parity]` / `[circle-keys-referenced]`: mint the three NEW keys** in A1.3 (`remnant.name.error.taken|chars|rejected`) alongside the frozen list, or both cases go RED on this lane's files.
* **⚠ TWO SUITE CASES MEET A SHARED SENDER — write them to that shape or they misfire.** `CircleSource` funnels every request through `SendGet` / `SendPost` → `AttachAndSend`.
  * `[circle-interactive-mint]`: **the `allowInteractiveSessionMint` literal is written at each of the 16 call sites** (`CircleSource.cs:117,123,130,137,145` and `:280` pass `false`; `:161,171,179,186,192,198,204,213,222,231` pass `true`). A per-call-site parse sees the literal; a per-*method*-body parse will not find one inside `Promote`/`Kick` etc. beyond the argument, which is exactly where it is.
  * `[circle-signed]`: the `UnityWebRequest` construction lives in `SendGet` (`:293`) / `SendPost` (`:314`) and `TryAttachAsync` lives in `AttachAndSend` (`:336`), so a lint asserting "attach within N lines of construction" **will fail on working code**. Assert instead that (a) `CircleSource.cs` contains no `SendWebRequest` outside `AttachAndSend`, and (b) `AttachAndSend` contains `TryAttachAsync` BEFORE `SendWebRequest` with the false-return abort between them. One sender is the reason the ordering can only be wrong in one place.
* **Known chattiness, recorded rather than fixed:** after create / join / leave the screen asks `/api/clan/me` **three** times — `EndWrite(refresh:true)` → `RefreshAll`, `WithRoomRefresh`'s own `/me`, and then `ClanRoomBinding.Changed` → `RefreshAll`. It is BOUNDED (nothing in `OnMe` writes the binding, so there is no loop) and it buys "one truth for the room" without a second binding. If the lead wants it down to one, the cheap move is to drop `EndWrite`'s refresh for those three verbs and let the binding's `Changed` do it — but that couples the screen's refresh to a static event, so it is a lead call, not a lane one.
* **`[circle-join-ungated]` is satisfied by construction:** `CircleScreenVM.cs` and `CircleSource.cs` contain none of `MinimumStake`, `RequiresStake`, `percent_staked`, `vigil_contribution` (pinned by the node test). The two stake fields are not declared even on the wire DTO.
* **Open questions 1, 3, 4, 5, 6, 7, 8, 9 in section 8 are UNTOUCHED by this lane** and still need the owner. This lane shipped the Vault **read plus the leader-only register form** (Q3) because `CanRegisterVault` is `IsLeader && !HasVault` and hiding it entirely would have needed a decision it did not have; if the owner rules read-only, Lane B simply does not render the form and no VM change is needed.

## A5. Verification run by this lane (edit-only: no Unity, no gate, no commit, no `git add`)

```
$ python tools/gate_brace.py <the 5 .cs files>
GATE_BRACE_SUMMARY bad=0 of 5          (exit 0)

$ CLAUDE.md §1 brace + NUL one-liner
Assets/_Modules/HUD/Circle/CircleWire.cs: braces balanced (33) OK, no NUL bytes
Assets/_Modules/HUD/Circle/CircleScreenVM.cs: braces balanced (235) OK, no NUL bytes
Assets/_Modules/HUD/Circle/CircleSource.cs: braces balanced (78) OK, no NUL bytes
Assets/Tests/EditMode/CircleScreenVMTests.cs: braces balanced (145) OK, no NUL bytes
Assets/Tests/EditMode/CircleErrorMapTests.cs: braces balanced (20) OK, no NUL bytes   (exit 0)

$ node --test test/profile-usernames.test.js
ℹ tests 12   ℹ pass 12   ℹ fail 0

$ git diff --stat -- api/_lib/username-policy.js api/profile/username.js api/_lib/patron-name.js
(empty — the policy and both of its callers are byte-identical to HEAD; the
 cancelled 3..24-with-spaces relaxation was never written, so nothing was reverted)

$ git status --short  (this lane's paths only)
?? Assets/_Modules/HUD/Circle/CircleWire.cs
?? Assets/_Modules/HUD/Circle/CircleScreenVM.cs
?? Assets/_Modules/HUD/Circle/CircleSource.cs
?? Assets/Tests/EditMode/CircleScreenVMTests.cs
?? Assets/Tests/EditMode/CircleErrorMapTests.cs
?? api/profile/usernames.js
?? test/profile-usernames.test.js
 M WorkOrders/WORK_ORDER_1870_full_remnant_screen.md
```

**NOT PROVEN by this lane, and named rather than ticked:** the C# has **not been compiled** (no Unity run from an edit-only seat), the two EditMode fixtures have **not been executed**, and `api/profile/usernames.js` has **not been run against live Neon** — the node test is a source lint, not an integration test. The lead's `COMPILE_GATE_OK` + the EditMode run are what turn this claim into a fact.

---

### Lane C hand-back, REVISION 3 (2026-09-18 — the three lead decisions applied, plus what the oracle caught when it was run against B)

#### The three decisions, applied to the sidecar AND `FrozenKeys` in the same edit

**1. Two ballot-reason keys MINTED** (`passed`/`failed`/`unknown` kept):

| Key | English | Server source |
|---|---|---|
| `circle.ballot.result.noVotes` | "No votes were cast." | `REASON_NO_VOTES` — `api/_lib/clan-ballot.js:624`, emitted `:642`, `:695` |
| `circle.ballot.result.insufficientTurnout` | "Not enough of the Circle voted." | `REASON_INSUFFICIENT_TURNOUT` — `:625` |

**2. FOUR keys REMOVED — ⛔ LANE B MUST NOT REFERENCE THESE:**

| Removed key | Why |
|---|---|
| `circle.create.openJoin` | **Q9 out of v1.** `join.js` still requires a code, so the toggle sets a policy that changes nothing. |
| `circle.vault.register` | **Q3 read-only v1.** |
| `circle.vault.addressField` | Q3 — part of the leader register form. |
| `circle.vault.indexField` | Q3 — part of the leader register form. |

⚠ **The VM SEAM IS UNCHANGED.** `CreateOpenJoin`/`ToggleCreateOpenJoin` and
`CanRegisterVault`/`VaultAddressInput`/`VaultIndexInput`/`SubmitRegisterVault` all stay exactly as Lane A
shipped them — **Lane B simply renders no control, and now has no label to render it with.** The
`circle.error.vault.*` family also STAYS: a read can still meet a 403 or a 503. All four removals are
recorded by name in the sidecar's new `_removedFromV1` block so a later ticket can restore them.

**Net: 129 − 4 + 2 + 1 = 128 keys.** Sidecar 128 / `FrozenKeys` 128, set-diff empty, **order identical**,
0 non-ASCII English values. (The +1 is explained below.)

#### 3. `[circle-keys-referenced]` simulation — and Lane B landed while it was running, which changed the answer

Run against Lane A only: **30 unreferenced**. Then `CircleScreenPanel.cs` appeared in the directory
mid-check, so it was re-run against all four files. **That second run is the one that matters, and it
found three real cross-lane faults plus one this lane fixed:**

```
frozen keys            : 128
UNREFERENCED           : 2   circle.ballot.result.noVotes
                             circle.ballot.result.insufficientTurnout
GHOST references       : 1   circle.create.openJoin
verdict                : RED (2 unreferenced, 1 ghost)   <- 125 of 128 keys clean
```

**Fixed by this lane (the 4th fault): `remnant.name.cancel` was MINTED.**
`CircleScreenPanel.cs:72,315` renders a Cancel face beside Claim — Lane A's `CancelEditName()` from
hand-back A4.2 — against a key **no catalog carried**. `LocalizedText` resolves a missing key to the key
itself, so the player would have read the literal string `remnant.name.cancel` on a live button. Minted
on evidence, not from the plan; this is exactly the gap the oracle exists to find.

**The three that are NOT this lane's to fix — one line each:**

1. **`circle.create.openJoin` is a GHOST (Lane B).** `CircleScreenPanel.cs:78` declares the const and
   `:447-451` builds a whole toggle band for it. The lead just ruled that control out of v1, so the key
   is gone and the reference now points at nothing. **Lane B deletes the const and the band.** This is the
   *only* fault that is a real player-facing break today — the button would render its own key name.
2. **`circle.ballot.result.noVotes` / `.insufficientTurnout` are UNREFERENCED (Lane A).**
   `CircleScreenVM.cs:1039-1040` reads:
   ```csharp
   ResultReasonKey = ResultPassed ? "circle.ballot.result.passed" : "circle.ballot.result.failed";
   if (string.IsNullOrEmpty(b.result.reason)) ResultReasonKey = "circle.ballot.result.unknown";
   ```
   It checks whether `b.result.reason` is EMPTY and never reads its VALUE, so both new keys are
   unreachable and the two real outcomes still collapse into "Did not pass". **Lane A maps the reason
   string** (`no_votes` → `.noVotes`, `insufficient_turnout` → `.insufficientTurnout`, default
   `.unknown`). Until then `[circle-keys-referenced]` stays red on those two, correctly.

#### Case state after all of the above

`[circle-keys-parity]` and `[circle-no-group-remnant]` remain RED pending **the lead's locale merge**
(now 128 keys × 20 catalogs, plus the 22-file rename sweep). `[circle-keys-referenced]` is RED on the
three faults above. The other Lane-B cases (`vm-bound`, `no-http-in-view`, `no-literals`, `touch-floor`,
`door`) were **not** re-simulated against the newly-landed `CircleScreenPanel.cs` — that file arrived
after this lane's scope closed and belongs in the lead's gate, not in a claim from here.

#### Gates, re-run after every change in this revision

```
$ python tools/gate_brace.py Assets/Editor/Regression/CircleScreenRegression.cs
GATE_BRACE_SUMMARY bad=0 of 1

$ <CLAUDE.md section 1 one-liner + the NUL guard>
Braces balanced (189) OK  |  no NUL bytes
```

---

# Lane B hand-back (2026-09-18, edit-only lane — nothing compiled, nothing gated, nothing committed, nothing staged)

**This is a CLAIM for the lead to verify.** No Unity ran from this seat, so the C# has not been
compiled and no screen has been captured. What IS proven below was measured on this machine today:
the brace/NUL gates, and a port of every Lane-B case in `CircleScreenRegression` run against the
files as they now sit on disk.

## B1. Files

| Path | Lines | What |
|---|---|---|
| `Assets/_Modules/HUD/Circle/CircleScreenPanel.cs` | 1-1218 (NEW) | The View. Binds `CircleScreenVM.CreateDefault(...)` and nothing else; switches on `State`; re-renders on `Changed`. |
| `Assets/_Modules/HUD/Circle/CircleScreenPanelBootstrap.cs` | 1-82 (NEW) | Door 2 / the `PanelDoorRegression` D2 root. |
| `Assets/_Modules/Core/UI/PanelRouter.cs` | `:184-194` (EDITED, append only) | `Circle = 28` + its doc comment. `HonestFeedback = 27` untouched, nothing renumbered. |
| `Assets/_Modules/HUD/ClanChatPanel.cs` | `:78-84`, `:186-210`, `:231-241` (EDITED) | Door 1: the always-visible Circle button, its strip, and `OpenCircleScreen()`. |

`.meta` files are deliberately absent for the two new files — Unity mints them on the next editor
open, and an edit-only seat inventing GUIDs is how a duplicate-GUID import break starts (the Lane A
precedent).

## B2. The registration lines, verbatim

```csharp
// CircleScreenPanel.cs:163-164  (Awake)
_panelHandle = PanelManager.Register("Circle", Close, () => IsOpen);
PanelRouter.Register(PanelId.Circle, (Action)Open);

// CircleScreenPanel.cs:169      (OnDestroy)
PanelRouter.Unregister(PanelId.Circle, (Action)Open);
```

`"Circle"` is a DISTINCT `PanelManager` handle from `ClanChatPanel`'s `"Remnant Chat"`
(`ClanChatPanel.cs:86`), which is what makes the modal arbiter close one as the other opens — the
hand-off the two doors depend on.

`PanelId.Circle = 28` is appended at `PanelRouter.cs:194`, after `HonestFeedback = 27`, with a doc
comment that records WHY it is not a gear-drawer row (the 2x3 grid is full — WO-1870 section 5).

## B3. The door sites

**Door 1 — `ClanChatPanel`, visible in EVERY state** (`ClanChatPanel.cs:186-210`, called from
`EnsureBuilt`), calling `PanelRouter.Open(PanelId.Circle)` at `:238`.

> ⛔ **A BUTTON PLACED IN THE CHAT BODY WOULD HAVE BEEN INVISIBLE, AND THAT IS NOT A STYLE POINT.**
> The Cherry embed is a **native WebView laid over `BodyViewport()`** (`ClanChatPanel.cs:217`,
> `:235-253`), and a native surface OCCLUDES the uGUI underneath it. A door authored inside that
> rect would have disappeared the instant the embed mounted — i.e. in exactly the state the WO says
> it must be visible in, and it would have looked correct in every editor screenshot taken before
> the plugin loaded. So the strip is **subtracted from the host area first**: `CircleDoorStrip` is
> anchored to the bottom of the body at `CircleDoorStripPx`, a second child `EmbedSurface` takes
> what is left, and **`_bodyHost` now points at `EmbedSurface`** — which is the rect `BodyViewport()`
> measures and hands the web host. `_statusText` moved onto the same sub-rect so it cannot collide
> with the strip either.
> `CircleDoorStripPx = ElarionUiKit.MinTouchPx / 0.84f + 8f` (`:84`) — derived from the SYMBOL, not
> a typed 112, and sized so the button's own 0.08-0.92 slice of the strip still clears the floor.

**Door 2 — `CircleScreenPanelBootstrap`** (`:31` `[RuntimeInitializeOnLoadMethod]`, `:45` the gate
check as the literal first statement, `:70` `new GameObject`).

> ⚠ **THE ATTRIBUTE IS WRITTEN BARE, AND COPYING THE CHAT BOOTSTRAP WOULD HAVE FAILED THE ORACLE.**
> `ClanChatPanelBootstrap.cs:21` spells it
> `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]`, and
> `CircleScreenRegression.CaseDoor` does a literal `IndexOf("[RuntimeInitializeOnLoadMethod]")`,
> which that form does **not** contain. `AfterSceneLoad` is the attribute's own default, so the bare
> form is behaviourally identical and is what the case can see. Named here because "copy the
> precedent" was the obvious move and it was the wrong one.

**Return door** — the screen's own Chat face calls `vm.OpenChat()` through the `ISource` seam, so the
View never runs a finder (`FindAnyObjectByType` is banned at `UiMvvmConformanceRegression.cs:66`).

**`HudKitController.cs` WAS NOT TOUCHED.** `ClanFeatureGateRegression.cs:37`'s needle is byte-identical.

## B4. What the View renders, per state

* **`NeedsName` — authored FIRST in the file, before anything tab-shaped** (`CircleScreenPanel.cs:292`).
  Prompt, hint, a single-line field capped at `CircleScreenVM.NameMaxLength`, a **live preview**, the
  Claim face, and a Cancel face that is offered ONLY to a player who already has a name (for a
  nameless player the step is the door, not a detour — Lane A's A4.2).
  The preview is `CircleScreenVM.ComposeDisplayName(_vm.NameDraft, _vm.CircleName)` — **the VM's own
  compose call. The View never concatenates a name.**
* **`NoWallet` / `Unreachable`** — one sentence chosen by the STATE (`circle.error.notSignedIn` /
  `circle.error.unreachable`) plus Refresh. **`Loading` deliberately carries no sentence**: the screen
  is about to answer, and inventing a "please wait" string would cost an 11th catalog row to say
  nothing.
* **`NotInCircle`** — Create (name, tag, hint, submit) and Join (code, hint, submit) plus Refresh.
  Both submits are gated on the VM's `CanSubmitCreate` / `CanSubmitJoin` and on `IsBusy`, never on
  anything computed here.
* **`InCircle`** — the four section faces are the only pinned band; the scrolling column then
  carries the header (the word "Circle", name, tag, code, role, member count, join policy, and
  **`MyDisplayNameText`** — the composed name, never the raw `MyDisplayName` token), the verb row
  (Copy code / Chat / Refresh / Change name), a two-step Leave whose confirm is a SENTENCE, the
  active-section caption in words, and the section rows.
* **Members** bind `m.DisplayNameText` (composed by the VM), the role WORD, tenure, the degraded WORD,
  and up to three verb chips gated by `CanPromote` / `CanDemote` / `CanKick`.
* **Leaderboard**, **Vault** (READ-ONLY, per the owner ruling — rows + signers, no register form) and
  **Ballots** (Vigil weight, tiers with the server's own published slate as TITLES, the open ballot's
  options as title + description + tally, turnout, result, perks).

**Ruling 3 is held by construction:** the file contains **no `EffectText` binding and no
percent-bearing literal anywhere** (both measured, B6). `SlateOf` publishes `CatalogOptions[].TitleText`
only.

**Ruling 4 is held by construction:** nothing on the join path names a stake, and `CanSubmitJoin` is
read straight off the VM.

**Colour never carries meaning alone:** role, degraded, "this is your Circle", "this is your vote",
tier-locked, join policy and the ballot result are all WORDS resolved from the label keys the VM
publishes. The tints on the active section face are decoration on top of a caption that already names
the active section in words.

## B5. Two design decisions the lead can overrule cheaply

1. **REBUILD IS SHAPE-KEYED, AND A NAIVE `Changed` -> REBUILD WOULD HAVE BROKEN EVERY TEXT FIELD.**
   `SetNameDraft` / `SetCreateName` / `SetCreateTag` / `SetJoinCode` each raise `Changed`, so a View
   that rebuilt its body on every notification would have **destroyed the field the player was typing
   into, on every keystroke** — focus lost, caret lost, one character per tap. So `Repaint` rebuilds
   only when the SHAPE moves (`(int)State * 16 + (int)ActiveTab`); otherwise it patches labels and
   `interactable` in place, and the join field is echoed back with `SetTextWithoutNotify` so the VM's
   upper-casing cannot start a notification loop. The scrolling list is rebuilt on every change,
   which is safe because no tab carries an input.
2. **Bands are FRACTIONS OF THE CANVAS, resolved to FIXED PIXELS once per build.** Ten
   `const float *BandH|*RowH|*CellH` constants, the smallest 0.13. `[circle-touch-floor]` multiplies
   each by the smallest captured reference height (965.4 ref px at 2670x1200), so the smallest legal
   fraction is 112/965.4 = 0.1161 — every band clears it with margin (B6). Each band owns its height
   on BOTH the `RectTransform` and, inside the scroll column, its `LayoutElement`; nothing is left to
   `ClampMinTouch`. The status line is the ONE exception and is sized in pixels
   (`StatusLinePx = 56f`): it is a sentence, not a tap target, and the floor governs what a finger
   must hit.
3. **⛔ ALMOST NOTHING IS A FIXED BAND, AND THE FIRST DRAFT OF THIS FILE GOT THAT WRONG.** Bands are
   absolutely stacked from the top of the body and NOTHING CLIPS THEM. The first draft made every row
   a fixed band, and the arithmetic — done afterwards, which is the lesson — says `NotInCircle` summed
   to **1.35 x canvasH + 108px of gaps = 1411px at 965 ref px**, against a FrameCore body well that
   `ManageScreenPanel.cs` records at **~533 ref px at 2670x1200**. The Join section — *the control the
   owner opened this ticket for* — would have painted below the frame or off-screen, on every device,
   and it would have looked correct in any editor screenshot taken at a taller aspect. Now the FIXED
   set is the section faces (which must stay reachable while the list scrolls) plus the status line,
   and **everything else — the header, the verb row, the leave confirm, both forms and the name
   step — lives in the scrolling column**. `BuildBody` sums the fixed heights, subtracts them from the
   MEASURED well, hands the remainder to the list, and `FlowTrace.Warn`s IN PIXELS before shrinking
   to `MinListPx` — the `ManageScreenPanel` law, this time actually executed rather than quoted.
4. **The in-Circle column is rebuilt on every `Changed`, and the shape key alone would have broken
   two things.** `ShapeOf()` is `State * 16 + ActiveTab`, so arming the Leave confirm does not move
   it: with the leave row as a fixed band, `RequestLeave()` would have set `LeaveConfirmArmed` and
   **nothing would ever have painted the confirm — Leave would have been a dead verb.** The header had
   the same defect in slow motion: written once, it would have shown a stale name after a claim and a
   stale role after a promote. Both are cured by the same move as decision 3 — header, verbs and leave
   are now `ListRow`s inside the column `BuildSectionContent` rebuilds. ⚠ Accepted for v1: a rebuild
   returns the scroll position to the top.

## B6. Suite cases this lane satisfies (section 7), each RE-RUN as a port against the files on disk

A port of each case's own logic (the same regexes, the same `StripComments` direction) was executed
against the current files — this is a measurement, not a reading:

| Case | Result |
|---|---|
| `[circle-vm-bound]` | `CircleScreenVM.CreateDefault(` present; **zero** matches for the banned service/finder set |
| `[circle-no-http-in-view]` | **zero** matches for `UnityWebRequest|BackendRequestSigner|BackendBase|/api/` |
| `[circle-no-literals]` | **35** `.text =` assignments, **0** offenders — every one resolves from `LocalizedText(`, `LocalText.Format(` or a VM property |
| `[circle-no-stat-copy]` (View half) | **zero** `.EffectText` bindings, **zero** percent-bearing literals |
| `[circle-name-first]` | first `NeedsName` at char **7648**; the first tab-shaped token (`Tabs`) at char **21747** |
| `[circle-touch-floor]` | 10 authored bands: `HeaderBandH` 212.4px, `OptionCellH` 212.4, `MemberRowH` 144.8, `VerbRowH`/`FaceRowH`/`FieldRowH`/`ActionRowH` 135.2, `SectionRowH`/`InfoRowH`/`PreviewRowH` 125.5 — all >= 112 |
| `[circle-door]` | `HonestFeedback = 27` intact, `Circle = 28` present; `PanelRouter.Open(PanelId.Circle)` found in `ClanChatPanel` with **no `noClan` reference in the 600 chars before it**; bootstrap carries the bare attribute with the gate check before `new GameObject(` |
| `[circle-keys-referenced]` | over `Assets/_Modules/HUD/Circle/`: **129/129 frozen keys referenced, 0 unreferenced, 0 referenced-but-not-frozen** |
| `[circle-dock-untouched]` | `HudKitController.cs` not opened by this lane |

Lane B also supplies the View half of `PanelDoorRegression` D1+D2 and the exemption
`UiMvvmConformanceRegression` requires (the `CreateDefault` call — **no allow-list entry was added**,
per section 4).

## B7. ⛔ Three things the lead must know before the gate

1. **`CircleScreenVM.ComposeDisplayName(string, string)` is a HARD COMPILE DEPENDENCY on Lane A.**
   The name-claim preview calls it directly (this is the sanctioned "helpers the View may READ but
   must never re-implement" list in A3). It is present in `CircleScreenVM.cs:1561` as read today. If
   Lane A renames or re-signs it, this file stops compiling — it is the one symbol outside A3's
   published surface that Lane B leans on.
2. **`circle.ballot.result.noVotes` and `.insufficientTurnout` are LANE A's to map, not this View's.**
   The View renders whatever `ResultReasonKey` the VM publishes, so the two new keys reach the screen
   for free — but only if `CircleScreenVM` maps the server's `no_votes` / `insufficient_turnout`
   reasons onto them instead of falling through to `.unknown`. Both keys are referenced in the Circle
   folder today, so `[circle-keys-referenced]` will be GREEN whether or not the mapping is right:
   **that case cannot catch this one, which is why it is written out here.**
3. **The join-policy toggle and the vault register form are NOT rendered** (owner ruling 2026-09-18),
   and Lane C removed `circle.create.openJoin` and the three `circle.vault.register*` keys in the same
   breath, so there is no authored-but-unrendered key left behind. `CircleScreenVM.CreateOpenJoin`,
   `ToggleCreateOpenJoin`, `SetVaultAddress`, `SetVaultIndex`, `SubmitRegisterVault` and
   `CanRegisterVault` remain on the VM, unbound — the shape is ready if the owner reverses either.

## B8. Gates run by this lane

```
$ python tools/gate_brace.py Assets/_Modules/HUD/Circle/CircleScreenPanel.cs \
    Assets/_Modules/HUD/Circle/CircleScreenPanelBootstrap.cs \
    Assets/_Modules/HUD/ClanChatPanel.cs Assets/_Modules/Core/UI/PanelRouter.cs
GATE_BRACE_SUMMARY bad=0 of 4                                         (exit 0)

$ CLAUDE.md section 1 brace + NUL one-liner
Assets/_Modules/HUD/Circle/CircleScreenPanel.cs:          braces balanced (88/88), NUL bytes: 0
Assets/_Modules/HUD/Circle/CircleScreenPanelBootstrap.cs: braces balanced (8/8),   NUL bytes: 0
Assets/_Modules/HUD/ClanChatPanel.cs:                     braces balanced (36/36), NUL bytes: 0
Assets/_Modules/Core/UI/PanelRouter.cs:                   braces balanced (23/23), NUL bytes: 0

$ git status --short   (this lane's paths only)
 M Assets/_Modules/Core/UI/PanelRouter.cs
 M Assets/_Modules/HUD/ClanChatPanel.cs
?? Assets/_Modules/HUD/Circle/CircleScreenPanel.cs
?? Assets/_Modules/HUD/Circle/CircleScreenPanelBootstrap.cs
```

**NOT PROVEN by this lane, and named rather than ticked:** the C# has **not been compiled**; the
screen has **never been built or captured**, so nothing here proves it LAYS OUT — `[circle-touch-floor]`
is arithmetic on authored constants and `LayoutOracle.Audit` on the capture path is the authority on
settled rects; and the WebView occlusion fix in `ClanChatPanel` is reasoned from `BodyViewport()`'s
own geometry, **not** observed on a device. The lead's `COMPILE_GATE_OK`, a `UI_CAPTURE_OK` with the
PNGs opened, and one Seeker frame of Circle Chat showing the door are what turn this claim into a fact.

**Not run by this lane, by instruction:** Unity, the compile gate, the regression batch, `git add`,
`git commit`. No Lane A file (`CircleScreenVM.cs`, `CircleSource.cs`, `CircleWire.cs`), no Lane C file,
no `HudKitController.cs`, no `DataRegression.cs`, no locale JSON, no Unity table, no scene, nothing
under `Assets/Tests/` or `Assets/Editor/`, no `api/**`, and none of the other live lanes' files
(`RaidDeployScreen.cs`, `ArmyMusterPanel.cs`, `StarterArmyGrant.cs`, `Village/World/Camps/**`,
`RaidVictoryController.cs`) were touched.
