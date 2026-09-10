# WO-1088 — Four-model review of AI localization first drafts

**Status:** CLOSED 2026-09-10 - owner, verbatim: "1088 was completed and results fed back and be closed not just fixed i validated" - the four-model localization draft review was done and validated by the owner; closed on her word. (was: READY FOR EXTERNAL REVIEW (not a code-implement ticket))
**Minted:** 2026-09-08 — Grok/UI seat
**Purpose:** Confirm or disprove the AI first-draft translations before CLI wires them into the game.

---

## 0. Folder contents (this package)

| File | Role |
|---|---|
| `en.json` | **English source of truth** (do not rewrite keys) |
| `es.json` | Spanish draft |
| `pt-BR.json` | Portuguese (Brazil) draft |
| `de.json` | German draft |
| `fr.json` | French draft |
| `ru.json` | Russian draft |
| `ja.json` | Japanese draft |
| `ko.json` | Korean draft |
| `zh-Hans.json` | Chinese Simplified draft |
| `ar.json` | Arabic (MSA) draft |
| `PASTE_TO_MODEL.md` | **Copy/paste this prompt** into GPT / Claude / Gemini / Grok |
| `SAMPLE_KEYS.txt` | Suggested 40-key sample for a fast pass |
| This work order | Context for humans / CLI |

All drafts claim: **AI FIRST DRAFT — NOT linguistic QA.**

---

## 1. Your job (as a reviewing model)

You are one of **four** independent reviewers. Do **not** invent new keys. Do **not** change the English source.

For each sampled key:

1. Read English from `en.json`.
2. Read the same key in the target locale file.
3. Score:
   - **OK** — natural fantasy-game UI tone; meaning matches; placeholders intact; lore names intact.
   - **REVISE** — usable but awkward / slightly wrong; propose a better string.
   - **BLOCK** — wrong meaning, broken placeholder, or unsafe for ship.

### Hard rules (fail → BLOCK)

- Placeholders must match English **exactly**: `{0}`, `{1}`, `{Language}`, `{minutes}`, `{Minimum}`, etc. Do not translate the token inside braces.
- Keep lore proper nouns in English unless the draft already used a clear standard form:
  `Elarion`, `Keeper`, `Heart`, `Spire`, `Hollowed`, `Hollow Ones`, `Alduin the Mournful`, `Syndrath the Devourer`, `Wardens`, `Folk`, `Chord`, `Lantern`, `Withering`.
- Do not drop rich meaning from tutorial / death / victory lines just to shorten.
- JSON must remain valid if you propose replacements (escape quotes).

### Soft rules (fail → REVISE)

- Sounds like machine translation / calque.
- Wrong register (too slangy or too biblical vs English tone).
- UI labels that will clearly overflow (especially `hud*` and `settings.*`) — note expansion risk.

---

## 2. Output format (required)

Return a markdown table, then a summary.

```markdown
## Reviewer: <model name>
## Locale: <code>   (repeat section per locale you review)

| Key | Score | Notes | Proposed fix (only if REVISE/BLOCK) |
|---|---|---|---|
| title.tagline | OK | | |
| settings.title | REVISE | calque | <better string> |

### Summary
- OK: N
- REVISE: N
- BLOCK: N
- Placeholder violations: N
- Lore-name violations: N
- Ship this locale for playtest wiring? YES / NO / YES_WITH_FIXES
```

If reviewing **all** locales, one section per locale. If time-boxed, use `SAMPLE_KEYS.txt` only and say so.

---

## 3. Pass / fail for the pack

| Outcome | Meaning |
|---|---|
| **Confirm** | ≥3/4 models say YES or YES_WITH_FIXES for a locale (after applying agreed fixes) |
| **Disprove** | ≥2/4 models BLOCK critical keys (title, settings, tutorial, hud) or find systemic placeholder damage |

CLI should **not** register a locale that is Disproved until fixes land.

---

## 4. Out of scope for reviewers

- Fonts / TMP tofu / RTL layout (flag only: “needs font” / “needs RTL”).
- Wiring Unity Localization / `LocalizationBuilder`.
- Changing C# or inventing new keys.

---

## 5. Paste for CLI (after human consolidates 4 reviews)

```text
After WO-1088 four-model review: apply agreed REVISE/BLOCK fixes to Canonical dual-copy locale JSON,
then register surviving locales and import via LocalizationBuilder into GameStrings_{locale}.
Do not invent a second string system. Font/RTL are separate tickets.
```
