# WO-1430: the seam oracles' first eight findings - three panels no player can open, five authored fields no code reads

**Status:** READY TO IMPLEMENT - fields 3-5 RULED 2026-09-10: DROP (remove from the catalog JSON + any schema/pin that names them; re-add when a reader exists); fields 1-2 landed `4f2698b86`. PRIOR STATUS: IMPLEMENTED (Group B: 2 of 5 wired) - awaiting gate (2026-09-09 lane FIELDS); 3 need an owner ruling.
Group A (the three doorless panels) RESOLVED in `bb51b8b9c` (`PanelDoorRegression.cs:159-161` allowlist now empty).
Group B: `unlockMethod` and `requiresHero` are WIRED and their exemptions DELETED (both the `ParkedClaims` and the
`UnreadBaseline` rows); `levelCurve`, `visibilityRule` and `expiry_behavior` **were** exempted pending three owner
rulings stated in `WORK_ORDER_1430_...RESULT.md` §3 — **RULED 2026-09-10: DROP all three** (see the OWNER RULING
block below for the file-by-file map). Minted 2026-09-06 (CLI). Each remaining finding is EXEMPTED in its oracle with a dated pointer
to this file, so the gate is green and the oracles stay sharp for anything NEW. **Nothing here is hidden; it is parked.**
**Silo:** mixed - HUD panels, and five separate catalogs
**Source:** `PanelDoorRegression` and `AuthoredFieldReaderRegression`, both shipped 2026-09-06 in Wave 0 of the Manage
redesign, both RED on arrival by design. Full evidence in `Builds/r23`.

---

### OWNER RULING 2026-09-10 (morning)

> **"Drop them: remove the dead fields from the catalog; re-add each when a system needs it"** — the
> owner's answer, given via AskUserQuestion ~03:40 on 2026-09-10, on fields 3-5 (`levelCurve`,
> `visibilityRule`, `expiry_behavior`).

Fields 1-2 (`unlockMethod`, `requiresHero`) are already WIRED and shipped in `4f2698b86` —
*"fix(data): WO-1430 Group B - unlockMethod gates achievement grants, requiresHero gates daily
quests"* (verified `git log --oneline -1 4f2698b86` and `git branch --contains 4f2698b86` → `dev`,
both run 2026-09-10). This ruling covers only the remaining three.

#### THE MAP — every place the three field names appear (grepped at source 2026-09-10)

**⚠ Read this before scoping the lane: only ONE of the three is authored in a canonical JSON at all.**
`grep -rn "visibilityRule\|expiry_behavior" Assets/Resources/Data Assets/Data --include=*.json`
returned **nothing** on 2026-09-10, so "remove from the catalog JSON" is a no-op for those two — for
them the drop is the DTO declaration.

| Field | Authored JSON | DTO declaration | Regression / pin references |
|---|---|---|---|
| `levelCurve` | `Assets/Resources/Data/Canonical/echoes-balance.json:8` **and its twin** `Assets/StreamingAssets/Data/Canonical/echoes-balance.json:8` (both `"levelCurve": "linear"`) | `Assets/_Modules/Village/Harvest/EchoBalanceCatalog.cs:74` | `Assets/Editor/Regression/AuthoredFieldReaderRegression.cs:132` (`ParkedClaims`), `:155` (`MechanicalClaims`), `:223` (`UnreadBaseline`) |
| `visibilityRule` | **none** — not authored in any canonical JSON | `Assets/_Modules/Core/Data/CardCollectionCatalog.cs:44` | `AuthoredFieldReaderRegression.cs:133` (`ParkedClaims`), `:174` (`MechanicalClaims`), `:254` (`UnreadBaseline`) |
| `expiry_behavior` | **none** in canonical JSON; it IS present as a **server-payload string inside a test fixture**: `Assets/Editor/Regression/CardCollectionFoundationRegression.cs:55` (`""expiry_behavior"":""fallback""`, twice) | `Assets/_Modules/Core/Data/CardCollectionCatalog.cs:107` | `AuthoredFieldReaderRegression.cs:134` (`ParkedClaims`), `:181` (`MechanicalClaims`), `:210` (`UnreadBaseline`) |

Nothing else in `Assets/_Modules` or `Assets/Data` references any of the three
(`grep -rn "levelCurve\|visibilityRule\|expiry_behavior" Assets/_Modules Assets/Data --include=*.cs`
returns exactly the three DTO lines above; a broader sweep over `Assets/` found no further hits).

#### THREE CONSTRAINTS THE IMPLEMENTING LANE MUST HONOUR

1. **The DTO deletion and the `UnreadBaseline` row deletion MUST land in the same edit.**
   `AuthoredFieldReaderRegression.cs:242-246` states that **case D FAILS on a baseline entry whose
   declaration no longer exists** ("this list cannot rot into ghosts"). Dropping the field without
   dropping its baseline row turns the suite red; dropping the row without dropping the field puts the
   finding straight back. All three rows per field — `ParkedClaims`, `MechanicalClaims`,
   `UnreadBaseline` — go with the declaration.
