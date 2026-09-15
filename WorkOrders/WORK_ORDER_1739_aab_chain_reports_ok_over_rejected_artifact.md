# WO-1739 — The Play AAB chain reported `AAB_OK` over an artifact the compliance gate had REJECTED

**Status:** DONE
**Silo:** Build/Ship chain (`google-play-aab-build.ps1`) — no `Assets/` code touched
**Opened:** 2026-09-15 (RCA lane)
**Related:** WO-1740 (the content leaks themselves — BLOCKED ON OWNER RULING), WO-1363/1364 (the gate), WO-1365 (this script)

---

## 1. What happened

2026-09-15 10:22-10:37. The owner asked for a Google Play AAB. `google-play-aab-build.ps1` ran and left a
**signed, 434 MiB, under-ceiling AAB on disk** at `Builds\Android\EchoesOfElarion-GooglePlay.aab`, with
`Builds\aab-status.txt` reading:

```
AAB_START 2026-09-15T10:22:08.6398218-05:00
AAB_SIGNING_PREFLIGHT_OK keystore='dotr-release.keystore' alias='dotr'
AAB_BUILD_MARKER_ABSENT run-unity-method exit=8 - see D:\EoA\Builds\aab-build.log
AAB_OK 2026-09-15T10:37:00.7079572-05:00 path=... size=455031426
AAB_SIGNING_OK [AndroidBuild] RELEASE signing: ...
R2_PARITY_OK 2026-09-15T10:37:19.3922584-05:00 ... objects=201
AAB_ON_DISK 455031426 bytes (434 MiB) ...
AAB_SIZE_OK 451620501 (48379499 under 500000000)
AAB_DONE 2026-09-15T10:37:49.9401908-05:00
```

Exit 0. But `Builds\aab-build.log` carries:

```
[GooglePlayPackagingGate] PLAY_ARTIFACT_DIRTY:
[AndroidBuild] PLAY_ARTIFACT_REJECTED - the AAB contains a forbidden crypto/wallet surface.
  DeNelle.Editor.GooglePlayPackagingGate:AssertBuiltArtifact (string) (at .../GooglePlayPackagingGate.cs:270)
  DeNelle.Editor.AndroidBuild:BuildAndroidArtifact (bool) (at Assets/Editor/AndroidBuild.cs:201)
```

**A human reading the status file would have uploaded a policy-violating artifact to Google Play.**
The lead caught it only by reading the log instead of the status file.

## 2. Root cause — file:line

`google-play-aab-build.ps1`, the block after the Unity invocation, as it stood:

```powershell
    if ($LASTEXITCODE -ne 0) {
        Say "AAB_BUILD_MARKER_ABSENT run-unity-method exit=$LASTEXITCODE - see $buildLog"
    }
} catch {
    Say "AAB_THREW $($_.Exception.Message)"
}
```

Both branches **RECORD and fall through**. Nothing downstream re-reads that verdict, so `AAB_OK`,
`AAB_SIGNING_OK`, `R2_PARITY_OK` and `AAB_SIZE_OK` were all emitted over a rejected artifact.

**Why the existing freshness guard cannot catch it — this is the whole trap.** The script's `AAB_STALE`
check exists precisely to stop "a failed build left an older artifact on disk". It is useless here,
because the Play compliance gate runs **AFTER** `BuildPipeline.BuildPlayer` has already succeeded and
written the file (`Assets/Editor/AndroidBuild.cs:197-204` — `summary.result == Succeeded` is the branch
that then calls `AssertBuiltArtifact`). So a REJECTED run leaves an artifact that is:

- **fresh** (written during this run — `AAB_STALE` passes),
- **correctly release-signed** (`AAB_SIGNING_OK` passes — the signing genuinely happened),
- **under the 500 MB ceiling** (`AAB_SIZE_OK` passes — bundletool genuinely measured it),
- and **unshippable**.

Every existence-and-freshness test passes. **The marker is the only signal there is** — which is exactly
`CLAUDE.md` §8 and memory `gates-report-success-without-proving-it`: judge by the marker on a fresh log,
and treat its **absence as a FAILURE, never an unknown**. The script surfaced the marker absence and then
overrode its own finding.

## 3. This is occurrence TWO, not one — the defect has been live since at least 09-11

`Builds/aab-final-chain-runner.log`, 2026-09-11 18:43-19:00, read at source:

```
[run] VERDICT=FAIL reason=MARKER_ABSENT - this run is NOT PROVEN. marker='[AndroidBuild] SUCCEEDED'
[aab] AAB_BUILD_MARKER_ABSENT run-unity-method exit=8 - see D:\eoa\Builds\aab-build.log
[aab] AAB_OK 2026-09-11T18:59:19 path=...EchoesOfElarion-GooglePlay.aab size=455113849
[aab] AAB_SIGNING_OK ...
[aab] AAB_SIZE_OK 451684148 (48315852 under 500000000)
[aab] AAB_DONE
```

Identical shape, identical exit=8, four days earlier. `run-unity-method.ps1` did its job correctly both
times — it printed `VERDICT=FAIL ... NOT PROVEN` and returned 8. The wrapper discarded the verdict.

## 3b. ⛔ PROVEN: the two 09-07 store AABs are dirty too — occurrences THREE and FOUR

Both files in `Builds/Android/store/` were opened as zips and scanned directly (not inferred). **Each one
carries every forbidden surface the 09-15 run was rejected for:**

