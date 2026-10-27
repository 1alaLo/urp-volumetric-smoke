using UnityEngine;

namespace SmokeVolumetric
{
    /// <summary>
    /// Smoke grenade: a ballistic projectile that pops in mid-air at the aimed
    /// point and leaves a <see cref="SmokeVolume"/> there. The fuse is set to the
    /// flight time, so the burst happens exactly at the aim point - never after
    /// bouncing along the floor.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SmokeGrenade : MonoBehaviour
    {
        public float cloudRadius = 4.2f;
        public float cloudLifetime = 18f;

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
                    s_BodyMaterial = new Material(shader) { name = "GrenadeBody" };
                    s_BodyMaterial.SetColor("_BaseColor", new Color(0.24f, 0.34f, 0.26f));
                    s_BodyMaterial.SetFloat("_Smoothness", 0.55f);
                    s_BodyMaterial.SetFloat("_Metallic", 0.25f);
                }
                return s_BodyMaterial;
            }
        }

        public static SmokeGrenade Throw(Vector3 from, Vector3 to, float flightTime,
                                         float cloudRadius = 4.2f, float cloudLifetime = 18f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "SmokeGrenade";
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.16f;

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = BodyMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var col = go.GetComponent<SphereCollider>();
            col.sharedMaterial = Bouncy;
            col.radius = 0.62f; // relative to the scaled mesh

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.42f;
            rb.linearDamping = 0.02f;
            rb.angularDamping = 0.15f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = Ballistics.VelocityFor(from, to, flightTime);
            rb.angularVelocity = Random.insideUnitSphere * 12f;

            var grenade = go.AddComponent<SmokeGrenade>();
            grenade.cloudRadius = cloudRadius;
            grenade.cloudLifetime = cloudLifetime;
            grenade._fuse = flightTime;
            grenade._spawnTime = Time.time;
            return grenade;
        }

        static PhysicsMaterial s_Bouncy;

        static PhysicsMaterial Bouncy
        {
            get
            {
                if (s_Bouncy == null)
                {
                    s_Bouncy = new PhysicsMaterial("GrenadeBounce")
                    {
                        bounciness = 0.36f,
                        dynamicFriction = 0.55f,
                        staticFriction = 0.6f,
                        bounceCombine = PhysicsMaterialCombine.Maximum
                    };
                }
                return s_Bouncy;
            }
        }

        void Update()
        {
            if (Time.time - _spawnTime >= _fuse)
                Detonate();
        }

        void OnCollisionEnter(Collision collision)
        {
            // clipped a wall or a crate on the way: pop where it hit, but not
            // during the first fraction of a second (that is the throw itself)
            if (Time.time - _spawnTime > 0.3f)
                Detonate();
        }

        void Detonate()
        {
            if (_spent)
                return;
            _spent = true;

            Vector3 burst = transform.position;
            SmokeVolume vol = SmokeVolume.Spawn(burst, cloudRadius, cloudLifetime, true);
            SmokeFx.Pop(burst, 0.9f);

            Destroy(gameObject);
            vol.transform.SetParent(null);
        }
    }
}
