// =============================================================================
// ManageArt - WO-2002. The ONE place a Manage surface names a frame or a status
// medallion, and the ONE loader that turns a Resources key into a Sprite.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Manage
//
// ⚠ THE DELIVERED FRAME SET IS NOT INTERCHANGEABLE, AND THAT IS THE WHOLE REASON
// THIS FILE EXISTS. Measured 2026-09-06 in Assets/Resources/RpgUi/manage/ (four
// 512px frames + five 256px status medallions):
//   * frame-tile      - OPAQUE centre
//   * frame-selected  - OPAQUE centre AND its glow BLEEDS OUTSIDE its own rect
//   * frame-locked    - HOLLOW centre
//   * frame-max       - HOLLOW centre
// So they CANNOT be 9-sliced against one another and they cannot be swapped in a
// single Image without the layer under them changing too. The renderer therefore
// paints a PLATE first (always), then the frame as a preserve-aspect layer, then
// the PORTRAIT ON TOP OF IT, and carries frame-selected on a SEPARATE, LARGER rect
// so its bleed has somewhere to go. A frame swap is then a sprite swap, which is
// what WO-2002 asks for - but only because the stack under it never moves.
//
// ⛔ CORRECTED 2026-09-06 (WO-1443 section 2) - THIS PARAGRAPH USED TO SAY "then the
// portrait, then the frame as a preserve-aspect OVERLAY", i.e. the frame ABOVE the
// portrait. That order is only correct if every frame's centre is hollow, and the
// four lines above this one say in the same breath that two of them are not. The
// consequence shipped: an OWNED item wears frame-tile, whose centre alpha MEASURED
// 253/255 across the portrait zone, so the near-black centre painted over the
// portrait and the tile rendered as an EMPTY FRAME. The owner captured it on
// 2026-09-06 - Footman and Archer (barracks tier 1, unlocked, frame-tile) blank while
// Spearman (tier 2, Locked, frame-locked, centre alpha 0) showed its art - and every
// owned BUILD tile had the same defect. Nothing was missing: LoadSprite resolved every
// key and therefore logged no art-miss, which is why the device log carried no troop
// portrait line at all. THE FIX IS THE LAYER ORDER, NOT THE KEY AND NOT THE LOADER.
// A design note that contradicts its own measurements is the failure mode this file's
// header exists to prevent; it is corrected here rather than restated somewhere new.
//
// ⛔ DO NOT 9-SLICE THESE. Do not set Image.type = Sliced on a manage frame; the
// two hollow members have no consistent border inset with the two opaque ones and
// a slice reads as a torn edge on exactly two of the four. Verified by inspection
// of the delivered set, not assumed.
//
// The five status medallion names map EXACTLY onto ManageTileVisualState's five
// members, which is why that enum has five members and not nine (the nine-value
// ManageTileBadge stays the authored state next door in ManageStateModel.cs).
//
// LOADER: same Texture2D fallback and same cache as the shipped Manage art path
// (ManageScreenPanel.LoadManageBuildingSpriteAt, ManageScreenPanel.cs:2176). That
// method is `internal` to DeNelle.Village and unreachable from Core, so this is a
// second implementation of one behaviour - duplicated state, which this repo has
// been burned by repeatedly (CLAUDE.md 2 / 5 / 16).
// ⚠ FOLLOW-UP OWED, HANDED BACK WITH WO-2002: WO-2001 should re-point
// ManageScreenPanel.LoadManageBuildingSpriteAt and HeartPanel.LoadManageSprite at
// THIS method and delete the Village copy, so the tree ends with one loader. That
// is a Village-assembly edit and WO-2002 is forbidden from making it.
// HeartPanel.cs:451-453 already states the rule ("the Heart cannot become the one
// art route with its own loader"); this note is that rule applied to Core.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using UnityEngine;

