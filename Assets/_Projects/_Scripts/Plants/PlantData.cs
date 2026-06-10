using UnityEngine;

namespace Plants
{
    [CreateAssetMenu(menuName = "Plants/Plant Data", fileName = "PlantData")]
    public class PlantData : ScriptableObject
    {
        public PlantId id;
        public string displayName;

        [TextArea(2, 4)]
        public string[] facts;
    }
}
