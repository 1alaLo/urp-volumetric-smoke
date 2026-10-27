using UnityEngine;

namespace SmokeVolumetric
{
    /// <summary>
    /// Hitscan rifle. Bullets do not stop in smoke, but the column of air they
    /// punch through stays open for about a second, so firing into a cloud leaves
    /// a visible tunnel that heals over. Rendered as a one-segment tracer.
    /// </summary>
    public class Weapon : MonoBehaviour
    {
        public Camera viewCamera;
        public float roundsPerSecond = 8f;
        public float range = 140f;
        public float holeRadius = 0.45f;
        public float holeLife = 1.1f;
        public float recoil = 0.045f;

        public int ShotsFired { get; private set; }

        LineRenderer _tracer;
        float _tracerTimer;
        float _nextShot;

        static Material s_TracerMat;

        static readonly int IdColor = Shader.PropertyToID("_Color");

        void Awake()
        {
            if (viewCamera == null)
                viewCamera = GetComponentInChildren<Camera>();

            var go = new GameObject("Tracer");
            go.transform.SetParent(transform, false);
            _tracer = go.AddComponent<LineRenderer>();
            _tracer.useWorldSpace = true;
            _tracer.positionCount = 2;
            _tracer.widthMultiplier = 0.022f;
            _tracer.numCapVertices = 2;
            _tracer.alignment = LineAlignment.View;
            _tracer.textureMode = LineTextureMode.Stretch;
            _tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tracer.receiveShadows = false;
            _tracer.sharedMaterial = TracerMaterial;
            _tracer.enabled = false;
        }

        static Material TracerMaterial
        {
            get
            {
                if (s_TracerMat == null)
                {
                    var shader = Shader.Find("Sprites/Default");
                    s_TracerMat = new Material(shader) { name = "SmokeTracer" };
                    s_TracerMat.SetColor(IdColor, new Color(1f, 0.92f, 0.62f, 1f));
                }
                return s_TracerMat;
            }
        }

        void Update()
        {
            if (_tracer == null || _tracerTimer <= 0f)
                return;

            _tracerTimer -= Time.deltaTime;
            float k = Mathf.Clamp01(_tracerTimer / 0.07f);
            var c = TracerMaterial.color;
            c.a = k;
            _tracer.startColor = c;
            c.a = k * 0.15f;
            _tracer.endColor = c;

            if (_tracerTimer <= 0f)
                _tracer.enabled = false;
        }

        public bool TryFire()
        {
            if (viewCamera == null || Time.time < _nextShot)
                return false;

            _nextShot = Time.time + 1f / Mathf.Max(0.5f, roundsPerSecond);
            ShotsFired++;

            Camera cam = viewCamera;
            Vector3 origin = cam.transform.position;
            Vector3 dir = cam.transform.forward;

            float wallDist = range;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, range, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!IsSelf(hit.collider))
                    wallDist = hit.distance;
            }

            PunchSmoke(origin, dir, wallDist);
            ShowTracer(origin + dir * 0.6f, origin + dir * wallDist);

            var player = FpsPlayer.Instance;
            if (player != null)
                player.AddShake(recoil, 0.06f);

            return true;
        }

        void PunchSmoke(Vector3 origin, Vector3 dir, float wallDist)
        {
            var clouds = SmokeVolume.All;
            for (int i = 0; i < clouds.Count; i++)
            {
                var vol = clouds[i];
                if (vol == null || !vol.RaySphere(origin, dir, out float enter, out float exit))
                    continue;

                float start = Mathf.Max(enter, 0f);
                float end = Mathf.Min(exit, wallDist);
                if (end - start < 0.25f)
                    continue;

                // per-shot jitter: two rounds in the same spot should not punch
                // an identical hole
                float r = holeRadius * Random.Range(0.82f, 1.18f);
                float life = holeLife * Random.Range(0.85f, 1.15f);
                SmokeImpulses.Add(origin + dir * start, origin + dir * end, r, life);
            }
        }

        void ShowTracer(Vector3 from, Vector3 to)
        {
            _tracer.SetPosition(0, from);
            _tracer.SetPosition(1, to);
            _tracer.startColor = Color.white;
            _tracer.endColor = new Color(1f, 0.9f, 0.6f, 0.15f);
            _tracer.enabled = true;
            _tracerTimer = 0.07f;
        }

        bool IsSelf(Collider c)
        {
            return c.transform == transform || c.transform.IsChildOf(transform);
        }
    }
}
