# SESSION HANDOVER — 2026-09-15 — the Play track, the wall fork, and three instruments that were lying

**Seat:** Claude desktop (Fable 5.1 from ~16:00; Opus 5 before), sole committer. **Branch:** `dev`,
pushed clean through `671833564`. **On the Seeker:** the PLAY-delivered `2026.09.09.362625`
(`installerPackageName=com.android.vending`) — the owner replaced the local tester build to test
sign-in. **On the emulator:** local tester `2026.09.15.370995`.

---

## 1. WHAT LANDED TODAY (all gated: `COMPILE_GATE_OK` Builds/cgFinal, `REGRESSION_OK 534/534`
Builds/regFinal; JS 736 tests / 735 pass / 0 fail / 1 pre-existing todo)

| Commit | WO | What |
|---|---|---|
| `452fc14fd` | 1732 | Top raid tier (IronBastion) was never regenerated — 210 walls / 0 ruins → 158/158; coverage guard went RED then GREEN |
| `06da9b7b6` | 1734 | Camera occluder FADE restored (regressed 09-01, `486cd7b17`); hero targets units over walls |
| `658f24b8a` | 1737 | Tier-1 wall had NO damage reduction (`pow(step,0)=1`); curve anchored, ratios kept |
| `dbe6b9544` | 1736 | Wave-end HUD-stuck diagnostic ARMED for external player Sminer (instrument only) |
| `50370c1fb` | 1733 | Analytics: every player collapsed into `unverified` since 09-07 — server-side guest-id fallback |
| `c51245e40` | 1745 | 409 `SAVE_RESET_STALE` was always recorded; the admin VIEW never read it — widened |
| `a726aa622` | 1739/1740 | AAB chain reported `AAB_OK` over a REJECTED artifact — now fails closed |
| `1cc0c5059` | 1741 | UniTask path allowlist (narrow, reasoned) + `Web3/Resources` quarantined from Play |
| `5cc929a53` | 1743 | Night Market rows overlapped into an unreadable stack → scroll zone + geometry regression |
| `b9dea6098` | 1744/1738 | Sign-in cause PROVEN BY BYTES; wall fork RULED |
| `671833564` | board | Fixed my own `BOARD_CHECK_FAIL` (invented "RULED" label) |

**Production API deployed by the owner ~16:37Z** from the working tree (`nq5qkpbwu`, ● Ready). Live:
the command-center console (WO-1244), the analytics identity fix, `ANALYTICS_EXCLUDED_PLAYER_IDS=unverified`.
**NOT yet live:** the 409 view widening (WO-1745) — it landed after the deploy; needs the next promote.

## 2. THE FACTS THAT CHANGE HOW THE OWNER SHOULD READ HER OWN DASHBOARD

- **The dashboard read "1 active player" since 09-07 because of an identity collapse, not a playerbase
  collapse** — `EventTracker.cs:290-294` sends no identity headers; `track.js` fell back to the literal
  `unverified`. `docs/GROWTH_RCA_2026-09-06.md` ("no dashboard, no way to tell zero from a hundred") was
  wrong on the day it was written — the dashboard landed 08-17.
- **The "2 paying buyers / $10.97" are BOTH the owner** (one SKR, one Pi). External revenue is ZERO.
  Those rows prove the rails work on two chains — a technical result, not a commercial one.
- **There is one real external player: "Sminer"** (Discord, Germany, Google Play TESTER track, hero
  Lv 45, Wave 146). His level-45 save is NOT in Neon (max hero level across 65 rows = 20). He reported
  the first outside bug (wave-end HUD stays in combat mode; logout is the only recovery) — WO-1736.
- **Google sign-in fails on EVERY Play-delivered build, proven by bytes** (WO-1744 §5b): the one
  Android OAuth client is bound to the LOCAL keystore's cert `09078344…`; the Play App Signing cert on
  the artifact is `84d4d209…`. Console-only fix, no rebuild. Not Firebase.
