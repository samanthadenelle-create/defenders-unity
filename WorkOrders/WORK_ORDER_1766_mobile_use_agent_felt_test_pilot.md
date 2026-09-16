# WO-1766: pilot minitap-ai/mobile-use as a natural-language felt-test driver (isolated, emulator-only)

**Status:** READY TO IMPLEMENT
**Silo:** tooling only — a clone under `tools/third_party/mobile-use/` (gitignored) plus this WO and
one SKILL.md section. **Touches NO `Assets/`, NO `.cs`, NO scene, NO catalog.** No Unity gate needed.
**Number:** PRE-ASSIGNED by the lead. `CLI_LANES_WO_NUMBERS.md` was NOT touched by this lane; the
lead owns the banner bump in the mint commit (CLAUDE.md §2).
**Source:** owner ask 2026-09-16 — *"scrcpy to let the agents test on pc from my device"* and
*"minitap-ai skill?"* pointing at https://github.com/minitap-ai/mobile-use. Every claim below was
read at source or measured on this PC on 2026-09-16 (CLAUDE.md §11B); anything NOT proven is marked
**UNPROVEN** in §8.

---

## 1. What mobile-use is, and how it drives Android

Apache-2.0 open-source agent (minitap-ai) that *"controls your Android or IOS device using natural
language."* Three perception/actuation legs, per its README:

1. **ADB** — device connection and control (the same `adb` this repo already uses).
2. **The accessibility tree / UI hierarchy** — its primary way of knowing what is on screen and what
   is tappable. This is the `uiautomator`-style node dump: class, `text`, `content-desc`, `bounds`,
   `clickable`.
3. **Screenshots** — visual understanding layered on top of (2).

It is a multi-agent graph. `llm-config.override.template.jsonc` defines the roles that each need a
model: **`planner`, `orchestrator`, `cortex`, `executor`, `contextor`**, plus
**`utils.hopper` / `utils.outputter` / `utils.video_analyzer`**. Accepted `provider` values in that
template: `openai, google, openrouter, xai, vertexai, anthropic, azure, minimax`. Each role takes
`provider` + `model` and an optional `fallback`. **There is no per-model `base_url` field** in the
template — an OpenAI-compatible endpoint is selected globally via the `OPENAI_BASE_URL` env var with
`provider: "openai"`. The template also notes `hopper` *"Needs at least a 256k context window."*

Entry point (README, verbatim):
```
python ./minitap/mobile_use/main.py "Go to settings and tell me my current battery level"
python ./minitap/mobile_use/main.py "Open Gmail, find all unread emails, and list their sender and subject line" \
  --output-description "A JSON list of objects, each with 'sender' and 'subject' keys"
```
Windows Docker quickstart: `powershell.exe -ExecutionPolicy Bypass -File mobile-use.ps1`.
Claimed benchmark: first framework to 100% on AndroidWorld.

## 2. ⛔ THE HARD LIMITATION FOR THIS GAME — the agent would be VISION-ONLY

**The README says it itself:** mobile-use has *"limited effectiveness with games as they don't
provide accessibility tree data."* A Unity player renders its whole UI into one `SurfaceView` /
`UnityPlayerActivity`; none of our buttons are Android views, so `uiautomator` sees one opaque node.
Every leg of mobile-use's perception except the screenshot goes dark, and the planner is reduced to
guessing pixel coordinates from an image.

**What was measured here (2026-09-16), and what was not:**
- Baseline on the **launcher** (`com.android.launcher3/.../SearchLauncher`, the foreground app at the
  time): `adb -s SM02G4061955851 shell uiautomator dump /sdcard/ui.xml` -> `UI hierchary dumped to:
  /sdcard/ui.xml`, **14 691 bytes, 39 `<node>` elements**, with real labelled, bounded, clickable
  nodes (`text="Gemini"`, `text="MetaMask"`, `bounds="[0,0][1200,2670]"`). So the tooling works and
  the device exposes a normal tree for a normal app. (`/sdcard/ui.xml` was removed afterwards.)
- **The game was NOT dumped.** `com.denellestudios.echoesofelarion` is installed on the Seeker but was
  not running, and this lane is read-only on the owner's device (memory `owner-prefs-playtest`:
  never auto-launch builds). **So "the Unity surface exposes no tree" is REASONED FROM THE README AND
  FROM HOW UNITY RENDERS — it is NOT measured on this APK.** Closing that is step 1 of the pilot
  (§6), and it must be done on the emulator, not on her phone.

