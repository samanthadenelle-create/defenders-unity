// =============================================================================
// CircleScreenVM — the whole state + every verb of the Circle screen (WO-1870, Lane A).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// ⛔ THE VIEW IS A SKIN. Everything the player can see or press on this screen is a
// property or an Action on THIS class. CircleScreenPanel (Lane B) binds it, re-renders on
// Changed, and computes nothing of its own — that is the UiMvvmConformanceRegression.cs:53
// contract (HardFailOnNew = true, KnownBaseline EMPTY): a new uGUI-constructing View earns
// its exemption from the banned-symbol scan ONLY by carrying IPanelViewModel or a
// .CreateDefault( call, and this class supplies both halves of that.
//
// ⛔ NO UnityEngine TYPES, DELIBERATELY — the ClanChatVM.cs:14-15 rule. Every body arrives
// as a string from ISource and is parsed through CircleWire, so Assets/Tests/EditMode/
// CircleScreenVMTests.cs drives the entire screen with a fake ISource: no scene, no
// network, no Unity player loop.
//
// ── THE FOUR OWNER RULINGS, AND WHERE EACH ONE LIVES IN THIS FILE ────────────
//  1. NAMING. The GROUP is a Circle; a Remnant is a PLAYER. Every key here is circle.* or
//     remnant.name.*; the C# key NAMES of the chat panel are untouched.
//  2. THE PLAYER'S NAME COMES FIRST. CircleState.NeedsName is entered FROM Loading when the
//     name lookup for the caller's own id answers nothing, and from nowhere else.
//     ⚠ LEAD RULING (2026-09-18) THAT OVERRIDES THE PLAN: the name is the EXISTING
//     player_profiles.username, written by the EXISTING POST /api/profile/username — no
//     display_name column, no second endpoint, no migration. So the server's real rule is
//     3..16 characters of [A-Za-z0-9_] plus a profanity gate
//     (api/_lib/username-policy.js:16-21), NOT the plan's "1..24, unfiltered", and
//     CanSubmitName mirrors the SERVER's rule so the player is never told "submit" by a
//     button the server will refuse.
//  3. NO STAT COPY. BallotOptionRowVM.EffectText exists and is ALWAYS string.Empty — the
//     server's magnitude field is not even declared on the DTO (CircleWire.cs).
//  4. JOINING IS NEVER STAKE-GATED. CanSubmitJoin is computed from the CODE SHAPE alone and
//     there is no stake term anywhere in this file.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using DeNelle.Core.Circle;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.HUD
{
    /// <summary>
    /// State + commands for the Circle screen: claim a name, create or join a Circle, then
    /// members / leaderboard / vault / ballots for the Circle you are in.
    /// </summary>
    public sealed class CircleScreenVM : IPanelViewModel, IDisposable
    {
        private const string Sys = "Circle";

        /// <summary>The server's own username rule, read at api/_lib/username-policy.js:16-21.</summary>
        public const int NameMinLength = 3;
        /// <summary>The server's own username rule, read at api/_lib/username-policy.js:17.</summary>
        public const int NameMaxLength = 16;

        /// <summary>clans.name is 1..32 (api/schema.sql:2305-2320 clans_name_len).</summary>
        public const int CircleNameMaxLength = 32;
        /// <summary>clans.tag is 1..5 (api/schema.sql:2305-2320 clans_tag_len).</summary>
        public const int CircleTagMaxLength = 5;
        /// <summary>clans.code is ^[A-HJ-NP-Z2-9]{6}$ — no I, L, O, 0 or 1, on purpose.</summary>
        public const int CircleCodeLength = 6;

        // =====================================================================
        // The service seam. The VM never builds a request, never signs one and never
        // touches UnityWebRequest — CircleSource does all three, and the EditMode suite
        // substitutes a fake that answers with the routes' own documented bodies.
        // =====================================================================
        public interface ISource
        {
            /// <summary>Raised when the wallet or the bound clan room moved underneath us.</summary>
            event Action Changed;

            /// <summary>The proven wallet id, or null when there is none.</summary>
            string WalletAddress { get; }

            /// <summary>True when the identity is a guest id — every clan route is wallet-only.</summary>
            bool IsGuest { get; }

            /// <summary>
            /// Why the last attach aborted. <c>missing</c> / <c>expired</c> are a session
            /// gap (NotSignedIn); anything else with http=0 is Unreachable (WO-1875).
            /// </summary>
            string LastAttachWhy { get; }

            // ── reads: done(body, httpStatus); httpStatus 0 means transport failure ──
            void FetchMe(Action<string, long> done);
            void FetchVigil(Action<string, long> done);
            void FetchLeaderboard(int limit, Action<string, long> done);
            void FetchBallot(Action<string, long> done);
            void FetchDisplayNames(string[] playerIds, Action<string, long> done);

            // ── writes: every one is an explicit player press ──
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

            /// <summary>The invite code's copy/share affordance.</summary>
            void CopyToClipboard(string text);

            /// <summary>Routes to the Cherry chat panel. The View never finds it itself.</summary>
            void OpenChat();

            /// <summary>
            /// The ONE minting Circle read (WO-1875). Opening reads stay non-minting;
            /// this press is the wallet sheet, then the VM re-runs RefreshAll.
            /// </summary>
            void SignIn(Action<string, long> done);
        }

        /// <summary>The ONE thing the View switches on.</summary>
        public enum CircleState
        {
            NoWallet = 0,
            NeedsName = 1,
            Loading = 2,
            NotInCircle = 3,
            InCircle = 4,
            Unreachable = 5,
            /// <summary>Wallet present, no live session. SIGN IN is the minting read (WO-1875).</summary>
            NotSignedIn = 6,
        }

        public enum CircleTab { Members = 0, Leaderboard = 1, Vault = 2, Ballots = 3 }

        public sealed class TabVM
        {
            public CircleTab Id;
            public string LabelKey;
            public bool IsActive;
            public Action Activate;
        }

        public sealed class MemberRowVM
        {
            public string PlayerId;
            public string DisplayNameText;
            public bool HasDisplayName;
            public string Role;
            public string RoleLabelKey;
            public string TenureText;
            public bool Degraded;
            public string DegradedLabelKey;
            public bool IsMe;
            public bool CanPromote;
            public bool CanDemote;
            public bool CanKick;
            public Action Promote;
            public Action Demote;
            public Action Kick;
        }

        public sealed class LeaderRowVM
        {
            public int Rank;
            public string RankText;
            public string CircleId;
            public string Name;
            public string Tag;
            public string MetricText;
            public int MemberCount;
            public string MemberCountText;
            public bool IsMine;
            public string MineLabelKey;
        }

        public sealed class VaultRowVM
        {
            public string LabelKey;
            public string ValueText;
        }

        /// <summary>
        /// One option in a TIER'S CATALOGUE — what a proposal would put on the ballot. Narrative
        /// only: there is no effect field here either, for the same ruling-3 reason.
        /// </summary>
        public sealed class BallotChoiceVM
        {
            public string OptionId;
            public string TitleText;
            public string DescriptionText;
        }

        public sealed class BallotTierVM
        {
            public int Tier;
            public string TierText;
            public double Threshold;
            public string ThresholdText;
            public bool Unlocked;
            public string LockedLabelKey;
            public bool CanPropose;
            public Action Propose;

            /// <summary>
            /// The slate this tier would open, published by the server on the tier row
            /// (api/_lib/clan-ballot.js:885-887). ⛔ NOT DECORATION: propose refuses an empty
            /// optionIds array with 400 CLAN_BALLOT_BAD_OPTIONS (:265), so this list is what
            /// makes the verb work at all.
            /// </summary>
            public IReadOnlyList<BallotChoiceVM> CatalogOptions;
        }

        public sealed class BallotOptionRowVM
        {
            public string OptionId;
            public string TitleText;
            public string DescriptionText;

            /// <summary>
            /// RULING 3. ALWAYS EMPTY. The server puts a stat magnitude on the wire
            /// (api/_lib/clan-ballot.js:192-241, "+5% build speed" and ten more); this VM
            /// never copies it, CircleWire does not even declare the field, and the View has
            /// no binding for it. Declared so the absence is DELIBERATE and pinned, not an
            /// oversight a later session "fixes".
            /// </summary>
            public string EffectText;

            public double Weight;
            public string WeightText;
            public int Voters;
            public string VotersText;
            public bool IsMyVote;
            public string MyVoteLabelKey;
            public bool CanVote;
            public Action Vote;
        }

        public sealed class PerkRowVM
        {
            public int Tier;
            public string TitleText;
            public string ActivatedAtIso;
            public string ExpiresAtIso;
        }

        // =====================================================================
        // Construction
        // =====================================================================
        private readonly ISource _source;
        private readonly Action _onClose;
        private readonly Action _changedHandler;
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<int, string[]> _tierOptionIds = new Dictionary<int, string[]>();
        private bool _disposed;
        private bool _selfNameAsked;

        /// <summary>The live screen. Named exactly as UiMvvmConformanceRegression.cs:74-78 needs it.</summary>
        public static CircleScreenVM CreateDefault(Action onClose)
            => new CircleScreenVM(new CircleSource(), onClose);

        public CircleScreenVM(ISource source, Action onClose)
        {
            _source = source;
            _onClose = onClose;
            State = CircleState.Loading;
            Members = new List<MemberRowVM>();
            Leaderboard = new List<LeaderRowVM>();
            VaultRows = new List<VaultRowVM>();
            VaultSignerShortTexts = new List<string>();
            BallotTiers = new List<BallotTierVM>();
            BallotOptions = new List<BallotOptionRowVM>();
            Perks = new List<PerkRowVM>();
            BuildTabs();

            NamePromptKey = "remnant.name.claim.title";
            NameHintKey = "remnant.name.hint";
            NameFieldKey = "remnant.name.field";
            NameSubmitKey = "remnant.name.submit";
            LeaveConfirmKey = "circle.leave.confirm";
            CreateHintKey = "circle.create.hint";
            JoinHintKey = "circle.join.hint";
            ReplayVigilKey = VigilCeremonyVM.KeyReplay;
            ReplayVigil = () => PanelRouter.Open(PanelId.CeremonyOfVigil, "replay");

            if (_source != null)
            {
                _changedHandler = OnSourceChanged;
                _source.Changed += _changedHandler;
            }

            RefreshAll();
        }

        // ── IPanelViewModel ───────────────────────────────────────────────────
        public string Title => new LocalizedText("circle.title").Resolve();
        public event Action Changed;
        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_source != null && _changedHandler != null) _source.Changed -= _changedHandler;
            Changed = null;
        }

        private void Raise()
        {
            if (_disposed) return;
            Changed?.Invoke();
        }

        private void OnSourceChanged()
        {
            FlowTrace.Step(Sys, "identity or room moved underneath the Circle screen — re-reading.");
            RefreshAll();
        }

        // =====================================================================
        // State
        // =====================================================================
        public CircleState State { get; private set; }
        public CircleTab ActiveTab { get; private set; }
        public IReadOnlyList<TabVM> Tabs { get; private set; }

        // ── the player's own name (ruling 2) ──────────────────────────────────
        /// <summary>The stored FIRST NAME — the raw token, which is what the rename field edits.</summary>
        public string MyDisplayName { get; private set; }

        /// <summary>
        /// ⛔ WHAT THE HEADER RENDERS: the composed name, "Sally of Emberwatch" (owner ruling
        /// 2026-09-18). Lane B binds THIS, never MyDisplayName — the raw token is the edit
        /// value, this is the shown value, and the two are deliberately different properties.
        /// </summary>
        public string MyDisplayNameText { get; private set; }

        public bool HasDisplayName { get; private set; }
        public string MyNameFallbackText { get; private set; }
        public string NameDraft { get; private set; }
        public bool CanSubmitName { get; private set; }
        public string NamePromptKey { get; private set; }
        public string NameHintKey { get; private set; }
        public string NameFieldKey { get; private set; }
        public string NameSubmitKey { get; private set; }

        // ── header ────────────────────────────────────────────────────────────
        public string CircleId { get; private set; }
        public string CircleName { get; private set; }
        public string CircleTag { get; private set; }
        public string CircleCode { get; private set; }
        public string JoinPolicy { get; private set; }
        public string JoinPolicyLabelKey { get; private set; }
        public int MemberCount { get; private set; }
        public string MemberCountText { get; private set; }
        public string MyRole { get; private set; }
        public string MyRoleLabelKey { get; private set; }
        public string CreatedAtIso { get; private set; }
        public string JoinedAtIso { get; private set; }
        public bool IsLeader { get; private set; }
        public bool IsOfficer { get; private set; }

        // ── not-in-Circle forms ───────────────────────────────────────────────
        public string CreateName { get; private set; }
        public string CreateTag { get; private set; }
        public bool CreateOpenJoin { get; private set; }
        public bool CanSubmitCreate { get; private set; }
        public string CreateHintKey { get; private set; }
        public string JoinCode { get; private set; }
        public bool CanSubmitJoin { get; private set; }
        public string JoinHintKey { get; private set; }

        // ── lists ─────────────────────────────────────────────────────────────
        public IReadOnlyList<MemberRowVM> Members { get; private set; }
        public string MembersEmptyKey { get; private set; }
        public IReadOnlyList<LeaderRowVM> Leaderboard { get; private set; }
        public string LeaderboardEmptyKey { get; private set; }

        public bool HasVault { get; private set; }
        public IReadOnlyList<VaultRowVM> VaultRows { get; private set; }
        public IReadOnlyList<string> VaultSignerShortTexts { get; private set; }
        public bool CanRegisterVault { get; private set; }
        public string VaultAddressInput { get; private set; }
        public int VaultIndexInput { get; private set; }
        public string VaultEmptyKey { get; private set; }

        public double VigilWeight { get; private set; }
        public bool VigilDegraded { get; private set; }
        public string VigilWeightText { get; private set; }
        public string VigilDegradedKey { get; private set; }
        /// <summary>Locale key for Ember..Dawn from vigil_weight. Empty when no tier is unlocked.</summary>
        public string VigilWordKey { get; private set; }
        public int EpochIndex { get; private set; }
        public string EpochEndsAtIso { get; private set; }

        public IReadOnlyList<BallotTierVM> BallotTiers { get; private set; }
        public bool HasBallot { get; private set; }
        public string BallotId { get; private set; }
        public int BallotTier { get; private set; }
        public bool BallotClosed { get; private set; }
        public string BallotOpenedAtIso { get; private set; }
        public string BallotClosesAtIso { get; private set; }
        public string MyVoteOptionId { get; private set; }
        public IReadOnlyList<BallotOptionRowVM> BallotOptions { get; private set; }
        public string TurnoutText { get; private set; }
        public bool TurnoutClears { get; private set; }
        public bool HasResult { get; private set; }
        public bool ResultPassed { get; private set; }
        public string ResultReasonKey { get; private set; }
        public string ResultWinningOptionTitleText { get; private set; }
        public IReadOnlyList<PerkRowVM> Perks { get; private set; }
        public string BallotsEmptyKey { get; private set; }

        /// <summary>WO-1874. True when a last vigil can be replayed, even if already seen.</summary>
        public bool CanReplayVigil { get; private set; }
        public string ReplayVigilKey { get; private set; }
        public Action ReplayVigil { get; private set; }

        public bool LeaveConfirmArmed { get; private set; }
        public string LeaveConfirmKey { get; private set; }
        public bool IsBusy { get; private set; }

        /// <summary>WO-1875. True only in NotSignedIn while no mint is in flight.</summary>
        public bool CanSignIn
        {
            get
            {
                return State == CircleState.NotSignedIn
                    && !IsBusy
                    && _source != null
                    && !string.IsNullOrWhiteSpace(_source.WalletAddress)
                    && !_source.IsGuest;
            }
        }

        /// <summary>The GOLD face on the NotSignedIn notice. Locale key, never a literal.</summary>
        public const string SignInFaceKey = "circle.signIn.face";

        public string ErrorKey { get; private set; }
        public string ToastKey { get; private set; }
        public void ClearToast() { ToastKey = null; Raise(); }

        // =====================================================================
        // Reads
        // =====================================================================

        /// <summary>
        /// The whole screen, from the top: the caller's own name first (ruling 2), then
        /// /api/clan/me for the header, then the active tab's read.
        /// </summary>
        public void RefreshAll()
        {
            if (_source == null)
            {
                State = CircleState.Unreachable;
                ErrorKey = "circle.error.unreachable";
                Raise();
                return;
            }

            string wallet = _source.WalletAddress;
            if (string.IsNullOrWhiteSpace(wallet) || _source.IsGuest)
            {
                FlowTrace.Step(Sys, "Circle screen has no wallet identity — NoWallet (fail-closed, no round trip).");
                State = CircleState.NoWallet;
                ErrorKey = "circle.error.noWallet";
                Raise();
                return;
            }

            MyNameFallbackText = Truncate(wallet);
            if (State != CircleState.InCircle && State != CircleState.NotInCircle
                && State != CircleState.NotSignedIn)
            {
                State = CircleState.Loading;
            }
            Raise();

            if (!_selfNameAsked)
            {
                _selfNameAsked = true;
                FlowTrace.Step(Sys, "reading the caller's own Remnant name before anything else (ruling 2).");
                _source.FetchDisplayNames(new[] { wallet }, OnSelfName);
                return;
            }

            FetchMe();
        }

        private void OnSelfName(string body, long status)
        {
            if (status != 200)
            {
                // ⛔ "WE COULD NOT ASK" IS NOT "YOU HAVE NO NAME". Falling through to the claim
                // step here would tell an offline player to claim a name they already hold —
                // the ClanMembershipClient.cs:27-31 hold-previous rule, in its nastiest form
                // because the next screen would then try to RENAME them. The flag is cleared
                // too, so the very next RefreshAll asks again instead of skipping the check
                // for the life of the VM.
                _selfNameAsked = false;
                if (EnterAskFailed(body, status, "name lookup could not be answered (http=" + status +
                                                 ") — NOT treating that as a nameless player."))
                    return;
                State = CircleState.Unreachable;
                ErrorKey = PlayerFacingKey(CircleWire.RefusalCode(body), status, AttachWhy());
                FlowTrace.Warn(Sys, "name lookup could not be answered (http=" + status +
                                    ") — NOT treating that as a nameless player.");
                Raise();
                return;
            }

            ApplyNames(body, status);

            if (!HasDisplayName)
            {
                FlowTrace.Step(Sys, "no Remnant name yet — the claim step is the first thing this player sees.");
                State = CircleState.NeedsName;
                Raise();
                return;
            }

            FetchMe();
        }

        private void FetchMe()
        {
            FlowTrace.Step(Sys, "GET /api/clan/me");
            _source.FetchMe(OnMe);
        }

        private void OnMe(string body, long status)
        {
            if (status == 0)
            {
                // ⛔ HOLD THE PREVIOUS STATE. A transport failure is "we could not ask",
                // never "you have no Circle" — the ClanMembershipClient.cs:27-31 rule.
                // WO-1875: missing/expired is NotSignedIn, never Unreachable.
                if (EnterAskFailed(body, status, "clan/me unreachable — holding the previous state."))
                    return;
                FlowTrace.Warn(Sys, "clan/me unreachable — holding the previous state.");
                State = CircleState.Unreachable;
                ErrorKey = "circle.error.unreachable";
                Raise();
                return;
            }

            var refusal = CircleWire.RefusalCode(body);
            if (status != 200 || refusal != null)
            {
                ErrorKey = PlayerFacingKey(refusal, status, AttachWhy());
                FlowTrace.Warn(Sys, "clan/me refused: http=" + status + " code=" + (refusal ?? "<none>"));
                Raise();
                return;
            }

            var me = CircleWire.Parse<CircleWire.MeResponse>(body);
            if (me == null || !me.ok)
            {
                State = CircleState.Unreachable;
                ErrorKey = "circle.error.unreachable";
                Raise();
                return;
            }

            ErrorKey = null;

            // ⛔ JsonUtility materialises a null object as a DEFAULT INSTANCE, so "no clan"
            // is an EMPTY clanId — never a null check (CircleWire.cs header).
            if (me.clan == null || string.IsNullOrEmpty(me.clan.clanId))
            {
                FlowTrace.Step(Sys, "clan/me: this player is in no Circle — a real success, not an error.");
                ClearCircle();
                State = CircleState.NotInCircle;
                Raise();
                return;
            }

            CircleId = me.clan.clanId;
            CircleName = me.clan.name;
            CircleTag = me.clan.tag;
            CircleCode = me.clan.code;
            JoinPolicy = me.clan.joinPolicy;
            JoinPolicyLabelKey = string.Equals(JoinPolicy, "open", StringComparison.Ordinal)
                ? "circle.policy.open" : "circle.policy.invite";
            MemberCount = me.clan.memberCount;
            MemberCountText = LocalText.Format("circle.header.members", MemberCount);
            CreatedAtIso = me.clan.createdAt;
            JoinedAtIso = me.joinedAt;
            MyRole = string.IsNullOrEmpty(me.role) ? "member" : me.role;
            MyRoleLabelKey = RoleKey(MyRole);
            IsLeader = string.Equals(MyRole, "leader", StringComparison.Ordinal);
            IsOfficer = string.Equals(MyRole, "officer", StringComparison.Ordinal);
            State = CircleState.InCircle;
            // The Circle is the second half of every shown name — joining one renames
            // everybody on screen, so nothing may be left composed against the old one.
            RecomposeNames();
            Raise();

            LoadActiveTab();
        }

        private void ClearCircle()
        {
            CircleId = null;
            CircleName = null;
            CircleTag = null;
            CircleCode = null;
            JoinPolicy = null;
            JoinPolicyLabelKey = null;
            MemberCount = 0;
            MemberCountText = null;
            MyRole = null;
            MyRoleLabelKey = null;
            IsLeader = false;
            IsOfficer = false;
            Members = new List<MemberRowVM>();
            // Leaving swaps the epithet back in: "Bob of RiverRun" becomes "Bob the Lonely".
            RecomposeNames();
            VaultRows = new List<VaultRowVM>();
            VaultSignerShortTexts = new List<string>();
            HasVault = false;
            CanRegisterVault = false;
            BallotTiers = new List<BallotTierVM>();
            BallotOptions = new List<BallotOptionRowVM>();
            Perks = new List<PerkRowVM>();
            HasBallot = false;
            CanReplayVigil = false;
            VigilWeight = 0;
            VigilDegraded = false;
            VigilWeightText = null;
            VigilDegradedKey = null;
            VigilWordKey = null;
        }

        public void SelectTab(CircleTab tab)
        {
            ActiveTab = tab;
            BuildTabs();
            Raise();
            LoadActiveTab();
        }

        private void BuildTabs()
        {
            var tabs = new List<TabVM>(4);
            Guard.Try(Sys, "CircleScreenVM.BuildTabs", () =>
            {
                AddTab(tabs, CircleTab.Members, "circle.tab.members");
                AddTab(tabs, CircleTab.Leaderboard, "circle.tab.leaderboard");
                AddTab(tabs, CircleTab.Vault, "circle.tab.vault");
                AddTab(tabs, CircleTab.Ballots, "circle.tab.ballots");
            });
            Tabs = tabs;
        }

        private void AddTab(List<TabVM> into, CircleTab id, string labelKey)
        {
            var captured = id;
            into.Add(new TabVM
            {
                Id = id,
                LabelKey = labelKey,
                IsActive = ActiveTab == id,
                Activate = () => SelectTab(captured),
            });
        }

        private void LoadActiveTab()
        {
            if (_source == null || State != CircleState.InCircle) return;
            switch (ActiveTab)
            {
                case CircleTab.Leaderboard:
                    FlowTrace.Step(Sys, "GET /api/clan/leaderboard");
                    _source.FetchLeaderboard(50, OnLeaderboard);
                    break;
                case CircleTab.Ballots:
                    FlowTrace.Step(Sys, "GET /api/clan/ballot/current");
                    _source.FetchBallot(OnBallot);
                    break;
                default:
                    // Members AND Vault both ride ONE read: /api/clan/vigil is the only
                    // roster the server renders, and the vault block rides the same body
                    // (api/_lib/clan-vigil.js:350, :376-393). /me supplies the header only.
                    FlowTrace.Step(Sys, "GET /api/clan/vigil");
                    _source.FetchVigil(OnVigil);
                    break;
            }
        }

        private void OnVigil(string body, long status)
        {
            if (status == 0)
            {
                if (EnterAskFailed(body, status, "clan/vigil unreachable — the tab says so rather than blanking."))
                    return;
                MembersEmptyKey = "circle.tab.unreachable";
                VaultEmptyKey = "circle.tab.unreachable";
                FlowTrace.Warn(Sys, "clan/vigil unreachable — the tab says so rather than blanking.");
                Raise();
                return;
            }

            var refusal = CircleWire.RefusalCode(body);
            if (refusal == "CLAN_NOT_IN_CLAN")
            {
                // 404 here, unlike the 200 from /me — the same fact in a different shape.
                ClearCircle();
                State = CircleState.NotInCircle;
                Raise();
                return;
            }
            if (status != 200 || refusal != null)
            {
                MembersEmptyKey = "circle.tab.unreachable";
                VaultEmptyKey = "circle.tab.unreachable";
                ErrorKey = PlayerFacingKey(refusal, status);
                Raise();
                return;
            }

            var v = CircleWire.Parse<CircleWire.VigilResponse>(body);
            if (v == null || !v.ok)
            {
                MembersEmptyKey = "circle.tab.unreachable";
                VaultEmptyKey = "circle.tab.unreachable";
                Raise();
                return;
            }

            ApplyVigilWeight(v.vigil_weight, v.degraded);

            var rows = new List<MemberRowVM>();
            var ids = new List<string>();
            Guard.Try(Sys, "CircleScreenVM.BuildMembers", () =>
            {
                var src = v.members ?? new CircleWire.VigilMemberDto[0];
                foreach (var m in src)
                {
                    if (m == null || string.IsNullOrEmpty(m.wallet)) continue;
                    rows.Add(BuildMemberRow(m));
                    ids.Add(m.wallet);
                }
            });
            Members = rows;
            MembersEmptyKey = rows.Count == 0 ? "circle.members.empty" : string.Empty;

            BuildVault(v.vault);
            Raise();

            if (ids.Count > 0 && _source != null)
            {
                _source.FetchDisplayNames(ids.ToArray(), OnMemberNames);
            }
        }

        private MemberRowVM BuildMemberRow(CircleWire.VigilMemberDto m)
        {
            string role = string.IsNullOrEmpty(m.role) ? "member" : m.role;
            bool isMe = _source != null &&
                        string.Equals(m.wallet, _source.WalletAddress, StringComparison.Ordinal);
            string target = m.wallet;

            var row = new MemberRowVM
            {
                PlayerId = m.wallet,
                // Composed: every row on this roster belongs to the SAME Circle, so the
                // second half is CircleName and is applied once, here.
                DisplayNameText = DisplayNameFor(m.wallet),
                HasDisplayName = _names.ContainsKey(m.wallet),
                Role = role,
                RoleLabelKey = RoleKey(role),
                TenureText = LocalText.Format("circle.tenure.days", m.tenure_seconds / 86400L),
                Degraded = m.degraded,
                // A WORD, never a colour — the owner is red/green colourblind.
                DegradedLabelKey = m.degraded ? "circle.member.degraded" : string.Empty,
                IsMe = isMe,
            };

            row.CanPromote = IsLeader && string.Equals(role, "member", StringComparison.Ordinal) && !isMe;
            row.CanDemote = IsLeader && string.Equals(role, "officer", StringComparison.Ordinal) && !isMe;
            row.CanKick = (IsLeader && !isMe) ||
                          (IsOfficer && string.Equals(role, "member", StringComparison.Ordinal) && !isMe);
            row.Promote = () => Promote(target);
            row.Demote = () => Demote(target);
            row.Kick = () => Kick(target);
            return row;
        }

        private void BuildVault(CircleWire.VaultDto vault)
        {
            var rows = new List<VaultRowVM>();
            var signers = new List<string>();
            bool has = vault != null && !string.IsNullOrEmpty(vault.vault_address);

            Guard.Try(Sys, "CircleScreenVM.BuildVault", () =>
            {
                if (!has) return;
                rows.Add(new VaultRowVM { LabelKey = "circle.vault.address", ValueText = Truncate(vault.vault_address) });
                rows.Add(new VaultRowVM { LabelKey = "circle.vault.multisig", ValueText = Truncate(vault.multisig_address) });
                rows.Add(new VaultRowVM
                {
                    LabelKey = "circle.vault.threshold",
                    ValueText = vault.threshold + " / " + vault.signer_count,
                });
                rows.Add(new VaultRowVM
                {
                    LabelKey = "circle.vault.verifiedSigners",
                    ValueText = vault.verified_signer_count + " / " + vault.signer_count,
                });
                rows.Add(new VaultRowVM
                {
                    LabelKey = "circle.vault.hardware",
                    // A WORD on both branches, never a colour.
                    ValueText = new LocalizedText(vault.hardware_backed
                        ? "circle.vault.hardware.yes" : "circle.vault.hardware.no").Resolve(),
                });
                rows.Add(new VaultRowVM { LabelKey = "circle.vault.verifiedAt", ValueText = vault.verified_at });

                var wallets = vault.signer_wallets ?? new string[0];
                foreach (var w in wallets)
                {
                    if (!string.IsNullOrEmpty(w)) signers.Add(Truncate(w));
                }
            });

            HasVault = has;
            VaultRows = rows;
            VaultSignerShortTexts = signers;
            VaultEmptyKey = has ? string.Empty : "circle.vault.none";
            // 403 CLAN_VAULT_NOT_LEADER otherwise — so the form is not offered at all.
            CanRegisterVault = IsLeader && !has;
        }

        private void OnMemberNames(string body, long status)
        {
            ApplyNames(body, status);

            RecomposeNames();
            Raise();
        }

        private void ApplyNames(string body, long status)
        {
            if (status != 200)
            {
                FlowTrace.Warn(Sys, "name lookup answered http=" + status + " — falling back to short ids.");
                return;
            }

            var parsed = CircleWire.Parse<CircleWire.UsernamesResponse>(body);
            if (parsed == null || !parsed.ok)
            {
                FlowTrace.Warn(Sys, "name lookup body was not ok — falling back to short ids.");
                return;
            }

            Guard.Try(Sys, "CircleScreenVM.ApplyNames", () =>
            {
                var rows = parsed.names ?? new CircleWire.UsernameRowDto[0];
                foreach (var r in rows)
                {
                    if (r == null || string.IsNullOrEmpty(r.playerId) || string.IsNullOrEmpty(r.username)) continue;
                    _names[r.playerId] = r.username;
                }
            });

            string me = _source != null ? _source.WalletAddress : null;
            if (!string.IsNullOrEmpty(me) && _names.TryGetValue(me, out var mine))
            {
                MyDisplayName = mine;
                HasDisplayName = true;
            }
            RecomposeNames();
        }

        private void OnLeaderboard(string body, long status)
        {
            if (status == 0)
            {
                if (EnterAskFailed(body, status, "clan/leaderboard unreachable — the tab says so rather than blanking."))
                    return;
                LeaderboardEmptyKey = "circle.tab.unreachable";
                Raise();
                return;
            }

            var refusal = CircleWire.RefusalCode(body);
            if (status != 200 || refusal != null)
            {
                LeaderboardEmptyKey = "circle.tab.unreachable";
                ErrorKey = PlayerFacingKey(refusal, status);
                Raise();
                return;
            }

            var parsed = CircleWire.Parse<CircleWire.LeaderboardResponse>(body);
            if (parsed == null || !parsed.ok)
            {
                LeaderboardEmptyKey = "circle.tab.unreachable";
                Raise();
                return;
            }

            var rows = new List<LeaderRowVM>();
            Guard.Try(Sys, "CircleScreenVM.BuildLeaderboard", () =>
            {
                var src = parsed.clans ?? new CircleWire.LeaderboardRowDto[0];
                foreach (var c in src)
                {
                    if (c == null) continue;
                    bool mine = !string.IsNullOrEmpty(CircleId) &&
                                string.Equals(c.clanId, CircleId, StringComparison.Ordinal);
                    rows.Add(new LeaderRowVM
                    {
                        Rank = c.rank,
                        RankText = "#" + c.rank,
                        CircleId = c.clanId,
                        Name = c.name,
                        Tag = c.tag,
                        // The server's own header calls this metric a NAMED PLACEHOLDER
                        // (member_count x days_since_created) — so it is never called Vigil.
                        MetricText = FormatNumber(c.metric),
                        MemberCount = c.memberCount,
                        MemberCountText = LocalText.Format("circle.leaderboard.members", c.memberCount),
                        IsMine = mine,
                        MineLabelKey = mine ? "circle.leaderboard.you" : string.Empty,
                    });
                }
            });

            Leaderboard = rows;
            LeaderboardEmptyKey = rows.Count == 0 ? "circle.leaderboard.empty" : string.Empty;
            Raise();
        }

        private void OnBallot(string body, long status)
        {
            if (status == 0)
            {
                if (EnterAskFailed(body, status, "clan/ballot unreachable — the tab says so rather than blanking."))
                    return;
                BallotsEmptyKey = "circle.tab.unreachable";
                Raise();
                return;
            }

            var refusal = CircleWire.RefusalCode(body);
            if (refusal == "CLAN_NOT_IN_CLAN")
            {
                ClearCircle();
                State = CircleState.NotInCircle;
                Raise();
                return;
            }
            if (status != 200 || refusal != null)
            {
                BallotsEmptyKey = "circle.tab.unreachable";
                ErrorKey = PlayerFacingKey(refusal, status);
                Raise();
                return;
            }

            var b = CircleWire.Parse<CircleWire.BallotResponse>(body);
            if (b == null || !b.ok)
            {
                BallotsEmptyKey = "circle.tab.unreachable";
                Raise();
                return;
            }

            ApplyVigilWeight(b.vigil_weight, b.vigil_degraded);
            EpochIndex = b.epoch != null ? b.epoch.index : 0;
            EpochEndsAtIso = b.epoch != null ? b.epoch.ends_at : null;

            HasBallot = b.ballot != null && !string.IsNullOrEmpty(b.ballot.ballot_id);
            BallotId = HasBallot ? b.ballot.ballot_id : null;
            BallotTier = HasBallot ? b.ballot.tier : 0;
            BallotClosed = HasBallot && b.ballot.closed;
            BallotOpenedAtIso = HasBallot ? b.ballot.opened_at : null;
            BallotClosesAtIso = HasBallot ? b.ballot.closes_at : null;
            MyVoteOptionId = HasBallot ? b.ballot.my_vote : null;

            BuildBallotTiers(b);
            BuildBallotOptions(b);
            BuildTurnoutAndResult(b);
            BuildPerks(b);

            BallotsEmptyKey = HasBallot ? string.Empty : "circle.ballot.none";

            var payload = VigilCeremonyVM.BuildPayload(b, VigilCeremonyLedger.Shared.LastPayload);
            if (payload != null && payload.Passed)
                VigilCeremonyLedger.Shared.RememberPayload(payload);
            CanReplayVigil = (payload != null && payload.HasContent && payload.Passed)
                             || ResultPassed
                             || (Perks != null && Perks.Count > 0);
            Raise();
        }

        private void BuildBallotTiers(CircleWire.BallotResponse b)
        {
            var tiers = new List<BallotTierVM>();
            _tierOptionIds.Clear();
            Guard.Try(Sys, "CircleScreenVM.BuildBallotTiers", () =>
            {
                var src = b.tiers ?? new CircleWire.BallotTierDto[0];
                foreach (var t in src)
                {
                    if (t == null) continue;
                    int tier = t.tier;

                    var choices = new List<BallotChoiceVM>();
                    var ids = new List<string>();
                    var catalogue = t.options ?? new CircleWire.BallotOptionDto[0];
                    foreach (var o in catalogue)
                    {
                        if (o == null || string.IsNullOrEmpty(o.option_id)) continue;
                        ids.Add(o.option_id);
                        choices.Add(new BallotChoiceVM
                        {
                            OptionId = o.option_id,
                            TitleText = string.IsNullOrEmpty(o.title) ? o.option_id : o.title,
                            DescriptionText = o.description,
                        });
                    }
                    _tierOptionIds[tier] = ids.ToArray();

                    var row = new BallotTierVM
                    {
                        CatalogOptions = choices,
                        Tier = tier,
                        TierText = LocalText.Format("circle.ballot.tier", tier),
                        Threshold = t.threshold,
                        ThresholdText = FormatNumber(t.threshold),
                        Unlocked = t.unlocked,
                        LockedLabelKey = t.unlocked ? string.Empty : "circle.ballot.locked",
                        // A tier whose catalogue did not arrive cannot be proposed: the route
                        // would answer 400 CLAN_BALLOT_BAD_OPTIONS, which is a button that
                        // always fails. Better to not offer it than to offer a lie.
                        CanPropose = t.unlocked && !HasBallot && ids.Count > 0,
                    };
                    row.Propose = () => ProposeTier(tier);
                    tiers.Add(row);
                }
            });
            BallotTiers = tiers;
        }

        private void BuildBallotOptions(CircleWire.BallotResponse b)
        {
            var options = new List<BallotOptionRowVM>();
            Guard.Try(Sys, "CircleScreenVM.BuildBallotOptions", () =>
            {
                if (!HasBallot) return;
                var src = b.ballot.options ?? new CircleWire.BallotOptionDto[0];
                foreach (var o in src)
                {
                    if (o == null || string.IsNullOrEmpty(o.option_id)) continue;
                    string optionId = o.option_id;
                    bool mine = string.Equals(optionId, MyVoteOptionId, StringComparison.Ordinal);
                    var row = new BallotOptionRowVM
                    {
                        OptionId = optionId,
                        TitleText = string.IsNullOrEmpty(o.title) ? optionId : o.title,
                        DescriptionText = o.description,
                        // RULING 3 — the one assignment this field ever gets.
                        EffectText = string.Empty,
                        Weight = o.weight,
                        WeightText = FormatNumber(o.weight),
                        Voters = o.voters,
                        VotersText = LocalText.Format("circle.ballot.voters", o.voters),
                        IsMyVote = mine,
                        MyVoteLabelKey = mine ? "circle.ballot.yourVote" : string.Empty,
                        CanVote = !BallotClosed,
                    };
                    row.Vote = () => Vote(optionId);
                    options.Add(row);
                }
            });
            BallotOptions = options;
        }

        private void BuildTurnoutAndResult(CircleWire.BallotResponse b)
        {
            TurnoutText = null;
            TurnoutClears = false;
            if (HasBallot && b.ballot.turnout != null)
            {
                TurnoutText = LocalText.Format("circle.ballot.turnout",
                    b.ballot.turnout.voters, b.ballot.turnout.member_count);
                TurnoutClears = b.ballot.turnout.clears;
            }

            HasResult = b.result != null && b.result.closed;
            ResultPassed = HasResult && b.result.passed;
            ResultReasonKey = null;
            ResultWinningOptionTitleText = null;
            if (!HasResult) return;

            ResultReasonKey = ResultKeyFor(b.result.reason);

            string winner = b.result.winning_option;
            if (string.IsNullOrEmpty(winner)) return;
            foreach (var o in BallotOptions)
            {
                if (string.Equals(o.OptionId, winner, StringComparison.Ordinal))
                {
                    // The TITLE, never the magnitude (ruling 3).
                    ResultWinningOptionTitleText = o.TitleText;
                    return;
                }
            }
            ResultWinningOptionTitleText = winner;
        }

        private void BuildPerks(CircleWire.BallotResponse b)
        {
            var perks = new List<PerkRowVM>();
            Guard.Try(Sys, "CircleScreenVM.BuildPerks", () =>
            {
                var src = b.perks ?? new CircleWire.PerkDto[0];
                foreach (var p in src)
                {
                    if (p == null) continue;
                    perks.Add(new PerkRowVM
                    {
                        Tier = p.tier,
                        // Narrative only: title + description when the wire carries them,
                        // else the tier line. Never a stat identifier, never effect.
                        TitleText = string.IsNullOrEmpty(p.title)
                            ? LocalText.Format("circle.ballot.tier", p.tier)
                            : p.title,
                        ActivatedAtIso = p.activated_at,
                        ExpiresAtIso = p.expires_at,
                    });
                }
            });
            Perks = perks;
        }

        // =====================================================================
        // Form state
        // =====================================================================
        public void SetNameDraft(string s)
        {
            NameDraft = s;
            CanSubmitName = IsValidName(s);
            Raise();
        }

        /// <summary>
        /// The SERVER's rule, mirrored so the button never promises what the route refuses:
        /// 3..16 characters of [A-Za-z0-9_] (api/_lib/username-policy.js:16-21). The profanity
        /// gate stays server-side — a client copy of that list is the duplicated state
        /// username-policy.js's own footer warns about.
        /// </summary>
        public static bool IsValidName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            string t = s.Trim();
            if (t.Length < NameMinLength || t.Length > NameMaxLength) return false;
            foreach (char c in t)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return true;
        }

        public void SetCreateName(string s)
        {
            CreateName = s;
            RecomputeCreate();
        }

        public void SetCreateTag(string s)
        {
            CreateTag = s == null ? null : s.ToUpperInvariant();
            RecomputeCreate();
        }

        public void ToggleCreateOpenJoin()
        {
            CreateOpenJoin = !CreateOpenJoin;
            Raise();
        }

        private void RecomputeCreate()
        {
            string n = CreateName == null ? string.Empty : CreateName.Trim();
            string t = CreateTag == null ? string.Empty : CreateTag.Trim();
            CanSubmitCreate = n.Length >= 1 && n.Length <= CircleNameMaxLength &&
                              t.Length >= 1 && t.Length <= CircleTagMaxLength;
            Raise();
        }

        public void SetJoinCode(string s)
        {
            JoinCode = s == null ? null : s.Trim().ToUpperInvariant();
            // ⛔ RULING 4: the CODE SHAPE ALONE decides this. There is no stake term here,
            // and none may be added — joining a Circle is never gated on holding anything.
            CanSubmitJoin = IsValidCode(JoinCode);
            Raise();
        }

        /// <summary>clans.code is ^[A-HJ-NP-Z2-9]{6}$ — I, L, O, 0 and 1 are excluded.</summary>
        public static bool IsValidCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length != CircleCodeLength) return false;
            foreach (char c in code)
            {
                bool letter = (c >= 'A' && c <= 'H') || (c >= 'J' && c <= 'N') || (c >= 'P' && c <= 'Z');
                bool digit = c >= '2' && c <= '9';
                if (!letter && !digit) return false;
            }
            return true;
        }

        public void SetVaultAddress(string s)
        {
            VaultAddressInput = s == null ? null : s.Trim();
            Raise();
        }

        public void SetVaultIndex(int i)
        {
            if (i < 0) i = 0;
            if (i > 255) i = 255;
            VaultIndexInput = i;
            Raise();
        }

        // =====================================================================
        // Writes — one in flight at a time, every one an explicit press
        // =====================================================================
        private bool BeginWrite(string what)
        {
            if (_source == null) return false;
            if (IsBusy)
            {
                FlowTrace.Warn(Sys, what + " ignored — a write is already in flight.");
                return false;
            }
            IsBusy = true;
            ErrorKey = null;
            FlowTrace.Step(Sys, "verb: " + what);
            Raise();
            return true;
        }

        private void EndWrite(string what, string body, long status, string successToastKey, bool refresh)
        {
            IsBusy = false;
            var refusal = CircleWire.RefusalCode(body);
            bool ok = status == 200 && refusal == null && CircleWire.IsOk(body);
            if (!ok)
            {
                ErrorKey = PlayerFacingKey(refusal, status);
                FlowTrace.Warn(Sys, what + " refused: http=" + status + " code=" + (refusal ?? "<none>"));
                Raise();
                return;
            }

            ToastKey = successToastKey;
            FlowTrace.Step(Sys, what + " accepted by the server.");
            Raise();
            if (refresh) RefreshAll();
        }

        public void SubmitDisplayName()
        {
            if (!CanSubmitName) return;
            if (!BeginWrite("set name")) return;
            string wanted = NameDraft.Trim();
            _source.SetDisplayName(wanted, (body, status) =>
            {
                IsBusy = false;
                var refusal = CircleWire.RefusalCode(body);
                if (status != 200 || refusal != null)
                {
                    ErrorKey = PlayerFacingKey(refusal, status);
                    FlowTrace.Warn(Sys, "name refused: http=" + status + " code=" + (refusal ?? "<none>"));
                    Raise();
                    return;
                }

                var parsed = CircleWire.Parse<CircleWire.UsernameSetResponse>(body);
                string got = parsed != null && !string.IsNullOrEmpty(parsed.username) ? parsed.username : wanted;
                MyDisplayName = got;
                HasDisplayName = true;
                string me = _source.WalletAddress;
                if (!string.IsNullOrEmpty(me)) _names[me] = got;
                RecomposeNames();
                ToastKey = "remnant.name.saved";
                FlowTrace.Step(Sys, "Remnant name claimed — moving on to the Circle itself.");
                Raise();
                FetchMe();
            });
        }

        /// <summary>The name is changeable — this re-enters the claim step on purpose.</summary>
        public void EditDisplayName()
        {
            NameDraft = MyDisplayName;
            CanSubmitName = IsValidName(NameDraft);
            State = CircleState.NeedsName;
            Raise();
        }

        /// <summary>
        /// Back out of a rename the player thought better of. ⚠ WITHOUT THIS THERE IS NO WAY
        /// OUT: a named player who taps "change" would be held in the claim step until they
        /// submitted a valid name or closed the whole screen. A player who has NO name yet
        /// stays in the claim step, because for them it is not a detour — it is the door.
        /// </summary>
        public void CancelEditName()
        {
            if (!HasDisplayName) return;
            NameDraft = null;
            CanSubmitName = false;
            State = string.IsNullOrEmpty(CircleId) ? CircleState.NotInCircle : CircleState.InCircle;
            Raise();
        }

        public void SubmitCreate()
        {
            if (!CanSubmitCreate) return;
            if (!BeginWrite("create")) return;
            string policy = CreateOpenJoin ? "open" : "invite";
            _source.Create(CreateName.Trim(), CreateTag.Trim(), policy,
                (body, status) => EndWrite("create", body, status, "circle.toast.created", true));
        }

        public void SubmitJoin()
        {
            if (!CanSubmitJoin) return;
            if (!BeginWrite("join")) return;
            _source.Join(JoinCode,
                (body, status) => EndWrite("join", body, status, "circle.toast.joined", true));
        }

        public void CopyCode()
        {
            if (_source == null || string.IsNullOrEmpty(CircleCode)) return;
            _source.CopyToClipboard(CircleCode);
            ToastKey = "circle.toast.codeCopied";
            Raise();
        }

        /// <summary>Arms the confirm and calls NOTHING — leaving is never one tap.</summary>
        public void RequestLeave()
        {
            LeaveConfirmArmed = true;
            Raise();
        }

        public void CancelLeave()
        {
            LeaveConfirmArmed = false;
            Raise();
        }

        public void ConfirmLeave()
        {
            if (!LeaveConfirmArmed) return;
            if (!BeginWrite("leave")) return;
            LeaveConfirmArmed = false;
            _source.Leave((body, status) => EndWrite("leave", body, status, "circle.toast.left", true));
        }

        public void Promote(string targetPlayerId)
        {
            if (string.IsNullOrEmpty(targetPlayerId)) return;
            if (!BeginWrite("promote")) return;
            _source.Promote(targetPlayerId,
                (body, status) => EndWrite("promote", body, status, "circle.toast.promoted", true));
        }

        public void Demote(string targetPlayerId)
        {
            if (string.IsNullOrEmpty(targetPlayerId)) return;
            if (!BeginWrite("demote")) return;
            _source.Demote(targetPlayerId,
                (body, status) => EndWrite("demote", body, status, "circle.toast.demoted", true));
        }

        public void Kick(string targetPlayerId)
        {
            if (string.IsNullOrEmpty(targetPlayerId)) return;
            if (!BeginWrite("kick")) return;
            _source.Kick(targetPlayerId,
                (body, status) => EndWrite("kick", body, status, "circle.toast.kicked", true));
        }

        public void ProposeTier(int tier)
        {
            if (tier < 1 || tier > 5) return;

            // ⛔ THE SLATE IS REQUIRED AND IT COMES FROM THE SERVER'S OWN TIER ROW.
            // api/_lib/clan-ballot.js:265 refuses an empty or non-array optionIds with 400
            // CLAN_BALLOT_BAD_OPTIONS, and :266-271 requires every id to be DISTINCT and to
            // belong to THIS tier's catalogue. So the client proposes exactly what
            // /ballot/current published for the tier — never an invented list, never an empty
            // one (which is what an earlier draft of this method would have sent every time).
            string[] optionIds;
            if (!_tierOptionIds.TryGetValue(tier, out optionIds) || optionIds == null || optionIds.Length == 0)
            {
                ErrorKey = "circle.error.ballot.badOptions";
                FlowTrace.Warn(Sys, "propose refused locally — tier " + tier +
                                    " published no option catalogue, so the route would 400.");
                Raise();
                return;
            }

            if (!BeginWrite("propose")) return;
            _source.ProposeBallot(tier, optionIds, (body, status) =>
            {
                IsBusy = false;
                var refusal = CircleWire.RefusalCode(body);
                if (status != 200 || refusal != null)
                {
                    ErrorKey = PlayerFacingKey(refusal, status);
                    Raise();
                    return;
                }
                ToastKey = "circle.toast.proposed";
                OnBallot(body, status);
            });
        }

        public void Vote(string optionId)
        {
            if (string.IsNullOrEmpty(BallotId) || string.IsNullOrEmpty(optionId)) return;
            if (BallotClosed) return;
            if (!BeginWrite("vote")) return;
            _source.Vote(BallotId, optionId, (body, status) =>
            {
                IsBusy = false;
                var refusal = CircleWire.RefusalCode(body);
                if (status != 200 || refusal != null)
                {
                    ErrorKey = PlayerFacingKey(refusal, status);
                    Raise();
                    return;
                }
                ToastKey = "circle.toast.voted";
                // The vote route answers the SAME body as /ballot/current, my_vote set.
                OnBallot(body, status);
            });
        }

        public void SubmitRegisterVault()
        {
            if (!CanRegisterVault || string.IsNullOrEmpty(VaultAddressInput)) return;
            if (!BeginWrite("register vault")) return;
            _source.RegisterVault(VaultAddressInput, VaultIndexInput,
                (body, status) => EndWrite("register vault", body, status, "circle.toast.vaultRegistered", true));
        }

        public void OpenChat()
        {
            if (_source == null) return;
            FlowTrace.Step(Sys, "opening Circle chat from the Circle screen.");
            _source.OpenChat();
        }

        // =====================================================================
        // Refusal code -> the ONE player-facing key
        // =====================================================================

        /// <summary>
        /// Every refusal this screen's fifteen routes can answer with, mapped to exactly one
        /// locale key. Public so Assets/Tests/EditMode/CircleErrorMapTests.cs drives the whole
        /// table directly — the ClanMembershipClient.ApplyResponseJson precedent.
        ///
        /// ⚠ A code with no case here is NOT a crash: the default branch names the http
        /// status instead, so an unmapped server code still produces a sentence a player can
        /// act on. The regression suite is what keeps the table total.
        /// </summary>
        public static string PlayerFacingKey(string serverCode, long httpStatus)
        {
            return PlayerFacingKey(serverCode, httpStatus, null);
        }

        /// <summary>
        /// Same table as the two-arg form, plus WO-1875: http=0 with attach why
        /// <c>missing</c> or <c>expired</c> is <c>circle.error.notSignedIn</c>, never
        /// unreachable. A transport fail with no session gap stays unreachable.
        /// </summary>
        public static string PlayerFacingKey(string serverCode, long httpStatus, string attachWhy)
        {
            if (httpStatus == 0 && IsSessionGap(attachWhy))
                return "circle.error.notSignedIn";

            if (!string.IsNullOrEmpty(serverCode))
            {
                switch (serverCode)
                {
                    // ── identity / auth: every rail's refusal reads the same to a player ──
                    case "AUTH_HEADERS_MISSING":
                    case "AUTH_WALLET_MALFORMED":
                    case "AUTH_WALLET_MISMATCH":
                    case "AUTH_BAD_SIGNATURE":
                    case "AUTH_CRYPTO_UNAVAILABLE":
                    case "AUTH_NONCE_UNKNOWN":
                    case "AUTH_NONCE_WRONG_WALLET":
                    case "AUTH_NONCE_REPLAYED":
                    case "AUTH_NONCE_EXPIRED":
                    case "AUTH_SESSION_UNKNOWN":
                    case "AUTH_SESSION_EXPIRED":
                    case "AUTH_SESSION_WRONG_WALLET":
                    case "AUTH_SESSION_MALFORMED":
                    case "AUTH_WALLET_REQUIRED":
                    case "GUEST_HEADER_MISSING":
                    case "GUEST_MISMATCH":
                    case "GUEST_DISABLED":
                    case "GOOGLE_IDENTITY_DISABLED":
                        return "circle.error.notSignedIn";

                    // ── malformed request ──
                    case "PLAYER_ID_MISSING":
                    case "PLAYER_ID_BAD_SHAPE":
                    case "BAD_PAYLOAD":
                    case "PAYLOAD_TOO_LARGE":
                    case "METHOD_NOT_ALLOWED":
                    case "CLAN_BAD_CLAN_ID":
                    case "CLAN_BAD_MESSAGE_ID":
                        return "circle.error.badRequest";

                    // ── budgets ──
                    case "CLAN_RATE_LIMITED":
                    case "GUEST_RATE_LIMITED":
                        return "circle.error.rateLimited";

                    // ── create ──
                    case "CLAN_BAD_NAME": return "circle.error.badName";
                    case "CLAN_BAD_TAG": return "circle.error.badTag";
                    case "CLAN_BAD_JOIN_POLICY": return "circle.error.badPolicy";
                    case "CLAN_CODE_UNAVAILABLE": return "circle.error.codeUnavailable";
                    case "CLAN_IDENTITY_MISSING": return "circle.error.identityMissing";

                    // ── join: two different sentences, on purpose ──
                    case "CLAN_BAD_CODE": return "circle.error.badCode";
                    case "CLAN_NOT_FOUND": return "circle.error.noSuchCode";
                    case "CLAN_ALREADY_IN_CLAN": return "circle.error.alreadyInCircle";

                    // ── membership / roles ──
                    case "CLAN_NOT_IN_CLAN": return "circle.error.notInCircle";
                    case "leader_must_transfer": return "circle.error.leaveRaced";
                    case "CLAN_BAD_TARGET":
                    case "CLAN_SELF_TARGET":
                        return "circle.error.badTarget";
                    case "CLAN_FORBIDDEN":
                    case "CLAN_REPORT_NOT_MEMBER":
                        return "circle.error.forbidden";
                    case "CLAN_TARGET_NOT_IN_CLAN": return "circle.error.targetGone";
                    case "CLAN_TARGET_ROLE": return "circle.error.targetRole";
                    case "CLAN_RACED": return "circle.error.raced";

                    // ── ballots ──
                    case "CLAN_BALLOT_BAD_TIER": return "circle.error.ballot.badTier";
                    case "CLAN_BALLOT_BAD_OPTIONS": return "circle.error.ballot.badOptions";
                    case "CLAN_BALLOT_BAD_BALLOT_ID": return "circle.error.ballot.badOptions";
                    case "CLAN_BALLOT_TIER_LOCKED": return "circle.error.ballot.tierLocked";
                    case "CLAN_BALLOT_ALREADY_OPEN": return "circle.error.ballot.alreadyOpen";
                    case "CLAN_BALLOT_NOT_FOUND": return "circle.error.ballot.notFound";
                    case "CLAN_BALLOT_CLOSED": return "circle.error.ballot.closed";
                    case "CLAN_BALLOT_WEIGHT_UNAVAILABLE": return "circle.error.ballot.weightUnavailable";

                    // ── vault ──
                    case "CLAN_VAULT_BAD_ADDRESS": return "circle.error.vault.badAddress";
                    case "CLAN_VAULT_NOT_A_MULTISIG": return "circle.error.vault.notMultisig";
                    case "CLAN_VAULT_BAD_INDEX": return "circle.error.vault.badIndex";
                    case "CLAN_VAULT_TOO_MANY_SIGNERS": return "circle.error.vault.tooManySigners";
                    case "CLAN_VAULT_NOT_LEADER": return "circle.error.vault.notLeader";
                    case "CLAN_VAULT_SIGNER_NO_SGT":
                    case "CLAN_VAULT_SIGNER_SGT_REUSED":
                        return "circle.error.vault.signer";
                    case "CLAN_VAULT_ALREADY_REGISTERED": return "circle.error.vault.already";
                    case "CLAN_VAULT_UNREADABLE":
                    case "CLAN_VAULT_SIGNER_UNVERIFIABLE":
                        return "circle.error.vault.unreadable";

                    // ── the player's name: the EXISTING username rail's own codes ──
                    case "USERNAME_TOO_SHORT":
                    case "USERNAME_TOO_LONG":
                        return "remnant.name.error.length";
                    case "USERNAME_INVALID_CHARS": return "remnant.name.error.chars";
                    case "USERNAME_TAKEN": return "remnant.name.error.taken";
                    case "USERNAME_REJECTED": return "remnant.name.error.rejected";

                    case "SERVER_ERROR": return "circle.error.server";
                }
            }

            switch (httpStatus)
            {
                case 0: return "circle.error.unreachable";
                case 400: return "circle.error.badRequest";
                case 401:
                case 403:
                    return "circle.error.notSignedIn";
                case 429: return "circle.error.rateLimited";
                default: return "circle.error.server";
            }
        }

        /// <summary>WO-1875. <c>missing</c> and <c>expired</c> are a session gap, nothing else is.</summary>
        public static bool IsSessionGap(string attachWhy)
        {
            return string.Equals(attachWhy, "missing", StringComparison.Ordinal)
                || string.Equals(attachWhy, "expired", StringComparison.Ordinal);
        }

        private string AttachWhy()
        {
            return _source != null ? _source.LastAttachWhy : null;
        }

        /// <summary>
        /// http=0 + missing/expired → NotSignedIn. Returns true when that state was entered
        /// so the caller does not also map the same answer to Unreachable.
        /// </summary>
        private bool EnterAskFailed(string body, long status, string trace)
        {
            if (status != 0 || !IsSessionGap(AttachWhy())) return false;
            State = CircleState.NotSignedIn;
            ErrorKey = PlayerFacingKey(CircleWire.RefusalCode(body), status, AttachWhy());
            FlowTrace.Warn(Sys, trace + " — NotSignedIn (why=" + AttachWhy() + "), never unreachable.");
            Raise();
            return true;
        }

        /// <summary>
        /// WO-1875. The ONE minting Circle read. Opening reads stay false; this press
        /// is the wallet sheet, then RefreshAll re-runs the non-minting reads.
        /// </summary>
        public void SignIn()
        {
            if (!CanSignIn) return;
            if (IsBusy)
            {
                FlowTrace.Warn(Sys, "SignIn ignored — a write is already in flight.");
                return;
            }
            IsBusy = true;
            ErrorKey = null;
            FlowTrace.Step(Sys, "NotSignedIn -> minting");
            Raise();

            bool started = false;
            Guard.Try(Sys, "CircleScreenVM.SignIn", () =>
            {
                _source.SignIn(OnSignInMinted);
                started = true;
            });
            if (!started)
            {
                IsBusy = false;
                State = CircleState.NotSignedIn;
                ErrorKey = "circle.error.notSignedIn";
                FlowTrace.Warn(Sys, "NotSignedIn -> minting -> fail (SignIn did not start)");
                Raise();
            }
        }

        private void OnSignInMinted(string body, long status)
        {
            IsBusy = false;
            if (status == 200)
            {
                FlowTrace.Step(Sys, "NotSignedIn -> minting -> ok");
                _selfNameAsked = false;
                RefreshAll();
                return;
            }
            State = CircleState.NotSignedIn;
            ErrorKey = "circle.error.notSignedIn";
            FlowTrace.Warn(Sys, "NotSignedIn -> minting -> fail http=" + status + " why=" + AttachWhy());
            Raise();
        }

        // =====================================================================
        // Formatting helpers — every one of them lives HERE, never in the View
        // =====================================================================

        /// <summary>
        /// The player's stored FIRST NAME, or the truncated id fallback. This is the raw token
        /// the server holds — it is what seeds the rename field, NOT what a screen renders.
        /// </summary>
        public string NameFor(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return string.Empty;
            if (_names.TryGetValue(playerId, out var n) && !string.IsNullOrEmpty(n)) return n;
            return Truncate(playerId);
        }

        /// <summary>
        /// ⛔ OWNER RULING 2026-09-18: A REMNANT'S SHOWN NAME IS COMPOSED, NOT TYPED.
        /// The player picks only a FIRST NAME (one token — the server's own rule is 3..16 of
        /// [A-Za-z0-9_], which is why a space can never reach this function's first argument),
        /// and the GAME appends their standing: "Bob of RiverRun" once they hold a Circle,
        /// "Bob the Lonely" while they hold none.
        /// ⛔ NEITHER CONNECTIVE IS HARDCODED. "of" lives in `remnant.name.composed`
        /// ("{0} of {1}") and the no-Circle epithet in `remnant.name.alone` ("{0} the Lonely").
        /// An English word baked into C# cannot be translated, and the no-Circle state is an
        /// EPITHET rather than an empty string, so each locale needs somewhere to word it.
        /// ⚠ The bare first name is therefore NEVER the shown name for a named player.
        /// Pure and static: the EditMode suite drives it with no VM at all.
        /// </summary>
        public static string ComposeDisplayName(string firstName, string circleName)
        {
            if (string.IsNullOrEmpty(firstName)) return string.Empty;
            if (string.IsNullOrWhiteSpace(circleName))
            {
                return LocalText.Format("remnant.name.alone", firstName);
            }
            return LocalText.Format("remnant.name.composed", firstName, circleName);
        }

        /// <summary>
        /// What a ROW or the HEADER actually shows for a player: their first name composed with
        /// this Circle, or the truncated id when they have claimed no name yet.
        /// ⚠ A NAMELESS PLAYER IS NEVER COMPOSED — "7QxK...XeFL of Emberwatch" reads as a bug,
        /// not as a member. The fallback stands alone.
        /// </summary>
        public string DisplayNameFor(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return string.Empty;
            if (_names.TryGetValue(playerId, out var n) && !string.IsNullOrEmpty(n))
            {
                return ComposeDisplayName(n, CircleName);
            }
            return Truncate(playerId);
        }

        /// <summary>
        /// Re-composes every shown name. Called whenever EITHER half can have moved — a new
        /// name arriving, or the Circle itself changing — because the shown name is a product
        /// of both and a stale half is a member row that silently names the wrong Circle.
        /// </summary>
        private void RecomposeNames()
        {
            string me = _source != null ? _source.WalletAddress : null;
            MyDisplayNameText = string.IsNullOrEmpty(MyDisplayName)
                ? (string.IsNullOrEmpty(me) ? string.Empty : Truncate(me))
                : ComposeDisplayName(MyDisplayName, CircleName);

            Guard.Try(Sys, "CircleScreenVM.RecomposeNames", () =>
            {
                foreach (var row in Members)
                {
                    row.DisplayNameText = DisplayNameFor(row.PlayerId);
                    row.HasDisplayName = _names.ContainsKey(row.PlayerId);
                }
            });
        }

        /// <summary>"7Qx...4fL" — a base58 address is never shown in full on a phone.</summary>
        public static string Truncate(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Length <= 11) return value;
            var sb = new StringBuilder(11);
            sb.Append(value.Substring(0, 4));
            sb.Append("...");
            sb.Append(value.Substring(value.Length - 4));
            return sb.ToString();
        }

        private void ApplyVigilWeight(double weight, bool degraded)
        {
            VigilWeight = weight;
            VigilDegraded = degraded;
            VigilWeightText = FormatNumber(weight);
            VigilDegradedKey = degraded ? "circle.vigil.degraded" : string.Empty;
            int unlocked = VigilCeremonyWords.HighestUnlockedTier(weight);
            // dotr.vigil sets Heart dressing without faking vigil_weight. When the
            // live weight unlocks nothing, the header word follows the dressing tier.
            if (unlocked <= 0)
                unlocked = VigilCeremonyLedger.Shared.CurrentDressingTier;
            VigilWordKey = VigilCeremonyWords.WordKeyForTier(unlocked);
        }

        /// <summary>Two decimals at most, invariant — never a locale-formatted float on the wire.</summary>
        public static string FormatNumber(double value)
        {
            return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A closed ballot's `result.reason` -> the ONE key that explains the outcome.
        /// ⛔ THE VALUE IS READ, NOT JUST ITS EMPTINESS. The server answers exactly three
        /// reasons — `passed`, `no_votes`, `insufficient_turnout` (declared at
        /// api/_lib/clan-ballot.js:623-625, produced at :642 and :695) — and the two failing
        /// ones are DIFFERENT THINGS to a Circle: "nobody voted" is a nudge to vote, while
        /// "not enough of you voted" says the votes cast were real but too few. Collapsing
        /// both into one "failed" sentence, which an earlier draft of this method did, throws
        /// away the only information a member could act on.
        /// An unrecognised non-empty reason still reads as a failure rather than as unknown:
        /// the server told us the ballot did not pass, and only the WHY is unfamiliar.
        /// Public + static so the EditMode suite drives the whole table with no VM.
        /// </summary>
        public static string ResultKeyFor(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "circle.ballot.result.unknown";
            switch (reason)
            {
                case "passed": return "circle.ballot.result.passed";
                case "no_votes": return "circle.ballot.result.noVotes";
                case "insufficient_turnout": return "circle.ballot.result.insufficientTurnout";
                default: return "circle.ballot.result.failed";
            }
        }

        private static string RoleKey(string role)
        {
            if (string.Equals(role, "leader", StringComparison.Ordinal)) return "circle.role.leader";
            if (string.Equals(role, "officer", StringComparison.Ordinal)) return "circle.role.officer";
            return "circle.role.member";
        }
    }
}