- **No Play AAB has ever passed the compliance gate.** Every AAB on disk carries a live Jupiter swap UI
  via a force-included `Resources` folder the asmdef constraint cannot sever (WO-1740).

## 3. OWNER ITEMS — nothing here can be done from a seat

1. **Cloud Console → Credentials, project `defenders-of-the-realm-echos`:** create an Android OAuth
   client for `com.denellestudios.echoesofelarion` with SHA-1 `84:D4:D2:09:58:B6:A0:61:39:9B:B5:FF:28:86:05:23:49:6A:72:22`;
   check the consent-screen publishing status. Fixes every Play tester at once.
2. **Next production promote** (`vercel --prod --yes`) — carries the WO-1745 view widening. Then
   `GET /api/admin/db?view=authrejects&code=SAVE_RESET_STALE&since_hours=168` answers "how many devices
   are frozen" over 09-07 → today with no new traffic.
3. **WO-1742 Lane A ruling — whose town survives on a 409:** (a) server wins / (b) ask the player /
   (c) push local — **(c) is a trap** (routes around the WO-1598 guard, destroys a New Game made elsewhere).
4. **`Packages/com.solana.unity_sdk/Resources/`** (Solana branding textures) — quarantine or not.
5. **`DeNelle.Core.Web3` assembly NAME** leaks into Play metadata — only a rename removes it.
6. **Siege identity** — the hero out-breaches the catapult 2.3x; ruling needed.
7. **Ask Sminer for his `Application.version` line**, and whether his progress survives a reinstall.
8. **LevelPlay:** set Store availability → "Not live yet" until the Play listing is public (404s today).

## 4. IN FLIGHT AT HANDOVER

- **WO-1746** (Opus lane, pre-assigned number): Branch B wall targeting — units-first default, siege +
  Breach-stance at full damage, BLOCKED-no-Breach fallback at 10% wall damage, Breach as a persistent
  auto-chaining stance. Files: `RaidAssaultAi.cs`, `TroopController.cs`, `TroopBreachOrder.cs`, a new
  regression + `DataRegression.cs` line. **When it lands:** verify diff → brace/NUL → compile gate →
  regression → commit by explicit path → board → push. Nothing else may touch those files meanwhile.
- **Analytics post-deploy proof:** `unverified` last_seen frozen at 16:30:37Z (pre-deploy) and ZERO
  sessions of any kind since 16:37Z. Pending TRAFFIC, not failed. One Seeker launch settles it: a new
  guest-shaped id must appear while `unverified` stays frozen.

## 5. THE MISTAKES THIS SEAT MADE (memories written)

- Pushed a board with `BOARD_CHECK_FAIL 1 unlabeled` in it — my own invented "RULED" status word, and a
  grep that only looked for `BANNER_OK`. → `status-label-is-a-fixed-word-rulings-go-in-prose`.
- Told the owner "the IronBastion scenes are the likely culprit" for the AAB rejection — WRONG; the
  Jupiter panel dates to 2026-05-26. I had labelled it unproven, which is the only reason it cost nothing.
- Said the Play-build sign-in failure "fails on both certificates" — the emulator log actually carried
  ZERO auth lines; the claim was unproven and I had used it to demote the true cause.
- Called the town-navmesh loss "an unfinished rename" — the owner challenged it and was right (Unity
  names `NavMeshSurface` output after the GameObject; nobody renamed anything).

## 6. VERIFICATION LEDGER — what is proven vs claimed

Proven by measurement: wall-fork wave-independence (`EnemyBrain.cs` has 0 refs to the raid AI);
all three signing certs; 60-clad exclusion count; 534/534; 735/0; prod deployment ● Ready.
Claimed, unverified: Night Market renders correctly (no capture PNG opened; `DeNelle.GooglePlay` only
compiles under `GOOGLE_PLAY`, so the default compile gate does not prove it — the Play AAB build is the
real compile proof); the AAB chain's rejection path (parse-checked, never executed); Sminer's wave-end
holder (instrumented, unnamed until his next log).
