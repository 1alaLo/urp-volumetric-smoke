using UnityEngine;

namespace SmokeVolumetric
{
    /// <summary>
    /// Demo-wide runtime setup. Keeping the editor ticking in the background is
    /// what lets the smoke simulate while the window is unfocused (and keeps
    /// CLI-driven capture runs from freezing on the first frame).
    /// </summary>
    public static class SmokeDemoBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void OnLoad()
        {
            Application.runInBackground = true;
        }
    }
}