namespace DeNelle.Core.Manage
{
    /// <summary>Manage art keys and the shared Resources loader. No game rules.</summary>
    public static class ManageArt
    {
        // ── Frames (512px, Assets/Resources/RpgUi/manage/) ────────────────────
        public const string FrameTile = "RpgUi/manage/frame-tile";
        public const string FrameSelected = "RpgUi/manage/frame-selected";
        public const string FrameLocked = "RpgUi/manage/frame-locked";
        public const string FrameMax = "RpgUi/manage/frame-max";

        // ── The delivered Manage UI sheet (Assets/Resources/UI/ElarionMedieval/Manage/) ───────
        /// <summary>
        /// The 36-file Manage UI folder delivered by the art wave (WO-1567 section 1). Frames,
        /// chips, resource glyphs and stat glyphs live here. It is NOT the same folder as
        /// <c>RpgUi/manage/</c>, which holds the older frame/medallion set the tiles still use;
        /// both are real and both are read, so neither key may be "tidied" into the other.
        /// </summary>
        public const string UiFolder = "UI/ElarionMedieval/Manage/";

        // ── Resource glyphs, delivered (256px) ───────────────────────────────
        // ⛔ THE COST ROW PAINTED NO GLYPH AT ALL until 2026-09-07: ManageScreenVM.CostVms set
        // IconKey = null on every row, so the owner's Lumber Mill capture showed two bare numbers
        // ("2600  970") with nothing saying WHICH resource. The five glyphs below have been on
        // disk the whole time. Mapping lives in the MODEL (ManageScreenVM.CostIconFor) because a
        // View that switched on a concept id would be canon-9 derivation.
        public const string ResWood = UiFolder + "res-wood";
        public const string ResStone = UiFolder + "res-stone";
        public const string ResIron = UiFolder + "res-iron";
        public const string ResCrystal = UiFolder + "res-crystal";
        public const string ResGold = UiFolder + "res-gold";
        /// <summary>The clock. Mockup panels 3 and 5 draw the time on its OWN line, never inside
        /// the cost row - a duration is not a price and cannot be compared against a bank.</summary>
        public const string IconTime = UiFolder + "icon-time";

        // -- Stat glyphs, delivered (WO-1567 section 1 art wave; wired WO-1654) ----------------
        // Mockup panel 5 draws a glyph against each of the four troop stats. The four PNGs have
        // been on disk since the art wave and were read by NOTHING until WO-1654 - the same
        // "delivered but unwired" shape as the cost glyphs above, which shipped as two bare
        // numbers naming no resource.
        // THE GLYPH IS A SECOND CHANNEL, NEVER THE ONLY ONE. ManageStatVM.IconKey's doc comment
        // carries the reasoning: the owner is red/green colourblind, WO-1566 C8 makes greyscale
        // the gate, and the WORD is what survives it. These keys ADD to the label.
        // ⚠ THE FILE IS stat-attack.png. It is the ATTACK glyph, which is why WO-1654 also
        // re-labelled the troop row "Damage" -> "Attack": the sheet, the mockup and row 5.3 all
        // said Attack while the card said Damage, so the screen disagreed with its own art.
        /// <summary>Troop HEALTH glyph - stat-health.png.</summary>
        public const string StatHealth = UiFolder + "stat-health";
        /// <summary>Troop ATTACK glyph - stat-attack.png.</summary>
        public const string StatAttack = UiFolder + "stat-attack";
        /// <summary>Troop RANGE glyph - stat-range.png.</summary>
        public const string StatRange = UiFolder + "stat-range";
        /// <summary>Troop SPEED glyph - stat-speed.png.</summary>
        public const string StatSpeed = UiFolder + "stat-speed";

        /// <summary>The four troop-stat glyph keys, in the order the card draws their rows.
        /// Enumerated so a coverage oracle can PROVE they resolve without re-listing them - a
        /// second list is the duplicated state CLAUDE.md sections 2/5/16 record three times over.</summary>
        public static readonly string[] StatIconKeys = { StatHealth, StatAttack, StatRange, StatSpeed };

