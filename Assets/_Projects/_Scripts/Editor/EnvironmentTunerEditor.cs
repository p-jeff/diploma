using UnityEditor;
using UnityEngine;
using Plants;

/// <summary>
/// Inspector for <see cref="EnvironmentTuner"/>: the normal fields plus big buttons to copy/log the
/// tuned values and force a rebuild. Also adds a Tools menu entry that drops a ready-to-use tuner
/// (with two starter columns) into the open scene.
/// </summary>
[CustomEditor(typeof(EnvironmentTuner))]
public class EnvironmentTunerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var tuner = (EnvironmentTuner)target;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Tick Live Preview and tune the columns + caps live (play mode follows the headset; " +
            "edit mode centres on this object). When it looks right, Copy values and paste them into " +
            "your PlantData layers and the scene's EnvironmentMoment.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Copy values to clipboard", GUILayout.Height(28)))
                tuner.CopyValuesToClipboard();
            if (GUILayout.Button("Log values", GUILayout.Height(28)))
                tuner.LogValues();
        }

        if (GUILayout.Button("Rebuild preview"))
            tuner.ForceRebuild();
    }

    [MenuItem("Tools/Environment/Create Live Tuner")]
    static void CreateTuner()
    {
        var go = new GameObject("Environment Tuner");
        var tuner = go.AddComponent<EnvironmentTuner>();
        tuner.columns.Add(new EnvironmentTuner.TunerColumn { radius = 4.5f, width = 0f });
        tuner.columns.Add(new EnvironmentTuner.TunerColumn { radius = 3.0f, width = 5f, heightOverride = 2.5f });

        Undo.RegisterCreatedObjectUndo(go, "Create Environment Tuner");
        Selection.activeGameObject = go;

        var sv = SceneView.lastActiveSceneView;
        if (sv != null)
            go.transform.position = sv.camera.transform.position + sv.camera.transform.forward * 0.01f;

        Debug.Log("[EnvironmentTuner] Created in scene. Tick Live Preview to see the diorama, " +
                  "then tune and Copy values.", go);
    }
}
