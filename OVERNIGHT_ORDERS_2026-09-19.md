# OVERNIGHT ORDERS — 2026-09-19 (demo: vigil + ballot + vault readout)

**Authority:** START_HERE §7. Owner asleep after ~22h. Executive decisions for THIS night are the tables below. Do not wake her. Do not invent a creative ruling. Push is NOT authorized.

**Goal:** a gated tester-shaped tree that can show Circle SIGN IN, a named vigil word, a votable ballot, a Squads N-of-M readout if a vault is bound, and ceremony force-show on a QA_SCENARIO APK. Film is morning. Felt-verify is hers.

**Token law:** one Grok implementer at a time. Ollama (`qwen2.5-coder:7b`) for WHERE only. Lead gates. No parallel Circle/Heart/chat agents. If a gate is red, one fix pass then re-gate. Second red on the same fault → STOP that lane, write the morning report, park.

---

## MUST (in order; stop at the first red that a one-fix pass cannot clear)

1. **Let WO-1875 finish, then VERIFY.** Do not spawn a second Circle agent. Brace + NUL on every touched `.cs`. Judge `COMPILE_GATE_OK` on a **fresh** log (`-ExpectMarker COMPILE_GATE_OK`). Then `DataRegression.RunAll` on a **fresh** log; judge `REGRESSION_OK <n>/<n> suites`. Combined tree (1874 ceremony + 1875 sign-in). Unity editor must be closed.

2. **Vigil word on the Circle header** (only if 1875 gated green). Ember / Flame / Beacon / Pyre / Dawn from `VigilCeremonyWords.HighestUnlockedTier(vigil_weight)`. Large. Not a raw number as the only tell. HUD only. No save-schema bump.

3. **QA_SCENARIO extras** (only if 2 is gated, or if 2 is skipped because 1875 ate the night). Extend `DevScenarioIntent` under `#if QA_SCENARIO_BUILD` only:
   - `dotr.circle=unsigned|members|ballots|vault`
   - `dotr.vigil=ember|flame|beacon|pyre|dawn` (dressing + header; does not fake on-chain stake)
   - `dotr.ceremony=play` (plate once; canned payload; not a live epoch lie)
   - `dotr.ballot=open` (opens Ballots tab; votes still hit the server)
   Never invent Squads tx signing. Never use `ff.stakedemo`. Never ship these extras on a Firebase tester / store APK.

4. **One tester APK** only after MUST 1 is green. `overnight-apk-build.ps1 -Tester`. Judge `APK_OK` + `R2_PARITY_OK` on a fresh log. Do not `adb install` on the owner's Seeker. Do not `-Scenario` onto her live save.

## STRETCH (only if MUST 1–3 are green and wall clock remains)

- Mint a WO from the banner (next free = the `CLI_LANES_WO_NUMBERS.md` RECONCILED line; bump in the same edit) for the scenario extras if they are more than a handful of keys on DevScenarioIntent.
- Server **test-settle** for a named demo Circle (do not fake `settledEpoch` on the client).
- EditMode xml run if the Unity test runner is free after the data gate.

## PARK (do not touch)

- Jupiter swap / UXML panel / WalletBridgeStub
- WO-1873 chat
- Raid fog, grey outer wall, Armorer plates, owned-town HUD
- In-game Squads proposal UI (game reads; Squads signs)
- KEY_FACTS / CANON rewrite (both are days stale; flag in the morning report, do not spend the night on them)
- git push
- `--prod` Vercel
- Hand-edit `.unity`

## GATE CADENCE

After every code-touching lane: `python tools/gate_brace.py` on the `.cs` list, NUL scan, then **one** Unity compile gate, then **one** DataRegression. Markers on a fresh log. Do not judge wrapper exit codes.

## MORNING REPORT (leave this filled)

- HEAD: `5b1b14c45 QA_SCENARIO: dotr.circle/vigil/ceremony/ballot extras (overnight MUST 3).` (local `dev` also has `d044c8b31` MUST 2 header word; `2a6f75f49` WO-1874/1875 gated). **Not pushed.**
- Branch / ahead: `dev...origin/dev [ahead 10]`. Dirty leftover: `ProjectSettings/ProjectSettings.asset` (AAB/WebGL/APK target flips — not committed).
- WO-1875 status line: `**Status:** IMPLEMENTED - gated 2026-09-19 COMPILE_GATE_OK (Builds/c1875d.log) + REGRESSION_OK 585/585 (Builds/r1875c.log). Owner felt-closes on a tester APK: fresh boot Circle SIGN IN Members.`
- WO-1874 status line: `**Status:** IMPLEMENTED - gated 2026-09-19 COMPILE_GATE_OK (Builds/c1875d.log) + REGRESSION_OK 585/585 (Builds/r1875c.log).`
- Compile log + marker: latest combined tree `Builds/cg1875f.log` → `COMPILE_GATE_OK :: scripts compiled clean` (MUST 3). Prior: `c1875d.log` / `cg1875e.log` also `COMPILE_GATE_OK`.
- Regression log + marker: latest `Builds/r1875f.log` → `REGRESSION_OK 585/585 suites -- 585 green, 0 red, 0 skipped`. Prior: `r1875c.log` and `r1875e.log` same 585/585. First red this night (`reg1875.log` 582/585) was locale tables + Play policy + a WO-1881 4000-char tutorial-reach window; loc rebuild + 8000-char pin cleared it.
- APK stamp: **tester APK built.** `Builds/overnight-apk-status.txt`: `APK_OK 2026-09-19T01:36:28 path=D:\eoa\Builds\Android\DefendersOfTheRealm.apk size=447MB`. Fresh `Builds/apk-build.log`: `[AndroidBuild] SUCCEEDED — 446 MB on-disk (468479287 bytes)`. `Builds/r2-parity.log`: `R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=207`. Sideload yourself — **not adb-installed on Seeker.** Not `-Scenario` (no `QA_SCENARIO_BUILD` on this artifact).
- What a waking owner should film first: install `Builds/Android/DefendersOfTheRealm.apk` (447MB, mtime 01:36). Fresh boot → Circle Chat → Circle door → **SIGN IN** (gold) → Members. In-Circle header should show **Ember / Flame / Beacon / Pyre / Dawn** (large Gold), not a raw number as the only tell. Ballots tab: votable ballot if the server has one; Vault tab: N-of-M readout if a vault is bound. Ceremony plate only if a live epoch actually passed and is unseen.
- What is still fake vs real: **Real** — SIGN IN mint (`clan/me` GET true), live Circle reads, ballot votes hit the server, header word from `HighestUnlockedTier(vigil_weight)`, Heart dressing from the ledger, ceremony auto-play from a real passed unseen epoch. **Fake / not on this APK** — `dotr.*` scenario extras (`circle/vigil/ceremony/ballot`) compile only under `QA_SCENARIO_BUILD`; this tester APK does **not** carry them. Canned `ceremony=play` (epoch 0, CircleName "Elarion") is QA-only. Dressing-tier header fallback only when live weight unlocks nothing. No Squads tx UI. No `ff.stakedemo`.
- Token/lane count: **1 Grok implementer** this wake (MUST 3 QA extras). Lead did the MUST 2 header-word HUD edit (overnight "one small HUD edit" exception) and all gates/commits/APK. 1874/1875 implementers had already handed back before this wake. **Off-script Unity contention:** `google-play-aab-build.ps1` (`AAB_BUILD_UNPROVEN`) and `build-webgl.ps1` held the editor and delayed the tester APK; neither was this night's MUST.
- FLAG (do not spend the night): KEY_FACTS / CANON are days stale, as parked.
