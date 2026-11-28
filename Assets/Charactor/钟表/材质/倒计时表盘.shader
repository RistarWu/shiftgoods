Shader "Hidden/ShiftGoods/CountdownClock"
{
    Properties
    {
        _MainTex ("钟表", 2D) = "white" {}
        _Dial ("表盘中心与半径", Vector) = (0.5, 0.5, 0.34, 0.34)
        _DialColor ("表盘颜色", Color) = (1, 1, 1, 1)
        _ElapsedColor ("已用时间颜色", Color) = (0.91, 0.16, 0.25, 1)
        _HandColor ("指针颜色", Color) = (0.10, 0.07, 0.17, 1)
        [HideInInspector] _Elapsed ("已用时间", Float) = 0
        [HideInInspector] _HandDirection ("指针方向", Vector) = (0, 1, 0, 0)
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Dial;
            float4 _DialColor;
            float4 _ElapsedColor;
            float4 _HandColor;
            float _Elapsed;
            float4 _HandDirection;

            float SoftStep(float distance)
            {
                float edge = max(0.5 * fwidth(distance), 0.0001);
                return smoothstep(-edge, edge, distance);
            }

            float ElapsedSector(float2 position, float2 hand)
            {
                float startSide = SoftStep(position.x);
                float endSide = SoftStep(dot(position, float2(-hand.y, hand.x)));
                if (_Elapsed <= 0.0)
                    return 0.0;
                if (_Elapsed >= 1.0)
                    return 1.0;

                // Half-planes intersect before 6 o'clock and unite after it.
                return _Elapsed <= 0.5 ? min(startSide, endSide) : max(startSide, endSide);
            }

            float HandCoverage(float2 position, float2 hand)
            {
                float advance = clamp(dot(position, hand), 0.0, 0.80);
                float needle = SoftStep(0.027 - length(position - advance * hand));
                float hub = SoftStep(0.072 - length(position));
                return max(needle, hub);
            }

            float CardinalTick(float2 position)
            {
                float2 axis = abs(position);
                return SoftStep(min(0.029 - axis.x, 0.039 - abs(axis.y - 0.862)));
            }

            float4 Frag(v2f_img input) : SV_Target
            {
                float4 art = tex2D(_MainTex, input.uv);
                float2 inDial = (input.uv - _Dial.xy) / max(_Dial.zw, float2(0.001, 0.001));
                float radius = length(inDial);
                float dial = SoftStep(1.0 - radius);
                float2 hand = _HandDirection.xy;
                float3 color = lerp(_DialColor.rgb, _ElapsedColor.rgb, ElapsedSector(inDial, hand));

                float rim = 0.76 * SoftStep(0.018 - abs(radius - 0.94));
                float ticks = max(CardinalTick(inDial), CardinalTick(inDial.yx));
                float marks = max(max(rim, ticks), HandCoverage(inDial, hand));
                color = lerp(color, _HandColor.rgb, marks);

                // Only repaint the bright disk so the model's dark ornaments stay visible.
                float plate = smoothstep(0.36, 0.82, dot(art.rgb, float3(0.2126, 0.7152, 0.0722)));
                return float4(lerp(art.rgb, color, dial * plate), art.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
