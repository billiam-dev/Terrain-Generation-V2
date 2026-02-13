using Unity.Mathematics;

namespace LevelGeneration.Terrain
{
    // Noise Shader Library for Unity - https://github.com/keijiro/NoiseShader
    //
    // Original work (webgl-noise) Copyright (C) 2011 Ashima Arts.
    // Translation and modification was made by Keijiro Takahashi.
    //
    // This shader is based on the webgl-noise GLSL shader. For further details
    // of the original shader, please see the following description from the
    // original source code.
    //

    //
    // Description : Array and textureless GLSL 2D/3D/4D simplex
    //               noise functions.
    //      Author : Ian McEwan, Ashima Arts.
    //  Maintainer : ijm
    //     Lastmod : 20110822 (ijm)
    //     License : Copyright (C) 2011 Ashima Arts. All rights reserved.
    //               Distributed under the MIT License. See LICENSE file.
    //               https://github.com/ashima/webgl-noise
    //

    public static class SimplexNoise
    {
        const float OneOverSix = 0.16666666666666666666666666666667f;
        const float OneOverThree = 0.33333333333333333333333333333333f;

        static float3 Mod289(float3 x)
        {
            return x - math.floor(x / 289.0f) * 289.0f;
        }

        static float4 Mod289(float4 x)
        {
            return x - math.floor(x / 289.0f) * 289.0f;
        }

        static float4 Permute(float4 x)
        {
            return Mod289((x * 34.0f + 1.0f) * x);
        }

        static float4 TaylorInvSqrt(float4 r)
        {
            return 1.79284291400159f - r * 0.85373472095314f;
        }

        public static float Sample(float3 v)
        {
            float2 C = new(OneOverSix, OneOverThree);

            // First corner
            float3 i = math.floor(v + math.dot(v, C.yyy));
            float3 x0 = v - i + math.dot(i, C.xxx);

            // Other corners
            float3 g = math.step(x0.yzx, x0.xyz);
            float3 l = 1.0f - g;
            float3 i1 = math.min(g.xyz, l.zxy);
            float3 i2 = math.max(g.xyz, l.zxy);

            // x1 = x0 - i1  + 1.0 * C.xxx;
            // x2 = x0 - i2  + 2.0 * C.xxx;
            // x3 = x0 - 1.0 + 3.0 * C.xxx;
            float3 x1 = x0 - i1 + C.xxx;
            float3 x2 = x0 - i2 + C.yyy;
            float3 x3 = x0 - 0.5f;

            // Permutations
            i = Mod289(i); // Avoid truncation effects in permutation
            float4 p =
              Permute(Permute(Permute(i.z + new float4(0.0f, i1.z, i2.z, 1.0f))
                                    + i.y + new float4(0.0f, i1.y, i2.y, 1.0f))
                                    + i.x + new float4(0.0f, i1.x, i2.x, 1.0f));

            // Gradients: 7x7 points over a square, mapped onto an octahedron.
            // The ring size 17*17 = 289 is close to a multiple of 49 (49*6 = 294)
            float4 j = p - 49.0f * math.floor(p / 49.0f); // mod(p,7*7)

            float4 x_ = math.floor(j / 7.0f);
            float4 y_ = math.floor(j - 7.0f * x_); // mod(j,N)

            float4 x = (x_ * 2.0f + 0.5f) / 7.0f - 1.0f;
            float4 y = (y_ * 2.0f + 0.5f) / 7.0f - 1.0f;

            float4 h = 1.0f - math.abs(x) - math.abs(y);

            float4 b0 = new(x.xy, y.xy);
            float4 b1 = new(x.zw, y.zw);

            float4 s0 = math.floor(b0) * 2.0f + 1.0f;
            float4 s1 = math.floor(b1) * 2.0f + 1.0f;
            float4 sh = -math.step(h, 0.0f);

            float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
            float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;

            float3 g0 = new(a0.xy, h.x);
            float3 g1 = new(a0.zw, h.y);
            float3 g2 = new(a1.xy, h.z);
            float3 g3 = new(a1.zw, h.w);

            // Normalise gradients
            float4 norm = TaylorInvSqrt(new float4(math.dot(g0, g0), math.dot(g1, g1), math.dot(g2, g2), math.dot(g3, g3)));
            g0 *= norm.x;
            g1 *= norm.y;
            g2 *= norm.z;
            g3 *= norm.w;

            // Mix final noise value
            float4 m = math.max(0.6f - new float4(math.dot(x0, x0), math.dot(x1, x1), math.dot(x2, x2), math.dot(x3, x3)), 0.0f);
            m *= m;
            m *= m;

            float4 px = new(math.dot(x0, g0), math.dot(x1, g1), math.dot(x2, g2), math.dot(x3, g3));
            return 42.0f * math.dot(m, px);
        }
    }
}
