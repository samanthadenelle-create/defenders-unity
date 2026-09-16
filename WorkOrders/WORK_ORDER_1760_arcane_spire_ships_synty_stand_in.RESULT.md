# WO-1760 RESULT — Arcane Spire re-pointed to the owner's FBX art

**Status:** CLOSED - owner felt-test PASS 2026-09-15 on Seeker APK 371627 ("spire is good close it")

- Cause (proven at source): the uncommitted 2026-09-13 dedup of `Structure_Art.asset` kept the Synty
  wrapper GUID for each of `Structures/ArcaneSpire_{1,2,3}` and dropped the owner's FBX GUIDs;
  `ArcaneSpire_2` alone mapped to `SM_Bld_Castle_Wall_Tower_L_01`, hence the upside-down middle tier.
- Fix: the three addresses resolve only to `Assets/StructureContent/ArcaneSpire_{1,2,3}.fbx`; the three
  `Assets/StructureContent/Synty/ArcaneSpire_*.prefab` wrappers and their `SyntyStructureRetheme` map rows
  deleted; GUIDs pinned in `OriginalStorefrontGuids`.
- Oracle: `ArcaneSpireArtAuthorityRegression` (`[arcane-spire-art]`, `ARCANE_SPIRE_ART_OK`) — green in
  `REGRESSION_OK 539/539` (`Builds/data-regression.log`, 21:01).
- Shipped: AAB `2026.09.16.371610` (versionCode 371610) and APK `371627`; `R2_PARITY_OK objects=204`;
  catalog names the FBX-derived bundles `_1_9b064c41…`, `_2_7d6191569…`, `_3_52e948e6…`.
  Record: `docs/releases/GOOGLE_PLAY_2026.09.16.371610.md`.
- Open: `[structure-orientation]` measured the FBX tiers for the first time with a single claimant and
  stayed green (539/539). Owner felt-test on device closes this ticket.
