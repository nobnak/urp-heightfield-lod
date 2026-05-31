Shader "HeightFieldLod/HeightFieldToon"
{
    Properties
    {
        _HeightTex ("Height", 2D) = "black" {}
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _LightColor ("Light", Color) = (0.55, 0.54, 0.35, 1)
        _ShadowColor ("Shadow", Color) = (0.15, 0.18, 0.22, 1)
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
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:SetupProcedural
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "HeightFieldLitCommon.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            float4 _BaseMap_ST;
            half4 _LightColor;
            half4 _ShadowColor;

            Light GetHeightFieldMainLight(float3 positionWS)
            {
            #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                Light light = GetMainLight(TransformWorldToShadowCoord(positionWS));
            #else
                Light light = GetMainLight();
            #endif
                if (light.distanceAttenuation < 0.5)
                    light.distanceAttenuation = 1.0;
                return light;
            }

            HFVaryings Vert(HFAttributes v)
            {
                HFVaryings o = HFVert(v);
                o.baseUv = TRANSFORM_TEX(float2(o.positionWS.x, o.positionWS.y), _BaseMap);
                return o;
            }

            half4 Frag(HFVaryings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.baseUv).rgb;
                float3 normalWS = NormalizeNormalPerPixel(TransformObjectToWorldNormal(SampleHeightFieldNormalOS(i.heightUv)));
                Light mainLight = GetHeightFieldMainLight(i.positionWS);
                half diffuse = saturate(dot(normalWS, mainLight.direction))
                    * mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                half3 tone = lerp(_ShadowColor.rgb, _LightColor.rgb, diffuse);
                return half4(albedo * tone, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:SetupProcedural

            #include "HeightFieldLitDepthNormalsPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:SetupProcedural
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "HeightFieldLitShadowCasterPass.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
