# WO-1510: Core reaches UP into Village by reflection, the HUD/Village invariant is pierced both ways, and one catch is silent

**Status:** PARTIALLY IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** Architecture. `SceneRouter`, `PersistenceBridge`, `BreakCaptureHarness`, ten HUD files, three Village
files.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1510 -> 1511 in the same edit).

## 1. EVIDENCE

**Layering inversion - Core reflecting UP into Village:**

```
SceneRouter.cs:510, 523      PersistenceBridge.cs:174      BreakCaptureHarness.cs:491
```

**HUD -> Village**, 18 `Type.GetType("DeNelle.Village...")` sites across 10 files:

```
HudKitController.cs:1539, 1556, 1579   HelpMenu.cs:613   OwnerDevToolsOverlay.cs:371
six *Bootstrap.cs files
```

**Village -> HUD**, the reverse direction:

```
HeroLocomotion.cs:2038   BuildModeController.cs:327   DungeonHero.cs:789
   -- all reflecting into DeNelle.HUD.Kit.HudMoveInput
```

And a silent failure, which sec.12 forbids outright:

```
HeroLocomotion.cs:2045-2048   catch { return Vector2.zero; }
```

CLAUDE.md sec.5's invariant is that HUD never references Village in EITHER direction, and reflection there is
"evidence of the rule". Eighteen sites in one direction and three in the other is not evidence of a rule; it
is an unmanaged seam, and the bare catch means when it breaks the hero simply stops moving with no line in
the log.

## 2. FIX SHAPE

- Core -> Village: replace the four sites with an INTERFACE on `CoreServices`, the sanctioned seam. Core must
  not name a Village type at all.
- HUD <-> Village: ONE resolver that owns the reflection, with the type lookup cached and every failure
  traced. Twenty-one call sites become one.
- `FlowTrace.Warn` inside `HeroLocomotion.cs:2045-2048` before the return. No catch swallows without logging.

## 3. WHAT NOT TO DO
- Do not add asmdef references to "fix" the HUD/Village seam. The asmdef boundary is the invariant; the
  resolver is how you cross it.

## 4. ACCEPTANCE
- [ ] Zero `DeNelle.Village` string literals in Core; zero in HUD outside the one resolver (grep pasted).
- [ ] The bare catch logs; proven by forcing the failure.
- [ ] A regression fails on a new `Type.GetType("DeNelle.Village` outside the resolver.
- [ ] `REGRESSION_OK n/n` on a fresh log.
