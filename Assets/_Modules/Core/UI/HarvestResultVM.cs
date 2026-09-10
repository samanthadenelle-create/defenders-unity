// =============================================================================
// HarvestResultVM - WO-1525. THE HARVEST RESULT, AS ROWS INSTEAD OF PROSE.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.UI
//
// THE MEASURED DEFECT (owner, 2026-09-06 20:29, device frame
// Logs/device/screens/owner-screen-20260906-202933.png, build 358574):
//
//     "Harvest results feel off and way to much to read, needs organized and
//      visually pleasing"
//
// The screen carried FOUR prose lines per resource, three resources over, plus a
// CLOSE - eleven lines to say three numbers three times:
//
//     Stone storage: 3000 / 3000 (full)
//     Collected: 0 of 32307 from your collectors | Uncollected: 32307
//     Those 32307 stone are still waiting in your collectors - nothing was lost.
//     Stone storage 3000 is full. Spend stone, or upgrade a Stoneyard, then collect again.
//
// =============================================================================
//  (!) THIS TYPE DECIDES; THE MODAL ONLY DRAWS.
// =============================================================================
// Every number, every word and every door lives here, as a PURE function of the
// clamp events plus ONE injected live signal (how many storage containers of that
// resource are already built). That is what makes the shape oracle-drivable with
// no canvas, no scene and no PlayMode - the same seam WelcomeBackDoorsVM (WO-1408)
// established, and for the same reason. HarvestOverflowModal must never grow a
// second opinion about what a row says.
//
// =============================================================================
//  (!) HIERARCHY BY SIZE, WEIGHT AND POSITION - NEVER BY HUE.
// =============================================================================
// The owner is red/green colourblind. FULL is the WORD "FULL"; a bar that is full
// still SAYS so in its value label. Nothing on this screen is legible only in
// colour, which is why every state this VM computes is emitted as text.
//
// =============================================================================
//  (!) WAITING, NEVER LOST - AND THE CONVERSE IS JUST AS BINDING.
// =============================================================================
// WO-1392 (collectors) and WO-1434 (Echo silo) both proved that the refused units
// STAY where they were, so their rows say WAITING and the footer reassures ONCE.
// But the burn branch is still a live code path (OfflineHarvestService.Grant
// discards its pre-clamp accrual), and printing "nothing was lost" over a row that
// genuinely burned would be the WO-1434 lie inverted. So the footer is
// SOURCE-AWARE: the reassurance appears only when EVERY row retained.
//
// ASCII ONLY - every string below reaches a mobile font atlas, and the figures are
// formatted with the invariant culture so a device locale cannot inject a
// non-ASCII group separator.
//
// =============================================================================
//  (!) WO-1099 - AND ABOVE THE CAP, THE REASSURANCE IS ITSELF THE LIE.
// =============================================================================
// THE MEASURED DEFECT (owner F8 seq=4974, 2026-09-09 13:06:17, frame
// docs/ui-evidence/harvest-result-2026-09-09/flag-seq4974-harvest-result.png):
//
//     Wood   0    732,031 / 34,000  OVER    4,213 waiting, safe
//     Iron   0    639,336 / 34,000  OVER    2,031 waiting, safe
//     Stone  0    411,480 / 34,000  OVER    4,441 waiting, safe
//     "Nothing was lost - every waiting unit banks as soon as there is room."
//
// The block above says the reassurance is source-aware; it was not CAP-aware. At a
// FULL bank "as soon as there is room" is true - room arrives by spending or by
// upgrading, and those rows carry the door. ABOVE the cap it is a promise the game
// cannot keep: 34,000 is the L6 ceiling (TownBankCapacity's own words), so 732,031
// is 21x the most storage that can ever exist and no build makes room. Under a
// banked column of 0 the screen read: you collected nothing, and nothing is wrong.
//
// So the footer gained a THIRD branch, above the reassurance and below the burn:
// over cap leads with the ACTION (spend), names the container as the ceiling the
// player is above, states how far over they are, and keeps WO-1434's safety
// promise in the tail instead of at the front. The three amounts - banked, pending,
// over-by - are now exposed as NUMBERS on every row (see HarvestResultRow), all of
// them lifted off the same BankOverflowStatus the bank published.
//
// !! ORIGIN OF THE 21x BALANCE IS NOT THIS FILE'S BUSINESS AND IS NOT CLAIMED HERE.
// docs/READY_RCA_2026-09-09.md row 1099 established it arrived via tester/dev codes,
// so normal accrual is not proven broken. This change is the PRESENTATION half, which
// is a defect either way: the copy was false in this state regardless of how the
// state was reached.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Economy;