| Probe | `...GooglePlay-2026.09.07.359670.aab` | `...GooglePlay-2026.09.07.359746.aab` |
|---|---|---|
| `jupiterswappanel` | present (`bin/Data/2e41928d...`, `b16d3476...`, `globalgamemanagers`) | present (same three) |
| `Powered by Jupiter` | present (`bin/Data/b16d3476...`) | present |
| `SKR: unavailable in this build` | present (`Data/Canonical/canon-strings.json`, `bin/Data/fe03eb5a...`) | present |
| `live on the Solana dApp Store` | present (incl. `global-metadata.dat`) | present |
| `SKR on-ramp` | present (`Data/Canonical/ad-placements.json`) | present |

`Assets/_Modules/Web3/Resources/JupiterSwapPanel.uxml` was added **2026-05-26** (`5d13d5b3b`, WO-43), so it
predates every AAB in the tree. `"SKR: unavailable in this build"` was added **2026-09-03** (`bcecb5991`,
WO-1334/1335), before the 09-07 builds.

**Conclusion: nothing in this tree has ever passed the artifact gate.** No log in `Builds/` contains
`PLAY_ARTIFACT_CLEAN_OK`. The Play lane has been red since the gate was hardened on 2026-09-04
(`6979fb961`, WO-1363/1364), and the reporting defect hid every instance. **The two 09-07 store AABs must
not be uploaded** — if the owner believed they were shippable, that belief came from this defect.

**Therefore "why now" is: it is NOT now.** Neither of the two candidates in the brief is the cause.
Today's commits (`50370c1fb`, `06da9b7b6`, `658f24b8a`, `dbe6b9544`, `452fc14fd`, `11350ed9d`) touched none
of the offending files nor `GooglePlayContentExclusion.cs`. The new `OwnedTown_IronBastion` /
`ArenaPractice_IronBastion` scenes are not the cause either: **not one** entry in the `PLAY_ARTIFACT_DIRTY`
list is a `level*` or `sharedassets*` scene payload — every hit is `Resources` / `StreamingAssets` /
IL2CPP metadata. What changed is only that someone read the log.

## 4. The fix (this WO)

`google-play-aab-build.ps1` only. **No `Assets/` file was touched** — the strip/isolation decision is
WO-1740 and needs an owner ruling.

- Both failure branches now set `$buildProven = $false` instead of only logging.
- A new verdict block takes its answer **from the log, not the exit code**: the log must postdate
  `$startedAt`, must carry `[AndroidBuild] SUCCEEDED`, and must **not** carry any of
  `PLAY_ARTIFACT_REJECTED`, `PLAY_ARTIFACT_DIRTY`, `PLAY_ARTIFACT_MISSING`,
  `PLAY_SOURCE_ISOLATION_FAIL`, `PLAY_NEUTRAL_UNMAPPED_TOKEN`, `PLAY_NEUTRAL_REWRITE_FAIL`.
  The `$LASTEXITCODE -ne 0` test is **kept** as well, because an unset `$LASTEXITCODE` is `$null` and
  `$null -ne 0` is TRUE — it fails closed (memory `prove-the-success-path`).
- On failure: emit `AAB_REJECTED`, copy the gate's `PLAY_ARTIFACT_DIRTY` bullet list into the status file
  so it names the actual entries and tokens, **move the artifact** to
  `Builds\Android\rejected\EchoesOfElarion-GooglePlay-<stamp>.REJECTED.aab`, then `AAB_DONE` and
  **exit 7** — *before* `tools\r2-ship.ps1` and before the size guard.
- `R2` behaviour is untouched. The `tools\r2-ship.ps1` delegation (§16, the ONE file) is unchanged; it is
  simply not reached on a rejected run, which is correct — there is nothing to ship.
- Header updated: new markers `AAB_REJECTED` / `AAB_BUILD_UNPROVEN` / `AAB_QUARANTINED`, new exit code 7.

Why the artifact is **moved** and not merely flagged: it is signed, correctly named and correctly sized,
so nothing about the *file* warns a human off it. Only its location can.

## 5. Acceptance criteria

- [x] A run whose Unity step lacks `[AndroidBuild] SUCCEEDED` on a fresh log emits **no** `AAB_OK` and
      **no** `AAB_SIGNING_OK`.
- [x] A run whose log carries `PLAY_ARTIFACT_REJECTED` / `PLAY_ARTIFACT_DIRTY` stops before R2 and before
      the size guard, with a non-zero exit.
- [x] The rejected artifact does not remain at `Builds\Android\EchoesOfElarion-GooglePlay.aab`.
- [x] Marker absence is treated as failure, not unknown (`CLAUDE.md` §8).
- [x] `tools\r2-ship.ps1` integration unchanged; push/verify not re-inlined (§16).
- [x] ASCII-only, parses under Windows PowerShell 5.1 (verified: `Parser::ParseFile` -> 0 errors,
      0 non-ASCII bytes).
- [x] No `.cs` touched, so no brace/NUL gate applies to this lane.

## 6. What NOT to touch

- **Do not weaken `GooglePlayPackagingGate`.** It was right. See WO-1740.
- **Do not touch `overnight-apk-build.ps1` in this lane.** It shares the defect *shape*
  (`:72-77` — `throw` caught by its own `catch` into `APK_THREW`, then falls through to `APK_OK`), but it
  is a different script on a different lane with **no packaging gate** (the Seeker APK is *supposed* to
  carry the wallet), so the blast radius and the correct remedy differ. Recorded here, deliberately not
  fixed here.
- **Do not weaken `tools\r2-ship.ps1`** (§16).
