# WO-1367 RESULT - Android texture pass on Resources/RpgUi: IMPLEMENTED on HEAD

**Verified:** 2026-09-09 (SILO 0B verify-and-flip pass, read-only; no `.cs`, no `.meta` touched)
**Landed in:** `c417d7997` - *"WO-1367: Android texture pass on Resources/RpgUi - 41.3 MB off the Play
download, gap closed"*, 2026-09-04
**Ancestry proof:** `git merge-base --is-ancestor c417d7997 HEAD` -> exit 0 (HEAD = `184c8ff06`, `dev`)
**Diff shape:** `git show --stat c417d7997` -> **408 files, 6763 insertions, 1154 deletions**, all
`Assets/Resources/RpgUi/**/*.png.meta` plus the WO markdown itself. **Zero `.cs`. Zero PNGs.**

## PRIOR STATUS NOTE

This ticket's Status line read **IN PROGRESS 2026-09-04**, not READY TO IMPLEMENT - it was flipped to
IMPLEMENTED from IN PROGRESS. It appeared in the Ready bucket of the 2026-09-09 board because the
bookkeeping never followed the code, not because it was awaiting an implementer.

## FILE:LINE PROOF AT HEAD

`Assets/Resources/RpgUi/troop/troop-archer.png.meta` in the working tree at HEAD, lines 97-105:

```
    buildTarget: Android
    maxTextureSize: 256
    resizeAlgorithm: 0
    textureFormat: 50        <- ASTC 6x6
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 1            <- the override is ACTIVE, not an inert block
```

The same file at `c417d7997` shows the pre-pass state (`textureFormat: 1`, `maxTextureSize: 2048`,
no active Android override) in the diff, so the change is the one this WO specifies.

**The measured close, quoted from the WO's own EXECUTION LOG (2026-09-04):**

```
                        MIN              MAX
BEFORE          510,443,276      510,523,099
AFTER           469,122,568      469,202,267
                                    -41.3 MB
AAB file:  514,062,537 -> 472,637,397 bytes  (-41.4 MB)
```

Under the 500,000,000-byte ceiling by ~30.8 MB, and under the binary reading (524,288,000) as well -
so the MB-vs-MiB ambiguity is moot. Gates recorded on fresh logs that day: `COMPILE_GATE_OK`
(`Builds/wo1367-compile2.log`, `error CS` 0), `RPGUI_TEXTURE_PASS_OK 0 applied / 568 already correct /
7 skipped`, `REGRESSION_OK 358/358 suites` (`Builds/wo1367-regression.log`), `PLAY_SOURCE_ISOLATION_OK`,
`ANDROID_CATALOG_OK`.

**Acceptance items met by the record:** rebuild + `bundletool get-size total` with MIN,MAX before and
after; under the ceiling with the margin stated; zero `.cs`, zero PNGs deleted, zero source dimensions
changed; zero GUID lines changed across 405 metas; the settings are applied by
`AssetImportPostprocessor.OnPreprocessTexture`, so a re-import cannot silently revert them and new art
inherits the pass by construction.

Two corrections are recorded in the WO body and are worth carrying forward: the lead's 4x4/6x6 split
was **reversed before shipping** (116 of the 173 "sharp" files were already 6x6, and promoting them
would have ADDED ~5.5 MiB to a size ticket), and a non-monotonic cap raise was caught and fixed with a
`Min(existing, default)` rule - **`RPGUI_TEXTURE_PASS_OK` was green on the bad run**, so the marker
alone would not have caught it.

## WHAT THE OWNER FELT-TESTS - THE ONE OPEN ACCEPTANCE ITEM

**Look at the UI on device and accept the quality.** ASTC 6x6 uniformly, with 342 files also capped
lower (289 to 256 px, 53 to 512 px). Judge **crispness, edge artifacts and banding** - especially
9-sliced frames/panels and spell icons at their largest on-screen size. (Owner is red/green
colourblind: never a hue question.) `UI_CAPTURE_OK` proves a panel rendered, never that it looks right.
If anything reads as damaged, the tier is a one-line change - `SharpAndroidRoles` is deliberately empty
and is the only place the tier is decided.

## WHAT IS **NOT** PROVEN

- **No size re-measurement was taken this session.** The MIN,MAX figures above are quoted from the
  2026-09-04 execution log; five days of content have landed since. **The AAB is not proven to be under
  the ceiling TODAY** - re-run `bundletool get-size total` before the next Play upload.
- No device screenshots of the re-compressed UI have been captured or opened this session, so no
  quality judgement of any kind is on record - the owner's is the first.
- **The artifact was NOT shippable at that time, for a reason outside this ticket:**
  `PLAY_ARTIFACT_REJECTED` on `canon-strings.json:231` `_storePiSkinNote` (token `solana`), tracked as
  **WO-1363**. Whether that is still open at HEAD was not checked here.
