using System.Collections;
using UnityEngine;

/// <summary>
/// Plays a particle-burst + pop effect on a hand-pose cue whenever a hand pose is recognised.
///
/// The cue is one of (preferred first):
///  • a world-space Canvas cue (the <c>Sprite.prefab</c>: UI Image + label + Bobber), driven through
///    its <see cref="CanvasGroup"/>. Auto-resolved from the first CanvasGroup under this object if
///    <see cref="targetCanvas"/> is left empty, so dropping the cue prefab under the pose object is enough.
///  • a legacy world-space <see cref="SpriteRenderer"/> (<see cref="targetSprite"/>), used only when no
///    Canvas cue is set or found.
///
/// The cue starts hidden and pops in (scale + fade) when the pose is recognised, holds, then pops out.
///
/// Setup:
///  1. Add this component to the cue's root (or any parent of the Canvas cue).
///  2. Drop the Sprite.prefab cue under this object (auto-found), or assign Target Canvas / Target Sprite.
///  3. Wire OnPoseSelected() → SelectorUnityEventWrapper._whenSelected
///     Wire Cancel()         → SelectorUnityEventWrapper._whenUnselected  (optional, pops out early)
/// </summary>
public class HandPoseAnimation : MonoBehaviour
{
    // ── References ─────────────────────────────────────────────────────────────

    [Header("Target")]
    [Tooltip("World-space Canvas cue (the Sprite.prefab root, via its CanvasGroup). Preferred over " +
             "Target Sprite. If left empty, the first CanvasGroup found under this object is used.")]
    [SerializeField] private CanvasGroup targetCanvas;

    [Tooltip("Legacy: the SpriteRenderer whose world-space bounds the particles burst around. " +
             "Used only when no Target Canvas is set or found.")]
    [SerializeField] private SpriteRenderer targetSprite;

    [Header("Particle System")]
    [Tooltip("Leave empty to auto-create a child particle system at runtime.")]
    [SerializeField] private ParticleSystem burstParticles;

    // ── Burst settings ─────────────────────────────────────────────────────────

    [Header("Burst Settings")]
    [Tooltip("Number of particles emitted per burst.")]
    [SerializeField, Range(4, 128)] private int burstCount = 24;

    [Tooltip("Spread radius around the sprite's centre (world units). 0 = tight centre.")]
    [SerializeField, Min(0f)] private float burstRadius = 0.08f;

    [Tooltip("Minimum / maximum particle lifetime in seconds.")]
    [SerializeField] private Vector2 lifetimeRange = new Vector2(0.35f, 0.65f);

    [Tooltip("Minimum / maximum launch speed.")]
    [SerializeField] private Vector2 speedRange = new Vector2(0.4f, 1.2f);

    [Tooltip("Minimum / maximum particle start size.")]
    [SerializeField] private Vector2 sizeRange = new Vector2(0.012f, 0.03f);

    [Tooltip("Gravity multiplier applied to bursted particles. Negative = float upward.")]
    [SerializeField] private float gravityModifier = -0.2f;

    // ── Colour ─────────────────────────────────────────────────────────────────

    [Header("Colour")]
    [SerializeField] private Gradient colorOverLifetime = DefaultGradient();

    // ── Cooldown ───────────────────────────────────────────────────────────────

    [Header("Cooldown")]
    [Tooltip("Seconds to wait before another burst can fire. Prevents spam.")]
    [SerializeField, Min(0f)] private float cooldownSeconds = 0.5f;

    // ── Pop animation ──────────────────────────────────────────────────────────

    [Header("Pop Animation")]
    [Tooltip("Seconds to scale from 0 → full size on select.")]
    [SerializeField, Min(0f)] private float popInDuration = 0.2f;

    [Tooltip("Seconds the sprite stays fully visible before popping out.")]
    [SerializeField, Min(0f)] private float holdDuration = 1f;

    [Tooltip("Seconds to scale from full size → 0 after the hold.")]
    [SerializeField, Min(0f)] private float popOutDuration = 0.2f;

    [Tooltip("Scale curve for pop-in. Y=0→1 over normalised time.")]
    [SerializeField] private AnimationCurve popInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Scale curve for pop-out. Y=1→0 over normalised time.")]
    [SerializeField] private AnimationCurve popOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    // ── State ──────────────────────────────────────────────────────────────────

