using UnityEngine;

namespace SmokeVolumetric
{
    /// <summary>
    /// Throwable smoke grenade: rigidbody projectile with a short fuse that
    /// detonates into a <see cref="SmokeVolume"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SmokeGrenade : MonoBehaviour
    {
        public float fuse = 1.55f;
        public float cloudRadius = 3.3f;
        public float cloudLifetime = 18f;

        static Material s_BodyMaterial;
        float _spawnTime;

        public static Material BodyMaterial
        {
            get
            {
                if (s_BodyMaterial == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit");
                    s_BodyMaterial = new Material(shader) { name = "GrenadeBody" };
                    s_BodyMaterial.SetColor("_BaseColor", new Color(0.24f, 0.30f, 0.24f));
                    s_BodyMaterial.SetFloat("_Smoothness", 0.55f);
                    s_BodyMaterial.SetFloat("_Metallic", 0.25f);
                }
                return s_BodyMaterial;
            }
        }

        public static SmokeGrenade Throw(Vector3 position, Vector3 velocity, float fuse = 1.55f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "SmokeGrenade";
            go.transform.position = position;
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
            rb.linearVelocity = velocity;
            rb.angularVelocity = Random.insideUnitSphere * 12f;

            var grenade = go.AddComponent<SmokeGrenade>();
            grenade.fuse = fuse;
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
            if (Time.time - _spawnTime < fuse)
                return;

            Detonate();
        }

        void Detonate()
        {
            Vector3 origin = transform.position;

            // settle the cloud on whatever surface is underneath
            if (Physics.Raycast(origin + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                origin = hit.point;

            SmokeVolume.Spawn(origin, cloudRadius, cloudLifetime);
            Destroy(gameObject);
        }
    }
}
