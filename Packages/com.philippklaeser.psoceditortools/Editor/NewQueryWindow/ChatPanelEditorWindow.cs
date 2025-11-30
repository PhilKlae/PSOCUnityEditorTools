// ChatPanelEditorWindow.cs
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class ChatPanelEditorWindow : EditorWindow
{
    VisualTreeAsset chatWindowUxml;

    private VisualElement root;
    private ScrollView chatHistory;
    private TextField chatInput;
    private Button sendButton;
    private ScrollView chatsScrollview;

    private ChatService chatService;
    private string chatId;

    // Keep a simple in-memory grouping by sender id -> container
    private readonly Dictionary<string, VisualElement> senderGroups = new Dictionary<string, VisualElement>();

    [MenuItem("PSOC/Chat Panel")]
    public static void ShowWindow()
    {
        var wnd = GetWindow<ChatPanelEditorWindow>("Chat Panel");
        wnd.minSize = new Vector2(600, 400);
    }

    private void OnEnable()
    {
        root = rootVisualElement;

        // Load UXML
        chatWindowUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.philippklaeser.psocai/Editor/NewQueryWindow/ChatWindow.uxml");
        if (chatWindowUxml != null)
        {
            chatWindowUxml.CloneTree(root);
        }
        else
        {
            root.Add(new Label($"Missing UXML at {chatWindowUxml}"));
        }

        // Find elements
        chatHistory = root.Q<ScrollView>("ChatHistory");
        chatsScrollview = root.Q<ScrollView>("ChatsScrollview");
        chatInput = root.Q<TextField>("ChatInput");
        sendButton = root.Q<Button>("SendButton");

        if (chatInput != null) chatInput.multiline = false;

        // Small toolbar above chat for session/connect/workflow
        var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4 } };
        var btnNew = new Button(() => CreateNewSession()) { text = "New Session" };
        var btnConnect = new Button(() => Connect()) { text = "Connect WS" };
        var btnStartWorkflow = new Button(() => StartWorkflow()) { text = "Start Workflow (input)" };
        toolbar.Add(btnNew);
        toolbar.Add(btnConnect);
        toolbar.Add(btnStartWorkflow);

        // Insert toolbar at top of ChatContentColumn if present
        var chatContent = root.Q<VisualElement>("ChatContentColumn");
        if (chatContent != null) chatContent.Insert(0, toolbar);
        else root.Insert(0, toolbar);

        if (sendButton != null)
            sendButton.clicked += OnSendClicked;

        chatService = new ChatService();
        chatService.OnMessageDelta += OnMessageDelta;
        chatService.OnMessageCompleted += OnMessageCompleted;
        chatService.OnFinalOutputReady += OnFinalOutputReady;
        chatService.OnConnected += () => Debug.Log("ChatService connected");
        chatService.OnDisconnected += () => Debug.Log("ChatService disconnected");
        chatService.OnError += (ex) => Debug.LogException(ex);

        // Try to restore last chat id
        chatId = chatService.LoadChatId();
        if (!string.IsNullOrEmpty(chatId))
        {
            AddSystemMessage($"Restored chat id: {chatId}");
        }
    }

    private void OnDisable()
    {
        if (sendButton != null) sendButton.clicked -= OnSendClicked;
        if (chatService != null) chatService.DisconnectWebSocket();
    }

    private async void CreateNewSession()
    {
        try
        {
            var id = await chatService.CreateChatAsync();
            chatId = id;
            AddSystemMessage($"Created chat {chatId}");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            AddSystemMessage($"Failed to create chat: {ex.Message}");
        }
    }

    private async void Connect()
    {
        if (string.IsNullOrEmpty(chatId))
        {
            AddSystemMessage("No chat id. Create a new session first.");
            return;
        }

        try
        {
            await chatService.ConnectWebSocketAsync(chatId, 0);
            AddSystemMessage("Connected to WS");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            AddSystemMessage($"WS connect failed: {ex.Message}");
        }
    }

    private async void StartWorkflow()
    {
        if (string.IsNullOrEmpty(chatId))
        {
            AddSystemMessage("No chat id. Create a new session first.");
            return;
        }

        var workflowKey = "default_workflow";
        var initPrompt = chatInput != null ? chatInput.value : string.Empty;

        try
        {
            await chatService.StartWorkflowAsync(chatId, workflowKey, initPrompt);
            AddSystemMessage($"Started workflow '{workflowKey}'");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            AddSystemMessage($"Start workflow failed: {ex.Message}");
        }
    }

    private async void OnSendClicked()
    {
        var text = chatInput?.value?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        // show immediate local user message
        var user = new SenderDto { id = "local_user", display_name = "You", role = "user" };
        var msg = new MessageDto { id = Guid.NewGuid().ToString(), sender = user, text = text, created_at = DateTime.Now, seq = -1, is_delta = false };
        AddMessageToUI(msg);

        chatInput.value = string.Empty;

        try
        {
            await chatService.SendUserMessageAsync(text);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            AddSystemMessage($"Send failed: {ex.Message}");
        }
    }

    private void OnMessageDelta(MessageDto msg)
    {
        AddOrUpdateDeltaMessage(msg);
    }

    private void OnMessageCompleted(MessageDto msg)
    {
        AddMessageToUI(msg);
    }

    private void OnFinalOutputReady(FinalOutputDto final)
    {
        EditorMainThreadDispatcher.Enqueue(() =>
        {
            var container = new VisualElement { style = { marginTop = 6, paddingTop = 6, borderTopColor = Color.gray, borderTopWidth = 1 } };
            var header = new Label("Final Output (code)") { style = { unityFontStyleAndWeight = FontStyle.Bold } };
            var codeField = new TextField { multiline = true, value = final?.code_snippet ?? "", style = { height = 200, unityTextAlign = TextAnchor.UpperLeft } };
            var runBtn = new Button(() => RunCodeInEditor(codeField.value)) { text = "Run in Editor" };
            container.Add(header);
            container.Add(codeField);
            container.Add(runBtn);
            chatHistory.Add(container);
            chatHistory.ScrollTo(container);
        });
    }

    private void AddSystemMessage(string text)
    {
        var sys = new SenderDto { id = "system", display_name = "System", role = "system" };
        var msg = new MessageDto { id = Guid.NewGuid().ToString(), sender = sys, text = text, created_at = DateTime.Now, seq = -1 };
        AddMessageToUI(msg);
    }

    private void AddOrUpdateDeltaMessage(MessageDto msg)
    {
        // For simplicity add a partial message with special style.
        EditorMainThreadDispatcher.Enqueue(() =>
        {
            // if group exists, append or update last child
            if (!senderGroups.TryGetValue(msg.sender.id, out var group))
            {
                group = CreateSenderGroup(msg.sender);
                senderGroups[msg.sender.id] = group;
                chatHistory.Add(group);
            }

            // try to find an existing delta label
            var last = group.Children().LastOrDefault();
            if (last is Label l && l.name == "delta")
            {
                l.text = msg.text;
            }
            else
            {
                var lbl = new Label(msg.text) { name = "delta", style = { unityFontStyleAndWeight = FontStyle.Italic } };
                group.Add(lbl);
            }

            chatHistory.ScrollTo(group);
        });
    }

    private void AddMessageToUI(MessageDto msg)
    {
        EditorMainThreadDispatcher.Enqueue(() =>
        {
            if (!senderGroups.TryGetValue(msg.sender.id, out var group))
            {
                group = CreateSenderGroup(msg.sender);
                senderGroups[msg.sender.id] = group;
                chatHistory.Add(group);
            }

            // Add completed message
            var lbl = new Label(msg.text) { style = { whiteSpace = WhiteSpace.Normal } };
            group.Add(lbl);
            // remove any delta placeholder
            var delta = group.Q<Label>("delta");
            if (delta != null) delta.RemoveFromHierarchy();

            chatHistory.ScrollTo(group);
        });
    }

    private VisualElement CreateSenderGroup(SenderDto sender)
    {
        var container = new VisualElement { style = { marginTop = 6, paddingLeft = 4, paddingRight = 4 } };
        var header = new Label($"{sender.display_name}{(string.IsNullOrEmpty(sender.tag) ? string.Empty : " · " + sender.tag)}") { style = { unityFontStyleAndWeight = FontStyle.Bold } };
        container.Add(header);
        return container;
    }

    private void RunCodeInEditor(string code)
    {
        try
        {
            // Use Unity's PythonRunner (package com.unity.scripting.python)
            var py = Type.GetType("Unity.Scripting.Python.Editor.PythonRunner, Unity.Scripting.Python.Editor");
            if (py != null)
            {
                // call EnsureInitialized and RunString via reflection to avoid compile-time dependency
                var ensure = py.GetMethod("EnsureInitialized", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var runString = py.GetMethod("RunString", new Type[] { typeof(string), typeof(string) });
                ensure?.Invoke(null, null);
                runString?.Invoke(null, new object[] { code, "__main__" });

                AddSystemMessage("Python code executed (RunString)");

                // optionally post execution report
                if (!string.IsNullOrEmpty(chatId))
                {
                    _ = chatService.PostExecutionReportAsync(chatId, "success", "executed");
                }
            }
            else
            {
                Debug.LogWarning("PythonRunner not available in project. Install com.unity.scripting.python package.");
                AddSystemMessage("PythonRunner not available");
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            AddSystemMessage($"Execution failed: {ex.Message}");
            if (!string.IsNullOrEmpty(chatId)) _ = chatService.PostExecutionReportAsync(chatId, "error", ex.Message);
        }
    }
}
#endif
