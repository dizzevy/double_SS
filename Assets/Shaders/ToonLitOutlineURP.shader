Shader "DoubleSS/Toon Lit Outline"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)

        [Header(Toon Lighting)]
        _ShadeSteps ("Shade Steps", Range(2, 8)) = 3
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.48
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1.05
        _BandPower ("Band Contrast", Range(0.6, 2.5)) = 1.35

        [Header(Color Comfort)]
        _Saturation ("Saturation", Range(0, 1)) = 0.76
        _Brightness ("Brightness", Range(0.5, 1.2)) = 0.88

        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineThickness ("Outline Width (world)", Range(0, 0.1)) = 0
        _RimLineStart ("Rim Line Start", Range(0.2, 0.98)) = 0.58
        _RimLineStrength ("Rim Line Strength", Range(0, 1.2)) = 1
        _RimNormalBoost ("Rim Edge Boost", Range(0, 16)) = 7
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        float4 _BaseColor;
        float _ShadeSteps;
        float _ShadowStrength;
        float _AmbientStrength;
        float _BandPower;
        float _Saturation;
        float _Brightness;
        float4 _OutlineColor;
        float _OutlineThickness;
        float _RimLineStart;
        float _RimLineStrength;
        float _RimNormalBoost;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            float3 normalWS : TEXCOORD2;
            half fogFactor : TEXCOORD3;
        };

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        ENDHLSL

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZTest LEqual
            ZWrite Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment

            struct OutlineVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            OutlineVaryings OutlineVertex(Attributes input)
            {
                OutlineVaryings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                positionWS += normalWS * _OutlineThickness;

                float4 positionCS = TransformWorldToHClip(positionWS);

                output.positionCS = positionCS;
                return output;
            }

            half4 OutlineFragment(OutlineVaryings input) : SV_Target
            {
                if (_OutlineThickness <= 1e-5)
                {
                    discard;
                }

                return _OutlineColor;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            Varyings Vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalize(normalInputs.normalWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                half3 normalWS = normalize(input.normalWS);

                Light mainLight = GetMainLight();
                half NdotL = saturate(dot(normalWS, mainLight.direction));

                half steps = max(_ShadeSteps, 2.0);
                half toonNdotL = floor(NdotL * steps) / (steps - 1.0);
                toonNdotL = saturate(toonNdotL);
                toonNdotL = saturate(pow(toonNdotL, _BandPower));

                half litFactor = lerp(1.0 - _ShadowStrength, 1.0, toonNdotL);

                half3 direct = albedo * mainLight.color * litFactor;
                half3 ambient = albedo * _AmbientStrength * 0.35;

                half3 color = direct + ambient;
                half luminance = dot(color, half3(0.299h, 0.587h, 0.114h));
                color = lerp(luminance.xxx, color, _Saturation);
                color *= _Brightness;

                half3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                half rim = 1.0h - saturate(dot(normalWS, viewDirWS));
                half rimAA = max(fwidth(rim), 1e-4h) * 1.3h;
                half rimMask = smoothstep(_RimLineStart - rimAA, _RimLineStart + rimAA, rim);

                half3 normalDx = ddx(normalWS);
                half3 normalDy = ddy(normalWS);
                half normalEdge = length(normalDx) + length(normalDy);
                normalEdge = saturate((normalEdge - 0.01h) * _RimNormalBoost);

                half rimLine = saturate(rimMask * normalEdge * _RimLineStrength);
                color = lerp(color, _OutlineColor.rgb, rimLine);

                color = MixFog(color, input.fogFactor);

                return half4(saturate(color), _BaseColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
