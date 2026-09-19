# WO-1880 — Emit wave_started so we can tell "never started wave 1" from "started and failed"

**Status:** IMPLEMENTED 2026-09-19 — `WaveManager.StartWave` emits `wave_started` `{ waveId }`. `?view=funnel` has `waves.started_players` / `completed_players`. Forward-only until a build ships.

**Owner, verbatim:** "Ticket C — add wave_started" / "Mint Ticket C today."

## Problem

0 `wave_started` rows in 30d. Only `wave_completed` exists (`WaveManager.cs` after a clear). Cannot distinguish never-started vs started-and-died. Blocks the tower-defense funnel.

## Fix

Emit `EventTracker.Track("wave_started", new { waveId })` at the moment a wave **begins** (`WaveManager.StartWave`, after the def resolves, when phase becomes Active). Same `waveId` property as `wave_completed`. Forward-only; no backfill.

Add `wave_started` to `?view=funnel` (or command wave activity) as started → completed. Do not add it to QUALIFYING_PLAY (a start is not a clear).

## Acceptance

`SELECT COUNT(*) FROM analytics_events WHERE event_name='wave_started'` > 0 after a play session / headless wave. Funnel can show start vs complete.

## Not in scope

WO-1879 player counts. Tutorial walk.
