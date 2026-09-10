# WORK ORDER 1092 — RESULT

**Status:** IMPLEMENTED - awaiting gate (2026-09-09 lane CACHE)
**Lane:** CACHE (edit-only). No Unity run, no `git add`, no commit — by lane rule.
**Branch:** `dev`. Baseline HEAD at lane start: `184c8ff06`.

---

## 0. Reconciliation of the untrusted working-tree edit (the trap)

`WO_1089_1094_WORKING_TREE_NOTE.md` records that a UI-seat edit agent was **killed mid-edit** on
`Assets/_Modules/Core/Addressables/OfflineContentService.cs` and the half-written file was swept into
commit `6a5c7a36d`. Reviewed by explicit path before touching anything.

```
git diff f4e4630e3 6a5c7a36d -- Assets/_Modules/Core/Addressables/OfflineContentService.cs
```

**The entire swept diff is TWO added `using` directives and nothing else** (`OfflineContentService.cs:115-116`
as committed; `:116-117` after this lane's `System.IO` insertion):

```
+using UnityEngine.ResourceManagement.ResourceLocations;
+using UnityEngine.ResourceManagement.ResourceProviders;
```

- **No dangling method, no partial body, no orphan brace, no truncated string.** The agent was stopped
  after writing its imports and before writing any member.
- Confirmed nothing landed afterwards: `git log --oneline f4e4630e3..HEAD -- <file>` lists only
  `6a5c7a36d`, and `git diff --stat 6a5c7a36d HEAD -- <file>` is **empty**. `git status --porcelain --
  Assets/_Modules/Core/Addressables/` was clean at lane start, so the working tree equalled HEAD.
- Braces 273/273 and **0 NUL bytes** measured on the reconciled file after this lane's edits.

**Decision: KEEP both usings.** They are not debris — `IResourceLocation` (`ResourceLocations`) and
`AssetBundleRequestOptions` (`ResourceProviders`) are exactly the two types the cause-level fix needs to
read a key's bundle dependencies synchronously. Nothing was removed; the fix is built on top of them.

---

## 1. The cause, re-proven at source this session

The ledger (`docs/READY_RCA_2026-09-09.md` section WO-1092) reconstructed the cause from the retained
catalog and cache. **This lane re-verified the on-disk half by hand rather than trusting the summary**
(CLAUDE.md §11B — a value copied from a doc is hearsay until re-read at source):

```
C:\Users\Elden\AppData\LocalLow\Unity\DeNelle_Echoes of Elarion.quarantine-20260908-raid-crash\
  912fa7abd447ca13619697b1fce844c6\
    2220522384eb58b0db363f6c6e1b47ab\  __lock   0 bytes, 2026-09-08 14:51
    3c9df7ea9a3c1566f0cde489711dfec6\  __data   19,400,698 bytes, __info 23 bytes
```

Two facts fall out of that listing, and both were assumptions before it:

1. **The on-disk layout is `<cacheRoot>/<bundle-dir>/<hash>/{__data,__info,__lock}`** — confirmed, not
   inferred. The scan's path construction is therefore correct.
2. **The version directory is named by the catalog HASH**, while the bundle directory is an opaque
   hash of the bundle name. That is what makes a *name-independent* scan able to *name* the bundle:
   the hash is carried by `AssetBundleRequestOptions.Hash` on the resolved locations, so a stale
   version dir can be looked up and logged with its real bundle name.

The current version holds a lock and no payload; the previous version is complete. Unity opened the
cache transaction, abandoned it before commit, and kept the lock — so every fetch of that bundle
succeeded in memory and never committed. `19,151,184 + (3 x 19,398,472) = 77,346,600` names the second
half: three separate 24-**address** chunks each re-requested the same shared family bundle, because
`MergeMode.Union` deduplicates *inside* a chunk and cannot deduplicate *across* chunks.

---

## 2. Files changed

| File | Change |
|---|---|
| `Assets/_Modules/Core/Addressables/OfflineContentService.cs` | the fix (below) |
| `Assets/Editor/Regression/OfflineCacheRepairRegression.cs` | **NEW** — the suite |
| `WorkOrders/WORK_ORDER_1092_*.md` | Status flipped; the "do NOT implement the retry" section given a dated supersession banner citing the ledger |
| `WorkOrders/WORK_ORDER_1092_*.RESULT.md` | this file |

**Not touched, by lane rule:** `StructureContentWarmer.cs`, `EnemyContentWarmer.cs` (WO-1089),
`DataRegression.cs` (registration line handed over below).

### (a) Repair the abandoned transaction, then ONE bounded retry

- `ClassifyCacheVersion(...)` — **pure**, filesystem-free, so the suite can pin both directions
  without staging a download. Returns `Healthy` / `Empty` / `LiveTransaction` /
  `AbandonedTransaction` / `Unknown`. Everything it is not certain about is `Unknown` and is never
  deletable: a non-32-hex directory name, a half-present payload, foreign files inside, an unknown
  lock age.
- `CacheLockStaleSeconds = 300` — a lock-only directory is only called abandoned once it is old.
  A content warmer can legitimately hold a live transaction at boot; deleting one would manufacture
  the corruption this repairs. The **retry pass passes 0** instead, because the pull's own handles
  are released by then — otherwise the retry would refuse to repair the very transaction the failed
  attempt just abandoned, which is the whole point of the retry.
- `RepairAbandonedCacheTransactions(factsByHash, attempt, minStaleAgeSeconds)` — walks
  `Caching.currentCacheForWriting.path`, classifies each version directory, and removes **exactly**
  the abandoned ones. Wrapped in `Guard.Try`. Deletion is verified with `Directory.Exists` afterwards
  and only then counted; a delete that throws, or that silently leaves the directory, logs
  `FlowTrace.Fail` and is **not** counted as repaired.
- **Liveness probe before every delete.** The age bound cannot see a *concurrent writer*, and the
  retry pass deliberately drops that bound to 0. So immediately before deleting, the repair tries to
  open `__lock` with `FileShare.None`; if anything else holds a handle, the version is reclassified
  live, logged `LEFT ALONE` with the OS reason, and skipped. If Unity does not hold the file open
  during a transaction the probe simply always passes — it costs nothing and can only ever prevent a
  wrong delete.
- **Targeted, never broad.** No `Caching.ClearAllCachedVersions` (it would also drop the complete
  19,400,698-byte previous version) and no route through `AddressablesCacheHealth.ReportDownloadFailure`
  from the repair path (that arms a whole-cache `ClearCache()` next launch). Both are pinned by the
  suite's lint.
- `MaxPullAttempts = 2` — a constant, not a `while`. One normal pass, one retry after the repair. If a
  bundle structurally cannot cache, the pull fails honestly instead of looping.

### (b) Chunk by unique bundle, not by address

- `ResolveKeyBundles(keys, factsByHash)` — walks each key's locations with the **synchronous**
  `IResourceLocator.Locate` and reads `AssetBundleRequestOptions` off each dependency (depth-bounded
  at 6 so a cyclic locator cannot hang the pull). One walk feeds both the chunk plan and the
  diagnostic — no async handles, so 682 keys cost no operations.
- `PlanUniqueBundleChunks(entries, chunkSize, out unique, out skipped)` — **pure**. A key joins a
  chunk only if it introduces at least one unclaimed bundle. A key whose bundles are all claimed is
  folded in (its bytes are already in the plan; coverage unchanged). **A key that resolves to NO
  bundle is always kept** — dropping unknowns is precisely how PROD-010 read zero keys as "already
  cached".
- **Coverage can only grow, never shrink — two guards, because one was not enough.** The walk sits
  inside a `Guard.Try`; a throw part-way through would leave `entries` SHORT and every key after the
  throw would silently drop out of the plan (the PROD-010 coverage-cut shape, one layer down, and the
  verifier would only catch it after the fact). So `ResolveKeyBundles` compares `entries.Count`
  against `keys.Count` and **backfills every un-walked key as an UNKNOWN** (the planner always keeps
  unknowns), with a `FlowTrace.Warn` naming the count. Separately, if planning ever yields zero
  chunks, the pull **falls back to plain address chunking and says so**.
- `MeasureDownloadSize` is still called with the **full `keys` list**, never the plan — so the
  verifier's denominator and its post-pull re-measurement always cover the whole set regardless of
  what the plan did.

### (c) The verifier is untouched, and the WO's diagnostic still ships

- `PullVerified` and `MeasureDownloadSize` are **byte-for-byte unchanged, proven by hunk range, not
  by memory.** `git diff HEAD -- <file>` produces exactly four hunks: `@@ -108,6`, `@@ -296,6`,
  `@@ -672,6`, `@@ -743,85`. At HEAD `PullVerified` occupies lines **270–297** and
  `MeasureDownloadSize` **630–650**. The `-296,6` hunk's first two lines (`return true;` / `}` at
  296–297) are pure **context**; every `+` line in it lands at 298 or later. No hunk overlaps
  630–650 at all. The suite additionally lints that `if (remainingBytes > 0)` still returns false.
