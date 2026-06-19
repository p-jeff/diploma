using System.Collections;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// A cheap glowing "fruit" orb that hangs in a tree's canopy and carries ONE context.
    /// It replaces a full splat clone as the per-context grow/gaze target for high-splat plants
    /// (trees), so a tree shows <b>1 splat body + N tiny orbs</b> instead of N splat copies.
    ///
    /// Self-building, like <see cref="Plants.Experience.HeroGlow"/>: it generates a small additive
    /// sphere + a <c>Custom/URP/FruitOrb</c> material at runtime, plus a CHILD collider GameObject
    /// (trigger <see cref="SphereCollider"/> + a gaze-only <see cref="PlantTouchTrigger"/>). The
    /// child collider is what makes the existing gaze targeter resolve a ray hit back to this orb:
    /// <c>GazeInstanceTargeter</c> finds the owning <see cref="Plant"/> via the trigger and, with no
    /// <c>GsplatRenderer</c> in the parents, returns the collider's parent — i.e. this orb root,
    /// which is the object registered in the plant's spawned-instance list.
    ///
    /// Dormant (dim) until <see cref="Ripen"/> brightens it (grown, liked, or bloomed).
    /// </summary>
    [DisallowMultipleComponent]
    public class ContextFruit : MonoBehaviour
    {
        // Built-in unit sphere mesh, grabbed once from a throwaway primitive and shared by all orbs.
        private static Mesh s_sphereMesh;

        private const float k_hoverBoost = 1.8f;

        private Material m_material;
        private float m_dormantIntensity = 0.22f;
        private float m_ripeIntensity = 1.4f;
        private float m_intensity;
        private Coroutine m_fade;
        private bool m_ripe;
        private bool m_hover;

        private static readonly int s_ColorId = Shader.PropertyToID("_Color");
        private static readonly int s_IntensityId = Shader.PropertyToID("_Intensity");

        public bool IsRipe => m_ripe;

        /// <summary>Build the orb visual + its gaze collider, wired to <paramref name="owner"/>.</summary>
        public void Init(Plant owner, float visualRadius, float colliderRadius, Color color,
                         float dormantIntensity, float ripeIntensity)
        {
            m_dormantIntensity = dormantIntensity;
            m_ripeIntensity = ripeIntensity;
            BuildVisual(visualRadius, color);
            BuildCollider(owner, colliderRadius);
            SetIntensity(m_dormantIntensity);
        }

        private void BuildVisual(float radius, Color color)
        {
            if (s_sphereMesh == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s_sphereMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                if (Application.isPlaying) Destroy(tmp); else DestroyImmediate(tmp);
            }

            var mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = s_sphereMesh;

            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            var shader = Shader.Find("Custom/URP/FruitOrb");
            if (shader == null)
            {
                Debug.LogWarning("[ContextFruit] Custom/URP/FruitOrb shader not found; orb will be invisible.", this);
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            m_material = new Material(shader) { name = "FruitOrb_Mat" };
            m_material.SetColor(s_ColorId, color);
            mr.sharedMaterial = m_material;

            // Built-in sphere primitive has diameter 1, so scale the root to 2*radius.
            transform.localScale = Vector3.one * (radius * 2f);
        }

        private void BuildCollider(Plant owner, float radius)
        {
            // The collider lives on a CHILD (not this root) on purpose: the gaze targeter's
            // "no GsplatRenderer" fallback returns the hit collider's PARENT, which must be this
            // orb root (the object stored in the plant's spawned-instance list) for the index
            // lookup in Plant.Replay to map back to the right context.
            var colGo = new GameObject("Collider");
            colGo.transform.SetParent(transform, false);

            // Counter the orb root's uniform scale so the trigger radius is in world metres.
            float s = transform.localScale.x;
            if (s > 1e-4f) colGo.transform.localScale = Vector3.one / s;

            var col = colGo.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = radius;

            var trigger = colGo.AddComponent<PlantTouchTrigger>();
            trigger.SetPlant(owner);
            trigger.SetGazeOnly(true); // gaze ray can resolve it; a hand touch never routes Select()
        }

        private void SetIntensity(float v)
        {
            m_intensity = v;
            if (m_material != null) m_material.SetFloat(s_IntensityId, v);
        }

        /// <summary>Brighten from dormant to ripe with a small overshoot pulse. Idempotent.</summary>
        public void Ripen()
        {
            if (m_ripe) return;
            m_ripe = true;
            if (!isActiveAndEnabled) { SetIntensity(m_ripeIntensity); return; }
            if (m_fade != null) StopCoroutine(m_fade);
            m_fade = StartCoroutine(RipenRoutine());
        }

        /// <summary>Set ripe/dormant immediately with no pulse — used by the spectator client to
        /// reconcile to the host's replicated state. Safe before/after <see cref="Init"/>.</summary>
        public void SetRipeImmediate(bool ripe)
        {
            m_ripe = ripe;
            if (m_fade != null) { StopCoroutine(m_fade); m_fade = null; }
            SetIntensity(ripe ? m_ripeIntensity : m_dormantIntensity);
        }

        private IEnumerator RipenRoutine()
        {
            float start = m_intensity;
            float peak = m_ripeIntensity * 1.5f;
            const float dur = 0.5f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                float v = k < 0.5f
                    ? Mathf.Lerp(start, peak, k * 2f)
                    : Mathf.Lerp(peak, m_ripeIntensity, (k - 0.5f) * 2f);
                SetIntensity(v);
                yield return null;
            }
            SetIntensity(m_hover ? m_ripeIntensity * k_hoverBoost : m_ripeIntensity);
            m_fade = null;
        }

        /// <summary>Brighten while the post-flourish gaze is on this orb (the orb-mode equivalent of
        /// the splat-instance Brightness boost). Boosts relative to the current dormant/ripe base.</summary>
        public void SetHover(bool on)
        {
            if (m_hover == on) return;
            m_hover = on;
            if (m_fade != null) return;   // mid-ripen pulse will settle respecting m_hover
            float baseI = m_ripe ? m_ripeIntensity : m_dormantIntensity;
            SetIntensity(on ? baseI * k_hoverBoost : baseI);
        }

        private void OnDestroy()
        {
            if (m_material != null) Destroy(m_material);
        }
    }
}