namespace DeNelle.Core.UI
{
    /// <summary>One resource's harvest outcome: what banked, what waits, the store, one door.</summary>
    public sealed class HarvestResultRow
    {
        /// <summary>Player word for the resource ("Wood"). Never empty.</summary>
        public string ResourceName = string.Empty;

        /// <summary>THE BIG NUMBER - what actually banked ("+2,814"). "0" when nothing fit
        /// (never "+0": a plus sign in front of nothing reads as a gain that did not happen).</summary>
        public string BankedText = string.Empty;

        /// <summary>The second number - what did not bank, and its fate. "23,353 waiting, safe"
        /// for a retaining producer; "12,291 lost" for the burn path. Empty when nothing was
        /// refused.</summary>
        public string WaitingText = string.Empty;

        /// <summary>The bar's inline value label: "26,000 / 26,000  FULL". The state WORD is part
        /// of this string on purpose - see the colourblind note in the file header.</summary>
        public string StorageText = string.Empty;

        /// <summary>"FULL", "OVER" or empty. Emitted separately so an oracle can assert the word
        /// exists without parsing the figures.</summary>
        public string StateWord = string.Empty;

        /// <summary>Store fill in 0..1 for the bar (AFTER this collect).</summary>
        public float Fill01;

        /// <summary>The store total after this collect, and its cap - the two figures inside
        /// <see cref="StorageText"/>, exposed for the oracle.</summary>
        public int After;
        public int Max;

        /// <summary>The one action chip's face ("UPGRADE LUMBERYARD" / "BUILD STONEYARD" /
        /// "SPEND WOOD"). Empty when this row needs no door.</summary>
        public string ActionText = string.Empty;

        /// <summary>
        /// The chip's face SPLIT IN TWO, so the modal can draw it on two lines instead of
        /// ellipsising it (owner device 2026-09-07 01:14, Logs/device/screens/
        /// owner-screen-20260907-011426.png: "UPGRADE LUMBER..." and "UPGRADE STONEYA..." both
        /// truncated on a 2670x1200 phone while "UPGRADE FOUNDRY" fit).
        /// <para>!! AN ELLIPSIS IS NOT A FIT. The chip names the ONE thing the player must go and
        /// do; "UPGRADE STONEYA..." names a building that does not exist. <see cref="ActionText"/>
        /// is kept EXACTLY as it was (oracles grep it, and it is the trace/accessibility string);
        /// these two fields are the same words with the break point authored, never re-derived by
        /// the View.</para>
        /// </summary>
        public string ActionVerb = string.Empty;
        public string ActionTarget = string.Empty;

        /// <summary>Units refused that are STILL held by a producer the player owns.</summary>
        public int WaitingUnits;

        /// <summary>Units refused that were genuinely discarded (no cache exists to hold them).</summary>
        public int BurnedUnits;

        /// <summary>How many producer statuses were merged into this row (1 = a single producer).
        /// The oracle seam for [one-producer-per-resource].</summary>
        public int MergedSources = 1;

        /// <summary>Where the chip leads. Routed through <see cref="PanelRouter"/>.</summary>
        public PanelId ActionDoor = PanelId.Manage;

        /// <summary>The tab handed to <c>PanelRouter.Open(id, context)</c>.</summary>
        public string ActionContext = string.Empty;

        /// <summary>True when this row carries a door.</summary>
        public bool HasAction => !string.IsNullOrEmpty(ActionText);

        /// <summary>True when the refused units are STILL somewhere the player owns them.</summary>
        public bool Waits;

        /// <summary>True when the refused units were genuinely discarded by the producer.</summary>
        public bool Burned;

        /// <summary>"collectors" / "silo" / "burned" / "clean" - the trace word.</summary>
        public string TraceKind = "clean";

        // =====================================================================
        //  (!) WO-1099 - THE THREE AMOUNTS, AS NUMBERS, TRUTHFULLY.
        // =====================================================================
        // The owner's F8 seq=4974 frame showed a banked column of `0` under a bar
        // reading `732,031 / 34,000  OVER`, with a footer promising the waiting units
        // would bank "as soon as there is room". Read together that is: you collected
        // nothing, and nothing is wrong. Each half was individually defensible and the
        // pair misled.
        //
        // The banked `0` is COMPUTED, not defaulted: TownBankCapacity's over-cap branch
        // logs "earned {requested}, added 0" by design (TownBankCapacity.cs:781-785), so
        // `Granted == 0` above the cap is the bank's own answer. What was missing was
        // the THIRD number - how far above the ceiling the store actually sits - and any
        // instruction about what to do with it.
        //
        // !! ALL THREE COME OFF THE SAME BankOverflowStatus THE BANK PUBLISHED. There is
        // no second arithmetic and no second reader: `Granted`, `Lost` (split into
        // Waiting/Burned by Merge) and `Current`/`Max` are the exact figures the clamp
        // weighed. The over-by is the bank's own subtraction - the same `{current - max}
        // above capacity` it prints at TownBankCapacity.cs:783. The VM never calls
        // TownBankCapacity.MaxOf; that would be the second opinion this seam exists to
        // prevent.

