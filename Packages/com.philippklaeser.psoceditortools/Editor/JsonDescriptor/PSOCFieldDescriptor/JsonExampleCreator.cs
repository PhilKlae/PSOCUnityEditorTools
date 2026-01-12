// AnnotatedSoJson.cs
// Drop this anywhere in your project (Editor folder NOT required).
// If you want GUID resolution in builds too, you'll need your own runtime GUID system.
// This version resolves GUIDs only in the editor via AssetDatabase.

using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using System.Collections.Generic;


#if UNITY_EDITOR
using UnityEditor;
#endif

public static class AnnotatedSoJson
{
    // Fully qualified + assembly name, but not super noisy like AssemblyQualifiedName
    private static string GetTypeId(Type t)
    {
        if (t == null) return null;
        var asm = t.Assembly.GetName().Name;
        //return $"{t.FullName}, {asm}";
        return $"{t.FullName}";
    }

    // Existing entry point stays (falls back to runtime type if you don't provide declared)
    public static string ToJson(ScriptableObject so)
        => ToJson(so, so != null ? so.GetType() : null);

    // NEW: call this when you care about field declaration type
    public static string ToJson(ScriptableObject so, Type declaredRootType)
    {
        if (so == null) return "null";

        // If caller passes null, fall back to runtime.
        var rootType = declaredRootType ?? so.GetType();

        var sb = new StringBuilder(512);
        sb.Append('{');
        sb.Append('\n');

        AppendProp(sb, "objectName", so.name);
        sb.Append(',');
        sb.Append('\n');

        // IMPORTANT: declared type, fully qualified
        AppendProp(sb, "objectType", GetTypeId(rootType));
        sb.Append(',');
        sb.Append('\n');

        sb.Append("\"fields\":");
        sb.Append('\n');
        // IMPORTANT: serialize fields based on declared type (field declaration)
        WriteAnnotatedObject(sb, so, rootType);
        sb.Append('\n');
        sb.Append('}');
        return sb.ToString();
    }

    // NEW convenience overload: declared type comes from generic parameter T
    public static string ToJson<T>(T so) where T : ScriptableObject
        => ToJson(so, typeof(T));

    // -------------------- Core serialization --------------------

    private static void WriteAnnotatedObject(StringBuilder sb, object obj, Type type)
    {
        sb.Append('{');
        sb.Append('\n');

        var fields = GetAllInstanceFields(type);
        bool first = true;

        foreach (var f in fields)
        {
            // Only fields with your attribute
            if (!HasAttribute<FieldDocAttribute>(f)) continue;

            if (!first) sb.Append(',');
            first = false;

            AppendJsonString(sb, f.Name);
            sb.Append(':');

            object value;
            try { value = f.GetValue(obj); }
            catch { value = null; }

            WriteValue(sb, value, f.FieldType);
            sb.Append('\n');
        }        
        sb.Append('}');
    }

    private static void WriteValue(StringBuilder sb, object value, Type declaredType)
    {
        if (value == null)
        {
            sb.Append("null");
            return;
        }

        // Handle UnityEngine.Object references => GUID
        if (typeof(UnityEngine.Object).IsAssignableFrom(declaredType))
        {
            WriteUnityObjectRef(sb, (UnityEngine.Object)value, declaredType);
            return;
        }

        // Strings
        if (declaredType == typeof(string))
        {
            AppendJsonString(sb, (string)value);
            return;
        }

        // Primitives + decimal
        if (declaredType.IsPrimitive || declaredType == typeof(decimal))
        {
            WritePrimitive(sb, value, declaredType);
            return;
        }

        // Enums
        if (declaredType.IsEnum)
        {
            // choose: string name or numeric. Usually nicer as string.
            AppendJsonString(sb, GetTypeId(value.GetType()) + "." + value.ToString());
            return;
        }

        // IList / arrays
        if (typeof(IEnumerable).IsAssignableFrom(declaredType) && declaredType != typeof(string))
        {
            // Special-case: Unity can serialize List<T> and arrays; handle any IEnumerable here.
            WriteEnumerable(sb, (IEnumerable)value, declaredType);
            return;
        }

        // Complex object: only serialize its annotated fields (same rule, recursive)
        WriteAnnotatedObject(sb, value, value.GetType());
    }

