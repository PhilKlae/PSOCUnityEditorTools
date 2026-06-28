using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

[CreateAssetMenu(fileName = "Glossary", menuName = "PSOC/Glossary")]
public class Glossary : ScriptableObject
{
    [Header("Glossary")]
    [Tooltip("When empty, ConnectionSettings.projectId is used.")]
    public string projectIdOverride = "";

    [TextArea]
    public string description = "Project glossary terms synced as a complete snapshot.";

    public List<GlossaryEntry> entries = new List<GlossaryEntry>();

    [Header("Sync")]
    public bool excludeFromSync;
    public string status;
    public int lastEntriesSaved;
    public string lastSyncedAt;

    public string ProjectId
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(projectIdOverride))
            {
                return projectIdOverride.Trim();
            }

            var settings = ConnectionSettings.Instance;
            return settings != null ? settings.projectId : string.Empty;
        }
    }

    public GlossarySyncPayload CreateSyncPayload()
    {
        return new GlossarySyncPayload
        {
            project_id = ProjectId,
            entries = entries
                .Where(entry => entry != null && !entry.excludeFromSync)
                .Select(entry => entry.ToPayloadEntry())
                .ToList()
        };
    }

    [ContextMenu("Sync Glossary Snapshot")]
    public async void SyncFromContextMenu()
    {
        try
        {
            var response = await SyncAsync();
            Debug.Log($"Glossary sync complete for '{response.project_id}': {response.entries_saved} entries saved ({response.status}).");
        }
        catch (Exception exception)
        {
            Debug.LogError($"Glossary sync failed: {exception.Message}");
        }
    }

    public async Task<GlossarySyncResponse> SyncAsync()
    {
        var response = await SyncPayloadAsync(CreateSyncPayload());
        ApplySyncResponse(response);
        return response;
    }

    public static async Task<GlossarySyncResponse> SyncPayloadAsync(GlossarySyncPayload payload)
    {
        var settings = ConnectionSettings.Instance;
        if (settings == null)
        {
            throw new InvalidOperationException("ConnectionSettings asset not found.");
        }

        if (payload == null || string.IsNullOrWhiteSpace(payload.project_id))
        {
            throw new InvalidOperationException("No project id configured. Set it in ConnectionSettings or on the Glossary asset.");
        }

        var url = new UriBuilder("http", settings.serverIP, settings.serverPort, "/v1/glossary/sync").ToString();
        var payloadJson = JsonConvert.SerializeObject(payload);

        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payloadJson));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        if (!string.IsNullOrEmpty(settings.apiKey))
        {
            request.SetRequestHeader("Authorization", $"Bearer {settings.apiKey}");
        }

        var operation = request.SendWebRequest();
        while (!operation.isDone)
        {
            await Task.Yield();
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new Exception($"{request.error}: {request.downloadHandler.text}");
        }

        return JsonConvert.DeserializeObject<GlossarySyncResponse>(request.downloadHandler.text);
    }

    public void ApplySyncResponse(GlossarySyncResponse response)
    {
        status = response.status;
        lastEntriesSaved = response.entries_saved;
        lastSyncedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
    }
}

[Serializable]
public class GlossaryEntry
{
    public List<string> player_terms = new List<string>();
    public List<string> engineering_terms = new List<string>();
    [TextArea] public string notes;
    public List<string> tags = new List<string>();
    public string source = "unity";
    public bool excludeFromSync;

    public GlossarySyncEntry ToPayloadEntry()
    {
        return new GlossarySyncEntry
        {
            player_terms = CleanList(player_terms),
            engineering_terms = CleanList(engineering_terms),
            notes = notes ?? string.Empty,
            tags = CleanList(tags),
            source = string.IsNullOrWhiteSpace(source) ? "unity" : source.Trim()
        };
    }

    private static List<string> CleanList(IEnumerable<string> values)
    {
        return values == null
            ? new List<string>()
            : values.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct()
                .ToList();
    }
}

[Serializable]
public class GlossarySyncPayload
{
    public string project_id;
    public List<GlossarySyncEntry> entries = new List<GlossarySyncEntry>();
}

[Serializable]
public class GlossarySyncEntry
{
    public List<string> player_terms = new List<string>();
    public List<string> engineering_terms = new List<string>();
    public string notes;
    public List<string> tags = new List<string>();
    public string source = "unity";
}

[Serializable]
public class GlossarySyncResponse
{
    public string project_id;
    public int entries_saved;
    public string status;
}