        /// <summary>
        /// ⭐ WO-1491 - THE BACK ARROW'S FACE. The mockup draws a plain left arrow on every
        /// numbered panel; the device build painted the ASCII literal "&lt;-", which the owner's
        /// capture (Logs/device/screens/owner-screen-20260907-010151.png) shows rendering as
        /// "&lt; -" with the two glyphs kerned apart.
        /// <para>⛔ THIS IS A DELIVERED KIT SPRITE, NOT A FONT GLYPH AND NOT A MIRRORED PLAY
        /// TRIANGLE. <c>icon-back.png</c> arrived with the WO-1567 art wave in
        /// <see cref="UiFolder"/> (36 files, section 1). The old note that rejected
        /// <c>RpgUi/button/arrow.png</c> - "a filled RIGHT-pointing play triangle, and a mirrored
        /// play glyph reads as rewind" - is still true of THAT file and is why this one is bound
        /// instead of it. The non-ASCII arrow CHARACTER stays banned (fonts render it as tofu);
        /// a sprite is not a character.</para>
        /// <para>A miss falls back to the ASCII literal rather than an empty button - the door
        /// must never disappear, which is the WO-1443 defect this file's siblings record.</para>
        /// </summary>
        public const string IconBack = UiFolder + "icon-back";

        /// <summary>The padlock the mockup draws beside a LOCKED row's requirement (panel 7).
        /// It is the same medallion <see cref="StatusFor"/> hands a Locked tile - ONE glyph for
        /// "you cannot have this yet", wherever the player meets it.</summary>
        public const string IconPadlock = StatusLocked;

        // ── Hub card art (mockup panel 1) - NOT DELIVERED. See HubArtFor. ────
        /// <summary>
        /// ⚠ THESE THREE FILES DO NOT EXIST, AND THAT IS A STANDING ART ASK, NOT A BUG.
        /// Mockup panel 1 draws a portrait-shaped illustration filling each hub card (a building,
        /// a helmet, a book). The art wave delivered 36 UI files into <see cref="UiFolder"/> -
        /// frames, chips and glyphs only - and no hub illustration among them.
        /// <para>⛔ AND THE RETIRED LANDSCAPE STRIPS ARE NOT A SUBSTITUTE.
        /// <c>Assets/Resources/UI/ElarionMedieval/cards/*.png</c> are 1963x789 strips drawn for the
        /// retired wide 2x2 seat; preserveAspect-ing one into a tall card letterboxes two thirds of
        /// it black, which reads as BROKEN rather than as art-pending. The hub therefore paints a
        /// FRAMED well and names the three missing keys through FlowTrace once per session
        /// (ManageScreenPanel.RenderLauncherCards).</para>
        /// <para>⚠ SUPERSEDED IN PART 2026-09-07 (WO-1597). The well is no longer EMPTY while the
        /// paintings are owed. Owner, on the device frame: the three cards read as dark plates with
        /// nothing in them. A framed empty well reads as art-pending only to the person who knows
        /// there is an art ask; to the player it reads as broken, which is the same verdict the
        /// landscape strips earned. <see cref="HubArtStandIns"/> now backs each key with a portrait
        /// that RESOLVES TODAY, and <see cref="LoadHubArt"/> paints the painting the moment it
        /// lands. The art ask is unchanged and is still announced by key - what changed is what the
        /// player sees while it is open (CLAUDE.md section 12: a fallback that is named is not a
        /// silent one).</para>
        /// </summary>
        public const string HubArtBuild = UiFolder + "hub-build";
        public const string HubArtArmy = UiFolder + "hub-army";
        public const string HubArtResearch = UiFolder + "hub-research";

        /// <summary>The three hub keys in card order, for the art-ask trace and its oracle.</summary>
        public static readonly string[] HubArtKeys = { HubArtBuild, HubArtArmy, HubArtResearch };