- `LogOutstandingBundles(factsByHash)` runs on the final non-verified verdict and names each
  still-cached-missing bundle's `BundleName`, `Hash`, `Hash128.Parse(...).isValid` and `BundleSize`,
  **capped at 8** plus a total count and an invalid-hash count. Synchronous via
  `Caching.IsVersionCached`. The cap is deliberate: this fires over a ~682-key set and an uncapped
  dump evicts the surrounding evidence out of the 256 KiB Android logcat ring (memory
  `logcat-ring-buffer-destroys-evidence`).

### FlowTrace on every branch (CLAUDE.md §12 — never stripped)

Scan start (with the threshold in the line) · `LEFT ALONE` (live transaction — by lock age, or by the
exclusive-open probe reporting the OS reason it is held) · short-walk backfill `Warn` (how many keys
were re-added as unknown) ·
`ABANDONED TRANSACTION found` (bundle name, hash, hash validity, size, lock byte count, age, path) ·
`REMOVED` (verified gone) · delete-threw / still-exists `Fail` · per-attempt outcome ·
`STILL OUTSTANDING` per bundle · a summary naming counts. The plan line reports chunk count, distinct
bundles and folded keys. The final `NOT VERIFIED` line now carries `attempts=` and
`cacheTransactionsRepaired=`. The suite's lint group fails if the found / left-alone / still-outstanding
lines are ever deleted.

