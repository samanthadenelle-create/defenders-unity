# URP / fog answers for the expert (2026-09-19)

**URP asset:** `Assets/Settings/DeNelle-URP.asset` (single pipeline for Seeker + WebGL + Windows).

- `m_RendererType: 1` — **Forward** (not Deferred). `m_PrefilteringModeForwardPlus: 0`, `m_PrefilteringModeDeferredRendering: 0`.
- Renderer list: one renderer guid `2b9d8b3a00d0f8e4c9a556fafa68f3ad` (`m_DefaultRendererIndex: 0`).
- **`m_RequireDepthTexture: 1` already.** Depth is on globally, not WebGL-only.
- No per-platform URP asset. Fog is not a serialized URP toggle in this asset (URP honors `RenderSettings.fog` at runtime).

**Owner raid PNG (WebGL):** bright blue sky, no ground haze, grey walls, brown dirt. Compass still on (this capture is an older player or town-hostile raid HUD). Troops still in the old bottom strip on this frame.

**Code change this session (Option A):** `ApplyRaidSky` now sets `RenderSettings.fog = true`, `ExponentialSquared`, grey-blue density 0.045, darker ambient (0.15,0.16,0.20) intensity 0.6, directional sun * 0.55. Keeps Skybox clear flags (no SolidColor). Restores fog+sun+ambient on teardown. Particle ground fog reduced to 1 centre wisp; storm cloud particles remain as supplementary.

Option B (height-based fullscreen pass) is the follow-up if A is not enough on WebGL.
