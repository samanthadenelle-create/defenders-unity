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

## Delivered revisions and current work

- `18952982e`: imported owner findings and applied Fixed queue policy.
- `b65996d05`: Google email promo lookup/binding, verified by 147 root Node tests.
  Schema migration and production proof remain pending; WO-1698 is Fixed.
- `c8326f721`: explicit retest receipts preserve old findings while delivered work
  awaits a fresh test. Validation roundtrip passed; WO-1702 and WO-1244 are Fixed.
- `ea9a7201c`: WO-1697 pip bands meet the existing floor. Root compile passed,
  regression 507/507 and 12 skill-tree captures completed. Device proof pending.
- `100984ce9`: captured-base/AI/player arena architecture records owner direction.

Current root integration: WO-1291 scoped completion generated eight approved
wrappers and reduced Structure_Art from 72 entries/45 keys to 45/45, preserving
protected towers and catalog bytes. Remaining storefront/delivery proof is under
review. WO-2016 passes 44 oracle cases and 22 Manage capture geometry/touch checks;
its synthetic 16-tile stress fixture proves 738.1px overflow and zero endpoint gaps.
The category-first normal BUILD screen remains intentional under later owner rules.
WO-1695 now restores existing charge_knight art after renewed owner feedback; its
focused regression went RED on the placeholder and GREEN on the correction.
WO-1703 ground coverage/material regression went RED on actual missing coverage
and texture, then GREEN. First two attempts failed in test setup and are not defect
proof. Persisted scene/nav proof and rendered floor inspection remain pending.

## Additional phone findings and product direction

Pulled and opened `Builds/device-frames/owner-20260910/Screenshot_20260910-202900.png`.
Owner reported missing bottom icons, exterior wall holes and disconnected inner
keep/gate. WO-1695 covers the primary icon; three empty hot-swap slots are not
proven lost assignments. The pale capsule's producer remains unproven; do not
change game toast code on a device-overlay guess. WO-1704 records measured wall,
gate and inner assembly continuity work. Root RED captured seams, actual mesh
holes, absent inner assemblies and inadequate usable gate spans on real configs.

Owner established: save castle in Chapter One, raid, capture a base as it stands,
repair and redesign it CoC-style, then battle with that build against AI or players.
A modest one-time essential repair grant supports starting out; upgrades remain
earned. The grant amount is not chosen. Playable, resumable ownership/repair/design
FTUE belongs in this progression. Details and recommended implementation stages:
`docs/RAIDS_TO_PVP_ARCHITECTURE_2026-09-10.md`.

Owner also requested a Pi funding pitch deck adapted from the existing SKR deck,
while fixes continue. This is a parallel artifact task, not authorization to submit
an application, publish the deck or make unsupported traction/approval claims.

## Pi deck publication and test-platform clarification

Owner subsequently explicitly requested publication beside the SKR deck on Vercel.
Published site/pi-network-grant-deck.pdf and .pptx to the existing marketing project,
then moved echoes-of-elarion.vercel.app to deployment dpl_3Qz3uuGPKYkzG15j7qEtP41KgDqs.
Used site/README.md's documented site-only fallback: the web-ship dry run also
planned a WebGL deployment outside this request. No WebGL deployment performed.
Both public Pi files returned HTTP 200 and exact local SHA256; existing SKR PDF
also matched local bytes and the marketing landing retained its dApp Store link.
Public links: https://echoes-of-elarion.vercel.app/pi-network-grant-deck.pdf
and https://echoes-of-elarion.vercel.app/pi-network-grant-deck.pptx.

Owner clarified the last test was SKR-facing on their phone, used first because
WebGL builds take longer. Treat the imported Fail as Android evidence, not proof
that WO-1701's original WebGL-only loading exception recurred. Existing native
screenshots show armored Knight; the expected hero and exact reported symptom
remain undetermined. Keep original WebGL acceptance separately pending.
