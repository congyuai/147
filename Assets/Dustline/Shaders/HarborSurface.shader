Shader "Dustline/Harbor Surface"
{
    Properties
    {
        [MainTexture] _BaseMap ("Macro color and surface class", 2D) = "white" {}
        _Smoothness ("Base smoothness", Range(0, 1)) = 0.1
        [NoScaleOffset] _AsphaltAlbedo ("Asphalt albedo", 2D) = "white" {}
        [NoScaleOffset] [Normal] _AsphaltNormal ("Asphalt normal", 2D) = "bump" {}
        [NoScaleOffset] _ConcreteAlbedo ("Concrete albedo", 2D) = "white" {}
        [NoScaleOffset] [Normal] _ConcreteNormal ("Concrete normal", 2D) = "bump" {}
        [NoScaleOffset] _SteelAlbedo ("Steel albedo", 2D) = "white" {}
        [NoScaleOffset] [Normal] _SteelNormal ("Steel normal", 2D) = "bump" {}
        [NoScaleOffset] _AggregateAlbedo ("Aggregate albedo", 2D) = "white" {}
        [NoScaleOffset] [Normal] _AggregateNormal ("Aggregate normal", 2D) = "bump" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_TexelSize;
            float _Smoothness;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Harbor Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_AsphaltAlbedo); SAMPLER(sampler_AsphaltAlbedo);
            TEXTURE2D(_AsphaltNormal); SAMPLER(sampler_AsphaltNormal);
            TEXTURE2D(_ConcreteAlbedo); SAMPLER(sampler_ConcreteAlbedo);
            TEXTURE2D(_ConcreteNormal); SAMPLER(sampler_ConcreteNormal);
            TEXTURE2D(_SteelAlbedo); SAMPLER(sampler_SteelAlbedo);
            TEXTURE2D(_SteelNormal); SAMPLER(sampler_SteelNormal);
            TEXTURE2D(_AggregateAlbedo); SAMPLER(sampler_AggregateAlbedo);
            TEXTURE2D(_AggregateNormal); SAMPLER(sampler_AggregateNormal);

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 surface : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float4 surface : TEXCOORD4;
                half4 fogAndLights : TEXCOORD5;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
                output.uv = input.uv;
                output.surface = input.surface;
                output.fogAndLights = half4(ComputeFogFactor(output.positionCS.z), VertexLighting(p.positionWS, output.normalWS));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 macroColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                float2 meters = input.surface.xy;
                float2 gx = ddx(meters), gy = ddy(meters);
                half strength = input.surface.w * (1.0 - smoothstep(72.0, 146.0, distance(_WorldSpaceCameraPos, input.positionWS)));
                half3 normalTS = half3(0.0, 0.0, 1.0);
                half metallic = 0.0, smoothness = _Smoothness;
                if (strength > 0.002)
                {
                    half material = input.surface.z;
                    if (material < -0.5)
                    {
                        // Keep categorical classes out of the interpolated macro color mip chain.
                        float2 center = (floor(saturate(input.uv) * _BaseMap_TexelSize.zw) + 0.5) * _BaseMap_TexelSize.xy;
                        material = floor(SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, center, 0.0).a * 4.0);
                    }
                    half3 scan;
                    half4 packedNormal;
                    half tintRatio, normalStrength;
                    if (material < 0.5)
                    {
                        float repeat = 0.26;
                        scan = SAMPLE_TEXTURE2D_GRAD(_AsphaltAlbedo, sampler_AsphaltAlbedo, meters * repeat, gx * repeat, gy * repeat).rgb;
                        packedNormal = SAMPLE_TEXTURE2D_GRAD(_AsphaltNormal, sampler_AsphaltNormal, meters * repeat, gx * repeat, gy * repeat);
                        tintRatio = 5.8; normalStrength = 0.77;
                    }
                    else if (material < 1.5)
                    {
                        float repeat = 0.31;
                        scan = SAMPLE_TEXTURE2D_GRAD(_ConcreteAlbedo, sampler_ConcreteAlbedo, meters * repeat, gx * repeat, gy * repeat).rgb;
                        packedNormal = SAMPLE_TEXTURE2D_GRAD(_ConcreteNormal, sampler_ConcreteNormal, meters * repeat, gx * repeat, gy * repeat);
                        tintRatio = 2.05; normalStrength = 0.67;
                    }
                    else if (material < 2.5)
                    {
                        float repeat = 0.38;
                        scan = SAMPLE_TEXTURE2D_GRAD(_SteelAlbedo, sampler_SteelAlbedo, meters * repeat, gx * repeat, gy * repeat).rgb;
                        packedNormal = SAMPLE_TEXTURE2D_GRAD(_SteelNormal, sampler_SteelNormal, meters * repeat, gx * repeat, gy * repeat);
                        tintRatio = 3.7; normalStrength = 0.60;
                        metallic = 0.29 * strength; smoothness += 0.21 * strength;
                    }
                    else
                    {
                        float repeat = 0.43;
                        scan = SAMPLE_TEXTURE2D_GRAD(_AggregateAlbedo, sampler_AggregateAlbedo, meters * repeat, gx * repeat, gy * repeat).rgb;
                        packedNormal = SAMPLE_TEXTURE2D_GRAD(_AggregateNormal, sampler_AggregateNormal, meters * repeat, gx * repeat, gy * repeat);
                        tintRatio = 3.2; normalStrength = 0.78;
                    }
                    half brightness = dot(scan, half3(0.2126, 0.7152, 0.0722));
                    half3 grit = clamp(lerp(brightness.xxx, scan, 0.38) * tintRatio, 0.42, 1.9);
                    macroColor = saturate(macroColor * lerp(1.0, grit, strength * 0.78));
                    normalTS = UnpackNormalScale(packedNormal, normalStrength * strength);
                }

                half3 n = normalize(input.normalWS);
                half3 t = normalize(input.tangentWS.xyz);
                half3 b = cross(n, t) * input.tangentWS.w;
                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, half3x3(t, b, n)));
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                lighting.fogCoord = input.fogAndLights.x;
                lighting.vertexLighting = input.fogAndLights.yzw;
                lighting.bakedGI = SampleSH(lighting.normalWS);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1.0, 1.0, 1.0, 1.0);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = macroColor;
                surface.normalTS = normalTS;
                surface.metallic = metallic;
                surface.smoothness = smoothness;
                surface.occlusion = 1.0;
                surface.alpha = 1.0;
                half4 color = UniversalFragmentPBR(lighting, surface);
                return half4(MixFog(color.rgb, lighting.fogCoord), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Harbor ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Harbor DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }
}
