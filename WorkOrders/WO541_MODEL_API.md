# WO-541 Frozen Model API Contract (Stage 1 + 2)

This is the SINGLE frozen contract every WO-541 agent builds to. Field names / signatures are
authoritative — do not rename or deviate. All types in namespace `DeNelle.Core.HudModel`, assembly
**DeNelle.Core**. Records are plain data + a `Changed` event + a producer-only mutator. Views READ
props + subscribe `Changed`. Producers (Village) call the mutator. Every mutator calls
`DeNelle.Core.Diagnostics.FlowTrace.Step("HUD", "<model> changed ...")` (throttle hot ones via
`FlowTrace.Throttle("HUD","<model>",1f,...)`).

## Enums (Core)
```csharp
public enum HudContext { Town, Overworld, Battle, Modal }
public enum HudRole    { None, Warrior, Tank, Mage }
public enum WavePhase  { Idle, Countdown, Active, Cleared, Breached, Defeated }
```

## Record structs (Core)
```csharp
public readonly struct PartyMemberRecord {
  public string Name; public string ClassId;
  public int Hp, MaxHp, Mana, MaxMana;
  public string PortraitKey; public bool Alive; public bool Visible;
}   // ctor with all fields
public readonly struct TargetRecord {
  public string Id; public string Name; public float HpFraction; public HudRole Role; public bool Alive;
}
public readonly struct AbilitySlotRecord {
  public string Key, Glyph, Name, Desc, IconKey, AccentHex;
  public bool Equipped; public float CooldownRemaining, CooldownTotal;
}
public readonly struct MinimapPoiRecord { public float X, Z; public string Kind; }
```

## Models (each: sealed class, read-only props, `event Action Changed`, one mutator that assigns + fires Changed + FlowTrace)
- `HeroVitalsModel` — props: `int Hp, MaxHp, Mana, MaxMana, Xp, XpToNext, Level; string ClassId`.
  mutator: `Set(int hp,int maxHp,int mana,int maxMana,int xp,int xpToNext,int level,string classId)`
- `PartyModel` — prop: `IReadOnlyList<PartyMemberRecord> Members`. mutator: `SetMembers(IReadOnlyList<PartyMemberRecord>)`
- `EconomyModel` — props: `int Gold, Wood, Iron, Food, Crystals`. mutator: `Set(int gold,int wood,int iron,int food,int crystals)`
- `WaveModel` — props: `WavePhase Phase; int Number, Max; float CountdownRemaining; bool Imminent; string LookoutStatus; int EnemiesRemaining, EnemiesTotal; string ClearBanner`.
  mutator: `Set(WavePhase,int number,int max,float countdown,bool imminent,string lookout,int remaining,int total,string banner)`
  - ⚠ **AMENDED 2026-09-18 (WO-1864) — the ONE break in this otherwise-frozen contract.** `EnemiesLive`
    is now **`EnemiesRemaining`**, and `EnemiesTotal` is the wave's **roster**, not the live list's
    length. The old name was fed by `WaveManager.LiveEnemies` alone, which the WO-1113 concurrency cap
    pins AT the cap — so the counter published a constant "8 enemies remain" for 101 seconds of the
    owner's wave 26 while 26 enemies were still held (proving lines in
    `WORK_ORDER_1864_...RESULT.md`). The rename is deliberate: a property named `Live` that must
    carry live+held would be the next seat's bug. Arithmetic lives in
    `Assets/_Modules/Core/HudModel/WaveCounterMath.cs`; pinned by `WaveCounterHonestyRegression`.
- `TargetModel` — props: `bool HasTarget; string Name; int Level, Hp, MaxHp; float HpFraction; HudRole Role; bool Locked`.
  mutator: `Set(bool has,string name,int level,int hp,int maxHp,float frac,HudRole role,bool locked)`; plus `Clear()`
