# WORK ORDER 1758 — The arena boundary ring renders as flat grey slabs: its pillars' 2nd material slot is a Polyperfect colour material with NO albedo

**Status:** IMPLEMENTED, NOT YET GATED
**Minted:** 2026-09-15 by the lead. This is the owner's "giant untextured grey box", NAMED by the WO-1751 census on its first device run — it supersedes the unproven candidates in WO-1751 part (A), which is now CLOSED by this ticket.
**Silo:** `Assets/Editor/ArenaBoundaryRing.cs` (the builder) and the Polyperfect material wiring. ⛔ NOT `RaidUntexturedCensus.cs` (it worked — do not touch the instrument), NOT `RaidBaseDresser.cs` / `RaidBaseGenerator.cs`, NOT any `.unity`.

## Evidence — device logcat, tester build `2026.09.15.371285`, F8 seq 5295-5300 (verbatim)
```
#1 URP shader with NO ALBEDO bound and a light tint (flat grey slab)
   path='RaidBase_iron_bastion/ArenaBoundary_Ring/ArenaBoundary_E_7'
   mesh='dungeon-pillar-stone-square' layer=0 slot=1
   material='M_21_Grey_Light_LPUP' shader='Universal Render Pipeline/Lit'
   tint=(0.65,0.63,0.62) bounds=2.6x7.0x2.6m at (-68.3,3.5,58.9)
   albedoSlots=[_BaseMap=EMPTY, _MainTex=EMPTY]
```
`#2` and `#3` are `ArenaBoundary_S_45` / `ArenaBoundary_E_46`, byte-identical in mesh, slot, material and bounds. The census counted **418 offending slots across 910 renderers / 1477 slots** in `RaidBase_IronBastion`; the boundary ring is the overwhelming majority. 2.6 x 7.0 m matches the slab in the owner's screenshot `Screenshot_20260915-135355.png`.

## What is PROVEN
- The offender is **slot 1** (the SECOND material slot) of the KayKit `dungeon-pillar-stone-square` mesh.
- The material is `Assets/polyperfect/Low Poly Ultimate Pack/Materials/Colors/M_21_Grey_Light_LPUP.mat` — it EXISTS in the tree (listed this session).
- It is a Polyperfect **colour-palette** material. ⚠ CLAUDE.md §4: the polyperfect pack is **gitignored** and re-imported per clone via **`Defenders/Art/Fix Polyperfect URP Materials`** (`PolyperfectUrpFix`, referenced at `CastleWallKitSpawner.cs:52` and `MagentaMaterialFixer.cs:32`). A palette material with an empty `_BaseMap` is exactly the symptom that pass exists to repair.
- `ArenaBoundaryRing.cs:634` already builds an `ArenaBoundary_Fallback` material, so the file knows about missing art.

## What is NOT proven — settle it before fixing
Whether this is (a) a **local import state** — the pack's URP fix never ran on this machine, so it would self-heal and must NOT be "fixed" by rewriting the ring; (b) a **genuine authoring gap** — the palette material is meant to carry a flat colour with no texture, and the defect is that the ring picked a palette material for a mesh slot that needs an atlas; or (c) the **ring builder binding the wrong slot**, leaving slot 1 at the prefab default.
⚠ (a) matters most: the pack is gitignored, so a fix that edits pack materials **cannot be committed** and would evaporate on the next clone. Determine which before touching anything.

## Acceptance
1. The cause is named with evidence, and the fix lives somewhere that SURVIVES a fresh clone (the ring builder or a committed material — never an edit to a gitignored pack asset).
2. A fresh device or headless run: zero `Flow:RaidArt` offenders whose path is under `ArenaBoundary_Ring`; the total falls from 418 by the ring's share.
3. The remaining offenders (418 minus the ring) are listed and classified — real or predicate artefact — so the next pass starts from facts.
4. Lane flips this Status line and writes the `.RESULT.md`.
