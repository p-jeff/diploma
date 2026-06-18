using System.Collections;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Draws a single soft ground-glow disc under the currently-selected (hero) plant.
    /// Self-contained: generates its own quad mesh + material (Custom/URP/GroundGlow)
    /// at runtime, like EnvironmentMoment's cylinder. Show()/Hide() fade it in and out.
    /// Auto-created by <see cref="ExperienceManager"/> if not assigned.
    /// </summary>
    public class HeroGlow : MonoBehaviour
    {
        [Tooltip("Seconds to fade the glow in/out.")]
        [SerializeField] private float fadeDuration = 0.4f;
        [Tooltip("Height above the ground point to place the disc (avoids z-fighting).")]
        [SerializeField] private float heightOffset = 0.02f;
        [Tooltip("Edge softness of the glow disc (0 = hard, 1 = very soft).")]
        [SerializeField, Range(0f, 1f)] private float softness = 0.6f;

        private GameObject m_quad;
        private Material m_mat;
        private Coroutine m_fade;
        private float m_alpha;
        private Color m_color = Color.white;

        static readonly int s_colorId = Shader.PropertyToID("_Color");
        static readonly int s_softId  = Shader.PropertyToID("_Softness");

        private void EnsureQuad()
        {
            if (m_quad != null) return;

            m_quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            m_quad.name = "Hero Glow (Generated)";

            var col = m_quad.GetComponent<Collider>();
            if (col != null) Destroy(col);

            m_quad.transform.SetParent(transform, false);
            m_quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // lie flat on the ground

            var shader = Shader.Find("Custom/URP/GroundGlow");
            if (shader == null)
            {
                Debug.LogWarning("[HeroGlow] 'Custom/URP/GroundGlow' shader not found; glow disabled.", this);
                m_mat = null;
            }
            else
            {
                m_mat = new Material(shader);
                var mr = m_quad.GetComponent<MeshRenderer>();
                mr.sharedMaterial = m_mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                m_mat.SetFloat(s_softId, softness);
            }

            m_quad.SetActive(false);
        }

        /// <summary>Position the glow at <paramref name="groundPos"/>, set its colour/size, and fade in.</summary>
        public void Show(Vector3 groundPos, Color color, float radius)
        {
            EnsureQuad();
            if (m_quad == null) return;

            m_color = color;
            m_quad.transform.position = groundPos + Vector3.up * heightOffset;
            m_quad.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            m_quad.SetActive(true);

            if (m_mat != null) m_mat.SetFloat(s_softId, softness);
            FadeTo(1f);
        }

        /// <summary>Fade the glow out and deactivate it.</summary>
        public void Hide() => FadeTo(0f, deactivate: true);

        private void FadeTo(float target, bool deactivate = false)
        {
            if (m_quad == null) return;
            if (m_fade != null) StopCoroutine(m_fade);

            if (!gameObject.activeInHierarchy)
            {
                m_alpha = target;
                ApplyAlpha();
                if (deactivate && Mathf.Approximately(target, 0f)) m_quad.SetActive(false);
                return;
            }
            m_fade = StartCoroutine(FadeRoutine(target, deactivate));
        }

        private IEnumerator FadeRoutine(float target, bool deactivate)
        {
            float start = m_alpha;
            float t = 0f;
            float dur = Mathf.Max(fadeDuration, 0.0001f);
            while (t < dur)
            {
                t += Time.deltaTime;
                m_alpha = Mathf.Lerp(start, target, t / dur);
                ApplyAlpha();
                yield return null;
            }
            m_alpha = target;
            ApplyAlpha();
            if (deactivate && Mathf.Approximately(target, 0f) && m_quad != null) m_quad.SetActive(false);
            m_fade = null;
        }

        private void ApplyAlpha()
        {
            if (m_mat == null) return;
            Color c = m_color;
            c.a = m_alpha;
            m_mat.SetColor(s_colorId, c);
        }

        void OnDisable()
        {
            if (m_fade != null) { StopCoroutine(m_fade); m_fade = null; }
            if (m_quad != null) m_quad.SetActive(false);
        }
    }
}
