# taptap26gj

A Unity **2022.3.62f1c1** project built around a **URP port of Ground Truth Ambient
Occlusion (GTAO)**.

## Highlights

* `Assets/GTAO/` — a clean, documented URP implementation of GTAO:
  horizon-based ambient occlusion + bent normals, separable cross bilateral filter,
  motion-vector temporal reprojection with variance clipping and an optional
  multi-bounce term.
* `Assets/Scenes/AOTestScene.unity` — a procedurally generated, medium sized courtyard
  (colonnade, arches, stairs, props, overhang) used to evaluate the effect.
* `Assets/Legacy_BuiltIn_GTAO/` — the original Built-in Render Pipeline implementation,
  kept for reference and comparison.

## Getting started

1. Open the project with Unity **2022.3.62f1c1** (URP 14.0.12).
2. Open `Assets/Scenes/AOTestScene.unity` and press **Play**.
3. Everything is pre-configured. To re-apply the setup at any time use
   **Tools ▸ GTAO ▸ Setup Everything (URP + Scene)**.

See [`Assets/GTAO/Documentation/README.md`](Assets/GTAO/Documentation/README.md) for the
full architecture, algorithm walk-through and tuning reference, and
[`Assets/GTAO/Documentation/GTAO_OVERVIEW.md`](Assets/GTAO/Documentation/GTAO_OVERVIEW.md)
for a line-by-line mapping between this implementation and the Activision GTAO paper
(Jimenez et al., *Practical Realtime Strategies for Accurate Indirect Occlusion*).
