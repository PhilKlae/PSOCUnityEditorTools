#if UNITY_EDITOR
// QueryEditorWindow.cs
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using System;
using Packages.PSOC.Workflows;
using UnityEditor.Scripting.Python;

public class QueryEditorWindow : EditorWindow
{
    private PSOCQueryable selectedAgent;
    private string queryText = "";
    private string generatedCode = "";
    private string json_log = ""; // workflows are logged to get an idea of what is happening behind the scenes
    private string additionalNotes = "";
    private Vector2 codeScroll;
    private Vector2 notesScroll;
    private bool isProcessing;

    [MenuItem("PSOC/Query Editor")]
    public static void ShowWindow() => GetWindow<QueryEditorWindow>("Query Editor");

    void OnGUI()
    {
        DrawAgentSelection();
        DrawQueryInput();
        DrawCodeOutput();
        DrawAdditionalNotes();
    }

    private void DrawAgentSelection()
    {
        EditorGUILayout.LabelField("Agent Configuration", EditorStyles.boldLabel);
        selectedAgent = (PSOCQueryable)EditorGUILayout.ObjectField(
            "Selected Agent", 
            selectedAgent, 
            typeof(PSOCQueryable), 
            false
        );
    }

    private void DrawQueryInput()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Query Input", EditorStyles.boldLabel);
        queryText = EditorGUILayout.TextArea(queryText, GUILayout.Height(100));
        
        EditorGUI.BeginDisabledGroup(selectedAgent == null || string.IsNullOrEmpty(queryText) || isProcessing);
        if (GUILayout.Button("Send Query", GUILayout.Height(30)))
        {
            if (selectedAgent is AgentBase agent)
                ProcessQueryAgent(agent);
            else if (selectedAgent is Workflow workflow)
                ProcessQueryWorkflow(workflow);
            else
                Debug.LogError("Invalid agent type selected!");
            
        }
        EditorGUI.EndDisabledGroup();
    }

    private void DrawCodeOutput()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Generated Code", EditorStyles.boldLabel);
        
        codeScroll = EditorGUILayout.BeginScrollView(codeScroll, GUILayout.Height(200));
        generatedCode = EditorGUILayout.TextArea(generatedCode, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();

        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(generatedCode));
        if (GUILayout.Button("Execute Code", GUILayout.Height(30)))
        {
            ExecuteGeneratedCode();
        }
        EditorGUI.EndDisabledGroup();
    }

    private void DrawAdditionalNotes()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Additional Notes", EditorStyles.boldLabel);
        notesScroll = EditorGUILayout.BeginScrollView(notesScroll, GUILayout.Height(100));
        additionalNotes = EditorGUILayout.TextArea(additionalNotes, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
        
        // add a button that opens the json log in a standard json editor if there is one 
        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(json_log));
        if (GUILayout.Button("Open JSON Log", GUILayout.Height(30)))
        {
            // Open the json log in a standard json editor
            var path = System.IO.Path.Combine(Application.dataPath, "PSOC", "Logs", "query_log.json");
            if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(path)))
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            }
            System.IO.File.WriteAllText(path, json_log);
            UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(path, 0);
        }
        EditorGUI.EndDisabledGroup();
    }
    
    
    private async void ProcessQueryWorkflow(Workflow workflow)
    {
        if (workflow == null || string.IsNullOrEmpty(workflow.workflowId))
        {
            Debug.LogError("No valid workflow selected!");
            return;
        }
        isProcessing = true;
        var settings = ConnectionSettings.Instance;
        var url = $"http://{settings.serverIP}:{settings.serverPort}/v1/query/workflow";

        try
        {
            var requestData = new WorkflowQueryRequest()
            {
                workflow_id = workflow.workflowId,
                query = workflow.GetPromptText(queryText)
            };

            var json = JsonConvert.SerializeObject(requestData);
            var request = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await System.Threading.Tasks.Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Query failed: {request.error}");
                return;
            }

            var response = JsonConvert.DeserializeObject<WorkflowCodeGenerationResponse>(request.downloadHandler.text);
            generatedCode = response.executable_unity_python_code;
            additionalNotes = string.Join("\n", response.notes);
            json_log = response.json_log; // store the json log for debugging purposes
            var inferenceTime = response.execution_time;
            
            // prepend additional notes with exection time
            additionalNotes = $"(inference took : {inferenceTime} seconds) \n {additionalNotes}";
        }
        catch (Exception e)
        {
            Debug.LogError($"Query processing error: {e.Message}");
        }
        finally
        {
            isProcessing = false;
            Repaint();
        }
    }
    
    private async void ProcessQueryAgent(AgentBase agentBase)
    {
        if (agentBase == null || string.IsNullOrEmpty(agentBase.agentId))
        {
            Debug.LogError("No valid agent selected!");
            return;
        }

        isProcessing = true;
        var settings = ConnectionSettings.Instance;
        var url = $"http://{settings.serverIP}:{settings.serverPort}/v1/query";

        try
        {
            var requestData = new AgentQueryRequest
            {
                agent_id = agentBase.agentId,
                query = agentBase.GetPromptText(queryText),
                max_tokens = 300
            };

            var json = JsonConvert.SerializeObject(requestData);
            var request = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await System.Threading.Tasks.Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Query failed: {request.error}");
                return;
            }

            var response = JsonConvert.DeserializeObject<CodeGenerationResponse>(request.downloadHandler.text);
            generatedCode = response.executable_unity_python_code;
            additionalNotes = string.Join("\n", response.notes);
            var inferenceTime = response.execution_time;
            
            // prepend additional notes with exection time
            additionalNotes = $"(inference took : {inferenceTime} seconds) \n {additionalNotes}";
        }
        catch (Exception e)
        {
            Debug.LogError($"Query processing error: {e.Message}");
        }
        finally
        {
            isProcessing = false;
            Repaint();
        }
    }

    private void ExecuteGeneratedCode()
    {
        if (string.IsNullOrEmpty(generatedCode))
        {
            Debug.LogWarning("No code to execute!");
            return;
        }

        try
        {
            PythonRunner.RunString(generatedCode);
            Debug.Log("Code executed successfully!");
        }
        catch (Exception e)
        {
            Debug.LogError($"Code execution failed: {e.Message}");
        }
    }

    [System.Serializable]
    private class AgentQueryRequest
    {
        public string agent_id;
        public string query;
        public int max_tokens;
    }
    
    [System.Serializable]
    private class WorkflowQueryRequest
    {
        public string workflow_id;
        public string query;
    }


    [System.Serializable]
    private class CodeGenerationResponse
    {
        public float execution_time;
        public string notes;
        public string executable_unity_python_code;
    }
    
    [System.Serializable]
    private class WorkflowCodeGenerationResponse
    {
        public float execution_time;
        public string notes;
        public string json_log;
        public string executable_unity_python_code;
    }

    [System.Serializable]
    private class ToolCall
    {
        public string tool_name;
        public string[] parameters;
    }
}
#endif