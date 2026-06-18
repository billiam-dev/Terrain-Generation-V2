Shader "TransvoxelTerrainDebug"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _SlopePower("Slope Power", Range(1, 100)) = 1.0
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
            Name "Forward Pass"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS  : POSITION;
                float4 normalOS    : NORMAL;
                float4 sPositionOS : TEXCOORD0;   // Padded position to make room for transition cells.
                uint edgeMask      : TEXCOORD1;   // Vertex edge mask, use in combination with neighbor LOD data to select secondaty positions.
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                uint edgeMask      : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseMap_ST;

            half4 _BaseColor;

            half _SlopePower;

            uint _PackedNeighborLOD;

            half4 _ClipmapDebugColor;
            half4 _TransitionDebugColor;

            Varyings vert(Attributes IN)
            {
                // Select secondary positions if the vertex edge mask is included in the LOD data.
                if ((IN.edgeMask & _PackedNeighborLOD) == IN.edgeMask)
                    IN.positionOS = IN.sPositionOS;

                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = mul(unity_ObjectToWorld, IN.positionOS).xyz;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS.xyz);
                OUT.edgeMask = IN.edgeMask;
                return OUT;
            }

            //
            // DEBUG OVERLAYS
            //

            half4 PackedNeighborData()
            {
                half4 col = 0;

                if ((_PackedNeighborLOD & 1) == 1)
                    col = half4(1.0f, 0.0f, 0.0f, 1.0f);
                else if ((_PackedNeighborLOD & 2) == 2)
                    col = half4(0.8f, 0.2f, 0.0f, 1.0f);
                else if ((_PackedNeighborLOD & 4) == 4)
                    col = half4(0.0f, 1.0f, 0.0f, 1.0f);
                else if ((_PackedNeighborLOD & 8) == 8)
                    col = half4(0.0f, 0.8f, 0.2f, 1.0f);
                else if ((_PackedNeighborLOD & 16) == 16)
                    col = half4(0.0f, 0.0f, 1.0f, 1.0f);
                else if ((_PackedNeighborLOD & 32) == 32)
                    col = half4(0.2f, 0.0f, 0.8f, 1.0f);

                return saturate(col + 0.02f);
            }

            half4 EdgeMask(uint edgeMask)
            {
                half4 col = 0;

                if ((edgeMask & 1) == 1)
                    col += half4(1.0f, 0.0f, 0.0f, 1.0f);
                if ((edgeMask & 2) == 2)
                    col += half4(0.8f, 0.2f, 0.0f, 1.0f);
                if ((edgeMask & 4) == 4)
                    col += half4(0.0f, 1.0f, 0.0f, 1.0f);
                if ((edgeMask & 8) == 8)
                    col += half4(0.0f, 0.8f, 0.2f, 1.0f);
                if ((edgeMask & 16) == 16)
                    col += half4(0.0f, 0.0f, 1.0f, 1.0f);
                if ((edgeMask & 32) == 32)
                    col += half4(0.2f, 0.0f, 0.8f, 1.0f);

                return saturate(col + 0.02f);
            }

            //
            // END DEBUG
            //

            float4 ComputeTriplanarTexture(TEXTURE2D() tex, SAMPLER() s, float4 st, float3 positionWS, float3 normalWS)
            {
                float2 uv0, uv1, uv2;
                float4 a1, a2, a3;

                float3 triplanarW = ComputeTriplanarWeights(normalize(normalWS));
                GetTriplanarCoordinate(positionWS, uv0, uv1, uv2);
                
                a1 = SAMPLE_TEXTURE2D(tex, s, uv0 * st.xy + st.zw) * triplanarW.y;
                a2 = SAMPLE_TEXTURE2D(tex, s, uv1 * st.xy + st.zw) * triplanarW.z;
                a3 = SAMPLE_TEXTURE2D(tex, s, uv2 * st.xy + st.zw) * triplanarW.x;

                return a1 + a2 + a3;
            }

            half GetSlopeMask(float3 normalWS)
            {
                half slope = saturate(dot(normalWS, half3(0, 1, 0)));
                slope = 1 - pow(slope, _SlopePower);

                return slope;
            }

            half3 GetLightColor(float3 normalWS)
            {
                Light mainLight = GetMainLight();

                // Simple main light intensity with dot product.
                half3 lightColor = mainLight.color * dot(mainLight.direction, normalWS);

                // Apply environment lighting.
                lightColor += _GlossyEnvironmentColor.rgb;

                return lightColor;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                //return PackedNeighborData();
                //return EdgeMask(IN.edgeMask);
                //return half4(IN.normalWS, 1.0h);

                // Compute albedo color.
                half4 color = ComputeTriplanarTexture(_BaseMap, sampler_BaseMap, _BaseMap_ST, IN.positionWS, IN.normalWS) * _BaseColor;

                // Apply tint based on slope.
                color *= 1 - GetSlopeMask(IN.normalWS);

                // Apply lighting.
                color.rgb *= GetLightColor(IN.normalWS);

                // Apply debug overlays.
                color *= _ClipmapDebugColor + 0.5h;
                color *= _TransitionDebugColor + 1.0h;

                return color;
            }

            ENDHLSL
        }
    }
}
