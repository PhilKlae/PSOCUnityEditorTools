using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json;

namespace Packages.PSOC.Workflows
{
    [CreateAssetMenu(fileName = "GraphDesignerWorkflow", menuName = "PSOC/Workflow/GraphDesignerWorkflow", order = 1)]
    public class GraphDesignerWorkflow : Workflow
    {
        [SerializeField, TextArea]
        private string workflow_common_concept;

        [SerializeField]
        private List<BlackboardClassGroup> classGroups;


        public IReadOnlyList<BlackboardClassGroup> ClassGroups => classGroups;
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
            var dict = new Dictionary<string, List<string>>();

            // go through each class group or class group child
            foreach (var group in classGroups)
            {
                
                if (group.Implementations.Count == 0)
                {
                    // get connectable classes                    
                    dict[group.BaseClassScript.GetClass().Name] = ClassFieldDescriptor.GetReferencableTypeNames(group.BaseClassScript.GetClass());
                }
                else
                {
                    foreach (var classEntry in group.Implementations)
                    {
                        var possibleEdges = new List<string>();                                                
                        // get connectable classes
                        possibleEdges.AddRange(ClassFieldDescriptor.GetReferencableTypeNames(classEntry.TargetScript.GetClass()));
                    
                        dict[classEntry.TargetScript.GetClass().Name ] = possibleEdges;
                    }
                }
            }

            return dict;
        } 

        public Dictionary<string,string> GetNodeDescriptions()
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
                        dict[classEntry.TargetScript.GetClass().Name ] = string.IsNullOrEmpty(classEntry.DescriptionOverride) ? ClassFieldDescriptor.Describe(group.BaseClassScript) : classEntry.DescriptionOverride;
                    }
                }
            }

            return dict;
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