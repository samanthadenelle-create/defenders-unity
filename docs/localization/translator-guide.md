# Translator guide

Defenders of the Realm uses stable keys and English source text. Translate meaning and player intent,
not the literal structure of the English sentence.

## What each work item must provide

Every translatable entry should include:

- its stable key and collection;
- English source text;
- the screen or gameplay surface;
- speaker, tone, and player action where relevant;
- argument names and types;
- a screenshot for context-sensitive or space-constrained copy;
- maximum expected expansion and any genuine layout constraint.

If context is missing or the English could mean two things, mark the entry blocked and ask the owning
module. Do not guess based on the key alone.

## Variables and formatting

Text inside braces is data supplied by the game. Preserve the argument and its spelling. Move it to the
grammatically correct place in the translation.

```text
English: Polishing takes {minutes} minutes.
```

Do not translate identifiers such as `minutes`. Do translate surrounding words and use the locale's
plural rules. Dates, times, numbers, currency, gender/select forms, and quantities must use the supplied
Smart String format rather than English punctuation or word order.

Do not build sentences by joining translated fragments. Report a fragmented source to the localization
owner so it can become one complete entry.

## Tone and terminology

- Preserve the distinction between gameplay instructions, character dialogue, errors, and legal copy.
- Keep instructions direct and make the intended action unmistakable.
- Preserve approved character, place, currency, and product names according to the project glossary.
- Do not invent abbreviations solely to fit a control. Layout should normally accommodate expansion.
- Retain intentional rich-text tags and line breaks only when their function is documented.

## Images and accessibility

Object art may replace a repeated visual noun, but it does not replace a localized action or accessible
label. Prefer text-free art with live localized text. If words are inseparable from approved branded art,
the item needs one Asset Table variant per shipped locale and an entry in the image-text approval list.

## Player-authored text

Player names, wallet addresses, chat, and free-form feedback are data. Do not translate or normalize them.
Automatic translation of player content requires a separate privacy and moderation decision.

## Linguistic QA

Spreadsheet review is not final approval. Verify translations in a game build using representative phone,
Seeker landscape, desktop, and portrait layouts where supported. Check:

- clipping, overlap, and ellipses that hide required meaning;
- font fallback and missing glyphs;
- plural, number, date, and argument substitution;
- button meaning and touch-target integrity;
- right-to-left shaping and order when that locale is in scope;
- fallback behavior when an entry is intentionally absent.

Pseudo-localization runs before a human-language pilot. It accents characters, expands text by roughly 40%,
and exposes concatenation, missing keys, fixed-width layouts, and incomplete font coverage.

## Manifest states

- `migrated`: keyed and resolved through the localization authority;
- `review`: scanner candidate awaiting human classification;
- `nonTranslatable`: intentionally protected, with a reason;
- `approvedAssetVariant`: text-bearing art handled through localized assets;
- `blocked`: cannot proceed without a named owner decision.

The scanner is report-only. It never rewrites source code and its literal candidates are leads, not proof
that a string is player-facing.
