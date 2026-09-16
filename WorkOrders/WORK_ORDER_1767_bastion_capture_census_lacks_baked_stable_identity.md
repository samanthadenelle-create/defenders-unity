# WORK ORDER 1767 — Iron Bastion capture census dies on a missing baked stable identity; WO-1732's regen stripped all 221 and desynced the owned-town pair

**Status:** IMPLEMENTED, NOT YET GATED
**Silo:** Raid / owned-town capture (WO-1705 lane). Editor bake chain + one new regression + (pinned) scene re-bake.
**Opened:** 2026-09-16 · read-only RCA lane · branch `dev`
**Stage:** RCA COMPLETE → CLI implements. One owner ruling is pinned in §6b; everything else is unblocked.

---

## 1. The proving line

Owner's Seeker, build `2026.09.16.371701`,
`D:\EoA\logs\device\pull-20260916-143101-bastion-owner-run\logcat_full.txt`:

```
line 3037262: 09-16 13:24:27.476  1294  1359 W Unity   : [BREAK] error: [Flow:Raid] Precombat capture census failed: Captured structure lacks a baked stable identity: Wall_Outer_SS_0
line 3037263: 09-16 13:24:27.476  1294  1359 E Unity   : [Flow:Raid] Precombat capture census failed: Captured structure lacks a baked stable identity: Wall_Outer_SS_0
line 3037986: 09-16 13:24:28.101  1294  1359 I Unity   : [Flow:Raid] RAID START config='iron_bastion' garrisonAlive=23 enemyLevel=7 difficultyx1.30 spire=3500hp scene='RaidBase_IronBastion'.
```

The throw site is `Assets/_Modules/Village/World/Camps/RaidCaptureCensus.cs:38-39`:

```csharp
var pose = OwnedTownScenePose.Capture(node);
if (string.IsNullOrEmpty(pose.templateStructureId))
    throw new InvalidOperationException("Captured structure lacks a baked stable identity: " + node.name);
```

It fires on the FIRST structure enumerated (`Wall_Outer_SS_0`), 625 ms before `RAID START` — so the
census is empty for the whole fight. `RaidVictoryController.Start` (`:124-128`) catches it and
`_captureCensus` stays `null`.

---

## 2. What "baked stable identity" is

| Thing | Where | Read 2026-09-16 |
|---|---|---|
| The component | `Assets/_Modules/Village/World/Camps/OwnedTemplateIdentity.cs` | `[DisallowMultipleComponent] sealed MonoBehaviour`, one `[SerializeField] private string _templateId`, script GUID `a1e512946c94bcd40ae870198aa00b4e` |
| Where it is read | `OwnedTownScenePose.Capture` (`:39,:46`) — `templateStructureId = identity != null ? identity.TemplateId : null`, and `parentFrame` is saved **only when the identity exists** (`:47`) | — |
| Why it exists | `OwnedTownScenePose.TryResolve:62-77` — the id branch finds the structure by id and verifies the parent frame. The `:79-92` fallback branch walks a **sibling-index path** and compares `node.name`. The id was invented to replace that fragile path | — |
| The only step that writes it | `Assets/Editor/OwnedTemplateIdentityBake.cs:14-71` (**UNTRACKED**, `??`, 09-11 11:07). `Run()` adds the component to every `WallSegment`/`DefenseTower`/`RaidSpire` in `RaidBase_IronBastion.unity`, assigns a `Guid.NewGuid().ToString("N")` (`:26`) via `SerializedObject` (`:65-70`), saves that scene, then maps the same ids onto `OwnedTown_IronBastion.unity` by legacy pose resolution (`:33-45`) and saves it | — |
| Its own proofs | `:50-60` — the id must survive a sibling reorder, duplicate ids must be refused, a changed coordinate frame must be refused. Marker `OWNED_TEMPLATE_IDS_OK structures=221` (`:61`) | — |

**No other editor step, regression or builder writes `_templateId`.** `grep -rn "OwnedTemplateIdentity" Assets/Editor --include=*.cs`
returns only the bake and `OwnedTownScenePoseProof.cs:104` (which *deletes* them in a proof fixture).
No regression asserts the stamp exists — that is the permission-gate hole (`docs/ARCHITECTURE_PRINCIPLES.md`
§2c, line 151: *"Unit tests are the PERMISSION GATE for holistic change"*).

