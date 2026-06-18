using System.Collections;
using System.Collections.Generic;
using Gsplat;
using Gsplat.Animation;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Plants
{
    /// <summary>
    /// Self-contained title sequence that plays BEFORE the garden experience.
    ///
    /// Flow (see <see cref="Begin"/>):
    ///   1. A standalone "title" Gaussian splat (the poppy) sits on the floor with a floating
    ///      "Touch Me" label. The garden (ExperienceManager + its plants) is held hidden.
    ///   2. Touching the poppy hides the label, fades the poppy out, then shows the title card
    ///      ("An ode to curiosity"), holds it, and fades it out.
    ///   3. The Wünschelrute poem text fades in while its VO plays; once the VO finishes the poem
    ///      fades out.
    ///   4. The garden is revealed: the ExperienceManager is enabled (its own Start() activates the
    ///      first plants + the real touch prompt) and the garden's Gaussians fade in.
    ///
    /// Nothing here edits the existing experience scripts — it just gates them. Designed to live as
    /// its own prefab under SceneRoot/Content so it can be dropped into any scene.
    ///
    /// Touch is read from a <see cref="PlantTouchTrigger"/> with NO plant assigned: this controller
    /// subscribes <see cref="Begin"/> to its hand-touch event in Awake.
    /// </summary>
    public class TitleSequenceController : MonoBehaviour
    {
        [Header("Title Gaussian (the poppy)")]
        [Tooltip("Root of the standalone title Gaussian splat. Its GsplatRenderers are faded out " +
                 "before the poem. Deactivated once the sequence completes.")]
        [SerializeField] private GameObject titlePoppy;
        [Tooltip("Optional reveal animator on the title poppy. If set, the poppy is revealed on " +
                 "Start() (Play) and reversed when faded out.")]
        [SerializeField] private GsplatRevealAnimator poppyReveal;
        [Tooltip("Seconds to fade the title poppy out before the title card.")]
        [SerializeField, Min(0f)] private float poppyFadeOutDuration = 1.2f;

        [Header("Touch")]
        [Tooltip("PlantTouchTrigger on the poppy's touch zone (no plant assigned). Begin() subscribes " +
                 "to its hand-touch event. Auto-found under the title poppy if left unset.")]
        [SerializeField] private PlantTouchTrigger touchTrigger;
        [Tooltip("\"Touch Me\" label, faded in on Start and out when touched.")]
        [SerializeField] private TMP_Text touchMeLabel;

        [Header("Title card")]
        [Tooltip("Title card text, e.g. \"An ode to curiosity\". Alpha-faded.")]
        [SerializeField] private TMP_Text titleCard;
        [SerializeField, Min(0f)] private float titleFadeDuration = 1.2f;
        [Tooltip("Seconds the title card stays fully visible before fading out.")]
        [SerializeField, Min(0f)] private float titleHoldDuration = 2.5f;

        [Header("Poem")]
        [Tooltip("Wünschelrute poem text. Alpha-faded in alongside the VO.")]
        [SerializeField] private TMP_Text poemText;
        [Tooltip("Wünschelrute VO. The poem holds until this clip finishes.")]
        [SerializeField] private AudioSource poemVo;
        [SerializeField, Min(0f)] private float poemFadeDuration = 1.5f;
        [Tooltip("Extra seconds to linger on the poem after the VO finishes before fading it out.")]
        [SerializeField, Min(0f)] private float poemTailHold = 1.5f;
        [Tooltip("Fallback poem hold (seconds) if no VO clip is assigned.")]
        [SerializeField, Min(0f)] private float poemHoldNoVo = 12f;

        [Header("Garden reveal")]
        [Tooltip("The ExperienceManager GameObject. Held DISABLED through the title (gated in Awake), " +
                 "enabled at the end so its own Start() activates the first plants + the real touch prompt.")]
        [SerializeField] private GameObject experienceManager;
        [Tooltip("GameObjects to hold disabled through the title so only the title poppy is visible — " +
                 "the garden plants. The ExperienceManager re-activates its starting batch itself when " +
                 "enabled, so these are NOT re-enabled here.")]
        [SerializeField] private List<GameObject> hideDuringTitle = new List<GameObject>();
        [Tooltip("After the manager starts, every GsplatRenderer under this root is opacity-faded in " +
                 "(inactive ones included, so the fade is armed before they appear). Set to the parent " +
                 "that holds the garden plants (e.g. Content).")]
        [SerializeField] private Transform gardenFadeRoot;
        [SerializeField, Min(0f)] private float gardenFadeInDuration = 2f;

        [Header("Timing")]
        [Tooltip("Delay after the poppy fades out before the title card appears.")]
        [SerializeField, Min(0f)] private float gapAfterPoppy = 0.4f;
        [Tooltip("Delay between the title card fading out and the poem appearing.")]
        [SerializeField, Min(0f)] private float gapTitleToPoem = 0.6f;
        [Tooltip("Delay after the poem fades out before the garden is revealed.")]
        [SerializeField, Min(0f)] private float gapPoemToGarden = 0.5f;

        [Header("Behaviour")]
        [Tooltip("Reveal the title poppy with a fade-in on Start() instead of it being present instantly.")]
        [SerializeField] private bool fadePoppyInOnStart = true;
        [SerializeField, Min(0f)] private float poppyFadeInDuration = 1.5f;

        [Header("Events")]
        [SerializeField] private UnityEvent onSequenceStarted;
        [SerializeField] private UnityEvent onSequenceComplete;

        // Shader opacity multiplier (1 = opaque). ≤0.0001 is treated as 1.0 by the shader, so the
        // "hidden" floor is 0.002 (see GsplatInstanceFader).
        static readonly int s_opacityMulId = Shader.PropertyToID("_GsplatOpacityMul");
        const float k_hiddenOpacity = 0.002f;

        private bool m_started;
        private bool m_done;

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        void Awake()
        {
            // Gate the garden in Awake — runs before ANY Start(), so the ExperienceManager's own
            // Start() never fires until we re-enable it at the end.
            if (experienceManager != null) experienceManager.SetActive(false);
            foreach (var go in hideDuringTitle)
                if (go != null) go.SetActive(false);

            // Touch wiring: the trigger carries no plant, so its event is ours to consume.
            if (touchTrigger == null && titlePoppy != null)
                touchTrigger = titlePoppy.GetComponentInChildren<PlantTouchTrigger>(true);
            if (touchTrigger != null) touchTrigger.AddTouchListener(Begin);
        }

        void Start()
        {
            ArmTitle();
        }

        /// <summary>
        /// Put the title into its opening state: clear the card/poem text, (re)show the title poppy
        /// with its reveal/fade-in, and fade the "Touch Me" label in. Called on Start() and again by
        /// <see cref="Replay"/> for an in-place restart.
        /// </summary>
        private void ArmTitle()
        {
            if (titleCard != null) SetTextAlpha(titleCard, 0f);
            if (poemText != null) SetTextAlpha(poemText, 0f);

            if (titlePoppy != null && !titlePoppy.activeSelf) titlePoppy.SetActive(true);

            if (poppyReveal != null)
                poppyReveal.Play();
            else if (fadePoppyInOnStart && titlePoppy != null)
                StartCoroutine(FadeRenderers(titlePoppy.transform, k_hiddenOpacity, 1f, poppyFadeInDuration));

            // Fade the "Touch Me" label in.
            if (touchMeLabel != null)
            {
                SetTextAlpha(touchMeLabel, 0f);
                SnapBillboard(touchMeLabel);
                StartCoroutine(FadeText(touchMeLabel, 0f, 1f, titleFadeDuration));
            }
        }

        // ── Public entry point ───────────────────────────────────────────────────────

        /// <summary>Start the title sequence. Safe to call multiple times — only the first runs.</summary>
        public void Begin()
        {
            if (m_started) return;
            m_started = true;
            onSequenceStarted.Invoke();
            StartCoroutine(SequenceRoutine());
        }

        private IEnumerator SequenceRoutine()
        {
            // 1. Dismiss the label and fade the poppy away.
            if (touchMeLabel != null) StartCoroutine(FadeText(touchMeLabel, touchMeLabel.alpha, 0f, titleFadeDuration));

            if (poppyReveal != null) poppyReveal.PlayReverse();
            if (titlePoppy != null)
                yield return FadeRenderers(titlePoppy.transform, 1f, k_hiddenOpacity, poppyFadeOutDuration);
            if (titlePoppy != null) titlePoppy.SetActive(false);

            if (gapAfterPoppy > 0f) yield return new WaitForSeconds(gapAfterPoppy);

            // 2. Title card.
            if (titleCard != null)
            {
                SnapBillboard(titleCard);
                yield return FadeText(titleCard, 0f, 1f, titleFadeDuration);
                if (titleHoldDuration > 0f) yield return new WaitForSeconds(titleHoldDuration);
                yield return FadeText(titleCard, 1f, 0f, titleFadeDuration);
            }

            if (gapTitleToPoem > 0f) yield return new WaitForSeconds(gapTitleToPoem);

            // 3. Poem + VO.
            if (poemText != null)
            {
                SnapBillboard(poemText);
                if (poemVo != null && poemVo.clip != null) poemVo.Play();
                yield return FadeText(poemText, 0f, 1f, poemFadeDuration);

                if (poemVo != null && poemVo.clip != null)
                {
                    yield return new WaitWhile(() => poemVo != null && poemVo.isPlaying);
                    if (poemTailHold > 0f) yield return new WaitForSeconds(poemTailHold);
                }
                else if (poemHoldNoVo > 0f)
                {
                    yield return new WaitForSeconds(poemHoldNoVo);
                }

                yield return FadeText(poemText, 1f, 0f, poemFadeDuration);
            }

            if (gapPoemToGarden > 0f) yield return new WaitForSeconds(gapPoemToGarden);

            // 4. Reveal the garden.
            yield return RevealGarden();

            m_done = true;
            onSequenceComplete.Invoke();
        }

        // ── Garden reveal ────────────────────────────────────────────────────────────

        private IEnumerator RevealGarden()
        {
            // On the FIRST reveal the manager is still disabled (gated in Awake): enabling it runs
            // its Awake + Start, and Start calls BeginGarden() — batch 0 comes up dormant. On a
            // RESTART the manager is already enabled (its Start ran once, and Unity never re-runs
            // Start), so we open the garden explicitly via BeginGarden(). Either way the garden ends
            // up freshly opened to batch 0; we only fade its opacity in on top (inactive renderers
            // included, so the fade is armed no matter when the plants activate — no full-opacity flash).
            bool wasInactive = experienceManager != null && !experienceManager.activeSelf;
            if (experienceManager != null) experienceManager.SetActive(true);

            yield return null; // let the manager's Awake/Start run on first activation

            if (!wasInactive && ExperienceManager.Instance != null)
                ExperienceManager.Instance.BeginGarden();

            if (gardenFadeRoot != null)
                yield return FadeRenderers(gardenFadeRoot, k_hiddenOpacity, 1f, gardenFadeInDuration);
        }

        // ── Restart (replay in place) ──────────────────────────────────────────────

        /// <summary>
        /// Restart the whole experience in place, back to the title sequence. Soft-resets the garden
        /// (un-likes every plant, destroys the spreads, closes the garden via
        /// <see cref="ExperienceManager.ResetAll"/>), re-gates the garden objects, and re-arms the
        /// title from the top. The ExperienceManager GameObject is intentionally left enabled — its
        /// Start() already ran, and <see cref="RevealGarden"/> re-opens the garden with BeginGarden().
        /// Wired to the chair's restart button (see <c>ChairSit.RestartExperience</c>).
        /// </summary>
        [ContextMenu("Restart (replay title)")]
        public void Replay()
        {
            StopAllCoroutines();

            if (ExperienceManager.Instance != null) ExperienceManager.Instance.ResetAll();

            // ResetAll already deactivated the roster plants; re-hide any other objects in the list.
            foreach (var go in hideDuringTitle)
                if (go != null) go.SetActive(false);

            m_started = false;
            m_done = false;

            ArmTitle();
        }

        // ── Fades ──────────────────────────────────────────────────────────────────

        /// <summary>Lerp _GsplatOpacityMul on every GsplatRenderer under <paramref name="root"/>
        /// (inactive included).</summary>
        private IEnumerator FadeRenderers(Transform root, float from, float to, float duration)
        {
            if (root == null) yield break;
            var renderers = root.GetComponentsInChildren<GsplatRenderer>(true);
            if (renderers.Length == 0) yield break;

            float dur = Mathf.Max(duration, 0.0001f);
            float elapsed = 0f;
            ApplyOpacity(renderers, from);
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                ApplyOpacity(renderers, Mathf.Lerp(from, to, elapsed / dur));
                yield return null;
            }
            ApplyOpacity(renderers, to);
        }

        private static void ApplyOpacity(GsplatRenderer[] renderers, float value)
        {
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var pb = r.PropertyBlock;
                if (pb == null) continue;
                pb.SetFloat(s_opacityMulId, value);
            }
        }

        private IEnumerator FadeText(TMP_Text text, float from, float to, float duration)
        {
            if (text == null) yield break;
            if (!text.gameObject.activeSelf) text.gameObject.SetActive(true);

            float dur = Mathf.Max(duration, 0.0001f);
            float elapsed = 0f;
            text.alpha = from;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                text.alpha = Mathf.Lerp(from, to, elapsed / dur);
                yield return null;
            }
            text.alpha = to;
        }

        private static void SetTextAlpha(TMP_Text text, float a)
        {
            if (text != null) text.alpha = a;
        }

        /// <summary>Re-aim a label's billboard (LookAtTarget on its canvas) at the viewer right before
        /// it appears, so it faces wherever the user is now rather than where they were at scene start.</summary>
        private static void SnapBillboard(TMP_Text text)
        {
            if (text == null) return;
            var look = text.GetComponentInParent<LookAtTarget>(true);
            if (look != null) look.Snap();
        }

        // ── Debug ────────────────────────────────────────────────────────────────────

        [ContextMenu("Debug Begin")]
        private void DebugBegin() => Begin();

        [ContextMenu("Debug Skip To Garden")]
        private void DebugSkipToGarden()
        {
            if (m_done) return;
            StopAllCoroutines();
            m_started = true;
            if (touchMeLabel != null) SetTextAlpha(touchMeLabel, 0f);
            if (titleCard != null) SetTextAlpha(titleCard, 0f);
            if (poemText != null) SetTextAlpha(poemText, 0f);
            if (titlePoppy != null) titlePoppy.SetActive(false);
            StartCoroutine(RevealGardenThenComplete());
        }

        private IEnumerator RevealGardenThenComplete()
        {
            yield return RevealGarden();
            m_done = true;
            onSequenceComplete.Invoke();
        }
    }
}
