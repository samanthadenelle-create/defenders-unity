# WORK ORDER 1348 - RESULT

**Lane:** VFX-TUNABLES SME (worktree `.claude/worktrees/agent-a75fecf8f4da804d5`, branch `dev`)
**Date:** 2026-09-10
**Status:** IMPLEMENTED - handed to the lead. **NOT gated, NOT committed, NOT pushed** (per the brief).
**Owner still CLOSES** after felt-verifying on her phone (acceptance lines 1 and last).

---

## 1. What was built, in one breath

Her ask, verbatim: *"is it possible to tag those from the command center? and then change pointer on
next town load?"* / *"realm.vfx(set)"*. **Yes, and her namespace is the key shape, adopted verbatim.**

A `realm.vfx.<VFX key>` override layer that rides the EXISTING remote-tunables rail:

- The value is a **stable, append-only OPTION ID** into a pool generated from **her own tag file**.
- **Option N for key K means "render K with the prefab key <N> already renders with"** - so every
  offer is **shipped by construction** and the picker cannot name art that was never built.
- **0 = the build-time pick.** `Assets/Editor/VfxManualPicks.json` remains the default and the record;
  nothing deletes, bypasses or writes to it.
- Resolved **once per scene load**, at the ONE seam every VFX key already passes through.
- Instrumented: key, id, **source**, and on a decline the **reason**.

⛔ **The CLI made no creative pick.** The four registered keys are the four the WO names; the option
pool is her existing tags, unchanged. Nothing was chosen for her.

---

## 1b. ⛔ THE FINDING THAT MATTERS MOST: THREE OF THE FOUR KEYS ARE NOT WIRED

**Only ONE of the four registered picks changes anything on screen today.** This was grepped at
source on 2026-09-10, not inferred from the ticket, and it is now stated in **four places** (the
registry comment, each `TunableSpec`, the Command Center card, and `docs/PROD022_TUNABLE_FLAGS.md`)
so she cannot discover it by picking something and seeing nothing:

| Key | Wired? | Evidence |
|---|---|---|
| `realm.vfx.BossDeath_Impact` | ⭐ **YES - LIVE** | `EliteVFXController.cs:326` plays it via `HeldVfxKeys.BossDeath`, gated on `isBoss`. (DragonBoss/Syndrath has its own `Die()` and is NOT covered - stated on the card.) |
| `realm.vfx.atfootprintoftree_Aura` | **CALLER EXISTS, SITE WITHHELD** | `HeartAuraController.cs:323` calls it via `HeldVfxKeys.TreeOfLifeFootAura` (`HeldVfxHook.cs:75`), but `AmbientAuraPolicy.WithholdTreeFootAura = true` (`AmbientAuraPolicy.cs:129`, her ruling 2026-09-07 / WO-1476 - the aura drifted up the Y axis over the town). **Nothing spawns whatever the row says.** The pick stands for the day that flag flips. |
| `realm.vfx.atfootprintoftree_Impact` | **NO** | Zero call sites in `Assets/_Modules`. Also absent from the tag file - this is the CREATION case. |
| `realm.vfx.EliteDeath_Impact` | **NO** | Zero call sites. An elite death plays the BOSS key, which is **her own ruling**: *"both get Elite_Death, name it BossDeath_Impact"*. |

⛔ **The three were NOT "fixed" by re-pointing them at live keys.** The WO names these four; VFX keys
map owner tags to hooks VERBATIM; the CLI never makes a creative pick. The honest fix is the one
taken - **each card says in words that its slot is not wired yet**, which is precisely acceptance
criterion 3 (*"the UI never implies a pick applied when it did not"*).

**For the owner / the lead:** if she wants elite deaths to differ from boss deaths, or the tree-foot
glow back, those are two small follow-up tickets (one call site; one flag flip), and both slots are
now already tunable when they land.

⚠ **I nearly shipped the opposite.** The first draft of these cards read *"Which effect plays when an
elite enemy dies"* - a sentence that would have had her pick, look, see nothing and conclude the whole
feature was broken. It was caught on review, not by me, and it is recorded here because it is the same
inference-instead-of-grep failure CLAUDE.md section 11B exists to stop.

---

