# WORK ORDER 1097 — the apex boss is requested as a COMPONENT type, so Addressables can never return it and the dragon never spawns

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-09 by the UI seat (UI reserved block; banner bumped 1097 → 1098 in the same edit)
**Silo:** Content / Addressables · Waves
**Severity:** P1 — the apex wave's headline threat silently never appears
**Source:** F8 captures seq=4969 (the exception), 4970 (the consequence), 4971 (the failed load)

---

## Root cause — stated by Addressables itself, no inference required

```
UnityEngine.AddressableAssets.InvalidKeyException: No Asset found for Key=Enemies/Boss_Dragon with
Type=DeNelle.Village.DragonBoss. Key exists as Type=UnityEngine.GameObject, which is not assignable
from the requested Type=DeNelle.Village.DragonBoss
```

`WaveManager.cs:2636` calls:

```csharp
_apexBossPrefab = DeNelle.Core.EnemyAssetLoader.LoadEnemyAsset<DragonBoss>("Enemies/Boss_Dragon");
```

→ `EnemyAssetLoader.LoadEnemyAsset<T>` (`:116`) → `Load<T>` (`:201`) →
`EnemyContentWarmer.Request<T>` (`:388`) → `StartRequest<T>` (`:393`) →
`Addressables.LoadAssetAsync<DragonBoss>` (`:395`).

**Addressables addresses a prefab as `UnityEngine.GameObject`. It cannot hand back a Component type.**
The request is unsatisfiable by construction, every time, on every platform. `_apexBossPrefab` stays
null and the apex wave spawns no boss — seq=4970: *"Apex wave has no `_apexBossPrefab` AND
EnemyAssetLoader found no 'Enemies/Boss_Dragon'"*.

## Why nothing caught it

1. **The generic constraint is too loose.** `LoadEnemyAsset<T>(string key) where T : Object`
   (`EnemyAssetLoader.cs:116`) — `UnityEngine.Object` admits `Component`, so the compiler happily
   accepts a call that can only fail at runtime.
2. **⚠ The API's own documentation prescribes the broken call.** The XML doc directly above that
   signature (`:112-115`) reads:
   *"Generic escape hatch for enemy assets addressed by a FULL Resources-relative key (prefix
   included), e.g. `LoadEnemyAsset<DragonBoss>("Enemies/Boss_Dragon")`."*
   The worked example **is** the bug. Anyone following the docs writes it again. Fixing the doc is
   part of this ticket, not a nicety.
3. **There is no type screen.** `EnemyContentWarmer.RejectUnsafeRequest<T>` exists but gates on
   `AddressablesCacheHealth.UnsafeThisSession` — cache health, not asset type. Nothing anywhere
   checks that `T` is a loadable asset type.

## Blast radius — exactly one caller, verified

`grep -rn "LoadEnemyAsset<"` over `Assets/`, excluding the loader itself, returns four hits:

| Call site | Type | Verdict |
|---|---|---|
| `WaveManager.cs:2636` | `DragonBoss` | ⛔ **the defect** — a Component |
| `EnemyRigColorRegression.cs:183` | `Texture` | fine |
| `VfxProofCapture.cs:988` | `GameObject` | fine |
| `EnemyRigColorRegression.cs:40` | (comment) | n/a |

So this is a **single-call-site bug with a systemic hole behind it.** Fix both.

## Fix spec

1. **Make the loader handle Component types**, so the hole closes for every future caller rather than
   just this one: when `typeof(Component).IsAssignableFrom(typeof(T))`, load the address as
   `GameObject` and return `GetComponent<T>()` from it. Report a `FlowTrace.Fail` naming the address
   and the type if the GameObject loads but the component is absent — that is a genuine authoring
   error and must not be silent.
   *If the owner/CLI prefers the narrower change,* fix `WaveManager.cs:2636` to load `GameObject` and
   `GetComponent<DragonBoss>()` — but then the loose `where T : Object` constraint and the misleading
   doc still stand, so the systemic fix is recommended.
2. **Fix the XML doc at `EnemyAssetLoader.cs:112-115`** so its example is a call that works.
3. **Pin it.** An editor regression asserting `Enemies/Boss_Dragon` resolves to a prefab carrying a
   `DragonBoss` component — and that the apex path produces a non-null boss — is cheap and would have
   caught this before a wave shipped without its boss.

## Do NOT

- Do not re-address the prefab in Addressables to some component type. That is not a thing
  Addressables does; the address is correct and the caller is wrong.
- Do not "fix" this by re-wiring `_apexBossPrefab` in the scene. The comment at `WaveManager.cs:2630`
  records that the serialized reference is null on purpose and this loader path is the deliberate,
  corruption-safe fallback for it. Repair the fallback, not the scene.

## Acceptance criteria

- [ ] An apex wave spawns the dragon, confirmed by a run — not by compile-green.
- [ ] `InvalidKeyException` no longer appears for `Enemies/Boss_Dragon`.
- [ ] A GameObject that loads but lacks the requested component fails loudly, naming address and type.
- [ ] The doc example at `EnemyAssetLoader.cs:112-115` compiles and works as written.
- [ ] A regression pins the apex boss resolve.
- [ ] Brace balance; gate markers on a fresh log.
- [ ] Owner felt-verifies an apex wave and closes.

## Unproven

- Whether the apex wave has *ever* spawned its dragon since this fallback was introduced. The failure
  is unconditional, so the honest expectation is "no" — but the fallback's own comment implies it was
  believed to work, and nobody has established when it last did.
- Whether other loaders (`StructureAssetLoader`, the hero prewarmer) carry the same `where T : Object`
  hole. Not checked; worth a sweep in the same pass.
