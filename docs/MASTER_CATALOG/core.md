# MASTER CATALOG — Core (`Assets/_Modules/Core`)

> **Verified from code 2026-08-02** (HEAD ~b77a178e, branch `wip/village2-and-f8-tickets`). Every claim
> below was read from the actual `.cs` files, NOT from their comments — file banners lie (the worst
> offenders are ledgered in RISK at the end). Supersedes the 2026-06-12 body + the 07-22/07-26 addenda.
> **Headline state: SaveSchema v36 * SaveMigrator top step 36 * CoreServices 8 slots *
> FeatureFlags 62 flags (12 XML-summary lies) · PanelId 0–15 (RealmMap=15) · ZoneManager home box 52/52.**

Foundation layer. Three asmdefs live here (verified from the `.asmdef` files):

| Assembly | rootNamespace | refs |
|---|---|---|
| `DeNelle.Core` (`DeNelle.Core.asmdef`) | `DeNelle.Core` | UniTask, Unity.TextMeshPro, Unity.Addressables, Unity.ResourceManager — **first-party nothing** |
| `DeNelle.AI` (`Scripts/AI/DeNelle.AI.asmdef`) | `DeNelle.AI` | DeNelle.Core |
| `DeNelle.Core.Tests` (`Tests/…asmdef`, Editor) | `DeNelle.Core.Tests` | DeNelle.Core, **DeNelle.Data**, TestRunner |

**Cross-module pattern:** Core defines interfaces + static seams; implementing modules register concrete
services via `CoreServices` / per-feature static hooks (PanelRouter, DialogueService, HudCommands,
JobEffectRegistry, …) so Village↔HUD↔Wallet never reference each other (CLAUDE.md §5). Reflection
bridges survive only in `PersistenceBridge` (→Village WaveManager) and `SceneRouter`'s return-point
hero warp (→Village HeroLocomotion, `SceneRouter.cs:350,383`).

---

## DELTA 2026-09-02 — the REMOTE rails (tunables + catalogs) and the ONE over-time engine

Three new Core clusters landed on 2026-09-02. All three are read-verified at source, and all three
share one shape, stated identically in each file's own header:

> ⛔ **NO ROW, NO NETWORK, NO SERVER, NO PARSE => TODAY'S BEHAVIOUR, EXACTLY.**

### `Core/Ops/RemoteTunables.cs` + `Core/Ops/RemoteTunablesService.cs` (`DeNelle.Core.Ops`) — PROD-022

The database-backed knob rail: candidate mitigations for the PROD-022 Pi Browser crash loop ship in
ONE build, each behind its own knob, all defaulting to today's shipping value, so a bisect is a flag
flip instead of a ~30-minute WebGL rebuild.

- **Split of duties, copied deliberately from `MaintenanceCatalog` / `MaintenanceService`:**
  `RemoteTunables` is **state + parse only** and knows nothing about transport;
  `RemoteTunablesService` owns **transport and only transport**. The reason is testability — the knob
  table stays headlessly drivable by a regression oracle with no network and no PlayMode.
- **⚠ DO NOT RESTATE THE KNOB COUNT OR THE KEY LIST HERE.** `RemoteTunables.Registry`
  (`RemoteTunables.cs:325`) is the machine-readable source of truth (key, kind, default, what ON does,
  which hypothesis it tests); the owner-facing list `docs/PROD022_TUNABLE_FLAGS.md` is WRITTEN FROM IT.
  The registry changed three times in one evening — read the array, and read the doc for the prose.
  Key constants start at `RemoteTunables.cs:145` and group as `pi.*` / `assets.*` / `visuals.*` /
  `trace.*` / `combat.*` / `vfx.*`.
- **Precedence, and it composes with `FeatureFlags` rather than fighting it:**
  LOCAL PlayerPrefs `ff.tun.<key>` (a human at the device) beats REMOTE payload (the owner at the
  database) beats DEFAULT (what this build hardcodes). The prefix is `ff.tun.` and deliberately NOT
  plain `ff.` so a tunable key and a `FeatureFlags` name can never collide in one PlayerPrefs
  namespace (`RemoteTunables.LocalPrefix`, `:131`). Every resolve carries a provenance string
  (`default` / `remote` / `local-playerprefs` / `remote-cached`) and is traced once per key.
- **Non-blocking is STRUCTURAL, not a comment** — this is a crash-loop ticket, so adding a boot
  failure mode would be self-defeating. `Bootstrap()` calls `PollForeverAsync().Forget()`; there is no
  barrier, no `WaitForCompletion`, and nothing anywhere yields on it. `req.timeout` is set, because a
  captive-portal socket otherwise never completes.
- **⭐ THIS ONE CACHES, and that is a DELIBERATE divergence from `MaintenanceService`** (which is
  owner-ruled cache-free, because a stale kill switch is a safety question). The knobs that matter
  most are read DURING BOOT (`StructureContentWarmer.Boot`, AfterSceneLoad), so a value that only
  arrived after a round trip would be too late on the very launch it was set for, forever. The cache
  is read at BeforeSceneLoad and the poll starts at AfterSceneLoad — that ordering is load-bearing.
  A 404 CLEARS the cache; a fresh payload REPLACES it wholesale, so it can never resurrect a knob the
  owner turned off.
- **NO AUTH** — `/api/client-tunables` is public read and must resolve before sign-in (these knobs
  govern boot-time asset policy, long before any identity exists). Do not call `BackendRequestSigner`
  from here.
- **⚠ ONE HONEST EXCEPTION to the invariant, stated in the file rather than hidden (WO-1327):** the
  two `vfx.*` knobs are BUG FIXES, so their defaults are the CORRECTED values. An empty table gives
  this build's fixed VFX behaviour, not the art pack's original — and the previous behaviour is one
  flip away. FlowTrace tag `Tunables`.

### `Core/Data/RemoteCatalogSource.cs` + `RemoteCatalogService.cs` + `RemoteCatalogOverrides.cs` — WO-1331

The seam that finally assigns `CanonicalJson.Source`, converting authored canonical data into
remotely-updatable content **with no call-site change anywhere in the game**.

- ⛔ **FLAG-GATED OFF.** `FeatureFlags.RemoteCatalogs => Get("catalogremote", defaultOn: false)`
  (`Assets/_Modules/Core/FeatureFlags.cs:1361`). With the flag off `RemoteCatalogSource` is **never
  constructed and never installed** — `RemoteCatalogService.Install()` returns before touching
  `CanonicalJson.Source`, which still holds the `LocalJsonCatalogSource` its own field initializer
  gave it. The flag-off claim is therefore provable by READING, not only by testing.
- **`RemoteCatalogSource` is a DECORATOR, not a replacement** — it owns no loading. With no validated
  override for a catalog (the normal case, and the only case with no database row) it delegates
  verbatim to the inner `LocalJsonCatalogSource`, so the resolved text is the SAME STRING the game
  would have got with the file absent. A null inner is replaced with a fresh `LocalJsonCatalogSource`
  so this type can never be the reason a catalog fails to resolve.
- **Validation happens BEFORE anything is replaced, and a payload is accepted WHOLE or rejected
  WHOLE** — never a partial merge, because a half-applied catalog overwriting a good one is strictly
  worse than no feature at all. Each candidate must (1) name an allowlisted path (deny list checked
  FIRST) that actually exists in the compiled build, (2) be non-empty and under `MaxCatalogBytes`,
  (3) parse through `Guard.Try` (which rejects malformed AND truncated text), and (4) have the SAME
  ROOT KIND as the compiled copy and, for an object root, carry every top-level key it has. One
  failure rejects the whole payload with `FlowTrace.Fail` and changes nothing.
- **Server-authoritative data is permanently OUT OF SCOPE and the boundary is enforced here in code,
  not in prose** — prices, entitlements, grants, base-unit amounts, token decimals and quote TTL stay
  decided in `api/_lib/purchase-catalog.js`.
- Serving an override logs a **`FlowTrace.Throttle`, not a `Step`** — an overridden catalog is NOT the
  shipping catalog and a capture must never let that read as ordinary narration (CLAUDE.md §12).
  FlowTrace tag `CatalogRemote`.

### `Core/Combat/OverTimeEffects.cs` (`DeNelle.Core.Combat`) — the ONE over-time engine, WO-1330

`OverTimeEngine<TTarget>` + `OverTimePulse<TTarget>` + `OverTimeTuning`. It **replaced four ad-hoc
tick loops**, and the file records what was actually true at source — which is neither what the CLI
first reported nor quite what the owner's correction assumed:

- The CLI first said "the DoT already exists" and pointed at `DeNelle.BattleATB`. **Wrong in the way
  that matters:** BattleATB is the SUPERSEDED turn-based engine and the shipping game cannot cast into
  it. The live real-time path `HeroAbilities.ResolveEffect` already dispatched `dot` and
  `healOverTime`, but over **three unrelated loops** — `HeroAbilities.BurnDoT` (coroutine, hardcoded
  1s tick), `HeroAbilities.PoisonDoT` (a SECOND coroutine, byte-for-byte the same loop), and the
  `_hpOverTime` per-FRAME drip in `Update`. None tunable; no mage ability able to reach any of them.
- `Core/Combat/CombatStatusTracker.cs` **is live and was never a candidate for the tick** — it is a
  HUD TIMER BAG that stores when a status ENDS, with no magnitude, no tick and no sink, so it can
  record that a foe is burning and can never make the burn hurt. It is the right home for the ROW;
  the two are used together, exactly as before.
- ⛔ **LIVENESS IS A REQUIRED CONSTRUCTOR ARGUMENT THAT THROWS ON NULL.**
  `OverTimeEngine(Func<TTarget,bool> isAlive)` (`:303`) throws `ArgumentNullException` (`:306`). The
  engine **cannot be built without saying how to test whether the target is still alive** — that is
  the design point, not a nicety: the classic over-time bug is ticking a corpse.
- **Pure by construction:** no MonoBehaviour, no coroutine, no `UnityEngine.Time` — the clock is a
  parameter (`Advance(now, onPulse)`, `:455`). Copied from the `HeroAbilities.TickManaOverTime`
  precedent: EditMode never runs `Update`, so an over-time effect whose ticking cannot be OBSERVED by
  a gate is one nobody can prove ticks (CLAUDE.md §12). `OverTimeEffectRegression` drives this type
  with a fake clock and counts the pulses.
- **One mechanism, both signs:** generic over the target type, so "damage a foe" and "heal the hero"
  are two closed generic types over ONE body. **Magnitude is always a POSITIVE quantity and the
  direction travels in `OverTimeKind`**, so no call site can heal by passing a negative damage (the
  classic sign bug). Tuning (`OverTimeTuning.TickSeconds` / `MagnitudeScale` / `DurationScale`) reads
  through the `combat.overTime*` remote tunables above. FlowTrace tag `OverTime`.

---

## DELTA 2026-08-21 — new Core surfaces (Defense data model, DefenseMapPlate, RaidStrings, RaidCooldownRecord, `AddRawImage`)

Read from source 2026-08-21. Where this block and the 08-02 body disagree, this block wins.

**HEADLINE CORRECTION: `PanelId` no longer stops at 15.** Verified in `Core/UI/PanelRouter.cs` (the
enum lives in that file, not in a `PanelId.cs`): `DefenseReport = 18`, `BattlePass = 19`,
`MonthlyLedger = 20`. The 08-02 header line "PanelId 0-15 (RealmMap=15)" is stale.
Read the save-schema version off `SaveSchema.CurrentVersion` as always — every copied number below is
stale by construction; the const is the authority. ⛔ **Do not "fix" those numbers by writing today's
value — delete the number and name the source.** This file has already been caught stale at v36 while
live was v38, and the index was caught again at v38 while live had moved on (2026-09-02).

### ⛔ NEW DIRECTORY `Core/Defense/` — **UNTRACKED IN GIT AS OF THIS WRITE.** See the P1 ledger entry in `docs/MASTER_CATALOG.md`.

- **`Defense/DefenseReport.cs` (576)** `DeNelle.Core.Defense` — the pure persisted data model for one
  attack on the player's town. Enums `AttackerSource` · `DefenseOutcome` · `DefenseResolution` ·
  `StructureState` · `DefenseBand`. Records `AttackerUnitRecord` · `AttackerIdentity` ·
  `DefenderSnapshot` · `BreachRecord` · `AttackPathPoint` · `StructureOutcome` · `StakesLedger`
  (with `StakesLedger.Interim()`) · `DefenseOutcomeRecord` (`NewEmpty()`, `Normalize()`), plus
  `static class LayoutFingerprint` (`Compute(IEnumerable<string>)`).
  `DefenseResolution.ResolvedInAbsentia` and `DefenderSnapshot.Garrison` exist **unused today on
  purpose** — they are the shape a future offline fast-forward drops into with no data change.
- **`Defense/DefenseReportLedger.cs` (159)** — static store: `Append(record)` · `All()` ·
  `NewestFirst()` · `TryGet(reportId)` · `UnreadCount()` · `MarkRead(reportId)` · `Clear()`.

### `Core/UI/DefenseMapPlate.cs` (390) `DeNelle.Core.UI` — static, + nested `sealed class Plate`

