# WO-1536: the ogre asks for model 'OgreMage', which was never imported, and silently wears a stand-in

**Status:** IMPLEMENTED - cd57a1c1e on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes
**Silo:** Village/Enemies art - `enemies.json` + the committed model registry + `EnemyResolverRegression`.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1536 -> 1537 in the same edit; **drafted as 1533 and renumbered** -
the owner-promo-bypass lane held 1533 on disk and on the banner first).

## 1. EVIDENCE

```
Assets/StreamingAssets/Data/Canonical/enemies.json:400   "modelKey": "OgreMage"
```

Device, 13:26:13.885:

```
ModelForEnemy: enemy id 'ogre' asks for model 'OgreMage', but that key is NOT in the committed
Resources/Enemies registry ... the code table's stand-in is used instead
```

So every ogre that has ever spawned wore the wrong body, and the only detector was a log line nobody was
reading. `EnemyArtCoverageRegression` is expected RED on this row tonight - note that suite is one of the
seven that WO-1496 found is not registered anywhere, which is why it never said so.

Same family as WO-1509 (orc albedo): enemy art fails silently and the game plays on.

## 2. FIX SHAPE

- Either IMPORT `OgreMage` into `Resources/Enemies`, or CORRECT the `enemies.json` row to a key that exists.
  The owner's art intent decides which; state the choice in the RESULT.
- Add a case to `EnemyResolverRegression`: **every `modelKey` in `enemies.json` is present in
  `CommittedModels`.** That is the durable half - it catches the next one before a device does.
- If the row is edited, do it from HEAD bytes with the LF count proven
  (memory `canonical-json-edits-binary-only-verify-newlines`), in both twins.

## 3. WHAT NOT TO DO
- Do not silence the `ModelForEnemy` warning. It is the only thing that caught this, and it must survive
  (CLAUDE.md sec.12 - instrumentation is permanent).
- Do not delete the stand-in fallback. A wrong body is better than a null; the defect is that it was silent,
  not that it existed.

## 4. ACCEPTANCE
- [ ] `ogre` resolves a real committed model; a device or headless capture of it opened and looked at.
- [ ] The `modelKey`-in-`CommittedModels` case exists; RED proof stated with the current row.
- [ ] Zero `ModelForEnemy: ... NOT in the committed` lines in a full town + raid session.
- [ ] `REGRESSION_OK n/n` on a fresh log.
