// =============================================================================
// api/_lib/tunable-manifest.js - WO-1328. The JSON that DRIVES the Command
// Center balance editor.
// -----------------------------------------------------------------------------
// Owner ruling 2026-09-02, verbatim:
//   "and think about it, should be in command center so you dont need to be a
//    rocket scientist. a area for skills, and tiers of skills or spells or
//    almost anything (misc) and they can have a simple UI that rives a json"
// said in the same breath as: "i have been screaming this for months."
//
// Read the second sentence as the requirement. The point is not "add a page".
// The point is that changing one balance number must stop costing a specialist:
// today it is either a thirty-minute rebuild or a PowerShell command with an
// exact key name, and the only person who can judge feel is on a phone.
//
// -----------------------------------------------------------------------------
// ⛔ THIS FILE IS NOT A COPY OF THE KNOB LIST. IT IS A JOIN.
// -----------------------------------------------------------------------------
// Three facts about a knob, three owners, and none of them written twice:
//
//   key + kind + default   ->  DERIVED from DeNelle.Core.Ops.RemoteTunables.Registry
//                              by tools/gen-tunable-manifest.mjs into
//                              ./tunable-manifest.generated.json. The build is the
//                              only place a default may live (api/_lib/tunables.js
//                              says so in capitals, and means it).
//
//   "may this key be written at all"
//                          ->  the TUNABLE_KEYS ALLOWLIST in ./tunables.js, which
//                              is a spell-check at write time and never a source
//                              of truth.
//
//   area + label + plain English + safe range
//                          ->  PRESENTATION below. Hand-authored, and genuinely
//                              NEW information: nowhere else in this repo says
//                              which of the owner's four areas a knob belongs to
//                              or what it reads like to a human on a phone.
//
// build() joins the three and REFUSES to invent. A key present in one source and
// absent from another is reported by mismatches() as a defect that NAMES THE TWO
// SOURCES - it is never papered over with a default, because a manifest that
// quietly drops a lever is a lever the owner cannot find and will not know to
// look for. test/tunables-manifest.test.js drives that oracle and is the thing
// that goes red.
//
// -----------------------------------------------------------------------------
// ⛔ SERVER-AUTHORITATIVE VALUES ARE PERMANENTLY OUT OF SCOPE.
// -----------------------------------------------------------------------------
// Prices, entitlements, grants and purchase amounts (api/_lib/purchase-catalog.js)
// must NEVER appear in this manifest or on the page it drives. The game takes real
// money on mainnet; a client-side override of a price is an exploit, not a feature.
// This is stated here, in the manifest itself, AND printed on the page, so that a
// future seat adding "just one more knob" has to walk past the rule twice.
//
// The rail this rides is described end to end in docs/PROD022_TUNABLE_FLAGS.md.
//
// CommonJS, no dependencies. Files under api/_lib/ are NOT routed by Vercel
// (leading underscore), so this is a library, never an endpoint. ASCII only:
// every string here is rendered into the served console page, which
// test/command-center.test.js pins as 7-bit ASCII end to end.
// =============================================================================

const generated = require('./tunable-manifest.generated.json');
const { TUNABLE_KEYS } = require('./tunables');
const vfxPickOptions = require('./vfx-pick-options.generated.json');

// -----------------------------------------------------------------------------
// WO-1348 - THE VFX PICK OPTION POOL, and why it is required rather than typed.
// -----------------------------------------------------------------------------
// A realm.vfx.* knob is not an ordinary number: its value is a STABLE OPTION ID
// naming an effect the build already carries. The page has to render those ids as
// NAMES - "Aura_Nature", not "13" - or the owner is back to being a rocket
// scientist, which is the exact sentence this whole surface exists to answer.
//
// The pool is GENERATED from her own tag file by tools/gen-vfx-pick-options.mjs
// into two byte-identical copies, one here and one in Assets/Resources/VFX/, and
// test/vfx-pick-options.test.js pins them together. Hand-typing the option count
// into the safe range below would have been a sixth copy of the very thing this
// file's header refuses to copy - so max is COMPUTED from the pool and the range
// simply grows the day she tags another effect.
// -----------------------------------------------------------------------------

/**
 * OFFERABLE options, in id order. Two kinds are held back and neither id is ever reused:
 *   retired    - the key left the owner's tag file.
 *   unresolved - the key is still tagged but THIS BUILD's HovlVfxCatalog cannot resolve it
 *                (a gitignored art pack that was not imported when the catalog was baked).
 * Offering an unresolved option would let her pick an effect that renders nothing with no
 * error on screen - CLAUDE.md section 16 - so it is held back rather than shown greyed.
 */
const VFX_PICK_OPTIONS = (Array.isArray(vfxPickOptions.options) ? vfxPickOptions.options : [])
    .filter((o) => o && Number.isInteger(o.id) && o.id > 0 && !o.retired && !o.unresolved)
    .map((o) => ({ id: o.id, key: o.key, prefab: o.prefab, isLoop: o.isLoop === true }))
    .sort((a, b) => a.id - b.id);

/** Highest id the pool can ever hand out, INCLUDING reserved retired ids. */
const VFX_PICK_MAX_ID = (Array.isArray(vfxPickOptions.options) ? vfxPickOptions.options : [])
    .reduce((max, o) => (o && Number.isInteger(o.id) && o.id > max ? o.id : max), 0);

/** Said under every VFX pick card, in words, because the owner is red/green colourblind. */
const VFX_PICK_TIMING_NOTE =
    'Takes effect on the NEXT TOWN LOAD - leave town and come back, or restart the game. ' +
    'Nothing already on screen changes. Setting it back to 0 restores the effect the game ships with.';

/**
 * One VFX pick card. Every one of them shares the same control, the same pool and
 * the same timing sentence - only the label and the "what" differ, so writing them
 * out four times would have been four chances to drift.
 */
function vfxPick(label, what, risk) {
    return {
        area: 'spells',
        label: label,
        what: what + ' ' + VFX_PICK_TIMING_NOTE,
        min: 0,
        max: VFX_PICK_MAX_ID,
        risk: risk,
        options: VFX_PICK_OPTIONS,
        optionsNote:
            'You are choosing from effects this build ALREADY carries. Adding a new effect to ' +
            'this list is still a rebuild - this changes WHICH shipped effect a thing uses, ' +
            'never WHICH effects exist. If a pick cannot be used (it is missing from this ' +
            'build, or it loops when the slot needs a one-shot), the game keeps the shipped ' +
            'effect and writes the reason into the log rather than showing nothing.',
    };
}

/**
 * The owner's four areas, in the order she named them, and what each one is FOR.
 * An area with no knobs still renders - it is how the page says "this lever does
 * not exist yet" instead of silently having no opinion.
 */
const AREAS = [
    {
        id: 'skills',
        title: 'Skills',
        blurb: 'How strong a hero ability is, and how fast it comes back.',
    },
    {
        id: 'tiers',
        title: 'Tiers',
        blurb: 'How much a skill or a structure gains per level or rank.',
    },
    {
        id: 'spells',
        title: 'Spells',
        blurb: 'What a cast does and how it looks: damage, healing, drain, and spell visuals.',
    },
    {
        id: 'misc',
        title: 'Misc',
        blurb: 'Everything else that ships as a number. Two very different kinds live ' +
               'here: the RAID REWARD table, which is an ordinary balance curve you are ' +
               'meant to move by feel, and the loading/retry/trace levers, which you ' +
               'should only touch when chasing a bug. Each card says which it is.',
    },
];

