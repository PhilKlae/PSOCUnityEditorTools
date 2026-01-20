using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Use on fields to provide a human-readable description.
/// The system will always append the fully-qualified field type.
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public class FieldDocAttribute : Attribute
{
    public string Description { get; }

    public FieldDocAttribute(string description)
    {
        Description = description;
    }
}

/// <summary>
/// Fields marked with this attribute will be marked as references to other objects. This attribute
/// is used to collect a list of possible types that can be referenced, in order to guardrail
/// automated reference resolution systems.
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public class FieldRefAttribute : Attribute
{
    public Type[] ReferencableTypes { get; } 

    public FieldRefAttribute(params Type[] referencableTypes)
    {
        ReferencableTypes = referencableTypes;
    }
   
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ClassDocAttribute : Attribute
{
    public string Description { get; }

    public ClassDocAttribute(string description)
    {
        Description = description;
    }
}


[Serializable]
public class FieldDescriptionEntry
{
    public string fieldName;
    public string fieldType;     // Fully-qualified type name
    public string description;   // Human-readable + type info appended
    public List<string> referenceTypeNames = new List<string>();
}

[Serializable]
public class ClassFieldDescriptorData
{
    public string className;         // Fully-qualified class name
    public string classDescription;  // From [ClassDoc], if present
    public List<FieldDescriptionEntry> fields = new List<FieldDescriptionEntry>();
}



/// <summary>
/// Editor-time utility that builds a JSON description of a class' serialized fields.
/// </summary>
public static class ClassFieldDescriptor
{
    private static readonly Dictionary<Type, List<string>> ReferenceTypeCache = new Dictionary<Type, List<string>>();

    /// <summary>
    /// Describe a class by its full name or simple name.
    /// Searches all loaded assemblies.
    /// </summary>
    public static string Describe(string typeName, bool requireFieldDoc = false)
    {
        var type = FindTypeByName(typeName);
        if (type == null)
        {
            Debug.LogError($"ClassFieldDescriptor: Could not find type '{typeName}'.");
            return null;
        }

        return Describe(type, requireFieldDoc);
    }

    /// <summary>
    /// Describe a class by Type.
    /// </summary>
    public static string Describe(Type type, bool requireFieldDoc = true)
    {
        if (type == null)
        {
            Debug.LogError("ClassFieldDescriptor: Type is null.");
            return null;
        }

        var data = BuildDescriptor(type, requireFieldDoc);
        // Pretty-printed JSON
        return JsonUtility.ToJson(data, true);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Describe a class from a MonoScript asset (drag & drop in editor).
    /// </summary>
    public static string Describe(MonoScript script, bool requireFieldDoc = true)
    {
        if (script == null)
        {
            Debug.LogError("ClassFieldDescriptor: MonoScript is null.");
            return null;
        }

        var type = script.GetClass();
        if (type == null)
        {
            Debug.LogError($"ClassFieldDescriptor: MonoScript '{script.name}' has no class.");
            return null;
        }

        return Describe(type, requireFieldDoc);
    }

     // returns all possible type names that can be referenced by this type (that are marked with [FieldRef])
    public static List<string> GetReferencableTypeNames(Type type)
    {
        // the type may have multiple FieldRef attributes, we should collect all possible types from all of them
        
        var fields = type.GetFields(
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly);
        List<string> allTypeNames = new List<string>();
        
        foreach (var field in fields)
        {
            var referenceAttribute = field.GetCustomAttribute<FieldRefAttribute>(inherit: true);
            if (referenceAttribute != null)
            {
                var referenceTypeNames = ResolveReferenceTypeNames(referenceAttribute);
                allTypeNames.AddRange(referenceTypeNames);
            }
        } 

        return allTypeNames.Distinct().OrderBy(n => n).ToList();
    }
#endif

    // ----------------- internals -----------------

    private static ClassFieldDescriptorData BuildDescriptor(Type rootType, bool requireFieldDoc)
    {
        var result = new ClassFieldDescriptorData
        {
            className = rootType.FullName
        };

        var classDescriptions = new List<string>();

        // Walk the inheritance chain up until MonoBehaviour / ScriptableObject / null
        Type current = rootType;
        while (current != null &&
               current != typeof(MonoBehaviour) &&
               current != typeof(ScriptableObject))
        {
            // Class-level description
            var classDoc = current.GetCustomAttribute<ClassDocAttribute>();
            if (classDoc != null)
            {
                classDescriptions.Add(classDoc.Description);                
            }            

            // Only fields declared on this level (we walk base classes ourselves)
            var fields = current.GetFields(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

            foreach (var field in fields)
            {
                if (!IsUnitySerializedField(field))
                    continue;

                // Optional description from [FieldDoc]
                var docAttr = field.GetCustomAttribute<FieldDocAttribute>(inherit: true);
                if (requireFieldDoc && docAttr == null)
                    continue;

                var entry = new FieldDescriptionEntry
                {
                    fieldName = field.Name,
                    fieldType = field.FieldType.FullName
                };

                // if this is a list or array, simplify the type name to be list<type>
                // but when its a collection, simplify this
                // to something like Enumerable<SomeType> instead of System.Collections.Generic.List`1[Namespace.SomeType]
                if(typeof(System.Collections.IEnumerable).IsAssignableFrom(field.FieldType) &&
                   field.FieldType.IsGenericType)
                {
                    var genericArgs = field.FieldType.GetGenericArguments();
                    if (genericArgs.Length == 1)
                    {
                        entry.fieldType = $"{field.FieldType.Name.Split('`')[0]}<{genericArgs[0].FullName}>";
                    }
                }
                         

                var baseDescription = docAttr?.Description ?? field.Name;

                // if the object is a UnityObject reference, add info that a guid should be used
                if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                {
                    baseDescription += " (Unity Object reference - use GUID)";
                }

                // do the same when its an enumerable collection of UnityObject references
                else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(field.FieldType))
                {
                    var genericArgs = field.FieldType.GetGenericArguments();
                    if (genericArgs.Length == 1 && typeof(UnityEngine.Object).IsAssignableFrom(genericArgs[0]))
                    {
                        baseDescription += " (Collection of Unity Object references - use GUIDs)";
                    }
                }

                // Always include fully-qualified field type,                    

                entry.description = $"{baseDescription})";

                var referenceAttribute = field.GetCustomAttribute<FieldRefAttribute>(inherit: true);
                if (referenceAttribute != null)
                {
                    var referenceTypeNames = ResolveReferenceTypeNames(referenceAttribute);
                    if (referenceTypeNames.Count > 0)
                    {
                        entry.referenceTypeNames.AddRange(referenceTypeNames);
                    }
                }

                result.fields.Add(entry);
            }

            current = current.BaseType;
        }

        // This does not seem to work, because it will just repeat the same description for each level for some reason
        // Combine class description, while using the highest-level one first
        /*classDescriptions.Reverse();
        result.classDescription = string.Join("\n", classDescriptions);*/

        if (classDescriptions.Count > 0)
        {
            result.classDescription = classDescriptions[0];
        }

        return result;
    }

    /// <summary>
    /// Mirrors Unity's serialization rules for fields.
    /// </summary>
    private static bool IsUnitySerializedField(FieldInfo field)
    {
        if (field.IsStatic)
            return false;

        // Public and not [NonSerialized]
        if (field.IsPublic && !field.IsDefined(typeof(NonSerializedAttribute), true))
            return true;

        // Non-public but marked [SerializeField]
        if (!field.IsPublic && field.IsDefined(typeof(SerializeField), true))
            return true;

        return false;
    }

    /// <summary>
    /// Find a type by full name or simple name across all loaded assemblies.
    /// </summary>
    private static Type FindTypeByName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;

        // Try fully-qualified name first
        var type = Type.GetType(typeName);
        if (type != null)
            return type;

        // Fallback: search all loaded assemblies
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            type = asm.GetType(typeName);
            if (type != null)
                return type;

            // Also allow simple name match as a last resort
            type = asm.GetTypes().FirstOrDefault(t => t.Name == typeName);
            if (type != null)
                return type;
        }

        return null;
    }

    private static List<string> ResolveReferenceTypeNames(FieldRefAttribute referenceAttribute)
    {
        if (referenceAttribute == null || referenceAttribute.ReferencableTypes == null)
            return new List<string>();

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaredType in referenceAttribute.ReferencableTypes)
        {
            foreach (var typeName in GetAssignableTypeNames(declaredType))
            {
                names.Add(typeName);
            }
        }

        return names.OrderBy(n => n).ToList();
    }

    private static IEnumerable<string> GetAssignableTypeNames(Type baseType)
    {
        if (baseType == null)
            yield break;

        if (!ReferenceTypeCache.TryGetValue(baseType, out var cached))
        {
            cached = BuildAssignableTypeNameCache(baseType);
            ReferenceTypeCache[baseType] = cached;
        }

        for (var i = 0; i < cached.Count; i++)
        {
            yield return cached[i];
        }
    }

    private static List<string> BuildAssignableTypeNameCache(Type baseType)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in EnumerateAssignableTypes(baseType))
        {
            var fullName = candidate.FullName ?? candidate.Name;
            names.Add(fullName);
        }

        return names.OrderBy(n => n).ToList();
    }

    private static IEnumerable<Type> EnumerateAssignableTypes(Type baseType)
    {
        if (baseType == null)
            yield break;

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            if (!IsUserCodeAssembly(assembly, baseType.Assembly))
                continue;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            if (types == null)
                continue;

            for (var i = 0; i < types.Length; i++)
            {
                var candidate = types[i];
                if (candidate == null)
                    continue;

                if (candidate.IsAbstract)
                    continue;

                if (candidate.IsGenericTypeDefinition)
                    continue;

                if (!baseType.IsAssignableFrom(candidate))
                    continue;

                yield return candidate;
            }
        }
    }

    private static bool IsUserCodeAssembly(Assembly assembly, Assembly baseAssembly)
    {
        if (assembly == null)
            return false;

        if (assembly == baseAssembly)
            return true;

        if (assembly.IsDynamic)
            return false;

        try
        {
            var location = assembly.Location;
            if (string.IsNullOrEmpty(location))
                return false;

            return location.IndexOf("Library\\ScriptAssemblies", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
