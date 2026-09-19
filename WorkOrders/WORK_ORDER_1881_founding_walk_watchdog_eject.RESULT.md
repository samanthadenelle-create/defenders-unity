# WO-1881 RESULT

Diagnosed 2026-09-19: founding_walk drops are the 120s watchdog (55 events at idle 120.0, 3 at 120.1–123.6). `autoAdvanced=true`, `recordedAs=skipped`, `coachBeats=3`. Not a network/wallet hang. Skip button is authored and unused. founding_stores drops are exactly 300.0s (placement bound).

Fix: if founding_walk watchdog fires and the hero has moved ≥8m from enter, complete as played (`STEP-WALK-VALID`). Radius and WatchdogSeconds unchanged (WO-962). Still-AFK players still drop.
