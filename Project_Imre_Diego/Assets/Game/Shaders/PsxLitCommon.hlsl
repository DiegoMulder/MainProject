#ifndef SURVIVALFP_PSX_LIT_COMMON_INCLUDED
#define SURVIVALFP_PSX_LIT_COMMON_INCLUDED

// Globals set by PsxCameraPresentation / MansionAtmosphere. Outside UnityPerMaterial on purpose:
// they are frame- or map-wide, so they never break SRP Batcher compatibility.
float4 _PsxSnapParams;   // xy: snap grid in pixels, z: 1 = snapping on, w: share of the snap allowed along world up
float4 _PsxGrimeTint;    // rgb: colour grime multiplies towards
float4 _PsxGrimeParams;  // x: storey spacing (m), y: skirting grime height (m), z: global grime scale

// Classic PSX vertex precision: clip-space positions snap to a coarse screen grid, so geometry
// shimmers very slightly as the camera moves. Every pass that writes depth uses the same function,
// so depth prepass, normals (SSAO) and colour stay in exact agreement.
//
// Grounded snapping: the screen snap moves a vertex sideways on screen but keeps its depth, so a surface
// no longer passes through the points it was modelled through. A room floor is only a couple of huge
// triangles; the snap error of corners many metres away, interpolated across it, lifted the floor up to
// ~2 cm above its true height right under a small item (which is not snapped, or snapped by its own nearby
// corners), and the floor then drew over the item's base. The snap's movement is therefore turned into a
// world-space displacement and its vertical part dropped (scaled by _PsxSnapParams.w): floors, table tops
// and shelves stay exactly level, so nothing resting on them can sink in, while the horizontal shimmer that
// gives the PSX look remains. The displacement depends on the position alone, so coincident vertices still
// move together and meshes stay watertight.
float4 PsxSnap(float4 positionCS)
{
    if (_PsxSnapParams.z < 0.5 || positionCS.w <= 0.05) return positionCS;
    float2 grid = max(_PsxSnapParams.xy * 0.5, 1.0);
    float4 snapped = positionCS;
    snapped.xy = round(positionCS.xy / positionCS.w * grid) / grid * positionCS.w;
    float4 original = mul(UNITY_MATRIX_I_VP, positionCS);
    float4 moved = mul(UNITY_MATRIX_I_VP, snapped);
    float3 positionWS = original.xyz / original.w;
    float3 displacement = moved.xyz / moved.w - positionWS;
    displacement.y *= saturate(_PsxSnapParams.w);
    return mul(UNITY_MATRIX_VP, float4(positionWS + displacement, 1.0));
}

float PsxHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float PsxNoise(float3 x)
{
    float3 i = floor(x), f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(PsxHash(i), PsxHash(i + float3(1, 0, 0)), f.x),
                     lerp(PsxHash(i + float3(0, 1, 0)), PsxHash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(PsxHash(i + float3(0, 0, 1)), PsxHash(i + float3(1, 0, 1)), f.x),
                     lerp(PsxHash(i + float3(0, 1, 1)), PsxHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

// World position and normal of the pixel being shaded, set by the forward fragment wrapper.
// Other passes leave g_PsxHasPosition false and get no grime.
static float3 g_PsxPositionWS;
static float3 g_PsxNormalWS;
static bool g_PsxHasPosition = false;

// Age and neglect without extra textures: dirt collecting along the bottom of walls, faint damp
// stains and a few streaks running down. World-space, so every room and every wall differs.
void PsxApplyGrime(inout SurfaceData surface, half amount)
{
    float scale = amount * _PsxGrimeParams.z;
    if (!g_PsxHasPosition || scale <= 0.001) return;
    float3 p = g_PsxPositionWS;
    float wall = 1.0 - saturate(abs(g_PsxNormalWS.y) * 2.0);
    float spacing = max(_PsxGrimeParams.x, 1.0);
    float height = p.y - floor((p.y + 0.05) / spacing) * spacing;
    float skirting = (1.0 - smoothstep(0.0, max(_PsxGrimeParams.y, 0.05), height)) * wall;
    float n = PsxNoise(p * 0.45) * 0.65 + PsxNoise(p * 1.9) * 0.35;
    float stain = smoothstep(0.5, 0.82, n);
    float streak = wall * smoothstep(0.62, 0.95, PsxNoise(float3((p.x + p.z) * 2.3, p.y * 0.22, (p.x - p.z) * 0.4)));
    float grime = saturate(skirting * 0.85 + stain * 0.6 + streak * 0.35) * scale;
    // Broad, gentle value drift so large surfaces never read as one flat, freshly painted tone.
    float age = lerp(0.84, 1.04, PsxNoise(p * 0.12 + 17.0));
    surface.albedo *= lerp(half3(1, 1, 1), (half3)_PsxGrimeTint.rgb, grime) * lerp(1.0, age, saturate(scale));
    surface.smoothness *= 1.0 - grime * 0.7;
}

#endif