    private static void WriteEnumerable(StringBuilder sb, IEnumerable enumerable, Type declaredType)
    {
        sb.Append('[');

        Type elementType = TryGetElementType(declaredType) ?? typeof(object);
        bool first = true;

        foreach (var item in enumerable)
        {
            if (!first) sb.Append(',');
            first = false;

            if (item == null)
            {
                sb.Append("null");
                continue;
            }

            // Use runtime type where helpful (for SerializeReference polymorphism etc.)
            var runtimeType = item.GetType();
            var t = runtimeType != typeof(object) ? runtimeType : elementType;

            WriteValue(sb, item, t);
        }

        sb.Append(']');
        sb.Append('\n');
    }

    private static void WriteUnityObjectRef(StringBuilder sb, UnityEngine.Object obj, Type declaredType)
    {
        // Output format: { "guid":"...", "type":"DamageType" }
        // If you prefer just "guid":"..." string, simplify this function.

        /*sb.Append('{');

        AppendProp(sb, "type", declaredType.Name);
        sb.Append(',');

        string guid = ResolveGuid(obj);
        AppendProp(sb, "guid", guid);

        sb.Append('}');*/

        sb.Append('"');       

        string guid = ResolveGuid(obj);
        // AppendProp(sb, "guid", guid);
        WritePrimitive(sb, guid, typeof(string));

        sb.Append('"');
    }

    private static string ResolveGuid(UnityEngine.Object obj)
    {
        if (obj == null) return null;

#if UNITY_EDITOR
        // Works for assets. For scene objects, this will usually return empty.
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long _))
            return guid;

        return null;
#else
        // Runtime fallback. Replace with your own GUID system if you have one.
        return null;
#endif
    }

    // -------------------- Reflection helpers --------------------

    private static FieldInfo[] GetAllInstanceFields(Type t)
    {
        // Include private fields in base classes too
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        // Accumulate manually because DeclaredOnly excludes base fields
        var list = new System.Collections.Generic.List<FieldInfo>(32);
        while (t != null && t != typeof(object))
        {
            list.AddRange(t.GetFields(flags));
            t = t.BaseType;
        }
        return list.ToArray();
    }

    private static bool HasAttribute<T>(FieldInfo f) where T : Attribute
        => Attribute.IsDefined(f, typeof(T), inherit: true);

    private static Type TryGetElementType(Type seqType)
    {
        if (seqType.IsArray) return seqType.GetElementType();

        if (seqType.IsGenericType)
        {
            var args = seqType.GetGenericArguments();
            if (args.Length == 1) return args[0];
        }

        // Try IEnumerable<T>
        foreach (var it in seqType.GetInterfaces())
        {
            if (it.IsGenericType && it.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return it.GetGenericArguments()[0];
        }

        return null;
    }

    // -------------------- JSON writing helpers --------------------

    private static void AppendProp(StringBuilder sb, string key, string value)
    {
        AppendJsonString(sb, key);
        sb.Append(':');
        if (value == null) sb.Append("null");
        else AppendJsonString(sb, value);
    }

    private static void AppendJsonString(StringBuilder sb, string s)
    {
        if (s == null) { sb.Append("null"); return; }

        sb.Append('\"');
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '\"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('\"');
    }

    private static void WritePrimitive(StringBuilder sb, object value, Type t)
    {
        // Ensure invariant culture (decimal point)
        if (t == typeof(bool))
        {
            sb.Append((bool)value ? "true" : "false");
            return;
        }

        if (t == typeof(char))
        {
            AppendJsonString(sb, value.ToString());
            return;
        }

        if (t == typeof(float))
        {
            float f = (float)value;
            if (float.IsNaN(f) || float.IsInfinity(f)) { sb.Append("null"); return; }
            sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
            return;
        }

        if (t == typeof(double))
        {
            double d = (double)value;
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            return;
        }

        if (t == typeof(decimal))
        {
            sb.Append(((decimal)value).ToString(CultureInfo.InvariantCulture));
            return;
        }

        // ints, bytes, etc.
        sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
    }
}
