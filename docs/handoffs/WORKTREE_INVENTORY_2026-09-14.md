# Worktree Inventory - 2026-09-14

**Generated:** 2026-09-14 04:01:01

## Summary

| Metric | Count | Size (GB) | Library (GB) |
|--------|-------|-----------|--------------|
| **Total Worktrees** | 97 | 891.39 | 161.71 |
| **Main Working Tree (D:\EoA)** | 1 | 386.08 | 64.28 |
| **Salvage (review first)** | 93 | 498.28 | - |
| **Safe to Delete** | 3 | 7.03 | - |
| **D: Disk Free Space** | - | **221.6** | - |

---

## Safe to Delete Now (no uncommitted changes, no commits ahead of dev)

These 3 worktrees are safe to delete immediately. Total: **7.03 GB** reclaimable.

| Path | Branch | HEAD | Size (GB) | Uncommitted | Ahead |
|------|--------|------|-----------|-------------|-------|
| D:\EoA\tmp\play-deploy-37837f585 | DETACHED | 37837f5 | 2.47 | 0 | 0 |
| D:\EoA\tmp\play-deploy-dcd25e9fe | DETACHED | dcd25e9 | 2.29 | 0 | 0 |
| D:\eoa-command-center-deploy | DETACHED | d185d40 | 2.27 | 0 | 0 |
---

## Salvage First (has uncommitted changes or commits ahead of dev)

These 93 worktrees have changes that may not be in dev. Verify before deletion.

### Large Worktrees (> 5 GB) - 5 items = 185.31 GB

| Path | Verdict | Changes | Ahead | Size (GB) |
|------|---------|---------|-------|-----------|
| D:\eoa-release-final | UNIQUE_OR_UNKNOWN | 51 uncommitted | 4 | 67.82 |
| D:\eoa-night-build-20260913 | UNIQUE_OR_UNKNOWN | 5 uncommitted | 1 | 39.06 |
| D:\eoa-release-2f167c8d6 | UNIQUE_OR_UNKNOWN | 55 uncommitted | 0 | 34.75 |
| D:\eoa-release-f59a3feea | UNIQUE_OR_UNKNOWN | 54 uncommitted | 2 | 34.71 |
| D:\eoa-grok-raid | UNIQUE_OR_UNKNOWN | 1042 uncommitted | 12 | 8.97 |
### Agent Worktrees (small, likely duplicates) - 71 items = 268.35 GB

Mostly agent worktrees with small changes (< 5 GB each). Likely duplicates of named worktree changes.

| Path | Uncommitted | Size (GB) |
|------|-------------|-----------|

| D:\eoa-codex-1418-polish | 56 | 3.05 |
| D:\eoa-codex-1418-a | 3 | 3.03 |
| D:\eoa-codex-1404 | 8 | 3.03 |
| D:\eoa-codex-1348 | 9 | 3.03 |
| D:\eoa-codex-1418-c | 4 | 3.03 |
| D:\eoa-codex-1418-b | 1 | 3.03 |
| D:\eoa-codex-1418-d | 1 | 3.03 |
| D:\eoa-codex-1413-part2 | 4 | 3.03 |
| D:\eoa-lane-1407 | 9 | 3.03 |
| D:\eoa-codex-1419 | 6 | 3.03 |
| D:\eoa-codex-1418-integration | 9 | 3.03 |
| D:\eoa-codex-1410-ready | 18 | 3.03 |
| D:\eoa-lane-1403 | 4 | 3.03 |
| D:\eoa-codex-1413 | 8 | 3.03 |
| D:\eoa-ready-board | 23 | 1 |
| D:\eoa-ready-1697 | 14 | 0.61 |
| D:\eoa-ready-backend | 24 | 0.57 |
---

## Cleanup Commands

### Delete SAFE worktrees immediately

Execute these in PowerShell to delete the 3 confirmed safe worktrees:

\\\powershell
# Remove registered linked worktrees
git -C D:\eoa worktree remove --force 'D:\EoA\tmp\play-deploy-37837f585'
git -C D:\eoa worktree remove --force 'D:\EoA\tmp\play-deploy-dcd25e9fe'
git -C D:\eoa worktree remove --force 'D:\eoa-command-center-deploy'

# Prune orphaned worktree entries
git -C D:\eoa worktree prune
\\\

**Reclaimable: 7.03 GB**

### Before Deleting Salvage Worktrees

For each named worktree (Codex, Lane, Ready, Release, Grok), verify its changes are committed or backed up:

