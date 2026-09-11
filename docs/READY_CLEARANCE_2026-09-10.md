# READY clearance - 2026-09-10

Owner-authorized goal: clear the READY board through RCA, isolated implementation,
root review and check-in. Rebuild the board after every iteration. Present the
best options and a recommendation for any decision requiring the owner.

## Baseline

- Root: `dev`, `642744e20`; local `origin/dev` aligned at intake.
- Existing owner change: `Assets/Resources/Localization/Fonts/ElarionLocaleFallback.asset`.
  Preserve it; it is outside this effort's commits.
- Initial regenerated board: 13 Ready; `BOARD_CHECK_OK`.
- Ready IDs: 1244, 1291, 1314, 1484, 1665, 1697, 1698,
  Manage 2009, 2010, 2012, 2014, 2015, 2016.
- Historical gate baseline: `Builds/wave12-compile1` has `COMPILE_GATE_OK`;
  `Builds/wave12-reg2` has 507/507 suites, zero red/skipped. These are previous
  run evidence, not a fresh gate on changes from this effort.

## Status rule

Owner clarified in this session: implemented items awaiting proof from a test
build move to Fixed. Owner findings close or reopen them. Do not move an
unimplemented requirement or unresolved diagnosis merely to empty Ready.

## Iteration 1 - in progress

| Silo | Assignment | Ownership / fence |
|---|---|---|
| RCA | All 13, current acceptance and captured evidence | Read-only agent; sends bounded fixes and proof gaps |
| UI fit | 1697 | `D:/eoa-ready-1697`; `HeroSkillTreePanelMvvm.cs`, `TextFitGuardArmRegression.cs` |
| Backend | 1698 | `D:/eoa-ready-backend`; Google identity/session, promo-bind route, admin console, additive schema/migration, Node tests |
| Manage | 2009/2010/2012/2014/2015/2016 | One collision domain; classify existing delivery before assigning edits |
| Root CLI | Integration, Unity gates, statuses/results, board, explicit-path commits | No second Unity executor or committer |

1697 regression-only change is under root RED verification against the unchanged
production builder. 1698 implements the specified Google-email-only workflow;
wallet-input and display-name extensions remain optional rather than assumed.

No production migration, deployment, remote push, device input or external
message has been performed by this effort.

## Test-build queue reconciliation

Applied the owner's status rule to eight existing implementations/diagnostic
deliveries. None is claimed owner-approved or newly behavior-corrected.

| WO | Evidence checked at root revision 642744e20 | Owner test remaining |
|---|---|---|
| 2009 | `ManageScreenVM.ComposeTroopItem` places training before the non-max upgrade branch; `TrainTroop` enqueues one. `ManageFlow_ARMY_max_2670x1200.png` shows Level 7/MAX/View Queue. | Train max-tier troop with free capacity; verify one command |
| 2010 | Five school tiles on `ManageFlow_RESEARCH_gridtop_2670x1200.png`; school navigation exists. | Select each school and check perk states |
| 2012 | `MANAGE_QUEUE_PANEL8_OK` in wave12-reg2; Research queue frame has the shared three-channel overlay. Current VM explicitly records the newer no-activity-strip ruling. | All entry points/channel tabs and blocked-item routing |
| 2014 | `MANAGE_DUMB_VIEW_OK`, `MANAGE_ONE_HEADING_OK`, `MANAGE_ROW_BENEFIT_OK` in wave12-reg2; max detail has one MAX message. | Current detail copy and information density |
| 2015 | `MANAGE_PORTRAIT_COVERAGE_OK 72`, zero exemptions; Army/Research grids visibly use portraits and separate state words. | Final art cohesion against owner's target |
| 1314 | Payload reductions 5163f425c, remote heroes d706b430b, current Pi loader lifecycle/heap crumbs. | Seeker Pi startup and over 10-minute survival; OOM cause unproven |
| 1484 | `PerfReporter` memory/GC/scene instrumentation and Pi lifecycle crumbs delivered. | Controlled 15-minute same-scene Pi time series; native plateau does not prove Pi |
| 1665 | probeRB/guardRB diagnostics delivered; repository and known scratchpad logcats contain no discriminating readback. | Fresh object-ID/font readback; Part A cause not proven |

Historical Manage capture evidence: `Builds/wave10i-manageflow1`,
`MANAGE_FLOW_MAP_OK 20`, `UI_GEOMETRY_OK 20`, `UI_TOUCH_OK 20/20`.
Frames listed above were opened by the RCA reviewer. This reclassification uses
existing delivery evidence; these are not fresh captures of this iteration.

2016 remains implementation work: existing BUILD capture only has four tiles,
so content and viewport are both 451px and identical top/bottom images prove
no overflow traversal. 1291 remains content work: three approved mappings have
not been generated, and duplicate addresses require a scoped reconciliation
that preserves later Tripo watchtower rulings.

1244 has delivered diagnostics, but its older stored Fail would immediately
bounce a new Fixed status on every rebuild. Preserve the owner's finding while
repairing that retest cycle; never delete the finding just to clear Ready.

## Owner validation import

Imported `eoa-validations-20260911T011643Z.json` at the owner's request:
16 changed marks, 385 total. The following board rebuild closed 14 tickets and
reopened WO-1701 from Fail (2026-09-11T01:14:33, build 364108; no note).
Evidence: `Builds/ready-owner-validation-import.log` and
`Builds/ready-owner-validation-board.log`.

The owner additionally requested textured raid floors reaching the walls,
recorded as WO-1703. WO-1702 records the repeated-old-Fail retest defect.
Both are added to this effort; existing raid boundary/prop sign-offs remain closed.
