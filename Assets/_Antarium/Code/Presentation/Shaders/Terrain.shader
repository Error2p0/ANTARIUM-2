Shader "Antarium/Terrain"
{
    Properties
    {
        [HideInInspector] _MainTex("Unused", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"
            #include "TerrainCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 positionWS : TEXCOORD0;
                half2 lightingUV : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.positionWS = TransformObjectToWorld(input.positionOS).xy;
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                TerrainSurface s = EvaluateTerrain(input.positionWS);
                if (s.hidden)
                    return s.fogOrder ? half4(_AntOverlay.rgb * 0.35, 1) : half4(0, 0, 0, 1);
                if (s.unlit)
                    return half4(s.albedo, 1);

                // Light2D contribution (global ambient + dynamic lights), evaluated for a white surface.
                SurfaceData2D surface;
                InitializeSurfaceData(half3(1, 1, 1), 1, half4(1, 1, 1, 1), surface);
                InputData2D inputData;
                InitializeInputData(input.positionWS, input.lightingUV, inputData);
                half3 light2D = CombinedShapeLightShared(surface, inputData).rgb;

                float3 light = light2D + QuantizedGridLight(input.positionWS) + s.glow;
                return half4(s.albedo * light, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "NormalsRendering" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include "TerrainCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.positionWS = TransformObjectToWorld(input.positionOS).xy;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                TerrainSurface s = EvaluateTerrain(input.positionWS);
                return half4(0.5 * (s.normalWS + 1.0), 1);
            }
            ENDHLSL
        }
    }
}
