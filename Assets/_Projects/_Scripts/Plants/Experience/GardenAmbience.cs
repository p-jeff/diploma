using System.Collections;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Scene-singleton ambient score for the garden. Crossfades between two looping 2D beds:
    /// an "empty garden" bed (title sequence + free-explore phase) and a "blooming garden" bed
    /// (after the user sits and the whole garden flourishes).
    ///
    /// Two child <see cref="AudioSource"/>s are created at runtime so scene setup is just this
    /// component + the two clips. The beds are NOT spatialised (<c>spatialBlend = 0</c>) — this is a
    /// score that fills the room, not a plant SFX, so it deliberately skips the HRTF/Force-To-Mono
    /// path the spatial feedback SFX use. Driven by the experience lifecycle:
    /// <list type="bullet">
    ///   <item><see cref="PlayEmpty"/> on start and on soft-reset (restart) — the default pre-bloom bed.</item>
    ///   <item><see cref="PlayBloom"/> from <c>ExperienceManager.StartFlourish</c> — the bloom swell.</item>
    /// </list>
    /// Both calls are null-safe via <see cref="Instance"/>, so a scene without this component no-ops.
    /// </summary>
    public class GardenAmbience : MonoBehaviour
    {
        public static GardenAmbience Instance { get; private set; }

        [Header("Clips")]
        [Tooltip("Looping bed for the pre-bloom garden (title sequence + free explore).")]
        [SerializeField] private AudioClip emptyClip;
        [Tooltip("Looping bed for the flourished garden (after the user sits to bloom it).")]
        [SerializeField] private AudioClip bloomClip;

        [Header("Levels")]
        [Tooltip("Target volume of the empty bed while it is the active track.")]
        [SerializeField, Range(0f, 1f)] private float emptyVolume = 0.5f;
        [Tooltip("Target volume of the bloom bed while it is the active track.")]
        [SerializeField, Range(0f, 1f)] private float bloomVolume = 0.5f;

        [Header("Crossfade")]
        [Tooltip("Seconds for one bed to fade out while the other fades in (also the fade-up time of the first bed from silence).")]
        [SerializeField, Min(0f)] private float crossfadeDuration = 2.5f;
        [Tooltip("Play the empty bed automatically on Start (the default state before any bloom). Turn off to drive purely via PlayEmpty()/PlayBloom().")]
        [SerializeField] private bool playEmptyOnStart = true;

        private AudioSource m_empty;
        private AudioSource m_bloom;
        private Coroutine m_fade;
        private bool m_bloomActive;

        void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning($"[GardenAmbience] Multiple instances; '{name}' overriding existing.", this);
            Instance = this;

            m_empty = CreateSource("Ambience_Empty", emptyClip);
            m_bloom = CreateSource("Ambience_Bloom", bloomClip);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (playEmptyOnStart) PlayEmpty();
        }

        /// <summary>A 2D, looping, silent-until-faded source carrying one bed.</summary>
        private AudioSource CreateSource(string label, AudioClip clip)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;   // 2D score — fills the room, not localised to a plant
            src.volume = 0f;         // brought up by the crossfade
            return src;
        }

        /// <summary>Crossfade to the pre-bloom "empty garden" bed (start + restart). Safe to call repeatedly.</summary>
        public void PlayEmpty()
        {
            m_bloomActive = false;
            CrossfadeTo(m_empty, emptyVolume, m_bloom);
        }

        /// <summary>Crossfade to the "blooming garden" bed — the flourish swell. Idempotent (only the first call after a reset fades in).</summary>
        public void PlayBloom()
        {
            if (m_bloomActive) return;
            m_bloomActive = true;
            CrossfadeTo(m_bloom, bloomVolume, m_empty);
        }

        private void CrossfadeTo(AudioSource into, float target, AudioSource outOf)
        {
            if (into == null) return;
            if (into.clip != null && !into.isPlaying) into.Play();
            if (m_fade != null) StopCoroutine(m_fade);
            m_fade = StartCoroutine(FadeRoutine(into, target, outOf));
        }

        private IEnumerator FadeRoutine(AudioSource into, float target, AudioSource outOf)
        {
            float dur = Mathf.Max(crossfadeDuration, 0.0001f);
            float fromIn = into != null ? into.volume : 0f;
            float fromOut = outOf != null ? outOf.volume : 0f;

            float t = 0f;
            while (t < dur)
            {
                // Self-correct: a Play() issued at a congested frame (e.g. the whole garden activating
                // at lock-in) can silently fail to start. Keep nudging the incoming bed until it's
                // actually playing, so we never fade the volume up onto a stopped source.
                if (into != null && into.clip != null && !into.isPlaying) into.Play();

                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                if (into != null) into.volume = Mathf.Lerp(fromIn, target, k);
                if (outOf != null) outOf.volume = Mathf.Lerp(fromOut, 0f, k);
                yield return null;
            }

            if (into != null) into.volume = target;
            if (outOf != null) { outOf.volume = 0f; outOf.Stop(); }   // free the idle (streamed) voice
            m_fade = null;
        }
    }
}
