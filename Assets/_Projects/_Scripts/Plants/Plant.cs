using System.Collections;
using System.Collections.Generic;
using Midterms;
using TMPro;
using UnityEngine;

namespace Plants
{
    public class Plant : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private PlantData plantData;

        private PlantData ResolveData() => plantData;

        [Header("Labels")]
        [Tooltip("The PlantLabels child object. Its child named 'Name' becomes the title; all other TMP_Text children become fact slots.")]
        [SerializeField] private Transform labelsParent;

        [Header("Splats")]
        [Tooltip("All GsplatShockwaveAnimator components to fire when PlayAnimation() is called. Assign via editor.")]
        [SerializeField] private List<GsplatShockwaveAnimator> splats = new List<GsplatShockwaveAnimator>();

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;

        [Header("Fade")]
        [SerializeField] private float fadeDuration = 0.4f;

        [Header("Like")]
        [Tooltip("Collider that enables selection of this plant. Disabled when the plant is liked.")]
        [SerializeField] private Collider selectionCollider;

        [Tooltip("Manually placed, disabled instances of this plant. Activated when the plant is liked. Place 2-3 copies around the scene and leave them inactive in the editor.")]
        [SerializeField] private List<GameObject> likedInstances = new List<GameObject>();

        private static Plant s_current;
        private bool m_liked;
        public bool IsLiked => m_liked;

        private TMP_Text m_titleLabel;
        private readonly List<TMP_Text> m_facts = new List<TMP_Text>();
        private Coroutine m_fadeRoutine;
        private Coroutine m_showRoutine;
        private float m_alpha;

        public bool IsSelected => s_current == this;

        void Awake()
        {
            CollectFacts();
            ApplyText();
            SetAlphaImmediate(0f);
        }

        void OnValidate()
        {
            if (!Application.isPlaying)
            {
                CollectFacts();
                ApplyText();
            }
        }

        private void CollectFacts()
        {
            m_titleLabel = null;
            m_facts.Clear();
            if (labelsParent == null) return;

            var found = labelsParent.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i].gameObject.name == "Name")
                    m_titleLabel = found[i];
                else
                    m_facts.Add(found[i]);
            }
        }

        private void ApplyText()
        {
            var data = ResolveData();
            if (data == null) return;

            if (m_titleLabel != null)
                m_titleLabel.text = data.displayName;

            for (int i = 0; i < m_facts.Count; i++)
            {
                m_facts[i].text = (data.facts != null && i < data.facts.Length) ? data.facts[i] : "";
            }
        }

        public void PlayAnimation()
        {
            foreach (var s in splats)
                if (s != null) s.Play();
        }

        public void ResetAnimation()
        {
            foreach (var s in splats)
                if (s != null) s.ApplyInitialGreyscale();
        }

        public void Selected()
        {
            if (m_liked) return;
            if (s_current == this) return;

            var previous = s_current;
            s_current = this;

            if (previous != null)
            {
                if (previous.m_showRoutine != null)
                {
                    previous.StopCoroutine(previous.m_showRoutine);
                    previous.m_showRoutine = null;
                }
                if (previous.audioSource != null) previous.audioSource.Stop();
                previous.FadeTo(0f);
                previous.ResetAnimation();
            }

            if (audioSource != null) audioSource.Play();

            ApplyText();

            if (m_showRoutine != null) StopCoroutine(m_showRoutine);
            m_showRoutine = StartCoroutine(ShowAfterAnimation());
        }

        private IEnumerator ShowAfterAnimation()
        {
            PlayAnimation();

            if (splats.Count > 0)
            {
                bool allDone = false;
                while (!allDone)
                {
                    allDone = true;
                    foreach (var s in splats)
                    {
                        if (s != null && !s.IsDone)
                        {
                            allDone = false;
                            break;
                        }
                    }
                    if (!allDone) yield return null;
                }
            }

            FadeTo(1f);
            m_showRoutine = null;
        }

        public void Deselect()
        {
            if (m_liked) return;
            if (s_current != this) return;
            s_current = null;
            if (m_showRoutine != null)
            {
                StopCoroutine(m_showRoutine);
                m_showRoutine = null;
            }
            FadeTo(0f);
        }

        private void FadeTo(float target)
        {
            if (!gameObject.activeInHierarchy)
            {
                SetAlphaImmediate(target);
                return;
            }
            if (m_fadeRoutine != null) StopCoroutine(m_fadeRoutine);
            m_fadeRoutine = StartCoroutine(FadeRoutine(target));
        }

        private IEnumerator FadeRoutine(float target)
        {
            float start = m_alpha;
            float t = 0f;
            float dur = Mathf.Max(0.0001f, fadeDuration);

            while (t < dur)
            {
                t += Time.deltaTime;
                SetAlphaImmediate(Mathf.Lerp(start, target, t / dur));
                yield return null;
            }
            SetAlphaImmediate(target);
            m_fadeRoutine = null;
        }

        private void SetAlphaImmediate(float a)
        {
            m_alpha = a;
            if (m_titleLabel != null) SetAlpha(m_titleLabel, a);
            for (int i = 0; i < m_facts.Count; i++)
                SetAlpha(m_facts[i], a);
        }

        private static void SetAlpha(TMP_Text label, float a)
        {
            var c = label.color;
            c.a = a;
            label.color = c;
        }

        public void Like()
        {
            if (m_liked) return;
            m_liked = true;

            if (m_showRoutine != null)
            {
                StopCoroutine(m_showRoutine);
                m_showRoutine = null;
            }
            if (m_fadeRoutine != null)
            {
                StopCoroutine(m_fadeRoutine);
                m_fadeRoutine = null;
            }

            if (audioSource != null) audioSource.Stop();

            if (labelsParent != null) labelsParent.gameObject.SetActive(false);
            SetAlphaImmediate(0f);

            if (selectionCollider != null) selectionCollider.enabled = false;

            for (int i = 0; i < likedInstances.Count; i++)
            {
                if (likedInstances[i] != null) likedInstances[i].SetActive(true);
            }

            if (s_current == this) s_current = null;
        }

        /// <summary>Wire this to the "Like" hand-pose UnityEvent. Likes whichever plant is currently selected.</summary>
        public static void LikeCurrent()
        {
            if (s_current != null) s_current.Like();
        }

        [ContextMenu("Test Like")]
        private void TestLike() => Like();

        [ContextMenu("Test Selected")]
        private void TestSelected() => Selected();

        [ContextMenu("Test Deselect")]
        private void TestDeselect() => Deselect();

        [ContextMenu("Test Play Animation")]
        private void TestPlayAnimation() => PlayAnimation();
    }
}
