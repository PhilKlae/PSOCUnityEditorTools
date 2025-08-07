// ConnectionSettingsEditor.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

[CustomEditor(typeof(ConnectionSettings))]
public class ConnectionSettingsEditor : Editor
{
    private bool isCheckingConnection;
    private UnityWebRequest activeRequest;
    private Texture2D statusTexture;
    private const int STATUS_SIZE = 20;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        
        var settings = (ConnectionSettings)target;

        EditorGUI.BeginDisabledGroup(Application.isPlaying);
        {
            DrawConnectionFields(settings);
            EditorGUILayout.Space(10);
            DrawStatusIndicator();
            EditorGUILayout.Space(10);
            DrawTestConnectionButton(settings);
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("_notes"));
        
        serializedObject.ApplyModifiedProperties();
    }

    private void DrawConnectionFields()
    {
        EditorGUILayout.PropertyField(serializedObject.FindProperty("serverIP"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("serverPort"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("apiKey"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("username"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("password"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("projectId"));
    }


    private void DrawStatusIndicator()
    {
        if (statusTexture != null)
        {
            GUILayout.Label("Connection Status:");
            var rect = GUILayoutUtility.GetRect(STATUS_SIZE, STATUS_SIZE);
            EditorGUI.DrawTextureTransparent(rect, statusTexture);
        }
    }

    private void DrawTestConnectionButton(ConnectionSettings settings)
    {
        if (GUILayout.Button("Test Connection"))
        {
            TestConnection(settings);
        }
    }

    private void TestConnection(ConnectionSettings settings)
    {
        if (isCheckingConnection) return;

        string url = $"http://{settings.serverIP}:{settings.serverPort}/v1/health";
        
        activeRequest = UnityWebRequest.Get(url);
        activeRequest.SetRequestHeader("Authorization", $"Bearer {settings.apiKey}");
        activeRequest.SendWebRequest();

        EditorApplication.update += CheckConnectionProgress;
        isCheckingConnection = true;
        statusTexture = null;
    }

    private void CheckConnectionProgress()
    {
        if (!activeRequest.isDone) return;

        statusTexture = new Texture2D(STATUS_SIZE, STATUS_SIZE);
        Color statusColor = activeRequest.responseCode == 200 ? 
            Color.green : 
            Color.red;

        if (activeRequest.result == UnityWebRequest.Result.ConnectionError)
        {
            Debug.LogError($"Connection failed: {activeRequest.error}");
            statusColor = Color.red;
        }

        Color[] pixels = statusTexture.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = statusColor;
        }
        statusTexture.SetPixels(pixels);
        statusTexture.Apply();

        isCheckingConnection = false;
        EditorApplication.update -= CheckConnectionProgress;
        activeRequest.Dispose();
        activeRequest = null;
        
        Repaint();
    }
}
#endif