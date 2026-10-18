Shader "Antarium/ColonySprite"
{
    // Pixel sprites (ants, queen, eggs, items). Each vertex carries the world point where the sprite samples
    // the grid light map, so a whole sprite is lit uniformly, like the terrain cell it stands in.
    Properties
    {
        [NoScaleOffset] _MainTex("Atlas", 2D) = "white" {}
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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _AntSpriteMinLight; // x = minimum perceptual brightness

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float2 lightPos : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float2 lightPos : TEXCOORD1;
                half2 lightingUV : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.uv = input.uv;
                o.color = input.color;
                o.lightPos = input.lightPos;
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                clip(c.a - 0.5);

                SurfaceData2D surface;
                InitializeSurfaceData(half3(1, 1, 1), 1, half4(1, 1, 1, 1), surface);
                InputData2D inputData;
                InitializeInputData(input.uv, input.lightingUV, inputData);
                half3 light2D = CombinedShapeLightShared(surface, inputData).rgb;

                // Sprites never drop below a minimum brightness so the colony stays watchable in dark tunnels.
                float3 light = max(light2D + QuantizedGridLight(input.lightPos), Perceptual(_AntSpriteMinLight.x));
                return half4(c.rgb * light, 1);
            }
            ENDHLSL
        }
    }
}
