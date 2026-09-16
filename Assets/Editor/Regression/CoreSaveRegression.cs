// =============================================================================
// CoreSaveRegression — the full CORE/SAVE architecture-path suite (headless).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core). Data + logic
// only — no scene loads; runs in the existing editor batchmode harness.
//
// COMPLEMENT, NOT DUPLICATE, of CoreSaveContractRegression (which owns the
// version-triple + the v1 migrate/round-trip + the future-version gate). This
// suite goes deeper on the SAME architecture path — the §4 oracle pattern
// (real object in → assert real response → one marker) applied to:
//
//   A. Envelope + WIRE-FORMAT stability (the React cross-engine contract:
//      exact camelCase keys, [EnumMember] kebab strings, TutorialStep 1..7|"done",
//      unknown-key tolerance).
//   B. LB-3 integrity envelope (EmbedSignature/TryExtractSigned: valid /
//      tampered / legacy-unsigned) as PURE functions.
//   C. The FULL migration chain — EVERY starting version 0..Current-1 executes
//      without throwing and validates; step SEMANTICS on seeded old payloads
//      (v2 starter seed, v8 gate-0→gate-2 rename, v18 aetherCrystals fold,
//      additive seeds v21..v30); migration NEVER clobbers carried data;
//      MigrateForImport rejects NaN/Infinity versions.
//   D. SaveSchema.Validate — NaN/Infinity rejection with the RIGHT field path,
//      negative clamp-to-0, fractional floor, null-payload failure, the
//      Zones/Settlements never-null-on-disk backfill.
//   E. Old-save additive defaults through the REAL parse path: a literal
//      v10-era JSON envelope (with the stale `prepTimerLocked` key) loads,
//      migrates, and reads heroLevel=1 / heroXp=0 / echoCount=1 /
//      strategicPlacementMigrated=false while KEEPING its carried fields.
//   F. LB-3 MaxDepth guard — a 70-deep JSON blob must be REJECTED at parse.
//   G. BaseLayout shape — full-field PlacedStructureData round-trip + the v27
//      default-on-read contract (a pre-v27 record reads worldY=0/wallMounted=false).
//   H. The REAL GameStateService end-to-end: mutate → Save() (signed, via a
//      swapped in-memory ISaveProvider) → service death → fresh service Load()
//      → values survive; ResetToNewGame keeps the wallet/breachStyle carve-out;
//      a TAMPERED stored blob is rejected by the real Load (fresh defaults).
//   I. Append-only persistence GAPS, assert-and-name (the flag_17 precedent):
//      GameState.Tribes / .Wards / .Arena exist in memory but have NO
//      PersistedState field — FAIL-BY-DESIGN oracles that flip green the moment
//      the save owner threads them through; PetName + Settlements ARE persisted
//      (their GameState.cs header comments are STALE) and are asserted green.
//
// Global state discipline: swaps GameStateService.Provider to an in-memory
// provider and restores it; saves/deletes/restores the legacy
// 'realm-defenders-settings' PlayerPrefs key around the v8→v9 step (which
// reads AND deletes it); DestroyImmediate on every created object in finally.
//
// Markers: CORESAVE_OK / CORESAVE_FAIL (FAIL via Debug.LogError so it lands in
// break-log.jsonl). Entry: CoreSaveRegression.Run(out reason).
//
// Wire into the suite from DataRegression.RunAll (one line — orchestrator):
//   if (!CoreSaveRegression.Run(out var coreSaveSuiteReason)) failures.Add(coreSaveSuiteReason); else log.AppendLine("[core-save-suite] " + coreSaveSuiteReason);
// =============================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Core.State;

namespace DeNelle.Editor
{
    public static class CoreSaveRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- CORE/SAVE FULL SUITE (envelope + integrity + migration chain + validate + service round-trip + gaps) ---");

            // v8→v9 reads AND DELETES the legacy settings key — preserve the machine's value.
            bool hadLegacyKey = PlayerPrefs.HasKey(SaveSchema.LegacySettingsKey);
            string legacyValue = hadLegacyKey ? PlayerPrefs.GetString(SaveSchema.LegacySettingsKey) : null;
            if (hadLegacyKey) PlayerPrefs.DeleteKey(SaveSchema.LegacySettingsKey);

