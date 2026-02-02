Shader "TransvoxelTerrain"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _BaseMap("Base Map", 2D) = "white"
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseMap_ST;

            half3 _BaseColor;

            int _PackedLODData;
            half3 _ClipmapDebugColor;

            Varyings vert(Attributes IN)
            {
                float4 positionOS = IN.positionOS;

                // Select secondary positions if the vertex edge mask is included in the LOD data.
                if (((IN.edgeMask & _PackedLODData) == IN.edgeMask))
                    positionOS = IN.sPositionOS;

                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(positionOS.xyz);
                OUT.positionWS = mul(unity_ObjectToWorld, positionOS).xyz;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS.xyz);
                return OUT;
            }

            half3 frag(Varyings IN) : SV_Target
            {
                // Compute albedo color.
                float2 uv0, uv1, uv2;
                half3 a1, a2, a3;

                float3 triplanarW = ComputeTriplanarWeights(IN.normalWS);
                GetTriplanarCoordinate(IN.positionWS, uv0, uv1, uv2);
                
                a1 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv0 * _BaseMap_ST.xy + _BaseMap_ST.zw).rgb * triplanarW.y;
                a2 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv1 * _BaseMap_ST.xy + _BaseMap_ST.zw).rgb * triplanarW.z;
                a3 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv2 * _BaseMap_ST.xy + _BaseMap_ST.zw).rgb * triplanarW.x;

                half3 albedoColor = (a1 + a2 + a3) * _BaseColor.rgb;

                // Compute light color.
                Light mainLight = GetMainLight();
                float lightIntensity = saturate(dot(IN.normalWS, -mainLight.direction));
                float3 lightColor = mainLight.color * lightIntensity + _GlossyEnvironmentColor.rgb;

                return albedoColor * lightColor * _ClipmapDebugColor;
            }

            ENDHLSL
        }
    }
}
