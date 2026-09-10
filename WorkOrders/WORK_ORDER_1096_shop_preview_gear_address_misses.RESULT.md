# WO-1096 RESULT — the retired Blink ARMOR flag was vetoing ratified Blink WEAPONS

**Status:** IMPLEMENTED - awaiting gate (2026-09-09 lane SHOP)
**Lane:** SHOP (edit-only; no Unity run, no commit from this lane)
**Silo:** Content / Addressables · Shop UI

---

## Root cause (source-proven, and it matches the capture line for line)

`PartyShopVM.WeaponLoadsViaAddressable` opened with:

```csharp
if (!DeNelle.Core.FeatureFlags.BlinkArmor && def.id != null &&
    def.id.StartsWith("blink_", StringComparison.OrdinalIgnoreCase)) return false;
```

That is the **ARMOR** kill-switch (`ff.blinkarmor`, default OFF — `FeatureFlags.cs:52`; Blink *armor*
junked in the 2026-06-22 pivot, `HeroArmorVisual.cs:105`) applied to **weapon** ids, and it returned
**before** the row's own `loadVia` was read. So `blink_shield1h_03` — which declares
`"loadVia": "addressable"` and `"prefabPath": "gear/weapon/Shield1h_03"`
(`Assets/Resources/Data/Canonical/weapons.json:1422-1436`), an address that **exists** in the local
Gear group (`Gear.asset:634`) — was reported as non-addressable, and `PartyShopPanelMvvm` took the
string/Resources overload of `VisualFactory.Skin`, which delegates to the structure loader and falls
back.

That is exactly what capture seq=4968 records: `resolveAttempts=1`, **`lastTransportUrl=(none)`** —
no Addressables request was ever issued.

**The veto had also FORKED this predicate from the equip path.**
`EquipmentController.LoadsViaAddressable` (`EquipmentController.cs:1100-1108`) has **no** prefix test —
so the shop previewed one loader while equipping the same row used another. The comment above the shop
copy claimed "replicated, NOT forked"; it was forked.

**Content ruling it violated:** the owner ratified all **65** `blink_` **weapon** rows as shelf content
(2026-08-14); only Blink **armor** stays excluded — `_excludeIdPrefixesNote`,
`Assets/Resources/Data/Canonical/vendors.json:42`, pinned by `ForgeShelfClassKindRegression` case 3.

### Acceptance criterion 1 — the Step-1 Editor-only hypothesis is EXCLUDED, not confirmed

No play-mode data-builder setting can explain this occurrence, because **the Addressables request was
never made**. The branch decision happened before any loader was touched. The Editor play-mode script
is therefore irrelevant to this capture and was deliberately not consulted.

## The fix

`Assets/_Modules/Village/Hero/PartyShopVM.cs`

1. **The `blink_` prefix veto is deleted from the WEAPON predicate.** The branch is decided by the
   ROW (`loadVia == "addressable"`, else a `gear/` `prefabPath`) — identical to
   `EquipmentController.LoadsViaAddressable`, so the fork is closed.
2. **The ARMOR predicate keeps its veto verbatim.** The armor flag governs armor, and only armor.
3. Both predicates are now `public static` (documented as such) so the regression can pin the branch
   without constructing a whole VM. No behaviour rides on the visibility change.
4. **A `FlowTrace.Step` was ADDED, none removed** (§12: instrumentation is permanent). `PreviewModelFor`
   now logs, per preview, which loader was chosen **and why** — e.g.
   `preview loader branch: id='blink_shield1h_03' role=weapon -> ADDRESSABLE (loadVia='addressable'
   prefabPath='gear/weapon/Shield1h_03') — reason: the ROW's own loadVia/gear-path says addressable`.
   The armor line additionally names `ff.blinkarmor` and says "junked Blink ARMOR" when the flag is what
   excluded it. It fires once per `RenderPreview` (one call site,
   `PartyShopPanelMvvm.cs:1435`) — not a frame path, so the 3-arg form is correct here.
5. A block comment records the defect, the capture, the ruling and the pin, so the veto cannot be
   "restored" as a cleanup.

No warmer, spawner or loader was added or touched. `VisualFactory.ReportResolveMiss` untouched.

## RED-first proof (measured against the live catalog, 2026-09-09)

The predicate is pure, so the old and new forms were evaluated over the actual shipping JSON:

```
weapon rows total 96   blink_ rows with loadVia=addressable: 65
OLD predicate (ff.blinkarmor OFF) -> addressable for   0 / 65
NEW predicate                      -> addressable for  65 / 65
case1 blink_shield1h_03   OLD: False   NEW: True
case3 blink_armor_centurion  flag OFF: False    case4 flag ON: True
```

`65` is the same 65 the vendors.json ruling names — the whole ratified shelf was being routed to the
wrong loader, not just the shield that happened to get captured. **Cases 1 and 2 of the new regression
fail against the pre-fix code and pass against the fixed code**; cases 3–5 are unchanged by the fix and
exist to prove the armor exclusion is the FLAG (case 4 flips it ON) rather than a hardcode.

⚠ This is an evaluation of the predicate logic against the real rows, **not** a Unity run. The suite
itself has not been executed — this lane never fires Unity.

