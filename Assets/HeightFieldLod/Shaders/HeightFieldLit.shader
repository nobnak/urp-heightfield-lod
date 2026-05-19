Shader "HeightFieldLod/HeightFieldLit"
{
    Properties
    {
        _HeightTex ("Height", 2D) = "black" {}
        _BaseColor ("Base Color", Color) = (0.35, 0.55, 0.4, 1)
        _Specular ("Specular", Range(0, 1)) = 0.15
        _Gloss ("Gloss", Range(1, 128)) = 32
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:SetupProcedural

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_HeightTex);
            SAMPLER(sampler_HeightTex);

            float4 _BaseColor;
            float _Specular;
            float _Gloss;

            StructuredBuffer<float4> _ChunkInstances;
            float4 _WorldScaleCenter;
            float4 _UvScaleOffset;

            void SetupProcedural()
            {
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                uint i = unity_InstanceID * 2;
                _WorldScaleCenter = _ChunkInstances[i];
                _UvScaleOffset = _ChunkInstances[i + 1];
            #endif
            }

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                SetupProcedural();
            #else
                _WorldScaleCenter = float4(1, 1, 0, 0);
                _UvScaleOffset = float4(1, 1, 0, 0);
            #endif

                float2 uv = v.uv * _UvScaleOffset.xy + _UvScaleOffset.zw;
                float h = SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_HeightTex, uv, 0).r;
                float2 worldXY = (v.positionOS.xy - 0.5) * _WorldScaleCenter.xy + _WorldScaleCenter.zw;
                float3 positionWS = float3(worldXY.x, worldXY.y, v.positionOS.z - h);

                float2 duv = _UvScaleOffset.xy / 32.0;
                float hL = SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_HeightTex, uv - float2(duv.x, 0), 0).r;
                float hR = SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_HeightTex, uv + float2(duv.x, 0), 0).r;
                float hD = SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_HeightTex, uv - float2(0, duv.y), 0).r;
                float hU = SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_HeightTex, uv + float2(0, duv.y), 0).r;
                float3 normalWS = normalize(float3(hL - hR, hD - hU, -1.0));

                o.positionWS = positionWS;
                o.normalWS = normalWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Light mainLight = GetMainLight();
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 l = mainLight.direction;
                float ndl = saturate(dot(n, l));
                float3 diff = _BaseColor.rgb * mainLight.color * ndl;
                float3 halfDir = normalize(l + viewDir);
                float spec = pow(saturate(dot(n, halfDir)), _Gloss) * _Specular;
                float3 col = diff + spec * mainLight.color;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
