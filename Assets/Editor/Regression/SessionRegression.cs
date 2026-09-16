// =============================================================================
// SessionRegression — guards THIS session's fixes so they can't silently regress.
// -----------------------------------------------------------------------------
// Companion to DataRegression.cs (same DeNelle.EditorRegression asmdef, same
// headless "pass the REAL object in, assert the REAL response" pattern). Where
// DataRegression guards the catalog->object mapping, this suite guards the
// SESSION'S INVARIANTS as pure DATA/LOGIC checks — no scene, no Play mode — so a
// silent regression in any of them becomes a hard REGRESSION_FAIL line instead of
// a player-facing bug with no error.
//
// Runs in batchmode (Unity closed) via:
//   run-unity-method.ps1 -Method DeNelle.Editor.SessionRegression.RunAll -LogName session-regression.log
//
// Prints a single authoritative marker:
//   SESSION_GUARDS_OK <pass>/<total> checks  /  SESSION_GUARDS_FAIL: <n> failure(s)
//
// COUNT NOTE (WO-1493, 2026-09-06): that marker used to read the string literal
// "SESSION_GUARDS_OK 6/6 checks" — a LABEL, not a MEASUREMENT. It would have printed
// 6/6 whether six checks ran, one ran, or none did, which is exactly the
// `gates-report-success-without-proving-it` class (audit G8 in
// RegressionMarkerRegression names it by name). The checks are now a TABLE
// (`Checks` below) that RunAll iterates; both numbers in the marker are derived from
// that table and from which entries added no failure. Adding or removing a check
// moves the marker with no edit to the format string. ⛔ Never write a digit back
// into that string — RegressionMarkerRegression RULE 6 [gate-marker-count] fails if
// you do.
//
// MARKER NOTE (2026-08-02): this class used to emit a bare `REGRESSION_OK`, the same
// literal DataRegression.RunAll and the legacy Assets/Editor/RegressionSuite.cs emitted.
// Because the project judges by MARKER (never exit code), a log carrying REGRESSION_OK
// did not say WHICH suite produced it — a 6-check pass read as a ~90-suite pass. The
// markers are now disjoint AND deliberately do not contain "REGRESSION_OK" as a
// substring, so a grep for the real gate can never be satisfied by this suite.
// Enforced by RegressionMarkerRegression [regression-marker].
//
// CHECKS (each fix it guards):
//   1. VENDOR CONTRACT  — VendorStockContract.AllowedFor maps each vendor context
//      to the expected GearKind (forge->Weapon, armorer->Armor, market->Potion,
//      jeweler->Armor, unknown->all). Guards WO-444 (armorer-sold-weapons regress).
//   2. STARTER WEAPONS  — GearCatalog.BestWeapon(job,1) is non-null for every
//      class (mage/knight/ranger/cleric). Guards WO-425 (a class spawns weaponless).
//   3. ENEMY MODELS     — every catalog enemy id resolves via EnemyFactory.ModelForEnemy
//      to a prefab Resources.Load finds (no id falls to the tinted-capsule fallback).
//      Guards the enemy-variety / #22 archer->lumber class.
//   4. STRUCTURE PREFABS— every structures-catalog visualPrefabPath resolves. Guards #22.
//   5. SAVE ROUND-TRIP  — a GameState payload with an owned+named pet, a claimed
//      settlement and economy values runs through the REAL save path
//      (JsonConvert + SaveSchema.JsonSettings -> deserialize -> SaveSchema.Validate)
//      and pet/settlement/economy/pet-name all survive. Guards WO-159 +
//      pet-persist + the PetName audit finding (see the AUDIT NOTE in CheckSaveRoundTrip).
//   6. VENDOR STOCK NON-EMPTY — a general vendor's resolved stock (weapons+armors
//      via the contract) is non-empty.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Village;
using DeNelle.Core.State;
using DeNelle.Core.World;

namespace DeNelle.Editor
{
    public static class SessionRegression
    {
        /// <summary>One session guard: a name for the log, and the check itself.</summary>
        private sealed class SessionCheck
        {
            public readonly string Name;
            public readonly Action<List<string>, StringBuilder> Run;
            public SessionCheck(string name, Action<List<string>, StringBuilder> run) { Name = name; Run = run; }
        }

