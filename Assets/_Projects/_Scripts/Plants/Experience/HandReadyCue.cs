using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Scene singleton that shows a "you can keep this now" cue on BOTH hands the moment the
    /// like/keep gesture unlocks (the selected plant's poem has finished). The cue is a soft
    /// green <b>silhouette outline</b> traced around each hand: an inverted-hull pass
    /// (<c>Custom/URP/HandOutline</c>) added as an extra material on the hand's depth-occluder
    /// SkinnedMeshRenderer. The occluder's depth carves the hand-shaped hole out of the shell, so
    /// the only green that survives is the rim just outside the silhouette — it reads as a glow in
    /// passthrough air and never washes the real skin (which is why this replaced the old
    /// additive disc that tinted the passthrough hand green). Driven by
    /// <see cref="ExperienceManager"/>: Show() when keep unlocks, Hide() when it is used or the
    /// selection changes. The affordance lives on the hand because the gesture is a hand action.
    /// </summary>
    public class HandReadyCue : MonoBehaviour
    {
        [Header("Hands")]
        [Tooltip("Hand SkinnedMeshRenderers to outline. If empty, auto-resolved by finding renderers " +
                 "that use the 'Custom/HandDepthOccluder' material.")]
        [SerializeField] private List<SkinnedMeshRenderer> handRenderers = new List<SkinnedMeshRenderer>();
        [Tooltip("Hand anchors used only to parent optional motes. If empty, resolved from HandProximity.Instance.Hands.")]
        [SerializeField] private List<Transform> hands = new List<Transform>();

        [Header("Look")]
        [Tooltip("Keep-cue colour. Green reads as 'go / you may keep this now'.")]
        [SerializeField] private Color color = new Color(0.35f, 1f, 0.45f, 1f);
        [Tooltip("Context-cue colour. Yellow reads as 'you can ask this plant for its context' (post-flourish gaze).")]
        [SerializeField] private Color contextColor = new Color(1f, 0.82f, 0.15f, 1f);
        [Tooltip("Line thickness (m). Keep small for a thin line — independent of the standoff gap.")]
        [SerializeField, Range(0f, 0.03f)] private float outlineWidth = 0.003f;
        [Tooltip("Standoff gap (m): passthrough distance between the real hand edge and the line.")]
        [SerializeField, Range(0f, 0.05f)] private float outlineOffset = 0.006f;
        [Tooltip("Blur — softens the line's outer edge (0 = crisp, 1 = very soft).")]
        [SerializeField, Range(0f, 1f)] private float outlineBlur = 0.2f;
        [Tooltip("Edge smoothing (anti-aliasing) in screen pixels, to take the hardness off corners.")]
        [SerializeField, Range(0f, 4f)] private float outlineSmoothing = 1.5f;
        [Tooltip("When BOTH cues show at once (pre-flourish: like available AND gazing a date fruit), the " +
                 "OUTER ring's standoff gap (m). The inner ring ([Edge Offset, Edge Offset+Width]) carries " +
                 "the like colour; this outer ring carries the context colour. Keep it a few mm clear of " +
                 "the inner band so the two colours read as separate concentric rings (too close and the " +
                 "depth masks cross-cull).")]
        [SerializeField, Range(0f, 0.08f)] private float dualOuterOffset = 0.013f;

        [Header("Animation")]
        [Tooltip("Seconds to fade the outline in/out.")]
        [SerializeField, Min(0f)] private float fadeDuration = 0.35f;
        [Tooltip("Outline alpha at the dim end of the breathing pulse.")]
        [SerializeField, Range(0f, 1f)] private float pulseMinAlpha = 0.45f;
        [Tooltip("Seconds for one full breathing pulse cycle.")]
        [SerializeField, Min(0.01f)] private float pulsePeriod = 1.4f;

        [Header("Motes (optional)")]
        [Tooltip("Authored green-mote ParticleSystem prefab, instantiated once per hand. Leave empty for outline-only.")]
        [SerializeField] private ParticleSystem motesPrefab;

        public static HandReadyCue Instance { get; private set; }

        const string k_outlineShader  = "Custom/URP/HandOutline";
        const string k_maskShader     = "Custom/URP/HandOutlineMask";
        const string k_occluderShader = "Custom/HandDepthOccluder";
        static readonly int s_colorId  = Shader.PropertyToID("_Color");
        static readonly int s_widthId  = Shader.PropertyToID("_Width");
        static readonly int s_offsetId = Shader.PropertyToID("_EdgeOffset");
        static readonly int s_blurId   = Shader.PropertyToID("_Blur");
        static readonly int s_smoothId = Shader.PropertyToID("_Smoothing");

        private class HandFx
        {
            public SkinnedMeshRenderer renderer;
            public Material maskMat;      // inner-ring depth-only standoff wall (queue Transparent-1)
            public Material outlineMat;   // inner-ring line (queue Transparent)
            public Material maskMat2;     // outer-ring standoff wall — only attached in dual mode
            public Material outlineMat2;  // outer-ring line — only attached in dual mode
            public ParticleSystem motes;
            public bool attached;         // inner ring on the renderer
            public bool attached2;        // outer ring on the renderer
        }

        private readonly List<HandFx> m_fx = new List<HandFx>();
        private bool m_built;
        private bool m_shown;
        private float m_alpha;
        private Color m_activeColor;     // inner-ring colour (single mode: keep=green / context=yellow)
        private bool m_dual;             // dual mode: inner ring m_activeColor + outer ring m_outerColor
        private Color m_outerColor;      // outer-ring colour while dual (the context/ask colour)
        private Coroutine m_anim;

        void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning($"[HandReadyCue] Multiple instances; '{name}' overriding existing.", this);
            Instance = this;
            m_activeColor = color;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var fx in m_fx)
            {
                DetachOutline(fx);
                DetachOuter(fx);
                if (fx.outlineMat != null) Destroy(fx.outlineMat);
                if (fx.maskMat != null) Destroy(fx.maskMat);
                if (fx.outlineMat2 != null) Destroy(fx.outlineMat2);
                if (fx.maskMat2 != null) Destroy(fx.maskMat2);
            }
        }

        private IReadOnlyList<Transform> ResolveHands()
        {
            if (hands != null && hands.Count > 0) return hands;
            if (HandProximity.Instance != null && HandProximity.Instance.Hands != null && HandProximity.Instance.Hands.Count > 0)
                return HandProximity.Instance.Hands;
            return null;
        }

        /// <summary>Hand renderers from the serialized list, else any SMR wearing the occluder material.</summary>
        private List<SkinnedMeshRenderer> ResolveRenderers()
        {
            var result = new List<SkinnedMeshRenderer>();
            if (handRenderers != null && handRenderers.Count > 0)
            {
                foreach (var r in handRenderers) if (r != null) result.Add(r);
                if (result.Count > 0) return result;
            }

            var all = FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var r in all)
            {
                if (r == null) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m != null && m.shader != null && m.shader.name == k_occluderShader)
                    {
                        result.Add(r);
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>Build one outline material per hand renderer (+ optional motes), parked detached/hidden.</summary>
        private void Build()
        {
            if (m_built) return;

            var renderers = ResolveRenderers();
            if (renderers.Count == 0) return; // hands not spawned yet; retry next Show()

            var shader = Shader.Find(k_outlineShader);
            if (shader == null)
            {
                Debug.LogWarning($"[HandReadyCue] '{k_outlineShader}' shader not found; outline disabled.", this);
                return;
            }
            var maskShader = Shader.Find(k_maskShader);
            if (maskShader == null)
                Debug.LogWarning($"[HandReadyCue] '{k_maskShader}' shader not found; standoff gap disabled (line will sit on the silhouette).", this);

            var anchors = ResolveHands();

            for (int i = 0; i < renderers.Count; i++)
            {
                var rend = renderers[i];
                var fx = new HandFx { renderer = rend };

                fx.outlineMat = new Material(shader) { name = "Hand Ready Outline (Generated)" };
                Color c = color; c.a = 0f;
                fx.outlineMat.SetColor(s_colorId, c);
                fx.outlineMat.SetFloat(s_widthId, outlineWidth);
                fx.outlineMat.SetFloat(s_offsetId, outlineOffset);
                fx.outlineMat.SetFloat(s_blurId, outlineBlur);
                fx.outlineMat.SetFloat(s_smoothId, outlineSmoothing);

                if (maskShader != null)
                {
                    fx.maskMat = new Material(maskShader) { name = "Hand Ready Outline Mask (Generated)" };
                    fx.maskMat.SetFloat(s_offsetId, outlineOffset);
                }

                // Outer ring (the second concentric line for dual mode), parked detached. Same shaders,
                // larger standoff offset so it sits outside the inner ring.
                fx.outlineMat2 = new Material(shader) { name = "Hand Ready Outline Outer (Generated)" };
                Color c2 = contextColor; c2.a = 0f;
                fx.outlineMat2.SetColor(s_colorId, c2);
                fx.outlineMat2.SetFloat(s_widthId, outlineWidth);
                fx.outlineMat2.SetFloat(s_offsetId, dualOuterOffset);
                fx.outlineMat2.SetFloat(s_blurId, outlineBlur);
                fx.outlineMat2.SetFloat(s_smoothId, outlineSmoothing);

                if (maskShader != null)
                {
                    fx.maskMat2 = new Material(maskShader) { name = "Hand Ready Outline Outer Mask (Generated)" };
                    fx.maskMat2.SetFloat(s_offsetId, dualOuterOffset);
                }

                if (motesPrefab != null)
                {
                    Transform anchor = (anchors != null && i < anchors.Count && anchors[i] != null) ? anchors[i] : rend.transform;
                    var ps = Instantiate(motesPrefab, anchor);
                    ps.transform.localPosition = Vector3.zero;
                    ps.transform.localRotation = Quaternion.identity;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    fx.motes = ps;
                }

                m_fx.Add(fx);
            }

            m_built = m_fx.Count > 0;
        }

        /// <summary>Show the green "you can keep this now" cue on both hands (single inner ring).</summary>
        public void Show() => ShowWith(color, dual: false, Color.clear);

        /// <summary>Show the yellow "you can ask for context" cue on both hands (single inner ring;
        /// post-flourish gaze).</summary>
        public void ShowContext() => ShowWith(contextColor, dual: false, Color.clear);

        /// <summary>Show BOTH cues at once as two concentric rings per hand: the green "keep" line
        /// inside, the yellow "ask" line outside. Used pre-flourish while the like gesture is offered
        /// AND the gaze is on a date fruit, so neither affordance is lost.</summary>
        public void ShowBoth() => ShowWith(color, dual: true, contextColor);

        /// <summary>Show the cue (attach + fade the outline in, start motes). <paramref name="dual"/>
        /// adds the outer ring in <paramref name="outer"/>. If already up, recolour + (de)attach the
        /// outer ring live to match the new mode.</summary>
        private void ShowWith(Color inner, bool dual, Color outer)
        {
            Build();
            if (!m_built) return;
            m_activeColor = inner;
            m_dual = dual;
            m_outerColor = outer;

            if (m_shown)
            {
                SyncOuterAttachment();   // attach/detach the outer ring to match the mode
                ApplyMaterial();         // recolour live
                return;
            }
            m_shown = true;

            foreach (var fx in m_fx)
            {
                AttachOutline(fx);
                if (m_dual) AttachOuter(fx);
                if (fx.motes != null) fx.motes.Play(true);
            }

            if (m_anim != null) StopCoroutine(m_anim);
            m_anim = StartCoroutine(ShowRoutine());
        }

        /// <summary>Attach the outer ring when dual, detach it otherwise (used on a live mode switch
        /// while the cue is already shown).</summary>
        private void SyncOuterAttachment()
        {
            foreach (var fx in m_fx)
            {
                if (m_dual) AttachOuter(fx);
                else DetachOuter(fx);
            }
        }

        /// <summary>Hide the cue (fade the outline out then detach; stop motes so they die out naturally).</summary>
        public void Hide()
        {
            if (!m_shown) return;
            m_shown = false;

            foreach (var fx in m_fx)
                if (fx.motes != null) fx.motes.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            if (m_anim != null) StopCoroutine(m_anim);
            if (gameObject.activeInHierarchy) m_anim = StartCoroutine(HideRoutine());
            else { m_alpha = 0f; ApplyMaterial(); DetachAll(); }
        }

        private IEnumerator ShowRoutine()
        {
            // Fade in.
            float dur = Mathf.Max(fadeDuration, 0.0001f);
            float start = m_alpha;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                m_alpha = Mathf.Lerp(start, 1f, t / dur);
                ApplyMaterial();
                EnsureAttached();
                yield return null;
            }

            // Breathe while shown.
            while (m_shown)
            {
                float phase = Mathf.Sin(Time.time / Mathf.Max(pulsePeriod, 0.0001f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                m_alpha = Mathf.Lerp(pulseMinAlpha, 1f, phase);
                ApplyMaterial();
                EnsureAttached();
                yield return null;
            }
        }

        private IEnumerator HideRoutine()
        {
            float dur = Mathf.Max(fadeDuration, 0.0001f);
            float start = m_alpha;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                m_alpha = Mathf.Lerp(start, 0f, t / dur);
                ApplyMaterial();
                yield return null;
            }
            m_alpha = 0f;
            ApplyMaterial();
            DetachAll();
            m_anim = null;
        }

        /// <summary>Push the current alpha + look params to each outline material (live-tunable in play mode).</summary>
        private void ApplyMaterial()
        {
            foreach (var fx in m_fx)
            {
                if (fx.outlineMat != null)
                {
                    Color c = m_activeColor;
                    c.a = m_alpha;
                    fx.outlineMat.SetColor(s_colorId, c);
                    fx.outlineMat.SetFloat(s_widthId, outlineWidth);
                    fx.outlineMat.SetFloat(s_offsetId, outlineOffset);
                    fx.outlineMat.SetFloat(s_blurId, outlineBlur);
                    fx.outlineMat.SetFloat(s_smoothId, outlineSmoothing);
                    if (fx.maskMat != null) fx.maskMat.SetFloat(s_offsetId, outlineOffset);
                }

                if (fx.outlineMat2 != null)
                {
                    Color c2 = m_outerColor;
                    c2.a = m_dual ? m_alpha : 0f;   // only lit in dual mode (it's also detached then)
                    fx.outlineMat2.SetColor(s_colorId, c2);
                    fx.outlineMat2.SetFloat(s_widthId, outlineWidth);
                    fx.outlineMat2.SetFloat(s_offsetId, dualOuterOffset);
                    fx.outlineMat2.SetFloat(s_blurId, outlineBlur);
                    fx.outlineMat2.SetFloat(s_smoothId, outlineSmoothing);
                    if (fx.maskMat2 != null) fx.maskMat2.SetFloat(s_offsetId, dualOuterOffset);
                }
            }
        }

        // --- Outline attach/detach: add/remove our materials as extra entries on the hand SMR. ---
        // The hand mesh is a single submesh, so each extra material re-renders the whole hand: the
        // mask (depth-only standoff wall) then the green line. Render queue (Transparent-1 < Transparent)
        // guarantees the mask draws first regardless of array order. We only ever add/remove our own
        // materials, never touching the occluder.

        private void AttachOutline(HandFx fx)
        {
            if (fx == null || fx.renderer == null || fx.outlineMat == null || fx.attached) return;
            var mats = new List<Material>(fx.renderer.sharedMaterials);
            if (fx.maskMat != null && !mats.Contains(fx.maskMat)) mats.Add(fx.maskMat);
            if (!mats.Contains(fx.outlineMat)) mats.Add(fx.outlineMat);
            fx.renderer.sharedMaterials = mats.ToArray();
            fx.attached = true;
        }

        private void DetachOutline(HandFx fx)
        {
            if (fx == null) return;
            fx.attached = false;
            if (fx.renderer == null) return;
            var mats = new List<Material>(fx.renderer.sharedMaterials);
            bool changed = false;
            if (fx.outlineMat != null) changed |= mats.Remove(fx.outlineMat);
            if (fx.maskMat != null) changed |= mats.Remove(fx.maskMat);
            if (changed) fx.renderer.sharedMaterials = mats.ToArray();
        }

        /// <summary>Attach the OUTER ring (dual mode) — its own mask + line, at the larger offset.</summary>
        private void AttachOuter(HandFx fx)
        {
            if (fx == null || fx.renderer == null || fx.outlineMat2 == null || fx.attached2) return;
            var mats = new List<Material>(fx.renderer.sharedMaterials);
            if (fx.maskMat2 != null && !mats.Contains(fx.maskMat2)) mats.Add(fx.maskMat2);
            if (!mats.Contains(fx.outlineMat2)) mats.Add(fx.outlineMat2);
            fx.renderer.sharedMaterials = mats.ToArray();
            fx.attached2 = true;
        }

        private void DetachOuter(HandFx fx)
        {
            if (fx == null) return;
            fx.attached2 = false;
            if (fx.renderer == null) return;
            var mats = new List<Material>(fx.renderer.sharedMaterials);
            bool changed = false;
            if (fx.outlineMat2 != null) changed |= mats.Remove(fx.outlineMat2);
            if (fx.maskMat2 != null) changed |= mats.Remove(fx.maskMat2);
            if (changed) fx.renderer.sharedMaterials = mats.ToArray();
        }

        private void DetachAll()
        {
            foreach (var fx in m_fx) { DetachOutline(fx); DetachOuter(fx); }
        }

        /// <summary>Re-add our materials if something (e.g. OVRHand's system-gesture swap) reset the array.</summary>
        private void EnsureAttached()
        {
            foreach (var fx in m_fx)
            {
                if (fx.renderer == null) continue;
                var mats = fx.renderer.sharedMaterials;

                if (fx.outlineMat != null)
                {
                    bool outlineMissing = System.Array.IndexOf(mats, fx.outlineMat) < 0;
                    bool maskMissing = fx.maskMat != null && System.Array.IndexOf(mats, fx.maskMat) < 0;
                    if (outlineMissing || maskMissing) { fx.attached = false; AttachOutline(fx); }
                }

                if (m_dual && fx.outlineMat2 != null)
                {
                    bool outline2Missing = System.Array.IndexOf(mats, fx.outlineMat2) < 0;
                    bool mask2Missing = fx.maskMat2 != null && System.Array.IndexOf(mats, fx.maskMat2) < 0;
                    if (outline2Missing || mask2Missing) { fx.attached2 = false; AttachOuter(fx); }
                }
            }
        }
    }
}
