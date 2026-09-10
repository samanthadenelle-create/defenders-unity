# WORK ORDER 1094 — the hero's ±50 playable-bounds clamp is a castle-era constant in a 1000×1000 world, and `_isTeleporting` guards zero frames

**Status:** IMPLEMENTED - awaiting gate (2026-09-09 lane LOCOMOTION)
**Minted:** 2026-09-09 by the UI seat (UI reserved block; banner bumped 1089 → 1095 in the same edit)
**Silo:** Hero / locomotion
**Severity:** P2 — latent, but it silently reverts any legitimate off-mesh position past ±50 anywhere in the merged world
**Source:** found at source while root-causing F8 capture seq=4706 (see **WO-1091**)

> Deliberately **not** folded into WO-1091, which is scoped to `HollowRoadsDropInjector.cs` alone.
> No working-tree edit exists for this ticket — `HeroLocomotion.cs` was declared off-limits to every
> agent in that batch and is clean.

---

## Defect 1 — the clamp bound belongs to a world that no longer exists

`const float PlayableHalf = 50f` (`Assets/_Modules/Village/Hero/HeroLocomotion.cs:1391`) clamps the
hero to a ±50 box. The home hub is now `Main_Castle_Overworld`, a **merged 1000×1000 world** (terrain
seated at (-500,-4,-500), size 1000×42). Any legitimate off-mesh hero position beyond ±50, anywhere
in that world, is silently reclaimed to the castle box.

Observed, in WO-1091's trace: the hero was warped to (-400, 17, 0) and relocated to (-50, 0) on the
very next frame —

```
[Flow:HeroLoco] playable-bounds CLAMP relocated the hero: (-400.00,0.00) -> (-50.00,0.00)
                [±50 off-mesh guard; agent=enabled/off-mesh, cc=<none>]
```

— after which the off-mesh ground-snap at `:1424-1452` settled them at (-50.34, 0.11, -2.83).

## Defect 2 — `_isTeleporting` is a dead guard

Set at `HeroLocomotion.cs:523`, cleared at `:577` — **both inside the same synchronous call.** The
clamp it is named to protect runs in `Update` (`:1388`). It therefore protects **zero frames**: a
deliberate warp is clamped on the very next tick, which is precisely what happened above.

*Both citations were read at source 2026-09-09. Re-verify before changing anything — the file may
have moved.*

## Fix spec

**The clamp exists for a reason** — it is an off-mesh guard, and removing it is not the fix. Make its
bounds match the actual world:

1. Derive the bound from the real world extent rather than a literal. Check whether it should come
   from the scene/terrain bounds or from `scene-configs.json`, and decide deliberately.
2. Check whether raid / dungeon / arena scenes need their own bounds — `:1391-1394` already
   special-cases the staged arena, so the per-scene seam exists.
3. **Never re-hardcode a second bound anywhere else** (the `RepoProps.MaxStructureLevel` lesson,
   CLAUDE.md §8): one authority, read from it.
4. `_isTeleporting`: either make it survive until the next `Update` tick — so a deliberate warp is
   never clamped in the frame after it lands — **or delete it as misleading.** It must not stay as-is,
   silently doing nothing while reading like protection.
5. **FlowTrace:** a clamp relocation must name both the bound it enforced **and where that bound came
   from**. The current line names the value only, which is why the ±50 read as intentional.

## Acceptance criteria

- [ ] A hero legitimately positioned past ±50 in `Main_Castle_Overworld` is not relocated.
- [ ] A genuinely off-mesh hero is still recovered — the guard's actual job still works.
- [ ] A deliberate warp is not clamped on the frame after it lands.
- [ ] The clamp trace names the bound and its source.
- [ ] Brace balance; gate markers on a fresh log.
- [ ] Owner felt-verifies movement at the far edges of the overworld and closes.

## Interaction with WO-1091

WO-1091 grounds the drop point so the hero never lands off-mesh at a biome door in the first place.
That fixes the *symptom path*; this ticket fixes the *latent trap*. Either alone leaves the other
live — a correctly grounded drop still gets clamped if it lands past ±50, and a corrected bound still
receives an un-probed y=17 point. **Both are needed; they can ship independently.**
