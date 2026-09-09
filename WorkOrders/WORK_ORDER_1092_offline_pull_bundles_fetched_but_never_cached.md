# WORK ORDER 1092 — the offline pull fetches ~19.4 MB that never becomes a cache entry, so the player can never be stamped offline-ready

**Status:** READY TO IMPLEMENT (diagnostic first — the behavioural fix is deliberately deferred)
**Minted:** 2026-09-09 by the UI seat (UI reserved block; banner bumped 1089 → 1095 in the same edit)
**Silo:** Content / Addressables
**Severity:** P2 — offline mode never stamps; no crash, no data loss
**Source:** F8 capture seq=4707

> ⚠ **UNTRUSTED WORKING-TREE EDIT EXISTS** in `OfflineContentService.cs` — a UI-seat agent was
> stopped while inserting diagnostic helpers. Braces balanced, 0 NUL bytes, **incomplete**. See
> `WO_1089_1094_WORKING_TREE_NOTE.md`.

---

## ⛔ The verifier is CORRECT. Do not "fix" it.

The capture reads like a measurement bug and is not one.

```
OFFLINE PULL NOT VERIFIED (downloads reported success but 19398472 byte(s) are still outstanding for
the same key set - the pull did NOT pull); failedChunks=0, downloaded=77346600/38549656.
```

`total − remaining = 38,549,656 − 19,398,472 = **19,151,184**`. The bundle cache live during that run
contained exactly **five** `__data` files stamped at the minute the pull finished
(11,269,635 + 6,216,707 + 1,096,169 + 482,203 + 86,470) = **19,151,184 bytes — byte-identical**.

So `GetDownloadSizeAsync` credited exactly what physically landed on disk and nothing else.
`PullVerified` (`OfflineContentService.cs:268-295`) and `MeasureDownloadSize` (`:628-648`) are
arithmetically honest, and the assertion must not be relaxed.

**The `downloaded=77,346,600` numerator IS a double-count**, but it is display-only: `:784` sums
per-chunk `GetDownloadStatus().DownloadedBytes` across 24-key chunks (`ChunkSize`, `:195`), and a
family bundle shared by ~16 addresses is counted in every chunk that touches it. It feeds
`onProgress` (`:809`) and the log line (`:822`) and **never reaches the verdict** — `:807` passes only
`keys.Count`, `allOk`, `remaining`. Do not route it into the verdict.

Numerator and denominator are *not* over different key sets: `total` (`:722-723`) and `remaining`
(`:804-805`) are the same function over the same key list, before and after.

## The real, still-unidentified defect

~19.4 MB was fetched with `failedChunks=0` — every handle `Succeeded` — and **never became a cache
entry**. The same subset will be outstanding on every retry, so this player can never be stamped
offline-ready.

Two live hypotheses, and one log tells them apart:
- an **invalid/empty `Hash`** ⇒ those bundles can never cache at all (`ComputeSize` keys off
  `Caching.IsVersionCached(BundleName, Hash)` — package `AssetBundleProvider.cs:226-238`);
- a **valid Hash** ⇒ the cache write was lost or evicted.

Ruled out already: `m_UseAssetBundleCache: 1` and `m_UseAssetBundleCrc*: 1` on every group including
`Structure_Art`; `git log -S"m_UseAssetBundleCache: 0" -- Assets/AddressableAssetsData` returns
nothing. `AddressablesCacheHealth.RepairBeforeAddressablesStarts` (`:19-45`) did not run that session
(no `[Flow:AddressablesCache]` line), so nothing wiped the cache mid-pull.

## Fix spec — diagnostic ONLY, one file

`Assets/_Modules/Core/Addressables/OfflineContentService.cs`. After the re-measure at `:804-805` and
**before** the verdict at `:807`, when `remaining > 0`: iterate the key set, per-key size each, and
for every key still reporting > 0 log via FlowTrace the key, its resolved bundle `InternalId`, the
`AssetBundleRequestOptions` `BundleName`, `Hash`, whether `Hash128.Parse(Hash).isValid`, and
`BundleSize`.

Constraints:
- **No behaviour change** — same verdict, same return, same progress reporting.
- Wrap in `Guard.Try` so a bad locator logs and is skipped, never throws inside the pull (§12.2).
- **Cap or summarise.** This fires on a failed pull over a 682-key set; an uncapped dump floods the
  device logcat ring and evicts the surrounding evidence (memory `logcat-ring-buffer-destroys-evidence`).
  A capped list plus a total count is the right shape.

## ⛔ Do NOT implement the retry yet

A bounded second pass over the still-outstanding locations is the obvious follow-up and is
**deliberately deferred until this diagnostic reports**. If those bundles structurally cannot cache,
a retry loops or hangs, and an honest failure is better than a hang. Mint a follow-up ticket once the
log names the bundles.

## Acceptance criteria

- [ ] A failed pull prints the offending bundles with their hash validity, capped.
- [ ] `PullVerified` and `MeasureDownloadSize` are unchanged.
- [ ] No behaviour change on a successful pull.
- [ ] Brace balance; gate markers on a fresh log.

## Adjacent finding — needs an owner/CLI look, not part of this fix

That run resolved against CDN catalog **`catalog_2026.08.21.334310`** (cached locally, mtime
2026-08-21 11:14, never refreshed) while the player build was **`2026.09.08.361113`**. A three-week-old
catalog serving a September build is the shape of the §16 class. **Not proven to be the cause here** —
the outstanding-bytes failure is a caching problem, not a 404 — but it should be checked on its own.

## Unproven, recorded honestly

- **Which** bundles make up the 19,398,472. Cache dirs are hashes; `__info` carries no plaintext name;
  none of the five sizes matches any file under `ServerData/**`, so the CDN content was built outside
  this working tree. That is exactly what the diagnostic above exists to answer.
- Whether the failure is deterministic — `Player.log` contains exactly one pull, `Player-prev.log`
  none. One sample cannot separate "permanent subset" from "one bad run".
- Whether non-persistence is a cache-write race, an eviction, or a disk/quota event.
- Evidence came from the **quarantine** cache directory only; the non-quarantine copy is stamped ~40
  minutes after the pull and must not be used for any claim.
