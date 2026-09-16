# Footprint cache: local implementation result

The DeepSeek source audit led to reproduced defects and a bounded local repair in `Assets/_Modules/Village/Catalog/StructureFactory.cs`. No art, catalog sizing values, scene, save schema, grid implementation or authored Barracks movement guard was changed.

## Evidence

`Assets/Editor/Regression/FootprintCacheRegression.cs` exercises the production resident loader, measurement and Create paths with owned asymmetric geometry in an isolated EditMode fixture. A unique known-absent address prevents real network requests; inserting the fixture into the resident dictionary simulates availability. This proves the synchronous recovery path, not real download timing.

- `Builds/footprint-cache-before.log`: first fixture attempt could not add an additive scene beside an unsaved untitled scene. Fixed fixture setup to reuse only a pristine empty batch scene or create an additive scene beside a saved scene. No user scene was discarded.
- `Builds/footprint-cache-baseline.log`: eight failures captured before the runtime edit. Rotation measured approximately (2,8) versus Create (7.071067,7.071068); late residency stayed at fallback (3,3) versus actual (8,2). Height, cap, source address, preserve-rotation, sub-rounding scale and resident-object replacement also returned stale results.
- `Builds/footprint-cache-after.log`: fresh `FOOTPRINT_CACHE_OK`, wrapper PASS, zero C# errors. All ten labelled comparisons pass; the existing-grid occupancy assertion also passes.

## Repair

Resolve the prefab before checking the successful-measurement cache. Missing art returns the authored fallback without creating a temporary probe or writing a successful cache entry. The loader retains its existing deduplicated request behavior.

Use an exact typed key containing catalog ID, address, resolved prefab identity, effective height, cap, rotation-preservation policy, manual flag, full-precision Euler and effective scale. Apply manual Euler once through OptsFor/Skin, as Create does. Cache only successful finite positive bounds; failed measurements remain retryable.

The authored fallback is not part of the key because it is never cached. Translation offset does not alter bounds size. Arbitrary in-place mesh edits on the same prefab object are not versioned by this change.

## Scope and remaining limits

- Existing grid cells do not mutate merely because a measurement refreshes. This does not certify a save migration or automatic reconciliation after late art replacement. Future placement/replay can now use corrected measurements; representative saved-layout validation remains necessary.
- The focused fixture is EditMode. Live player download, domain-reload settings and device timing are unverified.
- Repeated unresolved art calls do not instantiate probes. A resolved but persistently unrenderable prefab can still retry probe creation; a negative-cache policy requires separate profiling rather than an invented timeout in this patch.
- No claim that storage sizing is fixed: its owner-backed dimensions and production measurements remain a separate task.
- `Builds/footprint-cache-castle-guardrails.log`: fresh `OWNER_CASTLE_FINAL_CHECKS_OK`, wrapper PASS, zero C# errors. Original assets, scene/builder/injector, authored Barracks reload/state replacement, store and captured-town reconstruction checks remain green. This is focused structural coverage, not a full player save-migration certification.

Next independent DeepSeek packet: `docs/handoffs/DEEPSEEK_COLLECTOR_LIFECYCLE_AUDIT.md`, also copied to the validation Build folder. It reviews collector identity/reload/payout behavior; it authorizes no implementation.
