# PSEUDOLOC ALLOWLIST — the confirmed-legitimate Latin dictionary

**Owner:** WO-1861 (the pseudolocalization leak-detection harness).
**Read by code, not just by humans:** `DeNelle.Editor.Regression.PseudolocLeakOracle.Allowlist.Load`
parses the fenced `allowlist` blocks below. A parse error, a missing file, or an entry with no
reason is a **suite FAILURE**, never a silently-empty allowlist — an allowlist that fails open
suppresses everything and the oracle would report a clean sweep over an unchecked game.

## What this file is for

With pseudolocalization active, every string resolved through `LocalText` is rewritten to
Cyrillic. Anything that still reads as Latin on screen is one of exactly two things:

1. **A localization LEAK** — a hardcoded literal that never went through `LocalText`. That is the
   thing WO-1857 and its sharded lanes exist to fix, and the thing the oracle must report.
2. **Legitimately Latin in every language** — a brand, a player-chosen name, a generated code, a
   wallet address fragment, a raw enum/data identifier that is correctly *not* copy. That is what
   this file holds.

Per this repo's `audit-outputs-as-known-dictionaries` convention, this is a **durable, growing
dictionary**, not a one-off report. Each round of the overnight loop adds the exceptions it
*proved* legitimate, so the next round does not re-discover them.

## ⛔ The one rule that keeps this honest

> **An entry requires a REASON, and the reason must be a finding — not "make the flag go away".**

The parser enforces the mechanics (no `#` reason = the entry is rejected and the suite fails). It
cannot enforce honesty, so: before you add an entry, you must have **opened the cited component's
authoring site** and be able to say why that string is correct in Russian, Japanese and Arabic. If
you cannot, it is a leak and it belongs in a WO-1857 lane, not here.

This is the mirror image of `UICaptureLaunch`'s two baselines (`TouchBaseline`, `GlyphBaseline`),
which are **shrink-only** — a fixed debt being paid down. This list is different in kind and is
expected to **grow**: it is a dictionary of facts about the game's vocabulary, not a suppression of
known defects. Do not copy the shrink-only rule onto it, and do not use it to park a real leak.

## Entry grammar

One entry per line inside a fenced block tagged `allowlist`:

```text
<kind>: <value>   # <reason — a finding, with the site you read>
```

| Kind | Matches | Use it for |
|---|---|---|
| `token` | one Latin **word** (no spaces), case-insensitively **equal** to a flagged run | brand and proper nouns: `Solana`, `SKR` |
| `phrase` | any flagged run on a label whose plain text **contains** this text (case-insensitive) | multi-word brands, where `token` would be far too broad |
| `path` | every run on a label whose **hierarchy path** contains this substring | one specific widget that legitimately shows Latin (a code field, an address row) |
| `screen` | every run on a panel build whose **screen name** contains this substring | a whole capture **fixture** that injects raw English test data, not a real screen |

Rules the parser applies:

- Blank lines and lines whose first non-space character is `#` are comments.
- `#` starts the reason. Everything before it is the value, trimmed.
- A `token` value containing whitespace is **rejected** (use `phrase`), because a multi-word
  `token` would silently suppress each of its words everywhere — `The Night Market` would
  allowlist the word `The` on every screen in the game.
- Matching is case-insensitive throughout: dock captions are authored upper-case
  (`BUILD`/`TALK`/`HERO`), copy is mixed-case, and the same word must not need two entries.

## The runs the oracle flags

A **run** is a maximal sequence of `[A-Za-z]` of length **2 or more**, measured *after* TMP
rich-text tags are stripped. One stray letter is noise (an initial, a units suffix, a `/`-joined
glyph); two consecutive letters is a word. Digits, punctuation and whitespace are never part of a
run, so `12 / 30`, `+5%` and `Lv.3` produce nothing.

---

## Entries

### Brand and proper nouns — correct in every language

These are the names the owner does not translate. A brand appearing *inside* a localized string is
already pseudolocalized and never reaches the oracle; these entries exist for the places a brand is
rendered from a non-localized source (a wallet symbol constant, a store SKU label, a chain name).

