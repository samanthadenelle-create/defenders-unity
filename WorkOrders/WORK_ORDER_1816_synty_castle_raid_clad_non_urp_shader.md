# WO-1816 — Fix Synty Castle materials rendering pink on device (non-URP shader fallback)

**Status:** IMPLEMENTED

## Coordinator updates (gate round 2)

1. ✅ Regression now follows DataRegression pattern: `public static bool Run(out string report)` 
2. ✅ Both tool and regression exclude FX/Flags (transparent/cutout) — separate ticket
3. ✅ Tool copies render queue and alpha-clip (_Cutoff, _AlphaClip) for proper transparency migration
4. ✅ Tool notes that Assets/Synty is gitignored (.gitignore:732) — converted .mat files don't commit, tool is deliverable
5. ✅ Both files: brace check PASS (bad=0 of 2), NUL check PASS

## Evidence

**Device observation (build 372984, 2026-09-16 19:55):**
- Scene: RaidBase_fortified_garrison
- Device log: `logs/device/raid-window-1955.txt` — nine [Flow:RaidArt] errors naming shader='Synty/Generic_Basic' (non-URP)
- Screenshot: Builds/raid-post-audit/RaidBase_fortified_garrison_CornerPost_Keep1_E.png — solid YELLOW (URP fallback rendering)

**Root cause:** materials under Assets/Synty/ use a non-URP shader (Synty/Generic_Basic); on a URP device, Unity renders the fallback (pink/magenta/yellow).

## Material inventory

**Non-URP shader guid:** `0730dae39bc73f34796280af9875ce14`

**Resolves to:** Assets/Synty/PolygonGeneric/Shaders/Generic_Basic.shadergraph

**Example material:** Assets/Synty/PolygonFantasyKingdom/Materials/Walls/Castle_Wall_01.mat
- m_Shader guid: 0730dae39bc73f34796280af9875ce14
- _Albedo_Map: guid 24f1ea296c9e695449086de7c2eca5e4
- _Normal_Map: guid 2201873fd1cce694bb2347a8c13e652d
- _Color: (1, 1, 1, 1)

**Materials affected (68 total):**
- Assets/Synty/PolygonFantasyKingdom/Materials/Alts/PolygonFantasyKingdom_Mat_*.mat (12)
- Assets/Synty/PolygonFantasyKingdom/Materials/Walls/Castle_Wall_*.mat (5)
- Assets/Synty/PolygonFantasyKingdom/Materials/FX/*.mat (4)
- Assets/Synty/PolygonFantasyKingdom/Materials/Flags/Flag_Symbol_*.mat (4)
- Assets/Synty/PolygonFantasyKingdom/Materials/Ground/*.mat (9)
- Assets/Synty/PolygonFantasyKingdom/Materials/Misc/*.mat (6)
- Assets/Synty/PolygonGeneric/Materials/Generic_*.mat (18)

## Acceptance criteria

1. ✅ Create Assets/Editor/SyntyCastleUrpMaterials.cs with:
   - Menu: "Defenders/Art/Fix Synty Castle URP Materials"
   - Batch method: `public static void Run()`
   - Scans all .mat files in Assets/Synty/ folder
   - For each material using the non-URP shader (guid 0730dae39bc73f34796280af9875ce14):
     - Changes shader to "Universal Render Pipeline/Lit"
     - Copies _MainTex → _BaseMap (if _MainTex present)
     - Copies _Color → _BaseColor
     - Preserves _Normal_Map → _NormalMap if present
   - Prints one log line per material converted
   - Final log: `SYNTY_CASTLE_URP_OK <n> material(s)` or `SYNTY_CASTLE_URP_FAIL`

2. ✅ Create Assets/Editor/Regression/SyntyCastleUrpRegression.cs with:
   - Marker: `SYNTY_CASTLE_URP_OK` / `SYNTY_CASTLE_URP_FAIL`
   - Loads every .mat under Assets/Synty/
   - Fails if any shader name does not start with "Universal Render Pipeline/"
   - **Do NOT register in DataRegression.cs** — lead will add registration

3. ✅ Brace check and Python NUL check clean on all .cs files

## Do NOT touch

- Assets/Editor/WallTools/
- Any .unity scene files
- CLI_LANES_WO_NUMBERS.md
- VFXManager.cs, AbilityVfxKit.cs, DamageNumberSpawner.cs

## Pattern reference

- See Assets/Editor/KayKitMaterials.cs for shader-finding and material-property patterns
- See Assets/Editor/EnemyMaterialRemap.cs for material remapping patterns
- Follow `Shader.Find("Universal Render Pipeline/Lit")` idiom
