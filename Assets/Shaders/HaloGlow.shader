Shader "AlgorithmicGallery/HaloGlow"
{
    Properties
    {
        _Color ("Halo Color", Color) = (0.3, 0.7, 1, 0.8)
        _EmissionIntensity ("Emission Intensity (HDR)", Range(0, 10)) = 2.5
        _EdgeSoftness ("Edge Softness", Range(0.01, 0.5)) = 0.15
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+10"
        }

        Pass
        {
            Name "HaloGlow"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One // Additive blending — contributes to bloom
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _EmissionIntensity;
                half _EdgeSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR; // LineRenderer passes vertex color
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 vertexColor : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.vertexColor = input.color;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // UV.y goes 0 → 1 across the line width.
                // Create soft edges so the glow fades smoothly.
                float edgeDist = abs(input.uv.y - 0.5) * 2.0; // 0 at center, 1 at edge
                float softEdge = 1.0 - saturate((edgeDist - (1.0 - _EdgeSoftness)) / max(0.001, _EdgeSoftness));
                softEdge = smoothstep(0.0, 1.0, softEdge);

                // Color = vertex color (from LineRenderer) * material color * HDR intensity
                half3 haloColor = input.vertexColor.rgb * _Color.rgb * _EmissionIntensity;
                half alpha = input.vertexColor.a * _Color.a * softEdge;

                // Additive output — multiply color by alpha for soft falloff
                half3 finalColor = haloColor * alpha;

                return half4(finalColor, 0); // Alpha doesn't matter for additive
            }
            ENDHLSL
        }
    }

    FallBack Off
}
