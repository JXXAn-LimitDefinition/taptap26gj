using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GTAO
{
    /// <summary>Which buffer to visualise instead of the lit scene.</summary>
    public enum GTAODebugMode
    {
        Off = 0,
        AmbientOcclusion = 1,
        BentNormal = 2,
    }

    /// <summary>
    /// All tweakable parameters of the GTAO feature.  Serialized on the renderer feature
    /// asset so that an artist can tune it per renderer without touching code.
    /// </summary>
    [Serializable]
    public class GTAOSettings
    {
        [Tooltip("When in the frame the effect runs. AfterRenderingOpaques matches the " +
                 "original Built-in pipeline injection point (before transparents).")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

        [Header("Quality")]
        [Tooltip("Number of slices cast per pixel.")]
        [Range(1, 4)] public int directions = 2;

        [Tooltip("Number of samples taken along each slice.")]
        [Range(1, 8)] public int steps = 2;

        [Header("Occlusion")]
        [Tooltip("World space radius of the occlusion search.")]
        [Range(0.05f, 10f)] public float radius = 2.5f;

        [Tooltip("Blend between no AO (0) and full AO (1).")]
        [Range(0f, 1f)] public float intensity = 1f;

        [Tooltip("Contrast curve applied to the occlusion term.")]
        [Range(1f, 8f)] public float power = 2.5f;

        [Tooltip("Approximate multi-bounce using the deferred albedo. Requires the " +
                 "Deferred rendering path.")]
        public bool multiBounce = true;

        [Header("Spatial Filter")]
        [Tooltip("Depth falloff of the cross bilateral blur. Higher = AO stays on " +
                 "geometry with more similar depth.")]
        [Range(0f, 1f)] public float sharpness = 0.25f;

        [Header("Temporal Filter")]
        public bool temporalFilter = true;

        [Tooltip("Variance clipping scale for the history. 1 = mean +/- 1 stddev.")]
        [Range(0f, 3f)] public float temporalScale = 1f;

        [Tooltip("How much of the previous frame is kept. Higher = more stable but " +
                 "more ghosting.")]
        [Range(0f, 0.98f)] public float temporalResponse = 0.9f;

        [Header("Distance Fade")]
        [Tooltip("Distance at which the AO starts to fade out. Keep large to disable.")]
        public float fadeStart = 1000f;

        [Tooltip("Distance at which the AO is fully faded out.")]
        public float fadeEnd = 1100f;

        [Tooltip("Radius multiplier applied at fadeEnd.")]
        [Range(0.1f, 1f)] public float fadeRadiusScale = 1f;

        [Tooltip("Thickness multiplier applied at fadeEnd.")]
        [Range(0f, 1f)] public float fadeThicknessScale = 1f;

        [Header("Debug")]
        public GTAODebugMode debugMode = GTAODebugMode.Off;

        [Tooltip("Also run the effect in the Scene view.")]
        public bool applyInSceneView = false;

        [Tooltip("Optional override. When empty the shader Hidden/GTAO/URP is used.")]
        public Shader shader;
    }
}
