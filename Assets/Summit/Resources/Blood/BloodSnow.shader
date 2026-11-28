// Blood soaked into snow (Blood.cs): every mark on the mountain is one mesh, laid on the snow's own surface (or a
// trunk's bark) a centimetre up, and drawn after the opaque world and the floodlights' pools as a multiply over
// what is already there (Blend DstColor SrcColor: out = 2 * src * dst). So the blood takes every bit of light the
// snow under it has (sun, moon, shadow, a floodlight's pool, the headlamp, fog) and only tints it the way blood in
// snow does: Beer-Lambert, a saturated dark-red core where it is thick, a pink wet rim where it has wicked out
// thin, fine capillary fingers at the rim. Over 0.5 brightens it, which is the wet sheen: the lights' highlights on
// fresh blood, as a share of the light that is there (BloodCommon.hlsl).
//
// Everything that moves is worked out here from the blood clock (_BloodClock, seconds): each mark soaks in over a
// second or two after it lands, the rim keeps creeping for a while, it darkens slowly as it dries, and falling
// snow (_BloodSnowAcc) and the powder thrown over it at the hit dust it over in flecks. On the snow, the marks
// follow the soft snow layer (SnowLayer) as it is pressed now: into the furrow the body dragged, over the rims.
//
//   uv0: the mark's own coordinates (xy), its seed, its kind (0 a stain, 1 a drag trail, 2 a drop, 3 on a trunk)
//   uv1: born (blood clock), soak time (s), amount (how much blood: 0..1), snow at birth (_BloodSnowAcc less the powder)
//   uv2: thrown (stretch ahead along +y), fade (1 .. 0), rest (a pool still filling), unused
//   uv3: the nearest floodlight's lens (xyz) and how much of the light here is its (0..1)
Shader "Summit/BloodSnow"
{
    Properties
    {
        _Noise ("Noise (BloodNoiseBake)", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-390" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Blood"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor SrcColor
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -8
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "BloodCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Noise_ST;
            CBUFFER_END
            float _BloodOnLayer;
            // the soft snow layer round the rider (SnowLayer.shader): the furrows, rims and holes bodies pressed in
            StructuredBuffer<float4> _SnowBase;   // x: terrain height, y/z: normal x/z, w: 1 snow, 0 rock, -1 a hole
            StructuredBuffer<float4> _SnowDyn;    // x: height change (m)
            float4 _SnowGrid, _SnowParams;

            float LayerAt(int i, int j)
            {
                float m = _SnowGrid.w;
                float e = min(min((float)i, m - i), min((float)j, m - j));
                if (e < 0.0) return 0.0;
                uint n = (uint)_SnowGrid.z, big = n * 8192u;
                uint sx = (uint)((int)_SnowGrid.x + i + (int)big) % n;
                uint sz = (uint)((int)_SnowGrid.y + j + (int)big) % n;
                uint s = sz * n + sx;
                return _SnowDyn[s].x * smoothstep(0.0, _SnowParams.z, e) * saturate(_SnowBase[s].w);
            }

            struct Attributes
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL;
                float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; float4 uv2 : TEXCOORD2; float4 uv3 : TEXCOORD3;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 mark : TEXCOORD2;    // local xy, seed, kind
                float4 life : TEXCOORD3;    // born, soak, amount, snow at birth
                float4 shape : TEXCOORD4;   // thrown, fade, rest, fog
                float4 lamp : TEXCOORD5;
                float wall : TEXCOORD6;     // on the steep wall of a hole in the soft layer
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                float3 pd = p;
                o.wall = 0.0;
                // on the snow (not a trunk): onto the soft layer as it is pressed now, bilinear between its cells.
                // Like the layer, it counts for depth as never below the terrain (the terrain is still drawn there,
                // and the layer only wins over it by that)
                if (_BloodOnLayer > 0.5 && i.uv0.w < 2.5)
                {
                    float2 c = p.xz / _SnowParams.x - _SnowGrid.xy;
                    int2 c0 = (int2)floor(c); float2 t = c - c0;
                    float h00 = LayerAt(c0.x, c0.y), h10 = LayerAt(c0.x + 1, c0.y), h01 = LayerAt(c0.x, c0.y + 1), h11 = LayerAt(c0.x + 1, c0.y + 1);
                    float h = lerp(lerp(h00, h10, t.x), lerp(h01, h11, t.x), t.y);
                    p.y += h + _SnowParams.y * saturate(abs(h) / 0.01);
                    pd.y = max(p.y, pd.y - 0.004);
                    float steep = max(max(abs(h10 - h00), abs(h01 - h00)), max(abs(h11 - h10), abs(h11 - h01)));
                    o.wall = saturate(steep / 0.12 - 0.5) * saturate(-h / 0.15);
                }
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                float4 cd = TransformWorldToHClip(pd);
                o.positionCS.z = cd.z / cd.w * o.positionCS.w;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.mark = i.uv0; o.life = i.uv1; o.lamp = i.uv3;
                o.shape = float4(i.uv2.xyz, ComputeFogFactor(o.positionCS.z));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.mark.xy;
                float seed = i.mark.z, kind = i.mark.w;
                float age = max(_BloodClock - i.life.x, 0.0);
                float thrown = i.shape.x, fade = i.shape.y, rest = i.shape.z;
                float amount = i.life.z;
                float D, wetEdge;

                if (kind < 0.5 || kind > 2.5)
                {
                    // a stain (an impact, a pool), or on a trunk with runs down it
                    D = BloodStain(p, seed, age, i.life.y, thrown, rest, kind > 2.5 ? 1.0 : 0.0, wetEdge) * amount;
                }
                else if (kind < 1.5)
                {
                    // a drag trail: x across (in its half widths), y along (m). Striated along the drag by the
                    // clothes, wobbling, broken up where it was running dry
                    float soak = BloodSoak(age, i.life.y);
                    float creep = 0.05 * log2(1.0 + age * 0.25);
                    float a = p.y, x = p.x;
                    half4 n1 = BloodN(float2(a * 0.045, x * 0.05) + seed);
                    half4 n2 = BloodN(float2(a * 0.018, x * 0.22) + seed * 1.3 + 0.5);
                    half4 n3 = BloodN(float2(a * 0.11, x * 0.09) + seed * 0.7 + 0.21);
                    float spread = lerp(0.55, 1.0, soak) + creep;
                    float e = abs(x + (n1.r - 0.5) * 0.7) / spread;
                    float body = 1.0 - smoothstep(0.62 + 0.25 * n1.g, 1.0 + 0.25 * n1.g, e);
                    float streak = lerp(0.45, 1.2, n2.g);
                    float keep = smoothstep(1.15 - 0.9 * amount - 0.12, 1.15 - 0.9 * amount + 0.12, n3.r);
                    float core = 1.0 - smoothstep(0.0, 0.85, e);
                    D = body * keep * streak * (0.2 + 0.75 * amount) * (0.3 + 0.7 * core);
                    wetEdge = body * (1.0 - core) * keep;
                    // spots shaken off along its sides
                    D = max(D, 0.6 * BloodDrops(p * float2(1.0, 0.6), seed, 0.45, 0.22 * amount) * step(1.05, e) * (1.0 - smoothstep(1.7, 2.2, abs(x))));
                }
                else
                {
                    // a drop: round where it fell straight, a tail ahead and a smaller drop beyond where it came in low
                    float soak = BloodSoak(age, i.life.y);
                    float2 q = float2(p.x, p.y > 0.0 ? p.y / (1.0 + 1.8 * thrown) : p.y * 1.15);
                    half4 lo = BloodN(p * 0.07 + seed);
                    half4 hi = BloodN(p * 0.33 + seed * 2.1);
                    float R = lerp(0.3, 0.52, saturate(soak + 0.2)) * (0.85 + 0.3 * lo.r);
                    float r = length(q) * (0.82 + 0.36 * hi.r);   // a ragged edge where the snow took it up
                    float body = 1.0 - smoothstep(R * 0.8, R, r);
                    float core = 1.0 - smoothstep(0.0, R * 0.9, r);
                    float2 t = p - float2(0.0, R * (1.0 + 2.2 * thrown) + 0.12);
                    float tail = thrown > 0.35 ? 1.0 - smoothstep(0.06, 0.1, length(t)) : 0.0;
                    D = (body * (0.35 + 0.75 * core * lerp(0.7, 1.2, hi.g)) + tail * 0.7) * amount;
                    wetEdge = body * (1.0 - core);
                }
                D *= fade * (1.0 - 0.8 * i.wall);
                return BloodShade(D, wetEdge, age, i.life.w, i.positionWS, i.normalWS, seed, i.lamp, i.shape.w);
            }
            ENDHLSL
        }
    }
}