## 1c. CHAIN 48 (`Builds/wave10g-reg1`, 499/504) - BOTH REDS FIXED

### RED 1 - `REGRESSION MARKER FAIL (2)`: hollow-pass guards

`HollowPassScanner` (`Assets/Editor/Regression/HollowPassScanner.cs`) flagged
`VfxPickOverrideRegression.cs:340 [A-missing-dependency] guard 'loopOpt == null'` - a verdict-method
early return that asserted nothing. Per the brief I swept the WHOLE file rather than the two it named.
**Six** silent returns are now assertions, resolved under the three-way rule:

| Line (now) | Guard | Arm taken | Why |
|---|---|---|---|
| `:297` | `!TryGetRawRow(catalog, FixtureKey)` | **FAIL** `MissingFixture("unknown-id")` | fixture-absent - the key IS in her tag file, so its absence is a stale catalog or an un-imported pack |
| `:320` | same, in `Case5_PickApplies` | **FAIL** `MissingFixture("pick-applies")` | fixture-absent |
| `:400` | same, in `Case7_LoopMismatchRefused` | **FAIL** `MissingFixture("loop-mismatch")` | fixture-absent |
| `:411` | `baseRow.IsLoop` | **ASSERT THE ABSENCE** | a death burst being a ONESHOT is canon, not incidental - `NightStoreAuraSelectionRegression` records her tag as `isLoop=false` with *"a death burst must not hold a loop slot - that is correct, not a bug to fix"* |
| `:425` | `loopOpt == null` (the flagged one) | **ASSERT THE ABSENCE** | she has tagged many looping `*_Aura` keys; a pool with NOT ONE resolvable loop means the pool or the generator is broken |
| `:519` | `rows == null` in `AssertBuildTimePicks` | **FAIL** | a catalog that loaded with a null `Rows` is a broken asset, not a pass |

A seventh was a `Debug.Log` + `return` in `Case6_KeyCreation` (`:369`): it now **asserts the absence** -
`atfootprintoftree_Impact` having no build-time row is the STATE UNDER TEST, and
`NightStoreAuraSelectionRegression` pins that row as deleted, so its return is a defect recurring, not
a precondition to stand down on.

**No `RegressionOutcome.PartialSkip` and no `hollow-pass-ok` opt-out was used anywhere** - every arm
had a real assertion available, and a skip token would have been the easy answer rather than the right
one. Audited afterwards: **every `return;` in the file is now immediately preceded by a
`failures.Add(...)`** (`awk` over lines 140+; 15 sites, 15 assertions).

### RED 2 - `[options-shipped] option id 58 'KnightShieldBuff_Aura'`

**My own oracle caught a real defect, and this is the part worth reading.** The pool was derived from
her tag file ALONE. But the tag file is her INTENT and the catalog is what the build can RENDER, and
they are **not the same set**: `HovlVfxCatalogGenerator` SKIPS a manual row whose prefab does not load,
which is exactly what happens when the prefab sits in a **gitignored art pack** that was not imported
on the machine that baked the catalog.

Measured 2026-09-10: the catalog holds **169 resolvable rows**; of her **152** tagged keys, exactly
**one** - `KnightShieldBuff_Aura` (option id 58) - has no row at all. Offering it would have let her
pick an effect that renders **nothing, with no error on screen** - CLAUDE.md §16 in a new place.

**The fix: the pool is now tag file INTERSECT catalog.**
- `tools/gen-vfx-pick-options.mjs:153` `parseCatalogKeys()` reads `HovlVfxCatalog.asset` directly (a
  `- Key:` line plus its `Prefab: {fileID, guid}`; resolvable only when fileID is non-zero AND a guid
  is present, because a null prefab serialises as `{fileID: 0}`). It **throws** rather than yielding an
  empty set, so a format drift can never silently empty the picker.
- `:219` marks the entry `unresolved: true` instead of dropping it.
- ⛔ **The pick is NEVER deleted - not from the pool and never from her JSON.** Her tag is her record.
  The entry keeps its **reserved id**, so the day the pack is imported and the catalog rebaked, the
  **SAME id 58** becomes offerable again rather than a different key inheriting it.
