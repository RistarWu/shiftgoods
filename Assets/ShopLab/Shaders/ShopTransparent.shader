Shader "CartoonShader/Transparent"
{
    Properties
    {
        [MainTexture] _BaseMap ("固有色贴图", 2D) = "white" {}
        [MainColor] _BaseColor ("固有色", Color) = (1, 1, 1, 1)
        _Alpha ("不透明度倍率", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("面剔除", Float) = 2
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull [_Cull]
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #pragma multi_compile_fog

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _Alpha;

            struct Varyings
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                UNITY_FOG_COORDS(3)
            };

            Varyings Vert(appdata_base input)
            {
                Varyings output;
                output.pos = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
                output.worldPos = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                UNITY_TRANSFER_FOG(output, output.pos);
                return output;
            }

            float4 Frag(Varyings input, float facing : VFACE) : SV_Target
            {
                float4 surface = tex2D(_BaseMap, input.uv) * _BaseColor;
                float3 normal = normalize(input.worldNormal) * (facing > 0.0 ? 1.0 : -1.0);
                float3 lightDirection = UnityWorldSpaceLightDir(input.worldPos);
                lightDirection *= rsqrt(max(dot(lightDirection, lightDirection), 0.000001));
                float band = smoothstep(0.35, 0.45, dot(normal, lightDirection));
                float peakColor = max(_LightColor0.r, max(_LightColor0.g, _LightColor0.b));
                float3 keyColor = lerp(1.0, saturate(_LightColor0.rgb), 0.25);
                surface.rgb *= lerp(0.75, keyColor, band * saturate(peakColor));
                surface.a = saturate(surface.a * _Alpha);
                UNITY_APPLY_FOG(input.fogCoord, surface);
                return surface;
            }
            ENDCG
        }
    }

    Fallback Off
}