---

## 3. RED-first proof

The suite is RED against `HEAD` (`184c8ff06`) **by construction — it cannot compile there**. Every
symbol it calls was absent from the pre-fix file, measured directly:

```
git show HEAD:Assets/_Modules/Core/Addressables/OfflineContentService.cs | grep -c <symbol>
  ClassifyCacheVersion              0
  PlanUniqueBundleChunks            0
  RepairAbandonedCacheTransactions  0
  LogOutstandingBundles             0
  CacheLockStaleSeconds             0
  MaxPullAttempts                   0
  KeyBundleSet                      0
git show HEAD:Assets/Editor/Regression/OfflineCacheRepairRegression.cs
  fatal: path ... exists on disk, but not in 'HEAD'
```

Because Unity cannot be run from this lane, the assertions were additionally executed as a
**line-for-line logic mirror** against the *real* artefacts, so the expected values in the suite are
measured rather than reasoned:

```
2220522384eb58b0db363f6c6e1b47ab  lock=True data=False info=False other=0 age=109119s -> AbandonedTransaction
3c9df7ea9a3c1566f0cde489711dfec6  lock=False data=True info=True other=0 age=-1s     -> Healthy
16 addresses / 1 shared bundle -> 1 chunk, 1 key, unique=1, skipped=15
mixed 6-key set               -> unique=6, skipped=1 ('c' folded in, 'd' kept)
unresolved-key set            -> u1,u2,u3 all kept, skipped=1
```

The real Orc directory classifies as `AbandonedTransaction` (lock 30.3 h old) and the real complete
sibling classifies as `Healthy` — i.e. the detector fires on the captured defect and protects the
recoverable 19,400,698 bytes. Every mirrored value equals the constant asserted in the suite.

**Suite groups:** 0 meta (the recorder must be able to record a failure) · 1 stale · 2 live · 3 healthy
· 4 refuse-on-doubt · 5 on-disk fixture (a real zero-byte backdated `__lock` beside a complete sibling,
in a temp dir it creates and deletes) · 6 shared-bundle-counted-once · 7 unresolved keys never dropped
· 8 source lint (seams wired, retry bounded, repair targeted, verifier still strict, FlowTrace intact).

