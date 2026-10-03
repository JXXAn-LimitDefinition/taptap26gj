#ifndef GTAO_COMMON_INCLUDED
#define GTAO_COMMON_INCLUDED

// =====================================================================================
//  Ground Truth Ambient Occlusion (GTAO) - shared shader code for URP 14 (Unity 2022.3)
// -------------------------------------------------------------------------------------
//  This file holds the *algorithm*.  It is deliberately kept free of any pipeline
//  plumbing so that it can be read top-to-bottom like a paper:
//
//    1. screen-space helpers   -> reconstruct view position / view normal from the
//                                 G-Buffer that URP already produced
//    2. GTAO()                 -> horizon based occlusion + bent normal
//    3. bilateral filter       -> spatial denoise of the raw AO
//    4. temporal AABB filter   -> reproject the previous frame and clamp to the
//                                 current 3x3 neighbourhood (ghost free)
//
//  Coordinate space note
//  ---------------------
//  Unity's view space looks down -Z (a point in front of the camera has a negative z).
//  GTAO is easier to reason about when "forward = +Z", so every helper below returns
//  positions/normals in a *flipped* view space where z is positive in front of the
//  camera.  That is the only place the `-z` terms come from.
//
//  References
//  ----------
//    Jimenez et al. "Practical Realtime Strategies for Accurate Indirect Occlusion" (2016)
//    https://www.activision.com/cdn/research/Practical_Real_Time_Strategies_for_Accurate_Indirect_Occlusion.pdf
// =====================================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
// Vert()/_BlitTexture/_BlitScaleBias used by the full-screen passes.
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

// -------------------------------------------------------------------------------------
// Uniforms - written every frame by GTAORenderPass.cs
// -------------------------------------------------------------------------------------
float  _GTAO_Directions;        // slices per pixel            (quality)
float  _GTAO_Steps;             // samples per slice           (quality)
float  _GTAO_Radius;            // world space AO radius
float  _GTAO_Intensity;         // 0..1 blend of the effect
float  _GTAO_Power;             // contrast of the occlusion term
float  _GTAO_Sharpness;         // bilateral filter depth falloff
float  _GTAO_TemporalScale;     // AABB clamp scale
float  _GTAO_TemporalResponse;  // history blend weight
float  _GTAO_HalfProjScale;     // 0.5 * height / tan(fov/2)
float  _GTAO_TemporalOffset;    // per-frame spatial jitter   (0, .5, .25, .75)
float  _GTAO_TemporalDirection; // per-frame rotation jitter  (0..1)
float4 _GTAO_UVToView;          // (2/fx, 2/fy, -1/fx, -1/fy) -> uv * xy + zw = NDC * invFocal
float4 _GTAO_RT_TexelSize;      // (1/width, 1/height, width, height)
float4 _GTAO_FadeParams;        // (fadeStart, 1/(fadeEnd-fadeStart), 0, 0)
float4 _GTAO_FadeValues;        // (0, radiusAtFade, 0, thicknessAtFade)

// Intermediate textures (bound with cmd.SetGlobalTexture by the render pass).
TEXTURE2D_X(_GTAO_TemporalTexture);   // RG = (AO, linear depth), temporally filtered
TEXTURE2D_X(_GTAO_HistoryTexture);    // same as above, previous frame
TEXTURE2D_X(_GTAO_BentNormalTexture);// RGB = world space bent normal

// Motion vectors are provided by URP when the pass requests
// ScriptableRenderPassInput.Motion.
TEXTURE2D_X_FLOAT(_MotionVectorTexture);

// Deferred G-buffer albedo, only valid on the Deferred rendering path.
TEXTURE2D_X_HALF(_GBuffer0);
TEXTURE2D_X_HALF(_GBuffer1);
TEXTURE2D_X_HALF(_GBuffer2);

#define GTAO_KERNEL_RADIUS 8

// -------------------------------------------------------------------------------------
// Screen space reconstruction
// -------------------------------------------------------------------------------------

// Raw depth -> linear eye depth (positive distance along the camera forward axis).
float GTAO_LinearDepth(float2 uv)
{
    return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
}

// Reconstruct the view space position of a pixel.  `viewDepth` is the linear eye
// depth, which is exactly the +Z coordinate in GTAO's flipped view space.
float3 GTAO_GetViewPosition(float2 uv, float eyeDepth)
{
    return float3((uv * _GTAO_UVToView.xy + _GTAO_UVToView.zw) * eyeDepth, eyeDepth);
}

