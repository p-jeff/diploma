using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// A decoupled, live-tuning harness for the environment cylinder system. Drop it in a scene
    /// (alongside your real plants), tick <see cref="livePreview"/>, and it renders a persistent
    /// preview of the parallax columns around your head so you can dial in radius / width / per-column
    /// HEIGHT / vertical offset live — in play mode (anchors to the headset) or in the editor (centres
    /// on this object). Nothing here writes back into your assets: when it looks right, hit "Copy values
    /// to clipboard" (or the context menu) and paste the numbers into your <see cref="PlantData"/> layers
    /// (and PlantData.environmentVerticalOffset). The preview objects are marked DontSave so they never
    /// pollute the scene file.
    /// </summary>
    [ExecuteAlways]
    public class EnvironmentTuner : MonoBehaviour
    {
        [System.Serializable]
        public class TunerColumn
        {
            [Tooltip("Painting for this column. May be left empty to preview just the silhouette/height.")]
            public Texture2D texture;
            [Tooltip("Cylinder radius in metres — larger = farther away.")]
            public float radius = 3.5f;
            [Tooltip("Painting width in METRES. 0 = wrap a full 180° at this radius.")]
            public float width = 0f;
            [Tooltip("HEIGHT override in metres. 0 = follow the texture's aspect ratio (no distortion); " +
                     ">0 = force this column to this exact height, independent of width.")]
            public float heightOverride = 0f;
            [Tooltip("Per-column vertical nudge in metres, added on top of the global offset below.")]
            public float verticalOffset = 0f;
            [Tooltip("Off = soft side-edge fade into passthrough. On = hard edges (rely on the PNG alpha).")]
            public bool hardEdges = false;
        }

        [Header("Live preview")]
        [Tooltip("Continuously build + update the preview rig so inspector edits show live.")]
        public bool livePreview = true;
        [Tooltip("Viewer to centre the diorama on. Empty = Camera.main in play mode, else this object.")]
        public Transform head;
        [Tooltip("OFF (default): the diorama is ANCHORED where your head was when it was (re)built, so " +
                 "you can look around freely without it moving. Press 'Rebuild preview' (or change any " +
                 "value) to re-anchor to where you're looking now. ON: it re-centres + re-faces your " +
                 "head every frame (it will appear to rotate/follow as you turn).")]
        public bool followHead = false;
        [Tooltip("Preview opacity. Tuning aid only — the real moment fades in/out at runtime.")]
        [Range(0f, 1f)] public float previewAlpha = 1f;

        [Header("Columns (parallax layers)")]
        public List<TunerColumn> columns = new List<TunerColumn>();

        [Header("Global placement")]
        [Tooltip("Raise (+) / lower (−) the whole diorama in metres. Copy this into the plant's " +
                 "PlantData.environmentVerticalOffset (or a context's PlantLabelContent.environmentVerticalOffset).")]
        public float verticalOffset = 0f;
        [Tooltip("Render queue of the farthest column; nearer columns are +1. Keep below the plants' 3000.")]
        public int baseRenderQueue = 2900;

        const string PreviewName = "__EnvTunerPreview";

        private Transform m_container;
        private readonly List<EnvironmentCylinder> m_cols = new List<EnvironmentCylinder>();
        // Rebuild only when the inspector changes (set by OnValidate), not every frame — placement
        // still updates when needed with no per-frame allocation.
        private bool m_dirty = true;

        void OnDisable() => TearDown();
        void OnDestroy() => TearDown();

#if UNITY_EDITOR
        void OnValidate() => m_dirty = true;
#endif

        void Update()
        {
            if (!livePreview)
            {
                if (m_container != null) TearDown();
                return;
            }

            EnsureRig();          // create the rig once / grow the pool — never destroys per change
            if (m_dirty)
            {
                ConfigureRig();   // (re)build meshes + queues only when the inspector changed
                PlaceRig();       // anchor the rig ONCE, at the head pose captured right now
                m_dirty = false;
            }
            else if (followHead)
            {
                PlaceRig();       // opt-in: keep re-centring/re-facing the head every frame
            }
        }

        // --- Rig lifecycle (reuse-based: no per-change destroy → no deferred-Destroy hang) --------

        /// <summary>Make sure the container and one cylinder per column exist. Pool only grows; surplus
        /// columns are deactivated in <see cref="ConfigureRig"/>, never destroyed.</summary>
        private void EnsureRig()
        {
            if (m_container == null)
            {
                // Our refs go null on assembly reload but a DontSave child can linger — clear strays.
                DestroyStrayContainers();
                var go = new GameObject(PreviewName) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                m_container = go.transform;
                m_cols.Clear();
                m_dirty = true;
            }

            while (m_cols.Count < columns.Count)
                m_cols.Add(NewChild("Column " + m_cols.Count).AddComponent<EnvironmentCylinder>());
        }

        /// <summary>Rebuild meshes/materials for the current values. Runs only on edits — never per
        /// frame — so the procedural meshes aren't regenerated every frame.</summary>
        private void ConfigureRig()
        {
            int q = baseRenderQueue > 0 ? baseRenderQueue : 2900;

            for (int i = 0; i < m_cols.Count; i++)
            {
                var cyl = m_cols[i];
                if (cyl == null) continue;
                bool on = i < columns.Count && columns[i] != null;
                cyl.gameObject.SetActive(on);
                if (!on) continue;

                var c = columns[i];
                cyl.Configure(c.texture, c.radius, c.width, c.heightOverride, c.hardEdges, q + i);
            }
        }

        private GameObject NewChild(string n)
        {
            var go = new GameObject(n) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(m_container, false);
            return go;
        }

        private void TearDown()
        {
            m_cols.Clear();
            if (m_container != null) { SafeDestroy(m_container.gameObject); m_container = null; }
            DestroyStrayContainers();
            m_dirty = true;
        }

        /// <summary>Remove leftover preview containers. CRITICAL: only the edit-mode branch may loop,
        /// because <see cref="DestroyImmediate"/> removes the object synchronously. In play mode
        /// <see cref="Destroy"/> is DEFERRED (the object survives the frame), so a Find-loop would spin
        /// forever and hang the editor — there we destroy at most one and rely on the cached reference
        /// the rest of the time.</summary>
        private void DestroyStrayContainers()
        {
            if (Application.isPlaying)
            {
                var s = transform.Find(PreviewName);
                if (s != null) Destroy(s.gameObject);
            }
            else
            {
                int guard = 0;
                var s = transform.Find(PreviewName);
                while (s != null && guard++ < 32)
                {
                    DestroyImmediate(s.gameObject);
                    s = transform.Find(PreviewName);
                }
            }
        }

        private void SafeDestroy(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        // --- Placement (anchor once on build/edit; per-frame only if followHead) ------------------

        private void PlaceRig()
        {
            Transform h = ResolveHead();
            Vector3 center = h != null ? h.position : transform.position;
            Vector3 forward = ProjectForward(h != null ? h.forward : transform.forward);

            for (int i = 0; i < m_cols.Count && i < columns.Count; i++)
            {
                var cyl = m_cols[i];
                if (cyl == null || !cyl.gameObject.activeSelf) continue;
                float vo = columns[i] != null ? columns[i].verticalOffset : 0f;
                cyl.PositionAt(center, forward, verticalOffset + vo);
                cyl.SetAlpha(previewAlpha);
            }
        }

        private Transform ResolveHead()
        {
            if (head != null) return head;
            if (Application.isPlaying && Camera.main != null) return Camera.main.transform;
            return null;
        }

        private static Vector3 ProjectForward(Vector3 f)
        {
            Vector3 p = Vector3.ProjectOnPlane(f, Vector3.up);
            return p.sqrMagnitude > 0.0001f ? p.normalized : Vector3.forward;
        }

        // --- Value export -----------------------------------------------------------------------

        [ContextMenu("Copy values to clipboard")]
        public void CopyValuesToClipboard()
        {
            string report = BuildValuesReport();
#if UNITY_EDITOR
            UnityEditor.EditorGUIUtility.systemCopyBuffer = report;
#endif
            Debug.Log("[EnvironmentTuner] Tuned values copied to clipboard:\n" + report, this);
        }

        [ContextMenu("Log values")]
        public void LogValues() => Debug.Log("[EnvironmentTuner] Tuned values:\n" + BuildValuesReport(), this);

        [ContextMenu("Rebuild preview")]
        public void ForceRebuild()
        {
            TearDown();
            m_dirty = true;
            // Edit mode: rebuild now for instant feedback (DestroyImmediate above was synchronous).
            // Play mode: the destroy above is DEFERRED, so let the next Update recreate — rebuilding
            // here would briefly double the rig.
            if (livePreview && !Application.isPlaying)
            {
                EnsureRig();
                ConfigureRig();
                PlaceRig();
                m_dirty = false;
            }
        }

        /// <summary>Human-readable dump of every tuned value, grouped to mirror where each one is
        /// pasted: per-column into a PlantData EnvironmentLayer, the offset into PlantData
        /// (environmentVerticalOffset).</summary>
        public string BuildValuesReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Environment Tuner values ===");
            sb.AppendLine("# Per column → PlantData.environmentLayers[i]");
            for (int i = 0; i < columns.Count; i++)
            {
                var c = columns[i];
                if (c == null) continue;
                string tex = c.texture != null ? c.texture.name : "(none)";
                sb.AppendLine($"[{i}] texture={tex}  radius={c.radius:0.###}  width={c.width:0.###}  " +
                              $"heightOverride={c.heightOverride:0.###}  verticalOffset={c.verticalOffset:0.###}  " +
                              $"hardEdges={c.hardEdges}");
            }
            sb.AppendLine();
            sb.AppendLine("# Per-plant / globals");
            sb.AppendLine($"environmentVerticalOffset={verticalOffset:0.###}   (→ PlantData.environmentVerticalOffset)");
            sb.AppendLine($"baseRenderQueue={baseRenderQueue}   (→ EnvironmentMoment)");
            return sb.ToString();
        }
    }
}