        // THE TABLE IS THE COUNT. RunAll iterates this and derives both halves of the
        // marker from it — the denominator is Checks.Length, the numerator is how many
        // entries returned without appending a failure. See the COUNT NOTE in the header.
        private static readonly SessionCheck[] Checks =
        {
            new SessionCheck("vendor-contract",     CheckVendorContract),     // WO-444
            new SessionCheck("starter-weapons",     CheckStarterWeapons),     // WO-425
            new SessionCheck("enemy-models",        CheckEnemyModels),        // #22 class
            new SessionCheck("structure-prefabs",   CheckStructurePrefabs),   // #22 class
            new SessionCheck("save-round-trip",     CheckSaveRoundTrip),      // WO-159 + pet-persist + PetName audit
            new SessionCheck("general-vendor-stock", CheckGeneralVendorStock),
        };

        public static void RunAll()
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== SessionRegression: guards this session's fixes (data/logic, headless) ===");

            int total = Checks.Length;
            int passed = 0;
            foreach (var check in Checks)
            {
                int before = failures.Count;
                try
                {
                    check.Run(failures, log);
                }
                catch (System.Exception ex)
                {
                    // Never swallow (CLAUDE.md sec.12): a throwing check is a RED check, and it
                    // must still leave the denominator intact so the marker cannot shrink quietly.
                    failures.Add($"check '{check.Name}' THREW {ex.GetType().Name}: {ex.Message}");
                }
                if (failures.Count == before) passed++;
                log.AppendLine($"  -- check '{check.Name}': {(failures.Count == before ? "OK" : "RED")}");
            }

            log.AppendLine("=== verdict ===");
            if (failures.Count == 0)
            {
                // Both numbers MEASURED, never typed. Do not inline a digit here.
                log.AppendLine($"SESSION_GUARDS_OK {passed}/{total} checks");
                Debug.Log(log.ToString());
            }
            else
            {
                log.AppendLine($"SESSION_GUARDS_FAIL: {failures.Count} failure(s) across {total - passed}/{total} check(s):");
                foreach (var f in failures) log.AppendLine("  - " + f);
                // LogError so it also lands in break-log.jsonl and fails loudly in the log scan.
                Debug.LogError(log.ToString());
            }
        }

