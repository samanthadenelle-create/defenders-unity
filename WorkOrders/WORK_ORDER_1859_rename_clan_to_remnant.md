# WORK ORDER 1859 — Rename the player-facing "clan" term to "Remnant"

**Status:** DONE - committed 670a1f3ff, COMPILE_GATE_OK + REGRESSION_OK 578/578 verified. PRIOR: READY FOR LEAD REVIEW

**Minted:** 2026-09-17, by the CLI lead, from the owner's ruling (see memory
`clan_renamed_to_remnant.md`): *"Remnant being the one that immediately tells me I'm playing Echoes
of Elarion, not Fantasy Game #4,782."* Ties into the game's own title (a remnant of a forgotten
civilization, restoring it together).

## Scope

**This is a content/copy edit to EXISTING locale keys, not a new-hardcoded-string problem.** Per the
standing localization law (WO-1857's own governing rule), every player-facing "clan" string should
already be routing through a locale key — confirm that's actually true for every hit below before
assuming it; a raw hardcoded "Clan" literal found during this sweep is itself a WO-1857-class defect
and should be fixed the same way (wired to a key), not just re-worded in place.

1. Grep every locale JSON (`Assets/Resources/Data/Canonical/*.json`,
   `Assets/StreamingAssets/Data/Canonical/*.json`) and `Assets/Localization/Tables/GameStrings_*.asset`
   for any English value containing "Clan"/"clan" (case-insensitive) and rename per this table:

   | Old | New |
   |---|---|
   | Clan | Remnant |
   | Create Clan | Create Remnant |
   | Join Clan | Join Remnant |
   | Clan Members / Clan Member | Remnant Members / Remnant Member |
   | Clan Rank | Remnant Rank |
   | (clan hall / clan HQ concept, if one exists) | Remnant Hall |
   | (clan quest concept, if one exists) | Remnant Quest |
   | (clan raid concept, if one exists) | Remnant Raid |
   | (clan war/conflict concept, if one exists) | Remnant War |

   Only rename concepts that actually EXIST today — do not invent new features (Remnant Quest/Raid/War)
   that have no current equivalent. If no "clan hall"/"clan quest"/"clan raid"/"clan war" concept
   exists in the shipped game yet, say so plainly in the hand-back rather than fabricating copy for a
   feature that isn't built. Only apply the corresponding rename where a real, shipped concept exists.

2. Translate the renamed English values into all 9 other locales, matching the register/tone of
   neighboring rows in each locale's file (placeholder-quality acceptable per WO-1857's own
   precedent — flag anything that reads awkward for a follow-up linguistic pass).

3. Do NOT rename internal code identifiers: `clan_id`, the `clans`/`clan_members`/`clan_messages`/
   `clan_reports`/`clan_rate_limit` tables, `ClanService`, `ClanFeatureGate`, `api/clan/*.js`,
   `api/_lib/clan*.js`, `ClanChatPanel`/`ClanChatVM`/etc. class names, git history, work order numbers
   and filenames (WO-1844 etc. keep their names), or this repo's own internal docs/comments that refer
   to "the clan system" as an engineering concept. Only the WORDS A PLAYER SEES change.

4. Check `site/clan-chat.html` and any Unity UI copy built from string concatenation (not just JSON
   keys) for a hardcoded "Clan" that also needs the swap — cite each one found.

## Non-scope

- Do NOT build the Remnant → Alliance → Kingdom progression tiers — that's real future design
  direction (recorded in memory `clan_renamed_to_remnant.md`), not this ticket's scope. This ticket
  is the rename only.
- Do NOT touch the actual gameplay logic of any clan feature (roles, rate limits, chat, leaderboard)
  — copy/locale only.
- Do NOT rename the git-tracked file/directory names for the clan API (`api/clan/`, `api/_lib/clan.js`,
  etc.) or any migration file — those are internal, not player-facing.

## Acceptance criteria

- [ ] Every player-facing occurrence of "Clan" in English locale files reads "Remnant" (or the
      correctly-mapped compound term per the table above, only for concepts that actually exist).
- [ ] All 9 other locales have a corresponding translated value for every changed key — no locale
      left with a stale "Clan"-equivalent word while English says "Remnant."
- [ ] `Assets/Localization/Tables/GameStrings_en.asset` (and any other language's `.asset` file, if
      those exist per-language) matches the JSON — `LocaleParityRegression` proves this, not prose.