        /// <summary>WHAT BANKED, as a number (the figure behind <see cref="BankedText"/>).
        /// Zero above the cap, and that zero is the bank's computed answer, not a default.</summary>
        public int BankedUnits;

        /// <summary>WHAT IS PENDING - the units a producer still holds. Alias of
        /// <see cref="WaitingUnits"/>, named the way WO-1099 asks for it so an oracle can read
        /// "pending" without knowing the older field name.</summary>
        public int PendingUnits;

        /// <summary>HOW FAR ABOVE THE CEILING the store sits after this collect
        /// (<c>After - Max</c>, floored at zero). The number the OVER bar never spelled out.</summary>
        public int OverCapUnits;

        /// <summary>"698,031 over cap" - <see cref="OverCapUnits"/> as an ASCII figure, or empty
        /// when the store is not above its ceiling. Exposed for the oracle and folded into the
        /// footer; NOT part of <see cref="HarvestResultVM.AllText"/>, because the modal does not
        /// draw it as its own field and AllText may only carry what is actually drawn.</summary>
        public string OverCapText = string.Empty;

        /// <summary>True when this row's store is ABOVE its cap - the state in which more
        /// storage is not the fix and the door reads SPEND.</summary>
        public bool OverCap;
    }

    /// <summary>
    /// The whole HARVEST RESULT screen as data: the rows, the one footer line, and the
    /// "+N more" tail. Built by <see cref="Build"/>; drawn by HarvestOverflowModal.
    /// </summary>
    public sealed class HarvestResultVM
    {
        /// <summary>
        /// How many rows are drawn before the rest collapse into "+N more".
        ///
        /// <para>THREE, and the number is DERIVED from the modal's own constants, not chosen.
        /// HarvestOverflowModal draws the row band between <c>RowsTop</c> (0.86) and
        /// <c>RowsFloorWithOverflow</c> (0.36) with a <c>RowGap</c> of 0.02, so a plate is
        /// <c>(0.50 - 0.06) / 3 = 0.147</c> of the content rect - and the chip is drawn at the
        /// plate's FULL height (the WelcomeBackPopup DoorRowH trick, whose own comment records
        /// what a 0.88 inset gave back). At FOUR rows the same arithmetic yields 0.12, which is
        /// under the touch floor on a 1080-tall screen: a fourth row would put every door out of
        /// reach, which is worse than a collapsed line.</para>
        ///
        /// <para>It also costs nothing today: TownBankCapacity.UncappableResources (:265-269)
        /// exempts Crystals and Coins BY DESIGN, so only Wood / Iron / Food can produce an
        /// overflow row at all and a fourth aggregated row cannot occur. The "+N more" tail
        /// exists for the day that stops being true.</para>
        ///
        /// <para>(!) The exact pixel heights are OWED TO A CAPTURE, not asserted here - this lane
        /// ran no Unity, and BuildObsidianModal's content rect was not measured.</para>
        /// </summary>
        public const int MaxRows = 3;

        /// <summary>The Manage tab the doors land on. ManageScreenPanel.Open(string) accepts
        /// exactly "Defense", "Buildings", "Research" and "Troops[:id]" and IGNORES anything
        /// else, so this string is not free - it is the one that browses structures.</summary>
        public const string BuildingsTab = "Buildings";

        /// <summary>The rows, in the order they arrived. Never null.</summary>
        public readonly List<HarvestResultRow> Rows = new List<HarvestResultRow>();

        /// <summary>How many resources the caller handed in (may exceed <see cref="MaxRows"/>).</summary>
        public int TotalRowCount;

        /// <summary>"+2 more" when resources were collapsed, else empty.</summary>
        public string OverflowLine = string.Empty;

        /// <summary>The ONE footer sentence, said once for the whole screen instead of once per
        /// resource. Empty when nothing was refused at all.</summary>
        public string FooterLine = string.Empty;

        /// <summary>True when the footer is the WO-1434 reassurance (every row retained).</summary>
        public bool FooterReassures;

        /// <summary>True when the footer is the WO-1099 over-cap instruction - the store is above
        /// its ceiling, so the screen names the ONE thing that unblocks it instead of promising
        /// room that cannot arrive on its own.</summary>
        public bool FooterOverCap;

