# WO-1498: the loading screen renders the RETIRED tagline, and the banned-copy suite cannot see .cs literals

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `Assets/_Modules/Core/UI/VillageLoadOverlay.cs` + `GlossaryRegression` + `CanonStrings` comments.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1498 -> 1499 in the same edit).

## 1. EVIDENCE

```
VillageLoadOverlay.cs:65   "Hold the last light."   -- in the rotating lore array
```

That string was RETIRED as the tagline on 2026-07-24 (CLAUDE.md sec.7; the live tagline is "Echoes of a
Forgotten Civilization"). It is on the first screen every player sees.

The suite that owns banned copy lists it and structurally cannot see it:

```
GlossaryRegression.cs:77      BannedInPlayerCopy includes the string
GlossaryRegression.cs:62-66   Case6 scans ONLY glossary.json + guide-content.json
```

`CanonStrings.cs:42` and `:84` doc-comments still quote it as well.

## 2. FIX SHAPE

- Remove the line from the `VillageLoadOverlay` lore array.
- Widen `GlossaryRegression` Case6 to scan UI SOURCE LITERALS under `Assets/_Modules/**/*.cs`, not just the two
  JSON files. That is the durable half - the retirement was three months ago and no oracle could see it.
- Fix the two `CanonStrings` doc-comments in the same commit (canon-in-the-same-breath, sec.15).

## 3. WHAT NOT TO DO
- Do not replace it with the new tagline in the lore rotation. The rotation is lore lines; the tagline belongs
  on the title, not among them. If a replacement line is wanted that is the owner's copy call.

## 4. ACCEPTANCE
- [ ] Zero hits for the retired string outside dated frozen ledgers (grep pasted).
- [ ] Case6 scans .cs literals; RED proof stated by re-adding the string locally.
- [ ] A fresh loading-screen capture opened.
- [ ] `REGRESSION_OK n/n` on a fresh log.