## 3. Prerequisites on this PC — measured 2026-09-16

| Need | State on this PC |
|---|---|
| Python | **3.14 at `C:\Python314\python.exe`**. mobile-use requires **3.12+** — 3.14 is newer than anything it is tested against; pin 3.12 in the venv (`uv python install 3.12 && uv venv --python 3.12`) rather than trusting 3.14. |
| `uv` | **NOT installed** (`where.exe uv` -> "Could not find files"). Must be installed for the manual path. |
| Docker | Present: `C:\Program Files\Docker\Docker\resources\bin\docker.exe` |
| Ollama | Present: `C:\Users\Elden\AppData\Local\Programs\Ollama\ollama.exe` |
| Local models | `ollama list` -> `qwen2.5-coder:7b`, `llama3.2:3b`, `gpt-oss:20b`. **NONE of these is a vision model** — a vision-only driver (§2) would need a VLM pulled first (e.g. a qwen2.5-VL or llava tag). That is a real cost/disk item, not a given. |
| adb | `1.0.41 / 36.0.0-13206524` at the Unity SDK `platform-tools` |
| Emulator | **`C:\Users\Elden\AppData\Local\Android\Sdk\emulator\emulator.exe` EXISTS**, and one AVD: **`Pixel_10_Pro_XL`** (`%USERPROFILE%\.android\avd\Pixel_10_Pro_XL.avd`). This is the pilot target. |

**Cost-first (owner standing rule `cost-always-top-of-mind`):** mobile-use accepts any
OpenAI-compatible endpoint, so Ollama is the zero-marginal-cost path:
```
OPENAI_BASE_URL="http://localhost:11434/v1"
OPENAI_API_KEY="ollama"          # Ollama ignores the value but the SDK requires one
```
with every role in `llm-config.override.jsonc` set to `provider: "openai"` and a local model.
⚠ Two caveats, both unproven: `utils.hopper` wants a 256k context window, which none of the three
local models offers; and the vision roles need a VLM we do not have yet. Budget for a paid fallback
(`ANTHROPIC_API_KEY` / `OPENAI_API_KEY`) on the planner only if the local run cannot plan.
Other vars in `.env.example`: `MINITAP_API_KEY`, `GOOGLE_API_KEY`, `XAI_API_KEY`,
`OPEN_ROUTER_API_KEY`, `MINIMAX_API_KEY`, `AZURE_BASE_URL`/`AZURE_API_KEY`,
`EVENTS_OUTPUT_PATH`, `RESULTS_OUTPUT_PATH`, and `MOBILE_USE_TELEMETRY_ENABLED` —
**set `MOBILE_USE_TELEMETRY_ENABLED="false"`** (anonymous usage collection is ON by default).

## 4. Install in isolation — never into the Unity project

1. `git clone https://github.com/minitap-ai/mobile-use tools/third_party/mobile-use`
   — and add `tools/third_party/` to `.gitignore` in the same change. **Nothing under `Assets/`**:
   a Python tree inside the Unity project gets imported, meta-filed and can end up in a build
   (memory `resources-folder-ships-regardless-of-asmdef-constraint`).
   A scratch dir outside the repo is equally acceptable; say which was used.
2. `uv python install 3.12` then `uv venv --python 3.12` then `uv sync` inside that clone.
3. `cp .env.example .env`; `cp llm-config.override.template.jsonc llm-config.override.jsonc`; fill
   per §3.
4. **Prefer the `uv venv` path over Docker for the pilot.** `adb` is host-side; how the Windows
   Docker quickstart reaches a host adb server is **unverified** and is a second failure surface on
   top of the one we are actually testing.

## 5. What we already have, and where mobile-use would add value

| Existing | What it does | Gap it leaves |
|---|---|---|
| `adb exec-out screencap` + `adb shell input tap` | Exact, scriptable, free, deterministic | Coordinates are hand-derived and brittle; it cannot decide anything |
| Headless **AutoPilot fleet** (`run-autopilot-fleet.ps1`, `-nographics`) | Drives the real game logic and emits JSON; the workhorse for logic/regression | Runs the Windows player, not the APK; judges state, not feel; no pixels |
| **F8 device bridge** (`f8-device-bridge.ps1`, WO-1227) | Pulls the phone's `break-log.jsonl` + flag PNGs into the §14 inbox | Reactive — it reports what the owner already hit |
| **scrcpy** (installed 2026-09-16, WO-1766 lane) | Mirror + record the device from the PC | An agent still has to be the one deciding what to look at |