    private bool m_ready = true;
    private ParticleSystem m_ps;
    private Vector3 m_cueOriginalScale = Vector3.one;
    private Coroutine m_popRoutine;

    // ── Unity lifecycle ────────────────────────────────────────────────────────

    void Awake()
    {
        m_ps = burstParticles != null ? burstParticles : BuildParticleSystem();
        ApplySettings();

        // Prefer the new Canvas cue; fall back to the first CanvasGroup parked under this object
        // (the Sprite.prefab instance), so no per-instance reference wiring is required.
        if (targetCanvas == null) targetCanvas = GetComponentInChildren<CanvasGroup>(true);

        var cue = CueTransform;
        if (cue != null)
        {
            m_cueOriginalScale = cue.localScale;
            SetCueVisible(false);   // hidden until the pose is recognised
        }
    }

    // ── Cue abstraction (Canvas cue preferred, legacy SpriteRenderer fallback) ──

    private Transform CueTransform =>
        targetCanvas != null ? targetCanvas.transform :
        (targetSprite != null ? targetSprite.transform : null);

    private bool HasCue => CueTransform != null;

    /// <summary>Show/hide the cue: CanvasGroup alpha for the Canvas cue, renderer enable for the sprite.</summary>
    private void SetCueVisible(bool on)
    {
        if (targetCanvas != null) targetCanvas.alpha = on ? 1f : 0f;
        else if (targetSprite != null) targetSprite.enabled = on;
    }

    // ── Public API (wire these to UnityEvents) ─────────────────────────────────

    /// <summary>Call this from SelectorUnityEventWrapper._whenSelected.</summary>
    public void OnPoseSelected()
    {
        if (!m_ready) return;
        if (!gameObject.activeInHierarchy) return;

        PositionAtCue();
        m_ps.Emit(burstCount);

        if (cooldownSeconds > 0f)
            StartCoroutine(Cooldown());

        StartPop();
    }

    /// <summary>Cancels the current pop sequence and pops out immediately. Wire to _whenUnselected if needed.</summary>
    public void Cancel()
    {
        if (!HasCue) return;
        if (m_popRoutine != null)
        {
            StopCoroutine(m_popRoutine);
            m_popRoutine = StartCoroutine(PopOutRoutine());
        }
    }

    // ── Pop helpers ────────────────────────────────────────────────────────────

    private void StartPop()
    {
        if (!HasCue) return;
        if (m_popRoutine != null) StopCoroutine(m_popRoutine);
        m_popRoutine = StartCoroutine(PopSequence());
    }

    private IEnumerator PopSequence()
    {
        var cue = CueTransform;
        if (cue == null) yield break;

        cue.localScale = Vector3.zero;
        SetCueVisible(true);

        yield return ScaleTo(popInCurve, popInDuration);
        yield return new WaitForSeconds(holdDuration);
        yield return ScaleTo(popOutCurve, popOutDuration);

        SetCueVisible(false);
        m_popRoutine = null;
    }

    private IEnumerator PopOutRoutine()
    {
        yield return ScaleTo(popOutCurve, popOutDuration);
        SetCueVisible(false);
        m_popRoutine = null;
    }

