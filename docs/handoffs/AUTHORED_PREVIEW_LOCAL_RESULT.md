# Local authored-preview implementation and raid-test correction

Local work performed after reviewing DeepSeek replies 1–4 and the implementation brief. No commits, pushes, deployments, or external messages were made.

## Implemented

`Assets/_Modules/Village/BuildMode/GhostPreview.cs` now exposes `SetAuthoredEntry(Transform)` and `MoveToAuthored(Vector3, Quaternion)`. The snapshot renders the selected root/descendants only, with render-only ancestor transforms preserving nonuniform scale and rotation. The owned root is detached in the host's scene so host scaling cannot distort it. Source meshes and texture inputs are retained; owned transparent URP/Lit material copies receive the existing valid/blocked tint.

Entry explicitly refreshes a snapshot. Refresh of the same source retains candidate pose, hidden state, validity, and reason. Captured hierarchy/visibility/mesh/material-reference changes invalidate it until refreshed. Source disappearance, host disable, Clear, and destruction clean up owned geometry/materials. Candidate pose updates do not create meshes or materials. Invalid entry preserves the prior preview; invalid finite/degenerate quaternion input is refused before pose mutation. Unsupported shaders, material overrides, and non-static renderers are refused explicitly.

**This does not enable Barracks movement.** The move refusal guard, occupancy, save schema, assets, and saved layout were not changed. Movement/occupancy integration is a separate task. Material property or in-place mesh edits require explicit entry refresh; this is a snapshot, not a general live-cloning system.

`Assets/Editor/Regression/AuthoredGhostPreviewRegression.cs` is a standalone executable regression. It tests independent reference hierarchy vertices at multiple translated/rotated poses, transformed host, ancestor mesh exclusion, source identity, textures/tints, invalid inputs, explicit refresh, invalidation, owned cleanup, and the actual catalog SetEntry transition. It also opens the saved castle as a preview scene, verifies the real Barracks mesh and pose, captures source/valid/blocked art, and verifies the saved scene bytes remain unchanged.

The useful logic from `Raid1DeePSeek.md` was implemented as a small C3-only correction in `RaidSelectionSpoilsRegression.cs`, plus a standalone entry. It resolves every required live catalog row before interpreting thresholds, tests both announcement producers with an independent expected result, includes negative/zero/historical/boundary counts, and reports actual coverage. No raid gameplay, JSON thresholds, or progression changed.

## Actual verification

- `Builds/castle-preview-local-20260914-third.log`: **AUTHORED_GHOST_PREVIEW_OK**, zero C# errors. Exact geometry, textures/tints, refresh/refusal, cleanup, catalog transition, and actual saved Barracks captures passed.
- `Builds/castle-preview-local-20260914-castle-guardrails.log`: **OWNER_CASTLE_FINAL_CHECKS_OK**, zero C# errors. Original assets/layout/builder/injector, Barracks save/reload/state replacement, store, and captured-town reconstruction remain green.
- `Builds/raid-selection-local-20260914.log`: **RAID_SELECTION_SPOILS_OK**, zero C# errors. C3 resolved 4/4 rows, 0 configured positive rungs, 16 silent probes, 6 next-target probes, and **38 producer assertions**. No positive announcement integration is claimed for all-zero authoring.
- `Builds/castle-preview-local-20260914-compile.log`: **COMPILE_GATE_OK** for the active target. WebGL emitted **COMPILE_GATE_WEBGL_SKIPPED reason=package-reference-gap** after 12 package-reference error lines; no WebGL pass is claimed.

The first preview run stopped at an Edit Mode callback-dispatch fixture assumption. The test now explicitly invokes the real OnDisable body and labels that limitation. The second run caught an editor-assembly dependency introduced by the capture helper; reflection now reuses the existing capture tool without a circular reference. Neither failed run is counted as passing evidence.

All preview verification above is **Edit Mode**, not a Play Mode/device movement or lifecycle acceptance. The full 514-suite regression was not rerun during this pass; do not infer a new full-suite result from focused checks.

## Visual evidence opened

- `Builds/castle-validation-20260913/AuthoredPreview_saved_source.png`: original textured Barracks.
- `Builds/castle-validation-20260913/AuthoredPreview_saved_valid.png`: the same geometry and textures with translucent valid-placement tint.
- `Builds/castle-validation-20260913/AuthoredPreview_saved_blocked.png`: same geometry with blocked-placement tint.

All three captures were opened and inspected. These inspection renders do not certify scene lighting, navigation, device performance, or movement UI.

## Next independent assistance

The storage-sizing audit is a separate evidence-only DeepSeek handoff. Lumberyard/Foundry/Silo remain the known sizing finding from the earlier full regression. It must distinguish approved flat-pallet geometry from an inappropriate generic threshold before proposing changes; no broad art replacement, rotation reset, or arbitrary shrink is authorized by that audit.