\\\powershell
# Example: check what's changed in a large salvage worktree
git -C 'D:\eoa-release-final' status --short | head -20
git -C 'D:\eoa-release-final' log --oneline dev..HEAD | head -5
\\\

If the work is committed on a branch, the worktree is safe to delete (just remove it).
If there are uncommitted changes, either commit them, stash them, or extract specific files.

---

## Detailed Worktree Manifest

### Main Working Tree

| Attribute | Value |
|-----------|-------|
| Path | D:\EoA |
| Branch | dev |
| Kind | Main |
| Size | 386.08 GB |
| Uncommitted | 599 |
| Commits ahead of dev | 0 |

### All Worktrees (sorted by size)

| Path | Kind | Branch | HEAD | Size (GB) | Uncommitted | Verdict |
|------|------|--------|------|-----------|-------------|---------|
| D:\EoA | Main | refs/heads/dev | e53ec0f | 386.08 | 599 | MAIN_WORKING |
| D:\eoa-release-final | Linked | DETACHED | b9ba3f0 | 67.82 | 51 | UNIQUE_OR_UNKNOWN |
| D:\eoa-night-build-20260913 | Linked | DETACHED | dfe0c74 | 39.06 | 5 | UNIQUE_OR_UNKNOWN |
| D:\eoa-release-2f167c8d6 | Linked | DETACHED | e12355d | 34.75 | 55 | UNIQUE_OR_UNKNOWN |
| D:\eoa-release-f59a3feea | Linked | DETACHED | 4b17923 | 34.71 | 54 | UNIQUE_OR_UNKNOWN |
| D:\eoa-grok-raid | Linked | refs/heads/grok/raid-1593-1595 | 2094eac | 8.97 | 1042 | UNIQUE_OR_UNKNOWN |
| D:\EoA\.claude\worktrees\agent-a2df2f3ed921d85dc | Agent | refs/heads/worktree-agent-a2df2f3ed921d85dc | 1133baf | 3.8 | 5 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a064dbfc3b3634ad6 | Agent | refs/heads/worktree-agent-a064dbfc3b3634ad6 | aba49bd | 3.8 | 5 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a54531f37537f7517 | Agent | refs/heads/worktree-agent-a54531f37537f7517 | efc56f6 | 3.8 | 6 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a35ba6c65858e79ea | Agent | refs/heads/worktree-agent-a35ba6c65858e79ea | e4b5906 | 3.8 | 16 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a47e408f33584f4db | Agent | refs/heads/worktree-agent-a47e408f33584f4db | 736b6b4 | 3.8 | 4 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-ac6db22cce5d58766 | Agent | refs/heads/worktree-agent-ac6db22cce5d58766 | 0a7edc6 | 3.8 | 4 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-ab9ab1a0459cf58cc | Agent | refs/heads/worktree-agent-ab9ab1a0459cf58cc | e4b5906 | 3.8 | 6 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-abd53a619eecc5054 | Agent | refs/heads/worktree-agent-abd53a619eecc5054 | 0b942d0 | 3.8 | 4 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a223665cd051ef469 | Agent | refs/heads/worktree-agent-a223665cd051ef469 | ff42319 | 3.8 | 7 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a2c1df16b3935ffd8 | Agent | refs/heads/worktree-agent-a2c1df16b3935ffd8 | e4b5906 | 3.8 | 6 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a1c87f271103479d0 | Agent | refs/heads/worktree-agent-a1c87f271103479d0 | 736b6b4 | 3.8 | 7 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a1f6b172258ffe963 | Agent | refs/heads/worktree-agent-a1f6b172258ffe963 | de91a22 | 3.8 | 8 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a64cca841b3bd61c1 | Agent | refs/heads/worktree-agent-a64cca841b3bd61c1 | 0a7edc6 | 3.8 | 5 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a6426a2948a092b81 | Agent | refs/heads/worktree-agent-a6426a2948a092b81 | c96030b | 3.8 | 16 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a1ff0405c160e4272 | Agent | refs/heads/worktree-agent-a1ff0405c160e4272 | c96030b | 3.8 | 5 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a0ea91f673c1d7d3f | Agent | refs/heads/worktree-agent-a0ea91f673c1d7d3f | 95eb7ea | 3.8 | 3 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a1662e1f33e9b926a | Agent | refs/heads/worktree-agent-a1662e1f33e9b926a | de91a22 | 3.8 | 9 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a0c15f9d18e6ed54b | Agent | refs/heads/worktree-agent-a0c15f9d18e6ed54b | e4b5906 | 3.8 | 7 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a1fc67410e85b9162 | Agent | refs/heads/worktree-agent-a1fc67410e85b9162 | abbeb93 | 3.8 | 9 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a1a691a2e742db7be | Agent | refs/heads/worktree-agent-a1a691a2e742db7be | 0b942d0 | 3.8 | 3 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a75fecf8f4da804d5 | Agent | refs/heads/worktree-agent-a75fecf8f4da804d5 | c96030b | 3.8 | 19 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a9119d7f1bb8d4e3e | Agent | refs/heads/worktree-agent-a9119d7f1bb8d4e3e | 0b942d0 | 3.8 | 5 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a6afae9850478e00d | Agent | refs/heads/worktree-agent-a6afae9850478e00d | aba49bd | 3.8 | 7 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a8bb71753a62e0d92 | Agent | refs/heads/worktree-agent-a8bb71753a62e0d92 | 4329acc | 3.8 | 25 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a7c6e0e7c4f3ba388 | Agent | refs/heads/worktree-agent-a7c6e0e7c4f3ba388 | cde1c1f | 3.8 | 6 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a97cae425faa2b2d6 | Agent | refs/heads/worktree-agent-a97cae425faa2b2d6 | f33451b | 3.8 | 6 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a985087f36e7b0c2e | Agent | refs/heads/worktree-agent-a985087f36e7b0c2e | 0b942d0 | 3.8 | 6 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a00db190be27b5549 | Agent | refs/heads/worktree-agent-a00db190be27b5549 | 4329acc | 3.8 | 17 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a9e001ddb25631dda | Agent | refs/heads/worktree-agent-a9e001ddb25631dda | c96030b | 3.8 | 6 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a9eeaae7251d2c8c4 | Agent | refs/heads/worktree-agent-a9eeaae7251d2c8c4 | 0b942d0 | 3.8 | 5 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-af53a4db6a2ef936d | Agent | refs/heads/worktree-agent-af53a4db6a2ef936d | f33451b | 3.8 | 3 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a98eb6596f14ac667 | Agent | refs/heads/worktree-agent-a98eb6596f14ac667 | a065424 | 3.8 | 3 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-accb906d0715e758e | Agent | refs/heads/worktree-agent-accb906d0715e758e | abbeb93 | 3.8 | 15 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-aa90b22e726b4c459 | Agent | refs/heads/worktree-agent-aa90b22e726b4c459 | 1133baf | 3.8 | 8 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-ab556907cc76f29fc | Agent | refs/heads/worktree-agent-ab556907cc76f29fc | 4329acc | 3.8 | 7 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-aa363b05fd9dba0d4 | Agent | refs/heads/worktree-agent-aa363b05fd9dba0d4 | 9592cdd | 3.8 | 3 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a879bf9887691bfa1 | Agent | refs/heads/worktree-agent-a879bf9887691bfa1 | a89603a | 3.8 | 5 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-ae2c937320406b224 | Agent | refs/heads/worktree-agent-ae2c937320406b224 | e4b5906 | 3.8 | 13 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-af0250ab2e956be05 | Agent | refs/heads/worktree-agent-af0250ab2e956be05 | c3e7676 | 3.8 | 6 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-ad0b5c7a8c34ea18a | Agent | refs/heads/worktree-agent-ad0b5c7a8c34ea18a | de91a22 | 3.8 | 12 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a5c4068f74dbd22cd | Agent | refs/heads/worktree-agent-a5c4068f74dbd22cd | c10e4f5 | 3.79 | 8 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a4ae96c893a73365b | Agent | refs/heads/worktree-agent-a4ae96c893a73365b | c10e4f5 | 3.79 | 4 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a7e746b59e527473d | Agent | refs/heads/worktree-agent-a7e746b59e527473d | c10e4f5 | 3.79 | 7 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a8b7c36493769d74d | Agent | refs/heads/worktree-agent-a8b7c36493769d74d | 3da5e53 | 3.79 | 3 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a3dd7f19089bc8008 | Agent | refs/heads/worktree-agent-a3dd7f19089bc8008 | 5a65a78 | 3.79 | 5 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a43ca968d138479be | Agent | refs/heads/worktree-agent-a43ca968d138479be | 406dbda | 3.79 | 4 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a945f93ff422c338b | Agent | refs/heads/worktree-agent-a945f93ff422c338b | bb12c72 | 3.79 | 4 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a3019a35d333a1c8e | Agent | refs/heads/worktree-agent-a3019a35d333a1c8e | 3da5e53 | 3.79 | 5 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a63700b5145296dbd | Agent | refs/heads/worktree-agent-a63700b5145296dbd | bb12c72 | 3.79 | 4 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-ab2eb46c6d45c9fca | Agent | refs/heads/worktree-agent-ab2eb46c6d45c9fca | a96bfe3 | 3.79 | 32 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a7c3150887594ff9e | Agent | refs/heads/worktree-agent-a7c3150887594ff9e | a96bfe3 | 3.79 | 7 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a23245c9833bd44f1 | Agent | refs/heads/worktree-agent-a23245c9833bd44f1 | c10e4f5 | 3.79 | 4 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-ac2fe3f338f28c3ac | Agent | refs/heads/worktree-agent-ac2fe3f338f28c3ac | 3da5e53 | 3.79 | 8 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-ab5c7c73de3dcc2dc | Agent | refs/heads/worktree-agent-ab5c7c73de3dcc2dc | e225ca5 | 3.79 | 3 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a587cc193e1950ad3 | Agent | refs/heads/worktree-agent-a587cc193e1950ad3 | c10e4f5 | 3.79 | 12 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a22e41529ea6dc56c | Agent | refs/heads/worktree-agent-a22e41529ea6dc56c | 8ed4c14 | 3.79 | 4 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a779ae42768c0c6c2 | Agent | refs/heads/worktree-agent-a779ae42768c0c6c2 | a96bfe3 | 3.79 | 5 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a6b38e5d1494904b1 | Agent | refs/heads/worktree-agent-a6b38e5d1494904b1 | a96bfe3 | 3.79 | 2 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-ac52944e70b8d2421 | Agent | refs/heads/worktree-agent-ac52944e70b8d2421 | 5a65a78 | 3.79 | 5 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-ae76284e121864b5a | Agent | refs/heads/worktree-agent-ae76284e121864b5a | 3da5e53 | 3.79 | 9 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-ae49ed9e35bde6122 | Agent | refs/heads/worktree-agent-ae49ed9e35bde6122 | a96bfe3 | 3.79 | 6 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-ae654ca97d671d112 | Agent | refs/heads/worktree-agent-ae654ca97d671d112 | 3da5e53 | 3.79 | 3 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a738c39975c466b06 | Agent | refs/heads/worktree-agent-a738c39975c466b06 | 446c8b9 | 3.79 | 4 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a71ccadbdccef7798 | Agent | refs/heads/worktree-agent-a71ccadbdccef7798 | 3c47ebb | 3.79 | 3 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a45005d2de18ae581 | Agent | refs/heads/worktree-agent-a45005d2de18ae581 | 5c54195 | 3.79 | 7 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-aedeb6debebdcf9ba | Agent | refs/heads/worktree-agent-aedeb6debebdcf9ba | 3da5e53 | 3.79 | 9 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a67dd93793c0c4545 | Agent | refs/heads/worktree-agent-a67dd93793c0c4545 | 02fb42b | 3.79 | 2 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a671dfc8ae916a90e | Agent | refs/heads/worktree-agent-a671dfc8ae916a90e | 3da5e53 | 3.79 | 6 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-af8f895ab6d98d09c | Agent | refs/heads/worktree-agent-af8f895ab6d98d09c | aff7f8d | 3.79 | 3 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-af5ccd5ebe01b0b28 | Agent | refs/heads/worktree-agent-af5ccd5ebe01b0b28 | f5d39ac | 3.22 | 1 | UNKNOWN_AGENT |
| D:\EoA\.claude\worktrees\agent-a74b360b34c7b3fb0 | Agent | refs/heads/worktree-agent-a74b360b34c7b3fb0 | f5d39ac | 3.22 | 5 | UNKNOWN_AGENT |
| D:\eoa-codex-1418-polish | Linked | refs/heads/codex/wo-1418-polish | ecf647b | 3.05 | 56 | UNIQUE_OR_UNKNOWN |
| D:\eoa-codex-1418-a | Linked | refs/heads/codex/wo-1418-a | 44d4612 | 3.03 | 3 | UNIQUE_OR_UNKNOWN |
| D:\eoa-codex-1404 | Linked | refs/heads/codex/wo-1404 | 003b64c | 3.03 | 8 | UNIQUE_OR_UNKNOWN |
| D:\eoa-codex-1348 | Linked | refs/heads/codex/wo-1348 | 44d4612 | 3.03 | 9 | UNIQUE_OR_UNKNOWN |
| D:\eoa-codex-1418-c | Linked | refs/heads/codex/wo-1418-c | 44d4612 | 3.03 | 4 | UNIQUE_OR_UNKNOWN |
| D:\eoa-codex-1418-b | Linked | refs/heads/codex/wo-1418-b | 44d4612 | 3.03 | 1 | UNIQUE_OR_UNKNOWN |
| D:\eoa-codex-1418-d | Linked | refs/heads/codex/wo-1418-d | 44d4612 | 3.03 | 1 | UNIQUE_OR_UNKNOWN |
| D:\eoa-codex-1413-part2 | Linked | refs/heads/codex/wo-1413-part2 | 458baf5 | 3.03 | 4 | UNIQUE_OR_UNKNOWN |
| D:\eoa-lane-1407 | Linked | DETACHED | 44d4612 | 3.03 | 9 | UNIQUE_OR_UNKNOWN |
| D:\eoa-codex-1419 | Linked | refs/heads/codex/wo-1419 | 003b64c | 3.03 | 6 | UNIQUE_OR_UNKNOWN |
| D:\eoa-codex-1418-integration | Linked | DETACHED | 44d4612 | 3.03 | 9 | UNIQUE_OR_UNKNOWN |
| D:\eoa-codex-1410-ready | Linked | refs/heads/codex/wo-1410 | 003b64c | 3.03 | 18 | UNIQUE_OR_UNKNOWN |
| D:\eoa-lane-1403 | Linked | refs/heads/lane/wo-1403 | 241d125 | 3.03 | 4 | UNIQUE_OR_UNKNOWN |
| D:\eoa-codex-1413 | Linked | refs/heads/codex/wo-1413 | 44d4612 | 3.03 | 8 | UNIQUE_OR_UNKNOWN |
| D:\EoA\tmp\play-deploy-37837f585 | Linked | DETACHED | 37837f5 | 2.47 | 0 | SAFE |
| D:\EoA\tmp\play-deploy-dcd25e9fe | Linked | DETACHED | dcd25e9 | 2.29 | 0 | SAFE |
| D:\eoa-command-center-deploy | Linked | DETACHED | d185d40 | 2.27 | 0 | SAFE |
| D:\eoa-ready-board | Linked | refs/heads/codex/ready-board | 1895298 | 1 | 23 | UNIQUE_OR_UNKNOWN |
| D:\eoa-ready-1697 | Linked | refs/heads/codex/ready-1697 | 642744e | 0.61 | 14 | UNIQUE_OR_UNKNOWN |
| D:\eoa-ready-backend | Linked | refs/heads/codex/ready-backend | 642744e | 0.57 | 24 | UNIQUE_OR_UNKNOWN |
---

