// SyncManagerWindow.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Linq;

public class SyncManagerWindow : EditorWindow
{
    [MenuItem("PSOC/Sync Manager")]
    public static void ShowWindow() => GetWindow<SyncManagerWindow>("Sync Manager");

    void OnGUI()
    {
        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField("PSOC Sync Manager", EditorStyles.boldLabel);
        EditorGUILayout.EndVertical();

        GUILayout.Space(8);

        if (GUILayout.Button("Sync All (Agents, Buckets, Tools)", GUILayout.Height(32)))
        {
            RunSyncAll();
        }

        if (GUILayout.Button("Sync All + Redo Bulk Exporters", GUILayout.Height(32)))
        {
            RunSyncAll(redrawBulkExporters: true);
        }
    }

    private void RunSyncAll(bool redrawBulkExporters = false)
    {
        // Trigger agents
        var agentWindow = GetWindow<AgentManagerWindow>();
        if (agentWindow != null)
        {
            agentWindow.SyncAllAgents();
        }

        // Trigger buckets
        var bucketWindow = GetWindow<DataBucketManagerWindow>();
        if (bucketWindow != null)
        {
            bucketWindow.SyncAllBuckets();
        }

        // Trigger tools
        var toolWindow = GetWindow<ToolManagerWindow>();
        if (toolWindow != null)
        {
            // use the window's finder to get tools
            toolWindow.SyncAllTools();
        }

        if (redrawBulkExporters)
        {
            RedoBulkExporters();
        }
    }

    private void RedoBulkExporters()
    {
        var guids = AssetDatabase.FindAssets($"t:BulkJsonExporter");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var exporter = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) as ScriptableObject;
            if (exporter == null) continue;

            var bulk = exporter as _Scripts.JsonDescriptor.BulkJsonExporter;
            if (bulk != null)
            {
                bulk.ExportAllTemplates();
                Debug.Log($"Ran BulkJsonExporter: {path}");
            }
        }
    }
}
#endif