- Held back by all three consumers: `api/_lib/tunable-manifest.js:91` (not offered on the page),
  `VfxPickOverrides.cs:410` (a row naming it falls back and traces *why*, naming the pack fix), and
  `VfxPickOverrideRegression.cs:255` / `:574`.
- The generator now prints it: `VFX_PICK_OPTIONS_GEN_OK options=152 offered=151 unresolved=[58:KnightShieldBuff_Aura]`

**Two new oracle cases pin the rule in both directions** (`test/vfx-pick-options.test.js`): an offered
option must be catalog-resolvable, AND an option marked unresolved that the catalog CAN resolve is
also a failure - otherwise a drifted parse would silently withhold picks she is entitled to.

**RED-first proof of the new rule, then restored:** clearing `unresolved` on id 58 by hand produced
`✖ an option is OFFERED only when this build's catalog can resolve its key` (plus the two byte-identity
cases); `node tools/gen-vfx-pick-options.mjs` restored it and all 12 pass.

---

## 2. The design decision, stated because the brief asked for it

**Int + stable id, NOT a new `TunableKind.String`.**

`DeNelle.Core.Ops.TunableKind` is `Bool | Int` and nothing else
(`Assets/_Modules/Core/Ops/RemoteTunables.cs:78-84`, read at source this session). A String kind would
have meant changing `TryParseValue`, `Describe`, the generator's regex parse in
`tools/gen-tunable-manifest.mjs`, `normalizeValue` in `api/_lib/tunables.js`, the manifest's kind and
range logic, and the console control - six edits to a rail three other lanes were authoring against in
the same wave. The Int route needed **zero** rail changes and matches the WO's own "six sources per
new key" framing.

⚠ **The id is APPEND-ONLY and that is the load-bearing property.** A sorted position would shift the
day anybody tags a new key and would silently re-point a row she set last week **while the trace still
said "override applied"** - a lying trace, which the WO's instrumentation section forbids by name. The
generator reads its own previous output, keeps every existing key->id pair, and appends new keys at
`max+1`; a key that leaves the tag file keeps its id **reserved** (`retired: true`) rather than freeing
it. Three test cases pin exactly this (section 5).

---

## 3. Files changed

### New
| Path | What |
|---|---|
| `tools/gen-vfx-pick-options.mjs` | The option-pool generator. Markers `VFX_PICK_OPTIONS_GEN_OK` / `VFX_PICK_OPTIONS_DRIFT` / `VFX_PICK_OPTIONS_GEN_FAIL`. `--check` for drift. |
| `api/_lib/vfx-pick-options.generated.json` | The pool the Command Center reads. 152 entries, **151 offered**, 1 held back as unresolved. 34200 bytes. |
| `Assets/Resources/VFX/vfx-pick-options.generated.json` | The pool the BUILD reads. **Byte-identical** (sha256 `14d420be9abaf690...`, both 34200 bytes, LF=1223, CR=0, no NUL). |
| `Assets/_Modules/Core/VFX/VfxPickOverrides.cs` | The runtime resolver: option load, scene-load snapshot, resolve, FlowTrace. `DeNelle.Core.Vfx`. |
| `Assets/Editor/Regression/VfxPickOverrideRegression.cs` | New editor suite `[vfx-pick-override]`, 9 cases, zero hollow-pass guards. **Needs one registration line - section 7.** |
| `test/vfx-pick-options.test.js` | New node oracle, 10 cases. |

