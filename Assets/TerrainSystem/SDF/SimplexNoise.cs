using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace TerrainSystem.SDF
{
    //
    // Noise Shader Library for Unity - https://github.com/keijiro/NoiseShader
    //
    // Description : Array and textureless GLSL 2D simplex noise function.
    //      Author : Ian McEwan, Ashima Arts.
    //  Maintainer : stegu
    //     Lastmod : 20110822 (ijm)
    //     License : Copyright (C) 2011 Ashima Arts. All rights reserved.
    //               Distributed under the MIT License. See LICENSE file.
    //               https://github.com/ashima/webgl-noise
    //               https://github.com/stegu/webgl-noise
    //

    //
    // This code has been modified by Keijiro Takahashi for use in Unity,
    // including a rewrite in HLSL with simplifications and optimizations.
    // Rights to the modifications are waived, and the original license
    // terms remain unchanged.
    //

    public static class SimplexNoise
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Mod(float x, float y) { return x - y * math.floor(x / y); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float2 Mod(float2 x, float2 y) { return x - y * math.floor(x / y); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float3 Mod(float3 x, float3 y) { return x - y * math.floor(x / y); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float4 Mod(float4 x, float4 y) { return x - y * math.floor(x / y); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Fade(float t) { return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float2 Fade(float2 t) { return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float3 Fade(float3 t) { return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Mod289(float x) { return x - math.floor(x / 289.0f) * 289.0f; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float2 Mod289(float2 x) { return x - math.floor(x / 289.0f) * 289.0f; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float3 Mod289(float3 x) { return x - math.floor(x / 289.0f) * 289.0f; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float4 Mod289(float4 x) { return x - math.floor(x / 289.0f) * 289.0f; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float2 Permute(float2 x) { return Mod289((x * 34.0f + 10.0f) * x); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float3 Permute(float3 x) { return Mod289((x * 34.0f + 10.0f) * x); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float4 Permute(float4 x) { return Mod289((x * 34.0f + 10.0f) * x); }

        const float C1 = 0.2113249f; // (3.0f - math.sqrt(3.0f)) / 6.0f;
        const float C2 = 0.3660254f; // (math.sqrt(3.0f) - 1.0f) / 2.0f;
        const float C3 = 257.6106f; // 41.0f * 3.14159265359f * 2.0f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float3 SimplexNoiseGrad2D(float2 v)
        {
            // First corner
            float2 i = math.floor(v + math.dot(v, C2));
            float2 x0 = v - i + math.dot(i, C1);

            // Other corners
            float2 i1 = x0.x > x0.y ? new float2(1.0f, 0.0f) : new float2(0.0f, 1.0f);
            float2 x1 = x0 + C1 - i1;
            float2 x2 = x0 + C1 * 2.0f - 1.0f;

            // Permutations
            i = Mod289(i); // Avoid truncation effects in permutation
            float3 p = Permute(i.y + new float3(0.0f, i1.y, 1.0f));
            p = Permute(p + i.x + new float3(0.0f, i1.x, 1.0f));

            // Gradients: 41 points uniformly over a unit circle.
            // The ring size 17*17 = 289 is close to a multiple of 41 (41*7 = 287)
            float3 phi = p / C3;
            float2 g0 = new(math.cos(phi.x), math.sin(phi.x));
            float2 g1 = new(math.cos(phi.y), math.sin(phi.y));
            float2 g2 = new(math.cos(phi.z), math.sin(phi.z));

            // Compute noise and gradient at P
            float3 m = new(math.dot(x0, x0), math.dot(x1, x1), math.dot(x2, x2));
            float3 px = new(math.dot(g0, x0), math.dot(g1, x1), math.dot(g2, x2));

            m = math.max(0.5f - m, 0.0f);
            float3 m3 = m * m * m;
            float3 m4 = m * m3;

            float3 temp = -8 * m3 * px;
            float2 grad = m4.x * g0 + temp.x * x0 +
                          m4.y * g1 + temp.y * x1 +
                          m4.z * g2 + temp.z * x2;

            return 99.2f * new float3(grad, math.dot(m4, px));
        }

        const float OneOverSix = 0.16666667f;
        const float OneOverThree = 0.33333333f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float4 SimplexNoiseGrad3D(float3 v)
        {
            // First corner
            float3 i = math.floor(v + math.dot(v, OneOverThree));
            float3 x0 = v - i + math.dot(i, OneOverSix);

            // Other corners
            float3 g = math.step(x0.yzx, x0.xyz);
            float3 l = 1.0f - g;
            float3 i1 = math.min(g.xyz, l.zxy);
            float3 i2 = math.max(g.xyz, l.zxy);

            float3 x1 = x0 - i1 + OneOverSix;
            float3 x2 = x0 - i2 + OneOverThree;
            float3 x3 = x0 - 0.5f;

            // Permutations
            i = Mod289(i); // Avoid truncation effects in permutation
            float4 p = Permute(i.z + new float4(0, i1.z, i2.z, 1));
            p = Permute(p + i.y + new float4(0, i1.y, i2.y, 1));
            p = Permute(p + i.x + new float4(0, i1.x, i2.x, 1));

            // Gradients: 7x7 points over a square, mapped onto an octahedron.
            // The ring size 17*17 = 289 is close to a multiple of 49 (49*6 = 294)
            float4 gx = math.lerp(-1.0f, 1.0f, math.frac(p / 7.0f));
            float4 gy = math.lerp(-1.0f, 1.0f, math.frac(math.floor(p / 7.0f) / 7.0f));
            float4 gz = 1 - math.abs(gx) - math.abs(gy);

            float4 zn = math.select(1.0f, 0.0f, gz < 0.0f);
            gx.x += zn.x * (gx.x < 0 ? 1 : -1);
            gx.y += zn.y * (gx.y < 0 ? 1 : -1);
            gx.z += zn.z * (gx.z < 0 ? 1 : -1);
            gx.w += zn.w * (gx.w < 0 ? 1 : -1);

            gy.x += zn.x * (gy.x < 0 ? 1 : -1);
            gy.y += zn.y * (gy.y < 0 ? 1 : -1);
            gy.z += zn.z * (gy.z < 0 ? 1 : -1);
            gy.w += zn.w * (gy.w < 0 ? 1 : -1);

            float3 g0 = math.normalize(new float3(gx.x, gy.x, gz.x));
            float3 g1 = math.normalize(new float3(gx.y, gy.y, gz.y));
            float3 g2 = math.normalize(new float3(gx.z, gy.z, gz.z));
            float3 g3 = math.normalize(new float3(gx.w, gy.w, gz.w));

            // Compute noise and gradient at P
            float4 m = new(math.dot(x0, x0), math.dot(x1, x1), math.dot(x2, x2), math.dot(x3, x3));
            float4 px = new(math.dot(g0, x0), math.dot(g1, x1), math.dot(g2, x2), math.dot(g3, x3));

            m = math.max(0.5f - m, 0.0f);
            float4 m3 = m * m * m;
            float4 m4 = m * m3;

            float4 temp = -8.0f * m3 * px;
            float3 grad = m4.x * g0 + temp.x * x0 +
                          m4.y * g1 + temp.y * x1 +
                          m4.z * g2 + temp.z * x2 +
                          m4.w * g3 + temp.w * x3;

            return 107.0f * new float4(grad, math.dot(m4, px));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sample2D(float2 position)
        {
            return SimplexNoiseGrad2D(position).z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sample3D(float3 position)
        {
            return SimplexNoiseGrad3D(position).w;
        }
    }
}
