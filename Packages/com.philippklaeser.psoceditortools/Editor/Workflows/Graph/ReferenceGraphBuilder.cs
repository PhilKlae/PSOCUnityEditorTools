using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Packages.PSOC.Workflows.Graph
{
    /// <summary>
    /// Builds a detailed reference graph from classes that have fields marked with [FieldRef].
    /// The resulting graph has one edge per referencable type, organized by field.
    /// </summary>
    public static class ReferenceGraphBuilder
    {
        /// <summary>
        /// Builds a detailed reference graph from the given types.
        /// Each field marked with [FieldRef] will create edges for each possible reference type.
        /// </summary>
        public static ReferenceGraph BuildGraphFromTypes(IEnumerable<Type> types)
        {
            if (types == null)
                throw new ArgumentNullException(nameof(types));

            var graph = new ReferenceGraph();
            var typeList = types.ToList();

            // First, add all nodes
            foreach (var type in typeList)
            {
                graph.AddNode(type.Name);
            }

            // Then, for each type, find all reference fields and add edges
            foreach (var sourceType in typeList)
            {
                var sourceNode = graph.GetOrAddNode(sourceType.Name);
                var fields = GetReferenceFields(sourceType);

                foreach (var field in fields)
                {
                    var fieldRefAttribute = field.GetCustomAttribute<FieldRefAttribute>(inherit: true);
                    if (fieldRefAttribute != null)
                    {
                        // For each referencable type in this field, add an edge
                        var referencableTypes = ResolveReferencableTypes(fieldRefAttribute);
                        foreach (var refType in referencableTypes)
                        {
                            var targetNode = graph.GetOrAddNode(refType.Name);
                            graph.AddEdge(sourceNode, targetNode, field.Name, refType.FullName);
                        }
                    }
                }
            }

            return graph;
        }

        /// <summary>
        /// Builds a detailed reference graph from classes in the given class groups.
        /// Used with the workflow's BlackboardClassGroup structure.
        /// </summary>
        public static ReferenceGraph BuildGraphFromBlackboardClassGroups(
            IEnumerable<BlackboardClassGroup> classGroups)
        {
            if (classGroups == null)
                throw new ArgumentNullException(nameof(classGroups));

            var types = new List<Type>();

            foreach (var group in classGroups)
            {
                if (group.Implementations.Count == 0)
                {
                    // Base class without implementations
                    var type = group.BaseClassScript.GetClass();
                    if (type != null)
                        types.Add(type);
                }
                else
                {
                    // Add all implementation classes
                    foreach (var classEntry in group.Implementations)
                    {
                        var type = classEntry.TargetScript.GetClass();
                        if (type != null)
                            types.Add(type);
                    }
                }
            }

            return BuildGraphFromTypes(types);
        }

        /// <summary>
        /// Gets all fields in a type that are marked with [FieldRef].
        /// </summary>
        private static FieldInfo[] GetReferenceFields(Type type)
        {
            var fields = type.GetFields(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

            return fields
                .Where(f => f.GetCustomAttribute<FieldRefAttribute>(inherit: true) != null)
                .ToArray();
        }

        /// <summary>
        /// Resolves the actual referencable types from a FieldRefAttribute.
        /// If the attribute specifies interfaces, this will find all implementing types.
        /// If it specifies concrete types, those are returned as-is.
        /// </summary>
        private static IEnumerable<Type> ResolveReferencableTypes(FieldRefAttribute attribute)
        {
            if (attribute == null || attribute.ReferencableTypes == null)
                yield break;

            var resolved = new HashSet<Type>();

            foreach (var declaredType in attribute.ReferencableTypes)
            {
                var assignableTypes = GetAssignableTypes(declaredType);
                foreach (var type in assignableTypes)
                {
                    resolved.Add(type);
                }
            }

            // Return in sorted order for consistency
            foreach (var type in resolved.OrderBy(t => t.FullName))
            {
                yield return type;
            }
        }

        /// <summary>
        /// Gets all types that are assignable to the given base type (including the base type itself if concrete).
        /// This includes implementations of interfaces and subclasses.
        /// </summary>
        private static IEnumerable<Type> GetAssignableTypes(Type baseType)
        {
            if (baseType == null)
                yield break;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var candidates = new HashSet<Type>();

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

                foreach (var candidate in types)
                {
                    if (candidate == null)
                        continue;

                    if (candidate.IsAbstract || candidate.IsGenericTypeDefinition)
                        continue;

                    if (!baseType.IsAssignableFrom(candidate))
                        continue;

                    candidates.Add(candidate);
                }
            }

            foreach (var type in candidates.OrderBy(t => t.FullName))
            {
                yield return type;
            }
        }

        /// <summary>
        /// Determines if an assembly is user code (should be included in type resolution).
        /// </summary>
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
}