        // =====================================================================
        //  1. VENDOR CONTRACT — VendorStockContract.AllowedFor (guards WO-444)
        // =====================================================================
        // The single source of truth for what each store TYPE sells. The regressed
        // bug was the armorer selling weapons + the marketplace selling swords; this
        // asserts the mapping AllowedFor declares for each canonical vendor context.
        private static void CheckVendorContract(List<string> failures, StringBuilder log)
        {
            // (context, expected GearKind). Mirrors VendorStockContract's documented intent.
            // WO-598: registered vendors (vendors.json) derive their kinds from the registry
            // (forge = weapon+armor; market = consumables+materials; jeweler = accessories+gems);
            // unregistered contexts keep the WO-444 heuristic (blacksmith=Armor per the owner's
            // 2026-06-13 ruling — the old Weapon expectation here was stale).
            VendorRegistry.Reload();
            var cases = new (string ctx, GearKind expect)[]
            {
                // ── RE-POINTED 2026-08-23 (WO-1161/1163). NOT softened — read why. ──
                // The old rows below expected a SUBSTRING HEURISTIC ("blacksmith"/"smith"/
                // "armor"/"armory") that has been retired. That heuristic is the mechanism
                // that collapsed `forge` and `armorer` onto one another and produced the
                // crossed cluster this whole lane exists to end: matching a vendor by a
                // fragment of a WORD cannot distinguish two shops whose words were swapped.
                // Identity now resolves by catalog id -> role, and a context that is neither
                // a catalog id nor a vendors.json id is UNKNOWN — which must fall to the safe
                // general default, never to a guess.
                //
                // ⚠ `("forge", Weapon|Armor)` WAS ALREADY STALE BEFORE THIS CHANGE, and that is
                // worth keeping on the record: `vendors.json` authors `forge` with
                // categories:["weapon"], so the registry has been returning Weapon alone while
                // this row claimed Weapon|Armor. The case was passing for a reason unrelated to
                // what it asserted. Corrected to the authored truth.
                ("forge",       GearKind.Weapon),                           // registry: vendors.json categories ["weapon"]
                ("armorer",     GearKind.Armor),                            // registry (armor-only) — a REAL catalog id, still resolves
                // The four retired-heuristic contexts. These assert the RULED behaviour
                // (unknown -> safe general default); they are kept, not deleted, so that a
                // future seat re-introducing substring matching turns this suite RED.
                ("blacksmith",  GearKind.Weapon | GearKind.Armor | GearKind.Potion),
                ("smith",       GearKind.Weapon | GearKind.Armor | GearKind.Potion),
                ("armor",       GearKind.Weapon | GearKind.Armor | GearKind.Potion),
                ("armory",      GearKind.Weapon | GearKind.Armor | GearKind.Potion),
                ("jeweler",     GearKind.Accessory | GearKind.Material),    // registry (rings/amulets + gems, WO-543/598)
                ("market",      GearKind.Potion | GearKind.Material),       // registry (consumables + materials)
                ("marketplace", GearKind.Potion | GearKind.Material),       // substring-matches the market row
                ("trader",      GearKind.Potion),                           // heuristic
                ("granary",     GearKind.Potion),                           // heuristic
                // unknown / empty -> safe general default (never broken / never empty)
                ("",            GearKind.Weapon | GearKind.Armor | GearKind.Potion),
                ("totally_unknown_vendor", GearKind.Weapon | GearKind.Armor | GearKind.Potion),
            };

            foreach (var c in cases)
            {
                GearKind got = VendorStockContract.AllowedFor(c.ctx);
                if (got != c.expect)
                    failures.Add($"VendorStockContract.AllowedFor(\"{c.ctx}\") = {got}, expected {c.expect} (WO-444 vendor-stock contract regressed)");
                log.AppendLine($"  VC '{c.ctx}' -> {got} (expect {c.expect})");
            }
        }

        // =====================================================================
        //  2. STARTER WEAPONS — GearCatalog.BestWeapon(job,1) per class (WO-425)
        // =====================================================================
        // Every playable class must have a level-1 equippable weapon, or that hero
        // spawns weaponless (the WO-425 fix). We force a fresh catalog read and probe
        // each job string the catalog is keyed by.
        private static void CheckStarterWeapons(List<string> failures, StringBuilder log)
        {
            GearCatalog.Reload();
            string[] jobs = { "mage", "knight", "ranger", "cleric" };
            foreach (var job in jobs)
            {
                var w = GearCatalog.BestWeapon(job, 1);
                if (w == null || string.IsNullOrEmpty(w.id))
                {
                    failures.Add($"GearCatalog.BestWeapon(\"{job}\", 1) is NULL — class '{job}' has no level-1 starter weapon (WO-425 regressed)");
                    log.AppendLine($"  SW [{job}] -> <none>  ***MISSING***");
                }
                else
                {
                    log.AppendLine($"  SW [{job}] -> '{w.id}' ('{w.name}')");
                }
            }
        }