**The catalog copy question is closed by hash.** `GearCatalog` loads from
`Application.streamingAssetsPath`; the simulation above parsed the `Assets/Resources/...` copy. Both
copies are byte-identical (SHA-256, 2026-09-09): `weapons.json` `818fda8ee65d3855…` in
`Assets/Resources/Data/Canonical/` **and** `Assets/StreamingAssets/Data/Canonical/`; `armor.json`
`94501767a27d49b8…` in both. The measurement is therefore of the file the runtime actually loads.

## Nothing pinned the old behaviour (checked before hand-back)

- `grep -i blink Assets/Tests/EditMode/PartyShopVMTests.cs` → **no hits**. The EditMode tests only
  assert `PreviewModelFor` on absent/null/empty ids; none asserts a `blink_` weapon is non-addressable.
- No regression under `Assets/Editor/Regression/` referenced `PartyShopVM.*LoadsViaAddressable` before
  this one. `GearPropRendersRegression.cs:83-84` has its own private mirror — of
  **`EquipmentController.LoadsViaAddressable`**, which never had the prefix test, so it already agreed
  with the fixed predicate and is unaffected.
- **No second `blink_` gate downstream.** `PartyShopPanelMvvm` and `VisualFactory` contain no id-prefix
  test — their `Blink` hits are `FeatureFlags.BlinkChrome` (unrelated panel chrome) and
  `BlinkWardrobe` (rig dressing). This is the whole defect, not half of it.

## New regression

`Assets/Editor/Regression/PartyShopPreviewLoaderBranchRegression.cs`
Marker: `SHOP_PREVIEW_LOADER_BRANCH_OK <n>/<n>` | `SHOP_PREVIEW_LOADER_BRANCH_FAIL`
Menu: `Defenders/Regression/Party Shop Preview Loader Branch (WO-1096)`

Forces `ff.blinkarmor` to a known value and **restores the prior PlayerPrefs value in a `finally`**
(including deleting the key when it was absent).

**Registration line for `DataRegression.RunAll` (the CLI applies this — this lane does not edit
`DataRegression.cs`):**

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "shop-preview-loader-branch suite", () => { if (!DeNelle.Editor.PartyShopPreviewLoaderBranchRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[shop-preview-loader-branch] " + r); });
```

## Quality gate

CLAUDE.md §1 raw character count:
```
Assets/_Modules/Village/Hero/PartyShopVM.cs                              braces 210/210 MATCH  NUL 0
Assets/Editor/Regression/PartyShopPreviewLoaderBranchRegression.cs       braces  44/44  MATCH  NUL 0
```

`CompileGate.BraceBalanced` rule (the gate's OWN scanner, replicated from
`Assets/Editor/CompileGate.cs:896-948`):
```
Assets/_Modules/Village/Hero/PartyShopVM.cs                              175/175 BALANCED
Assets/Editor/Regression/PartyShopPreviewLoaderBranchRegression.cs        20/20  BALANCED
```

### ⚠ Gate RED once, and the reason is worth keeping

`Builds/wave1-compile2` (2026-09-09 23:24) reported
`BRACE MISMATCH in PartyShopVM.cs (175 open vs 174 close)` while the compiler raised **no CS error**
in the file. The raw §1 count was 210/210 the whole time — the two rules disagreed, and only the
gate's rule matters.

**Cause:** `CompileGate.BraceBalanced` is a plain character scanner with **no interpolated-string
model**. A `"` inside a `$"...{ ... }..."` hole *ends* the string for its state machine, so everything
after it is scanned as code until the next `"`. My first Step lines put nested literals in the holes
(`{(armorAddr ? "ADDRESSABLE" : "RESOURCES/fallback")}`, and a `?:` chain containing `"blink_"`), which
desynced the scanner and left one `{` counted as code.

**Fix (behaviour identical — same text, same values, same one Step per branch):** the branch label,
the reason, and the def fields are computed into **plain locals** first, so every interpolation hole
holds a bare identifier. The apostrophe in `"the ROW's own …"` also went, since a stray `'` flips the
scanner's char-literal state once quote parity is off. A comment at the site records why, so nobody
"tidies" it back into a ternary. The same hardening was applied to the regression's case-2 message
(`string.Join(", ", …)` moved out of its hole into `offenderList`).

## Unproven / for the CLI + PO

- **Not compiled.** No Unity was run from this lane; `COMPILE_GATE_OK` / `REGRESSION_OK` are owed.
- **The new `.cs` has no `.meta`** — Unity generates it on next import; it must be staged with the file.
- **Device repro was never established** (the WO's own Unproven section). The cause is source-proven and
  is a pure catalog/flag decision with no Editor-only component, so it applies to a player build
  identically — but that is an argument, not a measurement.
- **The other 64 rows have not been eyeballed.** The fix routes them to Addressables; whether every one
  of the 65 addresses resolves is a separate question (`GearAddressableGroupRegression` covers the
  group's entries, not this catalog's coverage of them).
- **PO closes on felt-verify:** open the party shop on `blink_shield1h_03` and one sibling shield on a
  cold panel open and confirm the model, not the fallback.