---

## 3. Was the step ever run on `RaidBase_IronBastion.unity`? YES — and then it was wiped.

Counted from the scene YAML with the component GUID (`grep -c`), 2026-09-16:

| Artifact | `a1e512946c94bcd40ae870198aa00b4e` refs | `_templateId` occurrences |
|---|---|---|
| `Assets/Scenes/RaidBase_IronBastion.unity` (worktree **and** HEAD) | **0** | **0** |
| `Assets/Scenes/OwnedTown_IronBastion.unity` (worktree **and** HEAD) | **221** | **221** |
| `Assets/Resources/OwnedTown/IronBastionTemplate.json` | — | **221** `templateStructureId` keys, `templateVersion "iron-bastion-20260911"`, and it contains `"sourceName": "Wall_Outer_SS_0"` |

Both scenes are **clean in the working tree** (`git status --short` on both: no output), so HEAD == what
the 371701 build read.

### The wipe, from the scene's own history

```
git log --oneline -- Assets/Scenes/RaidBase_IronBastion.unity   (identity refs per commit, measured)
  3b4b98834  WO-1749 spire unburied            ->   0
  452fc14fd  WO-1732 top tier regenerated      ->   0     <-- THE STRIP
  8b88a5053  WO-1723 navmesh/wall root cause   -> 221
  f06a73600  WO-1703/1704                      -> 221
git log --all -S"a1e512946c94bcd40ae870198aa00b4e" -- <both scenes>
  452fc14fd, f06a73600, dfe0c7403, ce5619a8a, a9940b1a0
```

**`452fc14fd` (WO-1732, 09-15 10:12) is the dead step.** It added `iron_bastion` to the generated set
(`RaidBaseGenerator.RaidConfigIdsFromCatalog`, `:466-478`) so `BuildAllRaidScenes` (`:504-520`)
**regenerated `RaidBase_IronBastion.unity` for the first time** — and the generator writes a fresh scene,
so all 221 `OwnedTemplateIdentity` components went with the old one. Its own file list touched
`RaidBase_IronBastion.unity` but **not** `OwnedTown_IronBastion.unity`.

`3b4b98834` (WO-1749) touched the raid scene again after the strip and kept 0. **Nothing since has
re-stamped.** The 22:00 09-15 bake is not implicated: the count was already 0 at `452fc14fd`.

### The second, larger half of the damage — the pair is now desynced

Structure counts by component GUID, plus object names, 2026-09-16:

| | `WallSegment` | `DefenseTower` | `RaidSpire` | census total | `m_Name: Wall_` | `m_Name: Ruin_Wall_` |
|---|---|---|---|---|---|---|
| `RaidBase_IronBastion.unity` (now) | 158 | 10 | 1 | **169** | 158 | 158 |
| `RaidBase_IronBastion.unity` @ `8b88a5053` | 210 | — | — | — | — | — |
| `OwnedTown_IronBastion.unity` | 210 | 10 | 1 | **221** | 210 | 0 |
| `IronBastionTemplate.json` | — | — | — | **221** entries | — | — |

The 158 ruin holders carry no `WallSegment` (guid count 158 == live `Wall_` count), so the census
enumerates live walls only — 169 is the real figure. WO-1732 moved the top tier onto the WO-1723 4.0 m
partition (210 walls → 158 + 158 ruins). `OwnedTown_IronBastion.unity` was last committed at
`8b88a5053` and still carries the **pre-WO-1723 210-wall partition**.

So the raid template, the owned-town template and the shipped manifest now describe **three different
buildings**. Re-stamping alone does not fix capture:
`OwnedTownLayoutSnapshot.TryValidateAndCopy:43-44` refuses any snapshot with
`structures.Count < _manifest.entries.Count` (221) and, outside construction mode, `!=` it — a
169-structure capture is refused by the town's own layout authority even after it saves.

⚠ Both bake files hardcode the census size — `OwnedTemplateIdentityBake.cs:30` (`poses.Count != 221`)
and `OwnedTownManifestBake.cs:57` (`manifest.entries.Count != 221`). **Re-running either as-is against
the 169-structure scene THROWS.** That is CLAUDE.md §8's stale-count pattern living inside code.

