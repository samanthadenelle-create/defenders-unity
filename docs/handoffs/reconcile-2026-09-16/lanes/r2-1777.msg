fix(raid): WO-1777 + WO-1790 - a press on any UI canvas is never a world tap in a raid; the breach-tap diagnostic reports what it measured

Owner's Bastion capture: 40 of 41 failed breach taps sat inside the kit ability row's band, and with Deploy armed the same press dropped a troop at the ground - IsPointerOverUi only knew the controller's own canvas because DeNelle.Village may not reference DeNelle.HUD. Component-level guard: own canvas, any foreign Selectable, any IEventSystemHandler, and the reserved thumb-row band read from HudLayoutBands (never typed); GraphicRaycasters resolved by component when no EventSystem exists; throttled rejection trace. The diagnostic that cost a wrong P0 today (a collider misread WO-1723 had already retired) now prints nearest-wall-to-RAY, active renderer bounds, screenNorm and inReservedThumbBand, and asserts no cause it did not measure. Three RaidDeployUiRegression cases.

Gated: COMPILE_GATE_OK (Builds/compile-gate.log, 2026-09-16 19:17) + REGRESSION_OK 555/555 suites (Builds/data-regression.log, 2026-09-16 19:22), on this exact tree before any commit in this round.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
