# Collector lifecycle audit: local review

Verdict: useful lifecycle scenarios, but the proposed repair is not accepted as-is. No collector, economy, persistence or registry implementation was changed. The collector scenarios remain UNRUN.

## Additional source evidence

- A search of all `Assets/**/*.cs` for `HarvestSourceRegistry` found only its declaration and the ResourceCollector register/unregister calls. No direct source-code consumer of `HarvestSourceRegistry.Active` was found. This does not exclude reflection/external code, but it removes the supplied audit's unverified list-reader payout premise from the known runtime paths.
- `ResourceBuildingHarvester` obtains its collector through `ResourceCollectorRegistry.Get(id)` before calling `Accrue` (around lines 179 and 233).
- `ResourceCollectorService` aggregates and collects from `ResourceCollectorRegistry.All`. Duplicate membership in the other registry alone does not establish a duplicate wallet credit.
- `ResourceCollectorBootstrap.NotifyCollectorConfigured` parks an active DDOL fallback through `ParkWithoutPersisting` when the real placed collector takes ownership. That real lifecycle step was omitted from the audit's abstract two-instance account.
- `ResourceCollector.OnDisable` deliberately saves only the registry owner and respects the suppression flag. `Configure` handles AddComponent registering a default farm ID before the factory supplies the final ID; the temporary default is not automatically an independently owned state that should be saved.

## Corrections

Do not change a generic IHarvestSource registry into a BuildingId registry without establishing its interface contract and a real consumer defect. It is not enough to infer a payout route from list membership.

Do not unconditionally call SaveState before Configure. A displaced/default-ID snapshot can overwrite the current owner's newer state. A legitimate rekey of an established owner and the initial default-ID setup are different cases; any repair must distinguish them and prove which state is authoritative.

The absence of town/player suffixes in the shown PlayerPrefs key strings is a source fact, not proof of an actual cross-town exploit or missing external save isolation. Likewise a list of possible two-instance sequences does not establish that the production instantiation path creates those states.

## Bounded next validation

Use isolated, uniquely named test persistence keys with restore/cleanup. Test actual factory Configure plus bootstrap fallback parking, repeated Configure on the same started owner, default-ID displacement/restoration, and rekey onto an occupied ID. Record registry owner, live/parked state, pending, timestamp and wallet deltas at every step. Include save/load and new-game reset through real authority entry points; never reset the user's save to test this.

If a test demonstrates loss or duplicate grant, repair that ownership transition with a narrow regression. Until then, retain the owner-only disable save and fallback parking protections. No further full DeepSeek rewrite is needed.

Separate completed work: `FOOTPRINT_CACHE_LOCAL_RESULT.md` records reproduced footprint defects, the local fix and passing focused/castle checks. It does not claim the collector scenarios or storage sizing are complete.
