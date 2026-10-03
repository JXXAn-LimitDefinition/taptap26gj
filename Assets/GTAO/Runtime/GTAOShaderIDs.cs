using UnityEngine;

namespace GTAO
{
    /// <summary>
    /// Central place for every shader property / keyword name used by the GTAO feature.
    /// Looking them up once with <see cref="Shader.PropertyToID(string)"/> avoids the
    /// per-frame string hashing that <c>Material.SetFloat("_Name", ...)</c> would do.
    /// </summary>
    public static class GTAOShaderIDs
    {
        public const string ShaderName      = "Hidden/GTAO/URP";
        public const string MultiBounceKw   = "_GTAO_MULTI_BOUNCE";
        public const string GbufferNormalsKw = "_GBUFFER_NORMALS_OCT";

        // RTHandle debug names (visible in the Frame Debugger).
        public static readonly string AOTextureName         = "_GTAO_AOTexture";
        public static readonly string BentNormalTextureName = "_GTAO_BentNormalTexture";
        public static readonly string SpatialTextureName    = "_GTAO_SpatialTexture";
        public static readonly string TemporalTextureName   = "_GTAO_TemporalTexture";
        public static readonly string HistoryTextureName    = "_GTAO_HistoryTexture";
        public static readonly string SceneColorName        = "_GTAO_SceneColor";

        // Uniforms.
        public static readonly int Directions        = Shader.PropertyToID("_GTAO_Directions");
        public static readonly int Steps             = Shader.PropertyToID("_GTAO_Steps");
        public static readonly int Radius            = Shader.PropertyToID("_GTAO_Radius");
        public static readonly int Intensity         = Shader.PropertyToID("_GTAO_Intensity");
        public static readonly int Power             = Shader.PropertyToID("_GTAO_Power");
        public static readonly int Sharpness         = Shader.PropertyToID("_GTAO_Sharpness");
        public static readonly int TemporalScale     = Shader.PropertyToID("_GTAO_TemporalScale");
        public static readonly int TemporalResponse  = Shader.PropertyToID("_GTAO_TemporalResponse");
        public static readonly int HalfProjScale     = Shader.PropertyToID("_GTAO_HalfProjScale");
        public static readonly int TemporalOffset    = Shader.PropertyToID("_GTAO_TemporalOffset");
        public static readonly int TemporalDirection = Shader.PropertyToID("_GTAO_TemporalDirection");
        public static readonly int UVToView          = Shader.PropertyToID("_GTAO_UVToView");
        public static readonly int RTTexelSize       = Shader.PropertyToID("_GTAO_RT_TexelSize");
        public static readonly int FadeParams        = Shader.PropertyToID("_GTAO_FadeParams");
        public static readonly int FadeValues        = Shader.PropertyToID("_GTAO_FadeValues");

        // Textures.
        public static readonly int TemporalTexture    = Shader.PropertyToID("_GTAO_TemporalTexture");
        public static readonly int HistoryTexture     = Shader.PropertyToID("_GTAO_HistoryTexture");
        public static readonly int BentNormalTexture  = Shader.PropertyToID("_GTAO_BentNormalTexture");

        // Blit helpers.
        public static readonly int BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
    }
}
