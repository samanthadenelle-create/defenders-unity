# Armor icon brief — 6 missing pieces

Echoes of Elarion (medieval fantasy mobile RPG). These six items already exist in the game's data and
are already wired to load these exact filenames — nothing else needs to change once the images are
dropped in.

## File spec (match the 19 armor icons already in the game)

- **Size:** 784 × 1168 px, portrait orientation (roughly a 2:3 card ratio)
- **Format:** PNG, RGB, no transparency — full-bleed opaque card art, not a cutout icon on a clear
  background
- **File names:** exactly as listed below, all lowercase, underscores, `.png` extension
- **Deliver to:** `Assets/Resources/ItemIcons/`

## Style

Stylized fantasy RPG equipment portrait — a single piece of worn armor (or the full suit) rendered as
box/card art, warm painterly lighting, consistent with a medieval fantasy village-and-dungeon game
called "Echoes of Elarion." These six form ONE progression ladder from a wandering commoner's garb up
to a legendary holy relic, so render them as a set: each step should look visibly heavier, more
ornate, and more magical than the last. The last one (Aegis of Elarion) should read as the most
elaborate, important-looking piece in the whole set — a legendary artifact, not just "armor."

## The six pieces, in order

1. **`armor_cloth.png` — Wanderer's Cloth**
   Rarity: Common. Worn by: any class.
   A plain traveler's tunic and wrap, patched cloth, muted earthy colors (browns, dull greens). Reads
   as the starting-gear look every new player begins in — humble, practical, nothing decorative.
   (Stats for flavor: +4% defense, +10 HP — the weakest piece in the game.)

2. **`armor_leather.png` — Tanned Leather**
   Rarity: Uncommon. Worn by: Ranger, Mage.
   Fitted leather armor — vest or light jacket with visible stitching and buckles, still practical but
   a clear step up from cloth. Slightly richer browns/tans, maybe a hint of green dye for a
   ranger/scout feel.
   (+8% defense, +25 HP.)

3. **`armor_chain.png` — Chainmail Vest**
   Rarity: Rare. Worn by: Knight, Cleric.
   A proper chainmail hauberk/vest over a padded underlayer, riveted rings visible, worn look but well
   maintained. Cooler metal tones (steel grey), the first piece that reads as real "soldier's armor."
   (+14% defense, +45 HP.)

4. **`armor_plate.png` — Elarion Plate**
   Rarity: Epic. Worn by: Knight, Cleric.
   Full plate armor with the kingdom's heraldry/insignia worked into the breastplate — polished steel,
   maybe gold trim or an emblem referencing "Elarion" (a heart/tree motif would fit the game's canon —
   the village is built around the Heart of Elarion, a world-tree/reliquary). This should look like
   armor a captain or elite guard would wear.
   (+20% defense, +75 HP.)

5. **`aegis_plate.png` — Aegis of Elarion**
   Rarity: Legendary. Worn by: any class.
   A holy/magical relic-armor — full plate but glowing with an inner light (gold or soft blue-white
   aether energy), ornate engravings, possibly a faint magical aura or particle glow around the edges.
   This is the best armor in the entire game — it should look unmistakably legendary next to the other
   five.
   (+28% defense, +100 HP — the strongest armor in the game.)

6. **`armor_mage_common.png` — Apprentice Robes`**
   Rarity: Common. Worn by: Mage only.
   A separate small ladder from the five above — this is a mage-only starting robe (there's already a
   full uncommon→legendary mage robe set in the game; this is the missing common tier below it, so it
   should look like a plainer, less magical version of a wizard's robe). Simple cloth robe, muted
   blues/purples, maybe a small hood, nothing glowing yet — an apprentice, not a master.
   (+3% defense, +8 HP, minor mana regen — flavor: a beginner's magic robe.)

## Notes for whoever renders these

- Same character pose/framing across all six if possible, so they sit consistently together on an
  equipment or shop screen (the existing 19 icons are individual portrait-style renders of the item
  itself, not a worn character — match that: show the ARMOR PIECE, not a person wearing it).
- No text, no watermark, no UI frame baked into the image — the game adds its own card border/rarity
  frame around whatever art is dropped in.
- If in doubt on color/theme for #4 and #5, lean toward the game's established palette: warm gold and
  parchment tones for "holy/kingdom" pieces, since the village's tagline is "Echoes of a Forgotten
  Civilization" and its centerpiece is the Heart of Elarion.