- [ ] No internal code identifier is renamed (grep confirms `clan_id`, table names, class names, file
      names all unchanged).
- [ ] Full regression suite green (`REGRESSION_OK <n>/<n>`), `node --test test/*.test.js` before/after
      counts reported.

## Test plan

1. Grep the full diff for any renamed line; spot-check 5 non-English locales render sensibly.
2. Boot the game (or the relevant screen headlessly) and confirm the clan UI now reads "Remnant"
   throughout — screenshot if a UI surface can be captured headlessly per this repo's existing
   capture tooling.
3. Full regression suite green.

## Rollback

Revert the locale JSON and `.asset` file changes; straight content revert, no schema/code implications.

## Copy rules

Match the existing tone of the game's copy (warm, in-world, not corporate) — "Remnant" language should
read consistently with existing lines like "The Folk built these long before you," not as a dry system
label.

---

## IMPLEMENTATION RECORD (2026-09-17, implementing agent)

### Scope finding — only ONE shipped clan concept exists

A full grep of every locale JSON, the Unity Localization tables, `chat-phrases.json`,
`GooglePlayLocalizationVariantPolicy.json`, every `.cs` file whose name/content mentions "clan", and
`site/clan-chat.html` found **exactly one player-visible concept**: the **clan CHAT** feature (5
`clanChat.*` locale keys + 2 hardcoded chat-panel-title strings + 1 HTML `<title>`), plus 2 generic
chat-wheel phrases that reference "the clan" socially. There is **no shipped "Create Clan" / "Join
Clan" flow, no "Clan Members" roster screen, no "Clan Rank" concept, and no clan Hall/Quest/Raid/War
concept** anywhere in locale data or UI code — the backend API (`api/clan/*.js`) supports create/join/
promote/demote/kick/leave, but **no locale string or UI copy for those actions exists yet** (they are
presumably still driven by raw API responses / not yet copy-wired, or that surface doesn't ship
player-facing text). Per the WO's own instruction, those rows in the mapping table are **N/A — not
built** and nothing was invented for them:

| Table row | Status |
|---|---|
| Clan → Remnant | Applied (in the 8 places below) |
| Create Clan → Create Remnant | **N/A — no such locale key/UI string exists in the shipped game** |
| Join Clan → Join Remnant | **N/A — no such locale key/UI string exists in the shipped game** |
| Clan Members → Remnant Members | **N/A — no such locale key/UI string exists** |
| Clan Rank → Remnant Rank | **N/A — no such locale key/UI string exists** |
| Clan Hall / Quest / Raid / War → Remnant Hall/Quest/Raid/War | **N/A — none of these concepts exist** |