2. **The `echoes-balance.json` edit is BINARY-ONLY and it is a TWIN edit.** Both copies carry
   `levelCurve` on line 8; a text-mode rewrite has flattened canonical JSON in this repo before. Patch
   from HEAD bytes and prove the LF count is unchanged (minus the removed line) on both files.
3. **Dropping `ExpiryBehavior` from the DTO is safe under Newtonsoft's default `MissingMemberHandling`
   (Ignore).** `grep -n "MissingMemberHandling\|JsonSerializerSettings"
   Assets/_Modules/Core/Data/CardCollectionCatalog.cs` returned **no hits** on 2026-09-10, so no strict
   setting is in force and the server (and the `CardCollectionFoundationRegression.cs:55` fixture) may
   keep sending the key without throwing. The fixture string may be left as-is — it is a server payload,
   not an authored catalog field — but if the lane prefers to strip it, that is cosmetic, not required.

**Re-add rule (the owner's own clause):** each field comes back the day a system needs it — i.e. the
day a production READER exists. Re-adding a declaration with no reader re-creates the exact finding
these oracles were built to catch.

---

## 1. Why this ticket exists

394 suites were green while 7 of 9 troops were unreachable. Every oracle asked *"does this system do its job?"*; none
asked *"can a player get here?"*. These two oracles ask the second question, and the first time they ran they found
eight things. **That is the oracles working, not failing.**

⚠ **None of these was introduced by Wave 0.** They are pre-existing and were invisible until something looked.

## 2. FINDING GROUP A - three panels no player can open

`PanelDoorRegression` proves a door three ways: a production `.cs` outside the panel's own View/VM loop names it, a
`[RuntimeInitializeOnLoadMethod]` bootstrap installs it, or a scene/prefab serialises its **script GUID**. (GUID, not
class name - Unity serialises components by guid, and a name grep proves nothing. An earlier investigation made exactly
that mistake and had to retract it.)

| Panel | Verdict | Evidence |
|---|---|---|
| **BarracksPanel** | no door at all | guid `b245a5682900ee14cbff23be363845d3` appears in no scene or prefab; only its own VM names it. **This is the defect that stranded 7 of 9 troops** - see OWNER_RULINGS_LOCKED §21. |
| **ShopPanel** | harness-only | guid `f4540a733986bf0478c8ec76a15fa527`; constructed ONLY by `AutoPilotDriver.cs` and `UICaptureLaunch.cs`. **A harness that AddComponents every panel so it can be photographed is not a door.** |
| **TalentTreePanel** | no door at all | guid `2490ba9b7d648424dafd1c61ba916a57`. Superseded by `HeroSkillTreePanelMvvm`; `DialogueCommandSink.cs:104-106` re-pointed `OpenTalents` to `PanelId.HeroSkillTree` and REMOVED the legacy route. **Its own header still carries an INTEGRATOR NOTE saying to wire the button.** That was never done. |

**Decisions owed (owner or CLI, per panel - do not delete on a whim):**
1. **BarracksPanel** - ruling 21 made it obsolete as a level control. **WO-2009 may want it as the troop DETAIL
   surface.** Reuse it or delete it; do not leave it doorless.
2. **ShopPanel** - `FeatureFlags.cs:152-156` claims the legacy screen opens when `ff.partyshop` is OFF. **That is not
   true of the code** (`DialogueCommandSink.cs:88-93` routes unconditionally to `PanelId.PartyShop`). Either restore
   the flag branch or retire the panel and fix the stale canon (CLAUDE.md section 15).
3. **TalentTreePanel** - the clearest delete. Also UI-Toolkit, which CLAUDE.md section 8 records as not working in builds.

### DECISIONS TAKEN - 2026-09-06, Group A lane. ALL THREE RETIRED.

| Panel | Decision | The evidence that decided it |
|---|---|---|
| **BarracksPanel + BarracksPanelVM** | **DELETED** | `OWNER_RULINGS_LOCKED.md` section 21 point 3 already named them "dead weight" and point 4 routes WO-2008's locked-tile CTA to *"the barracks BUILDING card in BUILD, which already exists and already works. **No new screen.**"* And WO-2009's own body puts troop detail on the Manage Army tab under a View that **"may not ... call BarracksService directly"** - the exact opposite of this panel, which binds a VM that owns `BarracksService`. **WO-2009 loses nothing.** |
| **ShopPanel + ShopVM** | **DELETED** | Restoring the flag branch was rejected as gaming the oracle: it would be a door to a screen `FeatureFlags` itself calls *"two sell bars, no party selection, blank icons"*, on a branch that is dead while `ff.partyshop` defaults ON - and `PanelDoorRegression`'s own header concedes "a door that is itself dead code still counts as a door here". `PartyShopVM` covers gold-priced weapons, armor, accessories AND consumables (`:1052 AddBuyConsumableRow`), so no player capability is lost. The stale `FeatureFlags` claim is corrected in the same change. |
| **TalentTreePanel** | **DELETED** | Superseded by `HeroSkillTreePanelMvvm`; `DialogueCommandSink` re-pointed `OpenTalents` to `PanelId.HeroSkillTree` and removed the legacy route. UI-Toolkit (CLAUDE.md section 8). Nothing in the tree constructed it. |

**No `PanelId` member was touched.** Verified: there is no `PanelId.Shop`; `BarracksPanel` opened via
`BarracksPanelVM.ResolveOrCreateHost`, never the router; `PanelId.HeroTalents = 0` was already retired-with-comment
and stays at its value. `JobKind.BarracksUpgrade` and `GameState.BarracksLevel` are **persisted save keys and were
left alone** - `BarracksProgression.ApplyBarracksUpgrade` is kept for the same reason (a pre-existing save can still
hold a queued job whose completion effect must resolve).

**All three allowlist entries were DELETED from `PanelDoorRegression`; that allowlist is now empty.**

**Coverage lost, recorded not hidden:** `UICaptureLaunch`'s `RealmGoldStore` / `RealmStorePurchase` captures were the
only headless assertion that a buy moves gold by exactly the total price and inventory by exactly the quantity.
`PartyShopVM` exposes no `Quantity`/`TotalPrice`, so they could not be re-pointed. **Follow-up owed: an equivalent
PartyShop purchase proof.** By contrast the AutoPilot `AssertVendorContracts` and `AssertEconomyDeduct` phases WERE
re-pointed onto `PartyShopPanelMvvm` (two passthrough properties added), so they now exercise the shop the player
actually opens.

## 3. FINDING GROUP B - five authored fields no production code reads

`AuthoredFieldReaderRegression` measured **463** authored `[JsonProperty]` string fields; **58** have no production
reader (12.5%), and these **5** make a MECHANICAL claim - a promise about behaviour that nothing implements. Editor and
test readers deliberately do not count: a suite proving a string is well-formed proves nothing about the game honouring it.

| Field | Where | The broken promise |
|---|---|---|
| `unlockMethod` | `Cosmetics/CosmeticCatalog.cs` | **All 37 cosmetics author `"achievement"`.** Nothing gates a cosmetic on an achievement and nothing routes the other kind to a purchase. |
| `levelCurve` | `Village/Harvest/EchoBalanceCatalog.cs` | authors `"linear"`; `EchoBonusCalculator` never asks. Authoring `"exponential"` tomorrow would change nothing. |
| `requiresHero` | `Core/Quests/DailyQuests.cs` | **A half-built gate** - its sibling `requiresFeature` IS read and IS authored (`"raids"` x2). This one has no reader anywhere. |
| `visibilityRule` | `Core/Data/CardCollectionCatalog.cs` | a show/hide rule the client cannot apply. |
| `expiry_behavior` | `Core/Data/CardCollectionCatalog.cs` | authored `"fallback"`; every expiry behaves identically regardless. |

**The `unlockMethod` one is the most player-visible:** every cosmetic in the game says it is earned by achievement, and
no achievement grants any of them.

**Decision owed per field:** implement the reader, or retire the field and the copy that implies it. **Do not simply
delete the field to silence the oracle** - that discards the design intent someone authored. Retiring one is a
deliberate act that should be recorded.

## 4. How each is parked, and what happens next
- Group A: **RESOLVED 2026-09-06** - all three panels retired, all three allowlist entries deleted, the allowlist is
  empty. See the decision table in section 2. Group B is still parked.
- Group A (as parked, for the record): named in `PanelDoorRegression`'s `Allowlist`, each entry dated and pointing here, stating what retires it.
- Group B: named in `AuthoredFieldReaderRegression`'s exemption set, same discipline.
- **Both oracles still FAIL on anything NEW.** A fourth doorless panel or a sixth unread mechanical field fails the
  build immediately. The exemptions are a ratchet, not an amnesty.
- Every entry names the condition that removes it, so the list cannot quietly become permanent.

## 5. Acceptance
- [ ] Each of the 8 is resolved - implemented, wired, or deliberately retired with the canon corrected in the same change.
- [ ] Its exemption entry is DELETED as part of that change. An exemption outliving its finding is the rot this ticket
      exists to prevent.
- [ ] `REGRESSION_OK n/n` with both oracles green and no exemptions remaining.

## 6. The finding beneath the findings
Five capabilities were found on 2026-09-06 that are built, correct, and unreachable: the barracks panel, the
village-tier control, the talent tree panel, move/sell on a placed structure, and the wood/iron/stone granted on every
kill and never shown. **`PanelDoorRegression` catches only the panel-shaped ones.** A capability behind a MODE (move/sell)
or with no surface at all (the kill drops) is invisible to it.
**Proposed sibling oracle, not yet written: every player-facing verb the code implements has at least one reachable
affordance.** That is the general form, and it is the one that would have caught all five.
