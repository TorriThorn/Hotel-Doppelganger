// Additive glow drawn on top of an object to show it can be grabbed. URP only.
// ObjectGrabber adds this as an extra material on the target's renderers while you aim at it.
Shader "Custom/GrabHighlight"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.85, 0.4, 1)
        _Fill ("Fill (whole-object tint)", Range(0, 1)) = 0.15
        _RimPower ("Rim Sharpness", Range(0.5, 8)) = 2.5
        _Pulse ("Pulse Amount", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "GrabHighlight"
            Tags { "LightMode"="SRPDefaultUnlit" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Fill;
                half _RimPower;
                half _Pulse;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(p.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                float3 v = normalize(IN.viewDirWS);

                half rim = pow(1.0h - saturate(dot(n, v)), _RimPower);
                half pulse = 1.0h - _Pulse * (0.5h + 0.5h * sin(_Time.y * 4.0));
                half amount = saturate(_Fill + rim) * pulse;

                return half4(_Color.rgb * _Color.a * amount, 0);
            }
            ENDHLSL
        }
    }
}
