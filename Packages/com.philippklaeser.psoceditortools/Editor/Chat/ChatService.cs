// ChatService.cs
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

public class ChatService
{
    private readonly string serverHost;
    private readonly int serverPort;
    private readonly string apiKey;

    private ClientWebSocket ws;
    private CancellationTokenSource wsCts;
    private Task receiveLoopTask;
    private Task heartbeatTask;

    private long lastSeenSeq = 0;
    private string currentChatId;

    // Events for UI binding
    public event Action<MessageDto> OnMessageDelta;
    public event Action<MessageDto> OnMessageCompleted;
    public event Action<JObject> OnAgentJoined; // payload
    public event Action<JObject> OnAgentLeft;
    public event Action<FinalOutputDto> OnFinalOutputReady;
    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action<Exception> OnError;

    public ChatService()
    {
        var settings = ConnectionSettings.Instance;
        if (settings != null)
        {
            serverHost = settings.serverIP;
            serverPort = settings.serverPort;
            apiKey = settings.apiKey;
        }
        else
        {
            serverHost = "localhost";
            serverPort = 8000;
            apiKey = string.Empty;
        }
    }

    #region REST

    public async Task<string> CreateChatAsync()
    {
        var settings = ConnectionSettings.Instance;
        var url = new UriBuilder("http", serverHost, serverPort, "/v1/chats").ToString();

        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        if (!string.IsNullOrEmpty(apiKey)) request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

        var op = request.SendWebRequest();
        while (!op.isDone) await Task.Yield();

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new Exception($"CreateChat failed: {request.error} - {request.downloadHandler.text}");
        }

