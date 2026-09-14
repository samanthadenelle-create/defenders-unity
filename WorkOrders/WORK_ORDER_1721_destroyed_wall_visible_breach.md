# WORK ORDER 1721 — Destroyed wall should leave a visible breach, not vanish

**Status: READY TO IMPLEMENT (queued — starts after WO-1719's collider/renderer height fix lands and
is committed, since both touch `WallSegment.cs`'s collapse code; avoid a same-file collision with that
in-flight lane)**
**Filed:** 2026-09-14, owner live playtest report: "I would like to see destroyed wall showing breach
if we can."
**Silo:** VFX/structure feedback (no gameplay-logic dependency once the collider fix is in) — but
same-file as the WO-1719 follow-up (`WallSegment.cs`), so sequence AFTER, never in parallel with it.

## Current behavior (read at source, cited)

`WallSegment.Collapse()` disables the solid collider + nav-carving obstacle, then runs a ~0.9s sink
coroutine ("collapse tell") that lowers the wall's renderers, ending with the ruin settling roughly
17m below the floor (`WallSegment.cs`, confirmed live: "ruin SETTLED at y=-17.28 (fell 17.28m)"). The
net visual result is the wall sinks out of view entirely — there is no rubble, stub, or breach gap left
behind; the space just reads as empty ground once the sink finishes.

## What the owner wants

A destroyed wall segment should read as a **breach** — visibly broken, not simply absent. Reference
the project's WWCD tie-breaker (memory: `design-tiebreaker-what-would-coc-do`) for the general shape:
a collapsed wall commonly leaves a low rubble pile / broken stub silhouette at the base, at or near
ground level, rather than a totally empty gap. Exact visual treatment is the CLI/asset's call within
that reference — this ticket does not mandate a specific asset, only that something reads as "this was
a wall, now it's breached" rather than "this ground was always empty."

## Owner rulings, 2026-09-14 evening, verbatim — READ BEFORE IMPLEMENTING

Given live, on the same day as the WO-1719/1720 collider fix, after the owner found (WO-1722) that an
**intact, non-destroyed** wall let her walk straight through it (the wall's real collider is far smaller
than its visible mesh — see WO-1722 for that separate defect). These rulings are about the DESTROYED
case specifically and stand regardless of how WO-1722 resolves:

1. *"if wall is destroyed remove the destroyed wall and replace with a destroyed wall with no colider
   that I can step over"* — on collapse, do not sink/hide the mesh (today's behavior, sinking 17m
   underground). Instead **swap to a distinct destroyed/rubble model** at the wall's footprint, and that
   model's collider (if any) must be low enough to step over — a rubble-height obstacle the hero can
   walk across, not a full wall-height blocker and not literally zero geometry either. This supersedes
   option (a) in the implementation guidance below ("stop the sink partway") — the owner wants an actual
   distinct destroyed VISUAL swap, not the same mesh merely stopped mid-sink.
2. *"that I can step over"* — confirms the ruling above: any residual collider on the destroyed-state
   model must be low/steppable, not blocking.
3. *"if there is not navmesh should be a navlink"* — raid navmeshes are pre-baked statically
   (`RaidNavBake.BakeAll`); dropping a `NavMeshObstacle` only reopens navmesh that was ALREADY baked
   walkable underneath it. If a wall's footprint sits somewhere the static bake never marked walkable in
   the first place (e.g., right at the mesh boundary, or the wall's own base literally has no walkable
   navmesh polygon under it even with the obstacle gone), dropping the obstacle alone will not let AI
   troops path through the breach — a `NavMeshLink` component must bridge that specific gap so troop
   pathing (not just the player's own physical walking) can actually cross a breach that opens onto
   previously-unbaked ground. Confirm at implementation time whether this is already the case for
   existing collapsed walls (it may already work if the ground under every wall was always baked
   walkable and only the obstacle was blocking it) before adding NavMeshLinks — do not add them
   speculatively; prove the gap exists first with a live troop-pathing capture through an actual breach.

## Implementation guidance (not mandatory shape, just a reasonable path)

1. Read `WallSegment.cs`'s full collapse sequence (search for `Collapse`, the sink coroutine, and
   the "ruin SETTLED" log line) to understand exactly what's rendered/hidden today.
2. Options to evaluate, pick the cheapest that reads well: (a) stop the sink partway (settle the ruin
   near y=0 instead of -17m) so the collapsed mesh itself becomes the visible rubble; (b) swap to a
   dedicated low-poly rubble/breach prop at the wall's footprint once the sink finishes; (c) leave a
   partial wall stub (lower fraction of the original mesh) rather than the whole mesh sinking.
   Prefer whichever needs the least new authoring — reusing the existing collapsed mesh (option a) is
   likely cheapest and worth trying first.
3. Confirm the breach still: drops the collider + nav obstacle (walkable, per existing behavior), does
   NOT block tower line-of-sight (existing, working behavior — do not regress), and does not conflict
   with WO-1720's fix if that lands first.
4. Add/extend FlowTrace at the collapse-visual step to record the new resting state (position/method
   chosen) so this is provable from a log read, not just eyeballed.
5. Screenshot-verify headlessly before calling this done (memory: `headless-screenshot-verify-ui-before-build`,
   `screenshots-are-primary-evidence-for-visual-defects`) — open the PNG, don't just read the log.

## Gate

Brace/NUL gate on every file touched. Do not run a full Unity gate yourself — the lead batches this.
Do not commit.

## Acceptance criteria

- A destroyed wall segment is visibly distinguishable from open ground — a breach/rubble reads clearly
  in a screenshot, not just in the log.
- The destroyed-state model's collider (if any) is low enough for the hero to walk over it without being
  blocked (owner ruling 1/2 above) — verify with an actual walk-through capture, not just a bounds read.
- AI troop pathing can cross the breach (owner ruling 3 above) — verify with a live troop-pathing capture
  through the breach, not assumed; add a `NavMeshLink` only if that capture proves a gap.
- Collision/LOS behavior for a destroyed wall does not block towers (existing, working behavior — do not
  regress).
- WO's own Status line flipped and `.RESULT.md` written on hand-back, per CLAUDE.md §11.
