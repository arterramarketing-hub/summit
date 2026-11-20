// The start page's title (TitleArt.cs builds its texture, StartPage.cs drives it). A UI shader: the letters' edge
// from a signed distance (sharp at any size), their body graded ice (or rock) with frost in it, a bevel lit by
// the sun or moon from where it stands in the view, snow where TitleArt laid it with a glint here and there, a
// soft shadow under it all so it reads on cloud, and spindrift: fine snow blowing off the loaded tops.
// The reveal: frost spreading across the word from the left, its front bright as it forms, then a light
// sweeping once across the finished letters.
Shader "Summit/TitleLetters"
{
    Properties
    {
        [PerRendererData] _MainTex ("Title (TitleArt)", 2D) = "white" {}
        _Top ("Body at the top", Color) = (0.82, 0.91, 1, 1)
        _Bottom ("Body at the foot", Color) = (0.36, 0.52, 0.74, 1)
        _SnowCol ("Snow", Color) = (0.97, 0.985, 1, 1)
        _ShadowCol ("Shadow", Color) = (0.02, 0.04, 0.1, 1)
        _Light ("The sun or moon's light (rgb), and its strength on the letters (a)", Color) = (1, 1, 1, 1)
        _Ambient ("The sky's light", Color) = (0.5, 0.55, 0.65, 1)
        _LightDir ("Toward the light, in the picture (xy)", Vector) = (-0.5, 0.8, 0, 0)
        _Spec ("Specular", Float) = 0.5
        _Edge ("Edge light", Float) = 0.7
        _Frost ("Frost in the body", Float) = 0.5
        _ShadowA ("Shadow strength", Float) = 0.4
        _Drift ("Spindrift", Float) = 1
        _Reveal ("Reveal 0..1", Float) = 1
        _Sweep ("Light sweep position (-0.3 .. 1.3)", Float) = -1
        _Word ("VERTIGO's foot and cap height (v)", Vector) = (0.156, 0.6, 0, 0)
        _Spread ("Distance held in r, pixels", Float) = 20
        _TexSize ("Texture size", Vector) = (1280, 448, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "False" }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Top, _Bottom, _SnowCol, _ShadowCol, _Light, _Ambient, _LightDir, _Word, _TexSize;
            float _Spec, _Edge, _Frost, _ShadowA, _Drift, _Reveal, _Sweep, _Spread;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; return o; }

            float hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + 1), f.x), f.y);
            }
            // the four channels, and the signed distance out of r (pixels of the texture; positive outside)
            float Dist(float4 t) { return (0.5 - t.r) * 2 * _Spread; }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float4 t = tex2D(_MainTex, uv);
                float sd = Dist(t);
                float aa = max(fwidth(sd), 0.0001) * 0.7;
                float fill = saturate(0.5 - sd / aa);

                // the reveal: frost forming across the word from the left
                float n = vnoise(uv * float2(38, 14)) * 0.6 + vnoise(uv * float2(120, 40)) * 0.4;
                float front = _Reveal * 1.35 - (uv.x * 0.9 + n * 0.3 + (1 - uv.y) * 0.1);
                float shown = saturate(front * 14);
                float forming = saturate(1 - abs(front * 14 - 0.6) / 1.2) * step(_Reveal, 0.999);

                // the body: graded down the word, frosted, streaked as ice is
                float wv = saturate((uv.y - _Word.x) / (_Word.y - _Word.x));
                float3 body = lerp(_Bottom.rgb, _Top.rgb, smoothstep(0, 1, wv));
                float streak = vnoise(float2(uv.x * 190 + uv.y * 60, uv.y * 9));
                float frost = vnoise(uv * float2(260, 90)) * 0.6 + vnoise(uv * float2(700, 240)) * 0.4;
                body *= 1 + (streak - 0.5) * 0.35 * _Frost;
                body = lerp(body, float3(0.92, 0.96, 1), smoothstep(0.55, 0.85, frost) * 0.45 * _Frost * (1 - wv * 0.5));

                // the bevel, lit from where the light is
                float2 g = (t.ba - 0.5) * 2;
                float3 N = normalize(float3(-g, 0.55));
                float3 L = normalize(float3(_LightDir.xy, 0.85));
                float diff = saturate(dot(N, L));
                float3 H = normalize(L + float3(0, 0, 1));
                float spec = pow(saturate(dot(N, H)), 40) * _Spec;
                // the very edge: a fine bright line on the side toward the light, dark on the far side
                float rim = saturate(1 - (-sd) / 2.2) * fill;
                float2 outward = normalize(g + 1e-4);
                float facing = dot(-outward, normalize(_LightDir.xy + 1e-4));
                float3 light = _Light.rgb * _Light.a;
                float3 col = body * (_Ambient.rgb + light * (0.25 + 0.85 * diff)) + light * spec;
                col += light * rim * saturate(facing) * _Edge * 0.6;
                col *= 1 - rim * saturate(-facing) * 0.35 * _Edge;

                // snow: soft-lit and bright, a little blue where it thins, glinting
                float sn = t.g;
                float wrap = saturate((dot(N, L) + 0.35) / 1.35);
                float3 snowC = _SnowCol.rgb * (_Ambient.rgb * 1.1 + light * (0.35 + 0.75 * wrap));
                snowC = lerp(snowC * float3(0.82, 0.9, 1.05), snowC, smoothstep(0.2, 0.8, sn));
                float2 cell = floor(uv * _TexSize.xy * 0.5);
                float glint = step(0.993, hash(cell)) * (0.5 + 0.5 * sin(_Time.y * 3 + hash(cell + 7) * 40));
                snowC += light * glint * 0.9 * sn;
                col = lerp(col, snowC, smoothstep(0.15, 0.6, sn));

                // the sweep: one band of light across the finished letters
                float band = uv.x + (uv.y - 0.5) * 0.35 - _Sweep;
                col += light * exp(-band * band * 260) * 0.9 * fill;
                // frost forming at the reveal's front
                col += float3(0.75, 0.9, 1) * forming * 1.2;

                // the shadow below and to the side, soft, so the letters read on bright cloud
                float2 off = float2(0.004, -0.011);
                float sds = Dist(tex2D(_MainTex, uv + off));
                float shade = saturate(0.5 - (sds - 2) / 9) * _ShadowA;

                // spindrift: snow upwind of here (below and to the left) blows across it, thinning as it goes
                float up = 0;
                up += tex2D(_MainTex, uv + float2(-0.006, -0.012)).g * 0.6;
                up += tex2D(_MainTex, uv + float2(-0.016, -0.024)).g * 0.4;
                float gust = vnoise(uv * float2(26, 70) + float2(-_Time.y * 1.3, -_Time.y * 0.25)) * vnoise(uv * float2(9, 22) + float2(-_Time.y * 0.4, 0));
                float drift = saturate(up * smoothstep(0.25, 0.75, gust) * 1.4) * _Drift * (1 - fill);

                // laid down in order: shadow, spindrift, letters
                float aS = shade * shown, aD = drift * 0.55 * shown, aL = fill * shown;
                float3 c = _ShadowCol.rgb * aS;
                float a = aS;
                c = snowC * aD + c * (1 - aD); a = aD + a * (1 - aD);
                c = col * aL + c * (1 - aL); a = aL + a * (1 - aL);
                return float4(c / max(a, 1e-4), a * i.color.a);
            }
            ENDCG
        }
    }
}
