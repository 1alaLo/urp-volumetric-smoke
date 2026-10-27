using UnityEngine;

namespace SmokeVolumetric
{
    /// <summary>
    /// Capsule-shaped holes punched through every smoke cloud: bullet tunnels and
    /// blast craters. They live in shader globals (a fixed array of 8) and decay
    /// on their own, so the smoke closes back up after being shot.
    ///
    /// Self-bootstrapping: the demo scene needs no explicit setup, and a cloud
    /// spawned from script still reacts.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class SmokeImpulses : MonoBehaviour
    {
        public const int Max = 8;

        struct Hole
        {
            public Vector3 a;
            public Vector3 b;
            public float radius;
            public float born;
            public float life;
        }

        static readonly Hole[] s_Holes = new Hole[Max];
        static readonly Vector4[] s_A = new Vector4[Max];
        static readonly Vector4[] s_B = new Vector4[Max];
        static SmokeImpulses s_Host;

        static readonly int IdA = Shader.PropertyToID("_ImpulseA");
        static readonly int IdB = Shader.PropertyToID("_ImpulseB");
        static readonly int IdCount = Shader.PropertyToID("_ImpulseCount");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            EnsureHost();
            Clear();
        }

        /// <summary>
        /// The host is a hidden, persistent GameObject whose only job is to push
        /// the hole array every frame. It can vanish on a script-domain reload
        /// (recompiling during Play Mode), so every entry point re-creates it
        /// instead of trusting the boot hook.
        /// </summary>
        static SmokeImpulses EnsureHost()
        {
            if (s_Host != null)
                return s_Host;

            s_Host = FindAnyObjectByType<SmokeImpulses>();
            if (s_Host != null)
                return s_Host;

            var go = new GameObject("SmokeImpulses") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            s_Host = go.AddComponent<SmokeImpulses>();
            return s_Host;
        }

        /// <summary>Punch a capsule from <paramref name="a"/> to <paramref name="b"/>.</summary>
        public static void Add(Vector3 a, Vector3 b, float radius, float life)
        {
            EnsureHost();

            // recycle the oldest slot when full
            int slot = 0;
            float oldest = float.MaxValue;
            for (int i = 0; i < Max; i++)
            {
                float remaining = s_Holes[i].life - (Time.time - s_Holes[i].born);
                if (remaining <= 0f)
                {
                    slot = i;
                    break;
                }
                if (s_Holes[i].born < oldest)
                {
                    oldest = s_Holes[i].born;
                    slot = i;
                }
            }

            s_Holes[slot] = new Hole { a = a, b = b, radius = radius, born = Time.time, life = Mathf.Max(0.05f, life) };
            Push();
        }

        /// <summary>A blast clears a sphere: big radius, short life, wide opening.</summary>
        public static void AddExplosion(Vector3 center, float radius)
        {
            Add(center, center, radius, 1.1f);
        }

        public static void Clear()
        {
            for (int i = 0; i < Max; i++)
                s_Holes[i] = default;
            Push();
        }

        void LateUpdate()
        {
            Push();
        }

        static void Push()
        {
            int count = 0;
            float now = Time.time;

            for (int i = 0; i < Max; i++)
            {
                var h = s_Holes[i];
                if (h.life <= 0f)
                    continue;

                float age = now - h.born;
                float t = age / h.life;
                if (t >= 1f)
                {
                    s_Holes[i] = default;
                    continue;
                }

                // hold the hole open for the first third of its life, then heal
                float open = 1f - Mathf.Clamp01((t - 0.33f) / 0.67f);
                s_A[count] = new Vector4(h.a.x, h.a.y, h.a.z, h.radius);
                s_B[count] = new Vector4(h.b.x, h.b.y, h.b.z, open * open * (3f - 2f * open));
                count++;
            }

            Shader.SetGlobalVectorArray(IdA, s_A);
            Shader.SetGlobalVectorArray(IdB, s_B);
            Shader.SetGlobalFloat(IdCount, count);
        }
    }
}