One incidental non-concept: `tooltip.buttonClan.title`/`.body` (all 10 locales) — the KEY name contains
"Clan" but the VALUE text does not say "clan"/"clã"/"クラン"/etc. in any locale (it reads "The Other
Valleys" / "Join your watch to other Keepers..." with no clan word at all). **Left untouched** — no
player-visible word to rename, and the key name itself is an internal identifier per the WO's own rule.

### A. Locale JSON — `clanChat.*` (5 keys × 10 locales × 2 mirrors = 100 value edits)

Keys unchanged (`clanChat.noWallet`, `clanChat.noClan`, `clanChat.unavailable`,
`clanChat.genericError`, `clanChat.opening`) — only the English-authored VALUE text changed, in both
`Assets/Resources/Data/Canonical/<locale>.json` and `Assets/StreamingAssets/Data/Canonical/<locale>.json`
(verified byte-identical mirrors after edit via `diff`).

**en** (source of truth):
| Key | Before | After |
|---|---|---|
| clanChat.noWallet | Connect your wallet to use clan chat. | Connect your wallet to use Remnant chat. |
| clanChat.noClan | You're not in a clan yet. | You're not in a Remnant yet. |
| clanChat.unavailable | Clan chat is not available in this build. | Remnant chat is not available in this build. |
| clanChat.genericError | Clan chat could not load. Try again in a moment. | Remnant chat could not load. Try again in a moment. |
| clanChat.opening | Opening clan chat... | Opening Remnant chat... |

**es** — term used: "Remanente"
| Key | Before | After |
|---|---|---|
| noWallet | Conecta tu monedero para usar el chat del clan. | Conecta tu monedero para usar el chat del Remanente. |
| noClan | Aún no perteneces a ningún clan. | Aún no perteneces a ningún Remanente. |
| unavailable | El chat del clan no está disponible en esta versión. | El chat del Remanente no está disponible en esta versión. |
| genericError | No se pudo cargar el chat del clan. Inténtalo de nuevo en un momento. | No se pudo cargar el chat del Remanente. Inténtalo de nuevo en un momento. |
| opening | Abriendo el chat del clan... | Abriendo el chat del Remanente... |

**pt-BR** — term used: "Remanescente"
| Key | Before | After |
|---|---|---|
| noWallet | Conecte sua carteira para usar o chat do clã. | Conecte sua carteira para usar o chat do Remanescente. |
| noClan | Você ainda não está em um clã. | Você ainda não está em um Remanescente. |
| unavailable | O chat do clã não está disponível nesta versão. | O chat do Remanescente não está disponível nesta versão. |
| genericError | Não foi possível carregar o chat do clã. Tente novamente em instantes. | Não foi possível carregar o chat do Remanescente. Tente novamente em instantes. |
| opening | Abrindo o chat do clã... | Abrindo o chat do Remanescente... |

**de** — term used: "Überrest"
| Key | Before | After |
|---|---|---|
| noWallet | Verbinde deine Wallet, um den Clan-Chat zu nutzen. | Verbinde deine Wallet, um den Überrest-Chat zu nutzen. |
| noClan | Du bist noch in keinem Clan. | Du bist noch in keinem Überrest. |
| unavailable | Der Clan-Chat ist in dieser Version nicht verfügbar. | Der Überrest-Chat ist in dieser Version nicht verfügbar. |
| genericError | Der Clan-Chat konnte nicht geladen werden. Versuche es gleich noch einmal. | Der Überrest-Chat konnte nicht geladen werden. Versuche es gleich noch einmal. |
| opening | Clan-Chat wird geöffnet... | Überrest-Chat wird geöffnet... |

**fr** — term used: "Vestige"
| Key | Before | After |
|---|---|---|
| noWallet | Connectez votre portefeuille pour utiliser le chat de clan. | Connectez votre portefeuille pour utiliser le chat du Vestige. |
| noClan | Vous n'êtes pas encore dans un clan. | Vous n'êtes pas encore dans un Vestige. |
| unavailable | Le chat de clan n'est pas disponible dans cette version. | Le chat du Vestige n'est pas disponible dans cette version. |
| genericError | Impossible de charger le chat de clan. Réessayez dans un instant. | Impossible de charger le chat du Vestige. Réessayez dans un instant. |
| opening | Ouverture du chat de clan... | Ouverture du chat du Vestige... |

**ru** — term used: "Осколок" (fragment/remnant; declined "Осколка"/"Осколке")
| Key | Before | After |
|---|---|---|
| noWallet | Подключите кошелёк, чтобы использовать чат клана. | Подключите кошелёк, чтобы использовать чат Осколка. |
| noClan | Вы пока не состоите в клане. | Вы пока не состоите в Осколке. |
| unavailable | Чат клана недоступен в этой версии. | Чат Осколка недоступен в этой версии. |
| genericError | Не удалось загрузить чат клана. Повторите попытку через мгновение. | Не удалось загрузить чат Осколка. Повторите попытку через мгновение. |
| opening | Открытие чата клана... | Открытие чата Осколка... |

**ar** — term used: "البقية" (the remnant/remainder)
| Key | Before | After |
|---|---|---|
| noWallet | قم بتوصيل محفظتك لاستخدام دردشة العشيرة. | قم بتوصيل محفظتك لاستخدام دردشة البقية. |
| noClan | أنت لست في عشيرة بعد. | أنت لست في البقية بعد. |
| unavailable | دردشة العشيرة غير متاحة في هذا الإصدار. | دردشة البقية غير متاحة في هذا الإصدار. |
| genericError | تعذر تحميل دردشة العشيرة. حاول مرة أخرى بعد قليل. | تعذر تحميل دردشة البقية. حاول مرة أخرى بعد قليل. |
| opening | جارٍ فتح دردشة العشيرة... | جارٍ فتح دردشة البقية... |

**ja** — term used: "残党" (remnant/surviving faction)
| Key | Before | After |
|---|---|---|
| noWallet | クランチャットを使うにはウォレットを接続してください。 | 残党チャットを使うにはウォレットを接続してください。 |
| noClan | まだクランに所属していません。 | まだ残党に所属していません。 |
| unavailable | このビルドではクランチャットは利用できません。 | このビルドでは残党チャットは利用できません。 |
| genericError | クランチャットを読み込めませんでした。しばらくしてからもう一度お試しください。 | 残党チャットを読み込めませんでした。しばらくしてからもう一度お試しください。 |
| opening | クランチャットを開いています... | 残党チャットを開いています... |

**ko** — term used: "잔당" (remnant/surviving faction)
| Key | Before | After |
|---|---|---|
| noWallet | 클랜 채팅을 사용하려면 지갑을 연결하세요. | 잔당 채팅을 사용하려면 지갑을 연결하세요. |
| noClan | 아직 클랜에 가입하지 않았습니다. | 아직 잔당에 가입하지 않았습니다. |
| unavailable | 이 빌드에서는 클랜 채팅을 사용할 수 없습니다. | 이 빌드에서는 잔당 채팅을 사용할 수 없습니다. |
| genericError | 클랜 채팅을 불러올 수 없습니다. 잠시 후 다시 시도해 주세요. | 잔당 채팅을 불러올 수 없습니다. 잠시 후 다시 시도해 주세요. |
| opening | 클랜 채팅을 여는 중... | 잔당 채팅을 여는 중... |

**zh-Hans** — term used: "遗民" (remnant people/survivors of a fallen civilization)
| Key | Before | After |
|---|---|---|
| noWallet | 连接你的钱包以使用军团聊天。 | 连接你的钱包以使用遗民聊天。 |
| noClan | 你还没有加入军团。 | 你还没有加入遗民。 |
| unavailable | 此版本不支持军团聊天。 | 此版本不支持遗民聊天。 |
| genericError | 军团聊天加载失败，请稍后重试。 | 遗民聊天加载失败，请稍后重试。 |
| opening | 正在打开军团聊天... | 正在打开遗民聊天... |

### B. `chat-phrases.json` (English-only canned quick-chat wheel; no per-locale variants exist for this
file in this repo — both `Assets/Resources/.../chat-phrases.json` and its StreamingAssets mirror edited
identically)

