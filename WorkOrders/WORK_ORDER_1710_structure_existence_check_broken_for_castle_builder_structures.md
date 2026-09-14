# WORK ORDER 1710 - Structure existence check broken for castle-builder-seeded structures

**Status:** READY TO IMPLEMENT - owner felt-report, RCA lane assigned
**Minted:** 2026-09-14 by the CLI lead (Fable seat), from the owner's live Firebase tester-build report
(release 2026.09.14.369302, dev @ 518e7f29a)

## 1. Owner report, verbatim (2026-09-14, playing the tester build)

> "everything loads properly on a new build however there's things that aren't working right so the
> items are all placed and they all look correct but when I go to the build menu, it's saying that I
> can't make troops because it says I don't have a barracks, but I do see I have barracks so I tried to
> remove the barracks and re-add barracks to see if I can get it to understand what was happening but
> once I removed it, I still didn't have any chance to re-add it so there's something wrong with being
> able to get your army ready for raids also on every single structure. That's there. We need to ensure
> that if it exists that it's marked as built so that you can't place another one on the Singleton ones
> so I was seeing things like it was telling me to add an iron mine and a crystal mine or something like
> that and those don't need to be there if they have no reason if they if they ever exist, we need to
> look at the structures that the castle builder already adds and make sure that we're not duplicating
> this"

## 2. Two symptoms, one suspected shared cause

1. **Troop training blocked.** Player has a barracks placed and visible, but the build/train UI reports
   no barracks and refuses to queue troops. Removing the barracks and re-placing it did NOT restore the
   ability to build one back (the re-add itself failed or silently no-opped).
2. **Duplicate singleton offers.** The build menu offers structures (named: iron mine, crystal mine) that
   the castle builder already seeded on the town, as if they were never built. This should never happen
   for a singleton structure already present.

**Working hypothesis (UNPROVEN - instrument before touching code, CLAUDE.md section 12):** both symptoms
read as the same failure shape - "does structure X already exist on this town" returning false for
structures that were placed by the castle-builder path rather than the interactive build-and-complete
path. If there is one census/registry that both (a) gates troop training on "barracks exists" and
(b) gates the build menu's singleton offer list, a registration gap in the castle-builder seeding code
would explain both symptoms in one root cause. This is a hypothesis to test, not a diagnosis - do not
patch either symptom independently before the RCA lane proves or disproves the shared cause.

## 3. Where to start looking (starting points, not conclusions)

- `docs/MASTER_CATALOG.md` / the relevant `docs/MASTER_CATALOG/<area>.md` section for the build/structure
  system - read first per CLAUDE.md's mandatory first step.
- Castle/town seeding: `CastleHubBuilder.cs` (referenced elsewhere in canon as the place that writes the
  `SpawnPoint`-tag-style historical bugs) and any `OwnedTown*` / `RealmStorePlacer` / structure-placement
  editor or runtime code that seeds structures onto a fresh or loaded town.
- Whatever answers "is structure type T built" for (a) the troop-training gate and (b) the build-menu
  singleton filter - these may or may not be the same function; find both call sites and compare.
- `everBuiltStructureIds` is named in `CLAUDE.md` section 8 (save schema history, WO-834 "blank-town
  baked standdown") - check whether castle-builder-seeded structures are being written into that set at
  all, since a missing write there is exactly the shape of both symptoms.
- The barracks re-add failure specifically: after removing a barracks, does the build menu let you
  select "Barracks" again at all, and if selected does placement succeed - trace with FlowTrace, don't
  guess from reading code alone.

## 4. Instrumentation-first requirement (CLAUDE.md section 12, BINDING)

No code edit until the RCA lane can cite captured data (FlowTrace lines, a headless repro, or a save-file
diff) that pinpoints where the existence check reads false for a structure that is actually present. If a
headless repro is not immediately available, add FlowTrace Step/Warn/Fail at each existence-check call
site named above, run a scene that mirrors the tester build's start state (fresh town + castle-builder
seeding), and read the trace before writing a single fix line.

## 5. Acceptance criteria

- [ ] RCA lane names the exact function(s) and file:line that answer "does structure T exist" for both
      (a) the troop-training/barracks gate and (b) the build-menu singleton offer list, with a citation
      proving each is currently reachable/called.
- [ ] RCA lane proves, with captured data, why a castle-builder-seeded barracks reads as absent to (a),
      and why castle-builder-seeded iron mine / crystal mine (or whichever singletons) read as absent to
      (b). Confirm whether it is the SAME check or two independent checks with the same bug.
- [ ] RCA lane explains the failed re-add: after removing a barracks, what state should allow placing a
      new one, and where does that state fail to reset.
- [ ] Fix (separate implementation lane once RCA lands) must not touch `Village.unity` or any curated
      scene by hand (CLAUDE.md section 3) and must add/extend a headless regression that builds a fresh
      castle-builder town and asserts: barracks-exists reads true, troop training is available, and no
      singleton the castle builder seeded appears in the build-menu offer list.

## 6. What NOT to touch

- Do not guess-fix the barracks gate or the singleton filter independently before the shared-cause
  question in section 5 is answered - a fix to one call site while the other still reads the stale/wrong
  state would look done and not be.
- Do not hand-edit any `.unity` scene.
