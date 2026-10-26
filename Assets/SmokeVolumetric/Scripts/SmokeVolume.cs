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
        // ---- tuning (CS2-ish: ~6.5m across, ~18s of coverage) ----
        public float maxRadius = 3.2f;
        public float lifetime = 18f;
        public float expandTime = 1.25f;
        public float fadeTime = 3.5f;
        public float startRadius = 0.55f;
        public float riseDistance = 0.9f;

        static Material s_Template;
        static int s_ActiveCount;

        public static int ActiveCount => s_ActiveCount;

        MeshRenderer _renderer;
        MaterialPropertyBlock _mpb;
        Vector3 _origin;
        float _radius;
        float _age;
        float _seed;
        float _densityMul;

        static readonly int IdNoiseTex = Shader.PropertyToID("_NoiseTex");
        static readonly int IdCenter = Shader.PropertyToID("_Center");
        static readonly int IdRadius = Shader.PropertyToID("_Radius");
        static readonly int IdAge = Shader.PropertyToID("_Age");
        static readonly int IdFade = Shader.PropertyToID("_Fade");
        static readonly int IdSeed = Shader.PropertyToID("_Seed");
        static readonly int IdDensityMul = Shader.PropertyToID("_DensityMul");

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

        /// <summary>Spawn a cloud centred on <paramref name="groundCenter"/>.</summary>
        public static SmokeVolume Spawn(Vector3 groundCenter, float maxRadius = 3.3f, float lifetime = 18f)
        {
            var go = new GameObject("SmokeVolume");
            go.transform.position = groundCenter;
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
            vol.Init(groundCenter);
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

        void Init(Vector3 groundCenter)
        {
            _renderer = GetComponent<MeshRenderer>();
            _mpb = new MaterialPropertyBlock();
            _origin = groundCenter + Vector3.up * (maxRadius * 0.47f);
            _radius = startRadius;
            _seed = Random.value;
            _age = 0f;
            _densityMul = 0f;
            transform.position = _origin;
            transform.rotation = Quaternion.Euler(0f, _seed * 360f, 0f);
            transform.localScale = Vector3.one * (startRadius * 2f);
            s_ActiveCount++;
            ApplyState();
        }

        void OnDestroy()
        {
            s_ActiveCount = Mathf.Max(0, s_ActiveCount - 1);
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
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
