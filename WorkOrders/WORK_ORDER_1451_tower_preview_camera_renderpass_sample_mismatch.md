# WO-1451: TowerPreviewCamera raises 260 [BREAK]s in 144 seconds - RenderPass sample-count mismatch

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `Assets/_Modules/Village/UI/TowerPreviewCamera.cs`. Disjoint from gameplay and from every api lane.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1451 -> 1452 in the same edit).

## 1. EVIDENCE

Device log, 13:25:34.559 to 13:27:58.320 - 260 `[BREAK]` errors in 144 seconds, two alternating messages,
130 of each:

```
[BREAK] error: RenderPass: Attachment 0 was created with 1 samples but 2 samples were requested.
[BREAK] error: EndRenderPass: Not inside a Renderpass
```

Stack:

```
UniversalRenderPipeline:RenderSingleCamera
  <- DeNelle.Village.TowerPreviewCamera:Begin(GameObject, OrientationFix, Int32)
  <- Guard:Try
```

The cause is INVERTED from the obvious reading. At HEAD:

```
Assets/_Modules/Village/UI/TowerPreviewCamera.cs:84    renderTexture.antiAliasing = 2;
Assets/_Modules/Village/UI/TowerPreviewCamera.cs:131   _cam.allowMSAA = false;
```

So the camera renders ONE sample into a TWO-sample target. It is a self-contradiction inside this one file -
the URP asset's `m_MSAA: 2` is not the driver and must not be followed here. Every preview frame therefore
opens a render pass it cannot close, and the F8 harness flags each one.

## 2. FIX SHAPE

- Make the two lines agree. Either `antiAliasing = 1` at `:84` (preferred - the preview does not need MSAA and
  this is the cheaper frame), or `allowMSAA = true` at `:131`. Name the choice in the RESULT.
- Regression: assert the preview RT's sample count is consistent with the preview camera's `allowMSAA`.

## 3. WHAT NOT TO DO
- Do not wrap the error away in a wider `Guard.Try`. The `Guard` here is already catching it and the pass is
  still broken; silencing it costs the next reader the evidence.
- Do not change the project-wide MSAA setting to match the preview, and do NOT make the RT follow the URP
  asset's `m_MSAA`. The defect is internal to this file; following the pipeline asset would set the RT to 2
  again and leave the camera at 1.

## 4. ACCEPTANCE
- [ ] A 3-minute build-mode device or headless session records ZERO `RenderPass`/`EndRenderPass` errors.
- [ ] Regression pins RT sample count == pipeline sample count; RED proof stated. *(SUPERSEDED — see §5.)*
- [ ] `REGRESSION_OK n/n` on a fresh log.

## 5. CORRECTION — 2026-09-06 (implementation lane, verified at source)

**§1 and §2 were RIGHT. §4's second acceptance line is WRONG and is replaced.**

Verified at HEAD before the fix: `TowerPreviewCamera.cs:84` `antiAliasing = 2` and `:131`
`_cam.allowMSAA = false` — exactly as §1 states.

But §4 asks the regression to pin **"RT sample count == pipeline sample count"**, which
contradicts §3's own instruction not to follow the URP asset's `m_MSAA: 2`. Under the
sanctioned fix the RT is 1 and the pipeline asset is 2, so that pin is **unmeetable**: it
would go RED on the correct fix and GREEN on the shipped defect.

**The correct pin is RT == CAMERA.** Proof that the pipeline asset is not the discriminator:
`Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs:1461-1464` records the *identical* error
string from the *opposite* mismatch — RT at the default 1 while the camera still asked for
`m_MSAA: 2` — and was fixed by setting `allowMSAA = false`, not by raising the RT. One error
string, two mismatches in opposite directions; only the agreement is load-bearing.

Chosen fix (per §2's preferred branch): **`antiAliasing = 1`**, camera left at
`allowMSAA = false`. Working sibling with the same pairing: `TalentNodeVfxRig.cs:139` + `:235`.

Acceptance line 2 as implemented: **regression pins `allowMSAA == false` ⇒ RT `antiAliasing == 1`,
measured off the live rig** (`Assets/Editor/Regression/PreviewRenderTextureSamplesRegression.cs`,
markers `PREVIEW_RT_SAMPLES_OK` / `_FAIL`, registered as the `preview-rt-samples` suite).

**OUT-OF-SILO FINDING — needs its own WO.** `Assets/_Modules/Village/Hero/HeroPreviewViewer.cs:101`
(`antiAliasing = 2`) with `:227` (`allowMSAA = false`) is the **same defect, untouched**. It was
deliberately left alone and the WO-1451 lint is scoped to `TowerPreviewCamera.cs` only.