/**
 * HAND-AUTHORED PRESENTATION, keyed by knob key.
 *
 *   area     one of AREAS[].id
 *   label    what the owner reads. Never the key.
 *   what     plain English. What moving it actually does, and what the shipped
 *            value means, in a sentence a person can act on.
 *   min/max  the SAFE range this surface will submit. Not a security boundary -
 *            api/_lib/tunables.js validates independently and the client clamps
 *            again - it is a guard rail against a fat thumb on a phone.
 *   risk     one short sentence shown under the control when the knob is not an
 *            ordinary balance dial. Optional.
 *
 * ⭐ ADDING A LEVER LATER IS A DATA EDIT, NOT A UI EDIT: add the knob to
 * RemoteTunables.Registry and to TUNABLE_KEYS, re-run
 * node tools/gen-tunable-manifest.mjs, and add one entry here. The page grows a
 * card on its own. That is what "a simple UI that rives a json" buys.
 */
const PRESENTATION = {
    'combat.drainReturnPct': {
        area: 'spells',
        label: 'Drain healing return',
        what: 'How much of the damage a drain spell deals comes back to the caster as ' +
              'healing, as a percent. The game ships at 60, which you chose so that drain ' +
              'helps stave off rather than run the show - a player using it well survives a ' +
              'fight they would have lost, but cannot stand still and win by attrition. 100 ' +
              'means the caster heals for exactly the damage dealt, which is how it used to ' +
              'behave. Lower it further to make the mage squishier. This covers EVERY drain: ' +
              'Syphon Essence, the mage Drain, and the ranger Healing Shot.',
        min: 0,
        max: 1000,
    },

    // ---- WO-1330: the three levers of the ONE over-time engine. Every
    // damage-over-time and every heal-over-time effect in the game reads these, so
    // the copy talks about "all of them" rather than naming one spell. The tick
    // knob is described as a BEAT you can count, never as a look - rhythm is
    // carrying signal here that colour cannot.
    'combat.overTimeTickMs': {
        area: 'spells',
        label: 'Over-time pulse beat',
        what: 'How often an effect that works over time lands one pulse, in ' +
              'milliseconds. The game ships at 1000 - one pulse a second, a row of ' +
              'separate thuds you can count. 250 turns the same effect into a fast ' +
              'continuous drain; 2000 into slow heavy hits. It does NOT change how much ' +
              'is dealt or healed in total: a faster beat always means smaller pulses. ' +
              'This is purely how the effect READS. Covers every burn, every poison and ' +
              'every regen at once.',
        min: 50,
        max: 60000,
    },

    'combat.overTimeMagnitudePct': {
        area: 'spells',
        label: 'Over-time strength',
        what: 'Scales how hard each over-time pulse hits or heals, as a percent of the ' +
              'numbers written in the ability files. The game ships at 100, meaning ' +
              'exactly what is authored. 50 halves every damage-over-time AND every regen ' +
              'at the same time; 0 makes them do nothing without deleting anything. Use it ' +
              'to ask whether over-time effects as a whole are pulling their weight ' +
              'against straight burst damage.',
        min: 0,
        max: 1000,
    },

    'combat.overTimeDurationPct': {
        area: 'spells',
        label: 'Over-time duration',
        what: 'Scales how long an over-time effect lasts, as a percent of the seconds ' +
              'written in the ability files. The game ships at 100. Raising it adds ' +
              'pulses, so unlike the strength dial it changes the TOTAL amount dealt or ' +
              'healed. Kept separate from strength on purpose: "each pulse hurts more" ' +
              'and "it lasts longer" feel completely different even when the totals match, ' +
              'and one dial could not tell you which you preferred.',
        min: 0,
        max: 1000,
    },

    // WO-1327 landed these two while WO-1328 was being built, and the oracle in
    // test/tunables-manifest.test.js went red naming them within the minute -
    // which is the entire argument for deriving the spine instead of typing it.
    'vfx.particleBouncePct': {
        area: 'spells',
        label: 'Spell particles: bounce off the world',
        what: 'How bouncy a spell particle is when it hits the ground or a wall, as a ' +
              'percent. The game ships at 0, meaning a particle stops where it lands ' +
              'instead of ricocheting back at the caster. 100 leaves the art pack exactly ' +
              'as its artist authored it. Anything between tightens it part way; it can ' +
              'never make an effect bouncier than the artist made it.',
        min: 0,
        max: 100,
    },
    'vfx.maxParticleLights': {
        area: 'spells',
        label: 'Spell particles: real lights per effect',
        what: 'How many real-time lights one spell effect may light the scene with at ' +
              'once. The game ships at 4. The big fire spell was authored with 25, which ' +
              'is what a phone struggles with. 0 turns particle lights off completely.',
        min: 0,
        max: 25,
        risk: 'This is a phone performance dial as much as a look. Raising it makes ' +
              'spells prettier and the frame rate worse.',
    },

    // ---- WO-1343. The Night Store's aura. These four exist so that a choice YOU
    // said you were not ready to make does not cost a rebuild each time you look at
    // it: "i added another option for REalm store, not sure which will be best",
    // and "can we use these slowly one after another instead ... if the other one
    // doesnt look good". All the candidates are in the build; this is where you pick.
    'vfx.nightStoreAuraMode': {
        area: 'spells',
        label: 'Night Store: which aura',
        what: 'Which effect the Realm Store wears. 0 is the one the game ships with: the ' +
              'blue starfall you tagged first. 1 is the loot flicker you added as a second ' +
              'option. 2 cycles slowly through the seven Aura spell effects, one at a time. ' +
              '3 puts back the plain ground ring the store used to have, if you want the ' +
              'old look again. Change it, walk up to the store, and look - nothing needs ' +
              'rebuilding.',
        min: 0,
        max: 3,
        risk: 'Options 0 and 1 are single bursts that fire on the timer below. Option 2 is ' +
              'a set of continuous glows, so the timer changes WHICH one is showing rather ' +
              'than re-firing it. Option 3 never changes at all.',
    },

    'vfx.nightStoreAuraCadenceMin': {
        area: 'spells',
        label: 'Night Store: minutes between changes',
        what: 'How many minutes pass between the store aura firing again, or switching to ' +
              'the next one if you are cycling. The game ships at 30, which is the number ' +
              'you asked for. Only counts while you are in town - it never runs during a ' +
              'raid, a battle or a dungeon.',
        min: 1,
        max: 1440,
    },

    'vfx.nightStoreAuraFamilyMask': {
        area: 'spells',
        label: 'Night Store: which Aura effects may appear',
        what: 'Only matters when the aura choice above is set to 2 (cycle). Add up the ones ' +
              'you want and enter the total: Arcane 1, Dark 2, Fire 4, Ice 8, Light 16, ' +
              'Nature 32, Storm 64. The game ships at 127, which is all seven. Drop one you ' +
              'do not like by leaving its number out - for example 127 minus 4 is 123, ' +
              'which is everything except Fire.',
        min: 0,
        max: 127,
        risk: 'An Aura effect only appears once you have tagged it in the VFX Caster. Any ' +
              'that you have not tagged are skipped by name in the log, never swapped for ' +
              'something else, and the store falls back to the aura you tagged.',
    },

    'vfx.nightStoreAuraBurstRepeatSec': {
        area: 'spells',
        label: 'Night Store: extra pulses between changes',
        what: 'Seconds between extra repeats of the burst inside one waiting period. The ' +
              'game ships at 0, which means it fires once and then waits the full time ' +
              'again - exactly what you asked for. If one flash every half hour turns out ' +
              'to read as nothing happening, set this to a few seconds to make it a slow ' +
              'heartbeat instead. Ignored when you are cycling the Aura effects, since ' +
              'those glow continuously.',
        min: 0,
        max: 600,
    },

    // ---- WO-1374. THE RAID REWARD TABLE. These seven are ordinary balance dials,
    // not bug-hunting levers, and they are the ones the north-star map expects to be
    // moved by feel until a raid feels worth doing. They sit under Misc only because
    // the four areas on this page are the four the owner named and none of them is
    // "economy"; every card below says in its first sentence that it is a reward
    // number, so it never reads as something you should leave alone.
    'raid.lootWoodBase': {
        area: 'misc',
        label: 'Raid reward: wood for a perfect run',
        what: 'How much WOOD a raid pays for a perfect result - three stars AND the camp ' +
              'razed to nothing - on a first-tier camp. The game ships at 1800, which is ' +
              'roughly two thirds of what four hours of your lumber camps produce, so a ' +
              'raid is worth doing without making the collectors pointless. Anything less ' +
              'than perfect pays a share of this, set by the five result dials below. ' +
              'Harder camps multiply it again on top. Set it to 0 and raids stop paying ' +
              'wood entirely, which is how the game behaved before.',
        min: 0,
        max: 1000000,
    },
    'raid.lootIronBase': {
        area: 'misc',
        label: 'Raid reward: iron for a perfect run',
        what: 'The same as the wood dial above, for IRON. The game ships at 1100. It is a ' +
              'separate number on purpose: wood and iron run out at different points in ' +
              'the build tree, so which one a raid should favour is a real question and ' +
              'this is where you answer it.',
        min: 0,
        max: 1000000,
    },
    'raid.lootFailPct': {
        area: 'misc',
        label: 'Raid reward: share paid for a LOSS',
        what: 'What percentage of the wood and iron above a FAILED attack still pays. The ' +
              'game ships at 18. It is deliberately not zero - a loss that pays nothing ' +
              'makes a failed raid feel like twenty wasted minutes, and the player stops ' +
              'trying. Raise it if losing feels too punishing; lower it if losing feels ' +
              'like no loss at all.',
        min: 0,
        max: 1000,
        risk: 'This is the dial that decides whether a wipe reads as "go again" or as ' +
              '"never again". Worth changing in small steps.',
    },
    'raid.lootOneStarPct': {
        area: 'misc',
        label: 'Raid reward: share paid at 1 star',
        what: 'What percentage of the wood and iron a ONE-STAR raid pays. The game ships ' +
              'at 50 - half. These four star dials are the ladder that makes getting ' +
              'better at raiding actually pay more.',
        min: 0,
        max: 1000,
    },
    'raid.lootTwoStarPct': {
        area: 'misc',
        label: 'Raid reward: share paid at 2 stars',
        what: 'What percentage of the wood and iron a TWO-STAR raid pays. The game ships ' +
              'at 75. The gap between this and the three-star dial is what decides whether ' +
              'a player pushes for the full clear or settles for a safe two and leaves.',
        min: 0,
        max: 1000,
    },
    'raid.lootThreeStarPct': {
        area: 'misc',
        label: 'Raid reward: share paid at 3 stars',
        what: 'What percentage of the wood and iron a THREE-STAR raid pays. The game ships ' +
              'at 100, meaning three stars pays exactly the numbers in the two dials at the ' +
              'top. Moving this re-scales every raid payout at once without touching those ' +
              'numbers - the quick way to ask "is raiding paying a bit too much overall".',
        min: 0,
        max: 1000,
    },
    'raid.lootPerfectPct': {
        area: 'misc',
        label: 'Raid reward: share paid at 3 stars and 100 percent',
        what: 'What percentage the very best result pays - three stars AND every building ' +
              'in the camp destroyed. The game ships at 110, the only result that pays more ' +
              'than the base. It is the reward for mastery rather than just winning. If the ' +
              'extra ten percent is not worth the extra minutes of mopping up, this is where ' +
              'you say so.',
        min: 0,
        max: 1000,
    },

    'raid.lootCoinsBaseCamp1': {
        area: 'misc',
        label: 'Raid reward: gold for a perfect run (Camp I)',
        what: 'How much GOLD a raid pays for a perfect result - three stars AND the camp ' +
              'razed to nothing - on the FIRST camp. The game ships at 2200. Gold buys ' +
              'troops, troops win raids, raids pay gold: this is the dial that closes that ' +
              'loop. It is sized so that one clean win pays for the squad you spent plus ' +
              'about 550 over, which is what lets a player raid again straight away. ' +
              'Anything less than perfect pays a share of it, set by the five result dials ' +
              'above. It is also what any camp the game does not recognise pays. Set it to ' +
              '0 and raids stop paying gold, which is how the game behaved before.',
        min: 0,
        max: 1000000,
        risk: 'Harder camps do NOT multiply this - they have their own three dials below. ' +
              'Changing this one changes the first camp and nothing else.',
    },
    'raid.lootCoinsBaseCamp2': {
        area: 'misc',
        label: 'Raid reward: gold for a perfect run (Camp II)',
        what: 'The same as the Camp I gold dial, for the SECOND camp. The game ships at ' +
              '3100, sized against the bigger squad that camp expects you to bring. It is ' +
              'its own number rather than a multiple of Camp I because each camp has a ' +
              'designed army cost, and the steps between them are what make unlocking a ' +
              'harder raid feel like progress.',
        min: 0,
        max: 1000000,
    },
    'raid.lootCoinsBaseCamp3': {
        area: 'misc',
        label: 'Raid reward: gold for a perfect run (Camp III)',
        what: 'The same again for the THIRD camp. The game ships at 4500. The reward is ' +
              'sized against the army the player is EXPECTED to bring, never the one they ' +
              'actually brought - otherwise attacking with nothing would be the best way ' +
              'to make money.',
        min: 0,
        max: 1000000,
    },
    'raid.lootCoinsBaseBastion': {
        area: 'misc',
        label: 'Raid reward: gold for a perfect run (Iron Bastion)',
        what: 'The same again for the Iron Bastion, the fourth and endless target. The ' +
              'game ships at 6500. This dial does nothing yet - the Bastion map is built ' +
              'but is not switched on in the game. It is here so the number is already a ' +
              'dial on the day it is.',
        min: 0,
        max: 1000000,
    },
    'raid.lootCrystalsBase': {
        area: 'misc',
        label: 'Raid reward: crystals for a perfect run',
        what: 'How many CRYSTALS a raid pays when the camp is razed to nothing, before the ' +
              'per-star bonus below. The game ships at 20, so a perfect clear pays 26 - ' +
              'down from the 55 it used to pay. This is the one raid reward that was ' +
              'lowered on purpose: crystals skip build timers, so paying a lot of them out ' +
              'of raids quietly makes the whole build tree shorter. Harder camps do not ' +
              'multiply this either, for the same reason.',
        min: 0,
        max: 1000000,
        risk: 'This dial sets how long the entire game takes to build through. Raising it ' +
              'shortens every timer in the game by the back door.',
    },
    'raid.lootCrystalsPerStar': {
        area: 'misc',
        label: 'Raid reward: extra crystals per star',
        what: 'Extra CRYSTALS on top of the dial above, per star earned. The game ships at ' +
              '2, down from 10. Kept separate so you can decide whether a great raid should ' +
              'pay more crystals or just more gold.',
        min: 0,
        max: 1000000,
    },
    'raid.lootRepeatClearPct': {
        area: 'misc',
        label: 'Raid reward: share paid for a REPEAT clear',
        what: 'What percentage of the wood, iron, stone and gold a raid pays when you clear ' +
              'the SAME camp again before its cooldown has run out. The game ships at 60, ' +
              'which is your ruling: full pay for the first clear after a cooldown, 60 ' +
              'percent for a repeat inside the same cycle, back to full once the cooldown ' +
              'expires. Crystals are not affected - those are once a day whatever you do. ' +
              'Set it to 100 to remove the repeat penalty altogether. It can never go above ' +
              '100, so a repeat can never pay more than a first clear.',
        min: 0,
        max: 100,
        risk: 'This is the number a player meets as "I won and got almost nothing". It sets ' +
              'whether re-running a camp reads as useful practice or as a waste of a raid.',
    },
    'raid.cacheCapPerResource': {
        area: 'misc',
        label: 'Raid Cache: how much overflow it holds per resource',
        what: 'When you win a raid and your storage is full, the leftover is held in a Raid ' +
              'Cache instead of being thrown away, and you claim it once you have upgraded ' +
              'or spent. This is how much the cache will hold of any ONE resource. The game ' +
              'ships at 1800 - one perfect first-tier raid\'s worth of wood - so the cache ' +
              'can never quietly turn into a second, bigger warehouse. Only wood, iron and ' +
              'stone go in it; crystals and gold have no storage limit in the first place. ' +
              'Set it to 0 and overflow is thrown away again, the way it used to be.',
        min: 0,
        max: 1000000,
        risk: 'The size was never specified - you asked for "a modest cap" and this is one ' +
              'raid\'s worth. Too small and a win still mostly evaporates; too large and the ' +
              'cache removes the reason to upgrade storage at all.',
    },

    'raid.starterArmySize': {
        area: 'misc',
        label: 'Free starter squad size',
        what: 'How many Footmen a new player is given FREE the first time they have a ' +
              'Barracks. The game ships at 3 - exactly what 1650 gold used to buy, which ' +
              'was the wall standing between a new player and their first raid. It is ' +
              'granted once per save and never again, so knocking a Barracks down and ' +
              'rebuilding it does not hand out more troops. Set it to 0 to turn the free ' +
              'squad off entirely.',
        min: 0,
        max: 10,
        risk: 'This is the first ten minutes of the game. Raising it makes the first raid ' +
              'easier to win; lowering it puts the wall back.',
    },

    'raid.heartfireMaxCharges': {
        area: 'misc',
        label: 'Heartfire: how many marches can be banked',
        what: 'Heartfire is what the Heart spends to send you beyond its reach. One is used ' +
              'per march. They build up while you are away and the game ships holding 3, so ' +
              'a night asleep or a day at work leaves you with a full evening waiting rather ' +
              'than one attack. Raise it to let a returning player do more in one sitting; ' +
              'lower it to spread play across the day. It is not money - it cannot be bought, ' +
              'sold or found, only waited for.',
        min: 1,
        max: 9,
        risk: 'This is the shape of a session. Raising it lets a player clear everything at ' +
              'once and then find nothing to do; lowering it can send them away with a ' +
              'charge they cannot use.',
    },

    'raid.heartfireRegenSeconds': {
        area: 'misc',
        label: 'Heartfire: seconds to get one march back',
        what: 'How long one Heartfire takes to come back. The game ships at 14400 - four ' +
              'hours - which is the same wait as the quickest camp takes to recover, so ' +
              'there is always somewhere to spend a fresh one.',
        min: 60,
        max: 86400,
        risk: 'Do not raise this past the quickest camp cooldown (four hours) without ' +
              'raising that too, or a player will be holding a march with nowhere to send ' +
              'it. Lowering it makes raiding more frequent everywhere at once.',
    },

    'economy.packTemporaryBuilderSeconds': {
        area: 'misc',
        label: "Builder's Hour: seconds the extra crew lasts",
        what: 'How long the extra builder crew from the $1.99 Builder\'s Hour pack keeps ' +
              'working. The game ships at 21600 - six hours. Buying another while one is ' +
              'running queues it to start when the first one ends; nothing is lost.',
        min: 0,
        max: 604800,
        risk: 'Raising it makes the cheapest pack worth more than the $9.99 permanent ' +
              'builder for a while, which is the wrong order. Lowering it below an hour ' +
              'makes the pack feel like nothing happened. 0 refuses the grant and keeps ' +
              'the charge queued.',
    },

    // ---- WO-1384b: the Night Market card's glow. Three feel knobs on the HUD's
    // permanent store face. Filed under Misc because the manifest's four areas
    // are pinned by test/tunables-manifest.test.js ('the four areas are the ones
    // the owner named') - a fifth 'hud' area is a ruling, not a knob.
    'hud.nightMarketGlowLapSec': {
        area: 'misc',
        label: 'Night Market card: seconds per lap of the glow',
        what: 'The store card on the HUD has a soft ring and three small lights that ' +
              'chase round its edge. This is how many seconds one trip round takes. The ' +
              'game ships at 5. Lower is busier, higher is calmer. Takes effect the next ' +
              'time the HUD is built.',
        min: 1,
        max: 60,
        risk: 'A very short lap reads as flicker on a phone and draws the eye away from ' +
              'the action bar; a very long one looks like the glow has stopped.',
    },
    'hud.nightMarketGlowAlphaPct': {
        area: 'misc',
        label: 'Night Market card: how bright the glow is (percent)',
        what: 'How strong the ring and the chasing lights are, from 0 (off) to 100 (solid). ' +
              'The game ships at 35 - a rim light, not a spotlight. Takes effect the next ' +
              'time the HUD is built.',
        min: 0,
        max: 100,
        risk: 'Above about 60 the card shouts over the Heart plate above it. 0 keeps the ' +
              'card and removes the glow entirely, which makes the store easier to miss.',
    },
    'hud.nightMarketGlowPaletteMask': {
        area: 'misc',
        label: 'Night Market card: which colours the glow cycles through',
        what: 'Add up the colours you want: Gold = 1, Amber = 2, Rose = 4. The game ships ' +
              'at 7 (all three, gold then amber then rose). 1 alone holds a steady gold. ' +
              '0 is treated as gold on its own, never as nothing. Takes effect the next ' +
              'time the HUD is built.',
        min: 0,
        max: 7,
        risk: 'Rose alone (4) can read as a warning tint next to the health bars. Any ' +
              'value above 7 is clamped back to 7.',
    },

    // ---- WO-1366: the Arena wager. Four price knobs. Filed under Misc for the
    // same reason as the glow knobs: the four areas are pinned by the test.
    'arena.wagerTier1': {
        area: 'misc',
        label: 'Arena: Crystals staked against the first opponent',
        what: 'How many Crystals a player puts down to fight the Ironhold Marauders, the ' +
              'easiest Arena opponent. The game ships at 50. Lose and the stake is gone; ' +
              'win and the purse below pays out.',
        min: 1,
        max: 100000,
        risk: 'Crystals are bought with real money, so this is a price. Raising it makes ' +
              'the first fight a real bet; lowering it makes the Arena a free spin. It ' +
              'cannot go to 0 - the game clamps it to at least 1.',
    },
    'arena.wagerTier2': {
        area: 'misc',
        label: 'Arena: Crystals staked against the second opponent',
        what: 'The stake to fight the Grimwatch Reavers, the middle Arena opponent. The ' +
              'game ships at 100.',
        min: 1,
        max: 100000,
        risk: 'The gap between this and the first tier is what decides whether the next ' +
              'fight feels like a step up or a wall. Keep it above tier 1.',
    },
    'arena.wagerTier3': {
        area: 'misc',
        label: 'Arena: Crystals staked against the third opponent',
        what: 'The stake to fight the Blackbanner Host, the hardest Arena opponent. The ' +
              'game ships at 200.',
        min: 1,
        max: 100000,
        risk: 'This is the one stake a player probably had to buy Crystals to afford. ' +
              'Raising it is asking for a bigger purchase; keep it above tier 2.',
    },
    'arena.winPursePct': {
        area: 'misc',
        label: 'Arena: what a win pays, as a percent of the stake',
        what: 'How much a winner gets back, measured against what they staked. The game ' +
              'ships at 200 - the stake back plus the same again. 100 hands back only the ' +
              'stake, so a win pays nothing. 300 pays the stake plus twice over.',
        min: 100,
        max: 1000,
        risk: 'Below 100 a win would lose money, so the game refuses it. Well above 200 the ' +
              'Arena starts printing Crystals, which undercuts the store.',
    },

    'hero.playableFallbackHalf': {
        area: 'misc',
        label: 'Hero: invisible wall distance in maps with no ground data',
        what: 'How far, in metres, the hero may wander from the centre of a map before ' +
              'the game pulls them back - and ONLY in maps the game cannot measure for ' +
              'itself. The home overworld measures its own size, so this number is never ' +
              'used there. Raid bases and the raid village cannot be measured, so this is ' +
              'their wall. The game ships at 50, which is exactly what it used before.',
        min: 1,
        max: 100000,
        risk: 'Too small and the hero snaps back inside a raid base they should be able ' +
              'to cross. Too large and a hero who walks off the map keeps going instead ' +
              'of being caught. Takes effect the next time a map loads.',
    },

    'raid.stagingCeilingSeconds': {
        area: 'misc',
        label: 'Raid: how long a player may sit in staging before the game sends them home',
        what: 'Staging is the screen where troops are placed before an assault begins. It ' +
              'costs nothing on the raid clock, so this is only meant to catch a session ' +
              'that has actually died - a phone put down and forgotten. The game ships at ' +
              '900 seconds, which is fifteen minutes.',
        min: 60,
        max: 86400,
        risk: 'Too short and a player who takes their time placing troops gets thrown out ' +
              'of a raid they were still setting up. Too long and a dead session sits there ' +
              'holding the raid instead of being cleaned up.',
    },

    'raid.honorThirdStarSeconds': {
        area: 'misc',
        label: 'Raid: seconds before the third star goes dark',
        what: 'A raid starts with three stars lit and puts them out as time passes. This ' +
              'is how many seconds of fighting a player gets before the THIRD star goes ' +
              'dark. The timer only starts when the fight starts, so setting up costs ' +
              'nothing. The game ships at 90 seconds, which is half the raid clock. It ' +
              'changes the reward too: a raid can never pay more stars than the bar was ' +
              'still showing at the end.',
        min: 1,
        max: 86400,
        risk: 'Too small and almost every raid loses its third star, so the best reward ' +
              'stops feeling reachable. Too large and speed stops mattering at all. ' +
              'Takes effect on the next raid.',
    },

    'raid.honorSecondStarSeconds': {
        area: 'misc',
        label: 'Raid: seconds before the second star can go dark',
        what: 'How many seconds of fighting before the SECOND star is at risk. It only ' +
              'goes dark if the camp is still less than half wrecked by then, so a player ' +
              'who is making progress keeps it. The game ships at 150 seconds - the last ' +
              'thirty before the clock runs out. The FIRST star is never taken away for ' +
              'time alone, whatever this is set to.',
        min: 1,
        max: 86400,
        risk: 'Too small and a slow raid is punished twice over. Set it above the raid ' +
              'clock and this milestone simply never happens. Takes effect on the next raid.',
    },

    'raid.honorSecondStarMinDestructionPct': {
        area: 'misc',
        label: 'Raid: how wrecked a camp must be to keep the second star',
        what: 'The percentage of a camp that has to be destroyed by the time above for ' +
              'the second star to stay lit. The game ships at 50, meaning half the camp. ' +
              '0 means the second star is never taken away; 100 means only a total ' +
              'flattening keeps it.',
        min: 0,
        max: 100,
        risk: 'Too high and players who fought well still lose the star, which reads as ' +
              'the game cheating. Too low and the milestone stops asking for anything. ' +
              'Takes effect on the next raid.',
    },

    'raid.roughStoneMinTier': {
        area: 'misc',
        label: 'Rough stone: lowest raid camp that can drop one',
        what: 'Raid camps sit on a ladder: 1 the Forsaken Camp, 2 the Broken Garrison, ' +
              '3 the Veiled Enclave, 4 the Iron Bastion. This is the lowest rung that ' +
              'is allowed to pay a rough stone. The game ships at 3, which is your ' +
              'ruling that only the top two tiers may drop it. Set it to 2 to let the ' +
              'Broken Garrison drop as well. Set it to 5 and no raid drops stone at all.',
        min: 1,
        max: 99,
        risk: 'Rough stone is the only thing the Jeweler chain eats, so this decides ' +
              'which raids feel like they lead somewhere. Lowering it makes rings much ' +
              'faster to reach. Takes effect on the next raid you win.',
    },
    'raid.roughStonePerDayCap': {
        area: 'misc',
        label: 'Rough stone: how many raids may pay one per day',
        what: 'How many rough stones ALL your raids together can pay inside one day ' +
              '(the day rolls over at UTC midnight). The game ships at 1, which is your ' +
              'ruling of no more than one a day. It is a shared total, not one per ' +
              'camp - winning both top camps on the same day still pays one stone. ' +
              'Set it to 0 to stop raids dropping stone without changing the tier above.',
        min: 0,
        max: 99,
        risk: 'This is the tap on the whole ring ladder. Raise it and the rarest rings ' +
              'stop being a long climb; drop it to 0 and dungeons become the only ' +
              'source again. Takes effect on the next raid you win.',
    },
    'dungeon.roughStoneDropPct': {
        area: 'misc',
        label: 'Rough stone: chance a dungeon run pays one',
        what: 'The percentage chance that finishing a dungeon pays a rough stone, once ' +
              'you already own your first one. The game ships at 5, which is your ' +
              'ruling. Starter dungeons never pay it whatever this is set to, and your ' +
              'very first stone is still guaranteed and cannot be rolled away. The ' +
              'build before this change used 15, so setting it back to 15 restores the ' +
              'old rate exactly.',
        min: 0,
        max: 100,
        risk: 'Dungeons were the only source of rough stone before raids could drop it. ' +
              'Set this too low and a delve stops paying for the lantern oil; too high ' +
              'and the Jeweler stops being a climb. Takes effect on the next run you finish.',
    },

    // ---- the PROD-022 loading knobs. Not balance. They live under Misc because
    // they are numbers that ship, and the owner asked for "almost anything (misc)",
    // but every one of them says out loud that it is a bug-hunting lever.
    'pi.eagerStructureWarm': {
        area: 'misc',
        label: 'Pi Browser: load all building art up front',
        what: 'ON makes the Pi Browser build load and keep every building model at ' +
              'startup instead of fetching each one when it is first needed. Slower ' +
              'start, fewer fetches later. Ships OFF.',
        min: 0,
        max: 1,
        risk: 'Bug-hunting lever for the Pi Browser crash loop. Takes effect on the ' +
              'next launch of the app, not immediately.',
    },
    'pi.awaitInitBeforeFirstLoad': {
        area: 'misc',
        label: 'Pi Browser: wait for the asset system before the first request',
        what: 'ON makes the Pi Browser build finish setting up its asset system before ' +
              'it asks for the first model, queueing anything asked for meanwhile. ' +
              'Ships OFF.',
        min: 0,
        max: 1,
        risk: 'Bug-hunting lever for the Pi Browser crash loop. Takes effect on the ' +
              'next launch of the app, not immediately.',
    },
    'pi.disableRemoteStructureArt': {
        area: 'misc',
        label: 'Pi Browser: stop downloading building art entirely',
        what: 'ON makes the Pi Browser build never request building art. Buildings show ' +
              'their placeholder instead. The town still works. Ships OFF.',
        min: 0,
        max: 1,
        risk: 'Deliberately trades how the game LOOKS for a clean answer about whether ' +
              'art downloads are what is crashing Pi Browser. Next launch, not immediate.',
    },
    'assets.maxConcurrentRequests': {
        area: 'misc',
        label: 'Art downloads allowed at once',
        what: 'How many art downloads may run at the same time. 0 means what ships ' +
              'today: no explicit cap. 1 or more installs a hard ceiling everywhere.',
        min: 0,
        max: 8,
        risk: 'Bug-hunting lever. 0 is not "none allowed" - it means "no cap", which is ' +
              'the shipped behaviour.',
    },
    'pi.requestTimeoutSeconds': {
        area: 'misc',
        label: 'Pi Browser: seconds before an art download gives up',
        what: 'How long one art download may take in Pi Browser before it is abandoned. ' +
              'Ships at 20 seconds.',
        min: 5,
        max: 120,
        risk: 'Bug-hunting lever. Setting this to 0 would not mean "no timeout" - it is ' +
              'why Reset exists.',
    },
    'assets.maxRequestAttempts': {
        area: 'misc',
        label: 'Retries per piece of art',
        what: 'How many times one piece of art is re-requested before it is given up on ' +
              'for the rest of the session. Ships at 3.',
        min: 1,
        max: 10,
        risk: 'Bug-hunting lever. Too high and the retries themselves become the load.',
    },
    'visuals.missLogCap': {
        area: 'misc',
        label: 'Full log lines per missing model',
        what: 'How many full "could not find this model" lines are written before the ' +
              'log drops to a short repeated line. It never goes silent. Ships at 3.',
        min: 0,
        max: 50,
        risk: 'Diagnostics volume only. Failures are always logged, at every setting.',
    },
    'trace.assetVerbosity': {
        area: 'misc',
        label: 'Asset log detail',
        what: 'How chatty the asset system is in the logs. 2 is what ships (everything). ' +
              '1 is milestones only. 0 is no routine narration.',
        min: 0,
        max: 2,
        risk: 'Only the SUCCESS narration dims. Warnings and failures are always logged ' +
              'and cannot be turned off.',
    },

    // -- WO-1348. THE FOUR VFX PICKS. Her ask, verbatim: "is it possible to tag those
    //    from the command center? and then change pointer on next town load?" These
    //    are the four tags she could not correct on 2026-09-03 without a rebuild.
    // ⛔ EACH CARD SAYS, IN WORDS, WHETHER THE GAME ACTUALLY PLAYS THAT SLOT TODAY.
    // Three of these four do NOT, and every one of those three was checked at source on
    // 2026-09-10 rather than assumed. A card that reads "which effect plays when X" for a
    // slot nothing calls would let her pick, look, see no change, and conclude the whole
    // feature is broken - which is acceptance criterion 3 of WO-1348 ("the UI never implies
    // a pick applied when it did not") failing on its first use.
    'realm.vfx.atfootprintoftree_Aura': vfxPick(
        'World tree: the glow at its foot',
        'Which effect sits glowing at the base of the world tree. 0 is the tag you were not ' +
        'happy with. NOTE - THIS SLOT IS SWITCHED OFF IN THE GAME RIGHT NOW: you turned the ' +
        'tree-foot glow off on 2026-09-07 because it drifted upward over the town, so the tree ' +
        'foot shows nothing whatever you pick here. Your pick is saved and will be what plays ' +
        'the moment the slot is switched back on, which is a code change.',
        'The slot runs CONTINUOUSLY, so pick from the entries marked continuous. A one-shot is ' +
        'refused and the game keeps the shipped effect - it will not leave the tree bare.'),

    'realm.vfx.atfootprintoftree_Impact': vfxPick(
        'World tree: the burst at its foot',
        'A burst at the base of the world tree. NOTHING IN THE GAME PLAYS THIS SLOT TODAY - ' +
        'there is no effect tagged for it and no code that calls it, so a pick here will not ' +
        'be visible until that call is wired, which is a code change. It is offered because ' +
        'the slot is the one you asked to be able to create rather than only replace.',
        'Picking here creates a tag rather than replacing one, and you will not see it yet. ' +
        'That is not a fault - it is what the game does today.'),

    'realm.vfx.EliteDeath_Impact': vfxPick(
        'Elite death burst (not wired yet)',
        'A separate burst for an elite death. NOTHING IN THE GAME PLAYS THIS SLOT TODAY: an ' +
        'elite dying uses the BOSS death slot below, which is what you asked for - "both get ' +
        'Elite_Death". A pick here will not be visible until an elite is given its own call, ' +
        'which is a code change. To change what an elite death looks like NOW, use the boss ' +
        'death card below.',
        'Offered so the slot is ready the day elite and boss deaths are pulled apart. Until ' +
        'then it changes nothing on screen.'),

    'realm.vfx.BossDeath_Impact': vfxPick(
        'Boss death burst',
        'Which effect plays when a boss dies. 0 is the one the game ships with. THIS IS THE ' +
        'ONE OF THE FOUR THAT IS LIVE - set it, load the town, kill a boss and look.',
        'Covers bosses that use the normal enemy path. The dragon, Syndrath the Devourer, has ' +
        'its own death effect and is NOT changed by this card.'),
};

