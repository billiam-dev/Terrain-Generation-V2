// https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader
// TODO: convert to shader graph?

Shader "Terrain"
{
    Properties
    {
        _SurfaceMetallicAlbedo("Surface Base Map", 2D) = "white" {}
        _SurfaceNormal("Surface Normal Map", 2D) = "white" {}
        _SurfaceAmbientOcclusion("Surface Ambient Occlusion", 2D) = "white" {}
        _SurfaceColor("Surface Color", Color) = (1, 1, 1, 1)

        _SideMetallicAlbedo("Side Base Map", 2D) = "white" {}
        _SideNormal("Side Normal Map", 2D) = "white" {}
        _SideAmbientOcclusion("Side Ambient Occlusion", 2D) = "white" {}
        _SideColor("Sides Color", Color) = (1, 1, 1, 1)

        _SlopeFactor("Slope Factor", Range(1, 100)) = 1.0
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

            HLSLPROGRAM

            #pragma target 2.0

            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_fragment _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma shader_feature_fragment _ _ADDITIONAL_LIGHTS
            #pragma shader_feature_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma shader_feature_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma shader_feature _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS  : POSITION;
                float4 normalOS    : NORMAL;
                float4 sPositionOS : TANGENT;   // Secondary positions, padded to make room for transition cells.
                uint edgeMask      : COLOR;     // Edge mask, combied with packed LOD data to determine whether to use secondary positions.
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                uint edgeMask      : TEXCOORD2; // TEMP!!
            };

            // Surface textures
            TEXTURE2D(_SurfaceMetallicAlbedo);
            SAMPLER(sampler_SurfaceMetallicAlbedo);
            float4 _SurfaceMetallicAlbedo_ST;

            TEXTURE2D(_SurfaceNormal);
            SAMPLER(sampler_SurfaceNormal);
            float4 _SurfaceNormal_ST;

            TEXTURE2D(_SurfaceAmbientOcclusion);
            SAMPLER(sampler_SurfaceAmbientOcclusion);
            float4 _SurfaceAmbientOcclusion_ST;

            // Side textures
            TEXTURE2D(_SideMetallicAlbedo);
            SAMPLER(sampler_SideMetallicAlbedo);
            float4 _SideMetallicAlbedo_ST;

            TEXTURE2D(_SideNormal);
            SAMPLER(sampler_SideNormal);
            float4 _SideNormal_ST;

            TEXTURE2D(_SideAmbientOcclusion);
            SAMPLER(sampler_SideAmbientOcclusion);
            float4 _SideAmbientOcclusion_ST;

            half4 _SurfaceColor;
            half4 _SideColor;

            half _SlopeFactor;

            uint _PackedLODData;

            half4 _ClipmapDebugColor;
            half4 _TransitionDebugColor;

            Varyings vert(Attributes IN)
            {
                float4 positionOS = IN.positionOS;

                // Select secondary positions if the vertex edge mask is included in the LOD data.
                if ((IN.edgeMask & _PackedLODData) == IN.edgeMask)
                    positionOS = IN.sPositionOS;

                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(positionOS.xyz);
                OUT.positionWS = mul(unity_ObjectToWorld, positionOS).xyz;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS.xyz);
                OUT.edgeMask = IN.edgeMask;
                return OUT;
            }

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

            //
            // DEBUG
            //

            half4 PackedNeighborData()
            {
                half4 col = 0;

                if ((_PackedLODData & 1) == 1)
                    col = half4(1.0f, 0.0f, 0.0f, 1.0f);
                else if ((_PackedLODData & 2) == 2)
                    col = half4(0.8f, 0.2f, 0.0f, 1.0f);
                else if ((_PackedLODData & 4) == 4)
                    col = half4(0.0f, 1.0f, 0.0f, 1.0f);
                else if ((_PackedLODData & 8) == 8)
                    col = half4(0.0f, 0.8f, 0.2f, 1.0f);
                else if ((_PackedLODData & 16) == 16)
                    col = half4(0.0f, 0.0f, 1.0f, 1.0f);
                else if ((_PackedLODData & 32) == 32)
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

            half4 frag(Varyings IN) : SV_Target
            {
                //return PackedNeighborData();
                //return EdgeMask(IN.edgeMask);

                // Compute materials.
                half4 surfaceColor = ComputeTriplanarTexture(_SurfaceMetallicAlbedo, sampler_SurfaceMetallicAlbedo, _SurfaceMetallicAlbedo_ST, IN.positionWS, IN.normalWS);
                half4 surfaceNormal = ComputeTriplanarTexture(_SurfaceNormal, sampler_SurfaceNormal, _SurfaceNormal_ST, IN.positionWS, IN.normalWS);
                half surfaceAO = ComputeTriplanarTexture(_SurfaceAmbientOcclusion, sampler_SideAmbientOcclusion, _SideAmbientOcclusion_ST, IN.positionWS, IN.normalWS).a;
                
                half4 sideColor = ComputeTriplanarTexture(_SideMetallicAlbedo, sampler_SideMetallicAlbedo, _SideMetallicAlbedo_ST, IN.positionWS, IN.normalWS);
                half4 sideNormal = ComputeTriplanarTexture(_SideNormal, sampler_SideNormal, _SideNormal_ST, IN.positionWS, IN.normalWS);
                half sideAO = ComputeTriplanarTexture(_SideAmbientOcclusion, sampler_SideAmbientOcclusion, _SideAmbientOcclusion_ST, IN.positionWS, IN.normalWS).a;

                // Get slope.
                half slope = saturate(dot(IN.normalWS, half3(0, 1, 0)));
                slope = 1 - pow(slope, _SlopeFactor);

                half4 metallicAlbedo = lerp(surfaceColor * _SurfaceColor, sideColor * _SideColor, slope);
                half4 normal = lerp(surfaceNormal, sideNormal, slope);
                half ao = lerp(surfaceAO, sideAO, slope);

                // Compute light color.
                InputData inputData = (InputData) 0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = IN.normalWS;
                inputData.viewDirectionWS = GetWorldSpaceViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);

                // https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl
                SurfaceData surfaceData = (SurfaceData) 0;
                surfaceData.albedo = metallicAlbedo.rgb;
                surfaceData.metallic = metallicAlbedo.a;
                surfaceData.normalTS = UnpackNormal(normal);
                surfaceData.occlusion = ao;
                surfaceData.alpha = 1;
                surfaceData.smoothness = 0;
                surfaceData.specular = 0;

                half4 litColor = UniversalFragmentPBR(inputData, surfaceData);// + unity_AmbientSky;

                return litColor * (_ClipmapDebugColor + 0.5 / 2.0);// * (_TransitionDebugColor + 0.5 / 2.0);
            }

            ENDHLSL
        }

        Pass
        {
            Name "Shadow Caster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM

            #pragma target 2.0

            #pragma vertex ShadowPassVertexe
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS  : POSITION;
                float4 normalOS    : NORMAL;
                float4 sPositionOS : TANGENT;   // Secondary positions, padded to make room for transition cells.
                int edgeMask       : COLOR;     // Edge mask, combied with packed LOD data to determine whether to use secondary positions.
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            int _PackedLODData;

            float4 GetShadowPositionHClip(Attributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalWS = TransformObjectToWorldNormal(input.normalOS.xyz);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                return ApplyShadowClamping(positionCS);
            }

            Varyings ShadowPassVertexe(Attributes IN)
            {
                float4 positionOS = IN.positionOS;

                // Select secondary positions if the vertex edge mask is included in the LOD data.
                if (((IN.edgeMask & _PackedLODData) == IN.edgeMask))
                    positionOS = IN.sPositionOS;

                Varyings OUT;
                OUT.positionHCS = GetShadowPositionHClip(IN);
                return OUT;
            }

            half3 ShadowPassFragment(Varyings IN) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }
    }
}
