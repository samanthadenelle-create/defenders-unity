# Clean build checkpoint inventory

Read-only inventory of the current worktree, 2026-09-13. No staging, index mutation, commit, reset,
stash, patch application, worktree creation, or source edit was performed by this inventory lane.
The only repository writes are these reports/manifests. Counts describe the timestamp in `paths.json`,
not an immutable claim about a worktree other lanes continue changing.

## Measured inventory

712 dirty paths: 354 tracked, 358 untracked. The actual index already contains 37 staged paths.
`DataRegression.cs` and `StructureFactory.cs` contain both staged and unstaged work. There are 96
registered worktrees (including this one) and six stashes. Stash subjects mostly claim supersession,
but one describes unfinished WO-1388/1389 work; subjects are not validation and nothing was imported.
No `.patch`/`.diff` files were found under `.ai`, `.claude`, or `docs/handoffs` with the scoped `rg`
query. Historical WorkOrders patch directories and every other worktree were not apply-checked.

`paths.json` is the exact status/path/size manifest, with overlapping classification tags and the
real index SHA256. Each tag has a matching explicit `*.paths.txt` list. These are review groupings,
not authority to commit every path in a group. Food-to-Stone tags include textual diff evidence
(removed Food token plus added Stone token), which is discovery rather than semantic proof.

| Review group | Tagged paths |
| --- | ---: |
| Food-to-Stone migration | 205 |
| Prior castle/storage/footprint work, including WO-1291 reconciliation | 79 |
| Owned-town/WO-1705 | 90 |
| Manage/WO-2016 | 13 |
| WebGL/WO-1701 | 9 |
| Raid floors/WO-1703 | 4 |
| Raid continuity/WO-1704 | 8 |
| Serialized scene/art/Addressables | 52 |
| Localization | 28 |
| Canonical JSON mirror paths | 38 |
| Generated evidence | 132 |
| Local orchestration tools/context | 37 |
| Still unclassified | 83 |

Tags overlap because shared files really contain more than one lane. In particular, the capture
harness and regression registry cannot be safely divided by filename into a pure new-ticket commit
and a pure migration commit. No hunk ownership was invented.

## Recommended source checkpoint

Use a separate Git index and detached checkpoint commit, then a separate clean build worktree.
This preserves the user's staged state byte-for-byte and avoids importing any historical worktree
or stash. The user requested the current validated product as a whole, so the checkpoint must carry
the current Food-to-Stone dependency closure together with castle/owned-town and six-ticket work.
Excluding old-but-required economy changes merely because they predate tonight would build a
different product or break compilation.

`candidate-current-unity-tree.paths.txt` and its NUL-delimited sibling enumerate the current dirty
paths under Assets, Packages, and ProjectSettings. They are an explicit current-product snapshot
candidate, not a claim each path passed review. Include their untracked runtime/scene/meta companions
after root validation; do not import anything from the other 95 worktrees. The snapshot starts from
HEAD, so unchanged tracked source remains present automatically. Include selected build runner
changes and result/canon documents by explicit path separately, after reviewing them. In particular,
the run-unity-method/playmode scripts are needed if root wants their current changed behavior.

Root-only sequence (not executed here):

1. Freeze edits; refresh status and the exact allowlist after final fixes. Save the real index hash,
   existing staged path list, HEAD, and all included file hashes. Verify LFS objects are available.
2. Set `GIT_INDEX_FILE` only in a child process environment to a new temporary index pathname outside
   the real `.git/index`; the file must not already be an empty placeholder. Run `git read-tree HEAD`.
3. In that same environment, run `git --literal-pathspecs add --pathspec-from-file=<approved.nul>
   --pathspec-file-nul`. This copies the approved worktree bytes into the alternate index, including
   staged+unstaged changes intentionally, while leaving the real index untouched. Inspect the
   alternate `git diff --cached --name-status` against the approved manifest before continuing.
4. `git write-tree`, then `git commit-tree <tree> -p <original-head> -F <message-file>`. Record the
   resulting SHA and an explicit local checkpoint ref with `git update-ref`; do not move `dev`, do
   not push, and do not represent the checkpoint as final ticket acceptance. `commit-tree` bypasses
   normal commit hooks, so root must execute the required hooks/checks explicitly. Recheck real
   index hash and main HEAD to prove preservation.
5. Create a detached build worktree at that SHA. No `reset`, stash, or checkout of the main tree is
   needed. Assert this worktree's tracked status is clean before gating/building. Run the complete
   final regression/capture checks on this exact source checkpoint, then build and record the SHA.

The separate worktree needs disk space and its own writable Library/cache. Do not share a live
Library with the main editor, and do not junction writable scenes/source back to the dirty root.
The project has required ignored art packs, so a bare Git checkout alone is not sufficient: inventory
and provision the exact immutable ignored assets via the established pack process, with hashes.
Read-only sharing of immutable pack inputs can avoid duplicating them if the existing build process
supports it; do not improvise shared writable imports. Storage audit must clear this before launch.
Creating a commit-tree object alone does not make the existing dirty working directory a clean build.

## Suggested later history groupings

After the checkpoint is verified, root can form normal explicit-path lane commits as appropriate:
Food-to-Stone foundation/compatibility; preserved Tripo castle + storage/footprint; owned-town and
raid floor/continuity; WebGL loader; Manage captures; release settings; board/courier/results. Shared
files need deliberate hunk review or an explicitly named integration commit. Preserve existing
staging; the playbook's generic reset recipe is superseded by this session's instruction not to reset.
ProjectSettings belongs to release provenance, not an unrelated lane commit.

## Exclusions and risks

- Preserve the owner's original Tripo castle. WO-1291 does not authorize reapplying the old Synty
  replacement. Scene/art changes require the current saved-scene and rendered evidence.
- Do not automatically include `.ai/`, `tools/hybrid-orchestrator/`, raw `proof/`/`Logs/debug/`, pitch
  builds, advisor drafts, or the unrelated deleted `docs/WARDROBE_ARCHITECTURE.zip`. The deleted zip
  is unrelated to the Unity release and should not become an accidental deletion commit.
- Backend `api/` and tests have separate compatibility/deployment implications. An EXE/APK/WebGL
  player build request does not implicitly deploy the dirty backend. Keep Vercel's publication root
  explicit so preview publishing cannot sweep API changes by accident.
- Preserve canonical Resources/StreamingAssets twins and Unity `.meta` partners; untracked owned
  town manifests/scenes are product inputs, not disposable output just because Git says untracked.
- Narrow credential-shaped text and filename scans found zero matches in the inventoried small text
  files. This is not an exhaustive secret audit. Raw device logs, screenshots, advisor documents,
  and local orchestration configuration remain privacy-review candidates and are excluded by default.
- A stale path manifest is not safe after root fixes more files. Refresh before snapshot and compare
  the final checkpoint's runtime files with the validated working tree. Do not claim equality from
  an earlier full-regression marker.
