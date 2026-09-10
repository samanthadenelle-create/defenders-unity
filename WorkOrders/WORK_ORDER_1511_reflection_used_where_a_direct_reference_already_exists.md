# WO-1511: reflection at seven sites where a direct reference exists, two of them same-assembly, plus one audio-seam bypass

**Status:** PARTIALLY IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** Architecture. Straightforward mechanical replacement; pairs with WO-1510.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1511 -> 1512 in the same edit).

## 1. EVIDENCE

Village reflecting on VILLAGE - same assembly, so the type is directly nameable:

```
BattleArena.cs:3095        CastleNavTopologyDiag.cs:153
```

Reflecting into Core, which every one of these assemblies already references:

```
VisualFactory.cs:672   HelpMenu.cs:670, 679   AdminOverlay.cs:522   AudioBootstrap.cs:190
```

And one bypass of the sanctioned seam:

```
HudKitController.cs:4281   reflects into DeNelle.Audio directly, bypassing CoreServices.Audio
```

CLAUDE.md sec.10's checklist forbids new reflection in bridge scripts and sec.5 names `CoreServices.Audio` as
the way across. Reflection where a direct call compiles costs a type lookup per call, defeats the compiler,
and - as WO-1510 shows - tends to come with a swallowing catch.

## 2. FIX SHAPE

- Replace all seven with direct calls. Two are same-assembly and need nothing but the type name.
- `HudKitController.cs:4281` goes through `CoreServices.Audio` with `?.`, as sec.5 requires.
- Delete the now-dead reflection helpers, if any become unreferenced.

## 3. WHAT NOT TO DO
- Do not touch the HUD/Village sites here; those are WO-1510 and need a resolver, not a direct call.

## 4. ACCEPTANCE
- [ ] All seven replaced; file:line list in the RESULT.
- [ ] `HudKitController` audio call goes through `CoreServices.Audio`.
- [ ] The no-new-reflection regression covers these files.
- [ ] `REGRESSION_OK n/n` on a fresh log.
