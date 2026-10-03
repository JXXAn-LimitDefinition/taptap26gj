#ifndef GTAO_PASSES_INCLUDED
#define GTAO_PASSES_INCLUDED

// =====================================================================================
//  GTAO fragment entry points.  One entry point per full-screen pass declared in
//  GTAO.shader.  All of them share the Vert() from core Blit.hlsl, so the source of a
//  blit is always available as _BlitTexture.
// =====================================================================================

// -------------------------------------------------------------------------------------
// Pass 0 - resolve: evaluate GTAO and write the raw (unfiltered) result.
//   SV_Target0 : R = AO, G = linear depth
//   SV_Target1 : RGB = world space bent normal
// -------------------------------------------------------------------------------------
void GTAO_Resolve_frag(Varyings input,
                       out half4 outOcclusion : SV_Target0,
                       out half4 outBentNormal : SV_Target1)
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half linearDepth;
    half4 gtao = GTAO(input.texcoord, (int)_GTAO_Directions, (int)_GTAO_Steps, linearDepth);

    outOcclusion = half4(gtao.a, linearDepth, 0.0, 1.0);

    // The algorithm works in the flipped view space, convert the bent normal back to
    // world space so the debug view (and any future reflection occlusion use) is sane.
    half3 bentNormalVS = half3(gtao.rg, -gtao.b);
    half3 bentNormalWS = mul((half3x3)UNITY_MATRIX_I_V, bentNormalVS);
    outBentNormal = half4(bentNormalWS, 1.0);
}

// -------------------------------------------------------------------------------------
// Pass 1 / 2 - separable cross bilateral spatial filter.
// -------------------------------------------------------------------------------------
half2 GTAO_SpatialX_frag(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    return GTAO_BilateralBlur(input.texcoord, half2(_GTAO_RT_TexelSize.x, 0.0));
}

half2 GTAO_SpatialY_frag(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    return GTAO_BilateralBlur(input.texcoord, half2(0.0, _GTAO_RT_TexelSize.y));
}

// -------------------------------------------------------------------------------------
// Pass 3 - temporal filter: reproject the history with motion vectors and clamp it to
// the current 3x3 neighbourhood (variance clipping) to remove ghosting.
// -------------------------------------------------------------------------------------
half2 GTAO_Temporal_frag(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float2 uv = input.texcoord;
    float2 velocity = SAMPLE_TEXTURE2D_X(_MotionVectorTexture, sampler_PointClamp, uv).rg;

    half4 minColor, maxColor, current;
    GTAO_ResolveAABB(uv, _GTAO_TemporalScale, minColor, maxColor, current);

    float2 historyUV = uv - velocity;
    half4 history = GTAO_SampleHistory(historyUV);
    history = clamp(history, minColor, maxColor);

    // History outside of the viewport is meaningless.
    if (historyUV.x < 0.0 || historyUV.x > 1.0 || historyUV.y < 0.0 || historyUV.y > 1.0)
        history = current;

    // Disocclusion heuristic: the faster a pixel moves the less history we trust.
    half historyWeight = saturate(clamp(_GTAO_TemporalResponse, 0.0, 0.98) * (1.0 - length(velocity) * 8.0));

    return lerp(current, history, historyWeight).rg;
}

// -------------------------------------------------------------------------------------
// Pass 4 - composite: multiply the opaque scene colour by the filtered AO.
//   _BlitTexture is the scene colour copy, _GTAO_TemporalTexture is the AO.
// -------------------------------------------------------------------------------------
half4 GTAO_Composite_frag(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float2 uv = input.texcoord;
    half ao = SAMPLE_TEXTURE2D_X(_GTAO_TemporalTexture, sampler_PointClamp, uv).r;

    // Multi bounce needs the deferred albedo; it is skipped in Forward.
#if defined(_GTAO_MULTI_BOUNCE)
    half3 albedo = SAMPLE_TEXTURE2D_X(_GBuffer0, sampler_PointClamp, uv).rgb;
    ao = GTAO_MultiBounce(ao, albedo);
#endif

    half3 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
    ao = lerp(1.0, ao, _GTAO_Intensity);

    return half4(sceneColor * ao, 1.0);
}

// -------------------------------------------------------------------------------------
// Pass 5 / 6 - debug views.
// -------------------------------------------------------------------------------------
half4 GTAO_DebugAO_frag(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half ao = SAMPLE_TEXTURE2D_X(_GTAO_TemporalTexture, sampler_PointClamp, input.texcoord).r;
#if defined(_GTAO_MULTI_BOUNCE)
    half3 albedo = SAMPLE_TEXTURE2D_X(_GBuffer0, sampler_PointClamp, input.texcoord).rgb;
    ao = GTAO_MultiBounce(ao, albedo);
#endif
    return half4(ao, ao, ao, 1.0);
}

half4 GTAO_DebugBentNormal_frag(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half3 bentNormal = SAMPLE_TEXTURE2D_X(_GTAO_BentNormalTexture, sampler_PointClamp, input.texcoord).rgb;
    return half4(bentNormal * 0.5 + 0.5, 1.0);
}

#endif // GTAO_PASSES_INCLUDED
