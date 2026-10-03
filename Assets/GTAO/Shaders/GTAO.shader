Shader "Hidden/GTAO/URP"
{
    // =================================================================================
    //  Ground Truth Ambient Occlusion - Universal Render Pipeline (Unity 2022.3 / URP 14)
    //
    //  Every pass is a full-screen draw that shares Vert() from core Blit.hlsl.
    //  The render pass (GTAORenderPass.cs) drives them in this order:
    //
    //    Pass 0  Resolve   raw AO + bent normal
    //    Pass 1  SpatialX  horizontal bilateral blur
    //    Pass 2  SpatialY  vertical bilateral blur
    //    Pass 3  Temporal  history reprojection / variance clipping
    //    Pass 4  Composite multiply the opaque colour by the AO
    //    Pass 5  Debug AO
    //    Pass 6  Debug Bent Normal
    // =================================================================================
    Properties
    {
        // All uniforms are set from C#; the inspector block only exists so the shader
        // shows up meaningfully in the material inspector while debugging.
    }

    HLSLINCLUDE
        #pragma target 4.5
        #include "GTAO_Common.hlsl"
        #include "GTAO_Passes.hlsl"
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        ZTest Always
        ZWrite Off
        Cull Off

        // ---------------------------------------------------------------- resolve ---
        Pass
        {
            Name "GTAO Resolve"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment GTAO_Resolve_frag
                #pragma multi_compile _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }

        // ------------------------------------------------------------- spatial X -----
        Pass
        {
            Name "GTAO Spatial X"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment GTAO_SpatialX_frag
            ENDHLSL
        }

        // ------------------------------------------------------------- spatial Y -----
        Pass
        {
            Name "GTAO Spatial Y"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment GTAO_SpatialY_frag
            ENDHLSL
        }

        // ------------------------------------------------------------- temporal ------
        Pass
        {
            Name "GTAO Temporal"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment GTAO_Temporal_frag
            ENDHLSL
        }

        // ------------------------------------------------------------- composite -----
        Pass
        {
            Name "GTAO Composite"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment GTAO_Composite_frag
                #pragma multi_compile _ _GTAO_MULTI_BOUNCE
            ENDHLSL
        }

        // ------------------------------------------------------------- debug AO ------
        Pass
        {
            Name "GTAO Debug AO"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment GTAO_DebugAO_frag
                #pragma multi_compile _ _GTAO_MULTI_BOUNCE
            ENDHLSL
        }

        // ------------------------------------------------------------- debug normal --
        Pass
        {
            Name "GTAO Debug Bent Normal"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment GTAO_DebugBentNormal_frag
            ENDHLSL
        }
    }

    Fallback Off
}