### Modified
| Path | What |
|---|---|
| `Assets/_Modules/Core/Ops/RemoteTunables.cs` | 4 key consts + `VfxPickBuildDefaultId` + 4 `TunableSpec` rows (Int, default 0), with the reasoning block above the consts. |
| `Assets/_Modules/Village/Vfx/HovlVfxCatalog.cs` | `TryGet` consults the override layer. **The single seam.** |
| `Assets/_Modules/Village/Vfx/VFXManager.cs` | Subscribes/unsubscribes `VfxPickOverrides.SnapshotChanged` in `Awake`/`OnDestroy`. |
| `Assets/_Modules/Village/Vfx/VFXManager.Hovl.cs` | `OnVfxPickSnapshotChanged` - flushes IDLE pooled bodies for re-picked keys only. |
| `api/_lib/tunables.js` | 4 allowlist rows + the reasoning comment. |
| `api/_lib/tunable-manifest.js` | Requires the pool, computes the safe range from it, 4 PRESENTATION rows via one `vfxPick()` helper, passes `options`/`optionsNote` through `build()`. |
| `api/_lib/tunable-manifest.generated.json` | Regenerated: 56 knobs (was 52). |
| `api/admin/console.js` | Named `<select>` picker for knobs carrying `options`, its save handler, and CSS at `min-height: var(--bigtap)`. |
| `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` | `ExpectedKnobCount` 52 -> **56** and 4 rows in `ExpectedDefaults` (all `0`). **REQUIRED, not optional** - `:452` compares `Registry.Length` against a LITERAL, so without this the `[tunable-defaults]` suite reds at the first gate. The literal is deliberate ("an oracle that measures the thing against itself certifies nothing") and its own comment instructs each lane to bump it. |
| `docs/PROD022_TUNABLE_FLAGS.md` | Rows 53-56 (each stating **WIRED? yes/no** with the file:line) + a `realm.vfx.*` family section. |
| `.gitattributes` | 2 `text eol=lf` pins - **see section 8, this is a disclosure.** |
| `WorkOrders/WORK_ORDER_1348_...md` | Status flipped, original line kept verbatim as history. |

---

## 4. The seam - proven, not assumed

`HovlVfxCatalog.TryGet` is the ONLY runtime path from a VFX key to a prefab. Measured this session:

```
grep -rn '\.TryGet(' Assets/_Modules --include=*.cs   (filtered to the Hovl catalog)
  Assets/_Modules/Village/Vfx/AoeCastReticle.cs:169
  Assets/_Modules/Village/Vfx/VFXManager.Hovl.cs:359   (CanPlayKeyInternal)
  Assets/_Modules/Village/Vfx/VFXManager.Hovl.cs:394   (PlayKeyInternal)
  Assets/_Modules/Village/Vfx/VFXManager.Hovl.cs:948
```

The only other reads of the catalog are `_hovlCatalog.Rows` at `VFXManager.Hovl.cs:174` (a log line),
`:202` and `:221` (the EAGER warm, which is OFF by default - WO-1113 demand-warm ships). **So all four
resolution call sites, and therefore all 152 keys and every consumer downstream of them, resolve
through the new layer with zero call-site edits.**

⚠ `VFXManager` is `DontDestroyOnLoad` (`VFXManager.cs:93`) and its Hovl pools are keyed by VFX key, so
an idle body built from the OLD prefab would outlive a town load. `OnVfxPickSnapshotChanged` destroys
**idle queue entries for the changed keys only**. That is a pool flush, **not** the live hot-swap the
WO forbids - nothing playing is touched, re-parented or stopped.

---

## 5. Evidence - what was measured

### 5a. Node tests (quoted verbatim)

```
node --test test/vfx-pick-options.test.js test/tunables-manifest.test.js test/command-center.test.js
  tests 89   pass 89   fail 0
```

```
node --test "test/*.test.js"
  tests 489   pass 486   fail 3
```

**The 3 failures are PRE-EXISTING and not mine, and that is measured, not asserted:** with my whole
change stashed (`git stash push -u`, clean tree at HEAD `c96030b5c`) the same three files reported
`tests 58 pass 55 fail 3`. They are:
- `test/admin.skus.view.test.js` - "the generated copy is LF..." - `api/_lib/sku-catalog.generated.json`
  is **CRLF in the working tree** while git reports it unmodified. A checkout artifact of
  `core.autocrlf=true`, not content. **Section 8 has the one-line fix; I did not apply it** (SKU lane).
- `test/benefactors.test.js` - "the migration that provisions it is additive only" - reports
  `DROP / DELETE / TRUNCATE` in `api/schema.sql`. **Explicitly out of my scope this wave.**
- `test/webgl-offline-content-surface.test.js` - Settings offline-download entry on WebGL.

### 5b. RED-FIRST, both directions of the join, then restored

Run against `api/_lib/tunable-manifest.js` in memory (baseline `defects: 0`):

