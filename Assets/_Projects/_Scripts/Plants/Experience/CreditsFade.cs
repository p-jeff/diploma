using System.Collections;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Fades a credits <see cref="CanvasGroup"/> in a short while after the garden flourishes.
    /// Wire <see cref="BeginFade"/> to the finale hook (the manager's <c>onGardenFlourish</c>
    /// UnityEvent): it holds the group hidden, waits <see cref="delay"/> seconds, then lerps the
    /// alpha 0 → 1 over <see cref="fadeDuration"/>. The credits are hidden from the start (Awake
    /// zeroes the alpha) and the object stays active so its coroutine can run; <see cref="ResetCredits"/>
    /// snaps it back to hidden for an in-place restart.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class CreditsFade : MonoBehaviour
    {
        [Tooltip("Credits canvas group to fade. Defaults to the CanvasGroup on this object.")]
        [SerializeField] private CanvasGroup canvasGroup;
        [Tooltip("Seconds to wait after the flourish before the credits begin to fade in.")]
        [SerializeField, Min(0f)] private float delay = 5f;
        [Tooltip("Seconds the credits take to fade from invisible to fully visible.")]
        [SerializeField, Min(0f)] private float fadeDuration = 2f;

        private Coroutine m_routine;

        /// <summary>The scene's credits fader, so the soft-reset can hide it without a hard
        /// reference (mirrors <see cref="GardenAmbience.Instance"/>).</summary>
        public static CreditsFade Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            // Hidden until the finale — the object itself stays active so its coroutine can run.
            if (canvasGroup != null) canvasGroup.alpha = 0f;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Hold the credits hidden for <see cref="delay"/> seconds, then fade them in. Wire
        /// this to the flourish event. Safe to call again — it restarts the fade from hidden.</summary>
        public void BeginFade()
        {
            if (m_routine != null) StopCoroutine(m_routine);
            m_routine = StartCoroutine(FadeRoutine());
        }

        private IEnumerator FadeRoutine()
        {
            if (canvasGroup == null) yield break;
            canvasGroup.alpha = 0f;
            if (delay > 0f) yield return new WaitForSeconds(delay);

            float t = 0f;
            while (fadeDuration > 0f && t < fadeDuration)
            {
                t += Time.deltaTime;
                canvasGroup.alpha = Mathf.Clamp01(t / fadeDuration);
                yield return null;
            }
            canvasGroup.alpha = 1f;
            m_routine = null;
        }

        /// <summary>Snap the credits back to hidden (e.g. on an in-place restart).</summary>
        public void ResetCredits()
        {
            if (m_routine != null) { StopCoroutine(m_routine); m_routine = null; }
            if (canvasGroup != null) canvasGroup.alpha = 0f;
        }
    }
}
