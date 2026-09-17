# WO-1761 — the raid victory screen still recruits a companion who does nothing

**Status:** CLOSED 2026-09-17 - owner felt-test PASS (validated 2026-09-17T18:18:59, build 2026.09.17.373943). PRIOR STATUS: FIXED - gated 2026-09-15 22:00 (COMPILE_GATE_OK, REGRESSION_OK 547/547, SINGLEHERO_VICTORY_JOIN_OK); awaiting the owner felt test on the next APK
**Silo:** Raid / end-state presentation

---

## The owner's words (felt-test, 2026-09-15)

> "They still say things like Sylas joined the team or Grom joined the team. It doesn't make
> any sense why they join the team if they don't offer any benefit."

## The ruling

**DROP THE JOIN.** Gate the recruit + the banner line behind the single-hero flag, and remove the
dead companion portrait from the raid deploy screen. The victory text keeps
**"The base is CLAIMED"** — only the second line goes.

---

## RCA (every line re-read at source 2026-09-15)

The join is not cosmetic drift: a companion is really enrolled into the persisted party, and then
every consumer of that party throws them away, because the SINGLE-HERO pivot shipped on the
*consumer* side only.

**The recruit sites — the three victory controllers, and they are the only three that gate nothing:**

| File | Recruit call | Text it feeds |
|---|---|---|
| `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs:282` | `newClaim ? UnlockNextCompanion() : null`; body `:838-866`, `svc.AddToParty` `:858` | `EndStateVM.FromRaidVictory` |
| `Assets/_Modules/Village/World/Camps/OutpostVictoryController.cs:196` | same shape; body `:209-238`, `AddToParty` `:230` | `EndStateVM.FromOutpostVictory` (`:245-254`) |
| `Assets/_Modules/Village/World/Camps/Village2RaidController.cs:253` | same shape; body `:275-299`, `AddToParty` `:291` | an INLINE banner string, `:355-357` |

`git grep -n "AddToParty("` over `Assets/_Modules` (run this session) returns exactly nine hits: the
three above, the service's own definition and internal call (`Core/State/GameStateService.cs:1038,
:1079`), two tutorial/dialogue paths (`Village/NPCs/ElaraWaveThreeJoin.cs:185`,
`Village/Tutorial/DialogueCommandSink.cs:130`) and two comment mentions. **The tutorial paths are
deliberately OUT of scope for this WO** — they are a different beat and the owner named the raid one.

**The strings — `git grep "joins your party"` returns exactly three, all covered here:**
`EndStateVM.cs:424`, `EndStateVM.cs:686`, `Village2RaidController.cs:356`.

**Why the join buys the player nothing.** `FeatureFlags.SingleHero`
(`Assets/_Modules/Core/FeatureFlags.cs:40`) **defaults ON**, and every consumer already honours it:

- `Assets/_Modules/BattleATB/BattleController.cs:311-316` — the ATB party is the hero alone.
- `Assets/_Modules/Village/NPCs/StoryCompanionInjector.cs:149-160` — no companion body is spawned.
- `Assets/_Modules/Village/NPCs/PartyHudBridge.cs:60-66` — companion slots 1..3 are hidden.
- `Assets/_Modules/Core/HudModel/HudModelProducers.cs:280, :320` — no companion rows.

So a recruited companion is invisible, absent from combat and absent from the HUD. **The one
surviving surface that still draws them** is the raid deploy screen:
`Assets/_Modules/Village/Hero/RaidDeployVM.cs:771-783` (`BuildPartyClasses`) feeds
`Assets/_Modules/Village/Hero/RaidDeployScreen.cs:588-638` (`BuildPartyRow`), which paints a
portrait plate + a canon name per class. That is the "dead companion portrait" in the ruling.

---

## The change

1. **The three victory controllers** — when `FeatureFlags.SingleHero` is ON, skip the recruit
   entirely and pass `null` for the joined name. The recruit is the thing dropped, not just the
   sentence: enrolling into the persisted roster and then hiding them everywhere is what makes the
   save disagree with the screen. Each gate emits one `FlowTrace.Step("Raid", ...)` naming the
   decision, in the file's existing `[Flow:Raid]` system tag. **The flag-OFF path is untouched** —
   `UnlockNextCompanion()` is not deleted, and with `ff.singlehero=0` the full join returns.
2. **`RaidDeployVM.BuildPartyClasses`** — under SingleHero, the hero only, mirroring
   `PartyHudBridge.cs:60-66`. It is the documented "ONLY resolution site", so the filter has one
   owner. Promoted `private static` -> `public static` so the regression can call it as an oracle
   without standing a screen up. **`RaidDeployScreen.BuildPartyRow`** trims to the first entry as
   defence-in-depth (the VM has a public constructor that bypasses the resolution site).
3. **`EndStateVM`** — NO CHANGE NEEDED, verified at source: `:423-424` and `:685-686` already
   build the second line only `if (!string.IsNullOrEmpty(joinedCompanionName))`, so a null name
   yields "The base is CLAIMED - it is yours now." / "The outpost is yours." exactly as ruled.
   The regression pins that behaviour so it cannot regress.

## Files touched

- `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs`
- `Assets/_Modules/Village/World/Camps/OutpostVictoryController.cs`
- `Assets/_Modules/Village/World/Camps/Village2RaidController.cs`
- `Assets/_Modules/Village/Hero/RaidDeployVM.cs`
- `Assets/_Modules/Village/Hero/RaidDeployScreen.cs`
- `Assets/Editor/Regression/SingleHeroVictoryJoinRegression.cs` (new)
- `Assets/Editor/Regression/DataRegression.cs` (ONE registration line)

**What NOT to touch:** `EndStateVM.cs` (already correct), `UnlockNextCompanion`'s body in any
controller (the flag-OFF path), the tutorial join paths (`ElaraWaveThreeJoin`,
`DialogueCommandSink`), and the four `FeatureFlags.SingleHero` consumers listed above.

## Acceptance

- [ ] SingleHero ON (default): a raid / outpost / Village2 clear enrols NOBODY — `PartySize` is
      unchanged across the win — and no victory surface prints "joins your party".
- [ ] The victory text still reads "The base is CLAIMED - it is yours now."
- [ ] The raid deploy screen's party row shows exactly ONE portrait (the hero).
- [ ] `ff.singlehero=0` restores the recruit, the banner line and the multi-portrait row.
- [ ] `SINGLEHERO_VICTORY_JOIN_OK` on a fresh log; `REGRESSION_OK <n>/<n>` with the suite counted.
- [ ] Owner felt-verifies a raid clear on device and closes (PO closes, not CLI — CLAUDE.md §13).

## Regression

`Assets/Editor/Regression/SingleHeroVictoryJoinRegression.cs` — tag `[singlehero-victory-join]`,
markers `SINGLEHERO_VICTORY_JOIN_OK` / `SINGLEHERO_VICTORY_JOIN_FAIL`. `git grep` proved both the
tag and the markers were unused before this WO. Registered once in `DataRegression.RunAll`.
