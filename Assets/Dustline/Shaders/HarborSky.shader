Shader "Dustline/Harbor Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.25, 0.50, 0.75, 1)
        _Horizon ("Horizon", Color) = (0.63, 0.73, 0.80, 1)
        _SunTint ("Sun tint", Color) = (1, 0.90, 0.76, 1)
        _SunVector ("Sun direction", Vector) = (0.4, 0.5, 0.6, 1)
        _CloudMotion ("Cloud drift", Vector) = (0, 0, 0, 0)
        _CloudQuality ("Cloud quality", Float) = 1
        [NoScaleOffset] _CloudNoise ("Weather noise", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CloudNoise);
            SAMPLER(sampler_CloudNoise);
            CBUFFER_START(UnityPerMaterial)
                float4 _Zenith, _Horizon, _SunTint, _SunVector, _CloudMotion;
                float _CloudQuality;
            CBUFFER_END

            struct Input { float4 positionOS : POSITION; };
            struct Output { float4 positionCS : SV_POSITION; float3 ray : TEXCOORD0; };

            Output Vert(Input v)
            {
                Output output;
                output.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                output.ray = v.positionOS.xyz;
                return output;
            }

            half4 Frag(Output input) : SV_Target
            {
                float3 ray = normalize(input.ray);
                float3 sunDir = normalize(_SunVector.xyz);
                float up = saturate(ray.y);
                float facing = saturate(dot(ray, sunDir));
                float sunset = _CloudMotion.z;
                half3 color = lerp(_Horizon.rgb, _Zenith.rgb, pow(up, 0.56));
                color *= lerp(0.68, 1.0, smoothstep(-0.28, 0.04, ray.y));
                color += _SunTint.rgb * pow(facing, 11.0) * lerp(0.14, 0.22, sunset) * smoothstep(-0.17, 0.12, ray.y);
                float disc = smoothstep(0.99972, 0.99991, facing);
                color += _SunTint.rgb * _SunVector.w * (0.93 * disc + 0.22 * pow(facing, 120.0));

                float cloudFade = smoothstep(0.075, 0.25, ray.y);
                if (cloudFade <= 0.0) return half4(color, 1.0);
                const float floorY = 290.0, depth = 112.0, frequency = 0.00048;
                float invUp = rcp(max(0.07, ray.y));
                float3 basePoint = _WorldSpaceCameraPos + ray * max(0.0, (floorY - _WorldSpaceCameraPos.y) * invUp);
                float2 march = ray.xz * (depth * invUp * frequency);
                float2 weatherUv = basePoint.xz * frequency - _CloudMotion.xy;
                float phase = _CloudMotion.w * 4.4;
                float threshold = 0.58 + 0.032 * sin(phase + weatherUv.y * 6.283);
                half3 cloudLight = lerp(half3(0.85, 0.90, 0.93), _SunTint.rgb * 0.87, sunset * 0.80);
                half3 cloudShade = lerp(half3(0.49, 0.56, 0.65), half3(0.36, 0.32, 0.42), sunset);
                if (_CloudQuality < 0.5)
                {
                    float2 low = SAMPLE_TEXTURE2D(_CloudNoise, sampler_CloudNoise, weatherUv + march * 0.45).rg;
                    float cover = smoothstep(threshold - 0.021, threshold + 0.16, low.x + (low.y - 0.5) * 0.10);
                    half3 paint = lerp(cloudShade, cloudLight, smoothstep(0.57, 0.83, low.x));
                    return half4(lerp(color, paint, cover * cloudFade * 0.87), 1.0);
                }

                int steps = _CloudQuality < 1.5 ? 7 : 11;
                float stride = rcp((float)steps);
                float transmittance = 1.0;
                half3 integrated = 0.0;
                float texLod = clamp((1.0 - ray.y) * 1.6, 0.0, 1.8);
                [loop] for (int i = 0; i < 11; i++)
                {
                    if (i >= steps || transmittance < 0.04) break;
                    float h = (i + 0.5) * stride;
                    float2 uv = weatherUv + march * h + float2(0.036, -0.016) * h;
                    float2 weather = SAMPLE_TEXTURE2D_LOD(_CloudNoise, sampler_CloudNoise, uv, texLod).rg;
                    float heightMask = smoothstep(0.0, 0.24, h) * (1.0 - smoothstep(0.63, 1.0, h));
                    float density = saturate((weather.x + 0.16 * (weather.y - 0.5) - threshold - 0.07 * abs(2.0 * h - 1.0)) * 5.2) * heightMask;
                    float alpha = density * min(3.3, 1.6 * invUp) * stride;
                    half3 light = lerp(cloudShade, cloudLight, saturate(h * 0.84 + 0.45 * pow(facing, 5.0) + 0.12 * weather.y));
                    integrated += transmittance * alpha * light;
                    transmittance *= 1.0 - alpha;
                }
                return half4(lerp(color, color * transmittance + integrated, cloudFade), 1.0);
            }
            ENDHLSL
        }
    }
}
