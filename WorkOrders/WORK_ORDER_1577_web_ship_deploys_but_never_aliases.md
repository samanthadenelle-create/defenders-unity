# WO-1577: Web ship aliases public domain to old deployment, never the current one

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:10:44, build 2026.09.08.361259). PRIOR STATUS: FIXED - tools/web-ship.ps1 aliases the public domains to the deployment it just made and refuses (WEB_ALIAS_FAIL, exit 16) unless `vercel alias ls` proves the alias names the new host; -DryRun prints the plan; PARSE_OK + WEB_DRYRUN_OK 2026-09-07. Unproven until the next real ship: Test-AliasResolves against live `alias ls` output; the cross-project defenders-webgl alias. PRIOR STATUS: READY TO IMPLEMENT
**Minted:** 2026-09-07 (web ship chain alias fix; number from
CLI_LANES_WO_NUMBERS.md main-line banner, bumped 1577 -> 1578 in same edit)
**Silo:** Build / gates (tooling only - no gameplay, no scene, no content)
**Lane:** Web deploy / alias management. File-disjoint from gameplay lanes.

---

## 1. The defect (proven)

`tools/web-ship.ps1` emits WEB_DEPLOY_OK when the new deployment reaches Vercel.
**It never aliases the public domain to that new deployment.**

Evidence: `Builds/vercel-deploy-echoes-of-elarion.log` shows successful deploy.
`vercel alias ls` (run by hand) shows echoes-of-elarion.vercel.app aliased to a 33-day-old deployment
until manual `vercel alias set echoes-of-elarion <deployment-id>` is run.

Same pattern: `defenders-webgl.vercel.app` remains aliased to a dormant deployment.

## 2. Proposal

After `vercel deploy`, parse the new deployment-id from the output, then invoke
`vercel alias` to set the public domain(s) to that id. Verify the alias was set via
`vercel alias ls` before emitting WEB_DEPLOY_OK.

Affected domains (per Vercel project defenders-of-the-realm-v2):
- echoes-of-elarion.vercel.app (production domain for echoes build)
- defenders-webgl.vercel.app (if still active; confirm with owner)

## 3. Acceptance criteria

1. `web-ship.ps1` captures the deployment-id from the successful deploy output.
2. It invokes `vercel alias set <domain> <deployment-id>` for each public domain.
3. It verifies the alias persisted via `vercel alias ls` and checks output.
4. If alias fails, emit a distinct marker (e.g. `WEB_ALIAS_THREW`) and withhold WEB_DEPLOY_OK.
5. Fresh log shows the deployment-id, the alias command, and the verification query result.
6. Confirm WEB_DEPLOY_OK postdates the successful alias.

## 4. Scope guards

- Do NOT modify `build-webgl.ps1` or any content-build path.
- Do NOT add retry loops on alias failure; let it fail loudly.
- Do NOT alias to a deployment from a different Vercel project.

---

*Provenance: minted 2026-09-07 from overnight web ship. Evidence: Builds/vercel-deploy-echoes-of-elarion.log, vercel alias ls manual check.*
