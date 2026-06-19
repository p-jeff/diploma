using System;
using System.Collections.Generic;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// One depth layer of a parallax environment diorama: a painting/sprite wrapped on a
    /// half-cylinder at a given radius. Stack several layers at increasing radii (far→near)
    /// so head movement reveals real parallax depth. Use opaque art for the farthest backdrop
    /// and transparent PNGs (cutouts) for nearer layers so the layers behind show through.
    /// </summary>
    [Serializable]
    public class EnvironmentLayer
    {
        [Tooltip("The painting/sprite for this layer. Transparent PNG for foreground cutouts; " +
                 "an opaque image works for the farthest backdrop.")]
        public Texture2D texture;

        [Tooltip("Cylinder radius in metres — larger = farther away. Layers are auto-sorted by " +
                 "radius so the farthest draws first; author order doesn't matter. (0 = default 3.5)")]
        public float radius = 3.5f;

        [Tooltip("Painting width in METRES (its real-world size). Height follows the texture's " +
                 "aspect ratio, so the image is never distorted. The layer is wrapped onto a cylinder " +
                 "at `radius`, so increasing `radius` makes a same-width painting appear smaller / " +
                 "further away — i.e. radius behaves like true distance. (0 = wrap a full 180° at " +
                 "this radius)")]
        public float width = 0f;

        [Tooltip("Off (default) = the left/right edges fade softly into transparency, good for a " +
                 "backdrop blending into passthrough. On = keep hard edges and rely on the texture's " +
                 "own alpha — good for cutout sprites.")]
        public bool hardEdges = false;

        [Tooltip("Per-layer vertical nudge in metres, added on top of EnvironmentMoment's global " +
                 "verticalOffset. Raise (+) / lower (−) this one layer to overlap it with its " +
                 "neighbours on the Y axis. (0 = sit on the floor like the others)")]
        public float verticalOffset = 0f;
    }

    /// <summary>
    /// One displayed item: the label text. Rendered by a <see cref="PlantLabel"/> as TextMeshPro,
    /// with a background panel auto-sized to the text by the label itself (the panel's look + the
    /// depth offset live on the Label prefab, not here).
    /// </summary>
    [Serializable]
    public class PlantLabelContent
    {
        [Tooltip("The label text, rendered by TextMeshPro. (Migrated from a Photoshop sprite.)")]
        [TextArea(2, 8)]
        public string text;

        [Tooltip("Legacy single 180° painting for this context. Superseded by environmentLayers " +
                 "below — used only as a fallback when that list is empty.")]
        public Texture2D environmentPainting;

        [Tooltip("Parallax diorama layers shown when this context instance is grown. " +
                 "If empty, falls back to the single environmentPainting above.")]
        public List<EnvironmentLayer> environmentLayers = new List<EnvironmentLayer>();
    }

    [CreateAssetMenu(menuName = "Plants/Plant Data", fileName = "PlantData")]
    public class PlantData : ScriptableObject
    {
        [Tooltip("Identification only — not displayed (titles are baked into the poem sprite).")]
        public string displayName;

        [Tooltip("Auto-assigned to the plant's AudioSource and played on Show().")]
        public AudioClip audioClip;

        [Tooltip("Legacy single 180° painting shown when this species is selected. Superseded by " +
                 "environmentLayers below — used only as a fallback when that list is empty.")]
        public Texture2D environmentPainting;

        [Tooltip("Parallax diorama layers shown when this species is selected. " +
                 "If empty, falls back to the single environmentPainting above.")]
        public List<EnvironmentLayer> environmentLayers = new List<EnvironmentLayer>();

        [Header("Sprites")]
        [Tooltip("The single poem: text sprite + its background.")]
        public PlantLabelContent poem = new PlantLabelContent();

        [Tooltip("One or more context infos, each a text sprite + its background. " +
                 "Mapped to PlantInfo's context labels by index.")]
        public List<PlantLabelContent> contextInfos = new List<PlantLabelContent>();
    }
}
