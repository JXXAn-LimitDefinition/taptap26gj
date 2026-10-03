using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GTAO
{
    /// <summary>
    /// URP entry point for Ground Truth Ambient Occlusion.
    ///
    /// Add this feature to a <c>UniversalRendererData</c> asset (Rendering &gt; Renderer
    /// Features &gt; Add Renderer Feature) and it will:
    ///   * request depth / normals / motion vectors from URP,
    ///   * run the GTAO pass after opaque geometry,
    ///   * multiply the opaque colour by the filtered occlusion.
    ///
    /// The implementation targets the <b>Deferred</b> rendering path so that the
    /// multi-bounce term can read the albedo G-buffer.  Forward still works with
    /// multi-bounce disabled.
    /// </summary>
    [DisallowMultipleRendererFeature("Ground Truth Ambient Occlusion")]
    [Tooltip("Screen space ground truth ambient occlusion (URP port of the Built-in pipeline version).")]
    public class GTAORendererFeature : ScriptableRendererFeature
    {
        [SerializeField] public GTAOSettings settings = new GTAOSettings();

        private Material m_Material;
        private GTAORenderPass m_Pass;

        public override void Create()
        {
            Shader shader = settings.shader != null ? settings.shader : Shader.Find(GTAOShaderIDs.ShaderName);
            if (shader == null)
            {
                // Shader not imported yet (or stripped from the build). Disable cleanly.
                m_Pass = null;
                return;
            }

            if (m_Material == null || m_Material.shader != shader)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = CoreUtils.CreateEngineMaterial(shader);
            }

            m_Pass = new GTAORenderPass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_Pass == null || m_Material == null)
                return;

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return;
            if (cameraType == CameraType.SceneView && !settings.applyInSceneView)
                return;

            m_Pass.Setup(settings, m_Material);
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            m_Pass?.Dispose();
            m_Pass = null;
            CoreUtils.Destroy(m_Material);
            m_Material = null;
        }
    }
}
