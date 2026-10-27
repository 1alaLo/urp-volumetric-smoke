using UnityEngine;

namespace SmokeVolumetric
{
    /// <summary>
    /// Frag grenade: same ballistic throw as the smoke canister, but it detonates
    /// into a blast that carves a crater through any smoke cloud and puffs the
    /// surviving volume outwards.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class FragGrenade : MonoBehaviour
    {
        public float blastRadius = 4.6f;
        public float smokePushRadius = 9f;
        public float smokePushStrength = 2.6f;

        static Material s_BodyMaterial;
        float _spawnTime;
        float _fuse;
        bool _spent;

        public static Material BodyMaterial
        {
            get
            {
                if (s_BodyMaterial == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit");
                    s_BodyMaterial = new Material(shader) { name = "FragBody" };
                    s_BodyMaterial.SetColor("_BaseColor", new Color(0.20f, 0.21f, 0.22f));
                    s_BodyMaterial.SetFloat("_Smoothness", 0.42f);
                    s_BodyMaterial.SetFloat("_Metallic", 0.55f);
                }
                return s_BodyMaterial;
            }
        }

        public static FragGrenade Throw(Vector3 from, Vector3 to, float flightTime)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "FragGrenade";
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.15f;

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = BodyMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var col = go.GetComponent<SphereCollider>();
            col.radius = 0.7f;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.4f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = Ballistics.VelocityFor(from, to, flightTime);
            rb.angularVelocity = Random.insideUnitSphere * 14f;

            var grenade = go.AddComponent<FragGrenade>();
            grenade._fuse = flightTime;
            grenade._spawnTime = Time.time;
            return grenade;
        }

        void Update()
        {
            if (Time.time - _spawnTime >= _fuse)
                Detonate();
        }

        void OnCollisionEnter(Collision collision)
        {
            if (Time.time - _spawnTime > 0.25f)
                Detonate();
        }

        void Detonate()
        {
            if (_spent)
                return;
            _spent = true;

            Vector3 p = transform.position;

            SmokeFx.Explosion(p, 1f);
            SmokeImpulses.AddExplosion(p, blastRadius);
            SmokeVolume.Blast(p, smokePushRadius, smokePushStrength);

            // shove loose rigidbodies around, purely for the demo feel
            var bodies = FindObjectsByType<Rigidbody>();
            for (int i = 0; i < bodies.Length; i++)
            {
                var rb = bodies[i];
                if (rb == null || rb.isKinematic)
                    continue;
                rb.AddExplosionForce(7f, p, blastRadius * 1.6f, 0.6f, ForceMode.VelocityChange);
            }

            Destroy(gameObject);
        }
    }
}
