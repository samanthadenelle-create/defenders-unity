# Storage sizing audit: local review

Verdict: accept the audit as a useful diagnosis and conditional recommendation, not an executable fix. No further complete DeepSeek rewrite is needed. No code, catalog, art, scene, save, or occupancy changes were applied in this review. Unity checks are UNRUN for this review.

Input: `Builds/castle-validation-20260913/SizeAudit.md`.

## Checked against current source

- `StructureFactory.EffectiveVisualHeight` multiplies the base height by `heightMul`; `OptsFor` supplies that height and the per-row `maxFootprint`.
- `VisualFactory.Fit` uniformly multiplies scale by target height / measured renderer height. Flat geometry therefore becomes wide when fitted to a tall target. `CapFootprint` uniformly scales down after fitting.
- All three live catalog rows (`lumberyard`, `foundry`, `silo`) use `Structures/GenericContainer`, `heightMul=0.5`, manual identity orientation and scale 1. No explicit maxFootprint is authored. The orientation notes explicitly preserve the owner's flat KayKit pallet pose.
- The wrapper prefab references source GUID `3cf11469951e4da4b9ee72c0a264aa40`; its collider dimensions match the audit's quoted proxy dimensions. Collider dimensions do not establish renderer dimensions.
- `StructureCadenceRegression.TryMeasure` mirrors fitting; it does not call the production Skin method. C5 currently requires heightMul 0.5.
- `MeasureUprightFootprintXZ` caches by ID and orientation/scale, omitting heightMul/maxFootprint. This is a relevant tuning concern, not a newly reproduced runtime defect.
- Both current catalog copies have identical SHA256: `D3C48D6F2DA60E6D34A6EAE729A3A0CC3CA41E6766791CB300273A2E564B9088`.

## Corrections to DeepSeek's conclusions

1. Treat the exact pre-fit renderer dimensions and approximately 6.7 multiplier as inferred until captured from the actual Skin path. The 11.49 by 2.00 metre editor result supports the explanation; collider agreement does not prove runtime geometry or absence of other runtime effects.
2. Delete the unconditional statement that shrinking a claim is overlap-safe. Visual shrink alone does not verify occupancy anchors, grid rounding, cached claims, save replay, upgrades, or pending-art replacement. Require parity checks before any such claim.
3. "Both directions wrong" for heightMul is not a mathematical conclusion: lowering it reduces all dimensions. Keeping 0.5 is the current policy/regression constraint, not evidence that lowering it cannot shrink the model. Do not change that constraint incidentally.
4. A cap equal to the actual measured width is a no-op, and 11.49 is rounded. A useful cap must be finite, positive and below the actual width, with its target justified independently of the gate ceiling.
5. Owner target size is not the only decision-changing input. Actual renderer bounds, source resolution and production-path/occupancy parity can change whether the proposed cap is sufficient. Do not defer all of that as mere corroboration.

## Next local implementation brief

First collect evidence without changing sizing:

1. Use an isolated Unity test scene and existing loader/factory instrumentation; resolve each row through the production path. Wait for asset residency and reject pending-art proxies as measurement evidence.
2. Record source identity, complete renderer X/Y/Z bounds, root/visual transforms and options before fit, after fit/cap/seat, and after factory orientation scale. Preserve original pose and materials.
3. Record measured footprint, actual grid cell conversion and claims. Trace whether these rows use the hub injector; do not assume the generic factory covers that path.
4. Capture a labelled comparison against an approved visible size reference, ideally the saved pallet props measured read-only. A saved prop is a comparison candidate, not automatically the catalog sizing specification. Obtain an owner size choice from the comparison if prior authorization does not establish one.
5. If an owner-backed target supports a per-row cap, apply only that small catalog change to both copies and relevant generated data through the existing generation process. Preserve original art, identity orientation, and the saved castle layout.
6. Verify create/preview/reload/upgrade and occupancy parity, current cadence checks, the known misfit negative fixture, unrelated buildings, and failed asset resolution. Do not silently weaken thresholds or change Barracks movement.

Evidence capture, size selection and the conditional implementation remain outstanding. This document reports source review only; it does not claim the storage failure is fixed.
