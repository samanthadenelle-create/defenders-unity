# Release record — Google Play AAB `2026.09.15.371436`

**The first artifact in this project's history to pass the Play compliance gate.**
Tag: `ship/play/2026.09.15.371436` → commit `f58dc1ebb`. Not yet uploaded.

---

## 1. Artifact identity (measured from the file, not from a build script's claim)

| Field | Value |
|---|---|
| Path | `Builds/Android/EchoesOfElarion-GooglePlay.aab` |
| Size | 455,016,317 bytes (433.9 MiB on disk) |
| Measured install size | 451,607,350 — **48,392,650 under** the 500,000,000 ceiling |
| SHA-256 | `648d2c246ed19d2439d48c8bcdf905df3aa63c48b6b9a5a3292f462eb477991d` |
| versionName | `2026.09.15.371436` (read from the AAB's own manifest) |
| Package | `com.denellestudios.echoesofelarion` |
| Signing | `dotr-release.keystore`, alias `dotr` — the stable signature, so tester updates install in place |
| Build tools | Unity `6000.4.8f1`, `bundletool-all-1.17.2.jar` |

## 2. Gate evidence — every marker on a fresh log, judged by MARKER not exit code

Chain run `17:36:01 → 17:47:54`:

```
[GooglePlayPackagingGate] PLAY_ARTIFACT_CLEAN_OK
[GooglePlayPackagingGate] PLAY_SOURCE_ISOLATION_OK
[AndroidBuild] SUCCEEDED — 433 MB in 00:04:06
AAB_OK → AAB_SIGNING_OK → R2_PARITY_OK (targets=Android,StandaloneWindows64,WebGL objects=201)
       → AAB_SIZE_OK → AAB_DONE
```

Pre-ship gates: `COMPILE_GATE_OK` (`Builds/cg1759`), `REGRESSION_OK 538/538 suites` (`Builds/reg1759b`).

`PLAY_NEUTRAL_TOKEN_SWEEP` neutralised 13 strings in `canon-strings.json` and 5 in `packs.json`, restored byte-exact after the build by `ValidateNeutralMirrorEquality`.

## 3. What actually cleared the gate — and what that cost

Six offenders on the 14:14 run became **zero**. Two of the four fixes were the **gate lying**, not the app leaking, which is the finding worth carrying forward.

| Offender | Ticket | Verdict |
|---|---|---|
| `crypto` | WO-1754 | **Never a leak.** `system.security.cryptography.hmacsha256`, a core .NET name in every IL2CPP build. The scanner read in 64 KiB chunks and truncated its own allowlist window at the seam (`crypto` @ 1,900,536, phrase ends 1,900,547, chunk 29 ends 1,900,544). ⚠ The same fix closed a strictly worse case: a file whose length is an exact chunk multiple lost its deferred tail entirely — and `globalgamemanagers.assets.split0` is 1,048,576 bytes, 16 chunks with no remainder, **in this very artifact**. Un-fixed, the false positive would have become a silent **MISS**. |
| `web3` | WO-1755 | **Real.** The namespace `DeNelle.Core.Web3` AND its folder path were both in `global-metadata.dat` — IL2CPP writes **source paths** (`\Assets\_Modules\Core\Web3\BackendRequestSigner.cs` at offset 10,852,131). Renaming the namespace alone would NOT have been enough; the folder moved too. There was never a Web3 *assembly* to rename — the type is owned by `DeNelle.Core.asmdef`, which ships in every variant. |
| `solana`, build receipt | WO-1754 | Owner-ruled identifiers (`SolanaDappStore`, `SolanaWallet` — WO-1377; `dotr-arena-skr-balance` — `ArenaWalletService`) allowlisted **with their rulings cited**. A NUL-bounded entry was proven impossible: the PowerShell mirror's `'"([^"]*)"'` parser reads NUL as two literal characters, so the two scanners would have held different vocabularies. |
| `skr` | WO-1759 | **Real, but not what the lead's ticket claimed.** Of 43 raw occurrences only **3** actually fired: `ArenaWalletService.cs:50`'s pref key (twice, once serialized) and a `" SKR"` literal that Roslyn **folds** into its neighbour at `VerifiedStakeSnapshot.cs:202`. `costSkr`, `stakedSkr`, `SkrShowcasePanel` etc. were already suppressed by boundary rules. Both live sources now sit behind `#if GOOGLE_PLAY`; the dApp Store arm keeps them verbatim. |