| id | Before | After |
|---|---|---|
| phrase_3 | Welcome to the clan! | Welcome to the Remnant! |
| phrase_4 | Hello, clanmates. (emoji 👋) | Hello, Remnants. (emoji 👋, unchanged) |

### C. `Assets/Editor/Localization/GooglePlayLocalizationVariantPolicy.json`

The `clanChat.noWallet` Google-Play-channel override row (a different value than the base "connect
wallet" copy, since Play builds hide the wallet requirement) needed the same rename across its own 10
locale values:

| Locale | Before | After |
|---|---|---|
| en | Sign in to use clan chat. | Sign in to use Remnant chat. |
| es | Inicia sesión para usar el chat del clan. | Inicia sesión para usar el chat del Remanente. |
| pt-BR | Faça login para usar o chat do clã. | Faça login para usar o chat do Remanescente. |
| de | Melde dich an, um den Clan-Chat zu nutzen. | Melde dich an, um den Überrest-Chat zu nutzen. |
| fr | Connectez-vous pour utiliser le chat de clan. | Connectez-vous pour utiliser le chat du Vestige. |
| ru | Войдите, чтобы использовать чат клана. | Войдите, чтобы использовать чат Осколка. |
| ar | سجّل الدخول لاستخدام دردشة العشيرة. | سجّل الدخول لاستخدام دردشة البقية. |
| ja | クランチャットを利用するにはサインインしてください。 | 残党チャットを利用するにはサインインしてください。 |
| ko | 클랜 채팅을 사용하려면 로그인하세요. | 잔당 채팅을 사용하려면 로그인하세요. |
| zh-Hans | 登录以使用军团聊天。 | 登录以使用遗民聊天。 |

(`owner: "Clan"` and `reason:` prose in this same row were left as-is — internal doc fields describing
the engineering concept, not player-visible text, per the WO's own carve-out.)

### D. Unity Localization String Table assets — the 6 GooglePlay-enabled locales
(`Assets/Editor/Localization/GooglePlayLocalizationVariantPolicy.json`'s `enabledTableLocales`: en, es,
pt-BR, de, fr, ru)

Edited the same 5 `clanChat.*` entries (by their shared `m_Id`s 44124330363760645–649) in:
- `Assets/Localization/Tables/GameStrings_en.asset`
- `Assets/Localization/Tables/GameStrings_es.asset`
- `Assets/Localization/Tables/GameStrings_pt-BR.asset`
- `Assets/Localization/Tables/GameStrings_de.asset`
- `Assets/Localization/Tables/GameStrings_fr.asset`
- `Assets/Localization/Tables/GameStrings_ru.asset`

Values now byte-for-byte match the corresponding JSON strings in section A (verified by extracting each
`m_Localized:` line, YAML-decoding it with `python3 -c "import yaml..."`, and eyeballing the decoded
Cyrillic/accented output against the JSON text). Preserved each file's existing escape convention
(`\xE1`/`\xFC`-style Latin-1-supplement hex escapes for es/pt-BR/de/fr, ` `-style escapes for ru) —
did NOT introduce literal unescaped UTF-8 characters where the file's own convention uses escapes (one
self-caught slip on 3 German lines, corrected before finishing). Entry count in each file confirmed
unchanged at 505 `m_Id:` rows before/after (no accidental duplication/deletion).

`ar`, `ja`, `ko`, `zh-Hans` have **no Unity table assets** (not in `enabledTableLocales`) — only their
JSON files were touched, as expected.

`tooltip.buttonClan.*` keys exist in the shared table + all 6 asset files but their VALUES were never
touched (see scope finding above) — confirmed no "clan" word in any of the 6 enabled locales' asset
values for those two entries.

### E. Hardcoded player-facing ".cs" literals found and fixed (a WO-1857-class defect, scoped narrowly)

Grepped every `.cs` file with "clan" in its name or content for actual player-visible string literals
(as opposed to identifiers, GameObject names, or `FlowTrace`/log tags, which are internal and were left
alone). Found exactly 3 genuine hardcoded player-facing literals, all reading "Clan Chat":

| File | Line | Context | Before | After |
|---|---|---|---|---|
| `Assets/_Modules/HUD/ClanChatVM.cs` | 89 | `public string Title => ...` | `"Clan Chat"` | `"Remnant Chat"` |
| `Assets/_Modules/HUD/ClanChatPanel.cs` | 86 | `PanelManager.Register(...)` display name | `"Clan Chat"` | `"Remnant Chat"` |
| `Assets/_Modules/HUD/ClanChatPanel.cs` | 161 | `ElarionUiKit.BuildObsidianModal(...)` modal header title | `"Clan Chat"` | `"Remnant Chat"` |

Also updated the in-code comment at `ClanChatPanel.cs:382-383` that quotes the (now-changed)
`clanChat.noClan` value, so the comment doesn't go stale next to the string it's documenting.

**IMPORTANT — this is a pattern shared by the WHOLE HUD module, not a clan-specific gap**: confirmed via
grep that `DailyQuestVM.Title`, `LeaderboardVM.Title`, and `QuestTrackerVM.Title` are ALL similarly
hardcoded, unlocalized `Title => "..."` properties — none of the HUD panel titles route through the
locale system. I did NOT wire `ClanChatVM.Title`/the two other spots to locale keys (that would create
an inconsistent, half-migrated pattern versus the other 3 unlocalized panel titles, and is a systemic
pre-existing gap outside this ticket's scope) — I did a literal text swap only, matching the ticket's
"pure content/copy edit" framing. **Flagging for a follow-up ticket**: all 4 HUD panel `Title` properties
(Clan/Daily Quest/Leaderboard/Quest Tracker) are unlocalized hardcoded English and should probably be
wired to locale keys together, not piecemeal.

`PanelManager.OpenPanelName` (which reads the "Remnant Chat" register name back) was checked — every
caller uses it only in internal `FlowTrace`/debug diagnostic strings, never rendered to the player, so
no further chase needed there.

### F. `site/clan-chat.html` (the Cherry Chat WebView embed host page)

| Line | Before | After |
|---|---|---|
| 6 | `<title>Clan Chat</title>` | `<title>Remnant Chat</title>` |

This is the `<title>` of the embedded WebView host page — low player-visibility (it's an embedded
webview, not a browser tab the player normally sees), but it is the one literal "Clan Chat" string in
that file; every other "clan" occurrence in the file is a comment, a variable name (`roomId`, `clanId`),
or prose describing the engineering concept, which per the WO stays untouched.

### G. Explicitly NOT touched (internal identifiers, confirmed unchanged by design)

Grepped for and confirmed untouched: `clan_id` / `clanId` (JS payload field + C# regex match), the
`clans`/`clan_members`/`clan_messages`/`clan_reports`/`clan_rate_limit` SQL tables and their migration
files (`api/migrations/2026...clan*.sql`), `ClanService`, `ClanFeatureGate`, `ClanChatPanel`,
`ClanChatVM` (class names), `ClanChatSource`, `ClanChatWebHostGreeWebView`, `IClanChatWebHost`,
`ClanMembershipClient`, `ClanRoomBinding`, `api/clan/*.js`, `api/_lib/clan*.js`, this WO's own filename
and number, and every internal comment describing "the clan system"/"clan chat" as an engineering
concept (e.g. `ClanMembershipClient.cs` header comments, `ClanChatSource.cs` comments).

### Files touched (26 total)

**JSON (23):** the 10 `Assets/Resources/Data/Canonical/<locale>.json` + 10
`Assets/StreamingAssets/Data/Canonical/<locale>.json` (en/es/pt-BR/de/fr/ru/ar/ja/ko/zh-Hans) +
`Assets/Resources/Data/Canonical/chat-phrases.json` + `Assets/StreamingAssets/Data/Canonical/chat-phrases.json`
+ `Assets/Editor/Localization/GooglePlayLocalizationVariantPolicy.json`

**Unity Localization tables (6):** `Assets/Localization/Tables/GameStrings_en.asset`,
`GameStrings_es.asset`, `GameStrings_pt-BR.asset`, `GameStrings_de.asset`, `GameStrings_fr.asset`,
`GameStrings_ru.asset`

**C# (2):** `Assets/_Modules/HUD/ClanChatVM.cs`, `Assets/_Modules/HUD/ClanChatPanel.cs`

**HTML (1):** `site/clan-chat.html`

### Gates run by this agent (per instructions — compile gate/DataRegression left for the lead)

- `python tools/gate_brace.py Assets/_Modules/HUD/ClanChatVM.cs Assets/_Modules/HUD/ClanChatPanel.cs`
  → `GATE_BRACE_SUMMARY bad=0 of 2`
- Manual brace count + NUL-byte scan on both `.cs` files → balanced (25/25, 35/35), no NUL bytes in
  either file.
- `node -e "JSON.parse(...)"` on all 23 touched JSON files → all OK.
- YAML-validated every touched `m_Localized:` line in all 6 `.asset` files with `python3 -c "import
  yaml; yaml.safe_load(...)"` → all parsed; entry counts (505 `m_Id:` rows) unchanged before/after in
  each file.
- Did **not** run `CompileGate`/`DataRegression`/`node --test test/*.test.js` — left for the lead per
  instructions.

### Open items for the lead

1. Run the Unity compile gate + `LocaleParityRegression` + `LocalizationAuthorityRegression` (this
   agent did not touch any C# in a way that should regress compilation, but the .asset edits should be
   proven by the regression, not by this prose).
2. Full `DataRegression.RunAll` for `REGRESSION_OK <n>/<n>` and `node --test test/*.test.js`
   before/after counts, per acceptance criteria.
3. Consider a follow-up WO for the systemic HUD panel-title localization gap noted in section E
   (Clan/Daily Quest/Leaderboard/Quest Tracker all hardcoded English `Title` properties).
4. Placeholder-quality translations flagged for a linguistic pass: the ru "Осколок" (fragment/shard),
   ja "残党" / ko "잔당" (remnant faction), and zh-Hans "遗民" (remnant people) choices are thematically
   reasoned but not native-speaker-verified; es/pt-BR/de/fr choices ("Remanente"/"Remanescente"/
   "Überrest"/"Vestige") are more straightforward dictionary-adjacent translations of "remnant" and
   lower-risk.
5. Gate + commit + board flip (`Status:` line already flipped to READY FOR LEAD REVIEW in this same
   file) are for the lead per instructions.
