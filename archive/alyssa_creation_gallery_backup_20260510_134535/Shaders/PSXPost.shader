Shader "Hidden/PSXPost"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "PSXPost"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _ColorBits;
            float _DitherStrength;
            float _GlitchIntensity;
            float _PixelScale;
            float _PSXTime;

            // 4x4 Bayer matrix (normalized 0-1)
            static const float kBayer4x4[16] = {
                 0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
                12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
                 3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
                15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
            };

            float BayerThreshold(float2 screenPos)
            {
                int x = (int)fmod(screenPos.x, 4.0);
                int y = (int)fmod(screenPos.y, 4.0);
                int idx = clamp(y * 4 + x, 0, 15);
                return kBayer4x4[idx];
            }

            float Quantize(float val, float bits)
            {
                float levels = pow(2.0, bits) - 1.0;
                return floor(val * levels + 0.5) / levels;
            }

            float2 GlitchUV(float2 uv, float intensity)
            {
                if (intensity <= 0.001) return uv;
                float row = floor(uv.y * 80.0);
                float noise = frac(sin(row * 127.1 + _PSXTime * 3.7) * 43758.5);
                float shift = (noise - 0.5) * intensity * 0.08;
                uv.x += step(0.85, noise) * shift;
                return uv;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                if (_PixelScale > 1.0)
                {
                    float2 texelSize = _PixelScale / _ScreenParams.xy;
                    uv = floor(uv / texelSize) * texelSize;
                }

                uv = GlitchUV(uv, _GlitchIntensity);

                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float2 screenPos = uv * _ScreenParams.xy;
                float threshold = BayerThreshold(screenPos) - 0.5;
                float bits = _ColorBits;
                float bias = _DitherStrength / pow(2.0, bits);

                col.r = Quantize(saturate(col.r + threshold * bias), bits);
                col.g = Quantize(saturate(col.g + threshold * bias), bits);
                col.b = Quantize(saturate(col.b + threshold * bias), bits);

                return col;
            }
            ENDHLSL
        }
    }
}