```allowlist
token: Solana      # Chain name. Never translated; WO-1861 scope note names it explicitly.
token: SKR         # Solana Mobile's governance token ticker. A ticker is a symbol, not copy (memory: skr-is-solana-mobile-governance-token).
token: Cherry      # Brand name carried verbatim by the owner's own naming canon.
token: Elarion     # The village's proper name (CLAUDE.md sec.7 - "Elarion", never "Avalon").
token: Remnant     # Player-facing clan noun; canon per WO-1859, so a Latin "Remnant" is the intended word, not a leak.
```

### Per-widget exceptions — a specific control that legitimately shows Latin

```allowlist
# (No entries yet.) This section is for `path:` entries covering the widgets that display
# player-chosen hero names, generated Remnant invite codes (e.g. GZL2ED) and wallet-address
# fragments. They are deliberately NOT pre-populated: the hierarchy paths are only knowable from
# a real capture run's report, and inventing one would (a) prove nothing and (b) risk matching a
# path that also carries real copy. Add each with the report line that produced it.
```

### Capture-fixture exceptions — raw English injected by the harness, not by the game

```allowlist
# (No entries yet.) The headless capture harness builds many panels from synthetic fixtures that
# set literal English test data directly on a VM or model - e.g. LocaleSmokeCapture's HUD fixture
# passes "knight" and "Focus" into HeroVitals.Set (LocaleSmokeCapture.cs:330), and
# UICaptureLaunch's reflected-secondary recipe passes "VILLAGE SHOP" to PartyShopPanelMvvm.Open
# (UICaptureLaunch.cs:8446). Those strings are the HARNESS's, so flagging them is a false
# positive - but WHICH fixtures actually surface Latin on screen is only knowable from a run.
#
# ⛔ TRIAGE RULE, so this section cannot become the place leaks go to die: prefer a `path:` entry
# over a `screen:` entry every time. A `screen:` entry blinds the ENTIRE panel - the same mistake
# UICaptureLaunch's GlyphBaseline header calls out at length ("keyed per finding, not per panel
# ... that is how a baseline turns into a blindfold"). Use `screen:` only when the fixture supplies
# every string on the panel, and say so in the reason.
```

---

## ⚠ Known gaps that are NOT allowlist entries

**The 6-locale / 10-locale table gap does not manifest under pseudoloc, so it needs no entry.**
The ticket asks for it to be cited here rather than re-discovered each round, so:

- Six locales have a live Unity String Table asset — `en, es, pt-BR, de, fr, ru`, the explicit list
  at `Assets/Editor/Localization/LocaleSmokeCapture.cs:37`, which the file's own comment guards as
  deliberately explicit ("a stray Locale asset must never silently expand a release gate").
- `ar / ja / ko / zh-Hans / zh-Hant` are reachable as locale codes (`LocalText.CodeFor`,
  `LocalText.cs:267-287`) but have no table, so they fall back to English.
- **Under pseudoloc the selected locale never changes** — that is the entire reason pseudoloc was
  chosen over an 11th synthetic locale. The transform is applied to whatever the table/JSON
  returned, in `en`. So an `ar` screen reading Latin is *not a state this harness can even enter*,
  and there is nothing to suppress. If you find yourself wanting an entry for it, you have changed
  the locale as well as the transform — stop and re-read the ticket.

**Missing-key markers are not leaks either, and are deliberately left un-transformed.**
`LocalText.Get` returns `[[missing:<key>]]` for an absent key and `LocalText`'s post-resolve hook
does **not** touch it, nor does it touch a call-site `englishFallback`
(`LocalText.cs` — see the hook's own comment). Both are *stronger* signals than a leak: the key is
absent from the table. They surface as Latin findings on purpose. Route them to the WO-1857 lane;
do not allowlist them.

---

## How the loop uses this file

1. Run the pseudoloc capture (see the WO-1861 implementation record for the exact command).
2. Read `Builds/ui-capture/pseudoloc-leaks.json` and the `UI_PSEUDOLOC_FAIL` lines in the log.
3. For each finding: open the cited component's authoring site.
   - Hardcoded literal → **fix it** (locale key) in a WO-1857 lane. Not an allowlist entry.
   - Provably language-independent → add the narrowest entry that covers it, with the reason.
4. Re-run for that screen; confirm `UI_PSEUDOLOC_OK`; move on.