---

## 4. Downstream: what a 3-star clear does TODAY

Not "silently skip" and not "throw". **It softlocks the victory screen.** Path, all read at source:

1. `RaidVictoryController.Start:124-128` — census construction throws, `FlowTrace.Fail` logs it, `_captureCensus = null`. Execution continues.
2. On victory, `:304-311` — `_captureRequired = captureRaidId == FinalRaidId && OwnedBase == null && _victoryStars >= CaptureStarsRequired`, then `FlowTrace.Step("Raid", "CAPTURE ELIGIBLE — …")` and `TryCommitCapturedTown(_victoryStars)`.
3. `TryCommitCapturedTown:1021-1027` — **the line that names it:**
   ```csharp
   if (_captureCensus == null)
   { FlowTrace.Fail("Raid", "Final victory cannot capture: precombat census is missing."); return false; }
   ```
4. `:915-919` — the end-state screen gets `PrimaryLabel = ownedTown.enter` and `PrimaryGate = CanEnterCapturedTown`.
5. `CanEnterCapturedTown:1004-1009` — `_captureRequired` true, `_captureCommitted` false, `TryCommitCapturedTown()` false → toast `ownedTown.captureRetry` (`en.json:16`: *"Your town could not be saved. Please try entering again."*) → returns **false**, forever.
6. `ReturnHome:990-993` — `if (!CanEnterCapturedTown()) return;` — **the only home route is gated by the same predicate.** So the auto-return timer and the primary button both refuse.

`RetryCaptureAfterDismissal` (`:1011-1017`) is **dead code** — `grep -n` finds no
`StartCoroutine(RetryCaptureAfterDismissal`. There is no retry that could ever succeed anyway: the
census is built once in `Start` and never rebuilt.

**Net player-facing effect:** a 3-star Iron Bastion clear shows the victory screen with an "Enter your
town" button that does nothing but toast, and no way back to the castle. The owner's 371701 run is one
3-star clear away from that.
**No bypass exists for a normal player.** `grep -rn "GoCastle" Assets/_Modules --include=*.cs`
(excluding `RaidVictoryController` and `SceneRouter` itself) returns no pause/system-menu route out of a
raid: the only other live callers are `HeroHealth.HandleDeath -> SceneRouter.GoCastle`
(`DungeonController.cs:231` documents it) — unreachable once the fight is won — and
`Assets/_Modules/HUD/OwnerDevToolsOverlay.cs:257` ("Go: Castle (home hub)"), which is the owner's dev
overlay, not a player affordance. The rest of the hits are onboarding/autopilot/comment lines.

---

## 5. Was it the regen or the bake? — answered

**The regen.** `452fc14fd` (WO-1732). The 22:00 09-15 bake is exonerated: the identity count was
already 0 at that commit, and `3b4b98834` (the only later scene commit) kept 0. `OwnedTown_IronBastion.unity`
has not been committed since `8b88a5053`, so it kept its 221 and the two halves separated there.

---

## 6. The fix

### 6a. REJECT the code fallback (census derives identity from the name)

Two reasons, both measured:

1. It reintroduces exactly what the identity was built to replace. `OwnedTownScenePose.TryResolve:79-92`
   is the legacy sibling-path + name branch, and `OwnedTemplateIdentityBake.cs:50-60` exists to prove
   the id survives a sibling reorder that the name path cannot. A name fallback fails that proof by
   construction, and `Wall_Outer_SS_*` names are generator-sequential — a regen renumbers them, so it
   would silently **mismap** saved structures instead of refusing.
2. **It would not restore capture.** `OwnedTownLayoutSnapshot.TryValidateAndCopy:43-44` refuses a
   169-structure snapshot against the 221-entry manifest. The break is the desynchronised
   raid/town/manifest triple, not the missing stamp alone.

### 6b. DO the bake-chain fix — and pin the identity so a regen cannot strip it again

Per `docs/ARCHITECTURE_PRINCIPLES.md` §2c (tests are the permission gate) and CLAUDE.md's
duplicated-state law (§2/§5/§8/§16 all state it): the defect is that a generated artifact carried
hand-stamped state that nothing verified.

