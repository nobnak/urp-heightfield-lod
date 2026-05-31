#ifndef HEIGHTFIELD_LIT_DEPTH_NORMALS_PASS_INCLUDED
#define HEIGHTFIELD_LIT_DEPTH_NORMALS_PASS_INCLUDED

#include "HeightFieldLitCommon.hlsl"

struct DNAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct DNVaryings
{
    float4 positionCS : SV_POSITION;
    float2 heightUv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

DNVaryings DepthNormalsVertex(DNAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    HFAttributes hf;
    hf.positionOS = input.positionOS;
    hf.normalOS = input.normalOS;
    hf.uv = input.uv;
    UNITY_TRANSFER_INSTANCE_ID(input, hf);
    HFVaryings o = HFVert(hf);
    DNVaryings outV;
    UNITY_TRANSFER_INSTANCE_ID(o, outV);
    outV.positionCS = o.positionCS;
    outV.heightUv = o.heightUv;
    return outV;
}

void DepthNormalsFragment(DNVaryings input, out half4 outNormalWS : SV_Target0)
{
    UNITY_SETUP_INSTANCE_ID(input);
    half3 normalOS = SampleHeightFieldNormalOS(input.heightUv);
    half3 normalWS = TransformObjectToWorldNormal(normalOS);
    outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
}

#endif