    private IEnumerator ScaleTo(AnimationCurve curve, float dur)
    {
        var cue = CueTransform;
        if (cue == null) yield break;

        dur = Mathf.Max(0.0001f, dur);
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            cue.localScale = m_cueOriginalScale * curve.Evaluate(Mathf.Clamp01(t / dur));
            yield return null;
        }
        cue.localScale = m_cueOriginalScale * curve.Evaluate(1f);
    }

    // ── Internals ──────────────────────────────────────────────────────────────

    private void PositionAtCue()
    {
        var cue = CueTransform;
        if (cue == null) return;

        // Move the particle system to the cue's world centre so the burst appears centred on it.
        // The legacy sprite gives a tight visual centre via its bounds; the Canvas cue uses its transform.
        m_ps.transform.position = (targetCanvas == null && targetSprite != null)
            ? targetSprite.bounds.center
            : cue.position;
    }

    private IEnumerator Cooldown()
    {
        m_ready = false;
        yield return new WaitForSeconds(cooldownSeconds);
        m_ready = true;
    }

    // ── Particle system construction ────────────────────────────────────────────

    private ParticleSystem BuildParticleSystem()
    {
        var go = new GameObject("HandPose_BurstFX");
        go.transform.SetParent(transform, worldPositionStays: false);

        var ps = go.AddComponent<ParticleSystem>();

        // Stop it immediately; we only emit via Emit().
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        return ps;
    }

    /// <summary>
    /// Push all inspector-controlled values into the particle system modules.
    /// Called once in Awake — safe to call again after inspector changes via
    /// the context menu.
    /// </summary>
    private void ApplySettings()
    {
        if (m_ps == null) return;

        // ── Main module ───────────────────────────────────────────────────────
        var main = m_ps.main;
        main.loop              = false;
        main.playOnAwake       = false;
        main.startLifetime     = new ParticleSystem.MinMaxCurve(lifetimeRange.x, lifetimeRange.y);
        main.startSpeed        = new ParticleSystem.MinMaxCurve(speedRange.x, speedRange.y);
        main.startSize         = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
        main.gravityModifier   = gravityModifier;
        main.simulationSpace   = ParticleSystemSimulationSpace.World;
        main.maxParticles      = Mathf.Max(burstCount * 4, 256);

        // ── Emission — burst-only, no continuous rate ─────────────────────────
        var emission = m_ps.emission;
        emission.enabled  = true;
        emission.rateOverTime = 0f;

        // ── Shape — sphere so particles scatter in all directions ─────────────
        var shape = m_ps.shape;
        shape.enabled       = true;
        shape.shapeType     = ParticleSystemShapeType.Sphere;
        shape.radius        = Mathf.Max(burstRadius, 0.001f);
        shape.radiusThickness = 1f; // emit throughout volume, not just surface

        // ── Colour over lifetime ──────────────────────────────────────────────
        var col = m_ps.colorOverLifetime;
        col.enabled  = true;
        col.color    = new ParticleSystem.MinMaxGradient(colorOverLifetime);

        // ── Size over lifetime — shrink to nothing at end ─────────────────────
        var sizeOL = m_ps.sizeOverLifetime;
        sizeOL.enabled = true;
        var shrinkCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.7f, 0.9f),
            new Keyframe(1f, 0f));
        sizeOL.size = new ParticleSystem.MinMaxCurve(1f, shrinkCurve);

        // ── Renderer — additive blending for a glowy feel ─────────────────────
        var renderer = m_ps.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode         = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder       = 10;   // above sprites by default
            renderer.material           = DefaultMaterial();
        }
    }

    // ── Default gradient / material helpers ────────────────────────────────────

    private static Gradient DefaultGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.85f, 0.95f), 0f),   // soft pink
                new GradientColorKey(new Color(1f, 0.65f, 0.85f), 0.5f), // rose
                new GradientColorKey(new Color(1f, 1f, 1f),       1f),   // white fade
            },
            new[]
            {
                new GradientAlphaKey(0f,  0f),
                new GradientAlphaKey(1f,  0.1f),
                new GradientAlphaKey(0.8f, 0.6f),
                new GradientAlphaKey(0f,  1f),
            });
        return g;
    }

    private static Material DefaultMaterial()
    {
        // Use Unity's built-in Particles/Standard shader with additive blending
        // if available; fall back to Sprites-Default so it always compiles.
        var shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) return null;

        var mat = new Material(shader);

        // Soft-additive blend — glowy without blowing out bright backgrounds.
        mat.SetFloat("_Mode", 4f); // Additive in Standard Unlit
        mat.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend",  (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_ZWrite",    0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;

        return mat;
    }

    // ── Editor helpers ─────────────────────────────────────────────────────────

    [ContextMenu("Test Burst (Play Mode)")]
    private void TestBurst() => OnPoseSelected();

    [ContextMenu("Reapply Particle Settings")]
    private void ReapplySettings()
    {
        if (m_ps == null) m_ps = burstParticles != null ? burstParticles : BuildParticleSystem();
        ApplySettings();
    }
}
