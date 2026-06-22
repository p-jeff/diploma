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
    ///      "Touch Me" sprite prompt. The garden (ExperienceManager + its plants) is held hidden.
    ///   2. Touching the poppy hides the prompt, fades the poppy out, then shows the title card
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
        [Tooltip("\"Touch Me\" prompt (the sprite canvas). Its CanvasGroup alpha is faded in on Start " +
                 "and out when touched.")]
        [SerializeField] private CanvasGroup touchMeGroup;

        [Header("Title card")]
        [SerializeField, Min(0f)] private float titleFadeDuration = 1.2f;
        [Tooltip("Seconds the title card stays fully visible before fading out.")]
        [SerializeField, Min(0f)] private float titleHoldDuration = 2.5f;

        [Header("3D title card (mesh + rose Gaussian)")]
        [Tooltip("Parent of the 3D title (the AOTC mesh + the rose Gaussian). Toggled active around the " +
                 "title beat and re-hidden on Replay. Auto-found by name \"3D_TitleCard\" under this " +
                 "object if left unset.")]
        [SerializeField] private GameObject titleCardRoot;
        [Tooltip("The 3D title mesh (e.g. AOTC_v1). It has no Animator, so it is \"animated in\" by fading " +
                 "its material's _BaseColor alpha 0->1 (the material is converted to transparent) and back " +
                 "out on hide — no scale animation.")]
        [SerializeField] private Transform titleMesh;
        [Tooltip("Seconds for the title mesh to fade in (and to fade out).")]
        [SerializeField, Min(0f)] private float titleMeshFadeDuration = 1.1f;
        [Tooltip("Reveal animator on the rose Gaussian. After the mesh has grown in, this blooms the rose " +
                 "(Play + opacity fade) before the sequence continues to the poem; reversed on hide. " +
                 "Auto-found on the \"Gaussian\" child of the title card if left unset.")]
        [SerializeField] private GsplatRevealAnimator titleRoseReveal;
        [Tooltip("Pause after the mesh settles before the rose Gaussian begins its reveal.")]
        [SerializeField, Min(0f)] private float gapMeshToRose = 0.35f;
        [Tooltip("Optional SFX played when the title card animates in (mesh fade-in + rose bloom) — a 2D " +
                 "one-shot, e.g. a plant-grow sound. Leave unset for a silent title card.")]
        [SerializeField] private AudioSource titleSfx;

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

        // URP/Lit main colour — the title mesh fades by lerping its alpha (material is transparent).
        static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");

        private bool m_started;
        private bool m_done;

        // The title mesh's renderers + base colour, cached in Awake so we can fade its _BaseColor alpha
        // (the mesh's material is transparent) instead of scaling it.
        private MeshRenderer[] m_titleMeshRenderers;
        private Color m_titleMeshBaseColor = Color.white;
        private MaterialPropertyBlock m_titleMeshMpb;

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        void Awake()
        {
            // SPECTATOR (Mac client): the garden is rendered purely from replicated host state, there
            // is no local intro, and there are no hands to advance it. Running the normal gating here
            // would hide the garden (ExperienceManager + plants) and then never reveal it — the touch
            // that drives RevealGarden() never comes — leaving the spectator stuck on the title.
            // Disabling this component via SpectatorModeController.componentsToDisable does NOT help:
            // Awake runs on every component of an active GameObject regardless of the enabled flag. So
            // skip the whole sequence here: hide the title visuals, leave the garden visible, opt out.

            // Resolve the 3D title card (mesh + rose) and cache the mesh renderers + base colour so
            // Show/HideTitleCard can fade its alpha. Done before the spectator early-out so the
            // spectator branch can hide the title card too.
            AutoFindTitleCard();
            CacheTitleMeshRenderers();

            if (Plants.Net.SpectatorState.IsSpectator)
            {
                if (titlePoppy != null) titlePoppy.SetActive(false); // also stops the IdleSplatCycler under it
                if (titleCardRoot != null) titleCardRoot.SetActive(false);
                SetGroupAlpha(touchMeGroup, 0f);
                SetTextAlpha(poemText, 0f);
                enabled = false;
                return;
            }

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
            if (poemText != null) SetTextAlpha(poemText, 0f);

            // Park the 3D title card hidden (mesh faded fully transparent, rose parked at its hidden
            // resting state + opacity floored) until the poppy is touched and ShowTitleCard runs.
            if (titleCardRoot != null && !titleCardRoot.activeSelf) titleCardRoot.SetActive(true);
            SetMeshAlpha(0f);
            if (titleRoseReveal != null)
            {
                titleRoseReveal.ResetToStart();
                ParkRenderersHidden(titleRoseReveal.transform);
            }

            if (titlePoppy != null && !titlePoppy.activeSelf) titlePoppy.SetActive(true);

            if (poppyReveal != null)
                poppyReveal.Play();
            else if (fadePoppyInOnStart && titlePoppy != null)
                StartCoroutine(FadeRenderers(titlePoppy.transform, k_hiddenOpacity, 1f, poppyFadeInDuration));

            // Fade the "Touch Me" prompt in.
            if (touchMeGroup != null)
            {
                SetGroupAlpha(touchMeGroup, 0f);
                SnapBillboard(touchMeGroup);
                StartCoroutine(FadeCanvasGroup(touchMeGroup, 0f, 1f, titleFadeDuration));
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
            // 1. Dismiss the prompt and fade the poppy away.
            if (touchMeGroup != null) StartCoroutine(FadeCanvasGroup(touchMeGroup, touchMeGroup.alpha, 0f, titleFadeDuration));

            if (poppyReveal != null) poppyReveal.PlayReverse();
            if (titlePoppy != null)
                yield return FadeRenderers(titlePoppy.transform, 1f, k_hiddenOpacity, poppyFadeOutDuration);
            if (titlePoppy != null) titlePoppy.SetActive(false);

            if (gapAfterPoppy > 0f) yield return new WaitForSeconds(gapAfterPoppy);

            // 2. 3D title card: grow the mesh in, then bloom the rose Gaussian, hold, then clear it.
            yield return ShowTitleCard();
            yield return HideTitleCard();

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

        // ── 3D title card (mesh + rose Gaussian) ──────────────────────────────────────

        /// <summary>Resolve the title card refs from the conventional child names if they were left
        /// unset, so the prefab works even before the references are wired.</summary>
        private void AutoFindTitleCard()
        {
            Transform card = titleCardRoot != null ? titleCardRoot.transform : transform.Find("3D_TitleCard");
            if (card == null) return;
            if (titleCardRoot == null) titleCardRoot = card.gameObject;
            if (titleMesh == null)
            {
                var m = card.Find("AOTC_v1");
                if (m != null) titleMesh = m;
            }
            if (titleRoseReveal == null)
            {
                var g = card.Find("Gaussian");
                if (g != null) titleRoseReveal = g.GetComponent<GsplatRevealAnimator>();
            }
        }

        /// <summary>Bring the 3D title in: fade the mesh's alpha 0->1, then bloom the rose Gaussian
        /// (assemble + opacity fade-in), and hold.</summary>
        private IEnumerator ShowTitleCard()
        {
            if (titleCardRoot != null && !titleCardRoot.activeSelf) titleCardRoot.SetActive(true);

            // Sound the title card's reveal (a 2D one-shot, e.g. a plant-grow SFX). Lives on a source
            // that stays active for the sequence, so a long clip isn't cut when the card hides.
            if (titleSfx != null && titleSfx.clip != null) titleSfx.Play();

            // Mesh fades in (alpha 0 -> 1) — no scale animation.
            yield return FadeMesh(0f, 1f, titleMeshFadeDuration);

            if (gapMeshToRose > 0f) yield return new WaitForSeconds(gapMeshToRose);

            // ...then reveal the rose Gaussian. Play() drives the geometric assemble while the opacity
            // fade lifts it off the hidden floor (composing cleanly regardless of the animator's startAt).
            // Wait for the bloom to finish so the rose is fully formed before we move on to the poem.
            if (titleRoseReveal != null)
            {
                titleRoseReveal.Play();
                StartCoroutine(FadeRenderers(titleRoseReveal.transform, k_hiddenOpacity, 1f, titleFadeDuration));
                float waited = 0f, timeout = titleRoseReveal.duration + 1f;
                while (!titleRoseReveal.IsDone && waited < timeout) { waited += Time.deltaTime; yield return null; }
            }

            if (titleHoldDuration > 0f) yield return new WaitForSeconds(titleHoldDuration);
        }

        /// <summary>Clear the 3D title before the poem: dissolve the rose and fade the mesh away.</summary>
        private IEnumerator HideTitleCard()
        {
            Coroutine roseFade = null;
            if (titleRoseReveal != null)
            {
                titleRoseReveal.PlayReverse();
                roseFade = StartCoroutine(FadeRenderers(titleRoseReveal.transform, 1f, k_hiddenOpacity, titleFadeDuration));
            }

            yield return FadeMesh(1f, 0f, titleMeshFadeDuration);

            if (roseFade != null) yield return roseFade;

            if (titleCardRoot != null) titleCardRoot.SetActive(false);
        }

        /// <summary>Cache the title mesh's renderers + its material base colour, so the fade can drive
        /// _BaseColor alpha without permanently editing the shared material.</summary>
        private void CacheTitleMeshRenderers()
        {
            if (titleMesh == null) { m_titleMeshRenderers = null; return; }
            m_titleMeshRenderers = titleMesh.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in m_titleMeshRenderers)
            {
                var mat = r != null ? r.sharedMaterial : null;
                if (mat != null && mat.HasProperty(s_baseColorId)) { m_titleMeshBaseColor = mat.GetColor(s_baseColorId); break; }
            }
        }

        /// <summary>Set the title mesh's _BaseColor alpha (its RGB is preserved) via a property block, so
        /// the fade is per-instance and never touches the shared material asset.</summary>
        private void SetMeshAlpha(float a)
        {
            if (m_titleMeshRenderers == null) return;
            m_titleMeshMpb ??= new MaterialPropertyBlock();
            Color c = m_titleMeshBaseColor; c.a = a;
            foreach (var r in m_titleMeshRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(m_titleMeshMpb);
                m_titleMeshMpb.SetColor(s_baseColorId, c);
                r.SetPropertyBlock(m_titleMeshMpb);
            }
        }

        /// <summary>Fade the title mesh's alpha — the "animate in/out" for the 3D title (it has no
        /// Animator; its material is transparent, so alpha IS the reveal). No scale animation.</summary>
        private IEnumerator FadeMesh(float from, float to, float duration)
        {
            if (m_titleMeshRenderers == null || m_titleMeshRenderers.Length == 0) yield break;
            float dur = Mathf.Max(duration, 0.0001f);
            float elapsed = 0f;
            SetMeshAlpha(from);
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                SetMeshAlpha(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / dur)));
                yield return null;
            }
            SetMeshAlpha(to);
        }

        /// <summary>Snap every GsplatRenderer under <paramref name="root"/> to the hidden opacity floor,
        /// so the rose doesn't flash before its reveal.</summary>
        private void ParkRenderersHidden(Transform root)
        {
            if (root == null) return;
            ApplyOpacity(root.GetComponentsInChildren<GsplatRenderer>(true), k_hiddenOpacity);
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

        /// <summary>Lerp a <see cref="CanvasGroup"/>'s alpha — used for the "Touch Me" sprite prompt,
        /// which is a world-space canvas rather than a single TMP label.</summary>
        private IEnumerator FadeCanvasGroup(CanvasGroup group, float from, float to, float duration)
        {
            if (group == null) yield break;
            if (!group.gameObject.activeSelf) group.gameObject.SetActive(true);

            float dur = Mathf.Max(duration, 0.0001f);
            float elapsed = 0f;
            group.alpha = from;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                group.alpha = Mathf.Lerp(from, to, elapsed / dur);
                yield return null;
            }
            group.alpha = to;
        }

        private static void SetGroupAlpha(CanvasGroup group, float a)
        {
            if (group != null) group.alpha = a;
        }

        /// <summary>Re-aim a label's billboard (LookAtTarget on its canvas) at the viewer right before
        /// it appears, so it faces wherever the user is now rather than where they were at scene start.</summary>
        private static void SnapBillboard(Component component)
        {
            if (component == null) return;
            var look = component.GetComponentInParent<LookAtTarget>(true);
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
            if (touchMeGroup != null) SetGroupAlpha(touchMeGroup, 0f);
            if (poemText != null) SetTextAlpha(poemText, 0f);
            if (titlePoppy != null) titlePoppy.SetActive(false);
            if (titleCardRoot != null) titleCardRoot.SetActive(false);
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
