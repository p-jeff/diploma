using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Lives on the plant's labels object (the "PlantInfos" prefab). Owns the poem and
    /// context <see cref="PlantLabel"/>s, applies a <see cref="PlantData"/>'s sprites to
    /// them, and fades them in/out together. Driven by <see cref="Plant"/>.
    /// </summary>
    public class PlantInfo : MonoBehaviour
    {
        [Header("Labels")]
        [Tooltip("The poem label (text + background sprites).")]
        [SerializeField] private PlantLabel poemLabel;

        [Tooltip("Context labels, placed manually. Filled from PlantData.contextInfos by index — " +
                 "add as many as the plant has context infos.")]
        [SerializeField] private List<PlantLabel> contextLabels = new List<PlantLabel>();

        [Tooltip("The world-space billboard root for each context label (parallel to Context Labels by index). " +
                 "Each is repositioned above its paired instance and snapped to face the user on ShowContext.")]
        [SerializeField] private List<Transform> contextLabelRoots = new List<Transform>();

        [Header("Fade")]
        [SerializeField] private float fadeDuration = 0.4f;

        // The poem and context labels fade independently: the poem appears on Show(),
        // while the contexts stay hidden until the context gesture calls FadeContext(1).
        private Coroutine m_poemFade;
        private Coroutine m_contextFade;
        private float m_poemAlpha;
        private float m_contextAlpha;

        // Per-label alpha tracking for single-label variants (lazy-initialised).
        private float[] m_labelAlphas;
        private Coroutine[] m_labelFades;

        // Lazily attached when the poem uses cylinder placement (see PositionPoemCylinder).
        // It drives the poem's CANVAS (the OVROverlayCanvas host), not the label, so a flat
        // compositor-layer capture still includes the text — see PoemCanvas.
        private CylinderLabelPlacer m_poemCylinder;

        /// <summary>
        /// The poem's Canvas transform — the object that gets positioned in the world. An
        /// OVROverlayCanvas on this Canvas captures it as a flat quad AT this transform, so the
        /// poem label must sit on the canvas's local origin (z=0 plane) and the canvas (not the
        /// label) is what we move. Falls back to the label's parent if no Canvas is found.
        /// </summary>
        private Transform PoemCanvas
        {
            get
            {
                if (poemLabel == null) return null;
                var canvas = poemLabel.GetComponentInParent<Canvas>();
                return canvas != null ? canvas.transform : poemLabel.transform.parent;
            }
        }

        /// <summary>Ensure a label's Canvas renders as a Meta compositor layer for crisp text
        /// (see <see cref="LabelOverlayCanvas"/>). Used for both the poem and the context labels.
        /// Runtime only — never spawns the overlay's hidden helper objects in the edit-mode scene.</summary>
        private static void EnsureLabelOverlay(PlantLabel label)
        {
            if (!Application.isPlaying || label == null) return;
            var canvas = label.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            if (canvas.GetComponent<LabelOverlayCanvas>() == null)
                canvas.gameObject.AddComponent<LabelOverlayCanvas>();
        }

        // Placement mode + radius, mirrored from Plant via PositionPoem / PositionPoemCylinder
        // so the context labels match whatever placement the poem uses.
        private bool m_cylinder;
        private float m_cylinderRadius = 0.5f;

        // How high the plant's collider top sits above an instance's origin. In Above placement
        // context labels are floated this far above each instance (plus contextHeightOffset) so they
        // clear the instance's body, mirroring how the poem clears the plant's top. Set by Plant;
        // ignored in cylinder placement (which keeps its own height). Zero on plants without a collider.
        private float m_contextTopLift;

        // Canopy-fruit context placement: the anchor passed to PlaceContextAt is a fruit orb that
        // is ALREADY up in the canopy, so the label floats just a small clearance above it — the
        // collider top-lift (plant base → top) must NOT be added or the label shoots above the tree.
        // Set by Plant alongside the poem placement; independent of m_cylinder.
        private bool m_fruitContext;

        /// <summary>Number of context labels (= number of context blocks for this plant).</summary>
        public int ContextCount => contextLabels.Count;

        /// <summary>
        /// Move the poem canvas to <paramref name="worldPos"/> and snap its billboard so it
        /// immediately faces the camera. Called by Plant.Show() each time the plant is selected.
        /// </summary>
        public void PositionPoem(Vector3 worldPos)
        {
            m_cylinder = false;

            // Switching back from cylinder placement (e.g. toggled in play mode): stop the orbit
            // and return the poem CANVAS to its prefab-local home under this root, so the Above
            // billboard (this root's LookAtTarget) drives it again. Scale is left alone.
            if (m_poemCylinder != null)
            {
                m_poemCylinder.Deactivate();
                var canvas = PoemCanvas;
                if (canvas != null)
                {
                    canvas.localPosition = Vector3.zero;
                    canvas.localRotation = Quaternion.identity;
                }
            }

            // Keep the label pinned at the canvas origin (z=0 plane) so the OVROverlayCanvas
            // capture stays valid in Above placement too.
            if (poemLabel != null)
            {
                poemLabel.transform.localPosition = Vector3.zero;
                poemLabel.transform.localRotation = Quaternion.identity;
            }

            transform.position = worldPos;
            var look = GetComponent<LookAtTarget>();
            if (look != null) look.Snap();
        }

        /// <summary>
        /// Cylinder placement (the alternative to <see cref="PositionPoem"/>): instead of
        /// floating the poem straight above the plant, orbit the poem label around the plant's
        /// vertical axis at <paramref name="axisPoint"/> (centre XZ at the chosen height) and
        /// <paramref name="radius"/>, keeping it on the side facing the viewer. Only the poem
        /// label is moved (in world space); the PlantInfos root and the context labels are
        /// left untouched.
        /// </summary>
        public void PositionPoemCylinder(Vector3 axisPoint, float radius)
        {
            m_cylinder = true;
            m_cylinderRadius = radius;

            if (poemLabel == null) return;

            // Pin the label to its canvas's local origin (z=0 plane) and orbit the CANVAS itself,
            // not the label. OVROverlayCanvas captures the canvas as a flat quad at the canvas
            // transform, so moving the label out from under the canvas would drop it from the
            // capture (invisible text). Driving the canvas keeps the text on-plane and visible.
            poemLabel.transform.localPosition = Vector3.zero;
            poemLabel.transform.localRotation = Quaternion.identity;

            var canvas = PoemCanvas;
            if (canvas == null) return;

            // (Re)bind the placer to the canvas. Disable any stale placer left on the label by an
            // older build so it can't keep fighting for the label's transform.
            if (m_poemCylinder == null || m_poemCylinder.transform != canvas)
            {
                var stale = poemLabel.GetComponent<CylinderLabelPlacer>();
                if (stale != null) stale.Deactivate();

                m_poemCylinder = canvas.GetComponent<CylinderLabelPlacer>();
                if (m_poemCylinder == null)
                    m_poemCylinder = canvas.gameObject.AddComponent<CylinderLabelPlacer>();
            }
            m_poemCylinder.Activate(axisPoint, radius);
        }

        /// <summary>
        /// Re-aim the cylinder-placed poem at the viewer's current position. The poem is
        /// positioned at Show() but only fades in after the reveal animation, by which time
        /// the viewer may have shifted; calling this at fade-in makes it face them as it
        /// appears, then hold still for reading. No-op in Above placement.
        /// </summary>
        public void ResnapPoemCylinder()
        {
            if (m_cylinder && m_poemCylinder != null) m_poemCylinder.Resnap();
        }

        /// <summary>
        /// Set how high the plant's collider top sits above an instance's origin so Above-placed
        /// context labels float above each instance's body (not its base). Cylinder placement
        /// ignores this. Driven by <see cref="Plant"/> alongside the poem placement.
        /// </summary>
        public void SetContextTopLift(float lift) => m_contextTopLift = Mathf.Max(0f, lift);

        /// <summary>Canopy-fruit mode: place context labels just above their fruit orb (no
        /// collider top-lift). Set by <see cref="Plant"/> when its context placement is CanopyFruit.</summary>
        public void SetFruitContext(bool fruit) => m_fruitContext = fruit;

        /// <summary>Assign the plant's sprites into the labels. Safe to call in edit mode.</summary>
        public void SetData(PlantData data)
        {
            if (data == null) return;
            if (poemLabel != null) poemLabel.SetContent(data.poem);

            // Wire each label's Canvas to render as a crisp compositor layer (play mode only).
            EnsureLabelOverlay(poemLabel);

            int validCount = data.contextInfos.Count;
            for (int i = 0; i < contextLabels.Count; i++)
            {
                if (contextLabels[i] == null) continue;
                if (i < validCount)
                {
                    contextLabels[i].SetContent(data.contextInfos[i]);
                    EnsureLabelOverlay(contextLabels[i]);
                }
            }

            // Deactivate context roots that exceed this species' context count so
            // empty labels never appear at runtime.
            for (int i = 0; i < contextLabelRoots.Count; i++)
            {
                if (contextLabelRoots[i] != null)
                    contextLabelRoots[i].gameObject.SetActive(false);
            }
        }

        /// <summary>Set both groups' alpha immediately (used to reset to hidden).</summary>
        public void SetAlphaImmediate(float a)
        {
            SetPoemAlpha(a);
            SetContextAlpha(a);
        }

        /// <summary>Fade the poem label to the given alpha.</summary>
        public void FadePoem(float target) => Fade(ref m_poemFade, () => m_poemAlpha, SetPoemAlpha, target);

        /// <summary>Fade the context labels to the given alpha.</summary>
        public void FadeContext(float target) => Fade(ref m_contextFade, () => m_contextAlpha, SetContextAlpha, target);

        /// <summary>
        /// Float each context label above its paired instance (root i over anchor i),
        /// snap it to face the user, then fade the context group in. Pairing is by index;
        /// extra roots or anchors are ignored.
        /// </summary>
        public void PlaceContextAt(IReadOnlyList<Transform> anchors, float heightOffset)
        {
            if (anchors == null) return;
            int n = Mathf.Min(contextLabelRoots.Count, anchors.Count);
            for (int i = 0; i < n; i++)
            {
                var root = contextLabelRoots[i];
                var anchor = anchors[i];
                if (root == null || anchor == null) continue;

                PlaceContextRoot(root, anchor, heightOffset);
            }
            FadeContext(1f);
        }

        /// <summary>
        /// Position one context root over its anchor. Cylinder placement (matching the poem)
        /// orbits the root around the anchor's vertical axis at <see cref="m_cylinderRadius"/>,
        /// keeping it on the viewer-facing side; here <paramref name="heightOffset"/> is the
        /// height above the anchor origin (unchanged). Otherwise it floats straight above the
        /// instance's body: <see cref="m_contextTopLift"/> (the plant's collider top above the
        /// instance origin) + <paramref name="heightOffset"/> as clearance, so the label clears
        /// the instance rather than burying in it.
        /// </summary>
        private void PlaceContextRoot(Transform root, Transform anchor, float heightOffset)
        {
            root.gameObject.SetActive(true);
            var placer = root.GetComponent<CylinderLabelPlacer>();
            if (m_fruitContext)
            {
                // The anchor IS a fruit orb hanging in the canopy: float the label a small
                // clearance above it, WITHOUT the collider top-lift (that's the plant base→top
                // height, which would shoot the label clear above the whole tree).
                if (placer != null) placer.Deactivate();
                root.position = anchor.position + Vector3.up * heightOffset;
                var look = root.GetComponent<LookAtTarget>();
                if (look != null) look.Snap();
            }
            else if (m_cylinder)
            {
                if (placer == null) placer = root.gameObject.AddComponent<CylinderLabelPlacer>();
                placer.Activate(anchor.position + Vector3.up * heightOffset, m_cylinderRadius);
            }
            else
            {
                if (placer != null) placer.Deactivate();
                root.position = anchor.position + Vector3.up * (m_contextTopLift + heightOffset);
                var look = root.GetComponent<LookAtTarget>();
                if (look != null) look.Snap();
            }
        }

        /// <summary>Fade the context labels back out and deactivate their roots.</summary>
        public void HideContext()
        {
            FadeContext(0f);
            foreach (var root in contextLabelRoots)
                if (root != null) root.gameObject.SetActive(false);
        }

        // ── Single-label variants (Experience layer) ─────────────────────────────

        /// <summary>
        /// Float a single context label (at <paramref name="index"/>) above
        /// <paramref name="anchor"/>, snap its billboard, and fade it to alpha 1.
        /// Does not affect other labels or the group fade state.
        /// </summary>
        public void PlaceContextAt(int index, Transform anchor, float heightOffset)
        {
            if (index < 0 || index >= contextLabelRoots.Count || index >= contextLabels.Count) return;
            var root = contextLabelRoots[index];
            if (root == null || anchor == null) return;

            PlaceContextRoot(root, anchor, heightOffset);

            FadeContextLabel(index, 1f);
        }

        /// <summary>
        /// Fade a single context label to <paramref name="target"/> alpha using a
        /// per-label coroutine with alpha tracking arrays (lazy-initialised).
        /// </summary>
        public void FadeContextLabel(int index, float target)
        {
            EnsureLabelArrays();
            if (index < 0 || index >= contextLabels.Count) return;

            if (m_labelFades[index] != null) StopCoroutine(m_labelFades[index]);
            if (!gameObject.activeInHierarchy)
            {
                m_labelAlphas[index] = target;
                if (contextLabels[index] != null) contextLabels[index].SetAlpha(target);
                m_labelFades[index] = null;
                return;
            }
            m_labelFades[index] = StartCoroutine(LabelFadeRoutine(index, target));
        }

        private void EnsureLabelArrays()
        {
            int n = contextLabels.Count;
            if (m_labelAlphas == null || m_labelAlphas.Length != n)
            {
                m_labelAlphas = new float[n];
                m_labelFades = new Coroutine[n];
            }
        }

        private IEnumerator LabelFadeRoutine(int index, float target)
        {
            float start = m_labelAlphas[index];
            float t = 0f;
            float dur = Mathf.Max(0.0001f, fadeDuration);
            var label = contextLabels[index];

            while (t < dur)
            {
                t += Time.deltaTime;
                float a = Mathf.Lerp(start, target, t / dur);
                m_labelAlphas[index] = a;
                if (label != null) label.SetAlpha(a);
                yield return null;
            }
            m_labelAlphas[index] = target;
            if (label != null) label.SetAlpha(target);
            m_labelFades[index] = null;
        }

        private void SetPoemAlpha(float a)
        {
            m_poemAlpha = a;
            if (poemLabel != null) poemLabel.SetAlpha(a);
        }

        private void SetContextAlpha(float a)
        {
            m_contextAlpha = a;
            for (int i = 0; i < contextLabels.Count; i++)
                if (contextLabels[i] != null) contextLabels[i].SetAlpha(a);
        }

        private void Fade(ref Coroutine handle, Func<float> get, Action<float> set, float target)
        {
            if (handle != null) StopCoroutine(handle);
            if (!gameObject.activeInHierarchy)
            {
                set(target);
                handle = null;
                return;
            }
            handle = StartCoroutine(FadeRoutine(get, set, target));
        }

        private IEnumerator FadeRoutine(Func<float> get, Action<float> set, float target)
        {
            float start = get();
            float t = 0f;
            float dur = Mathf.Max(0.0001f, fadeDuration);

            while (t < dur)
            {
                t += Time.deltaTime;
                set(Mathf.Lerp(start, target, t / dur));
                yield return null;
            }
            set(target);
        }
    }
}
