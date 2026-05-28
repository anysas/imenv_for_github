Shader "Hidden/AlgorithmicGallery/ScreenGlitch"
{
    Properties
    {
        _GlitchIntensity ("Intensity", Range(0, 1)) = 0
        _RGBSplitAmount ("RGB Split", Range(0, 0.1)) = 0.02
        _ScanLineIntensity ("Scan Lines", Range(0, 1)) = 0.3
        _NoiseIntensity ("Noise", Range(0, 1)) = 0.15
        _GlitchTime ("Time", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "ScreenGlitch"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _GlitchIntensity;
                float _RGBSplitAmount;
                float _ScanLineIntensity;
                float _NoiseIntensity;
                float _GlitchTime;
            CBUFFER_END

            float GlitchHash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float GlitchBlockNoise(float2 uv, float time)
            {
                float2 blockUV = floor(uv * float2(4.0, 24.0));
                return step(0.92 - _GlitchIntensity * 0.3, GlitchHash(blockUV + floor(time * 8.0)));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                // Block-based displacement
                float blockNoise = GlitchBlockNoise(uv, _GlitchTime);
                float displacement = blockNoise * _RGBSplitAmount * (GlitchHash(float2(_GlitchTime, uv.y * 40.0)) * 2.0 - 1.0);

                // RGB channel split
                float2 uvR = uv + float2(displacement, 0);
                float2 uvG = uv;
                float2 uvB = uv - float2(displacement, 0);

                half r = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uvR).r;
                half g = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uvG).g;
                half b = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uvB).b;
                half4 color = half4(r, g, b, 1.0);

                // Scan lines
                float scanLine = sin(uv.y * 400.0 + _GlitchTime * 15.0) * 0.5 + 0.5;
                scanLine = pow(scanLine, 2.0);
                color.rgb -= scanLine * _ScanLineIntensity * _GlitchIntensity * 0.15;

                // Random noise
                float noise = GlitchHash(uv * 500.0 + _GlitchTime * 100.0);
                color.rgb = lerp(color.rgb, half3(noise, noise, noise), _NoiseIntensity * _GlitchIntensity * blockNoise);

                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