**Where mobile-use earns its place:** a felt-test script the OWNER can write in plain words —
*"open the game, go to the build menu, try to place a storage container, tell me what the screen
says"* — run repeatedly against a build without a human holding the phone. That is a capability none
of the four rows above has. **Where it does not:** anything the AutoPilot fleet can already assert
headlessly. mobile-use is more expensive, slower and non-deterministic; it is a felt-test tool, never
a regression gate.

## 6. First pilot task — READ-ONLY, and NOT on the owner's device

**Step 0 (always, even read-only):** overlay check —
`adb -s <serial> shell dumpsys window windows | Select-String "SYSTEM_ALERT","TYPE_APPLICATION_OVERLAY"`.
Baseline taken 2026-09-16 on the Seeker: the ONLY match was
`package=com.android.systemui appop=SYSTEM_ALERT_WINDOW` (the system's own UI) — **no third-party
overlay**. A hit from any other package = STOP and report; do not disable apps on her device.

1. Boot the AVD: `emulator -avd Pixel_10_Pro_XL` (path in §3); `adb devices` to get its serial.
2. Install the current APK there (`adb -s <emulator serial> install -r <apk>`) — **the emulator, not
   the Seeker**, and a fresh save.
3. **Close §2's gap:** with the game in the foreground, `adb -s <emu> shell uiautomator dump` and
   record (a) the node count, (b) whether ANY node carries a non-empty `text` or `content-desc`, (c)
   the package on the root node. Paste the numbers into the RESULT. This is the measurement that
   decides whether mobile-use is vision-only here.
4. Run ONE read-only mobile-use task against the emulator:
   `python ./minitap/mobile_use/main.py "Describe what is on screen and list every button you can see"
   --output-description "A JSON object with 'screen_summary' and a 'buttons' array of strings"`
   Capture the full output, the wall-clock, and the token/cost figures.
5. Compare its button list against a `screencap` PNG read by a human/agent. Record the hit rate.

## 7. ⛔ What NOT to do
- **No taps, swipes, keyevents or app launches on the owner's Seeker** (serial `SM02G4061955851`) —
  it holds her live save, `TitleController.OnStartNew` wipes it with no confirm, and one scripted
  lane already destroyed it on 2026-09-10 (memory `device-lanes-overlay-apps-and-start-new`). The
  pilot runs on the AVD. If a device-only defect ever forces her phone in, it is an explicit owner
  approval, with the overlay check and a screencap-proven layout first, and never the title row.
- Do not install, disable, enable or uninstall anything on her device.
- Do not clone into `Assets/`, and do not commit the clone, the `.env`, or any key.
- Do not wire mobile-use into any gate, ship chain, or `.githooks/` hook.
- Do not leave a scrcpy window with control enabled pointed at her phone (`--no-control`).

## 8. UNPROVEN in this lane (close in the RESULT, do not assert before then)
- That the game's Unity surface exposes no usable accessibility tree — reasoned, **not measured on
  this APK** (§2).
- That Ollama serves mobile-use acceptably — no vision model is local, and no role meets the 256k
  `hopper` context note (§3).
- That the Windows Docker quickstart can reach the host adb server (§4).
- That the `Pixel_10_Pro_XL` AVD boots and can install/run this APK — the AVD and the emulator binary
  were confirmed to EXIST on disk; neither was launched.

## 9. Acceptance criteria
- [ ] Clone lives OUTSIDE `Assets/`, is gitignored, and `git status` is clean of it.
- [ ] `uv` installed; venv on Python 3.12; `uv sync` completed — paste the versions.
- [ ] `.env` has `MOBILE_USE_TELEMETRY_ENABLED="false"` and no key is committed.
- [ ] §6 step 3 done: the game's node count + labelled-node answer recorded with the raw numbers.
- [ ] §6 step 4 done: the describe-the-screen output pasted verbatim, with runtime and cost.
- [ ] Step 5 hit rate recorded against a screenshot.
- [ ] A one-paragraph VERDICT: adopt / adopt-with-paid-vision / park — with the measured reason.
- [ ] The owner's Seeker is untouched: no install, no tap, no app state change. Say so explicitly.
- [ ] `**Status:**` flipped and a `.RESULT.md` written in the same commit; `python tools/board_build.py`
      re-run (CLAUDE.md §11 — the lane that owns the ticket flips the board).

## 10. Licence
**Apache License 2.0** (per the repo). Compatible with cloning and internal use; keep the
`LICENSE` and `NOTICE` intact in the clone. Nothing from it is vendored into the game.