        /// <summary>The totals the footer speaks for, summed across EVERY merged resource (not
        /// just the drawn <see cref="MaxRows"/>), so the one sentence is true about the whole
        /// harvest. Each is the bank's own figure - see the WO-1099 block on HarvestResultRow.</summary>
        public int TotalBanked;
        public int TotalPending;
        public int TotalOverCap;

        /// <summary>The one trace line the modal emits per open.</summary>
        public string TraceLine
        {
            get
            {
                int waiting = 0, burned = 0, doors = 0;
                for (int i = 0; i < Rows.Count; i++)
                {
                    var r = Rows[i];
                    if (r == null) continue;
                    if (r.Waits) waiting++;
                    if (r.Burned) burned++;
                    if (r.HasAction) doors++;
                }
                string footerWord = FooterReassures
                    ? "reassure"
                    : (FooterOverCap ? "overcap" : (string.IsNullOrEmpty(FooterLine) ? "none" : "loss"));
                return "statuses=" + SourceStatusCount + " merged=" + TotalRowCount +
                       " shown=" + Rows.Count + " waiting=" + waiting +
                       " burned=" + burned + " doors=" + doors +
                       " banked=" + TotalBanked + " pending=" + TotalPending +
                       " overBy=" + TotalOverCap +
                       " footer='" + footerWord + "'";
            }
        }

        /// <summary>How many raw producer statuses were handed to <see cref="Build"/> before the
        /// merge. Differs from <see cref="TotalRowCount"/> exactly when two producers overflowed
        /// the same resource - the case that produced the owner's "+3 more".</summary>
        public int SourceStatusCount;

        /// <summary>
        /// EVERY PLAYER-FACING STRING THIS SCREEN WILL DRAW, ON ONE LINE (CLAUDE.md section 12).
        /// <para>The modal traced its INPUT numbers and a row COUNT, which is why a screenshot of
        /// the owner's device could not be checked against what the code believed it had written.
        /// This is the line that closes that: one grep for <c>harvest-result screen</c> returns the
        /// exact text, and the fixture asserts the same string equals the banked deltas.</para>
        /// </summary>
        public string ScreenText => AllText().Replace("\n", " | ");

