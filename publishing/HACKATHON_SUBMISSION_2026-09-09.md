# Hackathon submission — answer sheet (drafted 2026-09-09)

Every claim below was re-read at source **this session** (§11B). Each answer is tagged:

- **PROVEN** — traceable to a file/line or an HTTP response captured today.
- **OWNER** — needs Samantha's word or an artifact only she can produce.

---

## PROJECT TITLE
**PROVEN** — `publishing/config.yaml:39` (`app.name`) and the store display name recorded in
`docs/SOLANA_STORE_LISTING.md`.

> Defenders of the Realm: Echoes of Elarion

*(Store display name is "Defenders Of the Realm", app name "Echoes of Elarion", tagline "Echoes of a
Forgotten Civilization". The combined title above matches how the form was pre-filled.)*

---

## PRIOR FUNDING FROM A VC FIRM OR ANGEL INVESTOR?

> **NO**

---

## HAS YOUR PROJECT BEEN BUILT IN THE LAST 3 MONTHS?

> **NO**

Rationale: the project predates the window — a Solana Mobile Builder Grant draft for it is dated
2026-07 (`docs/SOLANA_MOBILE_GRANT_APPLICATION_2026-07.md`), and the game has been live on the
Solana dApp Store since release `2026.08.17.328845`. Answering NO routes you to the "what major
mobile development have you done" question, which is the strongest section of this application — the
Android/Seeker work all lands *inside* the window.

---

## HAVE YOU WON A PREVIOUS HACKATHON WITH THIS PROJECT?

**OWNER** — needs the hackathon's *name*; the question explicitly asks "which one".

> Yes — [NAME OF LOCAL HACKATHON], [MONTH YEAR]. A $200 local prize, which went straight back into
> development costs. No other hackathon wins.

---

## IF PORTING AN EXISTING APPLICATION OVER TO MOBILE, WHAT MAJOR FEATURES OR NEW SIGNIFICANT MOBILE DEVELOPMENT HAVE YOU DONE?

> N/A

---

## IF NO (not built in the last 3 months), WHAT MAJOR FEATURES OR NEW SIGNIFICANT MOBILE DEVELOPMENT HAVE YOU DONE?

**PROVEN** — sources named per bullet.

> Echoes of Elarion moved from a browser build to a native Android/Seeker game and shipped to the
> Solana dApp Store (App NFT `5MG4atMRDSVn9t75oFz1KVxKdUkyz2wPi2MeunT8yFe6`, package
> `com.denellestudios.echoesofelarion`). The work since then has been almost entirely mobile:
>
> - **Native Android build pipeline** — IL2CPP/ARM64 release APK, keystore-signed for in-place
>   updates, with two isolated store variants built from one tree: a dApp Store artifact that
>   compiles the Solana/SKR modules **in**, and a Google Play artifact that compiles them **out**
>   entirely (not runtime-gated — the string literals never reach the binary). Enforced by a
>   packaging gate in CI, not by convention.
> - **Mobile Wallet Adapter sign-in with session persistence** — the player signs in once and stays
>   signed in for the day; the wallet is only re-asked at a purchase or a code redemption. Key
>   material never touches game code.
> - **On-chain SKR staking read** (details in the SKR answer below).
> - **Manage rebuilt for a phone screen** — nine full-screen pages, buildings as picture cards, an
>   exit control on every page, a queue drawer for building/training/research, and touch-native
>   structure pick-up-and-move. Touch targets held to a 112 px floor; landscape phone aspect
>   verified per panel.
> - **Remote content delivery** — enemy and structure art is served from a CDN rather than packed
>   into the APK, with a push/parity gate that blocks any release whose content bundles were not
>   uploaded.
> - **Cloud save keyed to the connected wallet** — the wallet address is the save identity; a guest
>   device id carries over automatically on first connect.
> - **Tester distribution over Firebase App Distribution**, with on-device capture and a live
>   crash/softlock triage harness used every session.
>
> Sources: `publishing/RELEASE_NOTES_2026-09-07.md`; `Assets/Editor/AndroidBuild.cs:236-247`
> (variant define isolation); `Assets/Editor/Regression/GooglePlayPackagingGate.cs:191`;
> `Assets/_Modules/Wallet/MwaSessionStore.cs`; `docs/SOLANA_STORE_LISTING.md`.

---

## DOES YOUR APPLICATION HAVE AN SKR INTEGRATION? IF SO, HOW?

**PROVEN** — `Assets/_Modules/Wallet/NativeSkrStakeQuery.cs:23-31,116-135`;
`Assets/Resources/Data/Canonical/stake-rewards.json`; `Assets/Resources/Data/Canonical/packs.json`
(`skr` prices at lines 40/68/97/133/169); `Assets/_Modules/Commerce/PackCatalog.cs:77-78`;
`Assets/_Modules/Wallet/DeNelle.Wallet.asmdef:28` + `Packages/manifest.json:3` (the Solana Unity SDK
is installed, so the real wallet path compiles into the dApp Store build).

> **Yes — two ways, both non-custodial. We never mint SKR, never hold it, and never keep a
> withdrawable in-game SKR balance. SKR is Solana Mobile's token, not ours.**
>
> **1. Read-only native staking → in-game recognition.** The game reads the player's *active native
> SKR stake* directly from the chain and thanks them for it. `NativeSkrStakeQuery` queries the real
> SKR staking program (`SKRskrmtL83pcL4YqLWt6iPefDqwXQWHSw9S9vz94BZ`) with the stake-config and
> guardian-pool accounts and PDA seeds taken from Solana Mobile's own `react-native-samples/
> skr-staking` sample, decoding the account against its published IDL layout. The connected MWA
> address is used **only as a lookup key** — no signature, no transfer, no custody. A staked amount
> maps to a tier, and a tier unlocks its own perks plus every lower tier's:
>
> | Tier | Active SKR | Unlocks |
> |---|---|---|
> | Seeker | 1 | Seeker profile badge |
> | Vanguard | 1,000 | "Vanguard" title + a small daily Crystal trickle |
> | Warden | 25,000 | Warden banner sigil |
> | Genesis | 250,000 | "Genesis" title + a golden aura flourish on your hero in town |
>
> The perks are deliberately **small, cosmetic and flavour-only — never pay-to-win**. The table is
> authored data (`stake-rewards.json`), tunable without a code change, and the whole reward path is
> covered by a monetization-covenant gate in CI that fails the build if a stake perk stops being
> cosmetic.
>
> **2. SKR as a first-class payment rail.** Every support pack carries a real SKR price alongside SOL
> and USDC (`CurrencyKind.Skr` / `PackPricing.skr`), so a player who holds SKR can pay in SKR
> natively on the dApp Store build, where the platform takes 0% — which is exactly why SKR is the
> currency we convert to. Signing goes through the Mobile Wallet Adapter / Seed Vault; the game
> builds a transaction only for an explicit purchase.
>
> The Google Play variant of the same game compiles all of this out at build time, so SKR utility is
> a genuine reason to take the Solana Mobile build rather than a bolt-on.

---

## DECK URL

**DRAFTED 2026-09-09** — source at `publishing/HACKATHON_DECK_2026-09-09.html`, published as an
artifact. It is **private until you share it** from the page's share menu — do that before pasting
the link, or the judges get a 404. Export to PDF from the page if the form wants a file.

> https://claude.ai/code/artifact/9babbc39-b644-404d-a0c4-b88c6e68697a

---

## DEMO VIDEO URL

**OWNER** — no trailer or capture exists in the repo. Needs a ~90 s device capture: town → Manage →
raid → wallet sign-in → SKR staking tier screen. I can produce a shot list and drive the on-device
capture; the upload (YouTube/Loom/Drive) is yours.

> [PASTE VIDEO LINK]

---

## REPOSITORY URL

**PROVEN** — GitHub API queried 2026-09-09: `full_name samanthadenelle-create/defenders-unity`,
`private: False`, `pushed_at 2026-09-07T20:18:46Z`. It is public and judges can open it.

> https://github.com/samanthadenelle-create/defenders-unity

⚠ Two things to know before submitting this link: (a) local `dev` commits from 09-08/09-09 are not
pushed yet, so the public tree is two days behind; (b) it being public means `api/`,
`publishing/config.yaml` and `docs/wallets-of-record.md` are public too — worth a glance, your call.

---

## ANDROID APK URL — a direct download link (REQUIRED)

**OWNER is hosting this one (ruling 2026-09-09) — she supplies the link, I leave the field blank.**
Context kept below so the link she picks satisfies the rules.

Neither existing channel satisfies "direct download link":

- Firebase App Distribution is **tester-gated** — a judge without an invite cannot download.
- A Solana dApp Store listing has **no https URL at all** — only `solanadappstore://details`, which
  resolves only on a device with the store installed.

Whatever host you use, two conditions must hold or the entry can be disqualified: the link must
**download the file without a login**, and the file must be a **store-shaped APK** — no `TESTER_BUILD`
define, no dev menu. `Builds/Android/DefendersOfTheRealm.apk` exists (465 MB, written 2026-09-09 10:41)
but I have **not** verified which of the two variants it is. Say the word and I'll confirm the stamp
before you upload it, or cut a clean store build.

⚠ If you upload to Google Drive, use "Anyone with the link" and note that Drive shows a virus-scan
interstitial for files this size — still a direct download, but judges see one extra click.

> [PASTE APK LINK]