**Registration line for `Assets/Editor/Regression/DataRegression.cs` `RunAll` (fully qualified, matching the `:348` / `:401` style):**

```csharp
            if (!DeNelle.Editor.Regression.OfflineCacheRepairRegression.Run(out var offlineCacheRepairReason)) failures.Add(offlineCacheRepairReason); else log.AppendLine("[offline-cache-repair] " + offlineCacheRepairReason);
```

Markers: `OFFLINE_CACHE_REPAIR_OK` / `OFFLINE_CACHE_REPAIR_FAIL`.
Standalone: `run-unity-method -Method DeNelle.Editor.Regression.OfflineCacheRepairRegression.RunAll`.

---

## 3b. Gate RED on the first pass, and the fix — WebGL, not a missing `using`

`Builds/wave1-compile2` (2026-09-09 23:24) failed with:

```
Assets\_Modules\Core\Addressables\OfflineContentService.cs(980,30):  error CS0103: The name 'Caching' does not exist in the current context
Assets\_Modules\Core\Addressables\OfflineContentService.cs(1153,47): error CS0103: The name 'Caching' does not exist in the current context
```

**Not a missing import.** `using UnityEngine;` was already present at `:112`. The gate log classifies
both under **`[CompileGate] WEBGL-ONLY COMPILE ERRORS`** — invisible to the active Android target and
reachable only by a WebGL content build. `UnityEngine.Caching` and `CachedAssetBundle` live in
`UnityEngine.AssetBundleModule`, which is not part of a WebGL player: **WebGL has no AssetBundle
cache at all**, so the identifiers genuinely do not exist there. Corroborated in-tree — the sibling
`AddressablesCacheHealth.cs:20` already guards its own `Caching.ClearCache()` call with
`#if !UNITY_EDITOR && !UNITY_WEBGL`, i.e. this file was the outlier.

Fixed by platform-guarding the two sites, **not** by an import that would only have hidden it on
Android. Every other branch still compiles on every target, and each guard names its decision in the
trace (CLAUDE.md §12 — the WebGL path is a `FlowTrace.Step`, not a silent skip):

| Directives | Site | Behaviour on WebGL |
|---|---|---|
| `:979 #if UNITY_WEBGL` / `:993 #else` / `:1099 #endif` | `RepairAbandonedCacheTransactions` — the `Caching.currentCacheForWriting.path` scan (now `:995`, inside the `#else`) | `FlowTrace.Step` "NOT APPLICABLE on WebGL … nothing to repair", returns 0. The `inspected`/`repaired`/`live` counters are named in that line so they are read on this target too (an int assigned-but-unused behind a platform guard is CS0219). |
| `:1166 #if !UNITY_WEBGL` / `:1177 #endif` | `LogOutstandingBundles` — the `Caching.IsVersionCached(new CachedAssetBundle(...))` probe (now `:1174`) | `cached` stays `false`, which is the truthful answer on a platform with no cache, so the capped report still lists the set honestly. |

**Stripped-symbol audit of everything this lane introduced:** `Caching` and `CachedAssetBundle` were
the only two WebGL-absent identifiers and both are now guarded (verified by grep — no unguarded
occurrence remains outside comments). `Directory` / `File` / `Path` / `FileInfo` are `System.IO` and
compile on every target; `Hash128` is `UnityEngine.CoreModule`; `IResourceLocation`,
`AssetBundleRequestOptions` and `IResourceLocator.Locate` come from the Addressables / ResourceManager
packages, which support WebGL. `TryTakeExclusively` and `FileLength` become unreferenced on WebGL —
unused private *methods* raise no warning. The new suite is Editor-only and touches none of these.

Preprocessor balance after the change: **3 `#if` / 1 `#else` / 3 `#endif`**, one pair of which
(`:568`/`:580`, `DEVELOPMENT_BUILD`) is pre-existing and untouched.

---

## 4. Brace + NUL gate (CLAUDE.md §1, WO-434)

