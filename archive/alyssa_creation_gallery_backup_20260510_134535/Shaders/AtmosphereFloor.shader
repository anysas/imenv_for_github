Shader "AlgorithmicGallery/AtmosphereFloor"
{
    Properties
    {
        [Header(Grid)]
        _GridColor ("Grid Color", Color) = (0.15, 0.6, 0.9, 1)
        _GridSpacing ("Grid Spacing", Float) = 2.0
        _GridLineWidth ("Grid Line Width", Range(0.001, 0.08)) = 0.015
        _GridEmission ("Grid Emission Intensity", Range(0, 5)) = 1.5
        _GridFadeDistance ("Grid Fade Distance", Float) = 40.0

        [Header(Proximity)]
        _PlayerWorldPos ("Player World Position", Vector) = (0, 0, 0, 0)
        _ProximityRadius ("Proximity Glow Radius", Float) = 8.0
        _ProximityFalloff ("Proximity Falloff", Range(0.5, 4)) = 2.0
        _ProximityBoost ("Proximity Brightness Boost", Range(0, 3)) = 1.5

        [Header(Phase)]
        _PhaseColor1 ("Phase 1 Color (Fascination)", Color) = (0.2, 0.7, 1, 1)
        _PhaseColor2 ("Phase 2 Color (Recognition)", Color) = (1, 0.7, 0.3, 1)
        _PhaseColor3 ("Phase 3 Color (Unease)", Color) = (0.9, 0.15, 0.1, 1)
        _PhaseBlend ("Phase Blend (0=P1, 0.5=P2, 1=P3)", Range(0, 1)) = 0

        [Header(Base Surface)]
        _BaseColor ("Base Floor Color", Color) = (0.02, 0.02, 0.025, 1)
        _NoiseScale ("Noise Modulation Scale", Float) = 6.0
        _NoiseIntensity ("Noise Modulation Intensity", Range(0, 0.5)) = 0.12

        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.7
        _Metallic ("Metallic", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Include/NoiseUtils.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GridColor;
                float _GridSpacing;
                half _GridLineWidth;
                half _GridEmission;
                float _GridFadeDistance;
                float4 _PlayerWorldPos;
                float _ProximityRadius;
                half _ProximityFalloff;
                half _ProximityBoost;
                half4 _PhaseColor1;
                half4 _PhaseColor2;
                half4 _PhaseColor3;
                half _PhaseBlend;
                half4 _BaseColor;
                float _NoiseScale;
                half _NoiseIntensity;
                half _Smoothness;
                half _Metallic;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 viewDirWS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.uv = input.uv;
                output.normalWS = normalInput.normalWS;
                output.positionWS = vertexInput.positionWS;
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float2 worldXZ = input.positionWS.xz;

                // --- Grid lines ---
                float2 gridUV = worldXZ / max(0.01, _GridSpacing);
                float2 gridFrac = abs(frac(gridUV - 0.5) - 0.5);
                float2 gridAA = fwidth(gridUV);
                float gridX = 1.0 - saturate((gridFrac.x - _GridLineWidth) / max(0.0001, gridAA.x));
                float gridY = 1.0 - saturate((gridFrac.y - _GridLineWidth) / max(0.0001, gridAA.y));
                float gridMask = max(gridX, gridY);

                // Distance fade to camera
                float distToCamera = length(input.positionWS - _WorldSpaceCameraPos);
                float distanceFade = 1.0 - saturate(distToCamera / max(1.0, _GridFadeDistance));
                gridMask *= distanceFade;

                // Noise modulation to break up grid regularity
                float noiseMod = VoronoiNoise(worldXZ * 0.1, _NoiseScale) * _NoiseIntensity;
                gridMask *= (1.0 - noiseMod);

                // --- Phase color ---
                half3 phaseColor;
                if (_PhaseBlend < 0.5)
                {
                    phaseColor = lerp(_PhaseColor1.rgb, _PhaseColor2.rgb, _PhaseBlend * 2.0);
                }
                else
                {
                    phaseColor = lerp(_PhaseColor2.rgb, _PhaseColor3.rgb, (_PhaseBlend - 0.5) * 2.0);
                }

                // --- Proximity glow ---
                float distToPlayer = length(worldXZ - _PlayerWorldPos.xz);
                float proximity = 1.0 - saturate(pow(distToPlayer / max(0.01, _ProximityRadius), _ProximityFalloff));
                float proximityBrightness = proximity * _ProximityBoost;

                // --- Compose ---
                half3 albedo = _BaseColor.rgb;
                half3 gridColorFinal = phaseColor * gridMask * _GridEmission * (1.0 + proximityBrightness);
                half3 proximityGlow = phaseColor * proximity * 0.15;
                half3 emission = gridColorFinal + proximityGlow;

                // --- URP Lighting ---
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = normalize(input.viewDirWS);
                inputData.fogCoord = input.fogFactor;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.emission = emission;
                surfaceData.alpha = 1.0;
                surfaceData.occlusion = 1.0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, input.fogFactor);

                return color;
            }
            ENDHLSL
        }

        // Shadow caster
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyBiasedPositionWS(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