        /// <summary>
        /// THE PURE SEAM. Clamp events in, rows out - no service lookup, no clock, no scene.
        /// </summary>
        /// <param name="results">The aggregated overflow rows. Null/empty yields an empty VM.</param>
        /// <param name="builtContainersFor">How many storage containers of that resource the
        /// player has ALREADY BUILT - the one live signal, and the only thing that decides
        /// BUILD versus UPGRADE. Null (or a negative answer) is read as ZERO, which produces
        /// BUILD: offering "upgrade" for a structure that does not exist is the worse miss.</param>
        public static HarvestResultVM Build(IReadOnlyList<BankOverflowStatus> results,
                                            Func<BankResource, int> builtContainersFor)
        {
            var vm = new HarvestResultVM();
            if (results == null || results.Count == 0) return vm;

            // (!) ONE ROW PER RESOURCE - THE FIX FOR THE TWO-SCREEN DIVERGENCE.
            // See the Merge header. TotalRowCount is the MERGED count, because that is what the
            // player is being shown a subset of; the raw status count is TraceLine's business.
            var merged = Merge(results);
            vm.SourceStatusCount = results.Count;
            vm.TotalRowCount = merged.Count;

            bool anyRefused = false;
            bool anyBurned = false;
            bool anyOverCap = false;
            var overCapNames = new List<string>();
            var overCapContainers = new List<string>();

            for (int i = 0; i < merged.Count; i++)
            {
                var s = merged[i];
                if (s.Waiting > 0 || s.Burned > 0) anyRefused = true;
                if (s.Burned > 0) anyBurned = true;

                string name = string.IsNullOrEmpty(s.ResourceName) ? "Resource" : s.ResourceName;
                string container = string.IsNullOrEmpty(s.ContainerName) ? "Storehouse" : s.ContainerName;

                // WO-1392's figure, unchanged: BankOverflowStatus.Current is the wallet BEFORE the
                // grant was applied (that is what the clamp weighed), so the number the player can
                // check against the HUD rail is Current + Granted. After the merge, Current is the
                // EARLIEST measurement across this resource's producers (Merge picks the minimum)
                // and Granted is their SUM - so `after` is still exactly one post-collect figure.
                int granted = s.Granted > 0 ? s.Granted : 0;
                long after64 = (long)Math.Max(0, s.Current) + granted;
                int after = after64 > int.MaxValue ? int.MaxValue : (int)after64;
                int max = s.Max;

                // WO-1099 - THE THIRD NUMBER. The bank's own subtraction (TownBankCapacity.cs:786
                // prints "{current - max} above capacity"), never a second reader of the caps.
                long over64 = max > 0 ? (long)after - max : 0L;
                int overBy = over64 > 0 ? (over64 > int.MaxValue ? int.MaxValue : (int)over64) : 0;

                // (!) THE FOOTER SPEAKS FOR THE WHOLE HARVEST, so its totals are accumulated over
                // EVERY merged resource - before the MaxRows cut, not after. A number that changed
                // depending on how many plates fit on screen would be the same class of lie this
                // ticket is about.
                vm.TotalBanked += granted;
                vm.TotalPending += s.Waiting > 0 ? s.Waiting : 0;
                if (s.OverCap)
                {
                    anyOverCap = true;
                    vm.TotalOverCap += overBy;
                    if (!overCapNames.Contains(name)) overCapNames.Add(name);
                    if (!overCapContainers.Contains(container)) overCapContainers.Add(container);
                }

                if (vm.Rows.Count >= MaxRows) continue;

                var row = new HarvestResultRow
                {
                    ResourceName = name,
                    BankedText = granted > 0 ? "+" + N(granted) : "0",
                    After = after,
                    Max = max,
                    Fill01 = max > 0 ? Clamp01((float)after / max) : 1f,
                    Waits = s.Waiting > 0,
                    Burned = s.Burned > 0,
                    WaitingUnits = s.Waiting,
                    BurnedUnits = s.Burned,
                    MergedSources = s.Sources,
                    TraceKind = s.TraceKind,
                    // WO-1099 - the three amounts as NUMBERS, straight off the clamp event.
                    BankedUnits = granted,
                    PendingUnits = s.Waiting > 0 ? s.Waiting : 0,
                    OverCapUnits = s.OverCap ? overBy : 0,
                    OverCapText = s.OverCap && overBy > 0 ? N(overBy) + " over cap" : string.Empty,
                    OverCap = s.OverCap,
                };

                // THE SECOND NUMBER. The law word rides WITH the figure, so a player who reads
                // nothing else still learns the fate of what did not fit. A merged row can carry
                // BOTH fates at once (the collectors retain, an away node haul has nowhere to be
                // retained) - and when it does, BOTH are said. Collapsing them into one figure is
                // the WO-1434 lie in either direction.
                // !! "LOST" IS THE RIGHT WORD ON THE BURN PATH AND IT STAYS. WO-1434 forbids
                // calling a RETAINED amount lost; it does not license softening a genuine discard.
                // Away node/settlement/pet yield has no pending pool (OfflineHarvestService.Grant's
                // WO-1445 block), so those units are gone, and a euphemism there would be the same
                // dishonesty pointed the other way. Pinned by [burn-never-lies].
                if (s.Waiting > 0 && s.Burned > 0)
                    row.WaitingText = N(s.Waiting) + " waiting, safe - " + N(s.Burned) + " lost";
                else if (s.Waiting > 0)
                    row.WaitingText = N(s.Waiting) + " waiting, safe";
                else if (s.Burned > 0)
                    row.WaitingText = N(s.Burned) + " lost";

                // THE STATE, AS A WORD. OverCap is a DIFFERENT situation from a full bank
                // (BankOverflowStatus.OverCap spends a paragraph on why) and keeps its own word.
                if (s.OverCap) row.StateWord = "OVER";
                else if (max > 0 && after >= max) row.StateWord = "FULL";
                row.StorageText = max > 0 ? N(after) + " / " + N(max) : N(after);
                if (!string.IsNullOrEmpty(row.StateWord))
                    row.StorageText += "  " + row.StateWord;

                // THE ONE DOOR. A row without a state word is not blocked by anything, so it gets
                // no chip - a door onto "nothing to do here" teaches the player the screen lies
                // (the WelcomeBackDoorsVM rule, same words).
                if (!string.IsNullOrEmpty(row.StateWord))
                {
                    int built = 0;
                    if (builtContainersFor != null)
                    {
                        int n = builtContainersFor(s.Resource);
                        built = n > 0 ? n : 0;
                    }
                    row.ActionVerb = s.OverCap ? "SPEND" : (built > 0 ? "UPGRADE" : "BUILD");
                    row.ActionTarget = (s.OverCap ? name : container).ToUpperInvariant();
                    row.ActionText = row.ActionVerb + " " + row.ActionTarget;
                    row.ActionDoor = PanelId.Manage;
                    row.ActionContext = BuildingsTab;
                }

                vm.Rows.Add(row);
            }

            int hidden = vm.TotalRowCount - vm.Rows.Count;
            if (hidden > 0) vm.OverflowLine = "+" + hidden + " more";

            // THE FOOTER - ONCE, for the whole screen, and SOURCE-AWARE. The reassurance is the
            // WO-1434 law sentence and it is only true when every producer on this screen retained.
            if (anyRefused)
            {
                // (!) `vm.TotalOverCap > 0` IS NOT BELT-AND-BRACES. Merge takes the MINIMUM Current
                // and the MAXIMUM Max across a resource's producers, so a status flagged OverCap by
                // one producer can still land at or under the merged ceiling. In that case the
                // player is NOT above the cap after this collect, "storage is 0 over" would be
                // nonsense, and the ordinary reassurance is the true sentence - so fall through.
                if (!anyBurned && anyOverCap && vm.TotalOverCap > 0)
                {
                    // =========================================================
                    //  (!) WO-1099 - ABOVE THE CAP, THE REASSURANCE IS A PROMISE
                    //      THE GAME CANNOT KEEP, SO IT IS NOT SAID.
                    // =========================================================
                    // "every waiting unit banks as soon as there is room" is TRUE at a
                    // FULL bank - room arrives by spending OR by upgrading, and those
                    // rows carry an UPGRADE/BUILD door. It is FALSE above the cap: on
                    // the owner's F8 seq=4974 frame the store sat at 732,031 against
                    // the L6 ceiling of 34,000, so no amount of storage the game can
                    // ever build makes room. Room only arrives by SPENDING.
                    //
                    // !! AND THAT IS WHY THE VERB HERE IS SPEND, NOT "SPEND OR UPGRADE".
                    // BankOverflowStatus.OverCap (TownBankCapacity.cs) spends a paragraph
                    // on over-cap being a DIFFERENT situation from a full bank, the bank's
                    // own over-cap Warn deliberately drops the "build or upgrade a
                    // {container}" clause it prints when merely full, and
                    // HarvestResultShapeRegression [overcap-spends] fails the build if the
                    // door reads anything but SPEND. Offering an upgrade here would be the
                    // false reassurance again in a new costume - it would sell the player a
                    // container that cannot help.
                    //
                    // The container is still NAMED, as the ceiling they are above rather
                    // than as a thing to buy, because "your Lumberyard cap" is the phrase
                    // that lets the player find the number they are fighting.
                    //
                    // WO-1434 law is preserved in the tail: the pending units ARE safe, and
                    // the screen still says so - it just no longer says so FIRST, and no
                    // longer claims they will bank on their own.
                    vm.FooterReassures = false;
                    vm.FooterOverCap = true;
                    string spendList = JoinWords(overCapNames, "or");
                    string capList = JoinWords(overCapContainers, "and");
                    string capWord = overCapContainers.Count > 1 ? " caps" : " cap";
                    vm.FooterLine =
                        "Spend " + (spendList.Length > 0 ? spendList : "resources") +
                        " to get back under your " + (capList.Length > 0 ? capList + capWord : "storage cap") +
                        " - storage is " + N(vm.TotalOverCap) + " over, so nothing banks yet. " +
                        N(vm.TotalPending) + " waiting stays safe until it does.";
                }
                else if (!anyBurned)
                {
                    vm.FooterReassures = true;
                    vm.FooterLine = "Nothing was lost - every waiting unit banks as soon as there is room.";
                }
                else
                {
                    // WO-1445 / WO-1461 law: spoils above the cap are never SILENTLY lost. Where no
                    // cache exists to hold them, the screen SAYS SO IN WORDS rather than printing a
                    // promise it cannot keep. Away node/settlement/pet yield has no pending store -
                    // OfflineHarvestService.Grant writes the wallet directly - so this sentence
                    // names the reason, not just the outcome.
                    vm.FooterReassures = false;
                    vm.FooterLine = "Storage was full, so the amounts marked lost never reached it - " +
                                    "away gathering has no store to wait in. Make room first.";
                }
            }

            // CLAUDE.md section 12 - the amounts, named, at the seam that decided them. The modal
            // traces TraceLine too (HarvestOverflowModal.cs:146), but the modal is not the only
            // caller of this seam and a headless run must be able to prove the numbers without a
            // canvas. One Step per build; TraceLine now carries banked/pending/overBy.
            FlowTrace.Step("Bank", "harvest-result vm: " + vm.TraceLine);

            return vm;
        }

