# Retire Food from active resource contracts; make Stone the real name

**Status:** IN PROGRESS — owner authorized 2026-09-11. Inventory begun; implementation not complete. Separate migration, outside the six-ticket 2026-09-13 night scope; status syntax normalized so the board can report it honestly.

## Outcome

Food is retired. Runtime balances, rewards, spending, method names, UI, content, tools and backend contracts must consistently describe Stone. The earlier WO-1212 decision to keep `Resources.Food` as the runtime Stone name is superseded by this owner instruction. Its protection against creating a second balance remains mandatory.

This is a migration, not a reward rebalance. Preserve legitimate Stone balances and existing reward amounts unless a reference is proven to represent obsolete Food gameplay. Do not turn every food-themed item or unrelated third-party identifier into Stone automatically.

## Inventory and execution pointer

- Baseline: `docs/food-migration/baseline.csv`, one occurrence per row with file, line, byte column and source context.
- Reproduce: `python tools/inventory-food-migration.py`. Later runs write `current.csv` without overwriting the baseline review ledger.
- Scope: active first-party source, serialized scenes/prefabs/assets, data, localization, API/server, tools and tests. Generated caches/build outputs are excluded. Historical work orders remain historical evidence.
- CURRENT POINTER: wallet, save-cost baskets, economy APIs, raid reward type and six resource enums migrated; expanded integrated proof passed 2026-09-11 13:51:43 (Builds/stone-enums-harvest-validated.log, zero compiler errors). ResourceBalance.Food source bridge removed. Next: separate harvest/reward/UI DTOs, residual authored content and explicit compatibility ledger. Full release proof and artifacts remain outstanding. See docs/food-migration/SAVE_FIRST.md for evidence and exceptions.
- Set each baseline row to REVIEWED with disposition RENAME, MIGRATE, COMPATIBILITY or UNRELATED and a reason/evidence. Pointer IDs identify this frozen baseline; line numbers will move after edits. Reconcile subsequent scans using path, symbol and source context, not IDs alone.
- The text scan is discovery, not a semantic call graph. For each definition, use symbol references where available and exact identifier searches otherwise; inspect overloads, extensions, delegates, reflection strings, serialized names and non-C# consumers. Compile to catch broken callers.

## Ordered slices

1. Map authoritative definitions and all callers: `Core/State/NestedTypes.cs` ResourceBalance; `Core/Catalog/RepoProps.cs` ResourceCost; `Village/EconomyService.cs` ResourceCost and economy API; `Village/Buildings/Progression/ResourceBuildingProgression.cs` ResourceCost; `Core/ResourceType.cs`; `Core/UI/ElarionUiKitObsidian.cs` CurrencyKind. These are different types, not one interchangeable symbol.
2. Specify save/wire migration before renaming fields. Inspect SaveSchema, GameStateService, backend save/load, cloud merge, receipts, PlayerPrefs and signed payloads. Distinguish nested resource `food` (real Stone) from the retired independent top-level `stone` field. Never sum them or revive the retired balance. Define old/new/both-key precedence and schema/version handling explicitly. Preserve enum numeric values and Unity serialized values. Coordinate backend rollout before clients emit incompatible keys; do not deploy merely to complete a local rename.
3. Rename runtime definitions and every caller in coherent compilable slices: getters, AddFood-style grants, costs, caps, harvest, upgrades, repair, raid rewards, overflow, purchases, development tools and diagnostics. Centralize legacy decoding at boundaries instead of retaining Food-named runtime aliases indefinitely.
4. Migrate authored data, localization keys and all locale tables, icons, scene/prefab field names and Unity serialized aliases. Audit frozen SKU/catalog IDs separately: compatibility identity is not player-facing vocabulary. Use Unity tooling for serialized scene changes. Preserve approved Iron Bastion geometry.
5. Verify old local/cloud saves retain exact balances; repeated migration is idempotent; both-key payloads do not double credit; malformed/negative values follow existing validation; enum values survive; purchases, grants, costs, caps and overflow all reach the same Stone balance. Verify new save/reload and supported old-client/server interoperability.
6. Regenerate inventory. Zero unreviewed active references; every retained Food token has a narrow compatibility or unrelated rationale. Add a regression check against new Food gameplay identifiers/copy, with explicit exceptions. Compile affected assemblies, run relevant economy/save/content suites, then full release gates. Build fresh tester artifacts only after passing; existing Windows EXE is unchanged by this planning step.

## Completion evidence

Latest checkpoint (2026-09-11 14:10): save, economy, enum, reward/harvest/UI DTO and compound member migrations pass the focused integrated suite. Temporary runtime Food aliases are removed; old wire keys remain explicit compatibility boundaries. Historical dungeon stash keeps LegacyFood separately to avoid altering old payloads. See docs/food-migration/SAVE_FIRST.md and compiler/compound JSON ledgers for exact pointers. Current raw scan: 1,901 occurrences / 299 files; residual review and full release validation remain OPEN. Earlier checkpoints below describe their state at that time.

Reviewed occurrence ledger; definition-to-caller migration record; explicit wire compatibility contract; passing migration/economy/content tests; final residual scan with documented exceptions; runtime reward/cost/HUD Stone proof. Do not close because the UI says Stone while active economy APIs still say Food.

## Save-first contract and checkpoint

Owner priority: touch save logic first. Baseline contains 2,811 occurrences across 340 files; this is a raw discovery count, not 2,811 proven defects.

First slice deliberately preserves the currently deployed JSON contract `resources.food`. Its C# storage field is now Stone, with `FormerlySerializedAs("Food")` for Unity data. The temporary Food source property has JsonIgnore and no independent storage. No schema bump is needed for this wire-identical slice. The retired top-level `stone` precedence is unchanged: a resources block wins; no summing. Backend wire renaming, nested costs/receipts and removal of the source bridge remain OPEN.

Verification entry: `DeNelle.Editor.StoneSaveMigrationProof.RunBatch` checks old JSON balances, one stored balance, repeated round trips, Unity old-field import and the core save contract. Terminal PASS at 12:59 on 2026-09-11: `Builds/stone-save-migration-retry.log`, marker `STONE_SAVE_MIGRATION_OK`, zero compiler errors. Initial JsonUtility-based fixture failed; a real Unity serialized asset verifies the actual asset migration (details in `docs/food-migration/SAVE_FIRST.md`). Existing Unity lifecycle reload/shutdown exceptions remain. Existing Windows tester has not been rebuilt. Full Food retirement is still OPEN.
