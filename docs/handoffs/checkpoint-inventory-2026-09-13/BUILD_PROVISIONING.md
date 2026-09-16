# Detached checkpoint build provisioning

Read-only findings, 2026-09-13. No packs/configs copied, no Unity run, no Git mutation. Paths below
are relative to the source/build project root; credential values were neither read nor recorded.

## Established procedure and its limit

`tools/art/REQUIRED_PACKS.md` establishes **tracked runtime plus zip/local-copy pack travel**.
`tools/art/verify-runtime-art.ps1` checks selected tracked fallbacks and pack presence; use it in the
destination after LFS hydration. It is a useful gate, not proof of complete source-art equivalence.

`build-webgl-isolated.ps1` is the existing separate-Library precedent, but **do not execute it as-is
for this local checkpoint**: it fetches origin and hard-resets its destination to `origin/<Branch>`.
That would discard the locally validated checkpoint from the build worktree. Its six-directory pack
copy list is also incomplete for today's tree. Reuse its concept (local copying, independent warm
Library), not its origin-reset or old pack list. Run the standard target wrapper from the already
provisioned checkpoint worktree so `$PSScriptRoot` resolves the checkpoint project.

## Pack inputs observed on this machine

`ignored-assets-observed.paths.txt` records the exact observed ignored Assets filenames. It includes
source-pack files, generated inputs, configs, and disposable files, so it is a review inventory rather
than an unconditional copy list. Root can select a pack closure without relying on stale ignore docs.

| Input class | Observed roots / handling |
| --- | --- |
| Character/environment packs | `Assets/Models`, `polyperfect`, `Synty`, `Blink`, `Supercyan`; copy required ignored contents with their `.meta` files. Models includes KayKit/Mystery/People shared skins. Do not overwrite tracked People files from the dirty source after checkpoint checkout. |
| VFX/UI packs | `Assets/Spells Pack`, `Mirza Beig`, `Hovl Studio`, `UnityTechnologies`, `Tech hud elements`, `Leohpaz`, `Lana Studio`; provision current used input closure. ParticlePack has documented runtime uses without a fallback. |
| Supporting tools/resources | `Assets/MeshBaker`, `TextMesh Pro`, `Standard Assets`, `Editor Default Resources`; distinguish required imported resources from optional examples. |
| Generated but currently referenced assets | **`Assets/Generated/RaidGround/*.mat` and `.meta`**, including OwnedTown and raid floor/keep-platform/ramp materials; also `Assets/Generated/Animators/*`. These are absent from the ordinary dirty-path snapshot and must be preserved/provisioned, not dismissed as build garbage. |
| Android local Maven inputs | `Assets/GeneratedLocalRepo/Firebase/...` and `.../GoogleSignIn/...` AAR/POM files with their parent/folder metas. Preserve or regenerate using the established dependency resolver before building. |
| Android Firebase generated lib | `Assets/Plugins/Android/FirebaseApp.androidlib/` plus sibling meta; generated from the appropriate Firebase config. |

`ignored-generated-build-inputs.paths.txt` lists the generated asset/Maven/lib subset explicitly.
The original isolated script additionally lists `Assets/Art/TripoStructures` and
`Assets/Resources/Structures`; they do not appear as ignored files in the current enumeration.
Do not infer absent Tripo art: required Tripo assets may be tracked/current snapshot inputs elsewhere.
Likewise Quaternius is in the travel document but not in this machine's ignored-file enumeration.

Current Firebase and ExternalDependencyManager ignored files are PDBs/metas; do not assume their
entire SDK is missing from Git based on the broad `.gitignore` comments. The tracked checkpoint
already supplies tracked binaries. Native iOS/tvOS files and CW examples are not Android/Windows/WebGL
prerequisites solely because they are ignored.

## Safe, efficient transfer plan for root

1. Checkout the checkpoint with LFS objects hydrated. Freeze pack-source imports while provisioning.
   Verify both resolved absolute roots are distinct and destination paths remain within the named
   build workspace. Preserve original Tripo castle source/scene bytes; do not run art conversion or
   scene rebuild commands as a provisioning shortcut.
2. Derive an explicit allowed ignored-input manifest from the inventory, selected runtime dependency
   closure, and local build-config list. Transfer only those relative filenames plus their metas.
   For wholly ignored pack directories, local `robocopy /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /XJ` is the
   existing copy approach without destructive mirroring. For mixed tracked/ignored roots such as
   Models, copy the explicit ignored-file set with native `Copy-Item -LiteralPath`; never overlay
   the whole source directory onto checkpoint tracked files. Treat robocopy codes >=8 as failures.