float3 GTAO_GetViewPosition(float2 uv)
{
    return GTAO_GetViewPosition(uv, GTAO_LinearDepth(uv));
}

// World space normal -> GTAO flipped view space (z negated to make +Z = forward).
half3 GTAO_GetViewNormal(float2 uv)
{
    half3 normalWS = normalize(SampleSceneNormals(uv));
    half3 normalVS = mul((half3x3)UNITY_MATRIX_V, normalWS);
    return half3(normalVS.xy, -normalVS.z);
}

// Distance based fade so large scale geometry does not get a uniform AO wash.
half GTAO_ComputeDistanceFade(half distance)
{
    return saturate(max(0.0, distance - _GTAO_FadeParams.x) * _GTAO_FadeParams.y);
}

// -------------------------------------------------------------------------------------
// Noise - a cheap interleaved gradient noise.  The per-pixel offset decorrelates the
// slice directions so the sample pattern does not show up as banding.
// -------------------------------------------------------------------------------------
half GTAO_Offsets(float2 uv)
{
    int2 position = (int2)(uv * _GTAO_RT_TexelSize.zw);
    return 0.25 * (half)((position.y - position.x) & 3);
}

half GTAO_Noise(float2 position)
{
    return frac(52.9829189 * frac(dot(position, half2(0.06711056, 0.00583715))));
}

// -------------------------------------------------------------------------------------
// Horizon integration
// -------------------------------------------------------------------------------------
// Analytic integral of the visible horizon arc, uniform weight (Jimenez 2016 eq. 9).
half GTAO_IntegrateArc_UniformWeight(half2 h)
{
    half2 arc = 1.0 - cos(h);
    return arc.x + arc.y;
}

// Analytic integral with a cosine (Lambert) weight (Jimenez 2016 eq. 10).
half GTAO_IntegrateArc_CosWeight(half2 h, half n)
{
    half2 arc = -cos(2.0 * h - n) + cos(n) + 2.0 * h * sin(n);
    return 0.25 * (arc.x + arc.y);
}

