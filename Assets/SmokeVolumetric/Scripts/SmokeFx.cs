using System.Collections;
using UnityEngine;

namespace SmokeVolumetric
{
    /// <summary>
    /// Tiny pooled flash / shake helper for detonations. No particles, no assets:
    /// one point light that fades out, plus a camera kick on the player.
    /// </summary>
    public class SmokeFx : MonoBehaviour
    {
        static SmokeFx s_Instance;
        Light _flash;
        Coroutine _routine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            EnsureHost();
        }

        /// <summary>Same domain-reload safety as <see cref="SmokeImpulses"/>.</summary>
        static SmokeFx EnsureHost()
        {
            if (s_Instance != null)
                return s_Instance;

            s_Instance = FindAnyObjectByType<SmokeFx>();
            if (s_Instance != null)
                return s_Instance;

            var go = new GameObject("SmokeFx") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<SmokeFx>();
            return s_Instance;
        }

        /// <summary>Short grey-ish puff of light when a canister pops.</summary>
        public static void Pop(Vector3 position, float strength = 1f)
        {
            Play(position, new Color(0.86f, 0.90f, 0.95f), 2.2f * strength, 5f, 0.22f);
        }

        /// <summary>Fragment blast: hot flash, wider range, harder shake.</summary>
        public static void Explosion(Vector3 position, float strength = 1f)
        {
            Play(position, new Color(1f, 0.72f, 0.38f), 9f * strength, 11f, 0.32f);

            var player = FpsPlayer.Instance;
            if (player != null)
            {
                float dist = Vector3.Distance(player.transform.position, position);
                float falloff = Mathf.Clamp01(1f - dist / 18f);
                player.AddShake(0.22f * falloff * strength, 0.45f);
            }
        }

        /// <summary>Flashbang: a hard, short white burst rather than a fireball.</summary>
        public static void Flash(Vector3 position)
        {
            Play(position, new Color(1f, 0.98f, 0.94f), 26f, 26f, 0.22f);
        }

        static void Play(Vector3 position, Color color, float intensity, float range, float life)
        {
            var host = EnsureHost();
            if (host == null)
                return;

            host.PlayInternal(position, color, intensity, range, life);
        }

        void PlayInternal(Vector3 position, Color color, float intensity, float range, float life)
        {
            if (_flash == null)
            {
                var go = new GameObject("Flash");
                go.transform.SetParent(transform, false);
                _flash = go.AddComponent<Light>();
                _flash.type = LightType.Point;
                _flash.shadows = LightShadows.None;
            }

            _flash.transform.position = position;
            _flash.color = color;
            _flash.range = range;
            _flash.intensity = intensity;
            _flash.enabled = true;

            if (_routine != null)
                StopCoroutine(_routine);
            _routine = StartCoroutine(Fade(life));
        }

        IEnumerator Fade(float life)
        {
            float t = 0f;
            float start = _flash.intensity;

            while (t < life)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(t / life);
                _flash.intensity = start * k * k;
                yield return null;
            }

            _flash.intensity = 0f;
            _flash.enabled = false;
            _routine = null;
        }
    }
}
