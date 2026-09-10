# WO-1486: ServerData/Android is never pruned (597 MB, 168 catalogs back to 2026-08-18) and the build marker reports the wrong size

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `tools/r2-ship.ps1` (pruning) + the Android build marker. Tooling only.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1486 -> 1487 in the same edit).

## 1. EVIDENCE

```
ServerData/Android   466 files, 597 MB, 168 catalogs dating back to 2026-08-18
```

Nothing ever removes a generation. Bundle names are content-hashed (CLAUDE.md sec.16), so every content build
adds a full set and none are retired - the directory only grows, and every push re-walks all of it.

The marker also lies about the size:

```
Builds/apk-build.log:35160   [AndroidBuild] SUCCEEDED - 2367 MB
```

against 463 MiB actually on disk. It is reporting the uncompressed figure, so nobody reading the marker can
tell whether the build grew.

## 2. FIX SHAPE

- Prune `ServerData/<target>` to the CURRENT generation (plus one previous, for rollback) inside
  `tools/r2-ship.ps1` - the one sanctioned file. Log what was removed.
- Make the build marker report ON-DISK bytes of the produced artifact, so the number in the log is the number
  that ships.

## 3. WHAT NOT TO DO
- Do not re-inline pruning, push, or verify into `overnight-apk-build.ps1` or `morning-ship-chain.ps1`.
  Sec.16 is explicit: call the one file.
- Do not prune before a successful verify; a prune that runs on a failed push destroys the rollback set.

## 4. ACCEPTANCE
- [ ] Prune runs inside `r2-ship.ps1` AFTER verify; before/after file counts and sizes pasted.
- [ ] The current generation still resolves on device (a launch capture opened).
- [ ] The build marker's MB matches `Get-Item` on the artifact.
