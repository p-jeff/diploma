using System.Collections.Generic;
using UnityEngine;

namespace Plants
{
    [CreateAssetMenu(menuName = "Plants/Plant Database", fileName = "PlantDatabase")]
    public class PlantDatabase : ScriptableObject
    {
        [SerializeField] private List<PlantData> entries = new List<PlantData>();

        private static PlantDatabase s_instance;

        public static PlantDatabase Instance => s_instance;

        void OnEnable() => s_instance = this;

        public PlantData Get(PlantId id)
        {
            if (id == PlantId.None) return null;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e != null && e.id == id) return e;
            }
            return null;
        }
    }
}
