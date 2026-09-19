# WO-1884 — Switch between Elarion (castle) and the captured village

**Status:** IMPLEMENTED — gated 2026-09-19 `COMPILE_GATE_OK` (`Builds/cg-1868-1884.log`) + `REGRESSION_OK` (`Builds/r-1885.log`). FIXED after tester APK. — claim 2026-09-19.

**Owner, verbatim (2026-09-19):** "i cant get back to the new village, after unlocking we need some mechanic to switch builds and choose"

## What is true now (read this session)
- 3-star Bastion → `SceneRouter.GoOwnedTown()` (`RaidVictoryController` `:1316`). That is the **first** entry.
- Re-entry from castle exists only as a yellow button on the **Realm deck** (`PlayerDeckWorkspace.cs:125-130`, `ownedTown.enter` = "Enter your town"), gated on `OwnedBaseProgression.Validate`. Realm is not a first-class home door (Map left the bar; Realm is a deck).
- Return **to castle** lived on `OwnedTownPanel` (`GoCastle` faces). WO-1876 stopped showing that panel, so those faces are gone. There is no production castle return on the owned-town HudKit.

So: unlock works once; after you leave (or never find Realm), the village is a dead end or a hidden card.

## Ruling
1. **Homes switcher** — a first-class HUD door, visible on **both** `Main_Castle_Overworld` and `OwnedTown_IronBastion` once `OwnedBaseProgression.Validate` is true. Not buried in Realm/gear/Settings.
2. Two choices: **Elarion** (`SceneRouter.GoCastle`) and **Your town** (`SceneRouter.GoOwnedTown`). Current scene is marked, not tappable. The other is the gold face.
3. Do **not** restore `OwnedTownPanel` as the door. Keep WO-1876 castle HUD on the owned town.
4. If only one captured town exists (Iron Bastion), still show two homes so the mechanic is obvious. Do not invent a third property.
5. Locale keys in all 10 catalogs, dual-copy. Reuse `ownedTown.enter` for the town row. Add `ownedTown.homes` (plate title) and `ownedTown.castle` (Elarion row). ASCII in TMP.
6. `PanelId` append-only if a new panel. HudActionBarRegression is the dock oracle if you add a dock face — a persistent chip (same family as gear) is acceptable and may be simpler.

## Files
- New HUD panel/VM or a thin Homes plate (HUD assembly; no Village types — `SceneRouter` + `OwnedBaseProgression` live in Core).
- `HudKitController` — show the chip when HasOwnedTown; **do not** leave Chat gear-row work to this ticket (WO-1882).
- `PlayerDeckWorkspace` Realm visit button may remain as a second door or be retired so there is exactly one public Homes door. Prefer **one door** (the new chip); keep the Realm button only if the oracle requires a named production constructor.
- Regression: `[homes-switcher]` — (A) with no owned base the door is absent; (B) with a valid owned base a production file outside the panel constructs it; (C) HUD files do not `using DeNelle.Village`; (D) `GoCastle` is reachable from owned-town source without `OwnedTownPanel`.

## Not in scope
WO-1882 chat door (HudKit Chat row). WO-1873 rooms. Raid fog. Rebaking scenes.

## Acceptance
`COMPILE_GATE_OK` + `REGRESSION_OK n/n`. Felt: from castle, open Homes → Your town. From the village, open Homes → Elarion. No Realm-deck hunt.
