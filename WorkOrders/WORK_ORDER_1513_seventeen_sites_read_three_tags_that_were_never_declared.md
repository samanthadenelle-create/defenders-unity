# WO-1513: seventeen sites read three tags that were NEVER declared - all guarded, all dead forever

**Status:** PARTIALLY IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** Village. Low severity, high clarity value.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1513 -> 1514 in the same edit).

## 1. EVIDENCE

`ProjectSettings/TagManager.asset` declares exactly four tags: `Tower`, `Building`, `HeartTarget`, `Player`.

Seventeen sites read three tags that are not among them - `HeroTarget`, `PetTarget`, `ScreenFlash`:

```
AwarenessSensor.cs:154        PortalVFXController.cs:854
~15 further SafeFindWithTag("HeroTarget") call sites
```

They are all wrapped in the safe finder, so nothing throws - `FindGameObjectsWithTag` on an undeclared tag
throws, which is exactly the WO-1038 defect CLAUDE.md sec.7 records. But that means every one of these
branches has been DEAD since it was written and will remain dead forever.

Seventeen dead fallbacks read as live alternatives to whoever next debugs targeting.

## 2. FIX SHAPE

- Decide per tag: DECLARE it in `TagManager.asset` if the fallback is wanted, or DELETE the branch.
  A GameObject has one tag, so declaring three more targeting tags is unlikely to be right - deletion is the
  expected answer for all three.
- Add a regression that fails on a tag literal not present in `TagManager.asset`. That is what would have
  caught WO-1038 before it shipped.

## 3. WHAT NOT TO DO
- Do not declare the tags "just in case". An undeclared tag that throws is a better state than a declared tag
  nothing sets, because at least the throw is visible.

## 4. ACCEPTANCE
- [ ] Zero reads of an undeclared tag (grep of tag literals vs `TagManager.asset`, pasted).
- [ ] The tag-literal regression exists; RED proof stated by adding a bogus tag read.
- [ ] `REGRESSION_OK n/n` on a fresh log.
