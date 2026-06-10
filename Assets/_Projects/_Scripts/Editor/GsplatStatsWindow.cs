using Gsplat;
using UnityEditor;
using UnityEngine;

public class GsplatStatsWindow : EditorWindow
{
    GsplatRenderer[] m_renderers = {};
    Vector2 m_scroll;

    [MenuItem("Window/Gsplat Stats")]
    static void Open() => GetWindow<GsplatStatsWindow>("Gsplat Stats");

    void OnFocus() => Refresh();

    void Refresh() =>
        m_renderers = FindObjectsByType<GsplatRenderer>(FindObjectsSortMode.None);

    void OnGUI()
    {
        if (GUILayout.Button("Refresh")) Refresh();

        ulong total = 0;
        m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
        foreach (var r in m_renderers)
        {
            uint count = r.GsplatAsset != null ? r.GsplatAsset.SplatCount : 0;
            total += count;
            EditorGUILayout.LabelField(r.name, count.ToString("N0") + " splats");
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Separator();
        EditorGUILayout.LabelField("Total", total.ToString("N0") + " splats", EditorStyles.boldLabel);
    }
}
