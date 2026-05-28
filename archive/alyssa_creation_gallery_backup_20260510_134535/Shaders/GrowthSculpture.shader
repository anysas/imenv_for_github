Shader "AlgorithmicGallery/GrowthSculpture"
{
    Properties
    {
        [Header(Base)]
        _BaseMap ("Base Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Header(Growth Parameters)]
        _Saturation ("Saturation", Range(0, 1)) = 0
        _EmissionPower ("Emission Power", Range(0, 3)) = 0
        _DissolveAmount ("Dissolve Amount", Range(0, 1)) = 0
        _GrowthProgress ("Growth Progress", Range(0, 1)) = 0
        _PhaseHue ("Phase Hue Shift", Range(0, 1)) = 0

        [Header(Dissolve)]
        _DissolveScale ("Dissolve Noise Scale", Float) = 4.0
        _DissolveEdgeWidth ("Dissolve Edge Width", Range(0.01, 0.2)) = 0.06
        _DissolveEdgeColor ("Dissolve Edge Color", Color) = (1, 0.6, 0.2, 1)
        _DissolveEdgeEmission ("Dissolve Edge Emission", Range(0, 8)) = 3.0
        _DissolveScrollSpeed ("Dissolve Scroll Speed", Float) = 0.15

        [Header(Emission)]
        _EmissionColor ("Emission Tint", Color) = (1, 0.7, 0.4, 1)
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.0
        _FresnelIntensity ("Fresnel Intensity", Range(0, 2)) = 0.8

        [Header(Vertex Displacement)]
        _DisplacementAmount ("Displacement Amount", Range(0, 0.15)) = 0.03
        _DisplacementNoiseScale ("Displacement Noise Scale", Float) = 3.0

        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3
        _Metallic ("Metallic", Range(0, 1)) = 0.0

        [HideInInspector] _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
        }

        // ------------------------------------------------------------------
        // Main URP Lit-equivalent pass with growth effects
        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

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

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Saturation;
                half _EmissionPower;
                half _DissolveAmount;
                half _GrowthProgress;
                half _PhaseHue;
                float _DissolveScale;
                half _DissolveEdgeWidth;
                half4 _DissolveEdgeColor;
                half _DissolveEdgeEmission;
                float _DissolveScrollSpeed;
                half4 _EmissionColor;
                half _FresnelPower;
                half _FresnelIntensity;
                half _DisplacementAmount;
                float _DisplacementNoiseScale;
                half _Smoothness;
                half _Metallic;
                half _Cutoff;
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
                float3 positionOS : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Vertex displacement along normal based on growth
                float3 displaced = input.positionOS.xyz;
                if (_DisplacementAmount > 0.001 && _GrowthProgress > 0.01)
                {
                    float noise = SimplexNoise3D(input.positionOS.xyz * _DisplacementNoiseScale + _Time.y * 0.3);
                    float displace = noise * _DisplacementAmount * _GrowthProgress;
                    displaced += input.normalOS * displace;
                }

                VertexPositionInputs vertexInput = GetVertexPositionInputs(displaced);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = normalInput.normalWS;
                output.positionWS = vertexInput.positionWS;
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                output.positionOS = input.positionOS.xyz;

                return output;
            }

            // Hue rotation helper
            half3 HueShift(half3 color, half shift)
            {
                // Simple hue rotation in RGB space
                float angle = shift * 6.28318530718; // 2*PI
                float cosA = cos(angle);
                float sinA = sin(angle);

                half3x3 hueRotation = half3x3(
                    cosA + (1.0 - cosA) / 3.0,
                    (1.0 - cosA) / 3.0 - sqrt(1.0 / 3.0) * sinA,
                    (1.0 - cosA) / 3.0 + sqrt(1.0 / 3.0) * sinA,

                    (1.0 - cosA) / 3.0 + sqrt(1.0 / 3.0) * sinA,
                    cosA + (1.0 - cosA) / 3.0,
                    (1.0 - cosA) / 3.0 - sqrt(1.0 / 3.0) * sinA,

                    (1.0 - cosA) / 3.0 - sqrt(1.0 / 3.0) * sinA,
                    (1.0 - cosA) / 3.0 + sqrt(1.0 / 3.0) * sinA,
                    cosA + (1.0 - cosA) / 3.0
                );

                return mul(hueRotation, color);
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // --- Dissolve ---
                float3 dissolveCoord = input.positionOS * _DissolveScale + float3(0, _Time.y * _DissolveScrollSpeed, 0);
                float dissolveNoise = SimplexNoise3D(dissolveCoord) * 0.5 + 0.5; // remap to 0-1

                // Clip dissolved pixels
                float dissolveThreshold = _DissolveAmount;
                clip(dissolveNoise - dissolveThreshold);

                // Dissolve edge glow
                float edgeProximity = DissolveEdge(dissolveNoise, dissolveThreshold, _DissolveEdgeWidth);
                half3 edgeGlow = _DissolveEdgeColor.rgb * _DissolveEdgeEmission * edgeProximity * step(0.01, _DissolveAmount);

                // --- Base color ---
                half4 baseTexColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 albedo = baseTexColor.rgb * _BaseColor.rgb;

                // --- Saturation ---
                half luminance = dot(albedo, half3(0.2126, 0.7152, 0.0722));
                half3 greyscale = half3(luminance, luminance, luminance);
                albedo = lerp(greyscale, albedo, _Saturation);

                // --- Phase hue shift ---
                if (_PhaseHue > 0.001)
                {
                    albedo = HueShift(albedo, _PhaseHue);
                }

                // --- Fresnel rim ---
                float NdotV = saturate(dot(input.normalWS, input.viewDirWS));
                float fresnel = pow(1.0 - NdotV, _FresnelPower);
                half3 fresnelColor = _EmissionColor.rgb * fresnel * _FresnelIntensity * _EmissionPower;

                // --- Emission ---
                half3 emission = _EmissionColor.rgb * _EmissionPower * _Saturation;
                emission += fresnelColor;
                emission += edgeGlow;

                // --- URP Lighting ---
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = normalize(input.viewDirWS);
                inputData.fogCoord = input.fogFactor;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                // Shadow coords
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

        // ------------------------------------------------------------------
        // Shadow caster pass
        // ------------------------------------------------------------------
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
            #include "Include/NoiseUtils.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Saturation;
                half _EmissionPower;
                half _DissolveAmount;
                half _GrowthProgress;
                half _PhaseHue;
                float _DissolveScale;
                half _DissolveEdgeWidth;
                half4 _DissolveEdgeColor;
                half _DissolveEdgeEmission;
                float _DissolveScrollSpeed;
                half4 _EmissionColor;
                half _FresnelPower;
                half _FresnelIntensity;
                half _DisplacementAmount;
                float _DisplacementNoiseScale;
                half _Smoothness;
                half _Metallic;
                half _Cutoff;
            CBUFFER_END

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
                float3 positionOS : TEXCOORD0;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyBiasedPositionWS(positionWS, normalWS, _LightDirection));
                output.positionOS = input.positionOS.xyz;

                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                // Dissolve clip in shadow pass too
                float3 dissolveCoord = input.positionOS * _DissolveScale + float3(0, _Time.y * _DissolveScrollSpeed, 0);
                float dissolveNoise = SimplexNoise3D(dissolveCoord) * 0.5 + 0.5;
                clip(dissolveNoise - _DissolveAmount);

                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Depth-only pass for depth prepass
        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Include/NoiseUtils.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Saturation;
                half _EmissionPower;
                half _DissolveAmount;
                half _GrowthProgress;
                half _PhaseHue;
                float _DissolveScale;
                half _DissolveEdgeWidth;
                half4 _DissolveEdgeColor;
                half _DissolveEdgeEmission;
                float _DissolveScrollSpeed;
                half4 _EmissionColor;
                half _FresnelPower;
                half _FresnelIntensity;
                half _DisplacementAmount;
                float _DisplacementNoiseScale;
                half _Smoothness;
                half _Metallic;
                half _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                float3 dissolveCoord = input.positionOS * _DissolveScale + float3(0, _Time.y * _DissolveScrollSpeed, 0);
                float dissolveNoise = SimplexNoise3D(dissolveCoord) * 0.5 + 0.5;
                clip(dissolveNoise - _DissolveAmount);

                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