```
MUTATION A  add PRESENTATION['realm.vfx.NotARealKey_Impact'] with no registry entry
  RED: CONSOLE MANIFEST vs BUILD REGISTRY: PRESENTATION in api/_lib/tunable-manifest.js has
       "realm.vfx.NotARealKey_Impact" but RemoteTunables.Registry does not - the page would
       show a lever that moves nothing.
MUTATION B  delete PRESENTATION['realm.vfx.BossDeath_Impact']
  RED: BUILD REGISTRY vs CONSOLE MANIFEST: RemoteTunables.Registry has
       "realm.vfx.BossDeath_Impact" but PRESENTATION in api/_lib/tunable-manifest.js does not -
       the knob would be INVISIBLE in the Command Center.
RESTORED defects: 0
```

A third RED came for free during the work: before `docs/PROD022_TUNABLE_FLAGS.md` was updated,
`test/tunables-manifest.test.js` failed with *"BUILD REGISTRY vs docs/PROD022_TUNABLE_FLAGS.md: the doc
does not mention realm.vfx.atfootprintoftree_Aura"*. Green after the doc rows were added.

### 5c. The join, all sources green

```
node -e "const m=require('./api/_lib/tunable-manifest'); console.log(m.mismatches().length)"  ->  0
api/_lib/tunable-manifest.generated.json  ->  56 knobs, the four realm.vfx.* rows all kind=int default=0
BossDeath card: { key:'realm.vfx.BossDeath_Impact', min:0, max:152, opts:152 }
```

### 5d. Byte / newline proof (the brief asked for the newline count)

```
api/_lib/vfx-pick-options.generated.json             bytes=34200 LF=1223 CR=0 NUL=False
Assets/Resources/VFX/vfx-pick-options.generated.json bytes=34200 LF=1223 CR=0 NUL=False
identical= True   sha256(prefix)= 14d420be9abaf690
api/_lib/tunable-manifest.generated.json             bytes=7282  LF=286  CR=0 NUL=False
```

Both generated copies were produced by `node tools/gen-vfx-pick-options.mjs` /
`node tools/gen-tunable-manifest.mjs` - **never hand-edited**, which is the procedure the generator
headers specify.

### 5e. Brace + NUL gate

```
python tools/gate_brace.py <the 6 .cs files>
  GATE_BRACE_SUMMARY bad=0 of 6
```
NUL scan over all 11 touched source files: **0 NUL bytes in every one.**
JS parse check: `api/_lib/tunables.js`, `api/_lib/tunable-manifest.js`, `api/admin/console.js`,
`test/vfx-pick-options.test.js` all parse. (`tools/gen-vfx-pick-options.mjs` is ESM and correctly
refuses a CommonJS parse - it runs fine as shown above.)

### 5f. Measured facts about the data

- `Assets/Editor/VfxManualPicks.json`: **152 rows, 120 distinct prefabs.**
- `atfootprintoftree_Impact` is **NOT in the tag file** - confirmed by parsing it. That is the
  **key-CREATION** case and it is why the WO says a design that can only override an existing key
  loses half its motivation.
- `BossDeath_Impact` (id 15) and `EliteDeath_Impact` (id 28) both currently point at `Elite_Death` -
  which is exactly the pair she wanted to be able to pull apart from a phone.

---

## 6. Acceptance criteria, line by line

