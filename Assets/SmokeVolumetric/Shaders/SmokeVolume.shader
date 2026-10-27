// Volumetric smoke for URP.
// Analytic ray/sphere intersection + ray-marched extinction/in-scattering
// against a tileable 3D noise density field, with light self-shadowing.
// Rendered as a transparent proxy sphere; scene depth clamps the ray so
// geometry in front of / inside the cloud composites correctly.
Shader "SmokeVolumetric/SmokeVolume"
{
    Properties
    {
        _NoiseTex ("Noise (3D)", 3D) = "white" {}
        _NoiseScale ("Noise Scale", Float) = 1.05
        _Density ("Density", Float) = 5.0
        _Absorption ("Absorption", Float) = 1.45
        _ScatterColor ("Scatter Color", Color) = (0.93, 0.945, 0.97, 1)
        _AmbientColor ("Ambient Tint", Color) = (0.34, 0.40, 0.50, 1)
        _AmbientStrength ("Ambient Strength", Float) = 1.25
        _LightScatter ("Light Scatter", Float) = 1.0
        _PhaseG ("Phase G", Range(-0.9, 0.9)) = 0.42
        _StepCount ("Ray Steps", Float) = 56
        _EdgeSoftness ("Edge Softness", Range(0.05, 1)) = 0.42
        _RiseSpeed ("Rise Speed", Float) = 0.26
        _Swirl ("Swirl", Float) = 0.45
        _Fade ("Fade", Range(0, 1)) = 1
        _DensityMul ("Density Mul", Float) = 1
        _Age ("Age", Float) = 0
        _Center ("Center (world)", Vector) = (0, 0, 0, 0)
        _Radius ("Radius (world)", Float) = 3
        _Seed ("Seed", Float) = 0
        _ShadowStrength ("Self Shadow", Range(0, 3)) = 1.35
        _BlastA0 ("Blast 0", Vector) = (0, 0, 0, 0)
        _BlastA1 ("Blast 1", Vector) = (0, 0, 0, 0)
        _BlastA2 ("Blast 2", Vector) = (0, 0, 0, 0)
        _BlastA3 ("Blast 3", Vector) = (0, 0, 0, 0)
        _BlastStrength ("Blast Strengths", Vector) = (0, 0, 0, 0)
        _BlastCount ("Blast Count", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "SmokeVolume"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma exclude_renderers gles gles3

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE3D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            // per-burst scene field: R = distance to solid, G = free-space
            // distance from the burst point (see SmokeField.cs)
            TEXTURE3D(_ReachTex);
            SAMPLER(sampler_ReachTex);

            // global capsule holes: bullet tunnels.
            // xyz = segment ends, w = radius (A) / remaining life 0..1 (B)
            float4 _ImpulseA[8];
            float4 _ImpulseB[8];
            float  _ImpulseCount;

            // per-cloud blast volumes: local, persistent density loss. A blast
            // eats the part of the cloud it overlaps instead of shrinking the
            // whole puff (xyz = centre, w = radius; _BlastStrength = 0..1).
            // Packed as scalars on purpose: per-renderer MPB overrides of
            // arrays proved unreliable here.

            CBUFFER_START(UnityPerMaterial)
                float4 _ScatterColor;
                float4 _AmbientColor;
                float4 _Center;
                float4 _GridMin;
                float4 _GridInv;
                float4 _GridParams;   // x on, y spread radius, z spread max, w solid max
                float  _NoiseScale;
                float  _Density;
                float  _Absorption;
                float  _AmbientStrength;
                float  _LightScatter;
                float  _PhaseG;
                float  _StepCount;
                float  _EdgeSoftness;
                float  _RiseSpeed;
                float  _Swirl;
                float  _Fade;
                float  _DensityMul;
                float  _Age;
                float  _Radius;
                float  _Seed;
                float  _ShadowStrength;
                float4 _BlastA0;
                float4 _BlastA1;
                float4 _BlastA2;
                float4 _BlastA3;
                float4 _BlastStrength;
                float  _BlastCount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 wpos = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = wpos;
                o.positionCS = TransformWorldToHClip(wpos);
                o.screenPos  = ComputeScreenPos(o.positionCS);
                return o;
            }

            // ---------------------------------------------------------------
            // density field
            // ---------------------------------------------------------------
            float DensityCore(float3 p)
            {
                float invR = 1.0 / max(_Radius, 1e-3);
                float3 n = (p - _Center.xyz) * invR;   // local space, -1..1
                n.y *= 1.18;                            // squat, mushroom-ish profile

                // slow internal circulation + rise, anchored so the cloud
                // keeps billowing while it ages
                float t = _Age;
                float3 drift = float3(0.05 + _Seed * 0.04, -_RiseSpeed * 0.5, 0.04) * t;
                float3 warp = float3(
                    sin(n.y * 3.1 + t * 0.85 + _Seed * 6.283),
                    sin(n.z * 2.7 + t * 0.63 + _Seed * 3.141),
                    sin(n.x * 3.3 + t * 0.74 + _Seed * 1.571)) * _Swirl * 0.16;

                float3 q = n * _NoiseScale + drift + warp;

                // explicit LOD 0: implicit derivatives inside a divergent
                // raymarch loop collapse to the top mip and flatten the field
                float n1 = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, q, 0).r;
                float n2 = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, q * 2.31 + float3(0.37, 0.11, 0.73), 0).r;

                float base = n1 * 0.78 + n2 * 0.22;   // raw mean ~0.31

                // remap to actually use the 0..1 range: the raw noise sits low,
                // so a naive smoothstep(0.3,0.7) crushes most of the cloud to nothing
                float f = saturate((base - 0.08) * 1.55);

                // push the silhouette around with the same field so the edge
                // is cauliflower-shaped rather than a clean sphere
                float radial = length(n) + (base - 0.5) * 0.78;
                float shape = saturate(1.0 - smoothstep(1.0 - _EdgeSoftness, 1.03, radial));

                // billowing shell + a core that still follows the noise, so the
                // interior keeps visible structure instead of one flat grey wall
                float shell = smoothstep(0.05, 0.85, f);
                float core = smoothstep(0.72, 0.12, length(n)) * (0.55 + 0.45 * f);
                float d = shape * max(shell, core * 0.9);

                // The proxy mesh is an analytic sphere and the march is clipped at
                // its silhouette, so the field must reach exactly zero just inside
                // that radius. Keep the band thin: the coarse `shape` term above is
                // what gives the outline its cauliflower edge, this only removes the
                // hard circular cut where the proxy sphere truncates it.
                float pc = length(p - _Center.xyz) * invR;
                d *= saturate((1.0 - pc) * 30.0);

                // as _Fade drops the cloud erodes into wisps and gets optically
                // thinner, so the last seconds read as dissipating smoke instead of
                // a solid ball that pops out of existence
                float erode = (1.0 - _Fade) * 0.72;
                d = shape * saturate((d - erode) / max(1e-3, 1.0 - erode));
                d *= _Fade;

                return pow(saturate(d), 1.05) * _DensityMul;
            }

            // How much of the cloud is allowed to exist at p, given the scene.
            // `reach` is the diffusion front (a BFS distance through free space),
            // `contact` keeps the field from pressing into geometry.
            float SceneMask(float3 p)
            {
                float3 g = (p - _GridMin.xyz) * _GridInv.xyz;
                float inside = step(0.0, g.x) * step(g.x, 1.0)
                             * step(0.0, g.y) * step(g.y, 1.0)
                             * step(0.0, g.z) * step(g.z, 1.0);

                float4 s = SAMPLE_TEXTURE3D_LOD(_ReachTex, sampler_ReachTex, saturate(g), 0);
                float solidDist  = s.r * _GridParams.w;
                float spreadDist = s.g * _GridParams.z;

                float reach   = saturate((_GridParams.y - spreadDist) / 0.55);
                float contact = saturate(solidDist / 0.14);
                return reach * contact * inside;
            }

            // Bullet tunnels are capsules carved out of the field. The wall of
            // the hole is displaced by a single coherent noise tap (features of
            // ~1.5 m) plus a slow angular lobe, then feathered by a smoothstep:
            // the channel stays open, the rim frays into torn wisps, and the
            // edge no longer reads as a high-frequency noise texture.
            float ImpulseMask(float3 p)
            {
                int n = (int)_ImpulseCount;
                if (n <= 0)
                    return 0.0;

                // two coherent taps: a broad one that tilts the whole bore, and
                // a finer one whose features are comparable to the hole itself
                // so the silhouette actually tears instead of staying a circle
                float rough = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex,
                                                   p * 0.62 + float3(0.17, 0.53, 0.81), 0).r;
                float fine = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex,
                                                  p * 1.30 + float3(0.71, 0.29, 0.47), 0).r;

                // slow undulation along the shaft: period ~2 m, derived from the
                // same tap so the rim opens in big soft bites, not static
                float axial = 0.5 + 0.5 * sin(dot(p, float3(2.3, 1.7, 2.9)) + rough * 5.0);

                float hole = 0.0;
                [loop]
                for (int i = 0; i < n; i++)
                {
                    float4 a = _ImpulseA[i];
                    float4 b = _ImpulseB[i];
                    float3 ab = b.xyz - a.xyz;
                    float  t  = saturate(dot(p - a.xyz, ab) / max(dot(ab, ab), 1e-4));
                    float3 c  = a.xyz + ab * t;
                    float  dist = length(p - c);

                    // gentle oval + coherent wobble: the bore stays recognisably
                    // cylindrical while its wall breathes in and out
                    float ang = atan2(p.z - c.z, p.x - c.x);
                    float lobe = 1.0 + 0.26 * (rough - 0.5) + 0.24 * (fine - 0.5)
                               + 0.09 * sin(ang * 3.0 + i * 2.1)
                               + 0.05 * sin(ang * 5.0 - i * 1.3);
                    float radius = max(a.w * lobe, 1e-3);

                    // slow undulation along the shaft so the opening bites in
                    // and out instead of extruding one static cross-section
                    dist += (axial - 0.5) * a.w * 0.55;

                    // small clear core with a long frazzled shoulder: the bullet
                    // punches through, and the rim frays out into thinned wisps
                    float k = dist / radius;
                    float core = 1.0 - smoothstep(0.68, 1.0, k);
                    hole = max(hole, core * b.w);
                }
                return hole;
            }

            // Local blast erosion: a blast volume removes the smoke it overlaps
            // and leaves the rest of the cloud intact. The radius is pushed
            // around by low-frequency noise so the cavity is an irregular bite,
            // not a clean sphere.
            float BlastAt(float3 p, float4 s, float strength, float rough)
            {
                if (s.w <= 0.0 || strength <= 0.0)
                    return 0.0;

                float3 d = p - s.xyz;
                d.y *= 1.15;                           // slightly flattened burst
                float rad = max(s.w * (0.80 + 0.40 * rough), 1e-3);
                // open cavity out to ~40% of the radius, then a soft, slightly
                // ragged shoulder: smoke disappears where the blast hits and
                // thins out towards the edge of the sphere
                float k = length(d) / rad;
                return (1.0 - smoothstep(0.40, 1.0, k)) * strength;
            }

            float BlastMask(float3 p)
            {
                int n = (int)_BlastCount;
                if (n <= 0)
                    return 0.0;

                float rough = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex,
                                                   p * 0.42 + float3(0.61, 0.23, 0.87), 0).r;

                float m = 0.0;
                if (n > 0) m = max(m, BlastAt(p, _BlastA0, _BlastStrength.x, rough));
                if (n > 1) m = max(m, BlastAt(p, _BlastA1, _BlastStrength.y, rough));
                if (n > 2) m = max(m, BlastAt(p, _BlastA2, _BlastStrength.z, rough));
                if (n > 3) m = max(m, BlastAt(p, _BlastA3, _BlastStrength.w, rough));
                return m;
            }

            float DensityAt(float3 p)
            {
                float d = DensityCore(p);
                if (d <= 0.0)
                    return 0.0;

                if (_GridParams.x > 0.5)
                    d *= SceneMask(p);

                if (_BlastCount > 0.5)
                    d *= saturate(1.0 - BlastMask(p) * 1.15);

                if (_ImpulseCount > 0.5)
                    d *= saturate(1.0 - ImpulseMask(p) * 1.35);

                return d;
            }

            float HenyeyGreenstein(float cosTheta, float g)
            {
                float g2 = g * g;
                float denom = max(1e-4, 1.0 + g2 - 2.0 * g * cosTheta);
                return (1.0 - g2) / (4.0 * PI * pow(denom, 1.5));
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float3 ro = _WorldSpaceCameraPos;
                float3 rd = normalize(i.positionWS - ro);

                // --- how far the opaque scene is along this ray -----------
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float rawDepth = SampleSceneDepth(uv);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                float  cosView = max(dot(rd, camFwd), 1e-3);
                float  tScene = eyeDepth / cosView;

                // --- analytic sphere intersection -------------------------
                float3 center = _Center.xyz;
                float  radius = max(_Radius, 1e-3);

                float3 oc = ro - center;
                float  b  = dot(oc, rd);
                float  c  = dot(oc, oc) - radius * radius;
                float  h  = b * b - c;
                if (h <= 0.0) discard;
                h = sqrt(h);

                float t0 = -b - h;
                float t1 = -b + h;
                t0 = max(t0, 0.0);          // camera may sit inside the cloud
                t1 = min(t1, tScene);       // stop at the first opaque surface
                if (t1 <= t0) discard;

                // adaptive: keep the sample spacing roughly constant so a big
                // cloud does not turn into visible slices, while short chords
                // through the edge stay cheap
                float span    = t1 - t0;
                int   steps   = (int)clamp(ceil(span * 7.5), 20.0, max(_StepCount, 20.0));
                float stepLen = span / steps;

                // interleaved gradient noise dither hides the slicing
                float jitter = frac(52.9829189 * frac(dot(i.positionCS.xy, float2(0.06711056, 0.00583715))));

                Light mainLight = GetMainLight();
                float3 L    = mainLight.direction;
                float3 Lcol = mainLight.color;
                float  phase = HenyeyGreenstein(dot(-rd, L), _PhaseG) * 4.0 * PI;

                float3 ambTop = SampleSH(float3(0.0, 1.0, 0.0)) * _AmbientStrength;
                float3 ambBot = SampleSH(float3(0.0, -1.0, 0.0)) * _AmbientStrength;

                float sigmaT = _Density * _Absorption;
                float T = 1.0;
                float3 scatter = 0.0;

                float t = t0 + stepLen * jitter;
                [loop]
                for (int s = 0; s < steps; s++)
                {
                    float3 p = ro + rd * t;
                    float d = DensityAt(p);

                    if (d > 1e-4)
                    {
                        // Sub-metre turbulence. Extinction saturates within the first
                        // metre or so, so the interior only ever shows the nearest
                        // slice of the field: without this it is a featureless wall.
                        float3 qf = (p - _Center.xyz) * (3.8 / max(_Radius, 1e-3))
                                  + float3(0.21, 0.57, 0.83)
                                  + float3(0.02, -0.08, 0.03) * _Age;
                        float fine = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, qf, 0).r;

                        // ...but only within a couple of metres of the camera, and never
                        // far enough down to open a see-through channel: a ray must stay
                        // extinct, otherwise geometry leaks through the cloud. Farther
                        // out the march step is coarser than these features anyway, and
                        // from outside the detail is not visible.
                        float detail = saturate(1.0 - t * 0.5);
                        float mod = 1.0 + (fine - 0.31) * 1.1 * detail;
                        d *= max(mod, 0.72);

                        // cheap light penetration: a few taps toward the sun.
                        // Deliberately the plain field - the scene mask and the
                        // hole test would triple the cost of every shadow tap.
                        float od = 0.0;
                        [unroll]
                        for (int k = 1; k <= 3; k++)
                        {
                            od += DensityCore(p + L * (radius * (0.12 + 0.16 * k)));
                        }
                        od *= radius * 0.16 * sigmaT * _ShadowStrength;
                        float sunT = exp(-od);

                        float3 lightIn = Lcol * (sunT * (0.45 + phase * 0.22) * _LightScatter);
                        // cheap multiple-scattering fill so thick smoke reads as
                        // bright fog instead of going pitch black inside
                        float3 multiScatter = Lcol * (1.0 - sunT) * 0.20;
                        // Height inside the cloud drives the ambient term: sunlit top,
                        // shadowed underside. Using the cloud's own normalised height
                        // (not world Y) is what keeps the interior readable instead of
                        // flattening into a single grey card.
                        float hNorm = saturate((p.y - (center.y - radius)) / max(2.0 * radius, 1e-3));
                        float3 ambIn = lerp(ambBot * 0.50, ambTop * 1.15, pow(hNorm, 1.25));

                        // Dense core reads darker than the thin outer wisps: light is
                        // extinguished before it reaches the middle of the cloud. The
                        // noise term keeps the interior structured instead of flat.
                        float shade = 0.98 - 0.30 * d + (fine - 0.31) * 0.95 * detail;

                        float3 S = _ScatterColor.rgb * (lightIn + multiScatter + ambIn * _AmbientColor.rgb) * 0.72 * shade;

                        float sigma = sigmaT * d;
                        float atten = exp(-sigma * stepLen);
                        scatter += T * S * (1.0 - atten);
                        T *= atten;

                        if (T < 0.004) break;
                    }
                    t += stepLen;
                }

                float alpha = saturate(1.0 - T);
                if (alpha < 0.002) discard;

                // premultiplied: blend One OneMinusSrcAlpha
                return float4(scatter, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

