using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GTAO
{
    /// <summary>
    /// The single render pass that implements Ground Truth Ambient Occlusion.
    ///
    /// Execution order (all full-screen draws):
    ///   Resolve  -> a render target holding (AO, linear depth) plus a bent normal texture
    ///   SpatialX -> horizontal cross bilateral blur
    ///   SpatialY -> vertical cross bilateral blur
    ///   Temporal -> reproject + variance clip the history
    ///   Composite-> multiply the opaque scene colour by the AO
    ///
    /// The pass owns its intermediate RTHandles and resizes them on demand, so it is
    /// safe to use with dynamic resolution / window resizing.
    /// </summary>
    public class GTAORenderPass : ScriptableRenderPass
    {
        // Shader pass indices, keep in sync with GTAO.shader.
        private const int k_PassResolve         = 0;
        private const int k_PassSpatialX        = 1;
        private const int k_PassSpatialY        = 2;
        private const int k_PassTemporal        = 3;
        private const int k_PassComposite       = 4;
        private const int k_PassDebugAO         = 5;
        private const int k_PassDebugBentNormal = 6;

        private static readonly ProfilingSampler s_Profiler = new ProfilingSampler("Ground Truth Ambient Occlusion");
        private static readonly float[] s_TemporalRotations = { 60f, 300f, 180f, 240f, 120f, 0f };
        private static readonly float[] s_SpatialOffsets    = { 0f, 0.5f, 0.25f, 0.75f };

        private GTAOSettings m_Settings;
        private Material m_Material;

        private RTHandle m_AO;          // R = AO, G = linear depth
        private RTHandle m_BentNormal;  // world space bent normal
        private RTHandle m_Spatial;     // bilateral filter ping-pong target
        private RTHandle m_Temporal;    // filtered result for this frame
        private RTHandle m_History;     // same as m_Temporal but from the previous frame
        private RTHandle m_SceneColor;  // copy of the camera colour so we can read & write

        private bool m_HistoryReset = true;
        private uint m_FrameIndex;

        public GTAORenderPass(GTAOSettings settings)
        {
            m_Settings = settings;
            // Handy name in the Frame Debugger / profiler.
            profilingSampler = s_Profiler;
        }

        /// <summary>Called every frame by the feature before the pass is enqueued.</summary>
        public void Setup(GTAOSettings settings, Material material)
        {
            m_Settings = settings;
            m_Material = material;

            // Tell URP which G-Buffer attachments this pass needs.  Because we ask for
            // Motion / Normals, URP will produce them even if no other effect needs them.
            var inputs = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;
            if (settings.temporalFilter)
                inputs |= ScriptableRenderPassInput.Motion;
            ConfigureInput(inputs);

            renderPassEvent = settings.renderPassEvent;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_Material == null || m_Settings == null)
                return;

            ref CameraData cameraData = ref renderingData.cameraData;
            Camera camera = cameraData.camera;

            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
                return;

            RTHandle cameraTarget = cameraData.renderer.cameraColorTargetHandle;
            if (cameraTarget == null || cameraTarget.rt == null)
                return;

            AllocateTextures(cameraData, out bool historyReallocated);
            if (historyReallocated)
                m_HistoryReset = true;

            CommandBuffer cmd = CommandBufferPool.Get();
            cmd.name = "Ground Truth Ambient Occlusion";

            using (new ProfilingScope(cmd, s_Profiler))
            {
                UpdateMaterial(cameraData, cmd);
                Render(cmd, cameraData, cameraTarget);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        // ---------------------------------------------------------------------------------
        // Resource management
        // ---------------------------------------------------------------------------------
        private void AllocateTextures(in CameraData cameraData, out bool historyReallocated)
        {
            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;

            // AO + linear depth only needs two channels.
            var aoDesc = desc;
            aoDesc.graphicsFormat = GraphicsFormat.R16G16_SFloat;
            RenderingUtils.ReAllocateIfNeeded(ref m_AO, aoDesc, FilterMode.Point, TextureWrapMode.Clamp,
                name: GTAOShaderIDs.AOTextureName);

            RenderingUtils.ReAllocateIfNeeded(ref m_Spatial, aoDesc, FilterMode.Point, TextureWrapMode.Clamp,
                name: GTAOShaderIDs.SpatialTextureName);

            RenderingUtils.ReAllocateIfNeeded(ref m_Temporal, aoDesc, FilterMode.Point, TextureWrapMode.Clamp,
                name: GTAOShaderIDs.TemporalTextureName);

            historyReallocated = RenderingUtils.ReAllocateIfNeeded(ref m_History, aoDesc, FilterMode.Point, TextureWrapMode.Clamp,
                name: GTAOShaderIDs.HistoryTextureName);

            // Bent normal is a direction in world space -> three channels.
            var bentDesc = desc;
            bentDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            RenderingUtils.ReAllocateIfNeeded(ref m_BentNormal, bentDesc, FilterMode.Point, TextureWrapMode.Clamp,
                name: GTAOShaderIDs.BentNormalTextureName);

            // Copy of the camera colour (kept in the camera's own format so HDR survives).
            RenderingUtils.ReAllocateIfNeeded(ref m_SceneColor, desc, FilterMode.Point, TextureWrapMode.Clamp,
                name: GTAOShaderIDs.SceneColorName);
        }

        public void Dispose()
        {
            m_AO?.Release();
            m_BentNormal?.Release();
            m_Spatial?.Release();
            m_Temporal?.Release();
            m_History?.Release();
            m_SceneColor?.Release();
        }

        // ---------------------------------------------------------------------------------
        // Uniforms
        // ---------------------------------------------------------------------------------
        private void UpdateMaterial(in CameraData cameraData, CommandBuffer cmd)
        {
            Camera camera = cameraData.camera;
            float width  = cameraData.cameraTargetDescriptor.width;
            float height = cameraData.cameraTargetDescriptor.height;

            m_Material.SetFloat(GTAOShaderIDs.Directions, m_Settings.directions);
            m_Material.SetFloat(GTAOShaderIDs.Steps, m_Settings.steps);
            m_Material.SetFloat(GTAOShaderIDs.Radius, m_Settings.radius);
            m_Material.SetFloat(GTAOShaderIDs.Intensity, m_Settings.intensity);
            m_Material.SetFloat(GTAOShaderIDs.Power, m_Settings.power);
            m_Material.SetFloat(GTAOShaderIDs.Sharpness, m_Settings.sharpness);
            m_Material.SetFloat(GTAOShaderIDs.TemporalScale, m_Settings.temporalScale);

            // On the frame the history was (re)allocated there is nothing to blend with.
            float temporalResponse = m_HistoryReset ? 0f : m_Settings.temporalResponse;
            m_Material.SetFloat(GTAOShaderIDs.TemporalResponse, temporalResponse);
            m_HistoryReset = false;

            // Projection dependent values, mirroring the original Built-in implementation
            // so the screen space radius behaves identically.
            float fovRad = camera.fieldOfView * Mathf.Deg2Rad;
            float invHalfTanFov = 1f / Mathf.Tan(fovRad * 0.5f);
            Vector2 focalLen = new Vector2(invHalfTanFov * (height / width), invHalfTanFov);
            Vector2 invFocalLen = new Vector2(1f / focalLen.x, 1f / focalLen.y);
            m_Material.SetVector(GTAOShaderIDs.UVToView,
                new Vector4(2f * invFocalLen.x, 2f * invFocalLen.y, -invFocalLen.x, -invFocalLen.y));

            float halfProjScale = height / (Mathf.Tan(fovRad * 0.5f) * 2f) * 0.5f;
            m_Material.SetFloat(GTAOShaderIDs.HalfProjScale, halfProjScale);

            m_Material.SetVector(GTAOShaderIDs.RTTexelSize, new Vector4(1f / width, 1f / height, width, height));

            // Sub-pixel jitter so the low sample count converges over time.
            float rotation = s_TemporalRotations[m_FrameIndex % s_TemporalRotations.Length];
            float offset   = s_SpatialOffsets[(m_FrameIndex / (uint)s_TemporalRotations.Length) % s_SpatialOffsets.Length];
            m_Material.SetFloat(GTAOShaderIDs.TemporalDirection, rotation / 360f);
            m_Material.SetFloat(GTAOShaderIDs.TemporalOffset, offset);
            m_FrameIndex++;

            // Distance fade (disabled by default: fadeStart is far away).
            float fadeRange = Mathf.Max(0.001f, m_Settings.fadeEnd - m_Settings.fadeStart);
            m_Material.SetVector(GTAOShaderIDs.FadeParams,
                new Vector4(m_Settings.fadeStart, 1f / fadeRange, 0f, 0f));
            m_Material.SetVector(GTAOShaderIDs.FadeValues,
                new Vector4(0f, m_Settings.fadeRadiusScale, 0f, m_Settings.fadeThicknessScale));

            // Multi bounce reads the deferred albedo.
            CoreUtils.SetKeyword(cmd, GTAOShaderIDs.MultiBounceKw, m_Settings.multiBounce);
        }

        // ---------------------------------------------------------------------------------
        // Rendering
        // ---------------------------------------------------------------------------------
        private void Render(CommandBuffer cmd, in CameraData cameraData, RTHandle cameraTarget)
        {
            // ---- 1. Resolve: raw AO + bent normal into an MRT -------------------------
            cmd.SetGlobalTexture(GTAOShaderIDs.BentNormalTexture, m_BentNormal);
            cmd.SetGlobalTexture(GTAOShaderIDs.TemporalTexture, m_Temporal);

            // DrawFullScreen does not set the blit scale/bias, and Vert() below samples it,
            // so make sure it is the identity here.
            cmd.SetGlobalVector(GTAOShaderIDs.BlitScaleBias, new Vector4(1f, 1f, 0f, 0f));
            CoreUtils.DrawFullScreen(cmd, m_Material,
                new RenderTargetIdentifier[] { m_AO, m_BentNormal }, null, k_PassResolve);

            // ---- 2. Separable spatial (cross bilateral) filter ------------------------
            Blit(cmd, m_AO, m_Spatial, m_Material, k_PassSpatialX);
            Blit(cmd, m_Spatial, m_AO, m_Material, k_PassSpatialY);

            // ---- 3. Temporal filter ---------------------------------------------------
            if (m_Settings.temporalFilter)
            {
                cmd.SetGlobalTexture(GTAOShaderIDs.HistoryTexture, m_History);
                Blit(cmd, m_AO, m_Temporal, m_Material, k_PassTemporal);

                // m_Temporal becomes the history for the next frame.
                Blitter.BlitCameraTexture(cmd, m_Temporal, m_History);
            }
            else
            {
                // No history: the spatially filtered AO is used directly.
                Blitter.BlitCameraTexture(cmd, m_AO, m_Temporal);
            }

            cmd.SetGlobalTexture(GTAOShaderIDs.TemporalTexture, m_Temporal);

            // ---- 4. Composite ---------------------------------------------------------
            // Read and write the same camera target is not allowed, so copy it first.
            Blitter.BlitCameraTexture(cmd, cameraTarget, m_SceneColor);

            int compositePass;
            switch (m_Settings.debugMode)
            {
                case GTAODebugMode.AmbientOcclusion: compositePass = k_PassDebugAO; break;
                case GTAODebugMode.BentNormal:       compositePass = k_PassDebugBentNormal; break;
                default:                              compositePass = k_PassComposite; break;
            }

            Blit(cmd, m_SceneColor, cameraTarget, m_Material, compositePass);
        }
    }
}