        var resp = JObject.Parse(request.downloadHandler.text);
        var chatId = resp.Value<string>("id");
        if (!string.IsNullOrEmpty(chatId)) SaveChatId(chatId);
        return chatId;
    }

    public async Task StartWorkflowAsync(string chatId, string workflowKey, string initPrompt)
    {
        var url = $"http://{serverHost}:{serverPort}/v1/chats/{chatId}/start-workflow";

        var payload = new JObject
        {
            ["workflow_key"] = workflowKey,
            ["init_prompt"] = initPrompt
        };

        using var request = new UnityWebRequest(url, "POST");
        var body = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        if (!string.IsNullOrEmpty(apiKey)) request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

        var op = request.SendWebRequest();
        while (!op.isDone) await Task.Yield();

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new Exception($"StartWorkflow failed: {request.error} - {request.downloadHandler.text}");
        }
    }

    public async Task<List<MessageDto>> GetMessagesAsync(string chatId, long afterSeq = 0)
    {
        var url = $"http://{serverHost}:{serverPort}/v1/chats/{chatId}/messages?after_seq={afterSeq}";
        using var request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Content-Type", "application/json");
        if (!string.IsNullOrEmpty(apiKey)) request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

        var op = request.SendWebRequest();
        while (!op.isDone) await Task.Yield();

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new Exception($"GetMessages failed: {request.error} - {request.downloadHandler.text}");
        }

        var arr = JArray.Parse(request.downloadHandler.text);
        var list = new List<MessageDto>();
        foreach (var item in arr)
        {
            try { list.Add(item.ToObject<MessageDto>()); }
            catch (Exception) { /* ignore parse errors */ }
        }
        return list;
    }

    #endregion

    #region Execution Report (optional)

    /// <summary>
    /// Posts a minimal execution report to the server. This is intentionally generic — adjust payload to match your server's API.
    /// </summary>
    public async Task PostExecutionReportAsync(string chatId, string status, string output)
    {
        try
        {
            var url = $"http://{serverHost}:{serverPort}/v1/chats/{chatId}/execution-report";
            var payload = new JObject
            {
                ["chat_id"] = chatId,
                ["status"] = status,
                ["output"] = output
            };

            using var request = new UnityWebRequest(url, "POST");
            var body = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(apiKey)) request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            var op = request.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"PostExecutionReport failed: {request.error} - {request.downloadHandler.text}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    #endregion

    #region EditorPrefs chat id

    private const string PrefKey = "PSOC_LastChatId";
    public void SaveChatId(string chatId)
    {
        currentChatId = chatId;
        EditorPrefs.SetString(PrefKey, chatId ?? string.Empty);
    }

    public string LoadChatId()
    {
        if (!string.IsNullOrEmpty(currentChatId)) return currentChatId;
        currentChatId = EditorPrefs.GetString(PrefKey, string.Empty);
        return string.IsNullOrEmpty(currentChatId) ? null : currentChatId;
    }

    #endregion

    #region WebSocket

    public async Task ConnectWebSocketAsync(string chatId, long lastSeq = 0)
    {
        try
        {
            DisconnectWebSocket();

            currentChatId = chatId;
            lastSeenSeq = lastSeq;

            wsCts = new CancellationTokenSource();
            ws = new ClientWebSocket();

            var scheme = "ws";
            var uri = new Uri($"{scheme}://{serverHost}:{serverPort}/ws?chat_id={chatId}&last_seq={lastSeq}");

            await ws.ConnectAsync(uri, wsCts.Token);

            OnConnected?.Invoke();

            receiveLoopTask = Task.Run(() => ReceiveLoop(ws, wsCts.Token));
            heartbeatTask = Task.Run(() => HeartbeatLoop(ws, wsCts.Token));
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex);
            throw;
        }
    }

    public void DisconnectWebSocket()
    {
        try
        {
            if (wsCts != null && !wsCts.IsCancellationRequested)
            {
                wsCts.Cancel();
            }
            wsCts = null;
            ws = null;
            OnDisconnected?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private async Task ReceiveLoop(ClientWebSocket websocket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();

        try
        {
            while (websocket != null && websocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await websocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                        OnDisconnected?.Invoke();
                        return;
                    }

                    var chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    sb.Append(chunk);
                } while (!result.EndOfMessage && !ct.IsCancellationRequested);

                var text = sb.ToString();
                if (string.IsNullOrWhiteSpace(text)) continue;

                // parse event envelope
                try
                {
                    var j = JObject.Parse(text);
                    var type = j.Value<string>("type");
                    var payload = j["payload"] as JObject;

                    switch (type)
                    {
                        case "message.delta":
                            if (payload != null)
                            {
                                var msg = payload.ToObject<MessageDto>();
                                if (msg != null)
                                {
                                    lastSeenSeq = Math.Max(lastSeenSeq, msg.seq);
                                    EditorMainThreadDispatcher.Enqueue(() => OnMessageDelta?.Invoke(msg));
                                }
                            }
                            break;
                        case "message.completed":
                            if (payload != null)
                            {
                                var msg = payload.ToObject<MessageDto>();
                                if (msg != null)
                                {
                                    lastSeenSeq = Math.Max(lastSeenSeq, msg.seq);
                                    EditorMainThreadDispatcher.Enqueue(() => OnMessageCompleted?.Invoke(msg));
                                }
                            }
                            break;
                        case "agent.joined":
                            EditorMainThreadDispatcher.Enqueue(() => OnAgentJoined?.Invoke(payload));
                            break;
                        case "agent.left":
                            EditorMainThreadDispatcher.Enqueue(() => OnAgentLeft?.Invoke(payload));
                            break;
                        case "final_output.ready":
                            if (payload != null)
                            {
                                var f = payload.ToObject<FinalOutputDto>();
                                EditorMainThreadDispatcher.Enqueue(() => OnFinalOutputReady?.Invoke(f));
                            }
                            break;
                        default:
                            // unknown event, ignore or log
                            Debug.Log($"WS event: {type} -> {text}");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EditorMainThreadDispatcher.Enqueue(() => OnError?.Invoke(ex));
        }
        finally
        {
            EditorMainThreadDispatcher.Enqueue(() => OnDisconnected?.Invoke());
        }
    }

    private async Task HeartbeatLoop(ClientWebSocket websocket, CancellationToken ct)
    {
        try
        {
            while (websocket != null && websocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var ping = new JObject { ["type"] = "ping" }.ToString(Formatting.None);
                var buf = Encoding.UTF8.GetBytes(ping);
                try
                {
                    await websocket.SendAsync(new ArraySegment<byte>(buf), WebSocketMessageType.Text, true, ct);
                }
                catch { }

                await Task.Delay(TimeSpan.FromSeconds(25), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EditorMainThreadDispatcher.Enqueue(() => OnError?.Invoke(ex));
        }
    }

    public async Task SendUserMessageAsync(string text)
    {
        if (ws == null || ws.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket is not connected");

        var envelope = new JObject
        {
            ["type"] = "send.message",
            ["payload"] = new JObject { ["text"] = text }
        };

        var bytes = Encoding.UTF8.GetBytes(envelope.ToString(Formatting.None));
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    #endregion

}

#endif
