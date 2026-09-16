# Documentation cleanup: lead review and guidance

Reviewed 2026-09-11 against the working tree. Source packet: `C:/Users/Elden/OneDrive/Desktop/review.md`. This is the requested evaluation, not approval to execute its migration. The source packet is unchanged.

**Decision: accept the measurement-card concept; amend the implementation and migration before applying. Keep the APK/AAB release work first.** The proposal identifies real readability problems, but several claimed safety properties are false and the proposed handover changes the owner's current priorities.

## Section 6 — seat answers

| # | Answer | Guidance and evidence |
|---|---|---|
| 1 | Partly | D1 is real: KEY_FACTS has 14 `## Latest` sections, not six, followed by standing sections such as Persistence/save and Builds. D2 is a discoverability issue, but root currently has two dated anchors, not an unbounded root chain. D3 is a useful automation opportunity, not proof that four fresh commands are a defect. D4 is real and already deliberately classified by the board parser. |
| 2 | Measurement card, with changes | STATE should report observations, sources, timestamps, errors and candidate identity. It must not become a new authority or make a green-current-tree claim from an old log. Do not add the proposed blanket ban on measured facts in prose: dated test results, incident reports and artifact receipts need their measured values. |
| 3 | Curate before moving | Do not assume every sentence under a dated heading is obsolete. Extract each exact section, identify still-binding instructions, and preserve them in the living card with provenance. Archive only reviewed historical content, byte-preserving it and documenting the successor. The supplied extractor is unsafe. |
| 4 | No, not this move list | GET_WELL_PLAN_2026-09-06.md explicitly says `LIVE PLAN. Supersedes nothing`; the packet has not demonstrated its successor. HANDOVER_2026-09-10_overnight.md still identifies itself as live. KEY_FACTS references PROGRAM_RAID_ECONOMY_2026-09-04.md as the North Star. Broad rename/archive passes require an explicit reviewed manifest and inbound-reference updates, not date/prefix inference. |
| 5 | No to folder-only classification | Existing `is_work_order` excludes numbered companion kinds. Pass 4 leaves WORK_ORDER_1114_IMPLEMENTATION_PLAN.md and WORK_ORDER_1038_VERIFICATION.md in WorkOrders, then a folder-only rule would promote them to tickets. README and RESULT files also remain. Keep the current classifier until all exceptions and result pairing are accounted for. Results are currently paired by basename recursively (`board_build.py:725`), so require unchanged ticket/result identities and bucket counts. |
| 6 | Staged, with a genuinely read-only preview | Three stages are reasonable after an explicit move manifest, collision checks, reference checks and before/after parser comparison. The supplied DRY_RUN writes files and cannot serve as a safety gate. Validate a replacement in a temporary fixture first; do not run this script even in dry-run mode on the repository. |
| 7 | Yes | Specifically protect the live plan and September 10 handover above; classify the two WORK_ORDER-prefixed companion files correctly. Keep WorkOrders/ManageRedesign untouched. Inventory destinations, RESULT companions, tools, docs and boot references before accepting any path change. No automatic bulk sweep is approved. |
| 8 | Yes, keep anchor changes separate | CLAUDE.md section 15 explicitly prescribes the dated root anchor and identifies the existing load-bearing entry documents. A docs cleanup cannot silently replace that convention or boot order. A later owner-approved work order can address discoverability; do not automatically mint or prioritize it during this release. |

## Corrections required in the four artifacts

### STATE generator

