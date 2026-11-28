Shader "Hidden/ShiftGoods/CountdownClock"
{
    Properties
    {
        _DialColor ("表盘颜色", Color) = (1, 1, 1, 1)
        _ElapsedColor ("已用时间颜色", Color) = (0.91, 0.16, 0.25, 1)
        [HideInInspector] _Elapsed ("已用时间", Range(0, 1)) = 0
        [HideInInspector] _DialCenter ("表盘中心", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DialRight ("表盘右方", Vector) = (1, 0, 0, 0)
        [HideInInspector] _DialUp ("表盘上方", Vector) = (0, 1, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "UnityCG.cginc"

            float4 _DialColor;
            float4 _ElapsedColor;
            float _Elapsed;
            float4 _DialCenter;
            float4 _DialRight;
            float4 _DialUp;
            float4x4 _ClockWorldToLocal;

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 dial : TEXCOORD0;
                float2 direction : TEXCOORD1;
            };

            Varyings Vert(appdata_base input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);

                // Project the skinned vertices into the clock root's frame.
                float3 world = mul(unity_ObjectToWorld, input.vertex).xyz;
                float3 relative = mul(_ClockWorldToLocal, float4(world, 1.0)).xyz - _DialCenter.xyz;
                output.dial = float2(dot(relative, _DialRight.xyz), dot(relative, _DialUp.xyz));

                float angle = saturate(_Elapsed) * 6.28318530718;
                float sine;
                float cosine;
                sincos(angle, sine, cosine);
                output.direction = float2(sine, cosine);
                return output;
            }

            float SoftStep(float distance)
            {
                float feather = max(0.5 * fwidth(distance), 0.0001);
                return smoothstep(-feather, feather, distance);
            }

            float ElapsedSector(float2 dial, float2 direction)
            {
                float start = SoftStep(dial.x);
                float end = SoftStep(dot(dial, float2(-direction.y, direction.x)));
                if (_Elapsed <= 0.0)
                    return 0.0;
                if (_Elapsed >= 1.0)
                    return 1.0;

                // The radial sector is an intersection up to 6 o'clock and a union afterward.
                return _Elapsed <= 0.5 ? min(start, end) : max(start, end);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float filled = ElapsedSector(input.dial, input.direction);
                return float4(lerp(_DialColor.rgb, _ElapsedColor.rgb, filled), 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
