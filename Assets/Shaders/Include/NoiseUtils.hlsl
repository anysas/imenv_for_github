#ifndef NOISE_UTILS_INCLUDED
#define NOISE_UTILS_INCLUDED

// ============================================================================
// Shared noise utilities for Algorithmic Gallery shaders.
// Simplex 2D/3D, Voronoi, value noise, fBM helpers.
// ============================================================================

// --- Hash helpers -----------------------------------------------------------

float hash11(float p)
{
    p = frac(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

float hash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float hash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float3 hash33(float3 p)
{
    p = float3(
        dot(p, float3(127.1, 311.7, 74.7)),
        dot(p, float3(269.5, 183.3, 246.1)),
        dot(p, float3(113.5, 271.9, 124.6))
    );
    return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
}

// --- Simplex 2D noise -------------------------------------------------------

float3 _mod289_f3(float3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float2 _mod289_f2(float2 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float3 _permute(float3 x) { return _mod289_f3(((x * 34.0) + 1.0) * x); }

float SimplexNoise2D(float2 v)
{
    const float4 C = float4(
        0.211324865405187,   // (3.0 - sqrt(3.0)) / 6.0
        0.366025403784439,   // 0.5 * (sqrt(3.0) - 1.0)
       -0.577350269189626,   // -1.0 + 2.0 * C.x
        0.024390243902439);  // 1.0 / 41.0

    float2 i = floor(v + dot(v, C.yy));
    float2 x0 = v - i + dot(i, C.xx);

    float2 i1 = (x0.x > x0.y) ? float2(1.0, 0.0) : float2(0.0, 1.0);
    float4 x12 = x0.xyxy + C.xxzz;
    x12.xy -= i1;

    i = _mod289_f2(i);
    float3 p = _permute(_permute(i.y + float3(0.0, i1.y, 1.0)) + i.x + float3(0.0, i1.x, 1.0));

    float3 m = max(0.5 - float3(dot(x0, x0), dot(x12.xy, x12.xy), dot(x12.zw, x12.zw)), 0.0);
    m = m * m;
    m = m * m;

    float3 x = 2.0 * frac(p * C.www) - 1.0;
    float3 h = abs(x) - 0.5;
    float3 ox = floor(x + 0.5);
    float3 a0 = x - ox;

    m *= 1.79284291400159 - 0.85373472095314 * (a0 * a0 + h * h);

    float3 g;
    g.x = a0.x * x0.x + h.x * x0.y;
    g.yz = a0.yz * x12.xz + h.yz * x12.yw;

    return 130.0 * dot(m, g);
}

// --- Simplex 3D noise -------------------------------------------------------

float4 _mod289_f4(float4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float4 _permute4(float4 x) { return _mod289_f4(((x * 34.0) + 1.0) * x); }
float4 _taylorInvSqrt(float4 r) { return 1.79284291400159 - 0.85373472095314 * r; }

float SimplexNoise3D(float3 v)
{
    const float2 C = float2(1.0 / 6.0, 1.0 / 3.0);
    const float4 D = float4(0.0, 0.5, 1.0, 2.0);

    float3 i = floor(v + dot(v, C.yyy));
    float3 x0 = v - i + dot(i, C.xxx);

    float3 g = step(x0.yzx, x0.xyz);
    float3 l = 1.0 - g;
    float3 i1 = min(g.xyz, l.zxy);
    float3 i2 = max(g.xyz, l.zxy);

    float3 x1 = x0 - i1 + C.xxx;
    float3 x2 = x0 - i2 + C.yyy;
    float3 x3 = x0 - D.yyy;

    i = _mod289_f4(float4(i, 0)).xyz;
    float4 p = _permute4(_permute4(_permute4(
        i.z + float4(0.0, i1.z, i2.z, 1.0))
      + i.y + float4(0.0, i1.y, i2.y, 1.0))
      + i.x + float4(0.0, i1.x, i2.x, 1.0));

    float n_ = 0.142857142857;
    float3 ns = n_ * D.wyz - D.xzx;
    float4 j = p - 49.0 * floor(p * ns.z * ns.z);

    float4 x_ = floor(j * ns.z);
    float4 y_ = floor(j - 7.0 * x_);

    float4 x2_ = x_ * ns.x + ns.yyyy;
    float4 y2_ = y_ * ns.x + ns.yyyy;
    float4 h = 1.0 - abs(x2_) - abs(y2_);

    float4 b0 = float4(x2_.xy, y2_.xy);
    float4 b1 = float4(x2_.zw, y2_.zw);

    float4 s0 = floor(b0) * 2.0 + 1.0;
    float4 s1 = floor(b1) * 2.0 + 1.0;
    float4 sh = -step(h, 0.0);

    float4 a0__ = b0.xzyw + s0.xzyw * sh.xxyy;
    float4 a1__ = b1.xzyw + s1.xzyw * sh.zzww;

    float3 p0 = float3(a0__.xy, h.x);
    float3 p1 = float3(a0__.zw, h.y);
    float3 p2 = float3(a1__.xy, h.z);
    float3 p3 = float3(a1__.zw, h.w);

    float4 norm = _taylorInvSqrt(float4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3)));
    p0 *= norm.x; p1 *= norm.y; p2 *= norm.z; p3 *= norm.w;

    float4 m = max(0.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
    m = m * m;

    return 42.0 * dot(m * m, float4(dot(p0, x0), dot(p1, x1), dot(p2, x2), dot(p3, x3)));
}

// --- Voronoi / cellular noise -----------------------------------------------

float VoronoiNoise(float2 uv, float scale)
{
    float2 p = uv * scale;
    float2 i = floor(p);
    float2 f = frac(p);

    float minDist = 1.0;

    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 neighbor = float2(x, y);
            float2 cellPoint = hash22(i + neighbor);
            float2 diff = neighbor + cellPoint - f;
            float dist = length(diff);
            minDist = min(minDist, dist);
        }
    }

    return minDist;
}

// --- Value noise (smooth) ---------------------------------------------------

float ValueNoise3D(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f); // smoothstep

    float n000 = hash31(i + float3(0, 0, 0));
    float n100 = hash31(i + float3(1, 0, 0));
    float n010 = hash31(i + float3(0, 1, 0));
    float n110 = hash31(i + float3(1, 1, 0));
    float n001 = hash31(i + float3(0, 0, 1));
    float n101 = hash31(i + float3(1, 0, 1));
    float n011 = hash31(i + float3(0, 1, 1));
    float n111 = hash31(i + float3(1, 1, 1));

    float nx00 = lerp(n000, n100, f.x);
    float nx10 = lerp(n010, n110, f.x);
    float nx01 = lerp(n001, n101, f.x);
    float nx11 = lerp(n011, n111, f.x);

    float nxy0 = lerp(nx00, nx10, f.y);
    float nxy1 = lerp(nx01, nx11, f.y);

    return lerp(nxy0, nxy1, f.z);
}

// --- Fractal Brownian Motion ------------------------------------------------

float FBM_Simplex3D(float3 p, int octaves, float lacunarity, float gain)
{
    float value = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;

    for (int i = 0; i < octaves; i++)
    {
        value += amplitude * SimplexNoise3D(p * frequency);
        frequency *= lacunarity;
        amplitude *= gain;
    }

    return value;
}

float FBM_Simplex2D(float2 p, int octaves, float lacunarity, float gain)
{
    float value = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;

    for (int i = 0; i < octaves; i++)
    {
        value += amplitude * SimplexNoise2D(p * frequency);
        frequency *= lacunarity;
        amplitude *= gain;
    }

    return value;
}

// --- Dissolve helper --------------------------------------------------------

// Returns 0 when fully dissolved, 1 when fully visible.
// edgeWidth controls the glow band width along the dissolve boundary.
float DissolveClip(float noiseValue, float dissolveAmount, float edgeWidth)
{
    float threshold = dissolveAmount;
    float alpha = step(threshold, noiseValue);
    return alpha;
}

// Returns a 0-1 value representing how close this pixel is to the dissolve edge.
// Useful for edge glow coloring.
float DissolveEdge(float noiseValue, float dissolveAmount, float edgeWidth)
{
    float threshold = dissolveAmount;
    float dist = noiseValue - threshold;
    return 1.0 - saturate(dist / max(0.001, edgeWidth));
}

#endif // NOISE_UTILS_INCLUDED