The FROZEN "where it went wrong" diagram. `Build(Transform parent, DefenseOutcomeRecord)` ->
`Plate` (`Relayout()`, internal segment list) · `DescribeMarks(record)` · `Compass(dx, dz)`.
⭐ **Why it is not `HudMinimapWidget`, asked and answered at source:** the minimap (WO-828) is a LIVE
hero-centred widget fed by `Func<>` providers wired by `HudKitController`. This plate draws
positions captured minutes or days ago, of structures that may no longer exist, in a town since
rebuilt — there is no live world to read, so feeding it would mean stubbing every provider with a
constant. It is also mechanically impossible: `DeNelle.HUD` references Core + Data ONLY (CLAUDE.md
§5's one enforced invariant), so `DeNelle.Village` cannot reach it.
**Reuse is deliberate, not incidental:** marks resolve through `RealmAtmosphereStyle.PinAscii`, so a
triangle means "threat" on every map surface in the game — this is the THIRD reader of that table,
not a third vocabulary. It also keeps the WO-828 cost rule verbatim: **no Camera, no RenderTexture,
no render pass, nothing ticks** — static Images + TMP labels built once and destroyed with the
detail view.
⛔ **Colourblind law, structural:** every mark is a distinct GLYPH first (`^` breach, `#` destroyed,
`O` damaged, `o` the Heart), the FIRST breach carries the literal text label "1st BREACH" plus a
ring, later breaches carry their ordinal as text, the attack path is a LINE, and a legend spells
every glyph out in words. Desaturate the plate and every fact survives.

### `Core/UI/RaidStrings.cs` (158) `DeNelle.Core.UI` — static

Keys-only accessor for the per-camp raid cooldown copy: `Get(key)` · `Format(key, args)` ·
`Humanise(double remainingSeconds)` · `Reload()`. Sentences live in `canon-strings.json` in BOTH
canonical copies, ASCII-only. Loader mirrors `PromoStrings` verbatim (flat string->string map via
`CanonicalJson`, Resources first, StreamingAssets fallback, WebGL-safe); a missing key returns the
visible `[[missing:key]]` marker AND self-reports through `FlowTrace` — never a silent blank.
⛔ Every cooldown state has a SENTENCE and the sentence is the primary signal; a tint is decoration
on top of it. Do not "simplify" a card by dropping the label and keeping the colour.

### `Core/State/RaidCooldownRecord.cs` (105) `DeNelle.Core.State` — pure data

One record per cleared raid camp: `ConfigId`, `StartedUnixMs`, `DurationSeconds`, `ServerAnchored`;
ctors + `static Normalize(r)`. It lives in Core because it is a field on
`SaveSchema.PersistedState` (a Village type could never be persisted there) — the
`DefenseOutcomeRecord` precedent. **No UnityEngine types on a persisted field.**
⛔ **The clock is NOT stored here and that is the point** — `StartedUnixMs` is written by
`RaidCooldownService` from `TimeSource.NowUnixMs()`, so exactly one place decides what "now" means
for a raid cooldown.
⚠ **Duration is persisted, not re-derived**, so retuning the balance table cannot silently lengthen
a cooldown a player already paid to start — the same reasoning as WO-911's persisted PAID BASKET.
**No schema bump:** nullable on the wire, absent on an older save falls to `GameState`'s empty-list
initializer, which is byte-identical to today's behaviour.

### `Core/UI/ElarionUiKit.cs` — NEW primitive `AddRawImage` (`ElarionUiKit.cs:2338`)

`public static RawImage AddRawImage(Transform parent, string name, Vector2 anchorMin,
Vector2 anchorMax, Texture texture, Color color, bool raycastTarget = false)`.
The kit previously had **zero** `RawImage` references. It returns the typed component rather than
the GameObject that `AddImage` returns, because the entire reason to reach for the primitive is to
animate `uvRect`. `raycastTarget` defaults **false**: a textured surface built this way is
decoration over content, and swallowing the tap on the card underneath is the default bug.
First caller: The Night Market's aurora (WO-1050 Lane G). The law it satisfies — presentation is
constructed in the one file sanctioned to touch raw uGUI, never hand-rolled in a feature module
(`ARCHITECTURE_PRINCIPLES` §2; `UiObsidianConformanceRegression`).

### `Core/Geometry/WeaponOrientHelper.cs` (1395) — grew the per-mesh sheathe derivation

New `TryResolveSheathedTipSign(GameObject prop, Transform gripRoot, out SheathedTipResolution)`
(`:889`) plus the resolution/extent reporting types (`SeatSource` precedence ladder: authored row >
`manual: true` catalog row > MEASURED > hand-typed archetype constant).
⛔ **It reads `mesh.bounds`, not vertices, and that is load-bearing:** the shipped props have
Read/Write **OFF**, which makes every vertex-based approach silently inert **on device** while
appearing to work in the editor.
Decision margins are explicit and DECLINE rather than guess: `GripEndDecisionMargin` 0.15,
`ShieldDecisionMargin` 0.10, `ShieldFaceBand` 0.25, `CrossGuardSpikeRatio` 1.6, `ShieldDishMargin`
0.05. Below a margin it `Warn`s and keeps existing behaviour.
⚠ **Deliberate non-members:** axe / hammer / mace / wand / crossbow all resolve to
`WeaponArchetype.Unknown` and DERIVE NOTHING — the owner's 2026-08-19 spec covers bow, sword, staff
and shield only, and each of those rules leans on a property the excluded families lack.
⚠ **A comment-vs-doc conflict is recorded IN the code and is worth repeating:** the STAFF grip is
"three quarters of the way up Y" (owner ruling 2026-08-19), which **supersedes**
`docs/WEAPON_ARMOR_ORIENT_LOGIC.md`'s older "staff -> grip lower third". Both values are kept in the
source so a reader who finds the old third in an older doc copy can tell which is current.

---

## ROOT FILES

| Class | Path | Responsibility (verified) | Bootstrap |
|---|---|---|---|
| `CoreServices` (static) | `CoreServices.cs` | Cross-asmdef registry, **8 slots**  -  see next section. |  -  |
| `FeatureFlags` (static) | `FeatureFlags.cs` | **62** demo/web feature gates — see dedicated section. | — |
| `SceneRouter` (static) | `SceneRouter.cs` | Scene routing — see dedicated section. | — |
| `HubScenes` (static) | `HubScenes.cs` | ONE hub/overworld/raid/enemy-scene classifier — see Behaviors. | — |
| `Constants` (static) | `Constants.cs` (38L) | Solana Admin/Vault/Sol/Usdc literals + `TowerSlots = 9`. | — |
| `DevBootScene` (static) | `DevBootScene.cs` | `-bootScene <Scene>` CLI arg → load scene, skip onboarding. Arg-gated no-op otherwise. | `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` |
| `IntroLauncher` (static) | `IntroLauncher.cs` (19L) | `Action Play` hook: DialogueUI sets, Title button invokes. | — |
| `SeekerBootstrap` (static) | `SeekerBootstrap.cs` (161L) | Frame pacing + Seeker device detect → quality tier + targetFrameRate. | `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` |
| `OnboardingMode` (static) | `OnboardingMode.cs` | FAST-PATH (default) vs FULL-TUTORIAL PlayerPrefs switch (`onboarding.fullTutorial`). | — |
| `DialogueEventBus` (static) | `Events/DialogueEventBus.cs` | Gameplay→dialogue latch bus (`Raise/HasFired/Clear`), case-insensitive. | — |
| `DialoguePortrait` (static) | `DialoguePortrait.cs` (16L) | `Forced` Resources path overriding speaker portrait. | — |
| `DialogueResetService` (static) | `DialogueResetService.cs` (73L) | Wipes dialogue/Yarn state keys for a fresh New Game. | — |
| `BuildModeState` (static) | `BuildModeState.cs` (52L) | WO-702 cross-module "is the builder open?" seam (pauses Sylas FTUE dialogue). | — |
| `MagentaGuard` (MonoBehaviour) | `MagentaGuard.cs` (469L) | TKT-1 runtime safety net: rebinds broken Built-in/Standard mats → URP/Lit so nothing renders magenta in builds. | self-installing |
| `AwarenessState` / `FormationType`+`FormationContext` / `ResourceType` (enums) | root | Perception enum · 5 pack shapes + context · Iron/Wood/Food/AetherCrystal mirror. | — |
| `TripoMaterialFixer` / `TreeOfLifeMaterialFixer` / `EnvironmentTreeMaterialFixer` / `GroundZFightFixer` (MonoBehaviours) | root | Material/visual fixers (Tripo FBX→URP with optional forced texture via `SetForcedTexture`; Village2 centrepiece grey-tree net — also SceneRouter's tree locator; WO-332 WebGL white-tree; WO-333 ground z-fight Y-0.05). | per-object |

---

## CoreServices  -  8 slots (`CoreServices.cs`)

Register-in-Awake / Unregister-in-OnDestroy; callers null-check (`CoreServices.Hud?.…`). Replacing a
live registration logs a FlowTrace `Warn` (`:51`, `:115`, `:187`).

| Slot | Interface | Registered by | Lines |
|---|---|---|---|
| `Hud` | `IVillageHud` (`HUD/IVillageHud.cs`) | VillageHudController | 43–59 |
| `HudModel` | `IHudModel` (`HudModel/HudModels.cs`, WO-541) | HudModelHost | 67–80 |
| `Population` | `IPopulationService` (`Population/IPopulationService.cs`, WO-587) | PopulationService | 88–100 |
| `Audio` | `IAudioService` (`Audio/IAudioService.cs`) | AudioService | 107–123 |
| `Jupiter` | `IJupiterService` (`Web3/IJupiterService.cs`) | JupiterSwapService | 130–144 |
| `WalletSigner` | `IWalletSigner` (`Web3/IWalletSigner.cs`) | WalletService on Connect | 155–169 |
| `SceneLinkResolver` | `ISceneLinkResolver` (`World/ISceneLinkResolver.cs`, WO1) | SceneLinkResolverHost | 179–198 |
| `VillageBridge` | `IVillageBridge` (`Bridging/IVillageBridge.cs`, WO-1510) | VillageBridgeService (`_Modules/Village/VillageBridgeService.cs`, RuntimeInitializeOnLoadMethod) |  -  |

**WO-1510 (2026-09-06):** the `VillageBridge` slot REPLACED the four `Type.GetType("DeNelle.Village...")`
sites that used to sit inside Core (`SceneRouter.cs:510,523`, `PersistenceBridge.cs:174`,
`BreakCaptureHarness.cs:491`)  -  a layering inversion, since `DeNelle.Core.asmdef` references no game
assembly. Core now names no Village type. Pinned by `Assets/Editor/Regression/CoreReflectionSourceRegression.cs`,
which fails on any `Type.GetType("DeNelle.Village` under `Assets/_Modules/Core`.

---

## FeatureFlags — 62 flags (`FeatureFlags.cs`, 876L)

Resolution: `Get(name, defaultOn)` (`:667`) — PlayerPrefs `ff.<name>` 0/1 wins, else the coded default.
`IsDevBuild` = editor OR Debug.isDebugBuild (`:664`). `ApplyUrlActivationOnce()` (`:706`) lets a WebGL
URL query flip ONLY the allow-list `{trace, stakedemo, skrpreview}` (`:682–697`). Editor menu toggles
for BlinkChrome/OverworldEncounter/LockOn/StakeDemo/SkrPreview/CombatHud611 (`:750–873`).

**READING RULE (BINDING): the `defaultOn:` argument in code is the ONLY truth. When an XML `<summary>`
disagrees, the trailing `//` comment on the property line (owner reversal note) is the current ruling.**

All 62, actual default, with the **12 XML-summary lies** marked ⚠ (XML states the OPPOSITE default):

| Flag (`ff.<key>`) | Default | Line | Note |
|---|---|---|---|
| Raid (`raid`) | ON | 27 | core raid loop closed |
| Arena (`arena`) | OFF | 33 | V1 descope; code kept |
| SingleHero (`singlehero`) | ON | 40 | ATB party = hero only |
| BlinkArmor (`blinkarmor`) | OFF | 52 | armor swap junked |
| KnightOnly (`knightonly`) | **OFF** (since 2026-08-05) | 67 | RETIRED as default — roster is Knight/Ranger/Mage via `PlayableHeroes`; set `ff.knightonly`=1 to restore solo-Knight |
| BaseBuilding (`basebuilding`) | OFF | 67 | convert-on-clear etc. gated |
| BuildTimers (`buildtimers`) | ON | 75 | WO-612 CoC timers |
| ⚠ RaidContinuousWalk (`raidwalk`) | **OFF** | 88 | XML says "Default ON"; WO-771 comment locks Teleport/Deploy |
| OverworldLeaderOnlyRoam (`overworldleaderonlyroam`) | ON | 95 | rep-only roam |
| OutpostTravel (`outposttravel`) | OFF | 104 | no outpost fast travel |
| BlinkChrome (`blinkchrome`) | OFF | 111 | hide our UI dressing |
| ⚠ WebTrace (`webtrace`) | **ON** | 120 | XML says "Default OFF (don't spam the DB)" — WebGL streams traces to Neon by default |
| BuildingUpgradePanel (`buildingupgradepanel`) | ON | 128 | MVVM WC3 perk grid |
| ⚠ PartyShop (`partyshop`) | **ON** | 138 | XML says "Default OFF" |
| CustomDialogue (`customdialogue`) | ON | 145 | Yarn fully removed (WO-557) — OFF has no fallback |
| ⚠ OverworldEncounter (`overworldencounter`) | **ON** | 154 | XML says OFF; owner REVERSAL 2026-07-30 (F8 seq511) in trailing comment |
| ⚠ RegionRoam (`regionroam`) | **ON** | 162 | XML says OFF per 07-26; reversed 07-30 in trailing comment |
| BypassPetSelect (`bypasspetselect`) | ON | 171 | intro skips PetSelect |
| RuntimeWorldSeam (`runtimeworldseam`) | OFF | 183 | superseded by merged world — dead code pending removal |
| EnemyInjuredStance (`enemyinjured`) | ON | 190 | |
| HeroInjuredStance (`heroinjured`) | ON | 198 | |
| EnemyRootedCast (`enemyrootedcast`) | ON | 205 | |
| EnemyStructureAwareness (`enemystructureaware`) | ON | 217 | all-direction structure sweep |
| EnemyWeapons (`enemyweapons`) | OFF | 226 | weaponless until grip perfected |
| ⚠ BattleHud9Zone (`battlehud9zone`) | **ON** | 242 | XML's last word says "ships OFF for V1"; trailing PREVIEW comment = ON |
| WaveAutoStart (`waveautostart`) | ON | 250 | prepare countdown auto-arms in hub |
| WaveBreachToAtb (`wavebreachtoatb`) | OFF | 260 | breach resolves in-hub, no ATB swap |
| DungeonRealtimeBattle (`dungeonrealtime`) | ON | 273 | dungeon fights → BattleArena, ATB retired-reversible |
| DevHotkeys (`devhotkeys`) | OFF | 283 | global dev-hotkey kill switch (F8/F9 unaffected) |
| DevResourceTool (`devresourcetool`) | **IsDevBuild** | 299 | store-hardened: OFF in release APK |
| FlagButton (`flagbutton`) | **IsDevBuild** | 315 | mobile F8 chip; OFF in release APK |
| HubAmbientVfx (`hubambientvfx`) | ON | 325 | |
| LockOn (`lockon`) | OFF | 334 | WO-512 soft lock-on, unproven |
| CastleMoat (`castlemoat`) | ON | 346 | |
| ⚠ MergedWorld (`mergedworld`) | **ON** | 358 | XML says "Default OFF until baked"; trailing TEST-BUILD comment = ON. Drives `SceneRouter.Castle` |
| HomeReturnPortal (`homereturnportal`) | ON | 366 | |
| ⚠ GateTraversal (`gatetraversal`) | **OFF** | 379 | XML says "Default ON"; 07-26 comment: warp teleport reverted, hero walks through |
| CastleEditorBridgeSeam (`castleeditorbridgeseam`) | OFF | 385 | |
| GateBeacon (`gatebeacon`) | OFF | 392 | |
| ⚠ OutpostCaves (`outpostcaves`) | **ON** | 403 | **WORST lie — XML says "this ships OFF" with NO correcting comment**; the resolver the XML says "DOES NOT EXIST YET" gates what ON arms |
| DungeonPortals (`dungeonportals`) | ON | 419 | XML self-corrects (ON since 07-13) |
| WorldFeel (`worldfeel`) | ON | 432 | |
| NoAutoHeal (`noautoheal`) | ON | 441 | field HP/MP persist; safe-zone heals |
| CombatFeel (`combatfeel`) | ON | 451 | |
| TutorialV2 (`tutorialv2`) | ON | 462 | data-driven FTUE |
| HeroPackage (`heropackage`) | ON | 473 | Paladin package |
| KnightV3 (`knightv3`) | ON | 484 | checked BEFORE heropackage |
| ⚠ MocapLocomotion (`mocaploco`) | **ON** | 497 | XML says "Default OFF until felt-approved"; trailing comment: approved 07-04 |
| WeaponGripInfer (`weapongripinfer`) | OFF | 505 | legacy inference path |
| StakeDemo (`stakedemo`) | OFF | 517 | URL-activatable |
| SkrPreview (`skrpreview`) | OFF | 535 | store-hardened OFF; URL-activatable |
| RealmStorePurchase (`realmstorepurchase`) | **IsDevBuild** | 545 | buy CTA rail; NOT URL-activatable |
| CombatHud611 (`combathud611`) | ON | 561 | XML self-corrects (approved = default) |
| BattleHudVm (`battlehudvm`) | OFF | 572 | ATB HUD VM A/B |
| SheathedDrawnRotFallback (`sheathdrawnrot`) | ON | 585 | XML self-corrects at end |
| PetCombat (`petcombat`) | OFF | 595 | pets harvest/companion only |
| ⚠ Barracks (`barracks`) | **ON** | 605 | XML says "When OFF (default)"; WO-771 trailing comment flips ON (raid roster needs it) |
| Colosseum (`colosseum`) | OFF | 614 | |
| WallsTab (`wallstab`) | OFF | 621 | |
| ⚠ PoiCallouts (`poicallouts`) | **ON** | 635 | XML says "Default OFF (dark-ship)"; trailing comment flipped ON for felt-test |
| DungeonFpv (`dungeonfpv`) | ON | 650 | FPV dungeon camera; wins over iso |
| DungeonCameraIso (`dungeoniso`) | OFF | 657 | legacy top-down escape hatch |

(Removed flags, do not resurrect: `ff.herotalents` — eyes-sweep 07-06, comment `:42`;
`ff.strategicplacement` — WO-682, always-on, comment `:637`.)

---

## State/ (`DeNelle.Core.State`) — the save/persistence spine

### GameState (ScriptableObject, sealed — `State/GameState.cs`, 567L)
Pure-data persisted store (~66 fields; asset `State/GameState.asset`). `SchemaVersion = SaveSchema.CurrentVersion` (`:34` — the field is DERIVED from the const, so there is no second number to keep in sync; read the value off `SaveSchema.CurrentVersion`).
Field map (line = declaration; ALL append-only at the end per the save law):

- Player: `Onboarded :38`, `BestWave :40`, `HeroClass` (HeroClassOpt `:45`), `BoundWallet :47`.
- Resources: `Resources` (ResourceBalance.Starter {250,80,15} `:51`), `Voidshards=5 :53`,
  `AetherCrystals` **DEPRECATED v18, kept 0** `:58`, `Stone=20 :60`, `Iron=5 :62`, `Wood=15 :64`, `OwnedItemIds :66`.
- Pets: `Pets :70`, `StarterPetId :72`, `PetBonds :74`, `OwnedPets :76`, `PetActiveSlots` (v34 deploy map `:88`), `PetName` (WO-277 `:280`).
- Village: `Towers`/`TowerAbilities` ([0]×9 `:92,:94`), `WallLevel :96`, `BuildingCooldowns :98`,
  `PendingBuilds :100`, `BuildingDamage :102`.
- Settings: `JoystickSensitivity :106`, `MovementStyle :108`, `BreachStyle :110`, `Muted :114`,
  `MusicVolume=70 :116`, `SfxVolume=80 :118`, `Difficulty :120`, `VoiceOvers :122`.
- Tutorial: `TutorialStep :126`, `SeenTutorials :128`.
- Combat: `Inventory` (AtbInventory `:132`), `GearInventory` (v20 `:135`), `AtbLossStreak :137`.
- Progress ledgers: `Dungeons :141`, `ActiveDungeonRun :143`, `Quests :145`, `Regions :147`.
- Social: `MyInviteCode :151`, `Contacts :153`, `BlockedCodes :155`, `Inbox :157`, `LastInboxSyncAt :159`.
- Echo/harvest: `LastHarvestClaimMs` (WO-115 accrual clock `:171`), `EchoCount=1 :182`, `SiloResources :190`,
  `WavesCompleted :198`, `EchoLanes` ("lane:level" CSV, initializer `"wood"` — legacy token normalizes to Harvest lv1 on read `:448`).
- Timers/jobs: `BuildJobs` (**wire back-compat only, not read at runtime since v35** `:208`),
  `AdSkipsUsedToday :214`, `AdSkipDayKey :220`, `ObsidianQueue` (v35, never null `:478`).
- World: `Zones` (v17 `:230`), `Tribes` (v34 `:239`), `Settlements` (v21 `:246`), `Wards` (v34 `:256`).
- Build mode: `BaseLayout` (v14 `:270`), `StrategicPlacementMigrated` (v30 `:432`), `FreeBuildsUsed` (v32 `:463`),
  `EverBuiltStructureIds` (**v36 WO-834** monotonic ledger `:530`; `MarkEverBuilt :538` idempotent
  OrdinalIgnoreCase set-add, caller owns Save; `HasEverBuilt :550`).
- Progression: `Magic` (v15 `:291`), `PartyMemberIds` (v16 `:305`; `PartySize` derived `:308`),
  `BuildingTiers` (v23 `:365`), `VillageTier` (v24 `:373`), `OwnedBuildingPerks` (v24 `:380`),
  `PopulationXP/Quests/Outposts/EchoSlots` (v28 `:389–401`), `HeroLevel/HeroXp/HeroLifetimeXp` (v29 `:412–418`).
- Arena/Army: `Arena` (ArenaProgress, v34 `:324`), `ArenaDefense` (v19 `:340`), `Army` (ArmyStorage, v22 `:354`).
- WO-771.9/WO-808 (additive, NO schema bump): `BarracksLevel=1 :489`, `TroopLevels :499`, `GearLevels :512`.

**Everything above round-trips through SaveSchema as of v36 — the old "Tribes/Wards/Arena unpersisted"
claim is DEAD (closed by v34).**

### GameStateService (MonoBehaviour, sealed singleton — `State/GameStateService.cs`, 1573L)
- **Pluggable save IO**: `static ISaveProvider Provider = new LocalSaveProvider()` (`:61`; WO-547 seam —
  serialization stays in the service, raw read/write/exists/delete delegated).
- 11 per-domain UnityEvents (`:81–101`): Resources/WaveRecorded/Tutorial/Player/Settings/Pets/Village/
  DungeonProgress/Combat/Social + `StateReplaced`.
- `Load()` (`:186`) — Provider read → **LB-3 atomic embedded-HMAC gate** (`TryExtractSigned`; invalid sig
  = reject + fresh state `:229–234`; legacy unsigned = load-once + re-sign `:278–282`) → parse →
  `SaveMigrator.MigrateForImport` → `SaveSchema.Validate` → `ApplyPersisted` (`:456`). Every reject is
  FlowTrace-loud and keeps fresh defaults.
- `Save()` (`:297`) — `EnsureAccount` (mints deterministic `guest-local-…` wallet when unbound, `:948,:942`) →
  Snapshot (`:362`) → serialize → `Provider.Write(key, SaveSchema.EmbedSignature(json))` single atomic value (`:319`).
- Mutators: AddCrystals `:334` / AddFood `:351` / FinishOnboarding (`:560` — also enrols the first
  companion via `FirstCompanionId()` `:585`, canon mapping Mage→Knight·Knight→Ranger·Ranger→Cleric·Cleric→Mage) /
  AddToParty `:609` / RemoveFromParty `:631` / IsInParty `:641` / RecordRun `:651` / BindWallet `:659` /
  **ChooseHero `:668` — applies the `FeatureFlags.KnightOnly` force at `:673`** /
  `EnsureHeroClassPersisted` `:689` (unset-after-load → defaults Knight) / Set* settings `:699–765` /
  AdvanceTutorial `:770` / MarkTutorialSeen `:780`.
- `ResetToNewGame()` (`:802–905`) — React reset() carve-out: keeps BoundWallet + BreachStyle + all social.
  Notables: founding seed ZERO (`StartingBudget.StrategicWood/Iron = 0`, `NestedTypes.cs:75–81` — replaced
  by FreeBuildsUsed freebies), `StrategicPlacementMigrated = true` (`:889` — blank-template New Game),
  `EverBuiltStructureIds` cleared (`:893` — closes every baked-twin surface gate = truly blank town),
  `EchoLanes = "harvest:1"` (`:890`), `ObsidianQueue = Empty()` (`:857`), `EnsureZoneGraph` (`:898`, def at `:548`).
- Backend delta-sync (`:907–1523`): `BackendBase = https://defenders-of-the-realm-v2.vercel.app` (`:925`,
  same in both build configs), `/api/game/save|load`, `/api/auth/nonce`; offline queue PlayerPrefs
  `dotr-sync-queue` (`:932`); `MinSyncDelay 8s`; `SyncAfterWave :998`, `SaveBeforeSceneChange :1008`,
  `LoadFromBackend :1019`, wallet-signed auth headers `:1145` (gated by `BackendAuthConfig.Enforced`, default OFF).
- Bootstrap is DUAL (both guard on Instance): `GameStateBootstrap` `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`
  (`GameStateBootstrap.cs:16`) AND `GameStateService.EnsureInstance` AfterSceneLoad (`GameStateService.cs:167`).

#### DELTA 2026-09-06  -  the cloud LOAD became a real load, and the offline queue coalesces

> STOP: **THE OLD MERGE POLICY SENTENCE IS RETIRED, AND IT WAS NEVER TRUE OF THE CODE.** Both this
> catalog and `LoadFromBackend`'s own docstring said *"server wins on BestWave (anti-rollback);
> local wins on Towers and Pets"*. There was **no per-field merge at all**: the body copied SEVEN
> fields by hand  -  `bestWave / resources / voidshards / aetherCrystals / stone / iron / wood`  -  and
> **never called `SaveMigrator.MigrateForImport` or `ApplyPersisted`**. The server was never the
> limiter (`api/game/load.js` returns the whole state document), so structures, army, build queue,
> echoes, cosmetics and quest state were all present in the payload and all dropped on the floor.
> A player who reinstalled or signed in on a second device got their currencies and a **BLANK
> TOWN**. Read at source: `GameStateService.cs` `LoadFromBackend` docstring `:2079-2089`.

- **`ApplyBackendState(SaveSchema.PersistedState server, double? serverSchemaVersion, double? serverLastSeenMs)`**
  (`:2244`, WO-1447 + WO-1448)  -  **the ONE apply seam, with TWO callers**, running the server row
  through the *same* `MigrateForImport -> SaveSchema.Validate -> ApplyPersisted` path the local
  `Load()` uses. Currency now rides that path like every other field, so a save field added later
  cannot silently fail to restore. `LoadFromBackend` is reduced to transport (clock anchor, config
  absorb) and one call to this. Ordered guards, each tracing:
  - **Recency gate (WO-1448):** `server > local` APPLY * `server == local` SKIP (same vintage, an
    overwrite is pure downside) * `server` undated SKIP *unless* local is also undated (the
    reinstall/new-device case). STOP: **NOT a per-field `max()` merge, by explicit ruling**  -  resources
    are SPENDABLE, so a per-field max mints currency out of a stale row. **Whole row or nothing.**
  - **Identity, FAIL CLOSED:** a row whose `boundWallet` differs from this device's rejects whole  - 
    applying it would both overwrite the town and repoint the account.
  - `BoundWallet` is **device-owned, never payload-owned**: restored unconditionally after
    `ApplyPersisted` so a null/blank row cannot blank the live wallet under the signer.
  - Only the APPLIED branch advances `_lastSyncedSnapshot` (the delta baseline) and calls `Save()`
     -  advancing that on a skip would mark unsent local changes as already synced and lose them.
  - The whole body is `try`-wrapped because `LoadFromBackend` runs inside a `Forget()`'d
    `UniTaskVoid` on every scene enter (`PersistenceBridge`), where an escaping throw would vanish
    into an unobserved task instead of reaching the break-log (CLAUDE.md S12).
- **`public enum BackendApplyOutcome`** (`:2211`)  -  `SkippedNoPayload` / `SkippedStaleServer` /
  `RejectedIdentity` / `RejectedMigration` / `RejectedValidation` / `Applied`. **RETURNED, not just
  logged**, so a headless oracle asserts the DECISION rather than its side effects
  (`CloudLoadRestoreRegression`, marker `CLOUDLOAD_RESTORE_OK`).
- **`public double LastLocalSaveUnixMs { get; private set; }`** (`:198`)  -  the recency anchor. Stamped
  by `Save()` **inside the try, after the write succeeds** (a failed write must not advance it, or a
  device that cannot persist starts refusing its own cloud restore) and re-hydrated in `Load()` from
  the envelope's `exportedAt`. One writer for both: **`StampLocalSaveClockFromEnvelope`** (`:544`),
  `Guard.Try`-wrapped, degrading to `0` **and saying so** on an unparseable/absent stamp. `0` means
  "this device has never saved"  -  the fail-safe direction, because the server row is this same
  player's own last accepted save. (!) **Cross-clock caveat is STATED, not engineered around**: this
  stamp is the DEVICE clock, the server's is Postgres `updated_at`; neither skew direction can mint
  currency, which is why the WO prescribes a comparison rather than a field merge.
- **Offline sync queue now COALESCES** (`:2946` region, WO-1441/WO-1454/WO-1455):
  `CoalesceOfflineQueue` runs on every enqueue. (!) **Nothing is at risk and that is why it only
  warns**  -  entries are retry MARKERS, not bodies; the upload is always the CURRENT full snapshot,
  so ONE successful save drains every entry. STOP: **Do not "fix" growth by trimming or clearing**  -  a
  queue that forgets it has unsent work is how a fail-closed refusal becomes real data loss. The
  depth warning was rewritten because it **structurally missed**: the old test was
  `Count % OfflineQueueDepthWarn == 0`, so it fired only on an exact multiple of 25 and a measured
  **112-deep** session emitted NOTHING (coalescing, foreign-identity drops and partial drains mean
  the counter is not sampled on every integer). It is now a **LATCHED CROSSING**  -  warn once on the
  way up, re-arm only after the depth falls back below.

### SaveSchema (static — `State/SaveSchema.cs`, 914L)
- `CurrentVersion` (the const line doubles as the full changelog, one clause per version, newest first — **read the number and the changelog there, never here**),
  `FileFormat = 1` (`:38`), key `dotr-save` (`:42`), legacy settings key (`:44`), `StarterDungeonId "healers_cottage"` (`:48`).
- `JsonSettings` (`:55–74`): StringEnumConverter + TutorialStepConverter, `MaxDepth = 64` (LB-3 deep-nesting cap).
- **LB-3 save integrity** (`:76–191`): keyed HMAC-SHA256, key assembled from fragments (`:101–106`,
  obfuscation only — server is authority); `ComputeSignature :109`, `VerifySignature :124` (constant-time);
  **atomic single-key envelope** `EmbedSignature :148` / `TryExtractSigned :160` (`<64-hex>\n<json>`;
  legacy raw JSON detected + migrated once).
- `SaveFile` envelope (`:203–215`): format/storeVersion/exportedAt/wallet/state.
- `PersistedState` (`:228–628`): **84 wire fields**, all nullable (Zod `.partial()` mirror), camelCase
  `[JsonProperty]`, unknown keys dropped. Last five: `obsidianQueue :586`, `barracksLevel :596`,
  `troopLevels :604`, `gearLevels :612`, `everBuiltStructureIds :628`.
- `Validate()` (`:677–853`): NonNegInt/FiniteInt clamps + NaN/Inf reject across resources, arrays, pets,
  inventory, pendingBuilds, dungeon run, inbox, echo, population, hero XP, buildJobs, **ObsidianQueue
  channels (`:821–831` via `ClampJobList :864`)**; Zones/Settlements null→empty (`:834–839`); volumes
  finite-only (not clamped, `:842–845`). `SaveValidationException :878` / `SaveValidationResult :892`.

### SaveMigrator (static — `State/SaveMigrator.cs`, 698L)
Registry-based chain, `Steps` dict (`:37–78`) = **{2–10, 14, 17, 18, 21–36}**; v11–13, 15, 16, 19, 20 are
additive-default-on-read (no step). `Migrate :85` (cumulative `< N`), `MigrateForImport :103` (rejects
newer-than-build / non-finite). Notable steps:
- v8→v9 (`:239`): pendingBuilds seed + legacy `realm-defenders-settings` fold + delete.
- v17→v18 (`:329`): AetherCrystals → Resources.Crystals fold, then zeroed.
- **v34→v35 (`MigrateToV35 :524–596`)**: builds ObsidianQueueState + folds legacy `buildJobs` (Kind
  backfilled from JobType), `pendingBuilds` (→ TowerBuild jobs, remaining time from FinishAt), and
  future-dated `buildingCooldowns` (→ Build jobs) into the **Builder** channel; legacy lists cleared;
  `FoldGuardHasId :655` prevents double-creation; Train+Research channels ensured.
- **v35→v36 (`MigrateToV36 :625–651`, WO-834)**: seeds `everBuiltStructureIds` = BaseLayout itemIds ∪
  FreeBuildsUsed ∪ (when BaseLayout non-empty = "established town") the **frozen hardcoded template
  snapshot** `DefaultTownTemplateIdsV36` (`:619–623` — workshop, collector_lumbermill, collector_farm,
  pet-house, forge, arcane-tower, market, jeweler, apothecary, jewelers-bench, barracks). An EMPTY
  BaseLayout (the blank-founding save) seeds only legs 1+2 → truly blank town. Never clobbers (`:627`).

### The rest of State/

| Type | Path | Verified essentials |
|---|---|---|
| `ArmyStorage` (sealed class) | `State/ArmyStorage.cs` (334L) | Persisted army on GameState (v22). `DefaultMaxArmySize=10 :43`; **`MaxArmySize` is DYNAMIC** — base 10 + `ModifierService.Active.ArmyCapBonus` (`:58–73`, derived/not serialized; change-only `[Flow:Perk]` log via static `s_lastLoggedCap :49`). `NextId` monotonic troop-id mint (`:80`, "troop-{n}"). `LastRecoveryTickMs` (WO-779 persisted recovery-clock anchor `:92`). `SlotsUsed/Remaining/CanTrain` take a `slotOf` seam delegate (`:101–132`); `TrainNow :148` (capacity → afford seam → mint); `GrantTrained :168` (WO-771.9 unconditional — paid at enqueue); wounded-recovery NO-permadeath: `MarkWounded :194`, `ReconcileAfterRaid :216`, `TickRecovery :253` (pure dt step), **`AdvanceRecovery :290`** (wall-clock resolver: fresh anchor seeds-to-now credits nothing; backwards clock ticks nothing); `AddVeterancy :316` (cap `PlayerTroop.MaxVeterancyRank`). |
| `PlayerTroop` | `State/PlayerTroop.cs` (111L) | One owned troop: Id/TroopDefId/Wounded/RecoveryRemaining/VeterancyRank/IsDeployable. |
| `BuildingTierCatalog` (static) + `BuildingPerkDef`/`BuildingTierDef`/`BuildingUpgradeDef`/`BuildingTierCatalogData` | `State/BuildingTierCatalog.cs` (199L) | Typed loader over `Data/Canonical/building-tiers.json` (`:100`) via CanonicalJson. `BuildingTierDef` carries cost W/F/C + `requiresVillageTier` (WC3 tech-gate `:60`) + `structureHpBonusPct` (cumulative-absolute `:68`) + cumulative `GameModifiers :73` + `Perks :76`. `BuildingPerkDef` = Gold-cost research perk whose effect IS a GameModifiers (`:46`). API: `All :104` / `Find :107` / `TierOf :117` / `MaxTier :127` / `IsUpgradable :137` / `FindPerk :140` / `PerkUnlockTier :156` / `Reload :170`. Load failure → LogError + empty catalog (`:178–197`). |
| `ModifierService` (static) | `State/ModifierService.cs` (161L) | THE read-point: compiles `GameState.BuildingTiers` + `OwnedBuildingPerks` through BuildingTierCatalog into the active `GameModifiers` (`Active` never null). |
| `GameModifiers` | `State/GameModifiers.cs` (78L) | Flat JSON-serializable perk contract (multipliers + `ArmyCapBonus` etc.); unset fields = no-op. |
| `EchoLaneBonuses` (static) | `State/EchoLaneBonuses.cs` (59L) | WO-738 passive per-lane multiplier contract (Core side of Echo lanes). |
| `ItemCapability` | `State/ItemCapability.cs` (54L) | Composable capability flags of a catalog Entry. |
| `ISaveProvider` / `LocalSaveProvider` | `State/ISaveProvider.cs` / `LocalSaveProvider.cs` | WO-547 raw-IO seam; default = exact legacy PlayerPrefs IO. |
| `Enums` | `State/Enums.cs` (81L) | Difficulty/MovementStyle/BreachStyle/HeroClass/PetSpecies/TutorialStep — `[EnumMember]` wire strings. |
| `NestedTypes` | `State/NestedTypes.cs` (277L) | PetData, ResourceBalance (Starter {250,80,15}), **`StartingBudget` = 0/0 (`:75–81`, freebies replaced the seed)**, PendingTowerBuild, AtbInventory, ChatContact, `ChatMessage` (1:1 mailbox — **name-collides with `Services.ChatMessage`**), LootStash, ActiveDungeonRun, SeedTree, DungeonProgress, QuestState/Progress, RegionProgress. |
| `BuildJobData` (struct) + `BuildJobType` | `State/BuildJobData.cs` | WO-172 offline-fair timer record, now + `Kind` + `Channel` (v35, additive default-on-read) — IS the "ObsidianJob". |
| `PlacedStructureData` / `PlacedDefenderData` (structs) | `State/PlacedStructureData.cs` / `PlacedDefenderData.cs` | Base-layout record (+ v27 `worldY`/`wallMounted`) · Arena-defense twin (WO-389). |
| `DifficultyTuning` / `ServerConfig` / `BackendAuthConfig` / `HeroClassOpt` / `SerializableDict` / `TutorialStepConverter` / `ArenaProgress` | `State/*` | As before: countdown multipliers · backend remote-config (never null) **but see the DEAD ROW below** · WO-121 auth flag default OFF · nullable-HeroClass wrapper · serializable dict · `1..7\|"done"` converter · Arena W/L struct (NOW persisted, v34). |
| `PersistenceBridge` (MonoBehaviour) | `State/PersistenceBridge.cs` | DDOL: wave-clear→SyncAfterWave (reflection to Village.WaveManager), scene-enter→LoadFromBackend, quit→Save. **`_loadOnEnterScenes` still lists dead `"PatriciaLight_TD"`** (`:61`; also mismatches SceneRouter's `"PatriciaLightMode"`). |

---

## Jobs/ (`DeNelle.Core.Jobs`) — the "Obsidian" multi-channel work queue (WO-773)

Player copy = "Builders"/"Training"/"Research"; "Obsidian" never surfaces in UI (`JobKind.cs:21–22`).

| Type | Path | Verified essentials |
|---|---|---|
| `JobKind` (enum) | `Jobs/JobKind.cs:32–56` | 11 kinds 0–10: Build/Upgrade/Repair/UnlockTier/LearnMagic/TrainTroop/TowerBuild/TowerUpgrade/WallUpgrade/**BarracksUpgrade=9**/**TroopUpgrade=10** (WO-771.9). |
| `ChannelId` (enum) | `Jobs/JobKind.cs:63–71` | Builder=0 / Train=1 / Research=2 — channels NEVER share slots. |
| `JobChannels.DefaultChannel` | `Jobs/JobKind.cs:81–94` | TrainTroop→Train; UnlockTier/LearnMagic/**TroopUpgrade**→Research; rest→Builder. |
| `ChannelState` | `Jobs/ObsidianQueueState.cs:33–54` | `BoughtSlots` + `ActiveJobs` (StartMs>0) + FIFO `PendingQueue` (StartMs=0) + `Count` + `EnsureLists`. SlotCount is DERIVED at runtime (config free slots + BoughtSlots) — never persisted. |
| `ObsidianQueueState` | `Jobs/ObsidianQueueState.cs:61–89` | `Channels` dict keyed ChannelId (enum-name on wire); `Channel(id)` create-on-demand never-null; `Empty()` seeds all three. |
| `ObsidianQueueEngine` (static, PURE) | `Jobs/ObsidianQueueEngine.cs` | No MonoBehaviour/statics/clock — takes ChannelState + slotCount + explicit nowMs (headless unit-testable). `Enqueue :43` (start iff slot free, else FIFO tail); `Resolve :65` — completes due jobs earliest-finish-first, **auto-pull starts the next job AT the freed slot's FinishMs (not now, `:111`) so offline chains resolve back-to-back in one call**; pending (StartMs≤0) never completes; 100000-iteration guard; `PullIntoFreeSlots :122`. Effects are the caller's job (BuildTimerService). |
| `IJobEffect` / `JobEffectRegistry` | `Jobs/IJobEffect.cs` | Per-kind completion handler seam (`:32`); registry `Register :51` (last wins) / `Has :59` / `Apply :66` (Guard-wrapped; unregistered kind = safe no-op — Build/Upgrade use their pre-existing seams and never double-apply). |

---

## Quests/ (`DeNelle.Core.Quests`)

| Type | Path | Verified essentials |
|---|---|---|
| `DailyQuestCatalog` (static) + DTOs | `Quests/DailyQuests.cs` (425L) | Loader over `Data/Canonical/daily-quests.json` (`:75`) via CanonicalJson (WebGL-safe, `:109–115`). DTOs: template (id/slot/target/weight/requiresHero/requiresFeature/`day1Guaranteed :44`), slot rewards, knobs (slotCount 3, 1 free reroll, 50-crystal reroll, max 3). |
| `DailyQuestService` (MonoBehaviour singleton) | same file | 3-slot daily roll, PlayerPrefs per-day (`dotr-daily-quests-v1`), `Report/Reroll/ForceRollToday`, `QuestCompleted` event; Day-1 guaranteed build-towers force-select (`:341–346`). **`FeatureShipped` (`:382–394`) now returns `true` for EVERY branch including `_ =>` — the FLAG-6 stale gate was fixed; the `requiresFeature` filter is currently vacuous (dead gate).** Bootstrap `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`. |
| `QuestService` (MonoBehaviour singleton) | `Quests/QuestService.cs` | Story quests → `GameState.Quests` (save-synced). `StartQuest/AdvanceQuest/CompleteQuest/GetStage/SetFlag/HasFlag/GiveKeystone`, `RewardEarned` event. BeforeSceneLoad bootstrap. |
| `QuestCatalog` (static) + DTOs | `Quests/QuestCatalog.cs` | `quests.json` loader via CanonicalJson. `Quests/FindQuest/Stages/Reload`. |

---

## World/ (`DeNelle.Core.World`)

| Type | Path | Verified essentials |
|---|---|---|
| `ZoneManager` (static) | `World/ZoneManager.cs` (210L) | THE region classifier. **Home-zone half-extents are now `VillageHalfX = VillageHalfZ = 52f` (`:41–42`)** — resized 2026-07-26 from the stale 42/33 Village.unity footprint to contain the merged castle (walls ±44, gates ±50); any doc still citing 42/33 is stale. `GetZone :56` (inside box = Village; else dominant normalized axis: +X Goldfields · −X Stoneback · +Z Ashwood · −Z Mirewood); `Regions` table w/ danger tiers 0–4 (`:45–53`); `ZoneAt :82`; `DangerTierAt :92`; `Depth :127` (overrun/`RegionDepthSpan 220` `:108`); `ThreatLevel :148` = 5×tier + round(4×depth) (`:112–116`); `DefaultZoneGraph :168` (5 ZoneStates, Village hub); `DefaultDestination :201` (tier≤1 City, ≥3 Horde). Hot path throttle-traced (`:60`). |
| `RealmMapCatalog` (static) + `RealmMapPoint`/`RealmRegionGate`/`RealmClearReward`/`HomeBaseDef`/`RealmRegionDef`/`RealmMapData` | `World/RealmMapCatalog.cs` (199L) | **WO-826** typed loader over dual-copy `Data/Canonical/realm-map.json` (`RelativePath :118`) — THE single source for the Realm Map (WO-825 boot rule: never author a second region list in C#). Gate = discriminated union on `kind` (`"bestWave"` reads Value, `"regionCleared"` reads RegionId; consts `:56–58`). Regions sorted by mapOrder (`:171`); `Home :124` / `Regions :128` / `Find :132` / `TitleFor :142` / `Reload :153`. Guard-wrapped parse; absent file → FlowTrace.Fail + EMPTY catalog (home-only render, never throws, `:183–185`). Region STATE derived at runtime from RegionProgress (WO-827 ledger pending). |
| `RegionId`/`RegionZone`/`NodeType`/`ZoneState` | `World/RegionZone.cs` | Region enum (append-never-renumber), static facts, persisted ZoneState. |
| `RegionSpawnTable` (static) | `World/RegionSpawnTable.cs` | WO-155 region→enemy roster, depth-banded weighted pick; `HasRoster` false only in the Village box (the safe-zone read). |
| `GameClock` (static) | `World/GameClock.cs` (61L) | `CurrentDay()` from PlayerPrefs epoch `dotr-gameclock-epoch`; self-flagged stopgap. |
| `CrystalGrade`/`CrystalRegion` | `World/CrystalGrade.cs` | Grade enum + `TopGradeFor(dangerTier)`. |
| `WardStoneDef/State/WardReach` | `World/WardContent.cs` | WO-112 ward-tether reach math. Persisted since v34. |
| `TribeDef/State`, `SettlementPhase/State`, `WorldPoint` | `World/WorldContent.cs` | WO-159/160 settlements + tribes. Persisted (v21/v34). |
| `SceneLink` / `ISceneLinkResolver` | `World/SceneLink.cs` / `ISceneLinkResolver.cs` | WO1 data-driven scene-crossing model + the Core contract behind `CoreServices.SceneLinkResolver`. |
| `SpawnAreaTable` (static) | `World/SpawnAreaTable.cs` (291L) | WO-606 geotagged spawn AREAS as queryable data. |
| `IHarvestSource` | `World/IHarvestSource.cs` | WO-656 registered harvest-faucet contract (CoC collector spine). |

(Note: `GarrisonRecipe.cs` + `GarrisonRecipeCatalog.cs` now live under `Data/` but KEEP namespace
`DeNelle.Core.World` — folder≠namespace, verified `Data/GarrisonRecipe.cs:23`.)

---

## Catalog/ (`DeNelle.Core.Catalog`) — build-system data model

| Type | Path | Verified essentials |
|---|---|---|
| `CatalogEntry` + `CellPlacement` + `OrientationFix` | `Catalog/CatalogEntry.cs` (125L) | id/displayName/type/kind, `visualPrefabPath` (LOOK `:37`), **`visualTexturePath`** (forced albedo for texture-lost Tripo FBX, `:50`), `repo` (BEHAVIOR `:53`), `composite :56`, `orientation :66` — only `manual=true` fixes applied; `OrientationFix.EffectiveScale` = uniform × per-axis, forced positive (`:100–111`). |
| `CatalogRegistry` (static) | `Catalog/CatalogRegistry.cs` (87L) | id+type registry: `Register :20` (replace logs Warn), `Get :43`, **`ResolveUpgradeId :55`** (collector id → bare `collectorBuildingId` so upgrade ladder agrees with placement), `OfType :63`, `Count :68`, `All() :73` (snapshot — StructureSingleton EnforceAll sweep), `Clear :81`. |
| `RepoProps` + `ResourceCost` | `Catalog/RepoProps.cs` (246L) | BEHAVIOR half. `navSurface :36`, `buildCost :45` (crystals fallback), `cost :54` (multi-resource wins when non-zero), `maxLevel :62`, `upgradeCost :73`, `upgradeVisualPath :86`, `upgradeTexturePath :101` (WO-719 per-tier forced albedo), `behaviorId :104`, `collectorBuildingId :114`, `singleton :123`, **`bakedTwins :133`** (legacy baked scene-root names that REPRESENT the row — singleton twin standdown/resurface + IsBuilt with zero code), **`npcModel :145`** (WO-818, catalog **v6**: KayKit NPC body slug, resolved `Resources/NPCs/KayKit/<slug>` first → People-pack chain → capsule; OWNER-ONLY creative pick, a swap is a one-word JSON retag), `storageCapacity :155` + `storageResource :163` + `IsStorageContainer :174` (WO-707: containers-only are raid targets — TODO seam noted at `:169`), `capacity :188` (collector reserve, designer-tunable), `placement :191`, **`heightMul :203`** (WO-764 multiplier vs the ONE global 4m base — SUPERSEDES `visualHeight :212`, deprecated + no longer read), combat stats `:215–224` (range/damage/fireRate/canHitAir/**airOnly** anti-air specialist `:223`/element), `projectileStyle :235` ("pellet"/"bolt"/"spell", visual only), AoE `:242–244`. |
| `CatalogType`/`EntryKind`/`NavSurfaceKind`/`PlacementSurface` | `Catalog/CatalogType.cs` | Taxonomy enums. |
| `PlacementRules` | `Catalog/PlacementRules.cs` | Declarative placement conditions. |
| `BuildTimerConfig` (SO) + `BuildJobKind` | `Catalog/BuildTimerConfig.cs` | WO-172 timer tuning SO (`Economy/BuildTimerConfig`, code-default fallback); its `freeBuildSlots` + `ChannelState.BoughtSlots` derive the Obsidian slot count. |

---

## UI/ (`DeNelle.Core.UI`)

### PanelManager (static modal arbiter — `UI/PanelManager.cs`, 240L, DEF-212)
- `PanelHandle` (`:40–57`): Close action + IsOpen probe + Name + **`BattleAllowed`** (WO-437).
  `Register :83` (battleAllowed:false) / `RegisterBattleAllowed :94` (Battle HUD, Pause only).
- `NotifyOpened` (`:104`): no-op on same handle; **battle-lock gate `:122`** — while
  `BattleLock.IsInBattle()` a non-battle panel is REJECTED (its Close invoked, returns false — a
  CONTRACT, not a failure); closes the previous panel; F8-15 `DeathTrace` window logging via
  CallerInfo params (`:107–108,:116`); **WO-465 visibility verify `:168–184`** — asks the handle's
  own IsOpen probe and FlowTrace.Fails the "recorded open but not visible" invisible-scrim class.
- `NotifyClosed :194` (stale close ignored), `CloseAll :212` (WO-611 hostile-posture sole ownership,
  bounded 32), `CloseOpen :223`, `AnyOpen :73`, `OpenPanelName :76`, `OpenStateChanged :70`.

### PanelRouter (static open registry — `UI/PanelRouter.cs`, 327L, DEF-213)
- `PanelId` (`:37–93`), stable values: `HeroTalents=0` **RETIRED** (kept for default(PanelId)),
  `Crafting=1`, `BuildingUpgrade=2`, `CosmeticShop=3`, *(4 retired — pet skill tree deleted)*,
  `PartyShop=5`, `RumorBoard=6`, `HeroSkillTree=7`, `HeroLoadout=8`, `ConsumableCrafting=9`,
  `JewelerCrafting=10`, `EquipmentPanel=11`, `GameGuide=12`, `RealmStore=13`, `Inventory=14`,
  **`RealmMap=15`** (`:92` — WO-826 parchment overworld, registered scene-independently by
  RealmMapPanel; travel a disabled stub until WO-827).
- Three opener maps: plain `Action` (`:104`), context `Action<string>` (DEF-186 `:114`),
  subject+mode `Action<string,string>` (`:123`). Register/Unregister per arity (`:131–195`,
  unregister only removes the exact delegate).
- `Open(id)` `:224` → Guard-wrapped invoke → **`VerifyOpenedVisible :248`**: PanelManager must record a
  panel open afterwards; battle-lock refusal is Warned as the WO-437 contract (`:259–264`), anything
  else Fails as invisible-scrim; success raises `PanelOpened` event (`:201`, WO-T1 tutorial signal
  "panel.opened:<id>"). `Open(id, ctx)` `:284` and `Open(id, ctx, mode)` `:311` fall back down the arity chain.

### ElarionUiKit (static, partial — surface only; details are HUD-lane canon)
`UI/ElarionUiKit.cs` (3204L) + partials `ElarionUiKitObsidian.cs` (2565L, Obsidian widget family —
single writer = Kit/Factory team), `ElarionUiKitConformance.cs` (WO-714 shared primitives),
`ElarionUiKitDetailCard.cs` (WO-693 parchment detail card), `ElarionUiKitNameplate.cs` (party HP/MP plate).
Public surface (line refs in ElarionUiKit.cs): `BuildModalCanvas :96`, `Scrim :120`, `Panel :142`,
`BuildObsidianPanel :514`, `BuildObsidianModal :812`, `ObsidianCloseButton :839`, `PinCanonicalCtaSize :871`,
`ClampMinTouch :919`, `BuildConfirmModal :1204`, `Header :1247`, `Button :1297`, `TechPrimaryButton :1372`,
`Slot :1483`, `Card :1549`, rarity helpers `:1659–1710`, `Bar :1797`, `Portrait :1934`, `PartyFrameRow :2063`,
`BuildSlideTab :2424`, `BuildCompass :2521`, `BuildVirtualDPad :2637`, `BuildAttackPill :2918`,
`AddSoftCooldownGlow :3034`, `BuildActionBarHousing :3060`, `BuildLockCrosshairBadge :3158`. Decorative
chrome is gated by `FeatureFlags.BlinkChrome`.

### Other UI/ files (one line each)

| Type | Path (lines) | Purpose |
|---|---|---|
| `UiStyle` (static) | `UI/UiStyle.cs` (355L) | THE one style authority every dumb View pulls tokens from. |
| `ElarionUi` (static) | `UI/ElarionUi.cs` (409L) | Legacy shared palette + UI-Toolkit inline-style helpers (predates UiStyle). |
| `ShopTheme` (static) | `UI/ShopTheme.cs` (443L) | WO-175 shop palette (CosmeticShop + PackStore) — duplicate of the palette, slated to fold into UiStyle. |
| `RpgUiCatalog` (static) | `UI/RpgUiCatalog.cs` (355L) | WebGL-safe sprite-pack accessor `Resources/RpgUi/<role>`; sprite-or-null. |
| `ConceptIconResolver` (static) | `UI/ConceptIconResolver.cs` (236L) | concept id → sprite via `concept-icons.json`; sprite-or-null. |
| `CombatTextLayer` | `UI/CombatTextLayer.cs` (222L) | Pooled, capped, non-stacking combat stamps (single writer). |
| `HudCommands` (static) | `HUD/HudCommands.cs` (124L) | Core command sink HUD-kit → Village handlers (HUD_OBSIDIAN A4). |
| `HudBuildingFocus` (static) | `UI/HudBuildingFocus.cs` | Cross-assembly "hero near upgradable building" proximity signal. |
| STOP: **`WorldHold` (static) + `WorldHold.Handle`**  -  entry added 2026-09-06 | `UI/WorldHold.cs` (1053L) | **THE ref-counted freeze owner.** Every pause / modal / purchase / death screen takes a hold instead of touching `Time.timeScale`; slowest-wins. **Two acquire FAMILIES, and the distinction is CATEGORICAL, not a bigger number** (WO-1360): `Acquire`/`AcquireScale` `:403,:498` are **`BoundedBeat`**  -  the DEFAULT for every caller, with a ceiling, because a hit stop / celebration / death cam has a length the CODE owns. `AcquirePlayerOwned(reason, isOwnerAlive)` `:446` and `AcquirePlayerOwnedScale` `:457` are **`HoldKind.PlayerOwned`**  -  **no ceiling**, because a pause menu, a bug-report form or a modal the player is reading can legitimately last hours and backgrounding the app is the normal way to do it. STOP: **RAISING THE CEILING IS NOT THE FIX**  -  it reproduces the bug at a longer timeout; on 2026-09-03 the 180 s ceiling force-released `pause-menu` after 507 s and the world ran underneath a PAUSED screen. An unbounded hold must be ASKED for **by name**, so it cannot be got by accident. STOP: **The liveness probe is REQUIRED and `RequireProbe` `:467` THROWS `ArgumentNullException` on null** (the `OverTimeEffects`/WO-1330 shape)  -  WO-1369: `game-over` was acquired by a screen that delegated release to a view it did not own, the arbiter destroyed that view 18 ms later, and with no ceiling and no probe the world sat at `timeScale 0.00` for 2 m 07 s until the OS killed the app. A good probe is a null-tolerant EXISTENCE test (`() => view != null`), **never** "has enough time passed"; `() => true` is the hole with a lambda around it and **`WorldHoldLivenessRegression` rejects it**. Other nets: `ReleaseAllForSceneLoad` `:951` (wired to `sceneLoaded`), `ForceReleaseAll` `:635`, `RestoreIfDrifted` `:907`, `WatchdogTick` `:803`, `Describe()` `:386`. **Live player-owned sites (measured 2026-09-06  -  10 under `_Modules`):** `PauseController:284` * `HudKitController:2693` (combat item picker) * `FocusedModalHost:47` * `HarvestOverflowModal:112` * `ObsidianNavigationWorkspace:58` * `BreakCaptureHarness:595` (F8 note) * `BugReportView:98` * `GameOverScreen:415` * `EndStateView:211` * `JewelerDiscoveryFtue:66`. Reason tokens are consts on the class (`ReasonPauseMenu` `:154`, `ReasonPurchase` `:157`, `ReasonCombatItemPicker` `:160`). |
| `HarvestPanelGate` / `ObsidianQueueGate` / `PauseGate` / `RaidEntryGate` (statics) | `UI/*Gate.cs` | Core open/close seams: Echo harvest panel · work-queue panel (WO-773) · back/pause · **RaidEntryGate** (F8 2026-07-30: HudKit "Raids" button → Village raid selection). `PauseGate` also owns the scoped native-full-screen suppression bit: rewarded ads keep the invoking `PanelManager` caller registered, so Android's ad-driven application-pause callback cannot replace it with Pause. The scope owns no navigation and reopens nothing. |
| `LoadingOverlay` | `UI/LoadingOverlay.cs` (211L) | Reusable code-built loading screen (Load Default / Design My Own delay). |
| `VillageLoadOverlay` | `UI/VillageLoadOverlay.cs` (265L) | Village-specific loader (spinner/progress/lore) driven by SceneRouter. |
| `ObjectiveBannerUi` | `UI/ObjectiveBannerUi.cs` (283L) | WO-T2 one-line objective strip (kit language). |
| `UiSpotlight` / `TutorialHighlightRegistry` / `UiKitTween` | `UI/*.cs` | WO-T2 dim+cutout spotlight · stable-id spotlight-target registry · the kit's one tween runner. |
| `SkrShowcasePanel` / `StakeRewardsPanel` | `UI/*.cs` | Grant-preview branding panel (ff.skrpreview) · read-only stake→rewards display (both no-wallet, presentation only). |
| `AddressableUIManager` (MonoBehaviour singleton) | `UI/AddressableUIManager.cs` (244L) | Async UI prefab load/unload via Addressables (UI-Core/Debug/Menus/Tower labels). |
| MVVM seam: `IPanelView`/`IPanelViewModel` + `BarVM`/`CraftRecipeVM`/`ItemVM`/`SlotVM`/`StakeRewardsVM`/`WalletVM` | `UI/Mvvm/*` | Strict-MVVM panel contracts (View = dumb skin; VM = no UnityEngine UI types) + shared VM records. |
| `ElarionUiKitDemo` | `UI/ElarionUiKitDemo.cs` (334L) | P1 kit demo overlay (acceptance surface). |

(**Removed since the old catalog:** `PortraitLockOverlay` — file no longer exists.)

---

## SceneRouter (`SceneRouter.cs`, 680L)

- Consts: `Title`, `HeroSelect`, `PetSelect`, `Village = "Village2"` (`:135`), `ATBBattle :154`,
  `PatriciaLight = "PatriciaLightMode"` (`:164`, DEAD — DTT removed), 3 raid consts
  `RaidBase_raider_camp_small` / `RaidBase_fortified_garrison` / `RaidBase_mage_enclave` (`:173–177`),
  7 dungeon consts (`:180–186`, only HealersCottage + FolksGranary are real scenes), `Dungeon(id) :195`.
- **`Castle` is a PROPERTY (`:150–152`)**: `FeatureFlags.MergedWorld ? "Main_Castle_Overworld" : "MainCastle_Hall"` —
  MergedWorld defaults ON, so the live home hub is the merged scene.
- `LoadScene :213` (sync; aborts + logs on unregistered scene; saves first) / `LoadSceneWithFade :238`
  (UniTask; **WO-769: `SaveBeforeSceneChange` failure is caught and never blocks the load `:260–266`**).
- **Return-point feature** (`:299–401, :599–642`): `GoBattle :567` stashes scene + hero pose on static
  `Return :588`; `ArmReturnPointRestore :299` one-shot sceneLoaded handler (detach-before-attach de-dupe
  `:309`); restore warps via reflected `HeroLocomotion.WarpTo(Vector3, Quaternion?)` (`:350–357`) —
  Core never references Village; transform fallback at `:363`.
- `GoTitle/GoHeroSelect/GoPetSelect :408–420`; `GoVillage :428` → `LoadVillageWithLoader :470`
  (VillageLoadOverlay + async progress + `SeatTreeOfLifeRootsUnderground :532` planting the decorative
  tree at (0,−0.25,0) — the gameplay Heart anchor stays (0,0,0)); `GoCastle :437` (fade to `Castle`);
  **`GoRaid(sceneName) :456`** — the raid V1 entry: fade-load a `RaidBase_*` scene, name-only contract
  (no RaidParams/loadout hand-off; unregistered names rejected by LoadSceneWithFade);
  `GoDungeon :560`; `GoBattle :567` + `PendingBattle :581`; `GoPatriciaLight :651` + params (DEAD path).
- `BattleParams` (`:50–78`) incl. `ReturnScene` + WO-770.3 `LastOutcome` (`BattleResultKind :46` — Core
  mirror so the dungeon can read Victory/Defeat without a BattleATB ref); `ReturnPoint :90`;
  `ISceneFader :672` (set by Core bootstrap).

---

## Diagnostics/ (`DeNelle.Core.Diagnostics`)

| Type | Path | Verified essentials |
|---|---|---|
| `FlowTrace` (static) | `Diagnostics/FlowTrace.cs` (415L) | **`Enabled` defaults `Application.isEditor \|\| Debug.isDebugBuild`** (`:28`) — a release player ships tracing OFF (PII); WebTrace flips it on for web sessions. Pluggable `Sink` (`:42`, never null, default `UnityLogSink :409`); per-category `Only/Mute/AllOn` (`:67–95`) behind `s_traceLock` (`:64`, lock taken only after the !Enabled early-out); `Step :139` / `Warn :147` / `Fail :151` (all `[Flow:<system>]`-tagged + depth-indented via `[ThreadStatic] s_depth :115`, capped visual depth 24 `:122`); `Throttle :163`; `Once :180`; `ResetSession :191`; `Measure :209` (budget-warn scope); `Enter :249` (`FlowScope` →/← with ms); `Try :288/:298` (always LogErrors independent of Enabled); **`Configure(TraceConfig) :328`** — runtime-reversible enable/sink/URL/filters (web sink = `WebTraceSink`, retargetable, flushed on swap-down); `TraceConfig :373`; `ITraceSink :397`. |
| `Guard` (static) | `Diagnostics/Guard.cs` (94L) | Error factory: `Try :29` (bool), `Try<T> :48` (fallback), `TryEach :67` ((built,failed) per-item). **`Report :80` logs DIRECTLY via `Debug.LogError`, deliberately NOT FlowTrace.Fail** (survives FlowTrace strip; includes full `ex.StackTrace` innermost-first). |
| `BreakCaptureHarness` (MonoBehaviour) | `Diagnostics/BreakCaptureHarness.cs` (836L) | Always-on flight recorder. Install `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] :102`; **early-outs on WebGL `:109`** (WebTrace covers web). Captures errors/exceptions/asserts, softlocks (**180s** no-move+no-progress, `:45`; input-suppression read from Village via cached reflection `:75–83`), scene transitions → `break-log.jsonl` + PNGs (cap 25/session `:51`) + `[BREAK]` console + EventTracker. **F8** flag key (`:87`) = owner subjective bug: clean-frame screenshot + optional typed note; flag PNGs carry a per-session UTC stamp (`:93`, evidence-loss fix). `Instance :61` lets the mobile `FlagCaptureButton` fire the same capture. Reentrancy-guarded, dedupes, never throws into gameplay. |
| `WebTrace` (MonoBehaviour) | `Diagnostics/WebTrace.cs` (447L) | WO-443 WebGL remote log sink. **LIVE IN PROD — the header self-documents this (`:11–23`)**: `FeatureFlags.WebTrace` defaults ON and `TraceEndpoint` is the hardcoded prod URL `https://defenders-of-the-realm-v2.vercel.app/api/trace` (`:76`), so a shipped WebGL player streams `[Flow:*]` to Neon (`analytics_events`, event_name `web_trace`) by default; activation also flips `FlowTrace.Enabled=true`. Bounded ring 500 / flush 50-or-5s / 200-per-POST (`:80–83`). Remote POST is `#if UNITY_WEBGL && !UNITY_EDITOR` — clean no-op elsewhere. |
| `WebTraceSink` | `Diagnostics/WebTraceSink.cs` (300L) | The SIBLING direct `ITraceSink` for `FlowTrace.Configure` (batches trace lines → configurable URL). WebTrace captures the whole Unity log; this one only FlowTrace output. |
| `DeathTrace` (static) | `Diagnostics/DeathTrace.cs` (221L) | F8-15 hero-death forensic WINDOW: opened by HeroHealth at the lethal moment; PanelManager logs every screen open/close + opener while `Active`. |
| `ScreenOpenWatchdog` | `Diagnostics/ScreenOpenWatchdog.cs` (164L) | `[Flow:ScreenOpen]` line whenever any registered modal becomes active (steady-state twin of DeathTrace). |
| `PerfReporter` | `Diagnostics/PerfReporter.cs` (227L) | Self-reporting perf gauge riding `[Flow:Perf]` → web-trace pipeline (real device FPS/frame/memory). |
| `PrivacySensitiveUi` (static) | `Diagnostics/PrivacySensitiveUi.cs` (112L) | WO-596 identity-widget opt-in registry; report captures hide registered widgets. |
| `UICaptureMode` | `Diagnostics/UICaptureMode.cs` (663L) | Graphics-enabled UI screenshot harness (deterministic panel opens via PanelRouter) — the "headless fleet frames are blank" fix. |
| `ArcaneTowerDiag` / `FloorDeepDiag` | `Diagnostics/*.cs` | Point diagnostics: white-arcane-tower disambiguator · MainCastle "pink floor" deep dump. Keep-while-unstable tools. |

---

## Dialogue/ (`DeNelle.Core.Dialogue`) — OUR dialogue system (WO-455/WO-557; Yarn fully removed)

| Type | Path | Verified essentials |
|---|---|---|
| `DialogueService` (static) | `Dialogue/DialogueService.cs` (107L) | The seam: Village registers `IDialogueCommandSink` + `IDialogueConditionSource` at boot (`:23–24`). `Play(id) :47` — catalog lookup → new VM → `Opened` (View builds/binds BEFORE first line) → `Started` → `vm.Begin`; **`PlayDef(def) :80`** runs a runtime code-built def through the same flow; `Stop :105` (synchronous, race-free); events `Opened :27`, `Started :32` / `Ended :34` (input-suppression pair), `EndedWithId :40` (WO-T1 tutorial "dialogue.ended:<id>"); `ActiveVm/IsRunning :42–43`. |
| `DialogueModel` + `DialogueCatalog` | `Dialogue/DialogueModel.cs` (171L) | Node-graph data (nodes → lines/commands/options); `DialogueCatalog` loads **`Data/Canonical/dialogue/dialogues.json`** (`:114`) via CanonicalJson (one file holds the lot). |
| `DialogueRunner` | `Dialogue/DialogueRunner.cs` (178L) | Plain C# state machine (no MonoBehaviour/async/codegen) — headless unit-testable; owns lifecycle (no Yarn "No node" race / teardown NRE). |
| `DialogueViewModel` | `Dialogue/DialogueViewModel.cs` (161L) | MVVM VM: ALL state/logic incl. speaker/portrait projection; DialogueView is a dumb skin calling `Advance/Choose`. Locked by `Tests/DialogueViewModelTests.cs`. |

---

## Enemies/ (`DeNelle.Core.Enemies`) — WO-772 Phase 1 taxonomy + resolver

| Type | Path | Verified essentials |
|---|---|---|
| `EnemyTaxonomy` types | `Enemies/EnemyTaxonomy.cs` (146L) | `EnemyFaction :29–43` (HollowOnes=0 · **Wildlands=1 reserved stub, no members** · Boss=2); `EnemyEquipParts :52` (armor part keys + weapon prop key — Phase-1 SCHEMA only, nothing attaches yet); `EnemyClass :74` / `EnemyFamily :114` / `ResolvedEnemyModel :128`. Pure data, headless. |
| `EnemyResolver` (static, pure) | `Enemies/EnemyResolver.cs` (344L) | THE id→family→class→distinct-model authority (fixes the generic-skeleton collapse), shared by wave loop + dungeons. `LintMarker "ENEMY_RESOLVER_OK" :42`. `KnownHollowModels :49–58` (7 committed meshes — data `modelKey` may only override to one of these). **`HollowTable :64–188` — 16 registered ids**: hollow-walker/warrior (sword_A)/rogue/acolyte/mage/reaper (scythe_A, Warrior variant)/brute/cellar-hollow, canon-locked necromancer + hollow-apprentice, 4 dungeon underscore-aliases (villager-a/b, apprentice-minor, healer), and **alduin (`:180`, `CombatSpawnable=false` — dialogue NPC, never a boss fight)**. `ApprovedHollowCombatIds :192–197` (10, codex order). **`Norm :231` folds `_`→`-`** (dungeon JSON spelling) + trim/lower. **Wildlands deferral gate**: `_deferredWildlandsIds :248–252` = {orc-raider, caveman, feral-wolf, tiefling-cultist} (no shippable art — exploded retarget); `IsCombatApproved :260`; `SubstituteHollowId :271` (heavy role/≥1.8m → hollow-warrior, else hollow-walker). Shipping Orc Warband stays approved. `FactionForFamily :282` (absent/hollow/undead → HollowOnes, any living token → Wildlands). `TryResolveHollowModel :304` (factory hook; data modelKey wins only if committed); `Resolve :321` (full identity; Alduin maps to Boss faction `:333`). Regression: `Editor/Regression/EnemyResolverRegression.cs`. |

---

## Combat/ (`DeNelle.Core.Combat`) — unchanged core + newer battle-lock stack

| Type | Path | Notes |
|---|---|---|
| `IDamageableStructure` | `Combat/IDamageableStructure.cs` | `IsAlive` + `ApplyContactDamage`. Impl: HeartController, HeroHealth, Building, Tower, Gate. |
| `IDamageable` bundle (+ CombatFaction/Layer/ICombatLayered/IDamageTintable/DamageElement/StatusEffect) | `Combat/IDamageable.cs` | Cross-module attack target; air/ground via ICombatLayered. |
| STOP: **`CombatFactionRules` (static)**  -  NEW 2026-09-06 | `Combat/CombatFactionRules.cs` | **WO-1439/WO-1438  -  THE ONE PLACE THAT ANSWERS "may this attacker hit that?"**, and the reason it exists is measured, not theorised: a raid garrison spent an entire raid destroying the RaidSpire it guards, because the sweep's own reject tally (`rejected[null,noStructComp,dead,hero]`, quoted in the file header at `:9-14`) enumerates every filter it had  -  **and faction was not among them**. Pure, allocation-free, side-effect-free so a per-frame selection loop can call it per candidate and an oracle can assert it directly. Surface: `MayAttack(CombatFaction, IDamageableStructure)` `:52`, `MayAttack(CombatFaction, IDamageable)` `:99` (WO-1438  -  the two interfaces are SEPARATE, neither extends the other, so the body-shaped selectors could not reach the structure overload and were re-implementing the predicate inline), `IsFriendlyFire` on both contracts (`:117`, `:128`  -  deliberately **liveness-blind**: classification is not attackability, and conflating them silently re-classifies a corpse), all funnelling into one private `Decide` `:108` (`!alive -> false; targetFaction != attacker`). STOP: **DO NOT COPY THE COMPARISON INTO A CALL SITE** and STOP: **do not special-case a target by name or id**  -  both bans are in the header, with the reasoning that "is it the spire?" leaves every wall, tower and building unfixed while looking fixed. (!) **OVERLOAD TRAP:** passing a CONCRETE type implementing BOTH interfaces is an ambiguous call and does not compile  -  deliberate, a loud error rather than a silent wrong answer; call through an interface-typed reference, which every selection loop already holds. (!) **The remaining-inline-copy census in the docstring (`:71-84`) is HAND-MAINTAINED and says so**  -  it was corrected on 2026-09-06 (WO-1503) from a singular "the one remaining copy" to a counted list after the author ran the grep instead of recalling it. **Run `grep "Faction != CombatFaction.Hostile" Assets/_Modules`  -  do not read that list as current.** |
| `BattleLock` (static) | `Combat/BattleLock.cs` (79L) | WO-437 single source of "is a battle active" — the PanelManager/PanelRouter gate. |
| `HeroCombatEngagement` / `PursuitBattleProbe` | `Combat/*.cs` | Battle-lock sources: in-scene real-time fight (2026-06-30) · "actively pursued = in battle" (F8-46 Option A). |
| ⚠ **Both battle-lock sources are released by `Enemy.OnDisable`** | `Village/Enemies/Enemy.cs` | WO-1337: an enemy raises the lock through TWO owners — the engagement token AND the pursuit pulse it stamps every DriveNav tick. Only the token was released on despawn; `PostureSignals.RevokePursuit` lived in `Die()` alone, so a body removed WITHOUT dying (the arena's retreat teardown uses `Destroy(gameObject)`) left a pulse live for `PursuitTtl`. `OnDisable` now revokes too — the one hook covering Destroy + pool release + scene unload. Revoked in BOTH places on purpose (death revokes immediately so town chrome returns with the last threat). |
| `PanelManager.DescribeOpen()` / `OpenPanelSelfReportsOpen` | `UI/PanelManager.cs` | WO-1337 attribution for the quiescence gate's modal invariant: names the open panel and asks its own `IsOpen` probe, so a VISIBLE panel (the player's to dismiss) is told apart from an INVISIBLE GHOST HANDLE (the WO-465 scrim class — the actual softlock). Only the ghost is healed, through the panel's own `Close`. |
| `CombatStatusTracker` | `Combat/CombatStatusTracker.cs` (146L) | Timed statuses (slow/freeze/burn + named buffs). |
| `ActorAnimator` / `IActorAnimator` / `AnimParams` (+ direction enums) | `Combat/*.cs` | Verb-level anim driver + canonical param hashes (Speed = raw world u/s); re-scans on body swap. |
| `ActionKeywords` (static) | `Combat/ActionKeywords.cs` (128L) | WO-670 motion-casting keyword vocabulary. |
| `CombatDeathDirection` | `Combat/CombatDeathDirection.cs` | Killing-hit → death-clip bucket resolver. |
| `DamageAttribution` (static) | `Combat/DamageAttribution.cs` | Per-target damage ledger for shared kill-XP (`Record/Drain/Forget/Clear`). |
| `ISiegeLootTarget` | `Combat/ISiegeLootTarget.cs` | WO-664 high-value siege-target marker (see the WO-707 container-only TODO in RepoProps `:169`). |
| `EnemyState` (enum) / `IRangedThreat` | `Combat/*.cs` | Idle/Chase/Attack/Hit/Dead · optional ranged marker (dormant). |

---

## Everything else (verified one-liners)

| Area | Types | Notes |
|---|---|---|
| **HudModel/** (`DeNelle.Core.HudModel`) | `HudModels.cs` (679L — the 11 Core HUD models + `IHudModel` facade + holder; producer-only mutators fire `Changed` + `[Flow:HUD]`), `HudModelTypes.cs` (shared enums/record structs; contract FROZEN per WO541_MODEL_API.md), `PostureSignals.cs` (calm→hostile(prebattle\|activebattle\|postbattle) battle-arc signals for the HUD kit). | WO-541 model layer behind `CoreServices.HudModel`. |
| **Arena/** | `ArenaContracts.cs` (124L) | Bounded-context data boundary: `ArenaRequest` in, `ArenaResult` out — JSON only crosses the seam (owner-ratified 2026-06-23). |
| **Platform/** (`DeNelle.Core.Platform`) | `CurrencySkin` + `CurrencySkinResolver` (WO-603: ONE build presents Pi OR Solana/$SKR skin, resolved pre-first-render from runtime skin.json + URL param), `IPiPlatform`/`PiPlatform`/`EditorPiPlatform`/`WebGLPiPlatform`/`PiSignInController` (Pi auth surface), `StakeRewardsResolver` (351L — READ-ONLY native-SKR-stake → in-game cosmetic perks; never mints/custodies), `StakeRewardsDemoBootstrap` (ff.stakedemo mock seed, self-installing). | |
| **Auth/** | `FirebaseAuthService.cs` (256L) | WO-769: email/password Firebase identity → verified ID token attached as Bearer to Neon `/api/game/save` (save keyed by Firebase UID). Async over the Firebase SDK, main-thread resume. |
| **Audio/** | `IAudioService`, `MusicTrack` (append-only), `IMusicAuthority` (the ONE music-request seam, 2026-07-09), `MusicLayer` (priority layers), `MusicRequest` (typed request). | Core owns the contracts; DeNelle.Audio implements. |
| **HUD/** | `IVillageHud` (passive display contract via CoreServices.Hud), `HudCommands` (kit→Village command sink). | |
| **Web3/** | `IJupiterService` (+SwapQuote/SwapInputToken), `IWalletSigner` (WO-121 ed25519; stub can't sign). | |
| **Progression/** | `SkillSystem` (craft-skill singleton, AfterSceneLoad bootstrap), `IXpEarner`, `XpEarnerRegistry`. | |
| **Population/** | `IPopulationService` (WO-587 milestone-driven Echo slot unlocks, via CoreServices.Population). | |
| **Services/** | `ClanService` (local PlayerPrefs clan+chat stub; own `ChatMessage` at `:67`), `ChatPhraseCatalog` (chat-phrases.json), `LeaderboardService` (pluggable; honest `LocalStubLeaderboardSource`). | |
| **Analytics/Promo/Referral** | `EventTracker` (batched → `/api/events/track`, offline queue cap 200, circuit breaker), `PromoCodeService`/`PromoCodeUI`, `ReferralService`/`InviteFriendsUI`. | The two `*UI` panels are UI-Toolkit/UIDocument — the known empty-in-player-builds risk. |
| **Data/** | `CanonicalJson` (static, **namespace `DeNelle.Core`** — Resources-first + StreamingAssets fallback, THE dual-copy contract hub), `DataInjector`, `ICatalogSource`/`LocalJsonCatalogSource` (Tier-0 source-agnostic seam; local source byte-identical to CanonicalJson.Read), `GarrisonRecipe(+Catalog)` (**namespace `DeNelle.Core.World`**), plus authoring SOs: BattlePassData/Reward, CampaignData/MissionData/CampaignProgressRecord, PetType, SkillTypes, SpecialAbility, TacticalData, TowerData. | |
| **Addressables/** (`DeNelle.Core.AssetDelivery`) | `AddressablesGroupConfig` (typed AssetReference SOs), `AddressablesMemoryProfiler` (handle-leak tracker), `SkinController` (async per-slot skin loader), **`HeroAssetLoader` + `HeroTextureLoader`** (WO-545 Tier-1 per-hero body/texture seams). | Folder ≠ namespace (avoids shadowing UnityEngine Addressables). |
| **Theme/** | `Theme.cs` (307L) — themes.json tokens via CanonicalJson (`:187`). **Header (:15) still claims streamingAssetsPath-only loading — stale.** | |
| **Tutorial/** | `TutorialSignals` (WO-T1 stable-id completion bus: "build.tower_placed", "panel.opened:<id>", "dialogue.ended:<id>"), `TutorialStepModel` (tutorial-steps.json data shape; interpreter is Village.TutorialFlow). | |
| **Geometry/** | `WeaponBoundsOrient` (canonical mesh-axis seating; BINDING doc WEAPON_ARMOR_ORIENT_LOGIC.md). | |
| **Validation/** | `OrientationValidator` (pure facing rule), `OrientationGuard` (inert unless `_enabled` + `ORIENTATION_GATE`). | |
| **VFX/** | `Hud` (Focus/Unfocus attention API, namespace `DeNelle.Core`), `AttentionGlow` (LineRenderer frame). | |
| **Debug/ + Dev/** | `DebugCanvasUI` (F12 overlay, **namespace `DeNelle.Core.DevOverlay`**), `FlagCaptureButton` (mobile F8 chip → same BreakCaptureHarness capture; gated `ff.flagbutton`). | |
| **Scripts/AI/** | `BTNode`/`Selector`/`Sequence`/`ActionNode`/`Condition` — behaviour-tree primitives, **assembly `DeNelle.AI`**. | |
| **Tests/** | `SaveLoadRoundTripTest`, `ResetCarveOutTest`, `SaveMigratorTest`, `SaveSchemaValidateTest`, `TestSupport`, **`DialogueViewModelTests`** (WO-744 §2c gate on the VM speaker/portrait projection). | Editor-only; refs DeNelle.Core + DeNelle.Data. |

---

### ⛔ `State/ServerConfig.cs` IS DEAD — fully wired client-side, never once settable (verified 2026-09-02)

The file carries its own banner (`ServerConfig.cs:1-4`, WO-1331): **"DORMANT. THIS MECHANISM HAS
NEVER ONCE BEEN SETTABLE."** Every client half exists — `GameStateService.ServerConfig` defaults to
`ServerConfig.Default` (`GameStateService.cs:157`), the load response declares
`[JsonProperty("config")] public ServerConfig Config` (`:2542`) and assigns it (`:1794`), and
`WaveManager.cs:3591-3592` really does read it for boss-wave crystal drops. The server half does not:
**`api/game/load.js` has never emitted a `config` key** — its response literal returns
`{ success, data, ... }` and nothing else, verified by reading the whole file.

So every field resolves to its compiled default on every launch, forever. ⚠ **Do not restate the
field count here** — the file's own banner says "its eleven fields" while a `[JsonProperty]` grep
returns 15, so the two disagree and both will rot; read the type. Treat any doc or ticket that
describes a knob as "server-tunable via ServerConfig" as describing a mechanism that has never run.

---

## DATA / JSON loaded by Core (dual-copy law: Resources/Data/Canonical wins, StreamingAssets fallback — keep byte-identical)

> ## ⛔ THE SINGLE MOST MISUNDERSTOOD FACT IN THIS REPO (recorded 2026-09-02, verified at source)
> **`Resources.Load<TextAsset>` resolves FIRST on EVERY platform, and `Assets/Resources/` is COMPILED
> INTO THE PLAYER.** `LocalJsonCatalogSource.Read` (`Assets/_Modules/Core/Data/LocalJsonCatalogSource.cs:36`)
> tries `Resources.Load<TextAsset>(resPath)` and returns its text if non-empty; the desktop
> `StreamingAssets` `File.ReadAllText` is only a `Guard.Try` FALLBACK reached when no Resources copy
> exists. Its own header says it plainly: "Resources wins."
>
> **Therefore "data-driven" in this repo has NEVER meant "tunable without a rebuild."** Editing any
> canonical JSON still costs a full player build (~10 min APK / ~30 min WebGL), and **editing the
> `StreamingAssets` twin ALONE changes nothing at runtime** — the compiled Resources copy shadows it.
> Every past attempt to make the game tunable by moving numbers into JSON was working on the wrong
> axis; the axis that actually works is the WO-1331 remote-catalog seam (see the 2026-09-02 delta at
> the top of this file), which is flag-gated OFF by default.
>
> Twin counts on disk (2026-09-02): `Assets/Resources/Data/Canonical/` **115** `.json`,
> `Assets/StreamingAssets/Data/Canonical/` **98** — i.e. they are NOT a mirror, and a file present
> only in StreamingAssets is WebGL-null (see the six-catalog flag in the index ledger).

`quests.json` (QuestCatalog) · `daily-quests.json` (DailyQuestCatalog) · `chat-phrases.json`
(ChatPhraseCatalog) · `garrison-recipes.json` (GarrisonRecipeCatalog) · `themes.json` (Theme) ·
`building-tiers.json` (BuildingTierCatalog) · **`realm-map.json` (RealmMapCatalog, WO-826)** ·
`dialogue/dialogues.json` (DialogueCatalog) · `scene-configs.json` (HubScenes ownership mirror) ·
`concept-icons.json` (ConceptIconResolver) · `tutorial-steps.json` (TutorialStepModel, walked by Village)
· generic tables via `DataInjector.Inject<T>` (owned by other modules).

PlayerPrefs keys owned/read by Core: `dotr-save` (+ embedded HMAC; the legacy sibling `.sig` key is
retired — signature is IN the value now) · `dotr-sync-queue` · `dotr-event-queue` ·
`dotr-daily-quests-v1` (+ day1-done) · `dotr-clans-v1` + `dotr-account-id-v1` · `dotr-redeemed-promos` ·
`dotr-referral-*` · `dotr-gameclock-epoch` · `onboarding.fullTutorial` · every `ff.<flag>` override ·
`realm-defenders-settings` (legacy; read+deleted by migrator v9).

---

## Behaviors & seams (cross-assembly contracts)

- **Localization (WO-1605):** every migrated surface calls the package-free
  `DeNelle.Core.UI.LocalText` facade. `DeNelle.Localization.UnityLocalizationProvider`
  is the only approved Unity String Database reader and asynchronously keeps the selected
  locale plus English fallback resident. Feature files may define thin key catalogs using
  `LocalizedText` / `LocalizedText<TArguments>`; they may not carry English sentences or
  become separate resolvers. Keys remain separated by domain (`hud.*`, `battle.*`,
  `interaction.*`, `shop.*`, `lore.*`, `settings.*`, and feature namespaces such as
  `jeweler.*`) while the active locale and resolution path stay global.
  The release/region authority is `Assets/Editor/Localization/LocalizationPolicy.json`:
  `en`, `es`, `pt-BR`, `de`, `fr`, and `ru` are build-enabled; `ar`, `ja`, `ko`, and
  `zh-Hans` remain required for key/argument parity but hidden pending RTL/CJK font and
  layout readiness. See `docs/localization/architecture.md` for the generation, fallback,
  all-region change rule, and verification contract.
  Store migration is data-first by semantic cohort: its 21 purchase-gate, Pi, and wallet-mirror keys
  are present in all ten required locale catalogs and all six enabled tables, while `StoreStrings` intentionally remains
  on its legacy reader until the complete Store key set can switch to `LocalText` atomically.

- **Service registry:** `CoreServices` (8 slots, above). Callers null-check; register Awake / unregister OnDestroy.
- **Panel routing:** panel registers opener on `PanelRouter` (+ optional context / context+mode arities);
  any assembly opens by `PanelId`; visibility is arbitered by `PanelManager` (one modal at a time,
  WO-437 battle-lock, WO-465 IsOpen verify). `PanelRouter.PanelOpened` feeds TutorialSignals.
- **Hub/scene classification:** `HubScenes.Names` (`:25`) = {Village2, MainCastle_Hall, CastleHub,
  CastleHub_MainKeep, **Main_Castle_Overworld**}; `IsHub :29` (exact-or-contains), `IsOverworld :42`
  (== Main_Castle_Overworld only), `IsRaid :52` (`RaidBase*` prefix), `IsEnemyOwnedScene :82`
  (scene-configs.json mirror of Village SceneOwnership — one cached parse), `SuppressTownHud :97`
  (the WO-550 chokepoint ~14 panel bootstraps gate on).
- **Jobs:** enqueue/resolve through `ObsidianQueueEngine` (pure); effects via `JobEffectRegistry`
  (Build/Upgrade keep their legacy seams); state on `GameState.ObsidianQueue`; clock = TimeSource.NowUnixMs
  (Village side), shared with `ArmyStorage.AdvanceRecovery`.
- **Dialogue:** gameplay verbs cross via `IDialogueCommandSink` / conditions via `IDialogueConditionSource`
  (Village registers); Views subscribe `DialogueService.Opened`; input suppression rides `Started`/`Ended`.
- **Enemy identity:** every spawner funnels through EnemyFactory.Build (Village), which consults
  `EnemyResolver.IsCombatApproved` → `SubstituteHollowId` → `TryResolveHollowModel`.
- **Army seams:** ArmyStorage's catalog-dependent methods take delegates (`slotOf`, `tryAfford`) wired
  Village-side to TroopCatalog/EconomyService — Core never references Village.
- **Reflection (only two spots):** PersistenceBridge → Village.WaveManager; SceneRouter return-point →
  Village.HeroLocomotion.WarpTo. Everything else is delegate/interface seams.
- **Save law:** every persisted-shape change = append-only nullable wire field + either a Steps entry or
  documented additive-default-on-read; keep the version triple aligned (SaveMigrator top step ==
  SaveSchema.CurrentVersion == GameState.SchemaVersion source).

---

## RISK / LANDMINE LEDGER (2026-08-02 — each verified from code)

### Comments/XML that LIE (trust code, then trailing comments)
1. **FeatureFlags — 12 XML-summary lies** (see ⚠ rows above): RaidContinuousWalk, WebTrace, PartyShop,
   OverworldEncounter, RegionRoam, BattleHud9Zone, MergedWorld, GateTraversal, **OutpostCaves** (the
   worst: XML asserts "ships OFF", code is ON, no correcting comment — and the XML says the resolver it
   arms "DOES NOT EXIST YET"), MocapLocomotion, Barracks, PoiCallouts. Rule: `defaultOn:` is truth;
   trailing `//` owner-reversal comment is the current ruling.
2. `Theme.cs:15` header still documents the abandoned streamingAssetsPath-only load; actual
   `EnsureLoaded` uses CanonicalJson (`:187`).
3. `ResourceType.cs:17` maps AetherCrystal → `GameState.AetherCrystals` — that field is DEPRECATED
   (folded into Resources.Crystals at v18, kept 0 for wire back-compat).
4. `GameState.cs` banner and several field XMLs still say "schema v34" / "~60 fields" — current is v36 / 84 wire fields.
5. `GameState.Wards :250–255` XML self-contradicts (says both "round-trips (v34)" and "remain in-memory
   only" in the same block) — the truth is it ROUND-TRIPS (v34, verified in SaveSchema `:549` + MigrateToV34).

### Live-in-prod surprises
6. **WebTrace is LIVE by default** — `ff.webtrace` ON + hardcoded prod endpoint (`WebTrace.cs:76`); a
   shipped WebGL player streams FlowTrace to Neon. The file header itself documents the 2026-07-15
   incident where a CLI believed the old "dormant" claim. Read path: Vercel `[sig]` echo / admin db endpoint.
7. The Vercel backend (`defenders-of-the-realm-v2.vercel.app`) is no longer "never deployed" — Neon
   `/api/game/save` (Firebase-token-keyed, WO-769) and `/api/trace` are live surfaces. Old "backend
   undeployed, all stubs" claims are stale; EventTracker/Promo/Referral endpoints remain
   deploy-dependent per-route.
8. `DailyQuestService.FeatureShipped` (`DailyQuests.cs:382–394`) now returns true for EVERYTHING
   including `_ =>` — the `requiresFeature` template gate is vacuous dead code (behavior fine; the
   switch is a trap for anyone re-adding a gated feature expecting it to filter).

### Dead / stale references
9. `SceneRouter.PatriciaLight` (+ GoPatriciaLight/PendingPatriciaLight/params) — DTT removed; DEAD
   routing. `PersistenceBridge._loadOnEnterScenes` still lists `"PatriciaLight_TD"` (`:61`), which
   ALSO never matched SceneRouter's `"PatriciaLightMode"`.
10. Dungeon consts: only HealersCottage + FolksGranary are real playable scenes; the other 5 consts are stubs.
11. `RuntimeWorldSeam` infrastructure is self-declared DEAD CODE pending removal (superseded by merged world).
12. `RepoProps.visualHeight :212` is deprecated and no longer read (heightMul supersedes) — kept only
    so old JSON deserializes.

### Behavioral landmines
13. **PanelRouter.Open returns false for BOTH "no opener registered" and "battle-lock refused"** — a
    caller showing "coming soon" on false will mislabel the in-battle refusal (the refusal is Warned,
    not Failed, but the return value doesn't distinguish).
14. `ArmyStorage.MaxArmySize` is a property with side effects (FlowTrace on change) and a **static**
    `s_lastLoggedCap` cache (`:49`) shared across all ArmyStorage instances — harmless for logging,
    but do not add per-instance logic there.
15. `GameState.BuildJobs` is wire-back-compat ONLY since v35 — runtime reads the ObsidianQueue. Writing
    to BuildJobs at runtime is a silent no-op for gameplay.
16. `MigrateToV36`'s template snapshot is deliberately HARDCODED (`SaveMigrator.cs:619`) — do NOT
    "helpfully" re-point it at the live census (a migration is a point-in-time transform; the v8
    gate-0→gate-2 precedent).
17. `EchoLanes` initializer is `"wood"` (`GameState.cs:448`) but New Game seeds `"harvest:1"`
    (`GameStateService.cs:890`) — consistent only because the WO-738 read-normalizer folds legacy
    wood/iron/food → Harvest lv1. Removing that normalizer breaks fresh-SO (non-reset) boots.
18. `ZoneManager` home box is **52/52** — code citing the old 42/33 (docs, spawn math) mis-classifies
    the courtyard band as an outer region again (the exact 2026-07-26 bug: enemies inside the castle
    during the tutorial).
19. Two `ChatMessage` classes: `DeNelle.Core.State.ChatMessage` (NestedTypes `:123`, 1:1 mailbox) vs
    `DeNelle.Core.Services.ChatMessage` (ClanService `:67`, team chat). Fully-qualify.
20. Dual GameStateService bootstrap (GameStateBootstrap BeforeSceneLoad + EnsureInstance AfterSceneLoad)
    — both guard on Instance; harmless but two mechanisms for one job.
21. Folder ≠ namespace set (intentional, memory `core-namespace-shadows-unityengine-statics`):
    `Debug/` → `DeNelle.Core.DevOverlay`; `Addressables/` → `DeNelle.Core.AssetDelivery`;
    `Data/CanonicalJson|DataInjector` → `DeNelle.Core`; `Data/GarrisonRecipe*` → `DeNelle.Core.World`.
22. `PromoCodeUI` / `InviteFriendsUI` remain UI-Toolkit UIDocument panels — empty in player builds
    (PIPELINE §8); every newer Core UI is code-built uGUI.
23. Store-hardening flags (`DevResourceTool`, `FlagButton`, `RealmStorePurchase`) default `IsDevBuild` —
    a PlayerPrefs `ff.*=1` still re-enables them on ANY build; a store-release checklist must verify no
    stale prefs override ships in a first-run state.
24. `CustomDialogue` OFF has NO fallback (Yarn removed, WO-557) — flipping `ff.customdialogue=0` breaks
    all dialogue; the flag is now effectively a kill-switch, not an A/B.