- `latest_marker` reads a fixed filename, not the newest relevant run. Current regression evidence includes `ready-iter4-regression.log`; do not assume `regression.log` is the latest authority.
- It searches success before failure and ignores ordering. A log containing an earlier OK and a later FAIL can be reported green. Resolve the terminal run verdict and mark ambiguous/truncated output unproven.
- Log age alone does not tie results to the current dirty source tree or artifact. Record run start/end, full HEAD, source/dirty fingerprint, target/defines and the tested artifact where available. Missing linkage means historical evidence, not current validation.
- Use the configured upstream, report ahead and behind separately, and say when the remote-tracking observation was last fetched. `origin/<branch>` need not be the upstream.
- The WO regex does not match the current Markdown banner reliably (including the PROD series and superseded entries). Reuse the project's allocation authority/parser; never invent a next free value from the first numeric match.
- `curl -sS -L` can return an HTTP error page with exit zero. Check HTTP status and validate the version payload. Select a device explicitly if multiple ADB devices are connected; distinguish offline, unauthorized, missing package and missing tool.
- Collect each observation once; use timeouts and explicit error reasons. Escape Markdown cells. Write output atomically and distinguish successful rendering from successful measurements.
- Keep remote/device probes optional. A local orientation command should not silently become a broad network check or a second mandatory boot system.

### Live handover

The supplied handover says Google Play AAB is parked, prioritizes Pi/WebGL, names WO-1706 as mandatory, and forbids Google Play tickets. Those are unsupported changes to this session: the owner explicitly prioritized **APK and AAB**, with raids through owned town to AI arena. No WO-1706 file was found in the current WorkOrders directory during this review.

Replace those asserted priorities with the actual release plan and authoritative work-order links. Do not claim this file is regenerated without providing a generator. Prefer refreshing the existing `docs/HANDOVER.md` and its established entry pointers over adding a competing read-first page. Do not assert one handover outranks an owner ruling merely because it is newer.

### Maintenance guidance

Retain the useful distinction between current rulings, measured state and historical evidence. Remove new blanket rules that declare all dated guidance obsolete or prohibit numbers in dated reports and ticket references. They conflict with the existing dated-anchor convention and evidence requirements. A summary document cannot promote itself to binding law while claiming not to change policy.

### Migration script

1. **Dry-run mutation:** the `awk` extraction runs directly, outside `run()`. It writes/truncates archive files even with DRY_RUN=1; if directories are absent, dry run may instead fail because mkdir was skipped.
2. **Incorrect section boundaries:** after the first Latest heading, awk keeps writing every following line until another Latest heading. The final archive therefore also receives the standing Persistence/save, Data catalogs, Builds, UI and Process sections. The Python deletion uses different boundaries, so extraction and removal disagree.
3. **Content/safety claims are inaccurate:** the script rewrites KEY_FACTS, creates files outside git mv, lacks destination collision checks and has no rollback for a partially completed move set. Its cleanliness check does not cover every root/tool target later proposed for changes.
4. **Classification mismatch:** the move pass keeps all WORK_ORDER-prefixed companions. A folder-only classifier would then misclassify them; result and README exceptions still need explicit handling.
5. **References are updated too late:** moves, successor banners, inbound links and parser behavior must be coherent in each stage, not repaired after a partially migrated tree.
6. **Board generation can change ticket status:** `tools/board_build.py:1672` calls `board_close_pass.run`; line 1690 calls `run_bounce`. A normal board rebuild is not a read-only verification step and conflicts with this packet's no-status-change boundary. Use a proven non-mutating validation path or an isolated fixture for comparison.
7. **Windows portability:** the awk match-array extension and shell assumptions need explicit validation. Prefer a small Python planner/executor with exact paths and one shared section parser for this workspace.

## Recommended sequence

1. Finish the current Android release checks and gameplay integration. Do not run the proposed migration alongside dirty release work.
2. Correct the packet's section 6 using the decisions above. The next proposal should contain exact file dispositions and evidence of each successor, not broad globs.
3. Build an optional measurement report using existing runner receipts. Test stale/mixed markers, dirty-source mismatch, unavailable devices, HTTP errors and Markdown banner parsing before making it an orientation dependency.
4. Reconcile existing entry documents rather than adding another authority layer.
5. Perform reviewed, staged documentation moves later, with a preview that writes nothing and pre/post proofs that all ticket identities, statuses, result pairings, references and archived bytes are preserved.

No migration, card generator, renamed canon, board rebuild, ticket-status change or commit was performed as part of this review.
