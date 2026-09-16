# Sword sheath on the main-hand hip

**2026-09-13 — implemented and verified by focused Unity regression and fresh play-mode captures. Full batch regression/release gates remain with the root.**

Owner direction: "it should sit opposite hip of the shield (if shield is offhand the sword should sit hilt up edge down on main hand hip". Only the sheathed sword is in scope; the drawn/in-hand pose remains approved.

Source before the change: `EquipmentController.SheatheSideMain` is `-1f`, putting the main weapon on the left hip. Both `ApplyHoldPose` and `ApplySheathedSeatingPreview` use it. Tip direction is already derived per mesh; a global sign flip would break other assets. Staff and bow also use the old side, so changing that constant globally would exceed the sword-only instruction.

Images opened: historical `logs/device/sheathed-weapon.png` and `logs/device/sheathe-retry.png`, then the fresh baseline front, hip-closeup and drawn weapon-hand closeup. The baseline shows the sword rising beside/inside the shield-side torso, while the drawn sword remains seated at the grip. Root archived all baseline images and summary under `Builds/night-sword-before/`. The after images live under `Builds/KnightGearProof/`; this lane opened after `04_SHEATHED_front34.png` and `06_SHEATHED_hips_closeup.png`, confirming the sword on the opposite/right side with hilt up and blade down. Root also inspected after 01, 05 and 07.

`Builds/night-sword-seat-before.log` supplies the cause: the live starter mesh is `Sword1h_01`, using its authored native grip origin. The taper rule picked hilt at +Y from end widths 0.01679/0.0263 and returned +1. The independent capture measured the grip origin 0.115 m from the low end versus 0.535 m from the high end, so the hilt is the low end. Actual world endpoints were hilt.y=0.758, tip.y=1.473, with centre side=-0.141 m along body.right. This proves both wrong side and tip-up; merely moving the hip would leave the inversion wrong. No sword_A@sheathed override was present, and the drawn offset's rotation was skipped on sheathe.

Changes: `MainHandSheatheSide()` places Sword on the right/main-hand hip in both runtime and preview while preserving other families' side. `TryResolveSheathedTipSign` has an optional authored-grip preference; only native Sword sheathing with grip inference off passes it. A decisive grip-origin measurement outranks taper for that contract; a centred origin retains the old taper/ambiguity fallback. Drawn grip calculation, authored pose rows, blade sign fallback, and shield production code are unchanged. The sheath diagnostic uses actual main-slot identity rather than mistaking a positive hip side for the off-hand slot.

`SheathePoseRegression` now drives the captured starter mesh and its mirror through production hold-pose and preview methods, measures transformed mesh-bound corners and hilt/tip endpoints, checks main-hand hip plus blade-down, checks preview parity and restoration of the drawn pose, and preserves staff/bow side. Root executed it: `Builds/night-sword-focused.log:567` records the new native/mirror case and `:606` records `SHEATHE_POSE_OK`.

`KnightGearProofCapture.Run` is the existing real play-mode capture chain. Its original safety snapshot covered only five knight gear preferences while `ChooseHero` could write the main save. The harness now installs an in-memory `ISaveProvider` and cloud suppression at SubsystemRegistration, refuses to choose a hero without isolation, and checks that the original main save is unchanged. It keeps the isolated provider through teardown and restores the prior provider after leaving Play mode. The gear preference restore remains in place. This safety edit does not alter the sword pose.

The baseline also exposed obsolete shield checks: the harness demanded a parent name containing ArmOff, but the live parent `Socket_Shield` is under `CC_Base_L_Forearm`. `docs/MASTER_CATALOG/village-hero.md` records the 2026-08-30 locked heater's exact authored pose and the same forearm parent in town and combat. The harness now asserts actual LeftLowerArm ancestry and equal parent/local position/rotation/scale across both states, rather than applying the sword's vertical-at-hip rule to a strapped shield. It additionally requires the measured sword and shield centres on opposite right/left body sides. No shield transform was changed to satisfy a test.

Root execution: use `tools/run-unity-playmode.ps1` with method `DeNelle.Editor.KnightGearProofCapture.Run`, no `-quit` and no `-nographics`. Read `Builds/KnightGearProof/_summary.txt` and open drawn, sheathed and hip-closeup PNGs. Preserve the baseline before rerunning. The post-change harness asserts the latest rule using the actual rendered mesh's hilt and centre relative to hips/body.right and the measured blade vector relative to -body.up, independent of the production offset/sign constants.

Fresh validation: `Builds/night-sword-seat-after.log:1824` reports `KNIGHT_GEAR_PROOF_OK 9`; the summary has zero failures, and `:1820` proves the original main save remained unchanged. Post-edit C# brace gate passed for all four touched source files, no NUL bytes, diff check clean. No scene or asset offset file was edited.

| Measured sheath result | Before | After |
|---|---|---|
| Hilt world position | (-0.145, 0.758, -0.016) | (0.147, 1.010, -0.094) |
| Tip world position | (-0.141, 1.473, -0.014) | (0.143, 0.295, -0.094) |
| Blade angle from world up | 0.4 degrees, tip up | 179.7 degrees, tip down |
| Sword centre side along body.right | -0.141 m, left | +0.157 m, right |
| Hilt side along body.right | Not separately logged | +0.158 m |
| Blade angle from -body.up | Not separately logged | 0.3 degrees |
| Sword/shield centre separation | 0.172 m | 0.472 m |

Drawn preservation is supported by the regression and measured local transforms, **not pixel equality**. Before/after `SEAT CHAIN [DRAWN]` both log local Euler `(272.2,65.4,92.8)`, position `(0.01,0.03,-0.01)`, scale `(0.66,0.66,0.66)`, and parent `CC_Base_R_Hand`. These are identical at logged precision. Both independent capture summaries give hand projection 0.586 along the grip, off-axis 0.051 m, hilt distance 0.1568 m and tip distance 0.5689 m. World hand Euler differs between captures: `(343.6,2.8,197.6)` before versus `(343.7,3.2,198.1)` after; world sword endpoints also vary slightly. SHA-256 comparison found all four drawn PNG pairs different. Full hashes and the extracted transform trace lines are preserved in `Builds/night-sword-drawn-comparison.txt`; no byte-identical or pixel-identical image claim is made.

This evidence closes the focused sheath correction. It does not claim a full combined regression, platform build, deployment, or owner device acceptance; those remain the root's release gates. WO-1701 hero readiness is a separate lane and remains pending its full gate.