## Reclamation Strategy

1. **Immediate (safe, 0 risk):** Delete 3 worktrees = **7.03 GB**
   - \git worktree remove --force\ for the 3 SAFE worktrees above
   - \git worktree prune\ to clean up metadata

2. **Library Cleanup (safe, regnerateable):** Delete all \Library/\ folders across all worktrees = **~161.71 GB**
   - Regenerates on next Unity Editor open
   - Use \Remove-Item -Recurse -Force\ per worktree

3. **Salvage Then Delete (medium risk):** Named worktrees = **498.28 GB**
   - Verify each has work committed or backed up
   - Run \git log --oneline dev..HEAD\ to check for commits
   - Run \git status --short\ to check for uncommitted changes
   - If clean, delete with \worktree remove --force\

4. **Agent Worktrees (low risk):** ~71 small agent worktrees = **268.35 GB**
   - These are usually duplicates of larger work
   - Safe to delete once the named worktrees are verified

**Total reclaimable (with Library cleanup):** ~667 GB

Current D: free space: **221.6 GB**

---

## Notes

- **Registered Linked Worktrees:** 81 (verified via \git worktree list --porcelain\)
- **Agent Worktrees Under .claude/worktrees/:** 71 actual directories
- **Standalone Clones Under D:\\:** ~26 \eoa-*\ directories (Codex, Lane, Ready, Release, etc.)
- **Orphan Worktree Metadata:** None found in .git/worktrees/ (all paths resolve)
- **Other Repositories:** DTT, Work_POC, and .tmp-solana-recover under D:\ (not eoa-related)

All sizes computed by recursive file enumeration with \Get-ChildItem\. Library folders are Unity cache and safe to delete.
