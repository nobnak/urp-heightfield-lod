#ifndef HEIGHTFIELD_LIT_FORWARD_PASS_INCLUDED
#define HEIGHTFIELD_LIT_FORWARD_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"
#include "HeightFieldLitCommon.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

float4 _BaseMap_ST;
half4 _BaseColor;
half _Metallic;
half _Smoothness;

// DrawMeshInstancedIndirect does not receive per-renderer unity_LightData.z (see URP RealtimeLights.hlsl).
Light GetHeightFieldMainLight(InputData inputData, half4 shadowMask, AmbientOcclusionFactor aoFactor)
{
    Light light = GetMainLight(inputData, shadowMask, aoFactor);
    if (light.distanceAttenuation < 0.5h)
        light.distanceAttenuation = 1.0h;
    return light;
}

half4 HeightFieldUniversalFragmentPBR(InputData inputData, SurfaceData surfaceData)
{
#if defined(_SPECULARHIGHLIGHTS_OFF)
    bool specularHighlightsOff = true;
#else
    bool specularHighlightsOff = false;
#endif
    BRDFData brdfData;
    InitializeBRDFData(surfaceData, brdfData);

    BRDFData brdfDataClearCoat = CreateClearCoatBRDFData(surfaceData, brdfData);
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
    Light mainLight = GetHeightFieldMainLight(inputData, shadowMask, aoFactor);

    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

    LightingData lightingData = CreateLightingData(inputData, surfaceData);
    lightingData.giColor = GlobalIllumination(brdfData, brdfDataClearCoat, surfaceData.clearCoatMask,
        inputData.bakedGI, aoFactor.indirectAmbientOcclusion, inputData.positionWS,
        inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);

    lightingData.mainLightColor = LightingPhysicallyBased(brdfData, brdfDataClearCoat,
        mainLight, inputData.normalWS, inputData.viewDirectionWS,
        surfaceData.clearCoatMask, specularHighlightsOff);

#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
        lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, brdfDataClearCoat, light,
            inputData.normalWS, inputData.viewDirectionWS,
            surfaceData.clearCoatMask, specularHighlightsOff);
    LIGHT_LOOP_END
#endif

    return CalculateFinalColor(lightingData, surfaceData.alpha);
}

struct HFForwardVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float2 baseUv : TEXCOORD1;
    float2 heightUv : TEXCOORD2;
    half fogFactor : TEXCOORD3;
    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 4);
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

void InitializeHeightFieldInputData(HFForwardVaryings input, half3 normalWS, out InputData inputData)
{
    inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.positionCS = input.positionCS;
    inputData.normalWS = NormalizeNormalPerPixel(normalWS);
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif

#if defined(_FOG_FRAGMENT)
    inputData.fogCoord = ComputeFogFactor(input.positionCS.z);
#else
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
#endif

    inputData.vertexLighting = half3(0, 0, 0);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.bakedGI = SAMPLE_GI(0, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = half4(1, 1, 1, 1);
}

void InitializeHeightFieldSurfaceData(float2 baseUv, out SurfaceData surfaceData)
{
    surfaceData = (SurfaceData)0;
    surfaceData.albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, baseUv).rgb * _BaseColor.rgb;
    surfaceData.alpha = 1;
    surfaceData.metallic = _Metallic;
    surfaceData.specular = half3(0.0h, 0.0h, 0.0h);
    surfaceData.smoothness = _Smoothness;
    surfaceData.normalTS = half3(0, 0, 1);
    surfaceData.occlusion = 1;
    surfaceData.emission = half3(0, 0, 0);
    surfaceData.clearCoatMask = 0;
    surfaceData.clearCoatSmoothness = 1;
}

HFForwardVaryings HeightFieldLitVert(HFAttributes v)
{
    HFForwardVaryings output = (HFForwardVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, output);

    HFVaryings hf = HFVert(v);
    output.positionCS = hf.positionCS;
    output.positionWS = hf.positionWS;
    output.baseUv = TRANSFORM_TEX(float2(hf.positionWS.x, hf.positionWS.y), _BaseMap);
    output.heightUv = hf.heightUv;

#if defined(_FOG_FRAGMENT)
    half fogFactor = 0;
#else
    half fogFactor = ComputeFogFactor(hf.positionCS.z);
#endif
    output.fogFactor = fogFactor;

    half3 normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(SampleHeightFieldNormalOS(hf.heightUv)));
    half4 probeOcclusion = 0;
    OUTPUT_SH4(hf.positionWS, normalWS, GetWorldSpaceNormalizeViewDir(hf.positionWS), output.vertexSH, probeOcclusion);
    return output;
}

half4 HeightFieldLitFrag(HFForwardVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);

    half3 normalWS = TransformObjectToWorldNormal(SampleHeightFieldNormalOS(input.heightUv));

    SurfaceData surfaceData;
    InitializeHeightFieldSurfaceData(input.baseUv, surfaceData);

    InputData inputData;
    InitializeHeightFieldInputData(input, normalWS, inputData);

    half4 color = HeightFieldUniversalFragmentPBR(inputData, surfaceData);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    return half4(color.rgb, 1);
}

#endif
