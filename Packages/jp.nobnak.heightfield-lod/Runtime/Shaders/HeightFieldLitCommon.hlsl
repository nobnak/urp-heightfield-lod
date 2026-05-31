#ifndef HEIGHTFIELD_LIT_COMMON_INCLUDED
#define HEIGHTFIELD_LIT_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_HeightTex);
SAMPLER(sampler_HeightTex);
TEXTURE2D(_NormalTex);
SAMPLER(sampler_NormalTex);

StructuredBuffer<float4> _ChunkInstances;
float4 _LocalScaleCenter;
float4 _UvScaleOffset;

void SetupProcedural()
{
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
    uint i = unity_InstanceID * 2;
    _LocalScaleCenter = _ChunkInstances[i];
    _UvScaleOffset = _ChunkInstances[i + 1];
#endif
}

half3 SampleHeightFieldNormalOS(float2 heightUv)
{
    half3 n = SAMPLE_TEXTURE2D_LOD(_NormalTex, sampler_NormalTex, heightUv, 0).xyz * 2.0 - 1.0;
    return normalize(n);
}

struct HFAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct HFVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    float2 baseUv : TEXCOORD2;
    float2 heightUv : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

HFVaryings HFVert(HFAttributes v)
{
    HFVaryings o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
    SetupProcedural();
#else
    _LocalScaleCenter = float4(1, 1, 0, 0);
    _UvScaleOffset = float4(1, 1, 0, 0);
#endif

    float2 heightUv = v.uv * _UvScaleOffset.xy + _UvScaleOffset.zw;
    float h = SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_HeightTex, heightUv, 0).r;
    float2 localXY = (v.positionOS.xy - 0.5) * _LocalScaleCenter.xy + _LocalScaleCenter.zw;
    float3 positionOS = float3(localXY.x, localXY.y, v.positionOS.z - h);
    float3 positionWS = TransformObjectToWorld(positionOS);

    o.positionWS = positionWS;
    o.heightUv = heightUv;
    o.normalWS = TransformObjectToWorldNormal(SampleHeightFieldNormalOS(heightUv));
    o.baseUv = 0;
    o.positionCS = TransformWorldToHClip(positionWS);
    return o;
}

#endif
