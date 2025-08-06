#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine.Networking;
using System;
using System.Threading.Tasks;
using Packages.PSOC.Workflows;

public class WorkflowManagerWindow : EditorWindow
{
    private Vector2 scrollPos;
    private bool isSyncing;
    private string statusMessage;
    private MessageType statusType;

    [MenuItem("PSOC/Workflow Manager")]
    public static void ShowWindow() => GetWindow<WorkflowManagerWindow>("Workflow Manager");

    void OnGUI()
    {
        var settings = ConnectionSettings.Instance;
        if (settings == null)
        {
            EditorGUILayout.HelpBox("Connection settings not found!", MessageType.Error);
            return;
        }

        DrawHeader();
        DrawWorkflowList();
        DrawSyncControls();
        DrawStatusArea();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField("Workflow Management", EditorStyles.boldLabel);
        EditorGUILayout.EndVertical();
    }

    private void DrawWorkflowList()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        
        foreach (var workflow in FindAllWorkflows())
        {
            DrawWorkflowEntry(workflow);
        }
        
        EditorGUILayout.EndScrollView();
    }

    private void DrawWorkflowEntry(Workflow workflow)
    {
        EditorGUILayout.BeginVertical(GUI.skin.box);
        
        EditorGUILayout.LabelField(workflow.name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"ID: {workflow.workflowId}");
        EditorGUILayout.LabelField($"Last Updated: {workflow.lastUpdated:yyyy-MM-dd HH:mm:ss}");
        
        EditorGUILayout.EndVertical();
    }

    private void DrawSyncControls()
    {
        EditorGUI.BeginDisabledGroup(isSyncing);
        if (GUILayout.Button("Sync All workflows", GUILayout.Height(30)))
        {
            SyncAllWorkflows();
        }
        EditorGUI.EndDisabledGroup();
    }

    private void DrawStatusArea()
    {
        if (!string.IsNullOrEmpty(statusMessage))
        {
            EditorGUILayout.HelpBox(statusMessage, statusType);
        }
    }

    private List<Workflow> FindAllWorkflows()
    {
        return AssetDatabase.FindAssets($"t:{nameof(Workflow)}")
            .Select(guid => AssetDatabase.LoadAssetAtPath<Workflow>(
                AssetDatabase.GUIDToAssetPath(guid)))
            .ToList();
    }

    // Add this method to fetch remote IDs
private async Task<List<string>> GetRemoteWorkflowIds()
{
    var settings = ConnectionSettings.Instance;
    var url = $"http://{settings.serverIP}:{settings.serverPort}/v1/workflows";
    
    using var request = UnityWebRequest.Get(url);
    request.SetRequestHeader("Authorization", $"Bearer {settings.apiKey}");
    
    var operation = request.SendWebRequest();
    while (!operation.isDone) await System.Threading.Tasks.Task.Yield();

    if (request.result != UnityWebRequest.Result.Success)
    {
        Debug.LogError($"Failed to fetch workflows: {request.error}");
        return new List<string>();
    }

    return JsonConvert.DeserializeObject<List<string>>(request.downloadHandler.text);
}

// Updated SyncAllworkflows method
private async void SyncAllWorkflows()
{
    isSyncing = true;
    statusMessage = "Starting workflow sync...";
    Repaint();

    try
    {
        // Get remote IDs first
        var remoteIds = await GetRemoteWorkflowIds();
        var localWorkflow = FindAllWorkflows();

        // Clear orphaned IDs
        foreach (var workflow in localWorkflow)
        {
            if (!string.IsNullOrEmpty(workflow.workflowId) && !remoteIds.Contains(workflow.workflowId))
            {
                Debug.Log($"Clearing orphaned ID for {workflow.name}");
                workflow.workflowId = string.Empty;
                EditorUtility.SetDirty(workflow);
            }
        }

        // Process sync
        foreach (var workflow in localWorkflow)
        {
            if (string.IsNullOrEmpty(workflow.workflowId))
            {
                statusMessage = $"Creating {workflow.workflowId}...";
                await CreateWorkflow(workflow);
            }
            else
            {
                statusMessage = $"Updating {workflow.name}...";
                await UpdateWorkflow(workflow);
            }
            
            workflow.lastUpdated = DateTime.Now;
            EditorUtility.SetDirty(workflow);
            Repaint();
            await System.Threading.Tasks.Task.Delay(100);
        }
    }
    catch (Exception e)
    {
        Debug.LogError($"Sync failed: {e}");
    }
    finally
    {
        isSyncing = false;
        statusMessage = "workflow sync completed!";
        Repaint();
    }
}

    private System.Threading.Tasks.Task CreateWorkflow(Workflow workflow)
    {
        var settings = ConnectionSettings.Instance;
        var url = $"http://{settings.serverIP}:{settings.serverPort}/v1/workflows";
        var payload = CreateWorkflowPayload(workflow);
        
        return SendWorkflowRequest(url, "POST", payload, workflow);
    }

    private System.Threading.Tasks.Task UpdateWorkflow(Workflow workflow)
    {
        var settings = ConnectionSettings.Instance;
        var url = $"http://{settings.serverIP}:{settings.serverPort}/v1/workflows/{workflow.workflowId}";
        var payload = CreateWorkflowPayload(workflow);
        
        return SendWorkflowRequest(url, "PUT", payload, workflow);
    }

    private System.Threading.Tasks.Task SendWorkflowRequest(string url, string method, string json, Workflow workflow)
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        
        var request = new UnityWebRequest(url, method);
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
     
        var operation = request.SendWebRequest();
        operation.completed += _ =>
        {
            if (request.result != UnityWebRequest.Result.Success)
            {
                tcs.SetException(new Exception(request.error));
                return;
            }

            var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(request.downloadHandler.text);
            if (response.ContainsKey("id"))
            {
                workflow.workflowId = response["id"].ToString();
                workflow.status = response["status"].ToString();
            }
            
            tcs.SetResult(true);
            request.Dispose();
        };

        return tcs.Task;
    }

    /*class AgentWorkflow(DbModel):
        """
        Multiple agents can be chained together to form a workflow, and hand eachother control to achieve a goal.
        """
    _collection: ClassVar = "Workflows"
    name: str = Field(None, min_length=3, max_length=50)
    description: Optional[str] = None
        agents: List[AgentCreate | Id] = Field(..., description="List of agents to be used in the workflow")
    root_agent: AgentCreate | Id = Field(..., description="The root agent of the workflow")
    */
    
    private string CreateWorkflowPayload(Workflow workflow)
    {
        // make sure the order is preserved!
        var agent_ids = new List<string>();
        foreach (var agent in workflow.Agents)
        {
            if (agent != null)
            {
                agent_ids.Add(agent.agentId);
            }
        }
        return Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            name = workflow.name,
            description = workflow.Description,
            agents = agent_ids,
            root_agent = workflow.RootAgent.agentId,
        });
    }
}
#endif