# Ground Truth Ambient Occlusion for Unity 2022.3 URP

This folder contains a **URP port** of the Built-in Render Pipeline GTAO that shipped with
this repository (`Assets/Legacy_BuiltIn_GTAO`). The original algorithm is preserved, but
every piece of pipeline plumbing has been replaced with the URP equivalent so the effect
can be used on a `UniversalRendererData` like any other renderer feature.

* Engine: **Unity 2022.3.62f1c1**
* Pipeline: **Universal Render Pipeline 14.0.12**
* Rendering path: **Deferred** (required for the multi-bounce term, see below)

---

## 1. Quick start

1. Open the project with Unity **2022.3.62f1c1**.
2. Open `Assets/Scenes/AOTestScene.unity` and press **Play**.
3. If the scene or the renderer feature is ever missing, run **Tools ▸ GTAO ▸ Setup
   Everything (URP + Scene)**. The same method is available head-less:
   `-executeMethod GTAO.Editor.GTAOSetup.SetupEverything`.

That wizard

* switches `Assets/Settings/URP-HighFidelity-Renderer.asset` to the **Deferred** path,
* adds the `GTAORendererFeature` to that renderer,
* regenerates the AO test scene and registers it in the build settings.

---

## 2. What is in this folder

```
Assets/GTAO/
├── Runtime/
│   ├── GTAORendererFeature.cs   URP entry point (ScriptableRendererFeature)
│   ├── GTAORenderPass.cs        The full GTAO pipeline as one ScriptableRenderPass
│   ├── GTAOSettings.cs          All tweakable parameters
│   └── GTAOShaderIDs.cs         Cached shader property / keyword ids
├── Shaders/
│   ├── GTAO.shader              Pass declarations (Resolve / Spatial / Temporal / Composite / Debug)
│   ├── GTAO_Common.hlsl         The algorithm: reconstruction, GTAO, filters
│   └── GTAO_Passes.hlsl         Fragment entry points
├── Editor/
│   ├── GTAOSetup.cs             One-click project setup
│   ├── GTAOTestSceneBuilder.cs  Procedural "medium sized" AO test scene
│   └── GTAORenderCheck.cs       Head-less before/after + debug image capture
└── Documentation/
    └── README.md                (this file)
```

The old implementation is kept for reference under
`Assets/Legacy_BuiltIn_GTAO/` — it is **not used** by the URP renderer.

---

## 3. The test scene

`GTAOTestSceneBuilder` procedurally builds a deterministic, medium sized courtyard
(about 300 objects, no external assets) that is designed to stress ambient occlusion:

| Feature | What it tests |
|---|---|
| Colonnade + semicircular arches | deep concavities, long screen-space horizons |
| Raised dais + 6 steps | occlusion along gradients |
| Roof overhang + beams | large scale occlusion, contact shadows |
| Crates / barrels / spheres / poles | small contact AO, thin occluders, curved bent normals |
| Directional sun + two point lights | occlusion against both direct and ambient light |

Regenerate it any time with **Tools ▸ GTAO ▸ Build Ambient Occlusion Test Scene**. The
random seed is fixed, so the layout is identical on every machine.

---

## 4. How it is wired into URP

The port keeps the exact pass order of the original renderer feature:

| Original (Built-in) | URP port |
|---|---|
| `CommandBuffer` on `CameraEvent.BeforeImageEffectsOpaque` | `ScriptableRenderPass` at `RenderPassEvent.AfterRenderingOpaques` |
| `_CameraGBufferTexture2` (world normal) | `SampleSceneNormals()` (`_CameraNormalsTexture`, produced by `ConfigureInput(Normal)`) |
| `_CameraDepthTexture` | `SampleSceneDepth()` + `LinearEyeDepth` (`ConfigureInput(Depth)`) |
| `_CameraMotionVectorsTexture` | `_MotionVectorTexture` (`ConfigureInput(Motion)`) |
| `_CameraGBufferTexture0` (albedo) | `_GBuffer0` (deferred only) — multi-bounce |
| `Blit` / `DrawMesh` full-screen quad | `Blitter.BlitCameraTexture` + core `Blit.hlsl` (`Vert`, `_BlitTexture`) |
| `RenderTexture` allocation | `RTHandle` + `RenderingUtils.ReAllocateIfNeeded` (resizes automatically) |
| Script inspector sliders | `GTAOSettings` on the renderer feature asset |

`ConfigureInput` is what makes URP produce the depth / normals / motion vector textures
for this pass even when no other effect needs them.

---

## 5. Algorithm walk-through (`GTAO_Common.hlsl`)