| File | `{` | `}` | NUL |
|---|---|---|---|
| `Assets/_Modules/Core/Addressables/OfflineContentService.cs` | 273 | 273 | 0 |
| `Assets/Editor/Regression/OfflineCacheRepairRegression.cs` | 68 | 68 | 0 |

---

## 5. Unproven — recorded, not ticked

- **⚠ THE CONCURRENT-WARMER RACE IS PROBED, NOT PROVEN CLOSED. Read this one first.** The retry pass
  calls the repair with a stale bound of **0**, so any lock-only version directory is a candidate
  regardless of age. That is correct for the pull's own abandoned transactions (its handles are
  released by then) and it is what makes the retry able to repair what the first attempt abandoned —
  but it does **not** by itself cover `EnemyContentWarmer` / `StructureContentWarmer` writing the
  same bundle concurrently. Measured this session: the only callers of `DownloadAllForOffline` are
  `Assets/_Modules/Core/UI/OfflineOptInPanel.cs:237` and `:264` — a **player-initiated opt-in
  prompt**, not a boot path (`OfflineContentBootstrap.cs:33` starts only `ResolveContentSource`), so
  the window is narrow. It is not zero: a warmer can run on a scene entry while the panel is open.
  The `FileShare.None` liveness probe above is the mitigation, and it is **unverified** — whether
  Unity holds `__lock` open for the duration of a cache transaction has not been demonstrated. If it
  does not, the probe always passes and the only protection on the retry pass is that narrow window.
  Deleting a genuinely live transaction is the partial-bundle shape `AddressablesCacheHealth` exists
  to recover from, so this is the one place a device run should be watched.
- **The suite has not been run.** No Unity in this lane, so GREEN is unproven. RED is proven by symbol
  absence at HEAD; GREEN is predicted by the logic mirror above and must be confirmed by the gate.
- **The WebGL guards (§3b) are reasoned from the gate log, not re-compiled.** The two CS0103 errors
  were read verbatim off `Builds/wave1-compile2` and the fix removes the only two WebGL-absent
  identifiers from the WebGL compile path — but this lane cannot run Unity, so a **fresh
  `COMPILE_GATE_OK` is still owed** and is the only thing that proves the RED is cleared.
- **Repair behaviour on WebGL is deliberately a no-op, and that is a behaviour decision, not a stub.**
  A WebGL player has no AssetBundle cache to abandon a transaction in, so there is nothing for the
  repair to do; if that platform ever grows a cache-like path, this guard is where it must be revisited.
- **That removing the lock-only directory lets Unity commit the retry is NOT proven.** It is the
  ledger's prescription and it is consistent with the surviving previous version, but only a real
  device/editor pull against that bundle can demonstrate the commit. If it does not, the new
  `STILL OUTSTANDING` lines will now name the bundle and its hash validity on the first failure —
  which is the point.
- **`Caching.currentCacheForWriting.path` was not read at runtime.** The layout was verified against a
  quarantined copy under `LocalLow\Unity\DeNelle_Echoes of Elarion.quarantine-20260908-raid-crash`. The
  scan logs the resolved root and warns if it does not exist, so the assumption self-reports.
- **Whether `Caching.ClearCachedVersion` would also clear a lock-only directory is untested** — the
  repair deliberately uses a targeted `Directory.Delete` of the exact version directory instead, and
  verifies removal.
- **The 5-minute stale bound is a judgement, not a measurement.** It is longer than any single fetch in
  this content set (largest bundle ~19.4 MB) and shorter than a session. It has not been observed
  against a real concurrent warmer. See the race bullet at the top of this section.
- **Key-based chunking cannot perfectly partition partially-overlapping dependency closures.** A later
  key can legitimately need one new bundle plus one already claimed. What (b) removes is the
  pathological case measured in the capture — a key set that adds nothing new and re-requests a whole
  family bundle. With (a) fixed, a cross-chunk re-touch of a *committed* bundle is a cache check, not a
  download; the 3x count only existed because nothing committed.
- **The adjacent finding in the WO is untouched** — the run resolved against catalog
  `catalog_2026.08.21.334310` while the build was `2026.09.08.361113`. Not proven related (this is a
  caching failure, not a 404) and deliberately out of this lane's scope. It still wants its own look.
