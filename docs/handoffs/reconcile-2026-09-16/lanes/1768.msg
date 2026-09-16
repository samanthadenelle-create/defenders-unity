fix(raid): WO-1768 - a hero death inside the down-beat no longer tears down the victory screen, the death cap latches before the wait, and the army-failure trace tells the truth

Captured on the owner's Seeker (build 371701, 13:26:05): the hero died 1.9 s before the spire fell; the raid won, the victory screen opened, the owner touched it, and the hero-death evacuation tore it down 0.78 s later ("torn down without firing"). The star cap for a fallen hero latched AFTER the 1.75 s down-beat, so a raid concluding inside that window could pay 3 stars and veterancy to a dead hero (this one settled 2 only because the honor clock clamped it). The "army settled as a failure" line printed unconditionally although both settle writers had refused the duplicate.

Fix: the EVAC path yields to VictoryOwnsTheReturn (latched right after EndStateView.Show inside the existing try) with a 45 s realtime watchdog that falls through to the unchanged EVAC and says why; NotifyHeroDied moves into the synchronous death sequence; RaidDeployController exposes Reconciled and the trace is conditional with a truthful "NOT re-settled" twin. RaidExitParityRegression PIN 4/5/6 added (red at HEAD, green now).

Gated: COMPILE_GATE_OK (2026-09-16 15:15) + REGRESSION_OK 550/550 suites (2026-09-16 15:31); [raid-exit-parity] and [raid-scoring] green.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
