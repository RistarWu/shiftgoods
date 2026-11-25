Shader "Shiftgoods/Windblown Asphalt"
{
    Properties
    {
        _MainTex ("Asphalt albedo", 2D) = "white" {}
        [Normal] _BumpMap ("Asphalt normal", 2D) = "bump" {}
        _AsphaltTint ("Asphalt tint", Color) = (1, 0.94, 0.84, 1)
        _SandColor ("Local sand", Color) = (0.776, 0.576, 0.394, 1)
        _DustAmount ("Windblown dust", Range(0, 1)) = 0.29
        _CenterDustScale ("Dust at road center", Range(0, 1)) = 0.74
        _NormalStrength ("Asphalt relief", Range(0, 1)) = 0.42
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 250

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.0
        #include "UnityStandardUtils.cginc"

        sampler2D _MainTex, _BumpMap;
        fixed4 _AsphaltTint, _SandColor;
        half _DustAmount, _CenterDustScale, _NormalStrength;

        struct Input
        {
            float2 uv_MainTex;
            float2 roadUV;
            float2 shoulder;
        };

        void vert(inout appdata_full vertex, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input, data);
            // The mesh UV runs across the road in U and follows the centerline in V.
            data.roadUV = vertex.texcoord.xy;
            data.shoulder = vertex.color.rg;
        }

        float hash(float2 p)
        {
            return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
        }

        float noise(float2 p)
        {
            float2 cell = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float bottom = lerp(hash(cell), hash(cell + float2(1, 0)), f.x);
            float top = lerp(hash(cell + float2(0, 1)), hash(cell + 1.0), f.x);
            return lerp(bottom, top, f.y);
        }

        void surf(Input IN, inout SurfaceOutputStandard output)
        {
            float2 road = IN.roadUV;
            float drift = (noise(road * float2(2.2, 0.72) + 9.8) - 0.5) * 0.11;
            float streaks = noise(float2((road.x + drift) * 5.3, road.y * 1.05));
            float smudges = noise(road * 2.25 + 7.4);
            float grit = noise(road * 70.0);
            float windCover = _DustAmount * lerp(0.55, 1.65, smoothstep(0.22, 0.79, streaks * 0.77 + smudges * 0.23));
            float awayFromCenter = smoothstep(0.10, 0.84, abs(road.x));
            windCover *= lerp(_CenterDustScale, 1.0, awayFromCenter);
            float ripple = (streaks - 0.50) * 0.23 * (1.0 - abs(2.0 * IN.shoulder.x - 1.0));
            float buried = saturate(IN.shoulder.x + ripple);
            float dust = saturate(buried + windCover * (1.0 - buried));
            float3 asphalt = tex2D(_MainTex, IN.uv_MainTex).rgb * _AsphaltTint.rgb;
            float3 sand = _SandColor.rgb * (1.0 + (1.0 - IN.shoulder.y) * ((grit - 0.5) * 0.10 + (smudges - 0.5) * 0.05));
            output.Albedo = lerp(asphalt, sand, dust);
            output.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _NormalStrength * (1.0 - dust));
            output.Metallic = 0;
            output.Smoothness = lerp(0.18, 0.199, dust);
            output.Occlusion = 1;
            output.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
