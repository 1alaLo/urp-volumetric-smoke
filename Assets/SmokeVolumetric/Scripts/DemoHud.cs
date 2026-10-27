using UnityEngine;

namespace SmokeVolumetric
{
    /// <summary>
    /// IMGUI overlay: crosshair, controls and live smoke stats.
    /// Deliberately dependency-free so the demo has no UI prefab/font setup.
    /// </summary>
    public class DemoHud : MonoBehaviour
    {
        GUIStyle _small;
        GUIStyle _title;
        float _fps = 60f;

        void Update()
        {
            if (Time.unscaledDeltaTime > 0f)
                _fps = Mathf.Lerp(_fps, 1f / Time.unscaledDeltaTime, 0.06f);
        }

        void OnGUI()
        {
            EnsureStyles();

            DrawCrosshair();
            DrawPanel();
            DrawFlash();
        }

        void EnsureStyles()
        {
            if (_small != null)
                return;

            _small = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = true,
                wordWrap = false,
                alignment = TextAnchor.UpperLeft
            };
            _small.normal.textColor = new Color(0.92f, 0.95f, 1f, 0.95f);

            _title = new GUIStyle(_small)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold
            };
            _title.normal.textColor = Color.white;
        }

        void DrawCrosshair()
        {
            var player = FpsPlayer.Instance;

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            float gap = 7f;
            float len = 8f;
            float thick = 2f;

            Color c = new Color(1f, 1f, 1f, 0.82f);
            if (player != null && player.CooldownRemaining > 0f)
                c = new Color(0.65f, 0.85f, 1f, 0.45f);

            var prev = GUI.color;

            // dark outline pass
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            CrosshairQuads(cx, cy, gap, len, thick, 1f);
            GUI.color = c;
            CrosshairQuads(cx, cy, gap, len, thick, 0f);

            // centre dot
            GUI.DrawTexture(new Rect(cx - 1f, cy - 1f, 2f, 2f), Texture2D.whiteTexture);

            GUI.color = prev;
        }

        static void CrosshairQuads(float cx, float cy, float gap, float len, float thick, float expand)
        {
            GUI.DrawTexture(new Rect(cx - thick * 0.5f, cy - gap - len - expand, thick, len), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - thick * 0.5f, cy + gap + expand, thick, len), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - gap - len - expand, cy - thick * 0.5f, len, thick), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + gap + expand, cy - thick * 0.5f, len, thick), Texture2D.whiteTexture);
        }

        void DrawFlash()
        {
            var player = FpsPlayer.Instance;
            if (player == null)
                return;

            float f = player.FlashAmount;
            if (f <= 0.001f)
                return;

            // the peak is instant, then a long tail: pow keeps the first frames
            // fully white while the after-image lingers
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Pow(f, 1.35f) * 1.08f));
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        void DrawPanel()
        {
            const float x = 18f;
            const float y = 18f;
            const float w = 470f;
            const float h = 128f;

            var prevColor = GUI.color;
            GUI.color = new Color(0.02f, 0.03f, 0.05f, 0.55f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = new Color(0.55f, 0.75f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(x, y, 2f, h), Texture2D.whiteTexture);
            GUI.color = prevColor;

            float tx = x + 14f;
            float ty = y + 10f;

            GUI.Label(new Rect(tx, ty, w - 20f, 22f), "VOLUMETRIC SMOKE  ·  URP", _title);
            ty += 24f;

            GUI.Label(new Rect(tx, ty, w - 20f, 20f), "<b>WASD</b> move   <b>Shift</b> sprint   <b>Space</b> jump", _small);
            ty += 18f;
            GUI.Label(new Rect(tx, ty, w - 20f, 20f), "<b>LMB</b> fire   <b>RMB</b> smoke   <b>G</b> flashbang   <b>F</b> frag", _small);
            ty += 18f;
            GUI.Label(new Rect(tx, ty, w - 20f, 20f), "<b>C</b> clear smoke   <b>R</b> respawn   <b>Esc</b> free cursor", _small);
            ty += 22f;

            var player = FpsPlayer.Instance;
            string stats = string.Format(
                "clouds <b>{0}</b>   smoke <b>{1}</b>   frags <b>{2}</b>   flashes <b>{3}</b>   shots <b>{4}</b>   {5:0} fps",
                SmokeVolume.ActiveCount,
                player != null ? player.GrenadesThrown : 0,
                player != null ? player.FragsThrown : 0,
                player != null ? player.FlashesThrown : 0,
                player != null ? player.ShotsFired : 0,
                _fps);

            GUI.Label(new Rect(tx, ty, w - 20f, 20f), stats, _small);
        }
    }
}
