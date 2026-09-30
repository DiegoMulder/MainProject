#ifndef SURVIVALFP_PSX_LIT_WRAPPERS_INCLUDED
#define SURVIVALFP_PSX_LIT_WRAPPERS_INCLUDED
// Thin wrappers around URP's own Lit passes. Included by the generated PSX Lit shader right after
// each pass's URP include, selected by the PSX_*_PASS define that pass sets.

#if defined(PSX_FORWARD_PASS)
Varyings PsxLitPassVertex(Attributes input)
{
    Varyings output = LitPassVertex(input);
    output.positionCS = PsxSnap(output.positionCS);
    return output;
}

void PsxLitPassFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    // Reconstruct the pixel's world position from its depth, so grime needs no extra interpolator.
    float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
    g_PsxPositionWS = ComputeWorldSpacePosition(screenUV, input.positionCS.z, UNITY_MATRIX_I_VP);
    g_PsxNormalWS = normalize(input.normalWS);
    g_PsxHasPosition = true;
    LitPassFragment(input, outColor
#ifdef _WRITE_RENDERING_LAYERS
        , outRenderingLayers
#endif
    );
}
#endif

#if defined(PSX_DEPTH_ONLY_PASS)
Varyings PsxDepthOnlyVertex(Attributes input)
{
    Varyings output = DepthOnlyVertex(input);
    output.positionCS = PsxSnap(output.positionCS);
    return output;
}
#endif

#if defined(PSX_DEPTH_NORMALS_PASS)
Varyings PsxDepthNormalsVertex(Attributes input)
{
    Varyings output = DepthNormalsVertex(input);
    output.positionCS = PsxSnap(output.positionCS);
    return output;
}
#endif

#endif
