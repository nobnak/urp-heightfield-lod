#ifndef HEIGHTFIELD_LIT_SHADOW_CASTER_PASS_INCLUDED
#define HEIGHTFIELD_LIT_SHADOW_CASTER_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "HeightFieldLitCommon.hlsl"

float3 _LightDirection;
float3 _LightPosition;

struct SCAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SCVaryings
{
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

float4 GetHeightFieldShadowPositionHClip(SCAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    HFAttributes hf;
    hf.positionOS = input.positionOS;
    hf.normalOS = input.normalOS;
    hf.uv = input.uv;
    UNITY_TRANSFER_INSTANCE_ID(input, hf);
    HFVaryings hfOut = HFVert(hf);

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - hfOut.positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    float3 normalWS = normalize(hfOut.normalWS);
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(hfOut.positionWS, normalWS, lightDirectionWS));
    return ApplyShadowClamping(positionCS);
}

SCVaryings ShadowPassVertex(SCAttributes input)
{
    SCVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    output.positionCS = GetHeightFieldShadowPositionHClip(input);
    return output;
}

half4 ShadowPassFragment(SCVaryings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);
    return 0;
}

#endif
