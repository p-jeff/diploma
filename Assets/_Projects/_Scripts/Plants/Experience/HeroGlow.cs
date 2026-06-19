using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Marks the currently-selected (hero) / touchable plant with a soft column of small
    /// particles that drift upward from a ring on the ground around it. Replaces the old
    /// flat ground-glow disc (Custom/URP/GroundGlow), which read as a single over-bright
    /// blue blob.
    ///
    /// Self-contained: builds its own <see cref="ParticleSystem"/> + additive material at
    /// runtime (no prefab wiring), mirroring the particle idiom in HandPoseAnimation.
    /// <see cref="Show"/>/<see cref="Hide"/> start/stop emission; in-flight particles live
    /// out their lifetime so the cue fades naturally rather than popping off.
    /// Auto-created by <see cref="Plant"/> if not assigned.
    /// </summary>
    public class HeroGlow : MonoBehaviour
    {
        [Header("Emitter Ring")]
        [Tooltip("Height above the ground point to start particles (avoids sinking into the floor).")]
        [SerializeField] private float heightOffset = 0.02f;
        [Tooltip("0 = emit on an exact ring at the footprint radius; 1 = fill the whole disc.")]
        [SerializeField, Range(0f, 1f)] private float ringThickness = 0.4f;

        [Header("Particles")]
        [Tooltip("Particles emitted per second while shown. Keep low for a calm, sparse drift.")]
        [SerializeField, Min(0f)] private float emissionRate = 14f;
        [Tooltip("Upward drift speed range (m/s).")]
        [SerializeField] private Vector2 riseSpeed = new Vector2(0.18f, 0.36f);
        [Tooltip("Particle world size range (m). Small for a fine, sparkly feel.")]
        [SerializeField] private Vector2 particleSize = new Vector2(0.012f, 0.03f);
        [Tooltip("Particle lifetime range (s) — with rise speed, sets how high they float.")]
        [SerializeField] private Vector2 lifetime = new Vector2(1.6f, 2.8f);
        [Tooltip("Brightness multiplier on the supplied colour. Lower if the cue is too strong.")]
        [SerializeField, Range(0f, 1f)] private float intensity = 0.6f;
        [Tooltip("Strength of the floaty horizontal wobble (Perlin noise).")]
        [SerializeField, Min(0f)] private float wobble = 0.05f;

        private ParticleSystem m_ps;
        private ParticleSystemRenderer m_renderer;
        private Material m_mat;
        private Color m_color = new Color(0.45f, 0.85f, 1f, 1f);

        // One shared soft-dot sprite for every instance (cheap, generated once).
        private static Texture2D s_dotTex;

        private void EnsureSystem()
        {
            if (m_ps != null) return;

            var go = new GameObject("Hero Glow Particles (Generated)");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localScale = Vector3.one;

            m_ps = go.AddComponent<ParticleSystem>();
            m_renderer = go.GetComponent<ParticleSystemRenderer>();

            ApplySettings();

            m_ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            go.SetActive(false);
        }

        private void ApplySettings()
        {
            if (m_ps == null) return;

            // ── Main ──────────────────────────────────────────────────────────────
            var main = m_ps.main;
            main.loop            = true;
            main.playOnAwake     = false;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed      = 0f; // upward motion comes from velocity-over-lifetime, not the shape
            main.startSize       = new ParticleSystem.MinMaxCurve(particleSize.x, particleSize.y);
            main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles    = 120;

            // ── Emission — steady gentle stream while shown ──────────────────────────
            var emission = m_ps.emission;
            emission.enabled      = true;
            emission.rateOverTime = emissionRate;

            // ── Shape — a flat ring on the ground (laid flat by the per-Show rotation) ─
            var shape = m_ps.shape;
            shape.enabled         = true;
            shape.shapeType       = ParticleSystemShapeType.Circle;
            shape.radius          = 0.6f; // overwritten per-Show with the plant footprint radius
            shape.radiusThickness = Mathf.Clamp01(ringThickness);
            shape.arc             = 360f;
            shape.alignToDirection = false;

            // ── Velocity — float straight up in world space, with a tiny wobble ──────
            // NB: x/y/z must all share the same MinMaxCurve mode (TwoConstants here) or
            // Unity throws "Particle Velocity curves must all be in the same mode" and
            // silently drops the up-velocity, leaving particles to wander on noise alone.
            var vel = m_ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space   = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(riseSpeed.x, riseSpeed.y);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            // ── Noise — organic drift so they don't rise in dead-straight lines ──────
            var noise = m_ps.noise;
            noise.enabled       = wobble > 0f;
            noise.strength      = wobble;
            noise.frequency     = 0.4f;
            noise.scrollSpeed   = 0.2f;
            noise.quality       = ParticleSystemNoiseQuality.Low;

            // ── Colour over lifetime — fade in then out (hue comes from startColor) ──
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f,   0f),
                    new GradientAlphaKey(1f,   0.25f),
                    new GradientAlphaKey(0.8f, 0.7f),
                    new GradientAlphaKey(0f,   1f),
                });
            var col = m_ps.colorOverLifetime;
            col.enabled = true;
            col.color   = new ParticleSystem.MinMaxGradient(grad);

            // ── Size over lifetime — twinkle up then shrink away ─────────────────────
            var sizeOL = m_ps.sizeOverLifetime;
            sizeOL.enabled = true;
            sizeOL.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f),
                new Keyframe(0.25f, 1f),
                new Keyframe(0.7f, 0.9f),
                new Keyframe(1f, 0f)));

            // ── Renderer — soft additive billboards ──────────────────────────────────
            if (m_renderer != null)
            {
                m_renderer.renderMode         = ParticleSystemRenderMode.Billboard;
                m_renderer.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
                m_renderer.receiveShadows     = false;
                if (m_mat == null) m_mat = BuildMaterial();
                m_renderer.sharedMaterial     = m_mat;
            }
        }

        /// <summary>Position the ring at <paramref name="groundPos"/>, size it to
        /// <paramref name="radius"/>, tint it, and start the upward drift.</summary>
        public void Show(Vector3 groundPos, Color color, float radius)
        {
            EnsureSystem();
            if (m_ps == null) return;

            m_color = color;
            var go = m_ps.gameObject;
            go.transform.position = groundPos + Vector3.up * heightOffset;
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // circle lies flat on the ground

            var shape = m_ps.shape;
            shape.radius = Mathf.Max(radius, 0.001f);

            // Additive: brightness scales with the colour's rgb; dim it via intensity.
            var main = m_ps.main;
            Color c = color * intensity;
            c.a = 1f;
            main.startColor = c;

            go.SetActive(true);
            if (!m_ps.isPlaying) m_ps.Play();
            var emission = m_ps.emission;
            emission.enabled = true;
        }

        /// <summary>Stop emitting; in-flight particles finish rising and fade out.</summary>
        public void Hide()
        {
            if (m_ps == null) return;
            m_ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void OnDisable()
        {
            if (m_ps != null) m_ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void OnDestroy()
        {
            if (m_mat != null) Destroy(m_mat);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        private Material BuildMaterial()
        {
            // Mirror HandPoseAnimation's additive setup for cross-pipeline consistency.
            var shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var mat = new Material(shader);
            mat.mainTexture = DotTexture();
            mat.SetFloat("_Mode", 4f); // Additive in Standard Unlit
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            return mat;
        }

        /// <summary>A small radial-falloff sprite so particles read as soft round dots,
        /// not hard squares. Generated once and shared by every HeroGlow.</summary>
        private static Texture2D DotTexture()
        {
            if (s_dotTex != null) return s_dotTex;

            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x - c) / c;
                float dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                a *= a; // soft edge
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            s_dotTex = tex;
            return tex;
        }
    }
}
