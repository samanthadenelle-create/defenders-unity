# WORK ORDER 1601 - The Skills tree draws a wide gold frame band straight across the nodes, and the outer nodes are clipped at the panel edges

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:10:47, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 evening gate (COMPILE_GATE_OK Builds/cg-wave11.log, REGRESSION_OK 456/456 Builds/reg-wave11.log 14:19); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT (instrument first) - minted 2026-09-07 (CLI) from the owner's reset screenshot
**Silo / Lane:** Village/Hero skills - `HeroSkillTreePanelMvvm` (log: `[Flow:UI] HeroSkillTreePanelMvvm created (single instance)` 13:28:40 / 13:29:06), its layout and the kit frame it draws
**Type:** EXISTING system, LAYOUT DEFECT
**Priority:** P1 - the screen is unreadable

## Evidence

Frame `Logs/device/seeker-shots/Screenshot_20260907-132616.png` (Seeker 2026.09.07.359651): SKILLS with
BACK / EQUIPMENT chrome; a full-width ornate frame band (~y 0.37-0.55) drawn ACROSS the tree, over the
ARCANE BOLT node and the connector lines; nodes at both edges cut off (0/1 medallions half outside the
panel at x~0.27 and x~0.9); "WISDOM 2 - next point at Level 3" pill; the assigned-skills strip at the
bottom is fine. This is a NEW hero (Lv2, one point) - the tree at its smallest population.

## What to do

- Instrument: `FlowTrace.Step("SkillTree", "layout: nodes=N bounds=... band=... viewport=...")` naming
  every rect the panel builds, including any kit frame/plate it instantiates and what it frames.
- Headless capture of the tree at Lv2 (one point) and Lv9 at 2670x1200; find the stray band's owner (a
  description plate sized to the whole width? a scroll frame? a second chrome row?) from the trace.
- Fit the tree inside the viewport (scale/scroll so no node is clipped), draw the frame only around what
  it frames; RED-first regression on node containment + no plate intersecting a node.

## Acceptance
- Capture and device: every node fully inside the panel, no band across the tree, connectors clean.
  Owner felt-test closes.
