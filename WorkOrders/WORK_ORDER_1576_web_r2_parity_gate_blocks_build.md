# WO-1576: Web R2 parity gate blocks content build when target state is missing

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:07:14, build 2026.09.08.361259). PRIOR STATUS: FIXED - tools/command-centre.ps1 builds WebGL content BEFORE r2-ship (content-hashed bundles the run built are the ones pushed/verified); a missing aa/WebGL/settings.json emits R2_PARITY_REBUILD_NEEDED then refuses AFTER the build with the one-liner if still absent; parity refusals carry the real R2 line; -DryRun prints the order; PARSE_OK + COMMAND_CENTRE_DRYRUN_OK 2026-09-07. Canon on the step order (CLI_OPERATIONS_RUNBOOK, HANDOVER, KEY_FACTS, WO-1199) still describes the old order. PRIOR STATUS: READY TO IMPLEMENT
**Minted:** 2026-09-07 (web ship chain ordering fix; number from
CLI_LANES_WO_NUMBERS.md main-line banner, bumped 1576 -> 1577 in same edit)
**Silo:** Build / gates (tooling only - no gameplay, no scene, no content)
**Lane:** Web deploy / R2 parity. File-disjoint from gameplay lanes.

---

## 1. The defect (proven)

`tools/command-centre.ps1` step 2 (R2_PARITY_OK) gates step 5 (build-webgl.ps1).
After a failed WebGL content build deletes `Library/com.unity.addressables/aa/WebGL/settings.json`,
the next build chain run enters step 2 with no built-state for target=WebGL.
`tools/r2-parity.log` 2026-09-07 05:07: `R2_PARITY_THREW target=WebGL` and step 5 is never reached.

**The gate treats a missing-build-state as a DEAD END instead of "build it first."**

Evidence: `Builds/r2-parity.log` line containing `target=WebGL` before any attempt to build WebGL content.

## 2. Proposal

The gate step 2 must distinguish:
- **Addressables bundle exists remotely:** continue to verification (current path, correct).
- **Addressables bundle missing OR no built-state for this target:** invoke step 5 (build-webgl.ps1)
  FIRST, then re-enter step 2 to verify the new push.

Alternative ordering: steps execute in order [content-build → r2-push → verify → player-build].
The current order [verify → build → push] creates the dead-end.

## 3. Acceptance criteria

1. After a failed content build deletes the built-state, the next `command-centre.ps1` run
   does NOT exit on R2_PARITY_THREW.
2. Step 2 emits a distinct marker (e.g. `R2_PARITY_REBUILD_NEEDED`) when it triggers a rebuild.
3. Build output flows to `Builds/webgl-build.log` and push output to `Builds/vercel-deploy-*.log`,
   never orphaned or overwritten by step 2's verification.
4. Fresh log shows R2_PARITY_OK postdating the build and push markers.
5. The fix does NOT alter step 1 (pre-flight checks) or steps 6+ (player build, deploy).

## 4. Scope guards

- Do NOT modify individual build scripts (`build-webgl.ps1`, `web-ship.ps1`).
- Do NOT add a retry loop; call the existing steps in the correct order.
- Do NOT split the gate into two separate invocations.

---

*Provenance: minted 2026-09-07 from overnight web chain. Evidence: Builds/r2-parity.log 05:07.*