// -------------------------------------------------------------------------------------
// GTAO - evaluates the horizon of one pixel by marching `numDirections` slices in
// screen space and integrating the highest occluded angle on both sides.
// -------------------------------------------------------------------------------------
half4 GTAO(float2 uv, int numDirections, int numSteps, out half outLinearDepth)
{
    half rawDepth = SampleSceneDepth(uv);
    outLinearDepth = 0.0;

    // Sky / background: fully unoccluded.
    if (rawDepth <= 1e-7)
        return half4(0.0, 0.0, 0.0, 1.0);

    float3 positionVS = GTAO_GetViewPosition(uv);
    half3  normalVS   = GTAO_GetViewNormal(uv);
    half3  viewDir    = normalize(-positionVS);

    // Radius and thickness, optionally faded with distance.
    half distanceFade   = GTAO_ComputeDistanceFade(positionVS.z);
    half2 radiusThick   = lerp(half2(_GTAO_Radius, 1.0), _GTAO_FadeValues.yw, distanceFade);
    half  radius        = radiusThick.x;
    half  thickness     = radiusThick.y;

    // How far to step in screen space for this radius (clamped for grazing pixels).
    half stepRadius = max(min((radius * _GTAO_HalfProjScale) / positionVS.z, 512.0), (half)numSteps);
    stepRadius /= ((half)numSteps + 1.0);

    half noiseOffset    = GTAO_Offsets(uv);
    half noiseDirection = GTAO_Noise(uv * _GTAO_RT_TexelSize.zw);
    half initialStep    = frac(noiseOffset + _GTAO_TemporalOffset);

    half occlusion  = 0.0;
    half3 bentNormal = 0.0;

    UNITY_LOOP
    for (int i = 0; i < numDirections; i++)
    {
        half angle = (i + noiseDirection + _GTAO_TemporalDirection) * (PI / (half)numDirections);
        half3 sliceDir = half3(cos(angle), sin(angle), 0.0);
        half2 slide   = sliceDir.xy * _GTAO_RT_TexelSize.xy;

        // Horizon of the two sides of the slice: start below the surface.
        half2 horizon = -1.0;

        UNITY_LOOP
        for (int j = 0; j < numSteps; j++)
        {
            half2 uvOffset = slide * max(stepRadius * (j + initialStep), 1.0 + j);
            half3 ds = GTAO_GetViewPosition(uv + uvOffset) - positionVS;
            half3 dt = GTAO_GetViewPosition(uv - uvOffset) - positionVS;

            half2 dsdt       = half2(dot(ds, ds), dot(dt, dt));
            half2 dsdtLength = rsqrt(dsdt);
            half2 falloff    = saturate(dsdt * (2.0 / (radius * radius)));

            // Cosine of the angle between the sample and the view direction,
            // normalised by the sample distance.
            half2 h = half2(dot(ds, viewDir), dot(dt, viewDir)) * dsdtLength;

            // Keep the highest horizon; samples outside the radius are ignored
            // (falloff -> 1 keeps the previous horizon), thin occluders blend.
            horizon.xy = (h > horizon.xy)
                ? lerp(h, horizon.xy, falloff)
                : lerp(h, horizon.xy, thickness);
        }

        // Build a basis in the slice plane and project the surface normal onto it.
        half3 planeNormal    = normalize(cross(sliceDir, viewDir));
        half3 tangent        = cross(viewDir, planeNormal);
        half3 projectedNormal = normalVS - planeNormal * dot(normalVS, planeNormal);
        half  projLength      = length(projectedNormal);

        half cos_n = clamp(dot(normalize(projectedNormal), viewDir), -1.0, 1.0);
        half n     = -sign(dot(projectedNormal, tangent)) * acos(cos_n);

        // Clamp the horizon to the tangent plane (Jimenez 2016 eq. 7).
        horizon = acos(clamp(horizon, -1.0, 1.0));
        horizon.x = n + max(-horizon.x - n, -HALF_PI);
        horizon.y = n + min(horizon.y - n,  HALF_PI);

        // Bent normal: the average unoccluded direction of this slice.
        half bentAngle = (horizon.x + horizon.y) * 0.5;
        bentNormal += viewDir * cos(bentAngle) - tangent * sin(bentAngle);

        occlusion += projLength * GTAO_IntegrateArc_CosWeight(horizon, n);
    }

    bentNormal = normalize(normalize(bentNormal) - viewDir * 0.5);
    occlusion  = saturate(pow(occlusion / (half)numDirections, _GTAO_Power));
    outLinearDepth = positionVS.z;

    return half4(bentNormal, occlusion);
}

// -------------------------------------------------------------------------------------
// Spatial filter - separable cross bilateral blur.
// Depth aware so that AO does not bleed across silhouettes / depth discontinuities.
// -------------------------------------------------------------------------------------
void GTAO_FetchAoAndDepth(float2 uv, out half ao, out half depth)
{
    half2 aod = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rg;
    ao = aod.r;
    depth = aod.g;
}

half GTAO_CrossBilateralWeight(half r, half d, half d0)
{
    const half blurSigma   = (half)GTAO_KERNEL_RADIUS * 0.5;
    const half blurFalloff = 1.0 / (2.0 * blurSigma * blurSigma);

    half dz = (d0 - d) * _ProjectionParams.z * _GTAO_Sharpness;
    return exp2(-r * r * blurFalloff - dz * dz);
}

void GTAO_AccumulateSample(half2 aod, half r, half d0, inout half totalAO, inout half totalWeight)
{
    half w = GTAO_CrossBilateralWeight(r, aod.y, d0);
    totalWeight += w;
    totalAO     += w * aod.x;
}

void GTAO_AccumulateRadius(float2 uv0, float2 deltaUV, half d0, inout half totalAO, inout half totalWeight)
{
    UNITY_UNROLL
    for (half r = 1.0; r <= GTAO_KERNEL_RADIUS / 2; r += 1.0)
    {
        half ao, depth;
        GTAO_FetchAoAndDepth(uv0 + r * deltaUV, ao, depth);
        GTAO_AccumulateSample(half2(ao, depth), r, d0, totalAO, totalWeight);
    }

    UNITY_UNROLL
    for (half r = 2.0; r <= GTAO_KERNEL_RADIUS; r += 2.0)
    {
        half ao, depth;
        GTAO_FetchAoAndDepth(uv0 + (r + 0.5) * deltaUV, ao, depth);
        GTAO_AccumulateSample(half2(ao, depth), r, d0, totalAO, totalWeight);
    }
}

