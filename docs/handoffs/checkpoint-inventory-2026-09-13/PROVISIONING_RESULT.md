# Detached build input provisioning - 2026-09-13

Completed reviewed local copies from `D:/eoa` into `D:/eoa-night-build-20260913` under BUILD_PROVISIONING.md. Destination started at detached `70e8cc8e249a7e3e3fd3a6b1f0b3ebef8147e29d`; parent owns subsequent checkpoint checkout and builds. No Unity, deployment, Git/index mutation, hardlinks, junctions, mirror deletion, or credential-root copy was performed in this lane.

## Exact copy result

- Inventory: 124,494 paths; SHA256 `e9c9ad73fd138b1546c73e47f12fac0e8af1d8dae0e9a2d02ea35605bdb3907f`.
- Copied and source/destination SHA256 verified: **124,160 files, 18,807,308,169 bytes (17.5157 GiB)**. Final zero copy/hash errors.
- Excluded: **334** (6 old Addressables content-state files/metas; 134 archives/backups/debug/probe/link files; 194 non-target iOS/tvOS files and optional CW examples).
- Legitimate nested source-art `obj` mesh/material folders retained. No project Library/Temp/obj/ServerData copied. SDK metas, generated animator inputs and generated RaidGround inputs retained. All 27 RaidGround material/meta records freshly rehashed equal after copying.
- Only the explicitly reviewed applicable Firebase google-services configs and metas copied; contents not printed. No keystores, env/auth stores, or deployment credentials copied.
- Final manifest: `Builds/night-build-provisioning/manifest.jsonl`; SHA256 `42f6a8ac8b814c773266bb4ac6b982cb5ea425289e80f50a2c0cfd9d5686f48e`. Each row records path, size, source/destination hash and source mtime. `summary.json`, `selected.paths.txt`, `excluded.jsonl`, and pass manifests are adjacent ignored evidence.
- Initial conservative filtering excluded legitimate nested art obj folders; corrected in a second pass. Transient Windows long-path namespace normalization caused safe pre-write refusals, recovered by bounded retries after canonicalizing the drive prefix. Final zero unresolved errors; no out-of-tree writes. Sum of measured copy/verify pass times: 518.69 seconds.
- Destination tracked status was clean immediately after provisioning. The checker/doc followup below intentionally changes two tracked files until the parent includes its checkpoint; this is recorded, not hidden.

## Runtime-art checker reconciliation

Original destination command `powershell -NoProfile -File tools/art/verify-runtime-art.ps1` exited 1: eleven obsolete Resources paths missing and one pack warning. Original log: `Builds/night-build-provisioning/verify-runtime-art.log`. All twelve missing paths were also absent in the source, so these were not copy failures. The eleven current assets existed and were hydrated under EnemyContent/HeroContent; evidence `relocated-critical-assets.json`.

Migration authority is WO-1338, including the owner R2 ruling, the enemy migration history, and intentional retention of local Knight troop body/controllers. Current runtime evidence: `EnemyAssetLoader` uses Enemies keys via resident cache/asynchronous requests, consumed by EnemyFactory/EnemyAnimatorFactory/AtbCombatantSwapper; `HeroBodySwapper` uses HeroAssetLoader and HeroContentPrewarmer retains the selected body before scene entry. EnemyContentMigrator preserves extensionless addresses. This supersedes the July bare-Resources body contract; no missing fallback was fabricated or masked.

Updated only `tools/art/verify-runtime-art.ps1` and its `REQUIRED_PACKS.md`: eleven exact current paths with pinned meta GUID, exact address, one GUID registration, expected registered group, linked enabled remote bundle schema and remote profile IDs. Empty/LFS-pointer tracked files fail. Local NPC/People checks remain. Documentation explicitly says static inputs/bindings do not prove offline playability, remote publication, dependency closure or rendered art. The missing People/textures pack warning remains visible.

Actual validation after correction:

- Root checker exit **0**, `RUNTIME-ART OK (static tracked inputs/bindings verified; source pack warnings)`; `verify-runtime-art-root-current.log`.
- Destination checker exit **0**, same result; `verify-runtime-art-destination-current.log`.
- Both retain one warning: `Assets/Models/People/textures`, absent at source and destination. This run is default mode, not Strict.
- In-memory negative checks accept the real Skeleton_Warrior binding and reject wrong GUID, address, group, and path without any asset mutation: `CHECKER_BINDING_NEGATIVE_OK`; `verify-runtime-art-negative.log`.
- `git diff --check` for both edited checker files passed. The identical owned script/doc were copied into the destination with explicit parent authorization; destination status records both modified until checkpoint checkout.

## Existing LFS warning (inspect only)

`Assets/Resources/Heroes/Knight.fbm/knight_basecolor.JPEG` is an actual 3,173,046-byte JPEG in source, destination and the tracked HEAD blob despite the LFS filter attribute. Source/destination SHA256 both `5e747464ab64ac7effd5cdd0123296881ec695a44bd4641b9e16ffef4a71d638`. This explains the malformed-pointer warning: raw JPEG committed under an LFS filter, not missing hydration. No asset or Git repair attempted.

## Limits

This is copy/hash and static input/binding evidence. No Unity import, compilation, full art dependency closure, runtime render, remote catalog/bundle HTTP checks or player builds were performed by this lane. Parent retains those gates and all release/publication ownership.

## Root refresh after full-height raid regeneration

Root regenerated three configured raid scenes and ran RaidNavBake.BakeAll (five scene inputs). Rehashed all 26 child files under Assets/Generated/RaidGround; six changed and were copied only after verifying destination prior hashes against the original manifest. Folder meta is outside this child prefix and unchanged. Builds/night-build-provisioning/raid-height-refresh.jsonl records old/current source/destination hashes; SHA256 4af6a08f811939dc89ba455753a784138200243d323b4c1194a1a2f565f6587d. Use this delta with the immutable original manifest. Root privately provisioned the four existing release credential/config files and made the configured signing key available; no values printed or committed.
