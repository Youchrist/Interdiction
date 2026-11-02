Shader "Custom/SpriteTeamColor"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _TargetColor ("Color To Replace", Color) = (0,0.6,0.2,1)
        _TeamColor ("Team Color", Color) = (1,0,0,1)
        _HueTolerance ("Hue Match Tolerance", Range(0,0.5)) = 0.08
        _MinSaturation ("Min Saturation", Range(0,1)) = 0.15
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ PIXELSNAP_ON
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            fixed4 _Color;
            fixed4 _TargetColor;
            fixed4 _TeamColor;
            float _HueTolerance;
            float _MinSaturation;
            sampler2D _MainTex;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif
                return OUT;
            }

            float3 RGBtoHSV(float3 c)
            {
                float4 K = float4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 HSVtoRGB(float3 c)
            {
                float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, IN.texcoord);

                float3 hsv = RGBtoHSV(tex.rgb);
                float3 targetHSV = RGBtoHSV(_TargetColor.rgb);
                float hueDist = abs(hsv.x - targetHSV.x);
                hueDist = min(hueDist, 1.0 - hueDist);

                if (hueDist < _HueTolerance && hsv.y > _MinSaturation)
                {
                    float3 teamHSV = RGBtoHSV(_TeamColor.rgb);
                    float3 newHSV = float3(teamHSV.x, hsv.y, hsv.z); // swap hue only, keep shading
                    tex.rgb = HSVtoRGB(newHSV);
                }

                fixed4 c = tex * IN.color; // selection tint (green/yellow flash) applied after recolor
                c.rgb *= c.a;
                return c;
            }
        ENDCG
        }
    }
}