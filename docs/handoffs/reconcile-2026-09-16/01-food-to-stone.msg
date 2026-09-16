refactor(economy): WO-1163 Food-to-Stone - one Stone identity over the frozen internal Food save slot

The internal Food resource becomes player-facing Stone across the whole economy surface: wallet, collectors, offline harvest, raid and wave payouts, build and upgrade baskets, quests, packs, troop and gear catalogs, plus every regression that asserts on those shapes. The SAVE WIRE DOES NOT MOVE - the persisted key stays "Food" behind [JsonProperty("Food")] / [FormerlySerializedAs("Food")] / [EnumMember(Value = "Food")] and the api/ whitelists keep the food column, so existing saves and the Neon backend load unchanged; only the C# member and the displayed word are Stone. Carries the migration scripts (tools/migrate-*.py, tools/inventory-food-migration.py) and docs/food-migration/ for the record.

Gated: <FILL - COMPILE_GATE_OK + REGRESSION_OK n/n, log path, HH:MM, run on this exact tree before any commit in this sequence>