        /// <summary>
        /// "Wood", "Wood or Iron", "Wood, Iron or Stone" - ASCII, no Oxford comma, and the
        /// conjunction is the caller's ("or" for a choice of things to spend, "and" for the set of
        /// caps they are above). Empty list yields an empty string so the caller can substitute.
        /// </summary>
        private static string JoinWords(List<string> words, string conjunction)
        {
            if (words == null || words.Count == 0) return string.Empty;
            if (words.Count == 1) return words[0];
            var sb = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0) sb.Append(i == words.Count - 1 ? " " + conjunction + " " : ", ");
                sb.Append(words[i]);
            }
            return sb.ToString();
        }

        // =====================================================================
        //  (!) THE MERGE - ONE ROW PER RESOURCE, AND IT IS THE ONE-PRODUCER FIX
        // =====================================================================
        //
        // THE MEASURED DEFECT (owner's Seeker, build 358872, two frames one minute apart):
        //
        //   Logs/device/screens/owner-harvest-20260907-011321.png  (WELCOME BACK)
        //       WOOD +2906      40972 MORE WAITS
        //       IRON +1535      21843 MORE WAITS
        //       STONE 45257 WAITING   STORAGE FULL - STAYS PUT
        //       footer: "108072 stays where it is..."   (= 40972 + 21843 + 45257)
        //
        //   Logs/device/screens/owner-screen-20260907-011426.png   (HARVEST RESULT, after COLLECT)
        //       WOOD  +2,906   12,236 waiting, safe
        //       IRON  +1,535    6,035 waiting, safe
        //       STONE      0   30,932 waiting, safe
        //       "+3 more"
        //
        // The banked figures AGREE to the unit. The WAITING figures did not, and the reason was not
        // a maths bug - it was a MISSING MERGE:
        //   !! READ THE WELCOME-BACK COLUMN CAREFULLY: "40972 MORE WAITS" is
        //   OfflineHarvestService.ReturnRowDestiny printing r.Waits = Pending - Banks. It is what
        //   will STILL be waiting AFTER the tap, NOT the pending pool. (Reading it as pending is an
        //   easy off-by-Banks and it fits the STONE row by accident, because Banks is 0 there.)
        //   So the two screens' waiting figures are the same quantity, and they reconcile exactly:
        //       wood   12,236 (collector remainder) + 28,736 (Echo silo) = 40,972
        //       iron    6,035                       + 15,808             = 21,843
        //       stone  30,932                       + 14,325             = 45,257
        //   ...which is the welcome-back column to the unit, and sums to its own 108,072 footer.
        // The welcome-back screen already merges both producers per resource
        // (OfflineHarvestService.BuildReturnRows, which sums FromCollectors + FromSilo). This
        // screen did not: it received the collector statuses AND the silo statuses as SIX rows,
        // drew the first three, and collapsed the other three into "+N more". So "+3 more" was
        // never a hidden fourth resource - it was the OTHER HALF OF THE SAME THREE, and the
        // player was shown 12,236 for a resource the previous screen had called 40,972.
        //
        // !! THE "+3 more" LINE WAS THE SYMPTOM, NOT THE BUG, AND RAISING MaxRows WOULD HAVE
        // ENTRENCHED IT: six rows for three resources is the defect, not a layout shortage.
        // TownBankCapacity.UncappableResources (:265-269) exempts Crystals and Coins, so at most
        // three resources can overflow at all - after this merge, TotalRowCount can never exceed
        // MaxRows from the live callers, and "+N more" is unreachable. It is KEPT (not deleted)
        // as the honest tail for the day a fourth cappable resource is introduced.
        //
        // Merged in FIRST-APPEARANCE order, which is the rail order the collector rows arrive in
        // (ResourceCollectorService.RailOrder) - never re-sorted here, so the two screens list the
        // three resources in the same order as well as with the same numbers.
        // =====================================================================

        /// <summary>One resource's outcome after every producer's status has been folded together.</summary>
        public struct MergedResource
        {
            public BankResource Resource;
            public string ResourceName;
            public string ContainerName;
            /// <summary>Everything every producer asked the bank for, this resource.</summary>
            public int Requested;
            /// <summary>Everything that actually banked, this resource.</summary>
            public int Granted;
            /// <summary>Refused units a producer STILL holds (collectors, Echo silo).</summary>
            public int Waiting;
            /// <summary>Refused units nothing holds - genuinely not added.</summary>
            public int Burned;
            public int Max;
            /// <summary>The EARLIEST wallet reading across this resource's producers.</summary>
            public int Current;
            public bool OverCap;
            /// <summary>How many statuses folded in.</summary>
            public int Sources;
            /// <summary>"clean" / "collectors" / "silo" / "burned" / "mixed".</summary>
            public string TraceKind;
        }

        /// <summary>
        /// Fold every producer's <see cref="BankOverflowStatus"/> into ONE row per resource.
        /// PURE - no service lookup, no clock, no scene. Pinned by HarvestResultShapeRegression
        /// [one-row-per-resource] / [merged-waiting-equals-welcome-back].
        /// </summary>
        public static List<MergedResource> Merge(IReadOnlyList<BankOverflowStatus> results)
        {
            var order = new List<BankResource>();
            var by = new Dictionary<BankResource, MergedResource>();
            if (results == null) return new List<MergedResource>();

            for (int i = 0; i < results.Count; i++)
            {
                var s = results[i];
                int refused = s.Lost > 0 ? s.Lost : 0;
                int granted = s.Granted > 0 ? s.Granted : 0;
                int current = Math.Max(0, s.Current);
                bool retains = Retains(s.Source);
                string word = SourceWord(s.Source);

                if (!by.TryGetValue(s.Resource, out var m))
                {
                    order.Add(s.Resource);
                    m = new MergedResource
                    {
                        Resource = s.Resource,
                        ResourceName = s.ResourceName,
                        ContainerName = s.ContainerName,
                        Max = s.Max,
                        Current = current,
                        Sources = 0,
                        TraceKind = "clean",
                    };
                }

                // The NAME and the CONTAINER are the player's words for the resource; the first
                // non-empty one wins so a producer that left them blank cannot blank the row.
                if (string.IsNullOrEmpty(m.ResourceName)) m.ResourceName = s.ResourceName;
                if (string.IsNullOrEmpty(m.ContainerName)) m.ContainerName = s.ContainerName;

                m.Requested += s.Requested > 0 ? s.Requested : 0;
                m.Granted += granted;
                if (retains) m.Waiting += refused; else m.Burned += refused;
                if (s.Max > m.Max) m.Max = s.Max;
                // EARLIEST wallet reading: the collector sweep measured the store BEFORE the tap,
                // the silo clamp measured it after the collectors had already banked. The minimum
                // is the pre-tap figure, and `Current + Granted` is then the ONE post-collect total.
                if (current < m.Current) m.Current = current;
                m.OverCap |= s.OverCap;
                m.Sources++;
                if (refused > 0)
                    m.TraceKind = m.TraceKind == "clean" || m.TraceKind == word ? word : "mixed";

                by[s.Resource] = m;
            }

            var list = new List<MergedResource>(order.Count);
            for (int i = 0; i < order.Count; i++) list.Add(by[order[i]]);
            return list;
        }

        /// <summary>
        /// True when the producer named by <paramref name="source"/> KEEPS what the cap refused.
        /// <para>The two live producers both do: ResourceCollector.Collect only drains what banked
        /// (WO-1392) and EchoService.DumpSilos settles against the APPLIED basket (WO-1434, proven
        /// on the owner's Seeker - pool 57600 survived three dumps). Everything else is read as a
        /// burn, which is the safe direction: it never promises safety that was not proven.</para>
        /// </summary>
        public static bool Retains(string source)
        {
            if (string.IsNullOrEmpty(source)) return false;
            if (string.Equals(source, HarvestOverflowModal.CollectorSource, StringComparison.Ordinal)) return true;
            return source.IndexOf("DumpSilos", StringComparison.Ordinal) >= 0;
        }

        /// <summary>"collectors" / "silo" / "burned" - the trace word for one row's producer.</summary>
        public static string SourceWord(string source)
        {
            if (string.Equals(source, HarvestOverflowModal.CollectorSource, StringComparison.Ordinal)) return "collectors";
            if (!string.IsNullOrEmpty(source) && source.IndexOf("DumpSilos", StringComparison.Ordinal) >= 0) return "silo";
            return "burned";
        }

        /// <summary>Grouped figure, ASCII-safe. InvariantCulture on purpose: a device locale can
        /// otherwise group with U+00A0 (narrow no-break space), which renders as tofu.</summary>
        public static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>Every player-facing string this VM produced, in reading order - the one seam
        /// an oracle uses to sweep for ASCII, for a repeated sentence, or for line count.</summary>
        public string AllText()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Rows.Count; i++)
            {
                var r = Rows[i];
                if (r == null) continue;
                sb.Append(r.ResourceName).Append('\n')
                  .Append(r.BankedText).Append('\n')
                  .Append(r.StorageText).Append('\n');
                if (!string.IsNullOrEmpty(r.WaitingText)) sb.Append(r.WaitingText).Append('\n');
                if (r.HasAction) sb.Append(r.ActionText).Append('\n');
            }
            if (!string.IsNullOrEmpty(OverflowLine)) sb.Append(OverflowLine).Append('\n');
            if (!string.IsNullOrEmpty(FooterLine)) sb.Append(FooterLine);
            return sb.ToString();
        }
    }
}
