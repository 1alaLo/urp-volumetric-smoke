using System.Collections.Generic;
using UnityEngine;

namespace SmokeVolumetric
{
    /// <summary>
    /// A single volumetric smoke cloud. Owns the lifetime curve (burst, hold,
    /// erode-away) and pushes per-cloud parameters to the raymarch shader
    /// through a MaterialPropertyBlock so all clouds share one material.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class SmokeVolume : MonoBehaviour
    {
        // ---- tuning (CS2-ish: ~8m across, ~18s of coverage) ----
        public float maxRadius = 4.2f;
        public float lifetime = 18f;
        public float expandTime = 1.25f;
        public float fadeTime = 3.5f;
        public float startRadius = 0.55f;
        public float riseDistance = 0.55f;

        static Material s_Template;

        /// <summary>Blast volumes remembered per cloud (local, permanent erosion).</summary>
        public const int MaxBlasts = 4;

        /// <summary>Every live cloud, so weapons can test rays against them.</summary>
        public static readonly List<SmokeVolume> All = new List<SmokeVolume>(8);

        public static int ActiveCount => All.Count;

        MeshRenderer _renderer;
        MaterialPropertyBlock _mpb;
        Vector3 _origin;
        float _radius;
        float _age;
        float _seed;
        float _densityMul;
        float _spread;
        float _blast;
        SmokeField.Reach _reach;

        readonly Vector4[] _blastA = new Vector4[MaxBlasts];
        readonly float[] _blastStrength = new float[MaxBlasts];
        int _blastCount;
        int _blastCursor;

        public float Radius => _radius;
        public Vector3 Center => transform.position;

        static readonly int IdNoiseTex = Shader.PropertyToID("_NoiseTex");
        static readonly int IdReachTex = Shader.PropertyToID("_ReachTex");
        static readonly int IdCenter = Shader.PropertyToID("_Center");
        static readonly int IdRadius = Shader.PropertyToID("_Radius");
        static readonly int IdAge = Shader.PropertyToID("_Age");
        static readonly int IdFade = Shader.PropertyToID("_Fade");
        static readonly int IdSeed = Shader.PropertyToID("_Seed");
        static readonly int IdDensityMul = Shader.PropertyToID("_DensityMul");
        static readonly int IdGridMin = Shader.PropertyToID("_GridMin");
        static readonly int IdGridInv = Shader.PropertyToID("_GridInv");
        static readonly int IdGridParams = Shader.PropertyToID("_GridParams");
        static readonly int IdBlastA0 = Shader.PropertyToID("_BlastA0");
        static readonly int IdBlastA1 = Shader.PropertyToID("_BlastA1");
        static readonly int IdBlastA2 = Shader.PropertyToID("_BlastA2");
        static readonly int IdBlastA3 = Shader.PropertyToID("_BlastA3");
        static readonly int IdBlastStrength = Shader.PropertyToID("_BlastStrength");
        static readonly int IdBlastCount = Shader.PropertyToID("_BlastCount");

        public static Material Template
        {
            get
            {
                if (s_Template == null)
                {
                    s_Template = Resources.Load<Material>("SmokeVolume");
                    if (s_Template == null)
                    {
                        var shader = Shader.Find("SmokeVolumetric/SmokeVolume");
                        if (shader != null)
                            s_Template = new Material(shader);
                    }
                    if (s_Template != null)
                        s_Template.SetTexture(IdNoiseTex, SmokeNoise.Get());
                }
                return s_Template;
            }
        }

        /// <summary>
        /// Spawn a cloud. <paramref name="point"/> is the burst point: for an air
        /// burst the cloud is centred there, for a ground burst it is lifted so
        /// the sphere sits on the surface instead of half-buried.
        /// </summary>
        public static SmokeVolume Spawn(Vector3 point, float maxRadius = 4.2f, float lifetime = 18f,
                                        bool airBurst = true)
        {
            var go = new GameObject("SmokeVolume");
            go.transform.position = point;
            go.layer = 0;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = GetSphereMesh();

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Template;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            var vol = go.AddComponent<SmokeVolume>();
            vol.maxRadius = maxRadius;
            vol.lifetime = lifetime;
            vol.Init(point, airBurst);
            return vol;
        }

        static Mesh s_Sphere;

        static Mesh GetSphereMesh()
        {
            if (s_Sphere != null)
                return s_Sphere;

            // reuse the built-in sphere so bounds/coverage are correct
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s_Sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Destroy(tmp);
            else DestroyImmediate(tmp);

            return s_Sphere;
        }

        void Init(Vector3 point, bool airBurst)
        {
            _renderer = GetComponent<MeshRenderer>();
            _mpb = new MaterialPropertyBlock();
            _origin = airBurst ? point : point + Vector3.up * (maxRadius * 0.47f);
            _radius = startRadius;
            _spread = startRadius;
            _seed = Random.value;
            _age = 0f;
            _densityMul = 0f;
            transform.position = _origin;
            transform.rotation = Quaternion.Euler(0f, _seed * 360f, 0f);
            transform.localScale = Vector3.one * (startRadius * 2f);

            // free-space diffusion field for this burst: makes the cloud hug
            // walls and pour through openings instead of being a clean sphere
            _reach = SmokeField.BuildReach(point);

            All.Add(this);
            ApplyState();
        }

        void OnDestroy()
        {
            All.Remove(this);

            if (_reach.texture != null)
            {
                if (Application.isPlaying) Destroy(_reach.texture);
                else DestroyImmediate(_reach.texture);
                _reach.texture = null;
            }
        }

        /// <summary>Ray/sphere test against the cloud's proxy volume.</summary>
        public bool RaySphere(Vector3 origin, Vector3 dir, out float enter, out float exit)
        {
            Vector3 oc = origin - transform.position;
            float b = Vector3.Dot(oc, dir);
            float c = oc.sqrMagnitude - _radius * _radius;
            float h = b * b - c;
            if (h <= 0f)
            {
                enter = exit = 0f;
                return false;
            }
            h = Mathf.Sqrt(h);
            enter = -b - h;
            exit = -b + h;
            return exit > 0f;
        }

        /// <summary>
        /// Explosion near the cloud: register the blast volume that overlaps this
        /// cloud. Only the smoke inside that volume is eaten; the rest of the puff
        /// keeps its size and density. The volume lives for the rest of the
        /// cloud's lifetime, so the hole does not silently refill.
        /// </summary>
        public void ApplyBlast(Vector3 center, float radius, float strength)
        {
            strength = Mathf.Clamp01(strength);
            if (strength <= 0f)
                return;

            _blastA[_blastCursor] = new Vector4(center.x, center.y, center.z, Mathf.Max(0.1f, radius));
            _blastStrength[_blastCursor] = strength;
            _blastCursor = (_blastCursor + 1) % MaxBlasts;
            _blastCount = Mathf.Min(_blastCount + 1, MaxBlasts);

            // one short outward shove while the smoke is displaced
            _blast = Mathf.Max(_blast, strength);
        }

        public static void Blast(Vector3 center, float radius, float strength)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var v = All[i];
                float reach = radius + v._radius;
                if ((center - v.transform.position).sqrMagnitude > reach * reach)
                    continue;
                v.ApplyBlast(center, radius, strength);
            }
        }

        /// <summary>Evaluate the cloud at an arbitrary age (used for edit-mode preview).</summary>
        public void Preview(float age)
        {
            _age = age;
            ApplyState();
        }

        void LateUpdate()
        {
            _age += Time.deltaTime;

            if (_age > lifetime)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
                return;
            }

            ApplyState();
        }

        void ApplyState()
        {
            if (_renderer == null || _mpb == null)
                return;

            // burst that eases out into a slow creep
            float e = Mathf.Clamp01(_age / expandTime);
            e = e * e * (3f - 2f * e);
            float creep = 1f + 0.10f * Mathf.Clamp01(_age / lifetime);
            _radius = Mathf.LerpUnclamped(startRadius, maxRadius, e) * creep;

            // a blast only gives the shell one short outward kick; the hole
            // itself is carved by the local blast volume in the shader
            if (_blast > 0.001f)
            {
                _blast *= Mathf.Exp(-Time.deltaTime * 3.2f);
                _radius += _blast * 0.35f;
            }
            _spread = _radius;

            // erosion fade at the end of life
            float fade = 1f;
            if (_age > lifetime - fadeTime)
                fade = Mathf.Clamp01(1f - (_age - (lifetime - fadeTime)) / fadeTime);
            fade = Mathf.SmoothStep(0f, 1f, fade);

            // density ramps in quickly but not instantly
            _densityMul = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_age / 0.30f));

            // gentle buoyancy
            float rise = riseDistance * (1f - Mathf.Exp(-_age * 0.35f));
            transform.position = _origin + Vector3.up * rise;
            transform.localScale = Vector3.one * (_radius * 2f);

            _mpb.SetVector(IdCenter, transform.position);
            _mpb.SetFloat(IdRadius, _radius);
            _mpb.SetFloat(IdAge, _age);
            _mpb.SetFloat(IdFade, fade);
            _mpb.SetFloat(IdSeed, _seed);
            _mpb.SetFloat(IdDensityMul, _densityMul);
            _mpb.SetTexture(IdNoiseTex, SmokeNoise.Get());

            _mpb.SetVector(IdBlastA0, _blastA[0]);
            _mpb.SetVector(IdBlastA1, _blastA[1]);
            _mpb.SetVector(IdBlastA2, _blastA[2]);
            _mpb.SetVector(IdBlastA3, _blastA[3]);
            _mpb.SetVector(IdBlastStrength, new Vector4(
                _blastStrength[0], _blastStrength[1], _blastStrength[2], _blastStrength[3]));
            _mpb.SetFloat(IdBlastCount, _blastCount);

            bool hasGrid = _reach.texture != null;
            _mpb.SetVector(IdGridParams, new Vector4(
                hasGrid ? 1f : 0f,
                _spread,
                SmokeField.MaxSpread,
                SmokeField.MaxSolid));
            _mpb.SetVector(IdGridMin, hasGrid ? (Vector4)_reach.gridMin : Vector4.zero);
            _mpb.SetVector(IdGridInv, hasGrid ? (Vector4)_reach.gridInv : Vector4.zero);
            if (hasGrid)
                _mpb.SetTexture(IdReachTex, _reach.texture);

            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