/** The verbatim boundary sentence. Printed on the page; asserted by the oracle. */
const OUT_OF_SCOPE_NOTICE =
    'Prices, purchase amounts, entitlements and grants are NEVER editable here and never ' +
    'will be. Those are decided by the server (api/_lib/purchase-catalog.js) because the ' +
    'game takes real money; a value the phone could override would be an exploit, not a ' +
    'feature. This page edits gameplay numbers only.';

/** Clear is not zero, in one sentence. Printed on the page; asserted by the oracle. */
const CLEAR_IS_NOT_ZERO_NOTICE =
    'Reset REMOVES the override so the knob answers whatever the installed game says, ' +
    'which is not the same as setting it to 0. Resetting the Pi Browser art timeout ' +
    'returns it to 20 seconds; setting it to 0 would mean zero seconds.';

/** Map from key to its allowlist entry, so the join can prove the key is writable. */
function allowlistMap() {
    const m = new Map();
    for (const spec of TUNABLE_KEYS) m.set(spec.key, spec);
    return m;
}

// ─────────────────────────────────────────────────────────────────────────────
// ⭐ SERVER-ONLY ROWS - Q-CONFIG (RULED 2026-09-10 13:36, owner): "Command Center,
//    server-only rows - teach tunable-manifest a serverOnly marker honoured by
//    build(); the client registry never carries the ladder."
// ─────────────────────────────────────────────────────────────────────────────
//
// THE PROBLEM THE MARKER SOLVES. This manifest is a THREE-WAY JOIN over
// RemoteTunables.Registry (the BUILD's knob list), TUNABLE_KEYS (the server write
// allowlist) and PRESENTATION (owner-facing prose). The join is deliberately
// unforgiving: a PRESENTATION row with no registry entry is reported as "the page
// would show a lever that moves nothing", which is exactly right for a CLIENT knob.
//
// But Heartbound's knobs are read by the BACKEND ONLY. Putting a tier threshold or
// an economic ceiling into RemoteTunables.cs to satisfy the join would hand the
// Unity client a readable copy of the ladder - a SECOND AUTHORITY on a number
// product rule 6 (spec :21) makes the server's, and precisely the duplicated-state
// failure CLAUDE.md §2/§5/§8/§16 each record a scar from. The knob would be
// visible in the Command Center and wrong in the game.
//
// So a row may declare `serverOnly: true`, which means:
//   * it is EXEMPT from the two registry-agreement defects, and from them only;
//   * every other check still applies - area, safe range, label, prose, ASCII;
//   * `build()` emits it carrying `serverOnly: true` so the page can render it
//     without pretending a build reads it.
//
// ⛔ AND `def` FOR SUCH A ROW IS REFERENCED, NEVER TYPED. A server-only knob's
// shipping value lives in the backend module that reads it (e.g.
// api/_lib/heartbound-tiers-config.json). A row must therefore carry `def` as a
// value pulled from that module at require time - `require(...).some.path` - which
// is a REFERENCE and not the copy this file spends its header forbidding. A row
// that hardcodes a number here recreates the exact defect the marker exists to
// avoid, one line lower down.
//
// ⚠ AS SHIPPED THERE ARE ZERO serverOnly ROWS, AND THAT IS DELIBERATE, NOT AN
// OVERSIGHT. The mechanism lands with WO-1682 D3; the Heartbound rows themselves
// need two things this lane does not own - a key in TUNABLE_KEYS (api/_lib/
// tunables.js, the write rail) and a card on the served page (api/admin/console.js)
// - or the owner would be shown a lever that genuinely could not move, which is the
// one defect this whole join exists to catch. See
// WorkOrders/WORK_ORDER_1682_passive_economy_guardrails.RESULT.md, "what was NOT
// done and why". The behaviour is proven by injection in
// test/tunables-manifest.test.js rather than by a live row.

