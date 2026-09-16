# Release record — Google Play AAB `2026.09.16.371610` (+ Seeker APK `371627`)

**Supersedes `2026.09.15.371436` before Google accepted it.** The owner's APK felt-test of the
371436-era content found the Arcane Spire wearing the Synty stand-ins with its middle tier upside
down (WO-1760). Both artifacts below carry the re-point. Built inside the owner's 30-minute window
(20:29 → 20:54 local, 2026-09-15). Not yet uploaded at the time of writing.

---

## 1. Artifact identity (measured from the files, not from a build script's claim)

| Field | AAB (Play) | APK (Seeker, store-shaped) |
|---|---|---|
| Path | `Builds/Android/EchoesOfElarion-GooglePlay.aab` | `Builds/Android/DefendersOfTheRealm.apk` |
| Size | 455,016,276 bytes (433.9 MiB) | 466,293,778 bytes |
| Measured install size | 451,607,675 — **48,392,325 under** the 500,000,000 ceiling | n/a (sideload) |
| SHA-256 | `43778f2b4899432b7664bde55ceac8029f0c74ca2d53982da58bc87aef5d5df8` | `d23f0ac6596a3487dbf472cd0af2621dbd8dd66b5bc720f2ed2a099c070dc475` |
| versionCode / versionName | `371610` / `2026.09.16.371610` (bundletool `dump manifest`) | `371627` / `2026.09.16.371627` (aapt2 `dump badging`) |
| Package | `com.denellestudios.echoesofelarion` | same |
| Signing | `dotr-release.keystore`, alias `dotr` (`AAB_SIGNING_OK`) | same keystore |
| Defines | none (no `TESTER_BUILD`) | none (`[apk] STORE-shaped build`) |
| Remote catalog the player requests | `catalog_2026.09.16.371610` | `catalog_2026.09.16.371627` |

The versionName date reads 09.16 because `AndroidBuild` stamps the version from UTC; the local
clock was 2026-09-15 evening.

## 2. Gate evidence — every marker on a fresh log, judged by MARKER not exit code

AAB chain `google-play-aab-build.ps1`, run start `20:29:32`, `AAB_DONE 20:46:27`
(`Builds/aab-chain-wrapper.out.log`, `Builds/aab-build.log` mtime 20:45:16):

```
ADDRESSABLES_CONTENT_OK 756 locations :: AndroidBuild target=Android
[GooglePlayPackagingGate] PLAY_SOURCE_ISOLATION_OK
PLAY_NEUTRAL_TOKEN_SWEEP  canon-strings.json 13 · packs.json 5 · siege-stakes.json 1 · ad-placements.json 2 · structures-catalog.json 1
[GooglePlayPackagingGate] PLAY_ARTIFACT_CLEAN_OK
[AndroidBuild] SUCCEEDED — 433 MB
AAB_OK → AAB_SIGNING_OK → R2_PARITY_OK (targets=Android,StandaloneWindows64,WebGL objects=204)
       → AAB_SIZE_OK 451607675 (48392325 under 500000000) → AAB_DONE
```

APK chain `overnight-apk-build.ps1`, run start `20:46:26`, APK on disk `20:53`
(`Builds/apk-chain-wrapper.out.log`, `Builds/apk-build.log`):

```
[AndroidBuild] SUCCEEDED
R2_PUSH_OK 2 uploaded (0.1 MB), 1123 unchanged
R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=204
```

Post-build gate on the same tree: `REGRESSION_OK 539/539 suites -- 539 green, 0 red, 0 skipped`
(`Builds/data-regression.log`, mtime 21:01), including the new `ARCANE_SPIRE_ART_OK`
(`[arcane-spire-art]`, 6 addresses, 21 assertions).

**Compile-gate caveat, recorded honestly.** The standalone `CompileGate.Run` at 20:25 produced NO
`COMPILE_GATE_OK` marker: the log shows a `Lifecycle ERROR … NullReferenceException` domain-reload
fault after the gate started its WebGL pass, with `errorCS=0` in project code (the only CS lines are
the 21 known advisory `CS1069 WebGLInput` lines from the Solana package that the gate itself labels
`(package, advisory)`). Compile proof for this release therefore rests on the two player builds
(`errorCS=0 underAssets=0` on both `[run] VERDICT=PASS` lines) and the 539/539 regression run,
which compiles the editor assemblies before executing. The gate fault is a separate defect to ticket.

## 3. What changed vs 371436 — one structure

`WorkOrders/WORK_ORDER_1760_arcane_spire_ships_synty_stand_in.md`. The uncommitted 2026-09-13 dedup
of `Structure_Art.asset` kept the Synty wrapper half of each double-claimed `Structures/ArcaneSpire_*`
address; `ArcaneSpire_2` alone had been mapped to `SM_Bld_Castle_Wall_Tower_L_01` (a wall tower),
hence the upside-down middle tier. The three addresses now resolve only to the owner's
`Assets/StructureContent/ArcaneSpire_{1,2,3}.fbx`; the wrappers and their retheme map rows are gone.

Proof it is in the content: `catalog_2026.09.16.371610.bin` names
`structure_art_assets_structures_arcanespire_1_9b064c41…`, `_2_7d6191569…`, `_3_52e948e6…` —
different hashes from the 371436/371500 catalogs (`_1_5e2c57db…`, `_2_672b96f4…`, `_3_4fcca318…`) —
and `Builds/r2-parity.log` (20:45) lists each as `ok` under `Android/`.

## 4. Remote-update note for the already-uploaded 371436

Catalog-update-on-start is enabled (`m_DisableCatalogUpdateOnStart = False` in the built Android
settings), so the 371436 AAB — had Google accepted it — could have been corrected in place by
overwriting `catalog_2026.09.15.371436.{bin,hash}` in the bucket with a renamed copy of a corrected
catalog. Not needed now that 371610 supersedes it; recorded so the next seat knows the lever exists,
and that it is NOT yet a step in `tools/r2-ship.ps1`.

## 5. Not in this release (held for the next build)

- WO-1761 — raid victory still prints "<Sylas|Grom> joins your party" over the retired companion
  system (owner ruling 2026-09-15: drop the join).
- WO-1762 — hub-ring heights (lumber mill / iron mine / quarry / cathedral to the 4.0 m family),
  removal of the three scenery pallets, invisible-barracks re-check, navmesh bake.
