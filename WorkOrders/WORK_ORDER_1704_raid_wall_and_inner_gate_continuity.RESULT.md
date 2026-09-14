# WO-1704 result — fixed pending owner's test build

Updated 2026-09-13 (evidence timestamps cross into 2026-09-14 UTC). This records delivered implementation and root-run evidence; it does not grant owner acceptance or close the ticket.

The exterior now carries continuous approved `Fantasy_M/Dungeon_Wall_Stone.prefab` backing through the actual wall body. The previous 3.003 m backing left visible upper openings between the 7.044 m pillars. Backing is now 6.692 m (95% of the measured original skyline); the decorative caps remain. Only its vertical fit changed: palette, random placement sequence, horizontal extent, depth, intended gate openings and staging envelope remain unchanged.

Measured red/green evidence:

- `Builds/night-raid-wall-height-baseline.log`: `RAID_WALL_CONTINUITY_FAIL 36`. Actual triangle probes at 50%, 75% and 90% of the independently measured original skyline exposed upper openings across all three configs. Earlier ankle/head-only checks were insufficient.
- `Builds/night-raid-wall-height-after.log`: `RAID_WALL_CONTINUITY_OK`. The higher probes pass alongside lower probes, cladding continuity, inner gate presence and gate aperture checks. All three configs report original skyline 7.044 m and backing 6.692 m.
- `Builds/night-raid-height-regenerate.log`: three representative saved raid scenes regenerated.
- `Builds/night-raid-height-nav.log`: `RAID_NAV_BAKE_OK scenes=5`.
- `Builds/night-raid-height-ground-saved.log`: `RAID_GROUND_SAVED_OK 4/4`, with the existing IronBastion local-navigation limitation explicitly logged.
- `Builds/night-raid-height-polish.log`: `RAID_POLISH_SAVED_PROOF_OK scenes=3/3 images=33/33`. Root inspected all 33 images in `Builds/raid-polish-saved-proof/20260914-031117-134`; the north oblique close-ups clearly show the upper wall closure. Saved gate path/aperture evidence remains passing.

The same capture set exposed an obscured mage floor frame despite passing collider probes. The capture harness now rejects conservative renderer-bound overlap across the entire ground-camera prism, in addition to its nine floor raycasts. Fresh ground-clear capture and image inspection are complete (final reproof below); the previous 33/33 ledger must not be described as proof of an unobstructed mage floor.

Reproduction entrypoints: `DeNelle.Editor.RaidWallContinuityRegression.RunHeadless`; regenerate with `DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes`, then `DeNelle.Editor.RaidNavBake.BakeAll`; inspect saved output with `DeNelle.Editor.RaidPolishSavedProof.Run`. Root owns Unity, asset generation and release gates. Full final regression, release artifacts and owner test-build acceptance remain separate pending gates.

## Final clear-floor reproof

`Builds/night-raid-clear-ground.log` passed `RAID_POLISH_SAVED_PROOF_OK` for all33 images in `Builds/raid-polish-saved-proof/20260914-040422-645`. All three ground cameras passed zero non-ground renderer-bound overlaps and nine ground-first physics rays. Root opened all three current ground images: continuous texture, faint repeat seams; mage lower cast shadow remains but no wall geometry obscures the frame. The seventeen changed non-ground images were also opened; no new wall gap, blocked gate or floor hole was identified. Fifteen other frames are byte-identical to the previously reviewed set. File hashes and visual limits are recorded in `Builds/night-raid-clear-visual-review.md` and comparison manifests.

The detached build inputs were refreshed and verified: all26 generated RaidGround files match current source, with six changed files copied after original destination-hash checks. Delta manifest SHA256 `4af6a08f811939dc89ba455753a784138200243d323b4c1194a1a2f565f6587d`. The complete124160-file ignored-input source-drift scan found no content drift. Full regression and actual release artifacts remain pending.