/** True when a PRESENTATION row declares itself backend-read. */
function isServerOnly(pres) {
    return !!(pres && pres.serverOnly === true);
}

/**
 * The checks that are about the PRESENTATION ROW ITSELF - area, safe range, prose,
 * and whether the shipping value is inside the range the page will offer.
 *
 * ⛔ EXTRACTED SO A serverOnly ROW GETS THE SAME SCRUTINY. Before this existed, all
 * of these lived inside the loop over the BUILD REGISTRY, so a row with no registry
 * entry was never reached by any of them. Marking such a row `serverOnly` would then
 * have exempted it not from ONE defect but from EVERY defect - a backend knob with a
 * broken range, no label and a nonexistent area would have joined cleanly and shown
 * up on the owner's page as a lever she could not use. Caught by
 * test/tunables-manifest.test.js's "the serverOnly exemption is EXACTLY ONE DEFECT
 * WIDE" case before it shipped, which is the case that exists to catch exactly this.
 *
 * @param {string} key
 * @param {object} pres the presentation row
 * @param {string} kind 'bool' | 'int'
 * @param {number|undefined} shippingDefault the value the knob ships at, if known
 * @param {string[]} out defects are appended here
 */
function checkPresentationRow(key, pres, kind, shippingDefault, out) {
    const areaIds = new Set(AREAS.map((a) => a.id));
    if (!areaIds.has(pres.area)) {
        out.push('CONSOLE MANIFEST: "' + key + '" claims area "' + pres.area +
                 '", which is not one of ' + AREAS.map((a) => a.id).join(' / ') + '.');
    }
    if (!(typeof pres.min === 'number') || !(typeof pres.max === 'number') || pres.min > pres.max) {
        out.push('CONSOLE MANIFEST: "' + key + '" has no usable safe range.');
    } else if (typeof shippingDefault === 'number' &&
               (shippingDefault < pres.min || shippingDefault > pres.max)) {
        out.push('BUILD REGISTRY vs CONSOLE MANIFEST: "' + key + '" ships at ' + shippingDefault +
                 ' but the manifest safe range is ' + pres.min + '..' + pres.max +
                 ' - the page could not offer the value the game actually ships with.');
    }
    if (kind === 'bool' && (pres.min !== 0 || pres.max !== 1)) {
        out.push('CONSOLE MANIFEST: "' + key + '" is a bool but its safe range is not 0..1.');
    }
    if (!pres.label || !pres.what) {
        out.push('CONSOLE MANIFEST: "' + key + '" has no label or no plain-English description.');
    }
}

