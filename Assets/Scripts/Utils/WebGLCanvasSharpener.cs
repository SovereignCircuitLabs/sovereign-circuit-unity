#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Why this exists: in the WebGL build the Unity stage is squeezed between the
// HTML left/right panels (~1300px CSS on a 1080p monitor) while the project's
// CanvasScalers reference 1920×1080 and run with `dynamicPixelsPerUnit = 1`.
// Result — every TMP / UGUI glyph is rasterised into the dynamic atlas at the
// already-downscaled size (~0.68×), and the WebGL backbuffer's extra dpr
// pixels cannot recover detail that was never rasterised. Pumping
// dynamicPixelsPerUnit up tells TMP / UGUI to bake glyphs at a higher atlas
// resolution so they stay sharp after scaling. We only do this in WebGL — on
// Windows the Canvas runs at scaleFactor ≈ 1.0 so the default already looks
// crisp and the extra atlas RAM is wasted there.
public static class WebGLCanvasSharpener
{
    private const float TargetDynamicPixelsPerUnit = 4f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.quitting += () => SceneManager.sceneLoaded -= OnSceneLoaded;
        SharpenAll();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SharpenAll();

    private static void SharpenAll()
    {
        var scalers = Object.FindObjectsByType<CanvasScaler>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < scalers.Length; i++)
        {
            var s = scalers[i];
            if (s.dynamicPixelsPerUnit < TargetDynamicPixelsPerUnit)
                s.dynamicPixelsPerUnit = TargetDynamicPixelsPerUnit;
        }
    }
}
#endif
