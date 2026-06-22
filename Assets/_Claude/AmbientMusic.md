# Ambient Music (Garden Ambience)

_Added 2026-06-22. Headset-verified._

Two looping ambient score beds that crossfade with the garden's state — an **empty garden** bed
before the bloom, and a **blooming garden** bed after. This is distinct from the per-plant feedback
SFX and the poem VO (see `ExperienceSystem.md` → Audio): it's a **non-spatialised score that fills
the room**, not localised to anything.

## Tracks
- **Empty bed** — `_Audio/Songs/An Empty Garden 02.wav` — plays through the title sequence + the
  free-explore phase (the pre-bloom garden).
- **Bloom bed** — `_Audio/Songs/A Blooming Garden.wav` — after the user sits and the garden flourishes.

## Component — `GardenAmbience.cs`
`_Scripts/Plants/Experience/`, namespace `Plants`. Scene singleton (`GardenAmbience.Instance`).

- **Auto-builds its sources.** On `Awake` it creates two child `AudioSource`s (`Ambience_Empty`,
  `Ambience_Bloom`): `loop = true`, `spatialBlend = 0` (2D), `playOnAwake = false`, `volume = 0`
  until faded. So scene setup is just the component + the two clip refs — no manual AudioSource wiring.
- **`PlayEmpty()` / `PlayBloom()`** crossfade between the beds. A single `FadeRoutine` lerps the
  incoming bed's volume up to its target and the outgoing bed down to 0 (then `Stop()`s it).
- **`playEmptyOnStart`** (default on): fades the empty bed up from silence on `Start()`, so the bed is
  already playing under the title sequence.
- All calls are reached via `GardenAmbience.Instance?.…`, so a scene **without** the GO just stays
  silent — nothing else breaks.

## Triggers (wiring)
| When | Call | Where |
|---|---|---|
| Garden start / lock-in | `PlayEmpty()` | `GardenAmbience.Start()` (automatic) |
| User sits → flourish | `PlayBloom()` | top of `ExperienceManager.StartFlourish()` |
| Restart (soft reset) | `PlayEmpty()` | `ExperienceManager.ResetAll()` |

Bloom fires at the **top of `StartFlourish`** (the instant the user sits) rather than off the
`onGardenFlourish` UnityEvent — that event only fires after the whole bloom cascade finishes
(~`flourishSpeciesStagger` × species), so the music would swell late. Triggering at the top makes the
score swell *with* the bloom.

## Scene placement
`[Garden Ambience]` GO lives under `SceneRoot/Content` (sibling of the Experience Manager) in
**`VerticalSlice.unity`**. It's gated by lock-in like the rest of `Content`. **Not yet in
`Experience.unity`** — drop the same GO under its `Content` to give that scene music (null-safe until
then). Scene references can't be wired here beyond the two clips, which are on the component.

## Import settings (IMPORTANT)
Both song `.wav` are **Compressed In Memory + Preload Audio Data** (`loadType 1`, `preloadAudioData`
on, `loadInBackground` off, Vorbis, **stereo** — do **NOT** Force-To-Mono; that's only for the
spatialised feedback SFX).

> **Gotcha — do not use Streaming.** Streaming was tried first and the **empty bed silently failed to
> start in-headset** while bloom worked. The empty bed's first `Play()` fires at lock-in — the
> congested frame the whole garden activates — and a cold *streaming* clip drops that play; the bloom
> bed plays later in a calm moment so it was fine. The editor couldn't reproduce it (fast disk; the
> clip plays in isolation). **Compressed In Memory + Preload** keeps the clip decoded-ready in RAM
> before any `Play()`, which fixed it. RAM cost is small (stays Vorbis-compressed). As a
> belt-and-suspenders, `FadeRoutine` re-`Play()`s the incoming source each frame until `isPlaying`, so
> a dropped play self-corrects.

## Tuning
| Field | Default | What it does |
|---|---|---|
| `crossfadeDuration` | 2.5 | Seconds for one bed to fade out while the other fades in (also the fade-up from silence on start). |
| `emptyVolume` / `bloomVolume` | 0.5 | Target volume of each bed while it's the active track. |
| `playEmptyOnStart` | on | Fade the empty bed up automatically on `Start()`. |

## Deferred / not built
- **Duck the empty bed under poem VO** — lower the empty bed while a poem narrates so the spoken VO
  sits clearly on top, then restore it. Designed, not built.

Related: [[ExperienceSystem]] (audio section), [[VerticalSliceScene]], [[TitleSequence]].