        // =====================================================================
        //  3. ENEMY MODELS — every spawnable enemy resolves to a real prefab (#22)
        // =====================================================================
        // DataRegression also walks the enemy catalog; here the assertion is framed
        // as the session invariant: NO catalog enemy id falls to the tinted-capsule
        // fallback (EnemyFactory.ModelForEnemy -> Resources.Load<GameObject> NULL).
        private static void CheckEnemyModels(List<string> failures, StringBuilder log)
        {
            string json = DeNelle.Core.CanonicalJson.Read(WaveDataLoader.EnemiesRelativePath);
            EnemyCatalog catalog = null;
            if (!string.IsNullOrEmpty(json))
            {
                try { catalog = JsonConvert.DeserializeObject<EnemyCatalog>(json); }
                catch (System.Exception ex)
                {
                    failures.Add($"enemies.json failed to parse: {ex.Message}");
                    log.AppendLine($"  EN parse error: {ex.Message}");
                    return;
                }
            }

            if (catalog == null || catalog.Enemies == null || catalog.Enemies.Count == 0)
            {
                failures.Add("enemies.json deserialized to 0 EnemyDef objects (enemy variety would be empty)");
                log.AppendLine("  EN -> 0 enemies");
                return;
            }

            int missing = 0;
            foreach (var e in catalog.Enemies)
            {
                // Skip the schema-doc placeholder row (id carries a space — it's the
                // field description, not a real enemy). Same conservative skip DataRegression uses.
                if (e == null || string.IsNullOrEmpty(e.Id) || e.Id.Contains(" ")) continue;

                string model = EnemyFactory.ModelForEnemy(e);
                string path = "Enemies/" + model;
                if (Resources.Load<GameObject>(path) == null)
                {
                    missing++;
                    failures.Add($"enemy '{e.Id}' resolves to model '{model}' but '{path}' loads NULL (would spawn as a tinted capsule — #22 class regression)");
                    log.AppendLine($"  EN {e.Id} -> '{model}'  ***MISSING '{path}'***");
                }
                else
                {
                    log.AppendLine($"  EN {e.Id} -> '{model}' OK");
                }
            }
            if (missing == 0)
                log.AppendLine($"  EN all spawnable enemies resolve to a real prefab");
        }

        // =====================================================================
        //  4. STRUCTURE PREFABS — every structures-catalog visualPrefabPath (#22)
        // =====================================================================
        // Parsed identically to DataRegression / the production CatalogBootstrap so a
        // schema or wrong-path break shows up here the same way it would at startup.
        [System.Serializable]
        private sealed class StructuresCatalogFile
        {
            [JsonProperty("version")] public int Version;
            [JsonProperty("entries")] public List<DeNelle.Core.Catalog.CatalogEntry> Entries =
                new List<DeNelle.Core.Catalog.CatalogEntry>();
        }