half2 GTAO_BilateralBlur(float2 uv0, float2 deltaUV)
{
    half totalAO, depth;
    GTAO_FetchAoAndDepth(uv0, totalAO, depth);
    half totalWeight = 1.0;

    GTAO_AccumulateRadius(uv0, -deltaUV, depth, totalAO, totalWeight);
    GTAO_AccumulateRadius(uv0,  deltaUV, depth, totalAO, totalWeight);

    return half2(totalAO / totalWeight, depth);
}

// -------------------------------------------------------------------------------------
// Temporal filter helpers
// -------------------------------------------------------------------------------------
half4 GTAO_SampleHistory(float2 uv)
{
    return SAMPLE_TEXTURE2D_X(_GTAO_HistoryTexture, sampler_LinearClamp, uv);
}

// 3x3 mean + standard deviation of the current frame -> the AABB the history is
// clamped to.  Also returns a HDR weighted average used as the current colour.
void GTAO_ResolveAABB(float2 uv, half aabbScale, out half4 minColor, out half4 maxColor, out half4 filteredColor)
{
    half2 texel = _GTAO_RT_TexelSize.xy;

    half4 c[9];
    c[0] = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + texel * half2(-1, -1));
    c[1] = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + texel * half2( 0, -1));
    c[2] = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + texel * half2( 1, -1));
    c[3] = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + texel * half2(-1,  0));
    c[4] = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
    c[5] = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + texel * half2( 1,  0));
    c[6] = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + texel * half2(-1,  1));
    c[7] = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + texel * half2( 0,  1));
    c[8] = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + texel * half2( 1,  1));

    // Variance clipping (mean +/- k * stddev) is stable against outliers.
    half4 m1 = c[0] + c[1] + c[2] + c[3] + c[4] + c[5] + c[6] + c[7] + c[8];
    half4 m2 = c[0] * c[0] + c[1] * c[1] + c[2] * c[2] + c[3] * c[3] + c[4] * c[4]
             + c[5] * c[5] + c[6] * c[6] + c[7] * c[7] + c[8] * c[8];

    half4 mean    = m1 / 9.0;
    half4 stddev  = sqrt(max(m2 / 9.0 - mean * mean, 0.0));
    minColor = mean - aabbScale * stddev;
    maxColor = mean + aabbScale * stddev;

    // Light HDR weighted average so the current frame is not just the centre pixel.
    filteredColor = c[4];
    half totalWeight = 1.0 / (dot(c[4].rgb, half3(1, 2, 1)) + 4.0);
    half4 accum = c[4] * totalWeight;
    [unroll]
    for (int i = 0; i < 9; i++)
    {
        if (i == 4) continue;
        half w = 1.0 / (dot(c[i].rgb, half3(1, 2, 1)) + 4.0);
        accum += c[i] * w;
        totalWeight += w;
    }
    filteredColor = accum / totalWeight;
    minColor = min(minColor, filteredColor);
    maxColor = max(maxColor, filteredColor);
}

// -------------------------------------------------------------------------------------
// Multi bounce approximation (Jimenez 2016 eq. 11), needs the deferred albedo.
// -------------------------------------------------------------------------------------
half3 GTAO_MultiBounce(half ao, half3 albedo)
{
    half3 a = 2.0 * albedo - 0.33;
    half3 b = -4.8 * albedo + 0.64;
    half3 c = 2.75 * albedo + 0.69;
    return max(ao, ((ao * a + b) * ao + c) * ao);
}

// Reflection occlusion (Jimenez 2016).  Kept for completeness / learning; the
// composite pass does not require it because URP does not expose the deferred
// reflection buffer the same way Built-in did.
half GTAO_ReflectionOcclusion(half3 bentNormal, half3 reflectionVector, half roughness, half occlusionStrength)
{
    half arc0 = max(roughness, 0.1) * PI;
    half bentLength = length(bentNormal);
    half arc1 = bentLength * PI * occlusionStrength;

    half angleBetween = acos(dot(bentNormal, reflectionVector) / max(bentLength, 0.001));
    half angleDifference = abs(arc0 - arc1);
    half intersection = smoothstep(0.0, 1.0, 1.0 - saturate((angleBetween - angleDifference) / (arc0 + arc1 - angleDifference)));
    return lerp(0.0, intersection, saturate((arc1 - 0.1) / 0.2));
}

#endif // GTAO_COMMON_INCLUDED
