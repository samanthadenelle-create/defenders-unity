# Localization key naming

Localization keys are permanent semantic identifiers. English wording can change; a key changes only when
the meaning or translation context changes.

## Shape

Use lower-case dotted segments:

```text
<domain>.<surface>.<meaning>[.<variant>]
```

Examples:

- `jeweler.ftue.title`
- `jeweler.polish.duration`
- `raid.queue.position`
- `settings.language.system_default`
- `feedback.rate_us.dismiss`

Use `snake_case` inside a segment when multiple words are unavoidable. Do not use the current English
sentence, a scene hierarchy, or a numeric database ID as the key.

## Meaning and reuse

- Reuse a key only when meaning, tone, grammar, and translator context are identical.
- Give the same English word separate keys when it performs different jobs. A verb on a button and a noun
  in an inventory heading may translate differently.
- Prefer one complete sentence over concatenated fragments.
- Use a named Smart String argument for changing values: `raid.queue.position = Position {position}`.
- Use plural/select formatting instead of branching into English fragments in C#.
- Numbered variants are acceptable for interchangeable authored lines, for example
  `victory.wave.variant_1`; do not use numbers to hide distinct meanings.

## Collections

`GameStrings` is the default collection for shared UI copy. Add a domain collection only when content
volume or a separate authoring workflow warrants it:

- `Dialogue` for authored conversations and ambient lines;
- `Items` for names and descriptions maintained with the item catalog;
- `Quests` for objectives and quest narrative;
- `Legal` for versioned policy copy.

Do not create a collection per screen.

## C# usage

C# constants may hold keys, not English copy:

```csharp
public const string PolishDuration = "jeweler.polish.duration";
```

English belongs in the English string table. During the compatibility migration, an existing
`LocalText.Get(key, englishFallback)` call may remain until its key is verified in the table. New code
should not add a second English source unless the transition owner explicitly records it in the manifest.

## Protected and non-player strings

Do not localize save keys, analytics events, object names, resource paths, URLs, protocol values, wallet
addresses, or developer logs. Proper names require an explicit product decision. Record a protected name
as `nonTranslatable` with a reason rather than silently omitting it.

Legacy camel-case keys remain valid until an atomic migration updates the table, call sites, saved
references, and regression coverage. The manifest scanner never renames keys.

## Review checklist

- The key describes meaning rather than English wording.
- Translator context identifies the screen, speaker, tone, and action.
- Arguments are named, typed, and documented.
- The string is complete and does not depend on neighboring fragments.
- A reused key has truly identical context.
- Accessibility text has a key even when the visual uses an icon.
- The manifest disposition and owner are current.
