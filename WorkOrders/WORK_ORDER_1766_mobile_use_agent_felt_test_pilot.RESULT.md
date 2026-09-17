# WO-1766 RESULT — PARKED, not piloted (owner scope ruling + upstream's own game limitation)

**Verdict:** **PARK.** Nothing was installed, cloned, pulled or run. The owner's Seeker was **not
touched** beyond two read-only queries (below). No Unity process was run by this lane.
**Date:** 2026-09-17. Every claim below was measured or read at source THIS session (CLAUDE.md §11B).

---

## 1. The decisive evidence — the owner parked this ticket BY NUMBER

Two owner rulings recorded 2026-09-16, i.e. the **same day** as the *"minitap-ai skill?"* curiosity ask
that seeded this WO, and therefore superseding it:

- `two-week-hackathon-video-focus-2026-09-16` — owner verbatim: *"for the next two weeks we need to turn
  to getting a video polished. we need to decide what we are submitting to the hackathon and focus only
  on that and bugs"*. That note names the parked items explicitly: *"New-feature specs (WO-1773 wave-AI
  branch, **WO-1766**, WO-1775 beyond what the video needs) are PARKED, not cancelled"* — and lists
  *"mobile-use pilots"* verbatim as the scope creep the window cannot absorb.
- `zero-revenue-200-a-month-stop-loss` — owner verbatim: *"Currently we are making $0 and im paying $200
  monthly for you. If i cant make any money I have to stop spending"*. Ruling: seat time goes only to the
  submission video + the first-sale path; *"no speculative specs, no new tickets outside the two buckets"*.

WO-1766 is neither the video nor a bug. **Building it would be going off script** (§11B.B), and the
honest move is the park, not a half-pilot.

## 2. The pilot's central premise is confirmed WEAK at source (not inferred)

`https://raw.githubusercontent.com/minitap-ai/mobile-use/main/README.md`, fetched 2026-09-17, states
verbatim: *"currently has limited effectiveness with games as they don't provide accessibility tree
data."* Also confirmed there: **Python 3.12+**, **Apache License 2.0**.

Our player is exactly that case, so the agent would be **vision-only** here — which is the WO's own §2.
`ollama list` on this PC (2026-09-17): `qwen2.5-coder:7b`, `llama3.2:3b`, `gpt-oss:20b` — **none is a
vision model**, so the zero-marginal-cost local path cannot even run the pilot as specified. Closing it
would mean a multi-GB VLM pull or paid vision tokens, against the stop-loss above.

## 3. Prerequisite state measured this session — the lane starts from zero

| Item | Measured 2026-09-17 |
|---|---|
| `tools/third_party/` | **ABSENT** — no clone exists; `git status` is clean of it (nothing to gitignore yet) |
| `uv` | **NOT installed** (`Get-Command uv` threw) |
| Local vision model | **NONE** (`ollama list`, above) |
| AVD | `Pixel_10_Pro_XL.avd` + `.ini` present under `%USERPROFILE%\.android\avd` — **not launched** |
| Attached devices | `adb devices` → **exactly one: `SM02G4061955851`**, the owner's Seeker |

## 4. Why §6 step 3 (the deciding measurement) could NOT be closed read-only

The only attached device is the owner's Seeker. Two **read-only** queries were run against it (no tap,
no swipe, no keyevent, no launch, no install, no file written):

- `dumpsys activity activities` → `topResumedActivity= com.android.launcher3/com.android.searchlauncher.SearchLauncher`
- `pidof com.denellestudios.echoesofelarion` → **`22087`** (the game process is alive but **backgrounded**)

So the game is not foreground, and bringing it forward is an **app launch**, which WO §7 forbids on her
device (memory `device-lanes-overlay-apps-and-start-new`: a scripted lane already destroyed her save on
2026-09-10). The `uiautomator` dump that would settle "does the Unity surface expose a tree" therefore
requires the emulator path — i.e. the full parked pilot. **It remains UNPROVEN on this APK, and is
recorded as unproven rather than ticked** (§11B.A).

## 5. What survives from this lane

**scrcpy** was already installed on 2026-09-16 under this WO number and is the genuinely useful half —
PC-side mirror/record for felt-tests and for **video capture**, which *is* in the hackathon bucket. It
needs no LLM, no clone and no cost. Use it per memory `scrcpy-on-seeker-h264-no-control`: h264 +
`--no-control` (the default codec records 0 bytes, and control must stay off on her phone).

## 6. Acceptance criteria — dispositioned, not faked

Every §9 box is deliberately **left unticked**: the clone, venv, `.env`, node-count measurement,
describe-the-screen run, hit rate and cost figures all require the parked pilot. Recording them as done
would be fiction. The WO's §8 UNPROVEN list stands unchanged, plus §4 above.

**Recommendation to the lead:** leave WO-1766 parked until after 2026-09-30, then re-decide against the
upstream game limitation in §2. If it is ever revived: emulator only, never the Seeker, and budget a
vision model up front — the local-Ollama cost story in the WO does not hold today.
