# WO-1703 result — implementation and clear-floor proof delivered; release gate pending

Updated 2026-09-13 (evidence timestamps cross into 2026-09-14 UTC). No owner acceptance or ticket closure is asserted.

The latest saved-scene checks confirm persisted textured ground reaches the measured enclosing wall/collision footprint. Ground and keep materials use the existing owned terrain texture dependencies and world-scale repetition. The accompanying WO-1704 height correction rebuilt the representative raids and refreshed their navigation before these saved checks.

Root-run evidence:

- `Builds/night-raid-height-regenerate.log`: three representative raid scenes regenerated.
- `Builds/night-raid-height-nav.log`: `RAID_NAV_BAKE_OK scenes=5`.
- `Builds/night-raid-height-ground-saved.log`: `RAID_GROUND_SAVED_OK 4/4` for persisted textured ground, wall/collision coverage and loaded navigation. The existing IronBastion local-navigation limitation is logged; this is not a broader navigation certification.
- `Builds/night-raid-height-polish.log`: `RAID_POLISH_SAVED_PROOF_OK scenes=3/3 images=33/33`, output `Builds/raid-polish-saved-proof/20260914-031117-134`. Root inspected all 33 images. Continuous ground texture is visible, with faint repeat seams; no open ground holes were identified. The mage ground frame remains partly obscured by rendered wall/keep strips, so the successful ledger is not sufficient clear-floor visual proof.

The capture-only correction now tests conservative bounds of every active, enabled non-ground renderer against the full padded orthographic ground-camera prism, alongside nine physics rays. It relocates the camera to a clear existing patch without hiding objects or modifying the scene. Marker `CLEAR_GROUND_RENDER_PRISM` records zero overlaps and nine floor samples. The fresh `DeNelle.Editor.RaidPolishSavedProof.Run` and visual inspection are complete; see the final reproof below.

The post-regeneration/bake provisioning step refreshed copies and hashes of the saved raid scenes, matching NavMesh assets, and ignored `Assets/Generated/RaidGround/RaidBase_<id>.mat` plus applicable `_KeepPlatform.mat`/`_KeepRamp.mat` and their metadata. The current correction introduces no new texture family.

Final full regression, release artifacts and owner test-build acceptance remain pending root gates. Owner-closed WO-1632/1633/1634/1635 remain closed.

## Final clear-floor reproof

`Builds/night-raid-clear-ground.log` passed `RAID_POLISH_SAVED_PROOF_OK` for all33 images in `Builds/raid-polish-saved-proof/20260914-040422-645`. All three ground cameras passed zero non-ground renderer-bound overlaps and nine ground-first physics rays. Root opened all three current ground images: continuous texture, faint repeat seams; mage lower cast shadow remains but no wall geometry obscures the frame. The seventeen changed non-ground images were also opened; no new wall gap, blocked gate or floor hole was identified. Fifteen other frames are byte-identical to the previously reviewed set. File hashes and visual limits are recorded in `Builds/night-raid-clear-visual-review.md` and comparison manifests.

The detached build inputs were refreshed and verified: all26 generated RaidGround files match current source, with six changed files copied after original destination-hash checks. Delta manifest SHA256 `4af6a08f811939dc89ba455753a784138200243d323b4c1194a1a2f565f6587d`. The complete124160-file ignored-input source-drift scan found no content drift. Full regression and actual release artifacts remain pending.