| Criterion | State |
|---|---|
| Set a VFX pick from the Command Center and see it on the next town load | Built. ⛔ **The "see it" half is UNPROVEN (section 9) AND only reachable on ONE of the four keys today (section 1b).** |
| No row / no network / corrupt payload => byte-for-byte the build-time pick. **Proven.** | Proven **server-side by construction** (default 0) and pinned by `Case2_NoRowInvariant` over the WHOLE catalog across 5 failure shapes. ⛔ That case has **not been executed** - no Unity run (section 9). |
| Unshipped prefab falls back and traces the reason; UI never implies it applied | Built (3 decline paths, each with a `FlowTrace.Warn` naming the reason). Pinned by `Case4_UnknownIdFallsBack`; the page prints *"NOT IN THIS BUILD, so the game is using the shipped effect"* in words. |
| She can CREATE a pick for a key with no build-time entry | Built and pinned (`Case6_KeyCreation`, `realm.vfx.atfootprintoftree_Impact`). |
| All six tunable sources enumerated; key shape `realm.vfx.<key>` | Enumerated **by token, not memory** - `grep -rn nightStoreAuraMode` over Assets/api/tools/docs/test named exactly 5 sources + the consumer. All updated (section 3). Key shape is hers, verbatim. |
| `VfxManualPicks.json` remains the default and the record | **Not touched** - `git status` shows it unmodified. |
| Every current consumer resolves through the new layer, enumerated from the tree | Done - section 4, 4 call sites. |
| An oracle pins the fallback invariant and that the picker cannot offer an unresolvable prefab. **Prove it RED first.** | Node oracle: 10/10, RED proven both directions (5b). Editor oracle: 9 cases written. ⛔ **Its RED run is UNPROVEN** (section 9). |
| Brace + NUL per `.cs`; PowerShell parses under 5.1 | `bad=0 of 6`, 0 NULs. **No PowerShell was written**, so nothing to parse. |
| Touch targets >= 112px, state in WORDS never hue | `min-height: var(--bigtap)` on the select (the same token the existing number field and buttons use); every state is a sentence - "The effect the game ships with", "NOT IN THIS BUILD, so the game is using the shipped effect", "continuous"/"one-shot". No colour carries meaning. |
| ASCII-only in player-facing strings | `test/command-center.test.js` "the manifest is 7-bit ASCII" passes (it is in the 89/89). |
| Do not touch prices/SKUs/entitlements/`purchase-catalog.js` | Not touched. |
| Do not run Unity / commit / build | Not done. |
| Owner uses it on her phone and CLOSES | **Hers.** |

---

## 7. ⛔ ONE REGISTRATION LINE FOR THE LEAD

`Assets/Editor/Regression/DataRegression.cs` was **not edited** (per the brief). Add this beside the
`night-store-aura` line (currently `DataRegression.cs:1683`):

```csharp
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "vfx-pick-override suite", () => { if (!DeNelle.Editor.Regression.VfxPickOverrideRegression.Run(out var rVpo)) failures.Add(rVpo); else log.AppendLine("[vfx-pick-override] " + rVpo); });
```

The suite also has a standalone batch entry, `VfxPickOverrideRegression.RunAll`, marker
`VFX_PICK_OVERRIDE_OK` / `VFX_PICK_OVERRIDE_FAIL`. **Adding the line raises the suite count in
`REGRESSION_OK <n>/<n>`** - judge by the marker on a fresh log, never a number from this file.

Also for the lead: the commit must carry the three `.meta` files Unity generates on the gate run
(section 9 item 6), and `Assets/Resources/VFX/vfx-pick-options.generated.json` is a **new file under
Resources** - small (28 KB of text) but it does enter every build, which is the price of the build
being able to read the same pool the console offers.

---

## 8. DISCLOSURE - I edited `.gitattributes`, a shared file

Two `text eol=lf` pins were added (8 lines including comments):

```
api/_lib/vfx-pick-options.generated.json text eol=lf
api/_lib/tunable-manifest.generated.json text eol=lf
```

**This is not defensive; the failure was OBSERVED.** `core.autocrlf=true` on this machine and
`.gitattributes` had no `*.json` rule, so a `git stash pop` mid-session re-checked both generated files
out as **CRLF** and turned three green tests red on content that had not changed at all:

```
AssertionError: TAG FILE vs GENERATED POOL: ... has moved and ... has not
  + '{\r\n' + ...
AssertionError: api/_lib/vfx-pick-options.generated.json contains a CR.
```

Regenerating restored 89/89. The pins stop it recurring on a fresh clone.

**The same defect is live on a file I did NOT fix:** `api/_lib/sku-catalog.generated.json` is CRLF in
this worktree right now (`test/admin.skus.view.test.js` red at HEAD, section 5a). The one-line fix is
`api/_lib/sku-catalog.generated.json text eol=lf`. **I left it for the SKU lane / the lead** rather
than reach into another lane's oracle.

---

## 9. ⛔ WHAT IS UNPROVEN, NAMED AS UNPROVEN