Everything is written in a "flipped" view space where **+Z points away from the camera**,
which makes the horizon math easier to read. The only place that leaks into the code is
the `-z` on the normals.

1. **Reconstruction** — `GTAO_GetViewPosition` rebuilds the view-space position from the
   depth texture and `_GTAO_UVToView` (derived from the camera FOV). Sky pixels return
   `AO = 1` immediately.

2. **`GTAO()`** — for each of `directions` slices:
   * rotate the slice by a per-pixel noise angle plus a per-frame temporal rotation,
   * march `steps` samples on both sides of the pixel,
   * track the highest horizon angle `h` on each side, weighting far samples out with a
     distance `falloff` and thin occluders with `thickness`,
   * clamp the horizon to the tangent plane and integrate the visible arc with a cosine
     weight (`GTAO_IntegrateArc_CosWeight`, Jimenez 2016 eq. 10),
   * accumulate the bent normal from the slice's unoccluded cone.

3. **`GTAO_BilateralBlur`** — a separable cross bilateral blur (horizontal then vertical)
   that rejects samples whose linear depth differs, so AO does not bleed over silhouettes.

4. **`GTAO_Temporal_frag`** — reprojects the previous frame with motion vectors, clamps it
   to the 3×3 mean ± `temporalScale`·stddev of the current frame (variance clipping) and
   blends with `temporalResponse`.

5. **`GTAO_Composite_frag`** — multiplies the opaque scene colour by the AO. When
   `multiBounce` is on it reads the deferred albedo and applies `GTAO_MultiBounce`
   (Jimenez 2016 eq. 11) so that dark corners do not crush to black.

---

## 6. Parameters

| Group | Parameter | Meaning |
|---|---|---|
| Quality | `directions` / `steps` | Slices per pixel / samples per slice. 2/2 is the default and matches the original demo. |
| Occlusion | `radius` | World-space search radius. |
| | `intensity` | Blend between no AO (0) and full AO (1). |
| | `power` | Contrast curve of the occlusion term. |
| | `multiBounce` | Deferred albedo based multi-bounce. |
| Spatial | `sharpness` | Depth falloff of the bilateral filter. |
| Temporal | `temporalFilter` | Enable the history reprojection. |
| | `temporalScale` | Variance clipping scale (1 = ±1 stddev). |
| | `temporalResponse` | How much history is kept (higher = more stable, more ghosting). |
| Distance fade | `fadeStart` / `fadeEnd` | Fade the AO out with distance (default disabled). |
| | `fadeRadiusScale` / `fadeThicknessScale` | Radius / thickness at `fadeEnd`. |
| Debug | `debugMode` | `Off`, `AmbientOcclusion`, `BentNormal`. |
| | `applyInSceneView` | Also run in the Scene view. |

---

## 7. Verifying the port

`GTAO.Editor.GTAORenderCheck.Run` renders the test scene head-less and writes four images
to `Logs/`:

```bash
Unity -batchmode -projectPath <project> \
      -executeMethod GTAO.Editor.GTAORenderCheck.Run -quit
```

* `gtao_disabled.png` / `gtao_enabled.png` — before/after comparison,
* `gtao_debug_ao.png` — the raw AO buffer,
* `gtao_debug_bentnormal.png` — the world-space bent normal (RGB = normal · 0.5 + 0.5).

---

## 8. Limitations and extension points

* **Deferred only for multi-bounce.** The albedo term reads `_GBuffer0`. On the Forward
  path turn `multiBounce` off; AO and bent normals still work.
* **Orthographic cameras** are not supported (the view position reconstruction assumes a
  perspective projection).
* **Reflection occlusion (`GTAO_ReflectionOcclusion`)** is provided in
  `GTAO_Common.hlsl` for reference but is not applied: URP does not expose the deferred
  reflection buffer under the same global name (`_CameraReflectionsTexture`) that the
  Built-in pipeline used. To re-enable it, sample the reflection probe atlas / planar
  reflection output inside `GTAO_Composite_frag` and multiply it by the function's result.
* Only the `_GTAO_MULTI_BOUNCE` shader keyword is toggled per frame; all other parameters
  are plain uniforms so a single material instance is enough.

## 9. References

* Jimenez et al., *Practical Realtime Strategies for Accurate Indirect Occlusion*, 2016.
  <https://www.activision.com/cdn/research/Practical_Real_Time_Strategies_for_Accurate_Indirect_Occlusion.pdf>

> For a formula-by-formula mapping between this implementation and the paper, see
> [`GTAO_OVERVIEW.md`](GTAO_OVERVIEW.md) in this folder.
