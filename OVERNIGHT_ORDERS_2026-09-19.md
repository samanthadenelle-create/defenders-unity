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

- HEAD: `git log -1 --oneline`
- Branch / ahead: `git status -sb`
- WO-1875 status line (quote it)
- WO-1874 status line (quote it)
- Compile log + marker
- Regression log + marker
- APK stamp if built, or "not built because …"
- What a waking owner should film first
- What is still fake vs real
- Token/lane count: how many Grok implementers ran