        private static void CheckStructurePrefabs(List<string> failures, StringBuilder log)
        {
            string json = DeNelle.Core.CanonicalJson.Read("Data/Canonical/structures-catalog.json");
            StructuresCatalogFile file = null;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var settings = new JsonSerializerSettings
                    {
                        Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() },
                        NullValueHandling = NullValueHandling.Ignore,
                        MissingMemberHandling = MissingMemberHandling.Ignore,
                    };
                    file = JsonConvert.DeserializeObject<StructuresCatalogFile>(json, settings);
                }
                catch (System.Exception ex)
                {
                    failures.Add($"structures-catalog.json failed to parse: {ex.Message}");
                    log.AppendLine($"  ST parse error: {ex.Message}");
                    return;
                }
            }

            if (file == null || file.Entries == null || file.Entries.Count == 0)
            {
                failures.Add("structures-catalog.json deserialized to 0 CatalogEntry objects");
                log.AppendLine("  ST -> 0 entries");
                return;
            }

            int missing = 0;
            foreach (var entry in file.Entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                // Composites/decorations may legitimately omit a path — only assert declared ones.
                if (string.IsNullOrEmpty(entry.visualPrefabPath)) continue;

                if (Resources.Load<GameObject>(entry.visualPrefabPath) == null)
                {
                    missing++;
                    failures.Add($"structure '{entry.id}' visualPrefabPath '{entry.visualPrefabPath}' loads NULL (builds with no mesh — #22 class regression)");
                    log.AppendLine($"  ST {entry.id} -> '{entry.visualPrefabPath}'  ***MISSING***");
                }
                else
                {
                    log.AppendLine($"  ST {entry.id} -> '{entry.visualPrefabPath}' OK");
                }
            }
            if (missing == 0)
                log.AppendLine($"  ST all declared structure prefab paths resolve");
        }

        // =====================================================================
        //  5. SAVE ROUND-TRIP — real save path survives pet / settlement / economy
        //     (guards WO-159 + pet-persist + the PetName audit finding)
        // =====================================================================
        // We build a PersistedState exactly as GameStateService.Snapshot() does, wrap
        // it in a SaveFile envelope, and run the SAME serialize the service runs:
        //   JsonConvert.SerializeObject(file, SaveSchema.JsonSettings)
        // then deserialize back and SaveSchema.Validate() — the real save/load path.
        //
        // AUDIT NOTE (PetName): the player's starter-pet name has TWO carriers.
        //   • PetData.Nickname  — IS in the persisted payload (PersistedState.Pets),
        //     so it round-trips; we assert it survives.
        //   • GameState.PetName — is NOT in Snapshot()/PersistedState (its own XML-doc
        //     says "not yet in SaveSchema's round-trip"). It can't be exercised through
        //     the real save path headlessly without inventing a code path that doesn't
        //     exist — so we DO NOT fake-assert it survives. Instead we ASSERT THE AUDIT
        //     FACT: PersistedState has no petName carrier of its own, and the persisted
        //     pet name lives on PetData.Nickname. If someone later threads PetName into
        //     SaveSchema, this assertion flips and prompts adding a positive check.
        private static void CheckSaveRoundTrip(List<string> failures, StringBuilder log)
        {
            const string petId = "pet_starter_001";
            const string petNick = "Sparky";
            const string siteId = "node_iron_ridge";
            const int razedUntil = 12;

            // Build the payload the way Snapshot() builds it (the real persisted shape).
            var pet = new PetData
            {
                Id = petId,
                Species = PetSpecies.AetherSprite,
                Nickname = petNick,
                Level = 3,
                Xp = 140,
            };

            var settlement = new SettlementState(siteId, RegionId.Goldfields, new WorldPoint(), 100f);
            settlement.Phase = SettlementPhase.Razed;
            settlement.Hp = 0f;
            settlement.RazedUntilDay = razedUntil;

            var payload = new SaveSchema.PersistedState
            {
                Pets = new List<PetData> { pet },
                StarterPetId = petId,
                OwnedPets = new List<PetSpecies> { PetSpecies.AetherSprite },
                Resources = new ResourceBalance(250, 80, 15),
                Stone = 40, Iron = 25, Wood = 60, Magic = 5,
                Settlements = new List<SettlementState> { settlement },
                HeroClass = HeroClass.Mage,
            };

            var file = new SaveSchema.SaveFile { State = payload };

            // ── The REAL serialize (identical to GameStateService.Save) ──────────
            string json;
            try
            {
                json = JsonConvert.SerializeObject(file, SaveSchema.JsonSettings);
            }
            catch (System.Exception ex)
            {
                failures.Add($"save round-trip: serialize threw {ex.Message}");
                log.AppendLine($"  SAVE serialize error: {ex.Message}");
                return;
            }

            // ── The REAL deserialize + validate (identical to GameStateService.Load) ─
            SaveSchema.SaveFile back;
            try
            {
                back = JsonConvert.DeserializeObject<SaveSchema.SaveFile>(json, SaveSchema.JsonSettings);
            }
            catch (System.Exception ex)
            {
                failures.Add($"save round-trip: deserialize threw {ex.Message}");
                log.AppendLine($"  SAVE deserialize error: {ex.Message}");
                return;
            }

            if (back == null || back.State == null)
            {
                failures.Add("save round-trip: deserialized SaveFile/State is NULL");
                return;
            }

            var validation = SaveSchema.Validate(back.State);
            if (!validation.Ok)
            {
                failures.Add($"save round-trip: Validate rejected the payload ({validation.FieldPath}: {validation.Reason})");
                log.AppendLine($"  SAVE validate FAIL: {validation.Message}");
                return;
            }
            var r = validation.Data;

            // ── PET persistence (pet-persist audit) ──────────────────────────────
            if (r.StarterPetId != petId)
                failures.Add($"save round-trip: StarterPetId '{r.StarterPetId}' != '{petId}' (pet ownership lost)");
            if (r.Pets == null || r.Pets.Count != 1)
                failures.Add($"save round-trip: Pets list did not survive (count={(r.Pets == null ? 0 : r.Pets.Count)})");
            else
            {
                // PetName audit: the persisted pet name lives on PetData.Nickname.
                if (r.Pets[0].Nickname != petNick)
                    failures.Add($"save round-trip: pet Nickname '{r.Pets[0].Nickname}' != '{petNick}' (PetName/nickname persistence lost)");
                if (r.Pets[0].Level != 3 || r.Pets[0].Xp != 140)
                    failures.Add($"save round-trip: pet level/xp changed ({r.Pets[0].Level}/{r.Pets[0].Xp}, expected 3/140)");
            }
            if (r.OwnedPets == null || r.OwnedPets.Count != 1 || r.OwnedPets[0] != PetSpecies.AetherSprite)
                failures.Add("save round-trip: OwnedPets did not survive");

            // ── SETTLEMENT persistence (WO-159) ──────────────────────────────────
            if (r.Settlements == null || r.Settlements.Count != 1)
                failures.Add($"save round-trip: Settlements did not survive (count={(r.Settlements == null ? 0 : r.Settlements.Count)}) — WO-159 regressed");
            else
            {
                var st = r.Settlements[0];
                if (st.SiteId != siteId)
                    failures.Add($"save round-trip: settlement SiteId '{st.SiteId}' != '{siteId}' (WO-159)");
                if (st.Phase != SettlementPhase.Razed)
                    failures.Add($"save round-trip: settlement Phase {st.Phase} != Razed (WO-159 claim phase lost)");
                if (st.RazedUntilDay != razedUntil)
                    failures.Add($"save round-trip: settlement RazedUntilDay {st.RazedUntilDay} != {razedUntil} (WO-159 3-day lockout lost)");
            }

            // ── ECONOMY persistence ──────────────────────────────────────────────
            if (!r.Resources.HasValue ||
                r.Resources.Value.Crystals != 250 || r.Resources.Value.Stone != 80 || r.Resources.Value.Coins != 15)
                failures.Add("save round-trip: Resources (crystals/food/coins) did not survive");
            if ((int?)r.Stone != 40 || (int?)r.Iron != 25 || (int?)r.Wood != 60 || (int?)r.Magic != 5)
                failures.Add($"save round-trip: harvestables/Magic did not survive (stone={r.Stone} iron={r.Iron} wood={r.Wood} magic={r.Magic})");

            // ── AUDIT FACT: GameState.PetName is NOT in the persisted shape ───────
            // PersistedState exposes no petName carrier; the persisted pet name is
            // PetData.Nickname (asserted above). This documents the audit finding so
            // a future thread of PetName into SaveSchema is noticed (flip + add a check).
            bool persistedStateHasPetName =
                typeof(SaveSchema.PersistedState).GetField("PetName") != null;
            if (persistedStateHasPetName)
                failures.Add("AUDIT: SaveSchema.PersistedState now HAS a PetName field — update SessionRegression to positively assert PetName round-trips (it previously only persisted via PetData.Nickname).");

            log.AppendLine($"  SAVE round-trip OK: pet '{petId}'/'{petNick}', settlement '{siteId}' (Razed, lockout {razedUntil}), economy intact");
            log.AppendLine($"  SAVE audit: GameState.PetName persists via PetData.Nickname (NOT a PersistedState field) — known, documented");
        }

        // =====================================================================
        //  6. VENDOR STOCK NON-EMPTY — a general vendor resolves a non-empty stock
        // =====================================================================
        // A general/unknown vendor allows Weapon|Armor (per the contract); its
        // resolved gear stock from the catalog must be non-empty. This is the
        // "store would not be empty" guard at the contract + catalog seam.
        private static void CheckGeneralVendorStock(List<string> failures, StringBuilder log)
        {
            GearCatalog.Reload();
            GearKind allowed = VendorStockContract.AllowedFor("");  // safe general default

            int stock = 0;
            if ((allowed & GearKind.Weapon) != 0)
                stock += new List<WeaponDef>(GearCatalog.AllWeapons()).Count;
            if ((allowed & GearKind.Armor) != 0)
                stock += new List<ArmorDef>(GearCatalog.AllArmors()).Count;

            if (stock == 0)
                failures.Add("general vendor resolved gear stock is EMPTY (contract allows Weapon|Armor but catalog yields 0 rows)");
            log.AppendLine($"  GS general vendor (allowed={allowed}) -> {stock} gear row(s)");
        }
    }
}