        /// <summary>
        /// ⭐ THE STAND-IN BEHIND EACH HUB KEY - art that RESOLVES TODAY, in card order
        /// (WO-1597, owner's device frame 2026-09-07 10:21).
        ///
        /// <para>⛔ NEVER A BLANK WELL. The three are each destination's own emblem, drawn from the
        /// ONE painting family the Manage screens already use
        /// (<see cref="BuildingPortraitFolder"/>, 1024x1024 squares - verified on disk 2026-09-07,
        /// so nothing here letterboxes the way the retired 1963x789 landscape strips did):
        ///   BUILD    -> lumbermill   (the building mockup panel 3 details, and BUILD's own first row)
        ///   ARMY     -> barracks     (the building that gates every troop - owner ruling 21)
        ///   RESEARCH -> arcane-tower (the Cathedral of Magic, the research home in panels 6 and 7)
        /// </para>
        /// <para>⚠ THESE ARE THE OWNER'S TO SWAP. They are named in WO-1597's RESULT for exactly
        /// that reason; changing one is a one-line edit here and nothing else moves.</para>
        /// <para>⚠ LEVEL 1 ON PURPOSE: the unsuffixed key is the one <see cref="BuildingPortraitKey"/>
        /// produces for level 1, so a stand-in never depends on how far the player has upgraded the
        /// real building.</para>
        /// </summary>
        public static readonly string[] HubArtStandIns =
        {
            BuildingPortraitFolder + "lumbermill",
            BuildingPortraitFolder + "barracks",
            BuildingPortraitFolder + "arcane-tower"
        };

        /// <summary>
        /// The sprite a hub card paints, in card order: the OWED PAINTING FIRST, then the stand-in.
        ///
        /// <para>⛔ KEY ORDER IS THE WHOLE CONTRACT. The moment
        /// <c>Resources/UI/ElarionMedieval/hub-build.png</c> et al. land, they win with no code
        /// change and no layout change - the well's geometry is the same either way. Until then the
        /// card shows a real building rather than a hole.</para>
        /// <para><paramref name="resolvedKey"/> reports WHICH of the two answered, so the caller's
        /// trace can say "stand-in" rather than the reader having to infer it. A null return means
        /// BOTH missed, and <see cref="LoadSprite"/> has already announced each miss by key.</para>
        /// </summary>
        public static Sprite LoadHubArt(int cardIndex, out string resolvedKey)
        {
            resolvedKey = null;
            if (cardIndex < 0 || cardIndex >= HubArtKeys.Length) return null;

            string painting = HubArtKeys[cardIndex];
            var art = LoadSprite(painting);
            if (art != null) { resolvedKey = painting; return art; }

            string standIn = cardIndex < HubArtStandIns.Length ? HubArtStandIns[cardIndex] : null;
            art = LoadSprite(standIn);
            if (art != null) { resolvedKey = standIn; return art; }
            return null;
        }

        // ── Status medallions (256px), one per canon-7 state ──────────────────
        public const string StatusAvailable = "RpgUi/manage/status-available";
        public const string StatusLocked = "RpgUi/manage/status-locked";
        public const string StatusInProgress = "RpgUi/manage/status-inprogress";
        public const string StatusQueue = "RpgUi/manage/status-queue";
        public const string StatusMax = "RpgUi/manage/status-max";

        /// <summary>
        /// The frame a state wears. A fixed table, not a computation - and it is a
        /// switch on a STATE enum, never on an item id (the id switch is what canon 9
        /// bans and what the WO-2002 oracle looks for).
        ///
        /// <para>⚠ Only Locked and Max get their own frame. Available, InProgress and
        /// QueueBlocked all wear <see cref="FrameTile"/>, because the delivered set has
        /// no frame for them and inventing one by tinting a hollow frame would read as a
        /// different item class. Their state is carried by the MEDALLION and the state
        /// WORD instead - two channels, neither of them colour alone (the owner is
        /// red/green colourblind).</para>
        /// </summary>
        public static string FrameFor(ManageTileVisualState state)
        {
            switch (state)
            {
                case ManageTileVisualState.Locked: return FrameLocked;
                case ManageTileVisualState.Max: return FrameMax;
                default: return FrameTile;
            }
        }

