using System;
using System.Collections.Generic;
using UnityEngine;

namespace Plants
{
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

        [Tooltip("Optional 180° environment painting displayed when this context instance is grown. " +
                 "Placeholder: lavender.jpeg.")]
        public Texture2D environmentPainting;
    }

    [CreateAssetMenu(menuName = "Plants/Plant Data", fileName = "PlantData")]
    public class PlantData : ScriptableObject
    {
        [Tooltip("Identification only — not displayed (titles are baked into the poem sprite).")]
        public string displayName;

        [Tooltip("Auto-assigned to the plant's AudioSource and played on Show().")]
        public AudioClip audioClip;

        public Texture2D environmentPainting; // Optional 180° environment painting shown when this species is selected

        [Header("Sprites")]
        [Tooltip("The single poem: text sprite + its background.")]
        public PlantLabelContent poem = new PlantLabelContent();

        [Tooltip("One or more context infos, each a text sprite + its background. " +
                 "Mapped to PlantInfo's context labels by index.")]
        public List<PlantLabelContent> contextInfos = new List<PlantLabelContent>();
    }
}