- `TargetCycleModel` — prop: `IReadOnlyList<TargetRecord> Targets`. mutator: `SetTargets(IReadOnlyList<TargetRecord>)`
- `AbilityLoadoutModel` — prop: `IReadOnlyList<AbilitySlotRecord> Slots` (4). mutators: `SetSlots(IReadOnlyList<AbilitySlotRecord>)` and `SetCooldown(int index,float remaining,float total)`
- `WorldMetricsModel` — props: `int HeartHp, HeartMaxHp; float HeartPct; int TowersBuilt, TowersMax, Population; float PassiveXpPerMin; int PassiveTowerCount; int ForgettingLevel; int WardsLit, WardsTotal; string WardsSummary; IReadOnlyList<MinimapPoiRecord> Minimap`.
  mutators: `SetMetrics(...)` (all scalar fields) and `SetMinimap(IReadOnlyList<MinimapPoiRecord>)`
- `MomentumModel` — props: `int Combo, KillStreak, Stars; float BattleElapsed, KeepStarSeconds`. mutator: `Set(int combo,int killStreak,int stars,float elapsed,float keepStar)`
- `EchoModel` — props: `int EchoCount, MaxEchoes; float Silo, FillFraction`. mutator: `Set(int count,int max,float silo,float fill)`
- `HudContextModel` — props: `HudContext Context; bool InVillage, CombatActive, ModalOpen`. mutator: `Set(HudContext ctx,bool inVillage,bool combat,bool modal)` (fires Changed ONLY when Context actually changes value; always FlowTrace the transition)

## Facade + exposure (Core)
```csharp
public interface IHudModel {
  HeroVitalsModel HeroVitals { get; } PartyModel Party { get; } EconomyModel Economy { get; }
  WaveModel Wave { get; } TargetModel Target { get; } TargetCycleModel TargetCycle { get; }
  AbilityLoadoutModel Abilities { get; } WorldMetricsModel World { get; }
  MomentumModel Momentum { get; } EchoModel Echo { get; } HudContextModel Context { get; }
}
public sealed class HudModel : IHudModel { /* constructs + holds one instance of each model */ }

// CoreServices partial (mirror the existing RegisterHud/UnregisterHud pattern in CoreServices.cs):
public static partial class CoreServices {
  public static IHudModel HudModel { get; private set; }
  public static void RegisterHudModel(IHudModel m) { HudModel = m; FlowTrace.Step("HUD","HudModel registered"); }
  public static void UnregisterHudModel(IHudModel m) { if (ReferenceEquals(HudModel,m)) HudModel = null; }
}
```

## Producer side (Village assembly) — Stage 2
- One DDOL `HudModelHost : MonoBehaviour` constructs `new HudModel()`, calls `CoreServices.RegisterHudModel` in Awake, `Unregister` in OnDestroy, and owns the producer components.
- One producer per model writes it from the existing systems (read-only): HeroVitals←HeroHealth/HeroAbilities/HeroProgression; Party←party source; Economy←EconomyService/WisdomCurrencyService; Wave←WaveManager; Target←HeroTargetIndicator/Enemy/EnemyBrain (map EnemyRole→HudRole); TargetCycle←Enemy scan+distance sort; Abilities←HeroLoadout+AbilityCatalog+HeroAbilities cooldowns; World←Heart/tower/population sources; Momentum←BattleStarRating+battle clock; Echo←EchoService.
- `HudContextEvaluator` is the ONE context writer: consolidates `BattleHudVisibilityManager.EvaluateMode` inputs (wave phase, BattleController/ATB, BattleLock.IsInBattle, HubScenes enemy-owned/raid) + `VillageHudController.InVillage` (scene+town-ring) → writes `HudContextModel`.
- DARK: nothing reads the model yet (views migrate in Stage 3). Registering + writing is harmless/additive.

## Assembly law (binding)
Models live in DeNelle.Core (primitives + Core enums only — NO UnityEngine UI, NO Village types).
Village WRITES (refs Core). HUD/BattleATB READ (ref Core). Never a Village↔HUD edge.

> AMENDMENT 2026-07-03 (P4, HUD-Obsidian program): HeroVitalsModel.Set gains `wisdom`; HudContextModel.Set gains `buildMode` (+ HudContext.BuildMode appended enum-safe); new CastModel {CasterName, AbilityName, Progress01, Visible}. Additive only; all existing comparisons unmoved. See docs/HUD_OBSIDIAN_ARCHITECTURE_2026-07-03.md.
