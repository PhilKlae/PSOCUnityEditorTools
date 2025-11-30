// ChatDtos.cs
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
// Simple main-thread dispatcher so background tasks can enqueue actions to run on the Editor main thread
using System.Collections.Concurrent;
using UnityEditor;

[Serializable]
public class SenderDto
{
    public string id;
    public string display_name;
    public string role; // user / agent / system
    // optional tag like "BalanceAgent · #042"
    public string tag;
}

[Serializable]
public class MessageDto
{
    public long seq;
    public string id;
    public SenderDto sender;
    public string text;
    public bool is_delta;
    public bool is_completed;
    public DateTime created_at;
}

[Serializable]
public class FinalOutputDto
{
    public string artifact_id;
    public string code_snippet;
    public string language;
    public string metadata;
}


public static class EditorMainThreadDispatcher
{
    private static readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();

    static EditorMainThreadDispatcher()
    {
        EditorApplication.update += Pump;
    }

    private static void Pump()
    {
        while (queue.TryDequeue(out var a))
        {
            try { a?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }
    }

    public static void Enqueue(Action a)
    {
        if (a == null) return;
        queue.Enqueue(a);
    }
}

#endif
