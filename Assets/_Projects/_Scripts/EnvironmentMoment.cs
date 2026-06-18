using System.Collections;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Orchestrates a 180° environment moment: fades a procedural cylinder with a painting
    /// into the user's view, holds it while the context audio plays, then fades out.
    /// The cylinder edges fade to transparent so passthrough remains visible.
    /// </summary>
    public class EnvironmentMoment : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The EnvironmentCylinder in the scene. Auto-found if left empty.")]
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

        private Coroutine m_momentRoutine;
        private bool m_interrupted;

        /// <summary>Cylinder is currently showing a moment.</summary>
        public bool IsActive => m_momentRoutine != null;

        void Awake()
        {
            Debug.Log("[EnvironmentMoment] Awake on '" + gameObject.name + "'", this);

            if (cylinder == null)
                cylinder = GetComponentInChildren<EnvironmentCylinder>(true);

            // Auto-create the cylinder if it doesn't exist in the scene.
            if (cylinder == null)
            {
                Debug.Log("[EnvironmentMoment] No cylinder found, creating procedural one.", this);
                var go = new GameObject("Environment Cylinder (Generated)");
                go.transform.SetParent(transform);
                go.SetActive(false);
                cylinder = go.AddComponent<EnvironmentCylinder>();
            }
        }

        void Reset()
        {
            // Auto-find cylinder sibling/child on reset.
            if (cylinder == null)
                cylinder = GetComponentInChildren<EnvironmentCylinder>();
        }

        /// <summary>
        /// Trigger an environment moment. Called by ExperienceManager after a context
        /// instance is grown.
        /// </summary>
        /// <param name="texture">The painting to display.</param>
        /// <param name="worldCenter">World position to centre the cylinder on (user head XZ).</param>
        /// <param name="forwardDir">Direction the cylinder arc faces.</param>
        /// <param name="audioSource">Optional AudioSource playing the context audio (for hold timing).</param>
        public void Trigger(Texture2D texture, Vector3 worldCenter, Vector3 forwardDir, AudioSource audioSource = null)
        {
            Debug.Log("[EnvironmentMoment] Trigger called — texture=" + (texture != null ? texture.name : "null")
                      + " center=" + worldCenter + " forward=" + forwardDir, this);

            if (cylinder == null)
            {
                Debug.LogError("[EnvironmentMoment] cylinder is null, cannot trigger!", this);
                return;
            }
            if (texture == null)
            {
                Debug.LogWarning("[EnvironmentMoment] texture is null, cannot trigger!", this);
                return;
            }

            // Interrupt any running moment.
            if (m_momentRoutine != null)
            {
                Debug.Log("[EnvironmentMoment] Interrupting previous moment.", this);
                StopCoroutine(m_momentRoutine);
                m_momentRoutine = null;
                // Snap back immediately so the new moment starts fresh.
                cylinder.SetAlpha(0f);
            }

            m_momentRoutine = StartCoroutine(MomentRoutine(texture, worldCenter, forwardDir, audioSource));
        }

        /// <summary>Interrupt the current moment and fade back out immediately.</summary>
        public void Interrupt()
        {
            Debug.Log("[EnvironmentMoment] Interrupt() called — stopping moment.", this);
            m_interrupted = true;
        }

        private IEnumerator MomentRoutine(Texture2D texture, Vector3 center, Vector3 forward, AudioSource audioSource)
        {
            Debug.Log("[EnvironmentMoment] MomentRoutine started.", this);
            m_interrupted = false;

            // Position the cylinder and set the texture.
            cylinder.PositionAt(center, forward);
            cylinder.SetTexture(texture);
            cylinder.gameObject.SetActive(true);

            // --- Fade in ---
            float elapsed = 0f;
            float safeFadeIn = Mathf.Max(fadeInDuration, 0.01f);
            while (elapsed < safeFadeIn && !m_interrupted)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / safeFadeIn);
                cylinder.SetAlpha(t);
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

            // Wait while holding, checking for interrupt each frame.
            float holdElapsed = 0f;
            while (holdElapsed < holdSeconds && !m_interrupted)
            {
                holdElapsed += Time.deltaTime;
                yield return null;
            }

            if (m_interrupted)
            {
                Debug.Log("[EnvironmentMoment] Interrupted during hold phase.", this);
            }
            else
            {
                Debug.Log("[EnvironmentMoment] Hold complete. Starting fade-out.", this);
            }

            yield return FadeOutAndFinish();
        }

        private IEnumerator FadeOutAndFinish()
        {
            Debug.Log("[EnvironmentMoment] FadeOutAndFinish starting (interrupted=" + m_interrupted + ").", this);

            float elapsed = 0f;
            float safeFadeOut = Mathf.Max(fadeOutDuration, 0.01f);
            while (elapsed < safeFadeOut)
            {
                // Check interrupt DURING fade-out too
                if (m_interrupted)
                {
                    // Skip to end immediately
                    cylinder.SetAlpha(0f);
                    cylinder.gameObject.SetActive(false);
                    m_momentRoutine = null;
                    Debug.Log("[EnvironmentMoment] Interrupted during fade-out — finished immediately.", this);
                    yield break;
                }

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / safeFadeOut);
                cylinder.SetAlpha(1f - t);
                yield return null;
            }

            Debug.Log("[EnvironmentMoment] Fade-out complete.", this);

            cylinder.SetAlpha(0f);
            cylinder.gameObject.SetActive(false);
            m_momentRoutine = null;
        }

        /// <summary>Set OVRPassthroughLayer opacity if one exists in the scene.</summary>
        private static void SetPassthroughOpacity(float opacity)
        {
            // Find the passthrough layer component via the [BuildingBlock] Passthrough GO.
            // This is a soft lookup — if the component isn't found, the passthrough stays as-is.
            var passthroughGO = GameObject.Find("[BuildingBlock] Passthrough");
            if (passthroughGO == null) return;

            // Use reflection to avoid a hard dependency on the Oculus SDK type.
            var ptLayer = passthroughGO.GetComponent("OVRPassthroughLayer");
            if (ptLayer == null) return;

            var prop = ptLayer.GetType().GetProperty("textureOpacity");
            if (prop != null && prop.CanWrite)
            {
                try
                {
                    prop.SetValue(ptLayer, Mathf.Clamp01(opacity));
                }
                catch { /* ignore */ }
            }
            else
            {
                Debug.LogWarning("[EnvironmentMoment] Could not find textureOpacity property on OVRPassthroughLayer.", ptLayer as Object);
            }
        }

        void OnDisable()
        {
            if (m_momentRoutine != null)
            {
                StopCoroutine(m_momentRoutine);
                m_momentRoutine = null;
            }
            if (cylinder != null)
            {
                cylinder.SetAlpha(0f);
                cylinder.gameObject.SetActive(false);
            }
        }
    }
}
