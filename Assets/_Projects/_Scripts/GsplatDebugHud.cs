using Gsplat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Plants
{
    /// <summary>
    /// Lightweight on-screen debug overlay that reports how many Gaussian splats are
    /// currently active across every <see cref="GsplatRenderer"/> in the scene.
    ///
    /// "Active" uses <see cref="GsplatRenderer.SplatCount"/> — the count actually
    /// uploaded to the GPU and rendering this frame — so plants that spawn, despawn or
    /// are still streaming in are reflected live. The nominal asset total is shown
    /// alongside for reference.
    ///
    /// Auto-spawns in the Editor and Development builds (no scene wiring needed) and is
    /// stripped from release builds. Press <see cref="ToggleKey"/> (F3) to hide/show.
    /// Drawn via IMGUI, so it appears on the Game view / desktop mirror, not in the HMD.
    /// </summary>
    public class GsplatDebugHud : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("[GsplatDebugHud]");
            go.AddComponent<GsplatDebugHud>();
            DontDestroyOnLoad(go);
        }
#endif

        [Tooltip("How often (seconds) to re-scan the scene and recount splats.")]
        public float RefreshInterval = 0.5f;

        [Tooltip("Key that toggles the overlay on/off.")]
        public Key ToggleKey = Key.F3;

        [Tooltip("Font size of the overlay text.")]
        public int FontSize = 18;

        ulong _activeSplats;   // uploaded / rendering right now
        ulong _assetSplats;    // nominal total from the assigned assets
        int _activeRenderers;  // renderers with at least one uploaded splat
        int _totalRenderers;   // active-in-hierarchy GsplatRenderers found

        float _nextRefresh;
        bool _visible = true;
        GUIStyle _style;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[ToggleKey].wasPressedThisFrame)
                _visible = !_visible;

            if (Time.unscaledTime < _nextRefresh)
                return;
            _nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, RefreshInterval);
            Recount();
        }

        void Recount()
        {
            // Disabled renderers dispose their GPU resources, so exclude inactive ones.
            var renderers = FindObjectsByType<GsplatRenderer>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            ulong active = 0, total = 0;
            int activeCount = 0;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                uint uploaded = r.SplatCount;
                active += uploaded;
                if (r.GsplatAsset != null)
                    total += r.GsplatAsset.SplatCount;
                if (uploaded > 0)
                    activeCount++;
            }

            _activeSplats = active;
            _assetSplats = total;
            _activeRenderers = activeCount;
            _totalRenderers = renderers.Length;
        }

        void OnGUI()
        {
            if (!_visible)
                return;

            if (_style == null || _style.fontSize != FontSize)
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = FontSize,
                    richText = false,
                    normal = { textColor = Color.white },
                    padding = new RectOffset(10, 10, 8, 8),
                };

            string text =
                $"GSPLAT DEBUG  (toggle: {ToggleKey})\n" +
                $"Active splats : {_activeSplats:N0}\n" +
                $"Asset total   : {_assetSplats:N0}\n" +
                $"Renderers     : {_activeRenderers}/{_totalRenderers} active";

            var content = new GUIContent(text);
            var size = _style.CalcSize(content);
            var rect = new Rect(10, 10, size.x, size.y);

            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(rect, content, _style);
        }
    }
}
