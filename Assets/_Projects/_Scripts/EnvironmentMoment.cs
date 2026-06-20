using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Orchestrates a 180° environment moment: fades a parallax stack of procedural cylinders
    /// (one per <see cref="EnvironmentLayer"/>) into the user's view, holds it while the context
    /// audio plays, then fades out. Each layer sits at its own radius so head movement reveals
    /// depth; every cylinder's edges fade to transparent so passthrough stays visible at the sides.
    /// </summary>
    public class EnvironmentMoment : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Primary EnvironmentCylinder (layer 0). Auto-found/created if left empty. " +
                 "Extra layers are pooled as additional children at runtime.")]
        [SerializeField] private EnvironmentCylinder cylinder;

        [Header("Timing")]
        [Tooltip("Seconds to fade the painting in.")]
        [SerializeField] private float fadeInDuration = 2f;
        [Tooltip("Seconds to fade the painting out.")]
        [SerializeField] private float fadeOutDuration = 2f;
        [Tooltip("Minimum hold time in seconds (used if no audio clip is playing).")]
        [SerializeField] private float defaultHoldSeconds = 12f;
        [Tooltip("Extra seconds added after the audio clip ends before fading out.")]
        [SerializeField] private float audioTailSeconds = 1f;

        [Tooltip("Radius used when a moment is triggered with a single texture (legacy path).")]
        [SerializeField] private float defaultRadius = 3.5f;

        [Header("Placement")]
        [Tooltip("Raise (+) or lower (−) the whole diorama in metres. Layers normally sit with their " +
                 "bottom edge on the floor; nudge this to line the visible art up with the ground when " +
                 "the texture has empty space at its bottom. Applies to every layer at once.")]
        [SerializeField] private float verticalOffset = 0f;

        [Header("Rendering")]
        [Tooltip("Render queue of the farthest layer; each nearer layer is +1. Keep this BELOW the " +
                 "gsplat plants' Transparent queue (3000) so the diorama sits behind the plants and " +
                 "labels instead of drawing on top of everything. (≤0 falls back to 2900)")]
        [SerializeField] private int baseRenderQueue = 2900;

        // Pool of cylinders, index 0 == `cylinder`. Grown on demand to match the layer count.
        private readonly List<EnvironmentCylinder> m_pool = new List<EnvironmentCylinder>();
        // Scratch buffer reused each Trigger to avoid per-call list allocation churn.
        private readonly List<EnvironmentLayer> m_sorted = new List<EnvironmentLayer>();

        private Coroutine m_momentRoutine;
        private bool m_interrupted;
        private int m_activeCount;

        /// <summary>Cylinder is currently showing a moment.</summary>
        public bool IsActive => m_momentRoutine != null;

        void Awake()
        {
            Debug.Log("[EnvironmentMoment] Awake on '" + gameObject.name + "'", this);

            if (cylinder == null)
                cylinder = GetComponentInChildren<EnvironmentCylinder>(true);

            // Auto-create the primary cylinder if it doesn't exist in the scene.
            if (cylinder == null)
            {
                Debug.Log("[EnvironmentMoment] No cylinder found, creating procedural one.", this);
                cylinder = CreateLayerCylinder("Environment Cylinder (Generated)");
            }

            if (cylinder != null && !m_pool.Contains(cylinder))
                m_pool.Add(cylinder);
        }

        void Reset()
        {
            // Auto-find cylinder sibling/child on reset.
            if (cylinder == null)
                cylinder = GetComponentInChildren<EnvironmentCylinder>();
        }

        /// <summary>
        /// Trigger a single-painting environment moment (legacy path). Wraps the texture in a
        /// one-layer diorama at <see cref="defaultRadius"/>.
        /// </summary>
        public void Trigger(Texture2D texture, Vector3 worldCenter, Vector3 forwardDir, AudioSource audioSource = null)
        {
            if (texture == null)
            {
                Debug.LogWarning("[EnvironmentMoment] texture is null, cannot trigger!", this);
                return;
            }

            m_sorted.Clear();
            // width = 0 → the cylinder wraps a full 180° at defaultRadius (legacy look).
            m_sorted.Add(new EnvironmentLayer { texture = texture, radius = defaultRadius });
            TriggerLayers(m_sorted, worldCenter, forwardDir, audioSource);
        }

        /// <summary>
        /// Trigger a parallax environment moment from a list of layers. Layers with a null texture
        /// are skipped; the rest are sorted far→near (largest radius first) so transparent blending
        /// is deterministic regardless of author order.
        /// </summary>
        public void Trigger(IReadOnlyList<EnvironmentLayer> layers, Vector3 worldCenter, Vector3 forwardDir, AudioSource audioSource = null)
        {
            if (layers == null || layers.Count == 0)
            {
                Debug.LogWarning("[EnvironmentMoment] layer list is empty, cannot trigger!", this);
                return;
            }
            TriggerLayers(layers, worldCenter, forwardDir, audioSource);
        }

        private void TriggerLayers(IReadOnlyList<EnvironmentLayer> layers, Vector3 worldCenter, Vector3 forwardDir, AudioSource audioSource)
        {
            // Collect valid layers into the scratch buffer and sort far→near.
            // (If the caller passed m_sorted itself, the contents are already what we want.)
            if (!ReferenceEquals(layers, m_sorted))
            {
                m_sorted.Clear();
                for (int i = 0; i < layers.Count; i++)
                    if (layers[i] != null && layers[i].texture != null)
                        m_sorted.Add(layers[i]);
            }

            if (m_sorted.Count == 0)
            {
                Debug.LogWarning("[EnvironmentMoment] no layers with a texture, cannot trigger!", this);
                return;
            }

            // Farthest (largest radius) first → drawn first → behind nearer layers.
            m_sorted.Sort((a, b) => b.radius.CompareTo(a.radius));

            Debug.Log("[EnvironmentMoment] Trigger — " + m_sorted.Count + " layer(s), center="
                      + worldCenter + " forward=" + forwardDir, this);

            // Interrupt any running moment and snap everything hidden so the new one starts fresh.
            if (m_momentRoutine != null)
            {
                Debug.Log("[EnvironmentMoment] Interrupting previous moment.", this);
                StopCoroutine(m_momentRoutine);
                m_momentRoutine = null;
                HideAll();
            }

            m_momentRoutine = StartCoroutine(MomentRoutine(worldCenter, forwardDir, audioSource));
        }

        /// <summary>Interrupt the current moment and fade back out immediately.</summary>
        public void Interrupt()
        {
            Debug.Log("[EnvironmentMoment] Interrupt() called — stopping moment.", this);
            m_interrupted = true;
        }

        private IEnumerator MomentRoutine(Vector3 center, Vector3 forward, AudioSource audioSource)
        {
            Debug.Log("[EnvironmentMoment] MomentRoutine started (" + m_sorted.Count + " layers).", this);
            m_interrupted = false;

            // Configure and position one cylinder per layer; deactivate any leftover pool members.
            EnsurePool(m_sorted.Count);
            for (int i = 0; i < m_sorted.Count; i++)
            {
                var cyl = m_pool[i];
                cyl.gameObject.SetActive(true);
                int q = (baseRenderQueue > 0 ? baseRenderQueue : 2900) + i;
                cyl.Configure(m_sorted[i].texture, m_sorted[i].radius, m_sorted[i].width, m_sorted[i].hardEdges, q);
                cyl.PositionAt(center, forward, verticalOffset + m_sorted[i].verticalOffset);
                cyl.SetAlpha(0f);
            }
            for (int i = m_sorted.Count; i < m_pool.Count; i++)
            {
                m_pool[i].SetAlpha(0f);
                m_pool[i].gameObject.SetActive(false);
            }
            m_activeCount = m_sorted.Count;

            // --- Fade in ---
            float elapsed = 0f;
            float safeFadeIn = Mathf.Max(fadeInDuration, 0.01f);
            while (elapsed < safeFadeIn && !m_interrupted)
            {
                elapsed += Time.deltaTime;
                SetAllAlpha(Mathf.Clamp01(elapsed / safeFadeIn));
                yield return null;
            }

            if (m_interrupted)
            {
                Debug.Log("[EnvironmentMoment] Interrupted during fade-in.", this);
                yield return FadeOutAndFinish();
                yield break;
            }

            Debug.Log("[EnvironmentMoment] Fade-in complete. Entering hold phase.", this);

            // --- Hold ---
            float holdSeconds = defaultHoldSeconds;
            if (audioSource != null && audioSource.isPlaying)
            {
                float remaining = audioSource.clip != null
                    ? audioSource.clip.length - audioSource.time
                    : 0f;
                if (remaining > 0f)
                    holdSeconds = remaining + audioTailSeconds;
                Debug.Log("[EnvironmentMoment] Hold will last " + holdSeconds + "s (audio-based).", this);
            }
            else
            {
                Debug.Log("[EnvironmentMoment] Hold will last " + holdSeconds + "s (default).", this);
            }

            float holdElapsed = 0f;
            while (holdElapsed < holdSeconds && !m_interrupted)
            {
                holdElapsed += Time.deltaTime;
                yield return null;
            }

            if (m_interrupted)
                Debug.Log("[EnvironmentMoment] Interrupted during hold phase.", this);
            else
                Debug.Log("[EnvironmentMoment] Hold complete. Starting fade-out.", this);

            yield return FadeOutAndFinish();
        }

        private IEnumerator FadeOutAndFinish()
        {
            Debug.Log("[EnvironmentMoment] FadeOutAndFinish starting (interrupted=" + m_interrupted + ").", this);

            float elapsed = 0f;
            float safeFadeOut = Mathf.Max(fadeOutDuration, 0.01f);
            while (elapsed < safeFadeOut)
            {
                // Interrupt during fade-out → snap to hidden immediately.
                if (m_interrupted)
                {
                    HideAll();
                    m_momentRoutine = null;
                    Debug.Log("[EnvironmentMoment] Interrupted during fade-out — finished immediately.", this);
                    yield break;
                }

                elapsed += Time.deltaTime;
                SetAllAlpha(1f - Mathf.Clamp01(elapsed / safeFadeOut));
                yield return null;
            }

            Debug.Log("[EnvironmentMoment] Fade-out complete.", this);
            HideAll();
            m_momentRoutine = null;
        }

        /// <summary>Set the alpha on every active layer this moment is using.</summary>
        private void SetAllAlpha(float alpha)
        {
            for (int i = 0; i < m_activeCount && i < m_pool.Count; i++)
                m_pool[i].SetAlpha(alpha);
        }

        /// <summary>Zero the alpha and deactivate every pooled cylinder.</summary>
        private void HideAll()
        {
            for (int i = 0; i < m_pool.Count; i++)
            {
                if (m_pool[i] == null) continue;
                m_pool[i].SetAlpha(0f);
                m_pool[i].gameObject.SetActive(false);
            }
            m_activeCount = 0;
        }

        /// <summary>Grow the cylinder pool so it has at least <paramref name="count"/> members.</summary>
        private void EnsurePool(int count)
        {
            while (m_pool.Count < count)
                m_pool.Add(CreateLayerCylinder("Environment Layer " + m_pool.Count));
        }

        private EnvironmentCylinder CreateLayerCylinder(string goName)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            return go.AddComponent<EnvironmentCylinder>();
        }

        void OnDisable()
        {
            if (m_momentRoutine != null)
            {
                StopCoroutine(m_momentRoutine);
                m_momentRoutine = null;
            }
            HideAll();
        }
    }
}