        /// <summary>
        /// The status medallion a state wears. One glyph per canon-7 state.
        ///
        /// <para>⚠ There is no "unaffordable" glyph in the delivered set and none is faked.
        /// An owned item the player cannot currently afford projects to
        /// <see cref="ManageTileVisualState.Available"/> and wears
        /// <see cref="StatusAvailable"/> - the same call HeartPanel.cs:420-440 records for
        /// the Heart, under owner ruling 15. The refusal is carried by the CTA's
        /// DisabledReasonText, which is the affordance the ruling asks for.</para>
        /// </summary>
        public static string StatusFor(ManageTileVisualState state)
        {
            switch (state)
            {
                case ManageTileVisualState.Locked: return StatusLocked;
                case ManageTileVisualState.InProgress: return StatusInProgress;
                case ManageTileVisualState.QueueBlocked: return StatusQueue;
                case ManageTileVisualState.Max: return StatusMax;
                default: return StatusAvailable;
            }
        }

        // ── Perk icons: art with a NAME BAKED INTO IT ────────────────────────

        /// <summary>Resources folder holding the research perk cards.</summary>
        public const string PerkIconFolder = "HudIcons/BuildingUpgrades/";

        /// <summary>
        /// ⛔ THESE FILES ARE PORTRAIT CARDS WITH THE PERK'S NAME PAINTED INTO THE BOTTOM THIRD,
        /// AND THE ROW ALREADY TYPESETS THAT NAME. MEASURED 2026-09-07 on
        /// <c>Lumber_Mill_T1_Improved_Logging.jpg</c> (786x1177): the ornate frame runs
        /// x 65..725 and y 155..800 from the top, and everything below y~840 is the words
        /// "Improved Logging" in gold. The owner's capture
        /// (Logs/device/screens/owner-screen-20260907-010151.png) shows exactly that - a baked
        /// caption bleeding out from under each round medallion, half-cropped, beside the real
        /// TMP name that says the same thing.
        /// <para>So the medallion is drawn from the FRAMED PICTURE ONLY. The four numbers below are
        /// that measured rect in UV (u left..right, v BOTTOM-up, which is the space a RectTransform
        /// anchor works in), trimmed slightly inside the measured edges so no frame pixel of the
        /// caption band can creep in at a rounding boundary.</para>
        /// <para>⚠ THIS IS A CROP, NOT A NEW ASSET. Nothing is re-exported and no key changes; a
        /// future re-draw without the caption simply makes the crop a no-op worth re-checking.</para>
        /// </summary>
        public const float PerkIconU0 = 0.09f, PerkIconU1 = 0.91f;
        /// <summary>Bottom-up V of the framed picture: 1 - 0.67 and 1 - 0.14 of the card height.</summary>
        public const float PerkIconV0 = 0.33f, PerkIconV1 = 0.86f;

        /// <summary>
        /// True when <paramref name="resourceKey"/> names one of the captioned perk cards, so the
        /// renderer knows to crop rather than paint the whole file.
        /// <para>⛔ MODEL-SIDE ON PURPOSE. A View that tested a key's folder itself would be
        /// deriving presentation from an id, which is exactly what canon 9 bans and what the
        /// WO-2002 oracle looks for. The art authority answers questions about art.</para>
        /// </summary>
        public static bool IsCaptionedPerkIcon(string resourceKey)
            => !string.IsNullOrEmpty(resourceKey) &&
               resourceKey.StartsWith(PerkIconFolder, System.StringComparison.Ordinal);

        // ── Building portrait keys ────────────────────────────────────────────

        /// <summary>Resources folder that holds STRUCTURE portraits, one per ladder id per tier.</summary>
        public const string BuildingPortraitFolder = "Portraits/Buildings/";