**Step 1 — make the identity part of the generator's output, deterministically.**
`OwnedTemplateIdentityBake.cs:26` uses `Guid.NewGuid()`. With a regenerable scene, random ids mean
**every regen churns every saved town's `templateStructureId`**, and `TryResolve:72/:76` then answers
*"migration is required"*. Prefer emitting `_templateId` from `RaidBaseGenerator` as a deterministic
function of (config id + structure role + index) at creation time, so a regen reproduces the same ids.
**Today the churn is free** — the census only ran at all because `GameStateService.State.OwnedBase == null`
(`RaidVictoryController.cs:124`), i.e. no captured town exists in any save yet. That window closes the
day one player captures.

**Step 2 — one sanctioned chain, in this order, never re-inlined anywhere else:**

```
DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes      (regenerates RaidBase_* incl. iron_bastion)
DeNelle.Editor.OwnedTemplateIdentityBake.Run             (stamp — or a no-op once step 1 lands)
DeNelle.Editor.OwnedTownSceneBuilder.Build               (re-derive the owned town FROM the raid scene)
DeNelle.Editor.OwnedTownManifestBake.Run                 (re-bake IronBastionTemplate.json)
DeNelle.Editor.RaidBaseGenerator.ReseatOwnedTownSpire    (re-apply WO-1749 to the town copy)
DeNelle.Editor.RaidNavBake.BakeAll                       (already the documented NEXT step, :516-519)
```

Judge each by its marker on a fresh log, never the exit code (CLAUDE.md §8): `OWNED_TEMPLATE_IDS_OK`,
`OWNED_TOWN_SCENE_OK`, `OWNED_TOWN_MANIFEST_OK`, `OWNED_TOWN_SPIRE_RESEAT_OK`, `RAID_NAV_BAKE_OK`.

Both hardcoded `221` assertions must become **derived** (raid census count == town count == manifest
count) rather than re-typed to 169 — re-typing a literal is the bug repeating.

