Shader "HeightFieldLod/HeightFieldLit"
{
    Properties
    {
        _HeightTex ("Height", 2D) = "black" {}
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (0.35, 0.55, 0.4, 1)
        _SpecColor ("Specular", Color) = (0.15, 0.15, 0.15, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.4
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
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF

            #define _SPECULAR_SETUP 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"
            #include "HeightFieldLitCommon.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _SpecColor;
            half _Smoothness;

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

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.baseUv).rgb * _BaseColor.rgb;
                surfaceData.alpha = 1;
                surfaceData.metallic = 0;
                surfaceData.specular = _SpecColor.rgb;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.occlusion = 1;
                surfaceData.emission = 0;

                BRDFData brdfData;
                InitializeBRDFData(surfaceData, brdfData);

                float3 normalWS = NormalizeNormalPerPixel(SampleHeightFieldNormalWS(i.heightUv));
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light mainLight = GetHeightFieldMainLight(i.positionWS);

            #ifdef _SPECULARHIGHLIGHTS_OFF
                bool specularHighlightsOff = true;
            #else
                bool specularHighlightsOff = false;
            #endif
                half3 color = LightingPhysicallyBased(brdfData, mainLight, normalWS, viewDirWS, specularHighlightsOff);
                return half4(color, 1);
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
    }
    FallBack Off
}