1. **"She sees it on the next town load" is a DEVICE claim I cannot make from here.** No Unity was
   run, no build was made, no phone was touched. What would prove it: set
   `realm.vfx.EliteDeath_Impact` to a live option id in the Command Center, launch the build, kill an
   elite, and capture `[Flow:VfxPicks]` - the applied line prints the key, the option id, the prefab
   name and `source=remote|remote-cached|local-playerprefs`. **If that line is absent the override
   never stood; if it is present and the art still looks the same, the pick applied and the effect is
   subtle.** That distinction is the whole reason the source field exists.
2. **`VfxPickOverrideRegression` has never been executed** - not green, not red. It compiles only in
   my reading of the tree; the lead's gate is the first real run. Its `Case2` is the byte-for-byte
   invariant proof the WO demands, so **that acceptance line is DESIGNED-for, not DEMONSTRATED.**
3. **No `COMPILE_GATE_OK`.** The 6 `.cs` files pass `tools/gate_brace.py` (`bad=0`) and carry no NULs,
   but brace balance is not compilation. Two things I would look at first if it reds:
   `Newtonsoft.Json.Linq` in the editor suite (`DeNelle.EditorRegression.asmdef` has
   `overrideReferences: false`, and five sibling suites already use Newtonsoft, so I expect it to
   resolve - **expect, not proven**), and `Assets/Resources/VFX/vfx-pick-options.generated.json`
   having no `.meta` yet (Unity generates it on import).
4. **The `.json`-under-`Resources` TextAsset load is unproven at runtime.** `VfxAssetLoader
   .LoadVfxAsset<TextAsset>("VFX/vfx-pick-options.generated")` follows Unity's documented rule (final
   extension stripped), but I have not seen it return non-null. If it returns null the layer logs a
   `Warn` and **every key falls back to the build-time pick** - i.e. the failure is safe and loud.
5. **The loop/one-shot refusal is a judgement call I made, not a ruling.** An override borrowing a
   LOOP prefab for a ONESHOT slot is refused (the call site would never return it to the pool - the
   owner has already flagged "random vfx stuck around" once). If she wants a loop in a one-shot slot,
   that is an owner ruling and the refusal branch is where it changes.
6. **Three `.meta` files do not exist yet** - `Assets/_Modules/Core/VFX/VfxPickOverrides.cs.meta`,
   `Assets/Editor/Regression/VfxPickOverrideRegression.cs.meta` and
   `Assets/Resources/VFX/vfx-pick-options.generated.json.meta`. Unity writes them on the gate run's
   import. **The lead's explicit-path commit must include them** or the three assets ship orphaned.
7. **Two review catches were fixed, and one of them was a real shipped-defect-in-waiting**
   (section 1b). The second: `AssertBuildTimePicks` originally compared `TryGet` against `Rows[i]`,
   which would have RED-ed on a non-defect - the catalog is a MERGE of a curated map with the manual
   overlay, and `BuildLookup` resolves a duplicated key LAST-WINS. It now compares against the
   last-wins row. Neither was caught by me, and both are recorded rather than quietly repaired.
8. **The per-play decline lines are `FlowTrace.Throttle(..., 30f, ...)`, not `Warn`** - matching
   `RemoteTunables.Int`'s bad-row precedent. `Resolve` runs on every play, and an unthrottled Warn on
   a hot path evicts the boot window out of the device logcat ring (memory
   `logcat-ring-buffer-destroys-evidence`) - trading one silent failure for another. Pinned by
   `Case9`.
9. **`docs/MASTER_CATALOG.md` was not updated.** The tunables canon
   (`docs/PROD022_TUNABLE_FLAGS.md`) was, in the same change, per CLAUDE.md section 15. Flagging the
   catalog gap rather than silently leaving it.

---

## 10. Hand-back

- **WO:** `WorkOrders/WORK_ORDER_1348_vfx_picks_tunable_from_command_center.md` - Status flipped to
  IMPLEMENTED, original line kept verbatim as history.
- **This file:** `WorkOrders/WORK_ORDER_1348_vfx_picks_tunable_from_command_center.RESULT.md`
- **Lead's next steps:** add the section 7 registration line, batch-gate the combined tree
  (`COMPILE_GATE_OK` + `REGRESSION_OK` on a FRESH log, marker not exit code), commit by explicit path,
  regenerate `BOARD.html`.
- **Then the owner**, on her phone, on a build - which is the only thing that can close this.
