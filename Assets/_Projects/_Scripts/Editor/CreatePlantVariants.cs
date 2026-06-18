using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Plants;

public static class CreatePlantVariants
{
    const string BasePrefabPath   = "Assets/_Projects/_Prefabs/Plant.prefab";
    const string VariantsFolder   = "Assets/_Projects/_Prefabs/Plants";
    const string PlantInfoFolder  = "Assets/_Projects/_Resources/_PlantInfo";
    const string ScenePath        = "Assets/_Projects/_Scenes/AllPlants.unity";

    [MenuItem("Tools/Create Plant Variants in AllPlants")]
    static void Run()
    {
        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
        if (basePrefab == null) { Debug.LogError($"Base prefab not found at {BasePrefabPath}"); return; }

        if (!AssetDatabase.IsValidFolder(VariantsFolder))
            AssetDatabase.CreateFolder("Assets/_Projects/_Prefabs", "Plants");

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Clear any existing root objects (idempotent re-run)
        foreach (var go in scene.GetRootGameObjects())
            Object.DestroyImmediate(go);

        var guids = AssetDatabase.FindAssets("t:PlantData", new[] { PlantInfoFolder });

        float spacing = 3f;
        int i = 0;
        foreach (var guid in guids)
        {
            var dataPath = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<PlantData>(dataPath);
            if (data == null) continue;

            string variantPath = $"{VariantsFolder}/{data.name}.prefab";

            // Instantiate into scene so we can set overrides
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab, scene);
            instance.name = data.name;
            instance.transform.position = new Vector3(i * spacing, 0f, 0f);

            // Override plantData via SerializedObject so it becomes a prefab override
            var plant = instance.GetComponent<Plant>();
            var so = new SerializedObject(plant);
            so.FindProperty("plantData").objectReferenceValue = data;
            so.ApplyModifiedProperties();

            // Save as a new prefab variant and keep the scene instance connected to it
            PrefabUtility.SaveAsPrefabAssetAndConnect(
                instance, variantPath, InteractionMode.AutomatedAction);

            i++;
        }

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Created {i} plant variant prefabs in '{VariantsFolder}' and placed them in '{ScenePath}'.");
    }
}
