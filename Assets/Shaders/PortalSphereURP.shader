Shader "DoubleSS/Portal Sphere URP"
{
    Properties
    {
        [Header(Color)]
        _CoreColor ("Core Color", Color) = (0.08,0.05,0.12,0.9)
        _AccentColor ("Accent Color", Color) = (0.19,0.07,0.24,1)
        _EdgeColor ("Edge Color", Color) = (0.01,0.01,0.015,1)
        _Opacity ("Opacity", Range(0, 1)) = 0.95

        [Header(Deformation)]
        _DeformAmount ("Deform Amount", Range(0, 0.35)) = 0.13
        _DeformFrequency ("Deform Frequency", Range(0.5, 8)) = 3.2
        _DeformSpeed ("Deform Speed", Range(0, 8)) = 2.3
        _PulseSpeed ("Pulse Speed", Range(0, 8)) = 1.8
        _PulseAmount ("Pulse Amount", Range(0, 0.4)) = 0.12

        [Header(Portal Surface)]
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.6
        _SwirlScale ("Swirl Scale", Range(0.5, 12)) = 4.2
        _SwirlSpeed ("Swirl Speed", Range(0, 8)) = 1.6

        [Header(Dark Aura)]
        _AuraStrength ("Dark Aura Strength", Range(0, 1)) = 0.85
        _AuraFalloff ("Dark Aura Falloff", Range(0.6, 6)) = 2.4

        [HideInInspector] _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+20"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _CoreColor;
        float4 _AccentColor;
        float4 _EdgeColor;
        float _Opacity;

        float _DeformAmount;
        float _DeformFrequency;
        float _DeformSpeed;
        float _PulseSpeed;
        float _PulseAmount;

        float _RimPower;
        float _SwirlScale;
        float _SwirlSpeed;

        float _AuraStrength;
        float _AuraFalloff;
        float _Seed;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            half fogFactor : TEXCOORD2;
        };

        float ComputeDisplacement(float3 positionOS, float time)
        {
            float3 p = positionOS * _DeformFrequency + (_Seed + 1.0) * float3(1.73, 2.41, 0.97);
            float waveA = sin(dot(p, float3(1.6, 2.2, 1.1)) + time);
            float waveB = sin(dot(p, float3(2.7, -1.3, 2.4)) - time * 1.37);
            float waveC = sin((p.x + p.y + p.z) * 1.1 + time * 0.71);

            float pulse = 1.0 + sin(time * _PulseSpeed + _Seed * 2.17) * _PulseAmount;
            float wave = waveA * 0.5 + waveB * 0.3 + waveC * 0.2;

            return wave * _DeformAmount * pulse;
        }

        Varyings PortalVert(Attributes input)
        {
            Varyings output;

            float time = _Time.y * _DeformSpeed;
            float3 normalOS = normalize(input.normalOS);
            float displacement = ComputeDisplacement(input.positionOS.xyz, time);

            float3 deformedPositionOS = input.positionOS.xyz + normalOS * displacement;
            VertexPositionInputs positionInputs = GetVertexPositionInputs(deformedPositionOS);
            VertexNormalInputs normalInputs = GetVertexNormalInputs(normalOS);

            output.positionCS = positionInputs.positionCS;
            output.positionWS = positionInputs.positionWS;
            output.normalWS = normalize(normalInputs.normalWS);
            output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);

            return output;
        }
        ENDHLSL

        Pass
        {
            Name "PortalBody"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PortalVert
            #pragma fragment PortalBodyFrag
            #pragma multi_compile_fog

            half4 PortalBodyFrag(Varyings input) : SV_Target
            {
                float time = _Time.y;
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);

                half fresnel = pow(saturate(1.0h - dot(normalWS, viewDirWS)), _RimPower);

                float swirlPhaseA = dot(input.positionWS, float3(0.84, 1.37, -1.11)) * _SwirlScale + time * _SwirlSpeed + _Seed * 6.1;
                float swirlPhaseB = dot(input.positionWS, float3(-1.21, 0.76, 1.43)) * (_SwirlScale * 0.83) - time * (_SwirlSpeed * 1.19) + _Seed * 3.7;

                half swirlA = 0.5h + 0.5h * sin(swirlPhaseA);
                half swirlB = 0.5h + 0.5h * sin(swirlPhaseB);
                half swirl = saturate(swirlA * 0.6h + swirlB * 0.4h);

                half3 baseColor = lerp(_CoreColor.rgb, _AccentColor.rgb, swirl);
                baseColor = lerp(baseColor, _EdgeColor.rgb, saturate(fresnel * 0.92h));

                half alpha = saturate(_Opacity * (0.32h + fresnel * 0.68h));
                baseColor = MixFog(baseColor, input.fogFactor);

                return half4(saturate(baseColor), alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "PortalDarkAura"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend DstColor Zero
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PortalVert
            #pragma fragment PortalAuraFrag

            half4 PortalAuraFrag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);

                half fresnel = saturate(1.0h - dot(normalWS, viewDirWS));
                half rim = pow(fresnel, _AuraFalloff);
                half pulse = 0.9h + 0.1h * sin(_Time.y * (_PulseSpeed * 1.27h) + _Seed * 2.3h);
                half aura = saturate(rim * _AuraStrength * pulse);

                half darken = saturate(1.0h - aura);
                return half4(darken, darken, darken, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