            try
            {
                CheckEnvelopeAndWireFormat(failures, log);   // A
                CheckIntegrityEnvelope(failures, log);       // B
                CheckMigrationChain(failures, log);          // C
                CheckValidator(failures, log);               // D
                CheckOldSaveDefaults(failures, log);         // E
                CheckMaxDepthGuard(failures, log);           // F
                CheckBaseLayoutShape(failures, log);         // G
                CheckServiceRoundTrip(failures, log);        // H
                CheckPersistenceGaps(failures, log);         // I
            }
            catch (Exception ex)
            {
                failures.Add($"suite threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (hadLegacyKey) PlayerPrefs.SetString(SaveSchema.LegacySettingsKey, legacyValue);
            }

            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "CORESAVE_OK");
                reason = $"CORE/SAVE SUITE OK — wire format + integrity + full migration chain (schema v{SaveSchema.CurrentVersion}) + validator + real-service round-trip hold";
                return true;
            }
            reason = "core-save-suite: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "CORESAVE_FAIL: " + reason);
            return false;
        }

        // =====================================================================
        //  A — Envelope defaults + cross-engine WIRE FORMAT stability
        // =====================================================================
        // The save is a cross-engine (React-origin) format: [EnumMember] kebab
        // strings, camelCase JsonProperty keys, TutorialStep as 1..7 | "done".
        // A converter/attribute regression silently forks the wire format — old
        // saves and backend records then misread. Pin the EXACT bytes.
        private static void CheckEnvelopeAndWireFormat(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[A] envelope + wire format");

            // A1 — envelope defaults track the constants.
            var envelope = new SaveSchema.SaveFile();
            if (envelope.Format != SaveSchema.FileFormat)
                failures.Add($"SaveFile.Format default ({envelope.Format}) != SaveSchema.FileFormat ({SaveSchema.FileFormat})");
            if (envelope.StoreVersion != SaveSchema.CurrentVersion)
                failures.Add($"SaveFile.StoreVersion default ({envelope.StoreVersion}) != SaveSchema.CurrentVersion ({SaveSchema.CurrentVersion})");

            // A2 — write side: exact keys + wire strings (Formatting.None → no spaces).
            var pinned = new SaveSchema.PersistedState
            {
                TutorialStep = TutorialStep.Done,
                Difficulty = Difficulty.Hard,
                HeroClass = HeroClass.Knight,
                MovementStyle = MovementStyle.Joystick,
                BreachStyle = BreachStyle.TowerSim,
                OwnedPets = new List<PetSpecies> { PetSpecies.FlamePup },
            };
            string json = JsonConvert.SerializeObject(pinned, SaveSchema.JsonSettings);
            AssertWire(json, "\"tutorialStep\":\"done\"", "TutorialStep.Done must serialize as the string \"done\"", failures);
            AssertWire(json, "\"difficulty\":\"hard\"", "Difficulty.Hard must serialize as \"hard\" ([EnumMember])", failures);
            AssertWire(json, "\"heroClass\":\"knight\"", "HeroClass.Knight must serialize as \"knight\" ([EnumMember])", failures);
            AssertWire(json, "\"movementStyle\":\"joystick\"", "MovementStyle.Joystick must serialize as \"joystick\"", failures);
            AssertWire(json, "\"breachStyle\":\"tower-sim\"", "BreachStyle.TowerSim must serialize as the KEBAB \"tower-sim\"", failures);
            AssertWire(json, "\"flame-pup\"", "PetSpecies.FlamePup must serialize as the kebab \"flame-pup\"", failures);

            var stepPinned = new SaveSchema.PersistedState { TutorialStep = TutorialStep.Step3 };
            string stepJson = JsonConvert.SerializeObject(stepPinned, SaveSchema.JsonSettings);
            AssertWire(stepJson, "\"tutorialStep\":3", "TutorialStep.Step3 must serialize as the raw NUMBER 3 (the 1..7|'done' union)", failures);

            // A3 — read side: the exact React wire strings parse back; unknown keys
            // (the stale `prepTimerLocked`) are tolerated, mirroring Zod .partial().
            const string reactWire = "{\"difficulty\":\"hard\",\"heroClass\":\"knight\",\"breachStyle\":\"tower-sim\"," +
                                     "\"movementStyle\":\"joystick\",\"tutorialStep\":\"done\",\"ownedPets\":[\"flame-pup\"]," +
                                     "\"prepTimerLocked\":true,\"someFutureKey\":{\"x\":1}}";
            try
            {
                var back = JsonConvert.DeserializeObject<SaveSchema.PersistedState>(reactWire, SaveSchema.JsonSettings);
                if (back == null) { failures.Add("React-wire parse returned null"); return; }
                if (back.Difficulty != Difficulty.Hard) failures.Add("wire read: \"hard\" did not parse to Difficulty.Hard");
                if (back.HeroClass != HeroClass.Knight) failures.Add("wire read: \"knight\" did not parse to HeroClass.Knight");
                if (back.BreachStyle != BreachStyle.TowerSim) failures.Add("wire read: \"tower-sim\" did not parse to BreachStyle.TowerSim");
                if (back.MovementStyle != MovementStyle.Joystick) failures.Add("wire read: \"joystick\" did not parse to MovementStyle.Joystick");
                if (back.TutorialStep != TutorialStep.Done) failures.Add("wire read: \"done\" did not parse to TutorialStep.Done");
                if (back.OwnedPets == null || back.OwnedPets.Count != 1 || back.OwnedPets[0] != PetSpecies.FlamePup)
                    failures.Add("wire read: [\"flame-pup\"] did not parse to [PetSpecies.FlamePup]");
                log.AppendLine("  wire format: write + read pins hold (kebab enums, tutorialStep union, unknown-key tolerance)");
            }
            catch (Exception ex)
            {
                failures.Add($"React-wire payload with unknown keys THREW ({ex.GetType().Name}) — .partial() tolerance broken");
            }

            // A4 — TutorialStepConverter tolerant reads (documented in the converter):
            // a numeric > 7 reads as Done; a numeric < 1 reads as Step1.
            var over = JsonConvert.DeserializeObject<SaveSchema.PersistedState>("{\"tutorialStep\":9}", SaveSchema.JsonSettings);
            if (over == null || over.TutorialStep != TutorialStep.Done)
                failures.Add("TutorialStepConverter: numeric 9 should read as Done (tolerant clamp)");
            var under = JsonConvert.DeserializeObject<SaveSchema.PersistedState>("{\"tutorialStep\":0}", SaveSchema.JsonSettings);
            if (under == null || under.TutorialStep != TutorialStep.Step1)
                failures.Add("TutorialStepConverter: numeric 0 should read as Step1 (tolerant clamp)");
        }

        private static void AssertWire(string json, string mustContain, string why, List<string> failures)
        {
            if (json == null || !json.Contains(mustContain))
                failures.Add($"wire format drift: serialized save missing `{mustContain}` — {why}");
        }

        // =====================================================================
        //  B — LB-3 integrity envelope (pure SaveSchema functions)
        // =====================================================================
        private static void CheckIntegrityEnvelope(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[B] LB-3 integrity envelope");
            const string payload = "{\"format\":1,\"state\":{\"bestWave\":7}}";

            // B1 — sign → extract: present + valid + payload byte-identical.
            string stored = SaveSchema.EmbedSignature(payload);
            string outJson = SaveSchema.TryExtractSigned(stored, out bool present, out bool valid);
            if (!present) failures.Add("EmbedSignature output not detected as SIGNED by TryExtractSigned");
            if (!valid) failures.Add("freshly-signed payload verified INVALID (HMAC round-trip broken → every save would be rejected as tampered)");
            if (outJson != payload) failures.Add("TryExtractSigned did not return the original payload byte-identical");

            // B2 — tamper one payload character: signature must be detected + INVALID.
            var tampered = new StringBuilder(stored);
            int i = stored.IndexOf("bestWave", StringComparison.Ordinal);
            tampered[i] = tampered[i] == 'b' ? 'B' : 'b';
            SaveSchema.TryExtractSigned(tampered.ToString(), out bool tPresent, out bool tValid);
            if (!tPresent) failures.Add("tampered blob no longer detected as signed (prefix layout broke)");
            if (tValid) failures.Add("TAMPERED payload verified VALID — the LB-3 integrity gate is open");

            // B3 — legacy raw-JSON save: no signature detected, payload passes through unchanged.
            string legacyOut = SaveSchema.TryExtractSigned(payload, out bool lPresent, out _);
            if (lPresent) failures.Add("legacy UNSIGNED save mis-detected as signed (would be rejected → save loss for pre-LB-3 players)");
            if (legacyOut != payload) failures.Add("legacy unsigned save payload was mutated by TryExtractSigned");

            // B4 — an empty/missing signature never verifies.
            if (SaveSchema.VerifySignature(payload, null) || SaveSchema.VerifySignature(payload, ""))
                failures.Add("VerifySignature accepted a null/empty signature");
            log.AppendLine("  sign/tamper/legacy/empty-sig contracts hold");
        }

        // =====================================================================
        //  C — the FULL migration chain (every start version + step semantics)
        // =====================================================================
        private static void CheckMigrationChain(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[C] migration chain");

            // C1 — every starting version 0..Current-1 migrates a bare payload
            // without throwing, and the result passes SaveSchema.Validate.
            for (int from = 0; from < SaveSchema.CurrentVersion; from++)
            {
                try
                {
                    var m = SaveMigrator.Migrate(new SaveSchema.PersistedState(), from);
                    if (m == null) { failures.Add($"Migrate(from v{from}) returned null"); continue; }
                    var vr = SaveSchema.Validate(m);
                    if (!vr.Ok) failures.Add($"Migrate(from v{from}) output FAILED validation: field '{vr.FieldPath}' ({vr.Reason})");
                }
                catch (Exception ex)
                {
                    failures.Add($"Migrate(from v{from}) THREW {ex.GetType().Name}: {ex.Message}");
                }
            }
            log.AppendLine($"  every start version 0..{SaveSchema.CurrentVersion - 1} migrated + validated");

            // C2 — step SEMANTICS on a bare v1 payload (each line names its step).
            var full = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 1);
            if (full == null) { failures.Add("Migrate(v1) returned null — semantics checks skipped"); return; }

            var r = full.Resources ?? ResourceBalance.Zero;
            if (!full.Resources.HasValue || r.Crystals != 250 || r.Stone != 80 || r.Coins != 15)
                failures.Add($"v2 step: resources not seeded to STARTER {{250,80,15}} (got {r.Crystals}/{r.Stone}/{r.Coins})");
            if (full.OwnedItemIds == null) failures.Add("v2 step: ownedItemIds not seeded to []");
            if (full.HeroClass != HeroClass.Mage) failures.Add("v3 step: heroClass not defaulted to Mage on a pre-hero-select save");
            if (!full.Wood.HasValue || full.Wood.Value != 15) failures.Add("v4 step: wood not seeded to 15");
            if (full.BuildingCooldowns == null) failures.Add("v4 step: buildingCooldowns not seeded to {}");
            if (full.TutorialStep != TutorialStep.Done) failures.Add("v4 step: tutorialStep not seeded to Done (an in-progress save must skip the FTUE)");
            if (full.TowerAbilities == null || full.TowerAbilities.Count != DeNelle.Core.Constants.TowerSlots)
                failures.Add($"v5 step: towerAbilities not seeded to [0]x{DeNelle.Core.Constants.TowerSlots} (got {(full.TowerAbilities == null ? "null" : full.TowerAbilities.Count.ToString())})");
            if (!full.Inventory.HasValue) failures.Add("v6 step: inventory not seeded");
            if (full.BreachStyle != BreachStyle.Ask) failures.Add("v6 step: breachStyle not seeded to Ask");
            if (full.Quests == null) failures.Add("v6 step: quests not seeded");
            if (full.Dungeons == null || full.Dungeons.Discovered == null
                || !full.Dungeons.Discovered.TryGetValue(SaveSchema.StarterDungeonId, out bool disc) || !disc)
                failures.Add($"v7 step: starter dungeon '{SaveSchema.StarterDungeonId}' not merged into dungeons.discovered");
            if (full.PendingBuilds == null) failures.Add("v9 step: pendingBuilds not seeded to []");
            // v9 defaults (legacy settings key guaranteed ABSENT here — bracketed in Run):
            if (full.Muted != false) failures.Add("v9 step: muted default should be false for a migrated save");
            if (!full.MusicVolume.HasValue || Math.Abs(full.MusicVolume.Value - 70) > 0.001) failures.Add("v9 step: musicVolume default should be 70");
            if (!full.SfxVolume.HasValue || Math.Abs(full.SfxVolume.Value - 80) > 0.001) failures.Add("v9 step: sfxVolume default should be 80");
            if (full.Difficulty != Difficulty.Normal) failures.Add("v9 step: difficulty default should be normal");
            if (full.Regions == null) failures.Add("v10 step: regions not seeded");
            if (full.BaseLayout == null) failures.Add("v14 step: baseLayout not seeded to []");
            if (full.Zones == null || full.Zones.Count == 0) failures.Add("v17 step: zone graph not seeded");
            if (full.Settlements == null) failures.Add("v21 step: settlements not seeded to []");
            if (full.Army == null) failures.Add("v22 step: army not seeded (older saves must load an empty cap-10 army)");
            if (full.BuildingTiers == null) failures.Add("v23 step: buildingTiers not seeded to {}");
            if (full.OwnedBuildingPerks == null) failures.Add("v24 step: ownedBuildingPerks not seeded to []");
            if (!full.EchoCount.HasValue || (int)full.EchoCount.Value != 1) failures.Add("v25 step: echoCount not seeded to 1 (the starter Echo)");
            if (!full.SiloResources.HasValue || full.SiloResources.Value != 0) failures.Add("v25 step: siloResources not seeded to 0");
            if (!full.WavesCompleted.HasValue || (int)full.WavesCompleted.Value != 0) failures.Add("v25 step: wavesCompleted not seeded to 0");
            if (full.EquippedRingId != "") failures.Add("v26 step: equippedRingId not seeded to \"\"");
            if (full.EquippedAmuletId != "") failures.Add("v26 step: equippedAmuletId not seeded to \"\"");
            if (!full.PopulationXP.HasValue || (int)full.PopulationXP.Value != 0) failures.Add("v28 step: populationXp not seeded to 0");
            if (!full.PopulationEchoSlots.HasValue || (int)full.PopulationEchoSlots.Value != 1) failures.Add("v28 step: populationEchoSlots not seeded to 1");
            if (!full.HeroLevel.HasValue || (int)full.HeroLevel.Value != 1) failures.Add("v29 step: heroLevel not seeded to 1");
            if (!full.HeroXp.HasValue || full.HeroXp.Value != 0) failures.Add("v29 step: heroXp not seeded to 0");
            if (!full.HeroLifetimeXp.HasValue || full.HeroLifetimeXp.Value != 0) failures.Add("v29 step: heroLifetimeXp not seeded to 0");
            if (full.StrategicPlacementMigrated != false) failures.Add("v30 step: strategicPlacementMigrated not seeded to false (bakes/injectors must keep ownership)");
            log.AppendLine("  v1→current step semantics hold (seeds v2..v30)");

            // C2b — v7→v8 gate rename: gate-0 damage moves to gate-2.
            var gateSave = new SaveSchema.PersistedState
            {
                BuildingDamage = new Dictionary<string, double> { { "gate-0", 42.0 }, { "heart", 5.0 } },
            };
            var gateMigrated = SaveMigrator.Migrate(gateSave, 7);
            if (gateMigrated.BuildingDamage == null
                || !gateMigrated.BuildingDamage.TryGetValue("gate-2", out double g2) || Math.Abs(g2 - 42.0) > 0.001)
                failures.Add("v8 step: buildingDamage['gate-0'] was not copied to 'gate-2' (south-gate damage lost)");
            if (gateMigrated.BuildingDamage != null && gateMigrated.BuildingDamage.ContainsKey("gate-0"))
                failures.Add("v8 step: orphan 'gate-0' key not removed after the rename");
            if (gateMigrated.BuildingDamage == null
                || !gateMigrated.BuildingDamage.TryGetValue("heart", out double heart) || Math.Abs(heart - 5.0) > 0.001)
                failures.Add("v8 step: unrelated buildingDamage key 'heart' was disturbed by the gate rename");

            // C2c — v17→v18 crystal fold: aetherCrystals folds INTO resources.crystals, then zeroes.
            var foldSave = new SaveSchema.PersistedState
            {
                AetherCrystals = 40,
                Resources = new ResourceBalance(10, 0, 0),
            };
            var folded = SaveMigrator.Migrate(foldSave, 17);
            if (!folded.Resources.HasValue || folded.Resources.Value.Crystals != 50)
                failures.Add($"v18 step: aetherCrystals(40) not folded into resources.crystals(10) — expected 50, got {(folded.Resources.HasValue ? folded.Resources.Value.Crystals.ToString() : "null")}");
            if (!folded.AetherCrystals.HasValue || folded.AetherCrystals.Value != 0)
                failures.Add("v18 step: aetherCrystals not zeroed after the fold (split-brain balance survives)");

            // C2d — NO-CLOBBER: a save that already CARRIES data must keep it through
            // the chain (every step is additive-only by contract).
            var carried = new SaveSchema.PersistedState
            {
                Resources = new ResourceBalance(1, 2, 3),
                HeroLevel = 9,
                Wood = 999,
                HeroClass = HeroClass.Knight,
                TutorialStep = TutorialStep.Step2,
                EquippedRingId = "ring_gold",
            };
            var kept = SaveMigrator.Migrate(carried, 3);   // runs steps 4..30
            if (!kept.Resources.HasValue || kept.Resources.Value.Crystals != 1 || kept.Resources.Value.Stone != 2 || kept.Resources.Value.Coins != 3)
                failures.Add("migration CLOBBERED carried resources (steps must be additive-only)");
            if (!kept.HeroLevel.HasValue || (int)kept.HeroLevel.Value != 9) failures.Add("migration CLOBBERED carried heroLevel=9");
            if (!kept.Wood.HasValue || (int)kept.Wood.Value != 999) failures.Add("migration CLOBBERED carried wood=999");
            if (kept.HeroClass != HeroClass.Knight) failures.Add("migration CLOBBERED carried heroClass=Knight");
            if (kept.TutorialStep != TutorialStep.Step2) failures.Add("migration CLOBBERED carried tutorialStep=2 (would skip the player past FTUE)");
            if (kept.EquippedRingId != "ring_gold") failures.Add("migration CLOBBERED carried equippedRingId");
            log.AppendLine("  gate-rename + crystal-fold + no-clobber semantics hold");

            // C3 — MigrateForImport version gate: NaN / Infinity are rejected
            // (the future-version + equal-version gates live in CoreSaveContractRegression).
            if (SaveMigrator.MigrateForImport(new SaveSchema.PersistedState(), double.NaN).Ok)
                failures.Add("MigrateForImport accepted a NaN storeVersion");
            if (SaveMigrator.MigrateForImport(new SaveSchema.PersistedState(), double.PositiveInfinity).Ok)
                failures.Add("MigrateForImport accepted an Infinity storeVersion");
        }

        // =====================================================================
        //  D — SaveSchema.Validate (the safeParse port)
        // =====================================================================
        private static void CheckValidator(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[D] validator");

            // D1 — NaN numeric → rejected WITH the right field path (the React toast
            // contract). Crystals is int on the struct, so NaN rides the double fields.
            var nanWave = new SaveSchema.PersistedState { BestWave = double.NaN };
            var v1 = SaveSchema.Validate(nanWave);
            if (v1.Ok || v1.FieldPath != "bestWave")
                failures.Add($"Validate accepted NaN bestWave or misnamed the field (ok={v1.Ok}, field='{v1.FieldPath}')");

            var infXp = new SaveSchema.PersistedState { HeroXp = double.PositiveInfinity };
            var v2 = SaveSchema.Validate(infXp);
            if (v2.Ok || v2.FieldPath != "heroXp")
                failures.Add($"Validate accepted Infinity heroXp or misnamed the field (ok={v2.Ok}, field='{v2.FieldPath}')");

            var nanBuild = new SaveSchema.PersistedState
            {
                PendingBuilds = new List<PendingTowerBuild> { new PendingTowerBuild { Slot = 0, Ability = 0, FinishAt = double.NaN } },
            };
            var v3 = SaveSchema.Validate(nanBuild);
            if (v3.Ok || v3.FieldPath != "pendingBuilds.0.finishAt")
                failures.Add($"Validate accepted NaN pendingBuilds finishAt or misnamed the path (ok={v3.Ok}, field='{v3.FieldPath}')");

            // D2 — negative → clamp to 0 (nonNegInt), fractional → floor.
            var clampState = new SaveSchema.PersistedState
            {
                BestWave = -5,
                Voidshards = -1,
                Stone = 10.9,
                PetBonds = new List<double> { -2, 3.7 },
            };
            var v4 = SaveSchema.Validate(clampState);
            if (!v4.Ok) failures.Add($"Validate rejected a clampable payload (field '{v4.FieldPath}')");
            else
            {
                if ((int)v4.Data.BestWave.Value != 0) failures.Add($"nonNegInt: bestWave -5 should clamp to 0 (got {v4.Data.BestWave.Value})");
                if ((int)v4.Data.Voidshards.Value != 0) failures.Add($"nonNegInt: voidshards -1 should clamp to 0 (got {v4.Data.Voidshards.Value})");
                if ((int)v4.Data.Stone.Value != 10) failures.Add($"nonNegInt: stone 10.9 should FLOOR to 10 (got {v4.Data.Stone.Value})");
                if ((int)v4.Data.PetBonds[0] != 0 || (int)v4.Data.PetBonds[1] != 3)
                    failures.Add($"nonNegInt list: petBonds [-2, 3.7] should clamp/floor to [0, 3] (got [{v4.Data.PetBonds[0]}, {v4.Data.PetBonds[1]}])");
            }

            // D3 — null payload → failure naming 'state'.
            var v5 = SaveSchema.Validate(null);
            if (v5.Ok || v5.FieldPath != "state") failures.Add("Validate(null) should fail with field 'state'");

            // D4 — Zones/Settlements never-null-on-disk backfill.
            var bare = new SaveSchema.PersistedState();
            var v6 = SaveSchema.Validate(bare);
            if (!v6.Ok) failures.Add($"Validate rejected an empty partial payload (field '{v6.FieldPath}') — .partial() tolerance broken");
            else
            {
                if (v6.Data.Zones == null) failures.Add("Validate did not backfill zones to a non-null list");
                if (v6.Data.Settlements == null) failures.Add("Validate did not backfill settlements to a non-null list");
            }
            log.AppendLine("  NaN/Infinity paths, clamps, floors, null-payload, backfills hold");
        }

        // =====================================================================
        //  E — old-save additive defaults through the REAL parse+migrate path
        // =====================================================================
        private static void CheckOldSaveDefaults(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[E] old-save (v10-era JSON) additive defaults");

            // A literal v10-era envelope: carries a real mid-game state, NONE of the
            // v11+ fields, plus the stale runtime-only `prepTimerLocked` key.
            const string oldSave =
                "{\"format\":1,\"storeVersion\":10,\"exportedAt\":\"2025-01-01T00:00:00.000Z\",\"wallet\":null," +
                "\"state\":{\"bestWave\":12,\"resources\":{\"crystals\":300,\"food\":90,\"coins\":25}," +
                "\"wood\":40,\"stone\":22,\"iron\":8,\"heroClass\":\"knight\",\"tutorialStep\":\"done\"," +
                "\"muted\":false,\"musicVolume\":55,\"sfxVolume\":60,\"difficulty\":\"easy\"," +
                "\"prepTimerLocked\":true}}";

            SaveSchema.SaveFile file;
            try { file = JsonConvert.DeserializeObject<SaveSchema.SaveFile>(oldSave, SaveSchema.JsonSettings); }
            catch (Exception ex) { failures.Add($"v10-era envelope failed to parse: {ex.GetType().Name}: {ex.Message}"); return; }
            if (file == null || file.State == null) { failures.Add("v10-era envelope parsed to null file/state"); return; }
            if (file.StoreVersion != 10) { failures.Add($"v10-era envelope storeVersion misread ({file.StoreVersion})"); return; }

            var import = SaveMigrator.MigrateForImport(file.State, file.StoreVersion);
            if (!import.Ok) { failures.Add($"v10-era save REJECTED by MigrateForImport: {import.Reason}"); return; }
            var vr = SaveSchema.Validate(import.Data);
            if (!vr.Ok) { failures.Add($"v10-era save failed validation post-migrate: field '{vr.FieldPath}'"); return; }
            var s = vr.Data;

            // Carried fields SURVIVE...
            if (!s.BestWave.HasValue || (int)s.BestWave.Value != 12) failures.Add("v10-era save: carried bestWave=12 lost through migrate+validate");
            if (!s.Resources.HasValue || s.Resources.Value.Crystals != 300) failures.Add("v10-era save: carried resources.crystals=300 lost");
            if (!s.Wood.HasValue || (int)s.Wood.Value != 40) failures.Add("v10-era save: carried wood=40 lost");
            if (s.HeroClass != HeroClass.Knight) failures.Add("v10-era save: carried heroClass=knight lost");
            if (s.Difficulty != Difficulty.Easy) failures.Add("v10-era save: carried difficulty=easy lost");
            // ...while every additive field reads its default:
            if (!s.HeroLevel.HasValue || (int)s.HeroLevel.Value != 1) failures.Add("v10-era save: heroLevel default 1 not seeded (F8-47 additive default)");
            if (!s.HeroXp.HasValue || s.HeroXp.Value != 0) failures.Add("v10-era save: heroXp default 0 not seeded");
            if (!s.HeroLifetimeXp.HasValue || s.HeroLifetimeXp.Value != 0) failures.Add("v10-era save: heroLifetimeXp default 0 not seeded");
            if (!s.EchoCount.HasValue || (int)s.EchoCount.Value != 1) failures.Add("v10-era save: echoCount default 1 not seeded");
            if (s.EquippedRingId != "") failures.Add("v10-era save: equippedRingId default \"\" not seeded");
            if (s.StrategicPlacementMigrated != false) failures.Add("v10-era save: strategicPlacementMigrated default false not seeded");
            if (s.BaseLayout == null) failures.Add("v10-era save: baseLayout default [] not seeded");
            if (s.Army == null) failures.Add("v10-era save: army default not seeded");
            log.AppendLine("  v10-era save loads: carried fields survive + all additive defaults seed");
        }

        // =====================================================================
        //  F — LB-3 MaxDepth guard (a hostile deep blob must be rejected)
        // =====================================================================
        private static void CheckMaxDepthGuard(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[F] MaxDepth guard");
            // 70 nesting levels > the 64 cap in SaveSchema.JsonSettings.
            var sb = new StringBuilder();
            for (int i = 0; i < 70; i++) sb.Append("{\"a\":");
            sb.Append("1");
            for (int i = 0; i < 70; i++) sb.Append("}");
            bool threw = false;
            try { JsonConvert.DeserializeObject<SaveSchema.SaveFile>(sb.ToString(), SaveSchema.JsonSettings); }
            catch (JsonException) { threw = true; }
            if (!threw)
                failures.Add("a 70-deep JSON blob parsed WITHOUT throwing — the LB-3 MaxDepth(64) stack-blow guard regressed");
            else
                log.AppendLine("  70-deep hostile blob rejected at parse (MaxDepth holds)");
        }

        // =====================================================================
        //  G — BaseLayout (PlacedStructureData) shape round-trip + v27 defaults
        // =====================================================================
        private static void CheckBaseLayoutShape(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[G] BaseLayout shape");

            // G1 — full-field record survives the REAL save serializer settings.
            var state = new SaveSchema.PersistedState
            {
                BaseLayout = new List<PlacedStructureData>
                {
                    new PlacedStructureData("tower_ground_archer", 3, -2, 3, 2, yawOffset: 45f, worldY: 2.5f, wallMounted: true),
                },
            };
            string json = JsonConvert.SerializeObject(state, SaveSchema.JsonSettings);
            var back = JsonConvert.DeserializeObject<SaveSchema.PersistedState>(json, SaveSchema.JsonSettings);
            if (back?.BaseLayout == null || back.BaseLayout.Count != 1)
            {
                failures.Add("baseLayout list did not round-trip (null or wrong count)");
            }
            else
            {
                var p = back.BaseLayout[0];
                if (p.itemId != "tower_ground_archer") failures.Add($"baseLayout itemId lost ('{p.itemId}')");
                if (p.cellX != 3 || p.cellZ != -2) failures.Add($"baseLayout cell lost ({p.cellX},{p.cellZ}) — negative cellZ must survive");
                if (p.yawSteps != 3) failures.Add($"baseLayout yawSteps lost ({p.yawSteps})");
                if (p.level != 2) failures.Add($"baseLayout level lost ({p.level})");
                if (Math.Abs(p.yawOffset - 45f) > 0.001f) failures.Add($"baseLayout yawOffset lost ({p.yawOffset})");
                if (Math.Abs(p.worldY - 2.5f) > 0.001f) failures.Add($"baseLayout worldY lost ({p.worldY}) — v27 wall-seat height");
                if (!p.wallMounted) failures.Add("baseLayout wallMounted=true lost — the v27 high-ground perk flag");
            }

            // G2 — the v27 default-on-read contract: a PRE-v27 record (no worldY /
            // wallMounted / yawOffset keys) reads as a ground placement.
            const string preV27 = "{\"baseLayout\":[{\"itemId\":\"market\",\"cellX\":1,\"cellZ\":2,\"yawSteps\":0,\"level\":1}]}";
            var old = JsonConvert.DeserializeObject<SaveSchema.PersistedState>(preV27, SaveSchema.JsonSettings);
            if (old?.BaseLayout == null || old.BaseLayout.Count != 1)
                failures.Add("pre-v27 baseLayout record failed to parse");
            else
            {
                var p = old.BaseLayout[0];
                if (p.worldY != 0f) failures.Add($"pre-v27 record read worldY={p.worldY}, must default 0 (ground)");
                if (p.wallMounted) failures.Add("pre-v27 record read wallMounted=true, must default false");
                if (p.yawOffset != 0f) failures.Add($"pre-v27 record read yawOffset={p.yawOffset}, must default 0");
            }
            log.AppendLine("  full-field round-trip + pre-v27 default-on-read hold");
        }

        // =====================================================================
        //  H — the REAL GameStateService end-to-end (in-memory provider)
        // =====================================================================
        // Drives the actual production path — Save(): Snapshot → serialize →
        // EmbedSignature → Provider.Write; Load(): Provider.Read → sig verify →
        // parse → MigrateForImport → Validate → ApplyPersisted — across TWO
        // service lifetimes (a session boundary), then proves the tamper gate
        // and the ResetToNewGame carve-out. Edit-mode AddComponent does not run
        // Awake, so it is invoked by reflection (the service's own ResetToNewGame
        // header documents this edit-mode-test pattern).
        private sealed class InMemorySaveProvider : ISaveProvider
        {
            public readonly Dictionary<string, string> Store = new Dictionary<string, string>();
            public bool Exists(string slot) => Store.ContainsKey(slot);
            public string Read(string slot) => Store.TryGetValue(slot, out var v) ? v : string.Empty;
            public void Write(string slot, string json) => Store[slot] = json;
            public void Delete(string slot) => Store.Remove(slot);
        }

        private static void CheckServiceRoundTrip(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[H] real GameStateService round-trip");

            if (GameStateService.Instance != null)
            {
                // Can't safely commandeer a live singleton (would clobber real session state).
                log.AppendLine("  SKIPPED — a live GameStateService.Instance already exists in this editor session");
                return;
            }

            var awake = typeof(GameStateService).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            if (awake == null)
            {
                failures.Add("could not reflect GameStateService.Awake — the lifecycle seam moved; re-point this oracle");
                return;
            }

            var priorProvider = GameStateService.Provider;
            var memory = new InMemorySaveProvider();
            var created = new List<GameObject>();
            try
            {
                GameStateService.Provider = memory;

                // ── Lifetime 1: fresh boot, mutate through REAL mutators, Save. ──
                var go1 = new GameObject("CoreSaveOracle_Svc1");
                created.Add(go1);
                var svc1 = go1.AddComponent<GameStateService>();
                awake.Invoke(svc1, null);   // Load (empty) + EnsureHeroClassPersisted (persists Knight)

                if (svc1.State == null) { failures.Add("service lifetime-1 has no State after Awake"); return; }
                svc1.RecordRun(12);                 // bestWave = 12 (+Save)
                svc1.AddCrystals(100);              // 250 starter + 100 = 350 (+Save)
                svc1.SetDifficulty(Difficulty.Hard);
                svc1.State.HeroLevel = 7;
                svc1.State.HeroXp = 55.5f;
                svc1.State.PetName = "Squeaks";
                svc1.State.BaseLayout.Add(new PlacedStructureData("market", 4, 5, 1, 1));
                if (svc1.State.FreeBuildsUsed == null) svc1.State.FreeBuildsUsed = new List<string>();
                svc1.State.FreeBuildsUsed.Add("pet-house");   // v32 — a consumed first-build freebie must round-trip (one-shot, never resets)
                svc1.Save();

                // Stored blob must be SIGNED and VALID (the LB-3 write contract).
                string stored = memory.Read(SaveSchema.PlayerPrefsKey);
                SaveSchema.TryExtractSigned(stored, out bool sigPresent, out bool sigValid);
                if (!sigPresent || !sigValid)
                    failures.Add($"service Save() wrote an unsigned/invalid blob (present={sigPresent}, valid={sigValid}) — LB-3 write contract broken");

                UnityEngine.Object.DestroyImmediate(go1);   // OnDestroy clears the singleton
                created.Remove(go1);

                // ── Lifetime 2: fresh service Load()s the same provider. ──
                var go2 = new GameObject("CoreSaveOracle_Svc2");
                created.Add(go2);
                var svc2 = go2.AddComponent<GameStateService>();
                awake.Invoke(svc2, null);

                var st = svc2.State;
                if (st == null) { failures.Add("service lifetime-2 has no State after Awake"); return; }
                if (st.BestWave != 12) failures.Add($"round-trip lost bestWave (wrote 12, loaded {st.BestWave})");
                if (st.Resources.Crystals != 350) failures.Add($"round-trip lost crystals (wrote 350, loaded {st.Resources.Crystals})");
                if (st.Difficulty != Difficulty.Hard) failures.Add($"round-trip lost difficulty=Hard (loaded {st.Difficulty})");
                if (st.HeroClass.ToNullable() != HeroClass.Knight) failures.Add("round-trip lost heroClass=Knight");
                if (st.HeroLevel != 7) failures.Add($"round-trip lost heroLevel=7 (loaded {st.HeroLevel}) — the F8-47 reset class through the REAL service");
                if (Math.Abs(st.HeroXp - 55.5f) > 0.01f) failures.Add($"round-trip lost heroXp=55.5 (loaded {st.HeroXp})");
                if (st.PetName != "Squeaks") failures.Add($"round-trip lost petName='Squeaks' (loaded '{st.PetName}') — Audit P2 persistence");
                if (st.BaseLayout == null || st.BaseLayout.Count != 1 || st.BaseLayout[0].itemId != "market"
                    || st.BaseLayout[0].cellX != 4 || st.BaseLayout[0].cellZ != 5)
                    failures.Add("round-trip lost the baseLayout record through the REAL service path");
                if (st.FreeBuildsUsed == null || !st.FreeBuildsUsed.Contains("pet-house"))
                    failures.Add($"round-trip lost freeBuildsUsed=['pet-house'] (loaded {(st.FreeBuildsUsed == null ? "null" : "[" + string.Join(",", st.FreeBuildsUsed) + "]")}) — a consumed v32 freebie must NEVER silently reset");
                log.AppendLine("  lifetime-1 -> lifetime-2 round-trip holds (bestWave/crystals/difficulty/heroLevel/petName/baseLayout)");

                // ── ResetToNewGame carve-out: wallet + breachStyle survive; progression wipes. ──
                // The fixture MUST be a real base58 pubkey. It used to be "test-wallet-123", which
                // the 2026-08-02 identity work now correctly RETIRES on save: a bound key that is
                // neither guest-shaped nor wallet-shaped can never authenticate against the backend
                // (api/_lib/wallet-auth.js WALLET_RE), so it is stashed and cleared rather than left
                // to 401 forever. A hyphen is not in the base58 alphabet. Asserting the old fixture
                // survived would have been asserting the BROKEN behaviour.
                const string walletFixture = "BwBB9LUS3Nmxqgc41xNbGUygsUVQniv9PdngiycicjJV";
                svc2.BindWallet(walletFixture);
                svc2.SetBreachStyle(BreachStyle.TowerSim);
                svc2.ResetToNewGame();
                if (st.BoundWallet != walletFixture) failures.Add("ResetToNewGame wiped BoundWallet — the carve-out contract broken");
                if (st.BreachStyle != BreachStyle.TowerSim) failures.Add("ResetToNewGame wiped BreachStyle — preferences must survive New Game");
                // ⭐ RE-POINTED BY OWNER RULING 2026-08-26 (WO-1217 Slice B), verbatim: "so start
                // gold at 200". Coins was 15 (the ResourceBalance.Starter value); a new game now
                // seeds StartingBudget.StrategicGold on top of Starter. The pin MOVED WITH the
                // ruling — it was NOT softened: crystals/food stay pinned at 250/80, and the coins
                // arm still fails on any value other than the ruled one.
                // ⚠ Read the expected coins off StartingBudget, never a literal here: a number
                // restated in a second place is this repo's most repeated defect, and the whole
                // point of routing the seed through StartingBudget was to have ONE authority.
                // ⛔ ResourceBalance.Starter itself is deliberately UNCHANGED (still coins 15) —
                // SaveMigrator uses it as the default for resource-less legacy saves, so editing it
                // would silently hand 200 gold to every migrating save. Nobody ruled that.
                if (st.Resources.Crystals != 250 || st.Resources.Stone != 80
                    || st.Resources.Coins != DeNelle.Core.State.StartingBudget.StrategicGold)
                    failures.Add($"ResetToNewGame did not restore STARTER resources (got {st.Resources.Crystals}/{st.Resources.Stone}/{st.Resources.Coins}; expected 250/80/{DeNelle.Core.State.StartingBudget.StrategicGold})");
                if (st.BestWave != 0) failures.Add("ResetToNewGame did not zero bestWave");
                if (st.HeroLevel != 1 || st.HeroXp != 0f) failures.Add("ResetToNewGame did not reset hero level/XP to 1/0");
                if (st.HeroClass.ToNullable().HasValue) failures.Add("ResetToNewGame did not clear HeroClass (onboarding must re-prompt)");
                if (st.Pets.Count != 0 || st.OwnedPets.Count != 0 || st.PetName != null)
                    failures.Add("ResetToNewGame left ghost pet ownership (Pets/OwnedPets/PetName must all clear)");
                if (st.Zones == null || st.Zones.Count == 0) failures.Add("ResetToNewGame did not seed the default zone graph");
                // WO-682/WO-707 + owner ruling 2026-07-13 evening: the seed is
                // unconditionally the StartingBudget pair — now ZERO, because the
                // per-id free-first-build flags REPLACE the resource seed. New Game =
                // the TRULY blank template (marker set, BaseLayout EMPTY — the WO-682
                // grace-default forge was KILLED: "should be placed by player").
                if (st.Wood != StartingBudget.StrategicWood)
                    failures.Add($"ResetToNewGame wood seed {st.Wood} != expected {StartingBudget.StrategicWood} (strategic placement is always on, WO-682)");
                if (st.Iron != StartingBudget.StrategicIron)
                    failures.Add($"ResetToNewGame iron seed {st.Iron} != expected {StartingBudget.StrategicIron} (strategic placement is always on, WO-682)");
                if (!st.StrategicPlacementMigrated)
                    failures.Add("ResetToNewGame did not SET the strategic-placement marker — a new game must be the blank template (WO-682), never a re-migrated town");
                if (st.BaseLayout == null || st.BaseLayout.Count != 0)
                    failures.Add($"ResetToNewGame BaseLayout != EMPTY (got {(st.BaseLayout == null ? "null" : st.BaseLayout.Count.ToString())} record(s)) — WO-707: no grace default, the player places everything");
                // v32 — the loaded save had 'pet-house' consumed; a New Game must hand back
                // EVERY first-build freebie (fresh empty list, owner ruling 2026-07-13 evening).
                if (st.FreeBuildsUsed == null || st.FreeBuildsUsed.Count != 0)
                    failures.Add($"ResetToNewGame FreeBuildsUsed != EMPTY (got {(st.FreeBuildsUsed == null ? "null" : "[" + string.Join(",", st.FreeBuildsUsed) + "]")}) — a fresh save must have ALL first-build freebies live");
                // WO-949 — the founding kit grants starter healing potions in the persisted larder
                // (GearInventory backs VillageInventory), under the canonical belt id, so a fresh
                // hero can heal from turn one. Count is the one owner-tunable constant.
                if (st.GearInventory == null
                    || !st.GearInventory.TryGetValue(DeNelle.Core.HUD.HudCommands.HpPotionId, out var foundingPotions)
                    || foundingPotions != StartingBudget.FoundingHealPotions)
                    failures.Add($"ResetToNewGame founding potion grant missing/wrong (want {StartingBudget.FoundingHealPotions}x '{DeNelle.Core.HUD.HudCommands.HpPotionId}' in GearInventory, got {(st.GearInventory == null ? "null dict" : st.GearInventory.TryGetValue(DeNelle.Core.HUD.HudCommands.HpPotionId, out var fp2) ? fp2.ToString() : "no key")}) — WO-949");
                log.AppendLine("  ResetToNewGame carve-out holds (wallet/breachStyle kept; progression + pets wiped; founding potions granted, WO-949)");

                // ── Tamper gate through the REAL Load: mutate, save, corrupt, reload. ──
                svc2.AddCrystals(50);   // 250 -> 300, saved — distinguishes a real load from fresh defaults
                string signedBlob = memory.Read(SaveSchema.PlayerPrefsKey);
                int payloadIdx = signedBlob.IndexOf('\n') + 1;
                int flipIdx = signedBlob.IndexOf("crystals", payloadIdx, StringComparison.Ordinal);
                if (flipIdx < 0) flipIdx = payloadIdx;   // fall back: flip the first payload char
                var corrupt = new StringBuilder(signedBlob);
                corrupt[flipIdx] = corrupt[flipIdx] == 'c' ? 'C' : 'c';
                memory.Write(SaveSchema.PlayerPrefsKey, corrupt.ToString());

                UnityEngine.Object.DestroyImmediate(go2);
                created.Remove(go2);

                var go3 = new GameObject("CoreSaveOracle_Svc3");
                created.Add(go3);
                var svc3 = go3.AddComponent<GameStateService>();
                awake.Invoke(svc3, null);
                if (svc3.State == null) { failures.Add("service lifetime-3 has no State after Awake"); return; }
                if (svc3.State.Resources.Crystals != 250)
                    failures.Add($"TAMPERED save was LOADED by the real service (crystals={svc3.State.Resources.Crystals}, a rejected load keeps the fresh 250) — the LB-3 HMAC gate is open on the live path");
                else
                    log.AppendLine("  tampered blob rejected by the real Load (fresh defaults stand)");
            }
            catch (Exception ex)
            {
                failures.Add($"service round-trip oracle threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                foreach (var go in created) if (go != null) UnityEngine.Object.DestroyImmediate(go);
                GameStateService.Provider = priorProvider;
            }
        }

        // =====================================================================
        //  I — append-only persistence GAPS, assert-and-name (flag_17 precedent)
        // =====================================================================
        // GameState carries in-memory fields whose own headers admit "NOT yet
        // wired into SaveSchema" — data players lose on every reload. Each gap
        // is a FAIL-BY-DESIGN oracle: it fails TRUTHFULLY today and flips green
        // the moment the save owner adds the PersistedState field (+ schema bump).
        // PetName and Settlements were ONCE on this list but ARE persisted now
        // (Snapshot/ApplyPersisted both carry them; the GameState.cs field
        // comments claiming otherwise are STALE) — asserted GREEN as regression
        // guards so they can never silently fall back off the wire.
        private static void CheckPersistenceGaps(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[I] append-only persistence gaps");

            // The persisted schema = PersistedState's JsonProperty names.
            var persistedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in typeof(SaveSchema.PersistedState).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var jp = f.GetCustomAttribute<JsonPropertyAttribute>();
                persistedKeys.Add(jp != null && !string.IsNullOrEmpty(jp.PropertyName) ? jp.PropertyName : f.Name);
            }
            var gameStateFields = new HashSet<string>();
            foreach (var f in typeof(GameState).GetFields(BindingFlags.Public | BindingFlags.Instance))
                gameStateFields.Add(f.Name);

            // Gap 1 — Tribes (WO-160 roaming raiders): in-memory only.
            if (gameStateFields.Contains("Tribes") && !persistedKeys.Contains("tribes"))
                failures.Add("FAIL-BY-DESIGN (append-only gap): GameState.Tribes (WO-160 tribe members-remaining/cleared/respawn state) has NO PersistedState field — tribe progress RESETS on every reload. Thread 'tribes' through SaveSchema (+ version bump); this oracle then flips green.");
            else if (persistedKeys.Contains("tribes"))
                log.AppendLine("  tribes: now persisted — gap closed (green)");

            // Gap 2 — Wards (WO-112 relight/reach): in-memory only.
            if (gameStateFields.Contains("Wards") && !persistedKeys.Contains("wards"))
                failures.Add("FAIL-BY-DESIGN (append-only gap): GameState.Wards (WO-112 ward relight + earned exploration reach) has NO PersistedState field — relit wards RESET on every reload (the forgetting effect fires spuriously). Thread 'wards' through SaveSchema; this oracle then flips green.");
            else if (persistedKeys.Contains("wards"))
                log.AppendLine("  wards: now persisted — gap closed (green)");

            // Gap 3 — Arena W/L record (ARENA MVP): in-memory + a loose PlayerPrefs
            // mirror (ArenaProgressStore), NOT the unified save. Distinct from
            // arenaDefense (the placed-defender layout), which IS persisted (v19).
            if (gameStateFields.Contains("Arena") && !persistedKeys.Contains("arena"))
                failures.Add("FAIL-BY-DESIGN (append-only gap): GameState.Arena (wins/losses/streak/totalPurse) has NO PersistedState field — the W/L ledger lives only in the loose ArenaProgressStore PlayerPrefs mirror, outside the signed/migrated/validated save. Thread 'arena' through SaveSchema; this oracle then flips green.");
            else if (persistedKeys.Contains("arena"))
                log.AppendLine("  arena record: now persisted — gap closed (green)");

            // Green guards — these two were historical gaps and are now wired; if a
            // refactor drops them off the wire, saves silently lose data again.
            if (!persistedKeys.Contains("petName"))
                failures.Add("REGRESSION: 'petName' fell OFF the persisted schema (it was wired by Audit P2) — the named starter pet would reset on reload again");
            if (!persistedKeys.Contains("settlements"))
                failures.Add("REGRESSION: 'settlements' fell OFF the persisted schema (wired at v21, WO-159) — claims/HP/razed-lockout would reset on reload again");
            log.AppendLine($"  persisted schema carries {persistedKeys.Count} keys; petName + settlements present (green guards)");
        }
    }
}