> ### ⛔ PINNED — ONE OWNER RULING BEFORE STEP 2 RUNS
> `RaidBaseGenerator.cs:528-533` declares `OwnedTown_IronBastion.unity` **"OWNER-AUTHORED AND
> PROTECTED — NOT regenerated, NOT re-dressed and NOT re-laid-out"**, and `ReseatOwnedTownSpire`
> (`:551+`) refuses in four places rather than rewrite it on a maybe.
> But `Assets/Editor/OwnedTownSceneBuilder.cs:14-40` (**UNTRACKED**) shows the scene **IS** derived:
> it opens `RaidBase_IronBastion.unity`, destroys the one `RaidGarrisonSpawner`, flips every
> `DefenseTower.Allegiance` to `PlayerOwned`, appends `OwnedTownController`, and Save-As's it to
> `OwnedTown_IronBastion.unity`.
> **The two statements conflict and only the owner can break the tie:**
> **(A)** re-derive the owned town from the new 158-wall raid scene (pair and manifest re-sync
> automatically; the town's 210-wall look changes to the WO-1723 partition), or
> **(B)** treat the 210-wall town as the authority and restore the raid scene's 210-wall partition
> (which re-opens WO-1732's destroyed-wall defect).
> Ask before running step 2. Everything in 6c is unblocked and can land first.
>
> ### ✅ RULED — OWNER, 2026-09-16. VERBATIM:
> > **"(A) re-derive OwnedTown_IronBastion and its manifest from the new 158-wall raid scene"**
>
> keeping WO-1732's partition. The pin is CLEARED; option **(A)** is canon. The owned town is a
> DERIVED artifact (`Assets/Editor/OwnedTownSceneBuilder.cs` has always derived it from the raid
> scene), and the `RaidBaseGenerator.cs:528-533` "owner-authored and protected — NOT regenerated"
> comment was FALSE and has been corrected in the same change.
>
> ### ✅ LEAD RULING, 2026-09-16 — RULING (A) EXTENDS TO THE PRACTICE ARENA
> The lead ruled that (A) covers `Assets/Scenes/ArenaPractice_IronBastion.unity` as well, because
> `Assets/Editor/OwnedTownPracticeSceneBuilder.cs:16` derives it **from the owned town** — so it
> re-derives with it. `OwnedTownPracticeSceneBuilder.Build()` is now a step in
> `OwnedTownChain.RebuildFromRaid`, and `OwnedTownTemplateIdentityRegression` lints the practice
> scene too: **raid == town == practice == manifest** on both census size and id set.
> Without it the arena stayed on 221 identities / 210 walls with the old 32-hex GUID ids while the
> town moved to 169, and `OwnedTownScenePose.TryResolve:59` accepts owned-template poses inside
> `PracticeCombatPolicy.SceneName` — so a captured town could not have resolved in practice mode.
>
> ⚠ **The step runs AFTER `RaidNavBake.BakeAll`, not before it as the lead's brief said** — a
> one-step deviation with a measured reason (see the RESULT §2h): the practice scene *shares* the
> town's `NavMesh.asset` (guid `819cc32d36ca7ed42af4986002cb0e8d`) and contains the `RaidGround`
> object, both of which only exist in a Save-As taken after the town was nav-baked. Derived before
> the bake it would have no ground and no navmesh.
>
> **Implementation note (deviation from §6b's step ORDER, recorded per CLAUDE.md §11B.B):** §6b
> listed `OwnedTemplateIdentityBake.Run` BEFORE `OwnedTownSceneBuilder.Build`. That order cannot
> run — the bake's second phase legacy-resolves every raid pose against the EXISTING town scene,
> which carried the pre-WO-1723 210-wall partition, so it throws before the town is re-derived.
> The bake is split into `StampRaid()` / `VerifyTown(poses)` and the verification runs after the
> derive. The sanctioned chain is `DeNelle.Editor.OwnedTownChain.RebuildFromRaid`.

### 6c. The regression that would have turned `452fc14fd` red — do this regardless of the ruling

New `Assets/Editor/Regression/OwnedTownTemplateIdentityRegression.cs`, an **asset lint** (no Unity play
needed) that reads the three artifacts and asserts, all derived, no literal counts:

- every `WallSegment`/`DefenseTower`/`RaidSpire` in **both** scenes carries a non-empty `_templateId`;
- ids are unique within each scene;
- the id **sets** of the two scenes are equal;
- `IronBastionTemplate.json`'s `templateStructureId` set equals them, and its entry count equals the
  scene structure count;
- and it names the sanctioned chain in its failure sentence.

Register it with the rest so it rides `REGRESSION_OK <n>/<n> suites` (mapping at
`Assets/Editor/Regression/DataRegression.cs:14-22`).

### 6d. Scene re-bake discipline

Steps 2's scene writes are a **re-bake**. They go through the batchmode entry points above only —
**never a hand edit** of `RaidBase_IronBastion.unity` or `OwnedTown_IronBastion.unity` (CLAUDE.md §3),
and never while the Unity editor is open.

---

## 7. ⛔ BLOCKER-CLASS FINDING — separate from this RCA, for the lead

The entire WO-1705 identity/capture layer is **untracked** (`git status --short`, 2026-09-16):

```
?? Assets/_Modules/Village/World/Camps/OwnedTemplateIdentity.cs      (+ .meta)
?? Assets/_Modules/Village/World/Camps/RaidCaptureCensus.cs
?? Assets/Editor/OwnedTemplateIdentityBake.cs                        (+ .meta)
?? Assets/Editor/OwnedTownManifestBake.cs                            (+ .meta)
?? Assets/Editor/OwnedTownSceneBuilder.cs                            (+ .meta)
?? Assets/Editor/OwnedTownScenePoseProof.cs, FinalRaidRouteProof.cs, OwnedTownReconstructionProof.cs, …
```

Two consequences:

1. **HEAD's `OwnedTown_IronBastion.unity` references script GUID `a1e512946c94bcd40ae870198aa00b4e`
   221 times, and neither that script nor its `.meta` is in git.** A fresh clone gets 221
   missing-script components and no way to regenerate them.
2. The 371701 device build **shipped uncommitted census code** — builds read the working tree (memory
   `a-deletion-in-git-status-will-ship-if-you-build`). The proving line in §1 comes from a class that
   does not exist at HEAD.

Recommend committing the WO-1705 layer by explicit path before or with this ticket's fix.

---

## 8. Files to edit

| File | Change |
|---|---|
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | emit a deterministic `_templateId` on each `WallSegment`/`DefenseTower`/`RaidSpire` at creation; document the chain in `BuildAllRaidScenes`' NEXT log line (`:516-519`) |
| `Assets/Editor/OwnedTemplateIdentityBake.cs` (untracked → commit) | derive the census size instead of `!= 221` (`:30`); keep the reorder/duplicate/frame proofs |
| `Assets/Editor/OwnedTownManifestBake.cs` (untracked → commit) | derive the entry count instead of `!= 221` (`:57`) |
| `Assets/Editor/Regression/OwnedTownTemplateIdentityRegression.cs` | NEW — §6c |
| `Assets/Scenes/RaidBase_IronBastion.unity`, `Assets/Scenes/OwnedTown_IronBastion.unity`, `Assets/Resources/OwnedTown/IronBastionTemplate.json`, `Assets/Scenes/RaidBase_IronBastion/NavMesh.asset` | **outputs of the chain only** — never hand-edited |

Optional, only if the owner also wants the softlock hardened: `RaidVictoryController.cs:990-1009` —
make `ReturnHome` fall through to `SceneRouter.GoCastle()` after N failed capture attempts so a broken
census can never strand the player. Do NOT bundle it with the data fix without the owner's word; it
would mask the very failure we want loud.

## 9. What NOT to touch

- `Assets/_Modules/**/SmartMobileCamera.cs` — another lane
- `Assets/_Modules/**/Troops/**` — another lane
- `RaidGarrisonSpawner.cs` — another lane
- `Assets/Scenes/RaidBase_raider_camp_small.unity`, `RaidBase_fortified_garrison.unity`,
  `RaidBase_mage_enclave.unity` — the lower tiers carry no owned-town template; the chain regenerates
  them as a side effect, which is fine, but no lower-tier behaviour changes here
- Do not edit `CLI_LANES_WO_NUMBERS.md` from this lane (number pre-assigned by the lead)

## 10. Acceptance criteria

1. `grep -c "_templateId" Assets/Scenes/RaidBase_IronBastion.unity` equals the scene's
   `WallSegment`+`DefenseTower`+`RaidSpire` count, and the same holds for `OwnedTown_IronBastion.unity`.
2. The two scenes' `_templateId` **sets are equal**, and equal to `IronBastionTemplate.json`'s
   `templateStructureId` set.
3. `OwnedTownTemplateIdentityRegression` is registered and **FAILS** when a `_templateId` is removed
   from either scene (prove the red, not just the green — CLAUDE.md §11B.A).
4. Markers on fresh logs: `OWNED_TEMPLATE_IDS_OK`, `OWNED_TOWN_SCENE_OK`, `OWNED_TOWN_MANIFEST_OK`,
   `OWNED_TOWN_SPIRE_RESEAT_OK`, `RAID_NAV_BAKE_OK`, `COMPILE_GATE_OK`, `REGRESSION_OK <n>/<n> suites`.
5. A headless/device run of Iron Bastion logs **no** `Precombat capture census failed`; a 3-star settle
   logs `OWNED_TOWN_CAPTURED` (`RaidVictoryController.cs:1031`) and the end-state primary button routes
   to the owned town.
6. Re-running `BuildAllRaidScenes` a second time leaves every `_templateId` **unchanged** (proves the
   deterministic emit, i.e. that WO-1732 cannot strip them again).
7. No hand edit of any `.unity` file in the diff (every scene change attributable to a chain marker).

---

## 11. Claims in this ticket that are NOT proven

- Whether the owner has hand-edited `OwnedTown_IronBastion.unity` since `8b88a5053`. Git shows no
  commit since, and the worktree is clean — but "authored by the builder then never touched" vs
  "authored by hand" is exactly the §6b ruling, and this lane did not open the scene in Unity.
- The 158 ruin holders were inferred not to carry `WallSegment` from the guid count matching the live
  `Wall_` name count (158 == 158). Not verified object-by-object.
