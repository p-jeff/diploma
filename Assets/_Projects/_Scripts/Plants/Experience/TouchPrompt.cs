using System.Collections;
using TMPro;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// A floating 3D text prompt that fades in above an anchor transform and fades out
    /// when dismissed. Billboard rotation is handled externally by a LookAtTarget component.
    /// </summary>
    public class TouchPrompt : MonoBehaviour
    {
        [SerializeField] private TMP_Text text;
        [SerializeField] private Vector3 offset = new Vector3(0f, 0.8f, 0f);
        [SerializeField] private float fadeDuration = 0.4f;

        private Coroutine m_fadeRoutine;

        /// <summary>Position above <paramref name="anchor"/> and fade alpha in.</summary>
        public void Show(Transform anchor)
        {
            if (anchor != null)
                transform.position = anchor.position + offset;

            gameObject.SetActive(true);
            FadeTo(1f);
        }

        /// <summary>Fade alpha out, then deactivate.</summary>
        public void Hide()
        {
            if (!gameObject.activeSelf) return;
            FadeTo(0f, deactivateAfter: true);
        }

        private void FadeTo(float target, bool deactivateAfter = false)
        {
            if (m_fadeRoutine != null) StopCoroutine(m_fadeRoutine);
            if (!gameObject.activeInHierarchy)
            {
                SetAlpha(target);
                if (deactivateAfter) gameObject.SetActive(false);
                m_fadeRoutine = null;
                return;
            }
            m_fadeRoutine = StartCoroutine(FadeRoutine(target, deactivateAfter));
        }

        private IEnumerator FadeRoutine(float target, bool deactivateAfter)
        {
            float start = GetAlpha();
            float elapsed = 0f;
            float dur = Mathf.Max(fadeDuration, 0.0001f);

            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                SetAlpha(Mathf.Lerp(start, target, elapsed / dur));
                yield return null;
            }
            SetAlpha(target);

            if (deactivateAfter && Mathf.Approximately(target, 0f))
                gameObject.SetActive(false);

            m_fadeRoutine = null;
        }

        private float GetAlpha()
        {
            if (text == null) return 0f;
            return text.alpha;
        }

        private void SetAlpha(float a)
        {
            if (text == null) return;
            text.alpha = a;
        }
    }
}