/**
 * Every way the three sources can disagree, as a list of plain-English defects.
 * EMPTY MEANS AGREEMENT. Each string NAMES THE TWO SOURCES, because "the manifest
 * is wrong" is not an actionable sentence at 2am and "the build registry has
 * combat.foo, the manifest does not" is.
 *
 * @returns {string[]}
 */
function mismatches(presentation = PRESENTATION) {
    const out = [];
    const allow = allowlistMap();
    const spine = Array.isArray(generated.knobs) ? generated.knobs : null;

    if (!spine) {
        out.push('tunable-manifest.generated.json has no knobs array - regenerate it with ' +
                 'node tools/gen-tunable-manifest.mjs');
        return out;
    }

    const seen = new Set();

    for (const knob of spine) {
        const key = knob && knob.key;
        if (!key) { out.push('tunable-manifest.generated.json holds a knob with no key'); continue; }
        seen.add(key);

        const spec = allow.get(key);
        if (!spec) {
            out.push('BUILD REGISTRY vs SERVER ALLOWLIST: RemoteTunables.Registry has "' + key +
                     '" but TUNABLE_KEYS in api/_lib/tunables.js does not - the server would ' +
                     'REFUSE every write to it.');
        } else if (spec.kind !== knob.kind) {
            out.push('BUILD REGISTRY vs SERVER ALLOWLIST: "' + key + '" is ' + knob.kind +
                     ' in RemoteTunables.Registry and ' + spec.kind +
                     ' in TUNABLE_KEYS (api/_lib/tunables.js).');
        }

        const pres = presentation[key];
        if (!pres) {
            out.push('BUILD REGISTRY vs CONSOLE MANIFEST: RemoteTunables.Registry has "' + key +
                     '" but PRESENTATION in api/_lib/tunable-manifest.js does not - the knob ' +
                     'would be INVISIBLE in the Command Center.');
            continue;
        }
        checkPresentationRow(key, pres, knob.kind, knob.default, out);
    }

    for (const spec of TUNABLE_KEYS) {
        // A serverOnly key is written from the console and read by a BACKEND module.
        // No build registers it, and that is the whole point of the marker.
        if (isServerOnly(presentation[spec.key])) continue;
        if (!seen.has(spec.key)) {
            out.push('SERVER ALLOWLIST vs BUILD REGISTRY: TUNABLE_KEYS in api/_lib/tunables.js ' +
                     'has "' + spec.key + '" but RemoteTunables.Registry does not - no build ' +
                     'reads it, so writing it would do nothing.');
        }
    }

    for (const key of Object.keys(presentation)) {
        // ⭐ THE serverOnly EXEMPTION, and it is EXACTLY ONE DEFECT WIDE. A
        // backend-read knob has no client registry entry BY DESIGN (Q-CONFIG) - so
        // it skips the registry-agreement defect below and NOTHING ELSE. Its row is
        // validated here instead, because the loop that normally does it iterates the
        // registry and would never reach a row that is not in it.
        if (isServerOnly(presentation[key])) {
            const pres = presentation[key];
            const spec = allow.get(key);
            checkPresentationRow(key, pres, spec ? spec.kind : 'int', pres.def, out);
            if (!spec) {
                out.push('SERVER-ONLY ROW vs SERVER ALLOWLIST: "' + key + '" is marked ' +
                         'serverOnly but TUNABLE_KEYS in api/_lib/tunables.js does not carry ' +
                         'it - the console could show the lever and the server would REFUSE ' +
                         'every write to it. serverOnly exempts a row from needing a BUILD, ' +
                         'never from needing to be writable.');
            }
            continue;
        }
        if (!seen.has(key)) {
            out.push('CONSOLE MANIFEST vs BUILD REGISTRY: PRESENTATION in ' +
                     'api/_lib/tunable-manifest.js has "' + key + '" but ' +
                     'RemoteTunables.Registry does not - the page would show a lever that ' +
                     'moves nothing.');
        }
    }

    return out;
}

