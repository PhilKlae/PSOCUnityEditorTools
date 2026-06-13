using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json;
using Packages.PSOC.Workflows.Graph;

namespace Packages.PSOC.Workflows
{
    [CreateAssetMenu(fileName = "GraphDesignerWorkflow", menuName = "PSOC/Workflow/GraphDesignerWorkflow", order = 1)]
    public class GraphDesignerWorkflow : Workflow
    {
        [SerializeField, TextArea]
        private string workflow_common_concept;

        [SerializeField]
        private List<BlackboardClassGroup> classGroups;

        private ReferenceGraph _detailedGraph;

        [SerializeField]
        private VectorQueryEngineTool SemanticClassQueryEngineTool;

        public string SemanticClassQueryEngineID => SemanticClassQueryEngineTool != null ? SemanticClassQueryEngineTool.toolId : null;

        public IReadOnlyList<BlackboardClassGroup> ClassGroups => classGroups;

        /// <summary>
        /// Gets or builds the detailed reference graph that contains per-field reference information.
        /// </summary>
        public ReferenceGraph GetDetailedReferenceGraph()
        {

            /*_detailedGraph = ReferenceGraphBuilder.BuildGraphFromBlackboardClassGroups(classGroups);*/
            var rootType = new List<Type>
            {
                classGroups[0].BaseClassScript.GetClass()
            };
            _detailedGraph = ReferenceGraphBuilder.BuildGraphFromTypesWithRoot(ReferenceGraphBuilder.GetTypesFromClassGroups(classGroups),rootType);
            _detailedGraph.UpdateRootNodes();
            return _detailedGraph;
        }
        [ContextMenu("Test Reference Graph")]
        private void TestReferenceGraph()
        {
            var graph = GetDetailedReferenceGraph();
            var tree = graph.PrintAsTree();
            Debug.Log(tree);
        }

        [ContextMenu("Test Misc Data")]
        private void TestMiscData()
        {
            // test getting possible nodes and edges
            var nodes = GetPossibleNodes();
            var edges = GetPossibleEdgesForNodes();
            // serialize to json for easy viewing
            string nodesJson = JsonConvert.SerializeObject(nodes, Formatting.Indented);
            string edgesJson = JsonConvert.SerializeObject(edges, Formatting.Indented);
            Debug.Log("Possible Nodes:\n" + nodesJson);
            Debug.Log("Possible Edges:\n" + edgesJson);

            // also test getting node descriptions
            var descriptions = GetNodeDescriptions();
            string descriptionsJson = JsonConvert.SerializeObject(descriptions, Formatting.Indented);
            Debug.Log("Node Descriptions:\n" + descriptionsJson);

            // test the detailed graph
            var detailedGraph = GetDetailedReferenceGraph();
            Debug.Log($"Detailed Graph: {detailedGraph}");
            Debug.Log($"Total edges in detailed graph: {detailedGraph.Edges.Count}");

            // Log edges grouped by source class
            foreach (var node in detailedGraph.Nodes)
            {
                var outgoing = detailedGraph.GetOutgoingEdges(node).ToList();
                if (outgoing.Count > 0)
                {
                    Debug.Log($"{node.ClassName} has {outgoing.Count} outgoing edges:");
                    foreach (var edge in outgoing)
                    {
                        Debug.Log($"  - Field '{edge.FieldName}' -> {edge.TargetNode.ClassName} (type: {edge.ReferencedTypeName})");
                    }
                }
            }
        }
        
        [ContextMenu("TestDetailledGraphJson")]
        private void TestDetailledGraphJson()
        {
            var graph = GetDetailedReferenceGraph();
            var json = graph.ToJson();
            Debug.Log(json);
        }

        public List<string> GetPossibleNodes()
        {
            var nodeList = new List<string>();

            // go through each class group or class group child
            foreach (var group in classGroups)
            {
                if (group.Implementations.Count == 0)
                {
                    // get connectable classes
                    nodeList.Add(group.BaseClassScript.GetClass().Name);
                }
                else
                {
                    foreach (var classEntry in group.Implementations)
                    {
                        nodeList.Add(classEntry.TargetScript.GetClass().Name);
                    }
                }
            }

            return nodeList;
        }

        public Dictionary<string, List<string>> GetPossibleEdgesForNodes()
        {
            var detailedGraph = GetDetailedReferenceGraph();
            return detailedGraph.DeriveSimplifiedGraph();
        }

        public Dictionary<string, string> GetNodeDescriptions()
        {
            var dict = new Dictionary<string, string>();

            // go through each class group or class group child
            foreach (var group in classGroups)
            {

                if (group.Implementations.Count == 0)
                {
                    // get connectable classes                    
                    dict[group.BaseClassScript.GetClass().Name] = ClassFieldDescriptor.Describe(group.BaseClassScript);
                }
                else
                {
                    foreach (var classEntry in group.Implementations)
                    {
                        // get connectable classes                        
                        dict[classEntry.TargetScript.GetClass().Name] = string.IsNullOrEmpty(classEntry.DescriptionOverride) ? ClassFieldDescriptor.Describe(classEntry.TargetScript) : classEntry.DescriptionOverride;
                    }
                }
            }

            return dict;
        }


        /// <summary>
        /// Exports each class description as an individual json file. The class descriptions contain detailed comments and information about the fields and how settings behave
        /// </summary>
        [ContextMenu("ExportClassDescriptions")]
        private void ExportClassDescriptions()
        {
            // create a default folder if it does not exist yet (should be next to the asset of this workflow for easy access)
            var assetPath = AssetDatabase.GetAssetPath(this);
            var folderPath = Path.Combine(Path.GetDirectoryName(assetPath), "ClassDescriptions");
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
                Debug.Log($"Created folder for class descriptions at {folderPath}");
            }
            // use GetNodeDescriptions and export each dict entry as file, use key as name
            var descriptions = GetNodeDescriptions();
            foreach (var kvp in descriptions)
            {
                var filePath = Path.Combine(folderPath, kvp.Key + ".json");
                File.WriteAllText(filePath, kvp.Value);
                Debug.Log($"Exported description for {kvp.Key} to {filePath}");
            }

        }

        [ContextMenu("Create Agents")]
        private void CreateAgents()
        {
            // create critic and creator automatically, eventually also databuckets and tools for looking up possible class details
        }
        
        private readonly struct AgentCandidate
        {
            public AgentCandidate(string displayName, MonoScript script, Type type, string descriptionOverride, bool isChild = false, ScriptableObject exampleInstance = null)
            {
                DisplayName = displayName;
                Script = script;
                Type = type;
                DescriptionOverride = descriptionOverride;
                IsChild = isChild;
                ExampleInstance = exampleInstance;
            }

            public string DisplayName { get; }
            public MonoScript Script { get; }
            public Type Type { get; }
            public string DescriptionOverride { get; }

            public bool IsChild { get; }

            public ScriptableObject ExampleInstance { get;}
        }
    }

}