        /// <summary>
        /// The Resources key for a placed building's portrait at <paramref name="level"/>.
        /// Shape: <c>Portraits/Buildings/&lt;ladderId&gt;</c> for level 1, plus a
        /// <c>-&lt;level&gt;</c> suffix from level 2 up.
        ///
        /// <para>⛔ THE FOLDER IS <c>Portraits/Buildings/</c> AND IT IS NOT INTERCHANGEABLE WITH
        /// <c>Portraits/</c>. Measured 2026-09-06 against building-tiers.json (six ladders:
        /// arcane-tower/armorer/barracks/farm/forge/lumbermill, 26 tiers between them):
        /// <c>Portraits/Buildings/</c> holds ALL 26; the <c>Portraits/</c> ROOT holds only the six
        /// level-1 legacy JPGs and is missing all TWENTY tier keys
        /// (barracks-2..6, arcane-tower-2..4, armorer-2..4, farm-2..4, forge-2..4, lumbermill-2..4).
        /// The root is also a MIXED namespace of NPC and structure art - which is precisely why
        /// <c>ManageScreenPanel.ManageBuildingPortraitGaps</c> exists and lists exactly these six
        /// ids as the ones whose root route resolves a PERSON rather than a building.</para>
        ///
        /// <para>⚠ THIS DELIBERATELY DOES NOT SLUG THE ID. <c>ManageScreenVM.ResolveBuildingPortraitKey</c>
        /// lowercases and maps <c>_</c> to <c>-</c>; <c>ManageScreenPanel.LoadManageBuildingSprite</c>,
        /// which is the shipped path that PROVES this folder works, uses the raw ladder id. Two
        /// spellings of one filename is the duplicated state CLAUDE.md 2/5/16 keeps paying for, so
        /// this method matches the loader byte-for-byte. All six live ladder ids are already
        /// lowercase-with-hyphens, so the two agree today; if a future ladder id is authored with an
        /// underscore the FILE is named with the underscore too, and nothing has to be kept in sync.</para>
        ///
        /// <para>⚠ SUPERSEDED 2026-09-07 BY THE OWNER'S SPEC (mockup panel 2). This paragraph read
        /// <i>"NO TIER-TO-BASE FALLBACK, ON PURPOSE ... quietly serving the level-1 sheet for a
        /// level-4 building is a wrong icon, and a wrong icon is a lie the capture loop cannot
        /// see."</i> The reasoning was sound and the conclusion was wrong on the player's screen:
        /// the alternative to a slightly-stale icon is a BLANK TILE, and a blank tile is the bigger
        /// lie - it says "this building has no art" rather than "this tier has no art yet".
        /// <see cref="LoadSprite"/> now retries the unsuffixed key of the same ladder and paints
        /// the base sheet.</para>
        ///
        /// <para>⛔ NOTHING IS HIDDEN BY THE FALLBACK. The miss is still announced by key
        /// (<c>art-tier-miss:</c>), and <c>ManagePortraitCoverage</c> still lists every missing
        /// tier as an ART ASK - so the owner still gets the request, and the capture loop can still
        /// see it. The fallback changes what the PLAYER sees, not what the gate reports.</para>
        /// </summary>
        public static string BuildingPortraitKey(string ladderId, int level)
        {
            if (string.IsNullOrEmpty(ladderId)) return null;
            return BuildingPortraitFolder + ladderId + (level >= 2 ? "-" + level : "");
        }

