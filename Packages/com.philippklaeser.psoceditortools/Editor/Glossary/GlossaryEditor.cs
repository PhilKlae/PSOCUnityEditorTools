using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Glossary))]
public class GlossaryEditor : Editor
{
    private bool isSyncing;
    private string syncMessage;
    private MessageType syncMessageType = MessageType.Info;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox("The glossary sync endpoint replaces the complete remote glossary for this project id. This asset always sends the full current snapshot, excluding entries marked as excluded.", MessageType.Info);

        var glossary = (Glossary)target;
        EditorGUI.BeginDisabledGroup(isSyncing);
        if (GUILayout.Button("Sync Glossary Snapshot", GUILayout.Height(30)))
        {
            SyncGlossary(glossary);
        }
        EditorGUI.EndDisabledGroup();

        if (!string.IsNullOrEmpty(syncMessage))
        {
            EditorGUILayout.HelpBox(syncMessage, syncMessageType);
        }
    }

    private async void SyncGlossary(Glossary glossary)
    {
        isSyncing = true;
        syncMessage = "Syncing glossary snapshot...";
        syncMessageType = MessageType.Info;
        Repaint();

        try
        {
            var response = await glossary.SyncAsync();
            syncMessage = $"Synced {response.entries_saved} entries for '{response.project_id}' ({response.status}).";
            syncMessageType = MessageType.Info;
        }
        catch (Exception exception)
        {
            syncMessage = exception.Message;
            syncMessageType = MessageType.Error;
            Debug.LogError($"Glossary sync failed: {exception}");
        }
        finally
        {
            isSyncing = false;
            Repaint();
        }
    }
}

public static class GlossarySyncUtility
{
    [MenuItem("PSOC/Glossary/Open Or Create Glossary")]
    public static void OpenOrCreateGlossary()
    {
        var glossary = FindAllGlossaries().FirstOrDefault();
        if (glossary == null)
        {
            glossary = ScriptableObject.CreateInstance<Glossary>();
            AssetDatabase.CreateAsset(glossary, "Assets/Glossary.asset");
            AssetDatabase.SaveAssets();
        }

        Selection.activeObject = glossary;
        EditorUtility.FocusProjectWindow();
    }

    public static List<Glossary> FindAllGlossaries()
    {
        return AssetDatabase.FindAssets($"t:{nameof(Glossary)}")
            .Select(guid => AssetDatabase.LoadAssetAtPath<Glossary>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(glossary => glossary != null)
            .ToList();
    }

    public static async Task<int> SyncAllGlossariesAsync()
    {
        var syncedCount = 0;
        var glossariesByProject = FindAllGlossaries()
            .Where(glossary => !glossary.excludeFromSync)
            .GroupBy(glossary => glossary.ProjectId)
            .ToList();

        foreach (var excludedGlossary in FindAllGlossaries().Where(glossary => glossary.excludeFromSync))
        {
            Debug.Log($"Skipping excluded glossary {AssetDatabase.GetAssetPath(excludedGlossary)}");
        }

        foreach (var projectGroup in glossariesByProject)
        {
            var glossaries = projectGroup.ToList();
            var payload = new GlossarySyncPayload
            {
                project_id = projectGroup.Key,
                entries = glossaries
                    .SelectMany(glossary => glossary.CreateSyncPayload().entries)
                    .ToList()
            };

            var response = await Glossary.SyncPayloadAsync(payload);
            foreach (var glossary in glossaries)
            {
                glossary.ApplySyncResponse(response);
            }

            Debug.Log($"Synced glossary snapshot '{response.project_id}': {response.entries_saved} entries saved from {glossaries.Count} asset(s) ({response.status}).");
            syncedCount++;
        }

        return syncedCount;
    }
}