/**
 * The manifest the page is driven by: areas in the owner's order, each carrying
 * its knobs in registry order.
 *
 * ⚠ IT REFUSES TO SHIP A DISAGREEMENT SILENTLY. Any knob this cannot fully join
 * is DROPPED from the areas and NAMED in `defects`, and the page prints those
 * words. A balance editor that quietly hides a lever is worse than one that says
 * it is broken, because the owner would go on believing the number cannot move.
 *
 * @returns {{version:number, areas:Array, defects:string[], notices:object}}
 */
function build(presentation = PRESENTATION) {
    const defects = mismatches(presentation);
    const allow = allowlistMap();
    const spine = Array.isArray(generated.knobs) ? generated.knobs : [];

    const byArea = new Map(AREAS.map((a) => [a.id, []]));
    for (const knob of spine) {
        const pres = knob && presentation[knob.key];
        if (!pres) continue;
        if (isServerOnly(pres)) continue;            // emitted below, from its own row
        if (!byArea.has(pres.area)) continue;
        if (!allow.has(knob.key)) continue;          // not writable => not offered
        byArea.get(pres.area).push({
            key: knob.key,
            kind: knob.kind,
            def: knob.default,
            label: pres.label,
            what: pres.what,
            min: pres.min,
            max: pres.max,
            risk: pres.risk || null,
            // WO-1348: present ONLY on a knob whose value names a thing rather than measures
            // one. The page renders a named picker for these and a number field for everything
            // else - "13" is not a choice anybody can make about how the game looks.
            options: Array.isArray(pres.options) ? pres.options : null,
            optionsNote: pres.optionsNote || null,
        });
    }

    // ⭐ SERVER-ONLY ROWS. They have no spine entry by design, so their kind and
    // shipping default come from the row itself - where `def` must be a REFERENCE
    // into the backend module that reads the knob, never a retyped number (see the
    // serverOnly header above). They still have to be WRITABLE to be offered: a row
    // the server allowlist would refuse is "a lever that moves nothing", the exact
    // defect this join exists to catch, so it is dropped here the same as any other.
    for (const key of Object.keys(presentation)) {
        const pres = presentation[key];
        if (!isServerOnly(pres)) continue;
        if (!byArea.has(pres.area)) continue;
        if (!allow.has(key)) continue;
        byArea.get(pres.area).push({
            key: key,
            kind: allow.get(key).kind,
            def: pres.def,
            label: pres.label,
            what: pres.what,
            min: pres.min,
            max: pres.max,
            risk: pres.risk || null,
            // The page needs to know NOT to describe this as a value the installed
            // game ships with - no build reads it. Saying so is the honest render.
            serverOnly: true,
        });
    }

    return {
        version: 1,
        source: generated.source,
        areas: AREAS.map((a) => ({
            id: a.id,
            title: a.title,
            blurb: a.blurb,
            knobs: byArea.get(a.id) || [],
        })),
        defects: defects,
        notices: {
            outOfScope: OUT_OF_SCOPE_NOTICE,
            clearIsNotZero: CLEAR_IS_NOT_ZERO_NOTICE,
        },
    };
}

module.exports = {
    AREAS,
    PRESENTATION,
    VFX_PICK_OPTIONS,
    VFX_PICK_MAX_ID,
    OUT_OF_SCOPE_NOTICE,
    CLEAR_IS_NOT_ZERO_NOTICE,
    isServerOnly,
    mismatches,
    build,
};