**Method note worth keeping:** a raw byte scan is **not** the matcher's verdict. The lane that settled `skr` ported the gate's matcher to Python and parsed the token arrays out of `GooglePlayPackagingGate.cs` at run time, which is the only reason the real three were found instead of the fifteen the lead had guessed at.

## 4. Player-facing changes in this build

Carries everything from tester `2026.09.15.371285` plus the Play compliance work:

- **Raids — troops ignore walls.** A warband never attacks a wall unless Breach is tapped; siege still breaks walls on its own. The old 10%-damage fallback is gone (owner ruling, WO-1752).
- **Raids — the warband defends the player.** A hostile damaging the hero becomes a valid target for every troop, regardless of that troop's own acquire radius.
- **Raids — the objective is reachable.** The spire was seated on the ground and then buried under a 1.5 m keep platform, so no troop could ever path to it (`PathPartial` 1650 times, `PathComplete` zero). Re-seated, measured off the platform, across all raid tiers (WO-1749).
- **Hero — zero HP now means down.** The phone's attack/cast buttons called the controllers directly, bypassing the component-level input lock, so a dead hero kept fighting. The documented outcome now runs: hero down, army fights on, 2-star cap (WO-1750).
- **Camera — walls fade instead of blocking.** 158 raid walls were invisible to the occlusion cast, and the fade hid only the first of a wall's two renderer subtrees. Both fixed; the pull-in now measures from the camera seat rather than the hero's chest (WO-1751, WO-1753).
- **Targeting — walls are tap-only.** The reticle no longer auto-acquires a wall, and the engage button no longer picks one for you. Cycle-target still reaches walls deliberately (WO-1756).
- **Art —** the arena boundary pillars no longer render as untextured grey slabs (WO-1758); the Field Cleric no longer renders as a white blob (WO-1747).
- **Analytics —** every player was collapsing into the shared id `unverified` since 09-07; identity headers now ride every event (WO-1733 + WO-1735).

## 5. Play Console "What's new" — paste this (≤500 chars)

```
Raids: your troops now ignore walls unless you order a Breach, and they break off to defend you when
you're attacked. Fixed a bug that made the enemy spire unreachable, so raids can actually be finished.
Fixed a hero who kept fighting at zero health. The camera now fades walls instead of hiding the fight
behind them, and tapping is required to target a wall. Fixed untextured grey pillars and a
white-looking Cleric.
```

## 6. ⛔ STILL BLOCKING PLAY SIGN-IN — no build can fix this

Google sign-in fails on **every Play-delivered build**, proven by bytes (WO-1744 §5b): the one Android OAuth client is bound to the **local keystore's** certificate `09078344…`, while Play App Signing re-signs the artifact with `84d4d209…`.

**Owner action, Cloud Console → APIs & Services → Credentials, project `defenders-of-the-realm-echos` (264518851517):**
- create an **Android** OAuth client for `com.denellestudios.echoesofelarion` with SHA-1
  `84:D4:D2:09:58:B6:A0:61:39:9B:B5:FF:28:86:05:23:49:6A:72:22`;
- check the consent screen's publishing status — if it is **Testing**, each tester's Google account must be added as a **test user** (separate from Play Console testers).

This is **not** Firebase and **not** Play Games Services — the app uses Google Sign-In for Unity v1.0.4, and the absence of PGS is proven in the artifact (0 `com.google.android.gms.games` DEX references, no `games.APP_ID` in the manifest).

## 7. Open, deliberately not decided here

- `Packages/com.solana.unity_sdk/Resources/` — three PNGs (incl. `magicblock-logo.png`) force-included into every Play AAB. Gate-clean, **unruled**, untouched.
- The gate's short-token strictness: making `stakedSkr` / `costSkr` / `SkrShowcasePanel` fire would add ~15 new offenders across Core and Village, including a **crystals cost** field. That is a ruling, not a fix.
- `Builds/Windows/…/DeNelle.Core.dll` still carries the old namespace — a stale shipped player. Wipe and rebuild per the standing rule.
