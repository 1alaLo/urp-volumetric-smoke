using UnityEngine;

namespace SmokeVolumetric
{
    /// <summary>
    /// Flashbang: same ballistic throw as the frag, but its payoff is a blinding
    /// screen flash instead of a crater. It only nudges smoke with a weak blast
    /// wave, so the two throwables read completely differently.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class FlashGrenade : MonoBehaviour
    {
        public float flashRadius = 15f;
        public float smokePushRadius = 7f;
        public float smokePushStrength = 0.30f;

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
                    s_BodyMaterial = new Material(shader) { name = "FlashBody" };
                    s_BodyMaterial.SetColor("_BaseColor", new Color(0.82f, 0.86f, 0.90f));
                    s_BodyMaterial.SetFloat("_Smoothness", 0.70f);
                    s_BodyMaterial.SetFloat("_Metallic", 0.15f);
                }
                return s_BodyMaterial;
            }
        }

        public static FlashGrenade Throw(Vector3 from, Vector3 to, float flightTime)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "FlashGrenade";
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.15f;

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = BodyMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var col = go.GetComponent<SphereCollider>();
            col.radius = 0.7f;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.38f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = Ballistics.VelocityFor(from, to, flightTime);
            rb.angularVelocity = Random.insideUnitSphere * 18f;

            var grenade = go.AddComponent<FlashGrenade>();
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
            SmokeFx.Flash(p);

            // a flashbang barely disturbs smoke: no crater, just a soft wave
            SmokeVolume.Blast(p, smokePushRadius, smokePushStrength);

            var player = FpsPlayer.Instance;
            if (player != null && player.viewCamera != null)
            {
                Vector3 eyes = player.viewCamera.transform.position;
                Vector3 delta = p - eyes;
                float dist = delta.magnitude;
                float f = Mathf.Clamp01(1f - dist / flashRadius);

                // cover matters: standing behind a wall only catches the spill
                if (dist > 0.05f)
                {
                    Vector3 dir = delta / dist;
                    if (Physics.Raycast(eyes, dir, out RaycastHit hit, dist - 0.25f, ~0,
                                        QueryTriggerInteraction.Ignore)
                        && !hit.collider.transform.IsChildOf(player.transform))
                    {
                        f *= 0.22f;
                    }
                }

                // never a full white-out from across the map: keep a floor so a
                // nearby pop always registers, and let distance decide the rest
                float amount = Mathf.Clamp01(0.30f + f * 0.95f);
                player.AddFlash(amount);
                player.AddShake(0.05f * f, 0.25f);
            }

            Destroy(gameObject);
        }
    }
}