3. On a warm destination, compare size/time then hash changed candidates; record final path/hash
   equality for required inputs. Do not use `/MIR`, junctions, or hardlinks to share writable source
   or Library. Exclude source archive duplicates (`.zip`, `.unitypackage`, etc.) only after confirming
   no builder consumes them; exclude caches, old ServerData, performance-test Resources JSON, and
   old Addressables content-state files from a fresh full-content-build seed.
4. Give the build worktree its own Library/Temp/obj/Logs/Builds/ServerData. Do not copy the active main
   Library or share it across editors. Keep the provisioned worktree warm for later targets/builds.
   Run art presence checks, compare checkpoint tracked status, and run actual compile/capture/build
   gates. Merely copying packs does not validate their references.

## Local configuration / credentials — names only

Observed present at the source: `keystore.properties`, `firebase-appid.txt`, `.env.local`, `.env.r2`,
`.vercel/project.json`, `Assets/google-services.json`, and
`Assets/StreamingAssets/google-services-desktop.json` (carry applicable metas).

- Android signing needs `keystore.properties` keys `keystore.path`, `keystore.alias`,
  `keystore.storepass`, `keystore.keypass`, plus the existing referenced keystore file. Preserve
  identity; do not create a new key. Relative keystore paths must resolve correctly from the build
  root. Keep credentials outside the checkpoint index; the AAB wrapper checks signing before/after.
- R2 wrapper consumes root `.env.r2` through `tools/r2_sync.py`: `R2_S3_ENDPOINT`, `R2_BUCKET`,
  `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`, `R2_PUBLIC_URL`. Provision privately or use the known
  operator context, without echoing contents. Fresh content builds still require fresh R2 parity.
- Firebase distribution uses `FIREBASE_APP_ID`/`firebase-appid.txt` plus the operator's existing CLI
  authentication; no need to copy user-wide authentication stores into the worktree.
- Vercel uses the correct project link and operator authentication / `VERCEL_TOKEN`. `.env.local`
  also contains backend/ops credentials; copy only if the selected wrapper actually needs them.
  `tools/ship-web.ps1` loads `.env.local` and delegates the wider command-centre pipeline; it is not
  a minimal preview-only publisher. Never copy `key.json`, private wallets, arbitrary `.env*`, or
  user-wide credential directories as part of pack provisioning.

## Runner changes to include explicitly

The Unity-only snapshot candidate omits these dirty runner files; append approved exact paths:

- `run-unity-method.ps1`: complete-token marker matching, and license-error classification that
  no longer masks a succeeded run.
- `tools/run-unity-playmode.ps1`: complete-token markers and explicit timeout/compiler-error failure.
- `tools/status-post.mjs`: honors `EOA_LOCAL_STATUS_ONLY=1`; include and set this for local verification
  so child runners cannot send unrequested outbound status messages.

Unchanged target wrappers already come from HEAD: Windows, APK, AAB, WebGL, editor-pin assertion and
R2 ship scripts. Execute them from the checkpoint, not the dirty source root. AAB goes through
`google-play-aab-build.ps1` for signing, fresh-build, R2 and size proof.

## Food-to-Stone and Vercel deployment closure

Current client `ResourceBalance.Stone` still serializes as JSON **`food`**
(`Assets/_Modules/Core/State/NestedTypes.cs`); `docs/food-migration/SAVE_FIRST.md` explicitly preserves
old wire keys. Do not rename backend food keys merely to match the C# identifier. The current dirty
API changes are owned-town isolation: `api/game/save.js` refuses `pendingTownCapture` and
`PendingTownCapture`, exports `buildState`; `api/game/load.js` strips those device-local intents.
Related tests are `test/game.save.owned-town.test.js` and `test/game.save.reset-epoch.test.js`.
If deploying the backend, these changes and existing auth/schema/save compatibility need their own
review and tests; a successful Unity build does not prove them.

**Deploy root is consequential:** root `.vercelignore` explicitly uploads `api/`, `package.json`,
`package-lock.json`, `vercel.json`, and `Builds/WebGL`. `tools/web-ship.ps1` uses the repo root for
its WebGL project entries and performs production/alias operations, so it is not the requested
preview-only route unchanged. A repo-root preview includes serverless backend code even if nobody
intends to change the API. For a WebGL-only preview use a deliberately staged payload and the correct
existing project routing/config; verify API calls still resolve to their intended backend. Do not
point a copied root `outputDirectory=Builds/WebGL` configuration at an already-flattened WebGL folder.
No Vercel command was executed by this lane.