        // ── Loader ────────────────────────────────────────────────────────────

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>
        /// Loads a Manage sprite by Resources key, with the Texture2D fallback the shipped
        /// path uses (some delivered PNGs import as textures, not sprites) and a per-key
        /// cache including MISSES, so a bad key costs one lookup rather than one per frame.
        ///
        /// <para>A miss returns null and is announced ONCE per key through FlowTrace - never
        /// swallowed (CLAUDE.md 12: a catch or a fallback that does not log turns a visible
        /// defect into an invisible one).</para>
        ///
        /// <para>⚠ CORRECTED 2026-09-06 (WO-1443 section 2). This paragraph used to end "the
        /// renderer then makes the Image fully transparent rather than painting a white box",
        /// and that is NOT what the shipped path does. ManageWorkspacePanel hands the null to
        /// ElarionUiKit.Portrait, which (ElarionUiKit.cs:2274) falls through to
        /// <c>disc.sprite = CircleSprite; disc.color = PortraitPlaceholder;</c> and then always
        /// adds the medallion Ring on top. So a missing portrait renders as the warm-tan
        /// PLACEHOLDER DISC inside its ring - visible, not invisible - AND it logs. That is the
        /// better of the two behaviours and it is what actually happens; the sentence describing
        /// a transparent slot was wrong, and a wrong description of a fallback is how a seat
        /// mis-diagnoses the next blank tile.</para>
        /// </summary>
        public static Sprite LoadSprite(string resourceKey)
        {
            if (string.IsNullOrEmpty(resourceKey)) return null;
            if (Cache.TryGetValue(resourceKey, out Sprite cached)) return cached;

            Sprite art = Resources.Load<Sprite>(resourceKey);
            if (art == null)
            {
                var texture = Resources.Load<Texture2D>(resourceKey);
                if (texture != null)
                {
                    art = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), 100f);
                    art.name = texture.name + "_manage";
                }
            }
            // ⭐ TIER FALLS BACK TO THE BASE SHEET (owner spec 2026-09-07, mockup panel 2).
            // A tier key that misses now retries the UNSUFFIXED key of the same ladder before
            // giving up, so a level-3 tower whose tier sheet was never drawn paints its level-1
            // portrait instead of a blank disc. The miss is still announced, naming the exact tier
            // key that is absent, so ManagePortraitCoverage keeps listing it as an art ask.
            // ⛔ THIS DELIBERATELY REVERSES THE "NO TIER-TO-BASE FALLBACK" RULE ABOVE - see the
            // superseded note on BuildingPortraitKey for the owner's reasoning.
            string baseKey = null;
            if (art == null)
            {
                baseKey = UnsuffixedKey(resourceKey);
                if (baseKey != null) art = LoadSprite(baseKey);   // one hop only: baseKey has no suffix
            }

            if (art == null)
                // ⚠ The old text here read "the slot renders transparent rather than as a white
                // box", which the doc comment eight lines above already records as FALSE - the slot
                // renders the warm-tan PLACEHOLDER DISC inside its ring. A log line that misdescribes
                // what the player sees is how the next seat mis-diagnoses the next blank tile, so the
                // sentence now says what was measured and names the two things it could be.
                FlowTrace.Once("Manage", "art-miss:" + resourceKey,
                    "manage art unresolved at Resources/" + resourceKey +
                    " - the slot renders the placeholder disc inside its ring, NOT the real portrait." +
                    " Either the art has not been delivered (art request) or the key names a folder" +
                    " the file is not in (mis-key).");
            else if (baseKey != null)
                FlowTrace.Once("Manage", "art-tier-miss:" + resourceKey,
                    "no tier sheet at Resources/" + resourceKey + " - painting the base sheet " +
                    baseKey + " instead. The TILE IS NOT BLANK, and that is the owner's spec " +
                    "(mockup panel 2); the missing tier stays an ART ASK and " +
                    "ManagePortraitCoverage still lists it.");

            Cache[resourceKey] = art;
            return art;
        }

        /// <summary>
        /// The same key with a trailing "-&lt;digits&gt;" tier suffix removed, or null when the key
        /// carries no suffix (so a base-sheet miss cannot recurse).
        /// <para>The suffix grammar is <see cref="BuildingPortraitKey"/>'s own and nothing else's -
        /// this reverses that method, it does not invent a second spelling.</para>
        /// </summary>
        private static string UnsuffixedKey(string resourceKey)
        {
            if (string.IsNullOrEmpty(resourceKey)) return null;
            int dash = resourceKey.LastIndexOf('-');
            if (dash <= 0 || dash == resourceKey.Length - 1) return null;
            for (int i = dash + 1; i < resourceKey.Length; i++)
                if (resourceKey[i] < '0' || resourceKey[i] > '9') return null;
            return resourceKey.Substring(0, dash);
        }

        /// <summary>Editor/test hook: drops the cache so a re-import is seen. No production caller.</summary>
        public static void ClearCache() { Cache.Clear(); }
    }
}
