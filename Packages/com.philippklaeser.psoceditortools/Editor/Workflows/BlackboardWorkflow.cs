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
    [CreateAssetMenu(fileName = "BlackboardWorkflow", menuName = "PSOC/Workflow/Blackboard")]
    public class BlackboardWorkflow : Workflow
    {
        [SerializeField, TextArea]
        private string workflow_common_concept;

        [SerializeField]
        private List<BlackboardClassGroup> classGroups;

        [SerializeField]
        private List<AgentBase> selectorAgents;

        public string WorkflowCommonConcept => workflow_common_concept;
        public IReadOnlyList<BlackboardClassGroup> ClassGroups => classGroups;

        // there is 2 types of agent prompts, experts and selectors
        // experts get a different prompt than selectors
        // public_blackboard, private_blackboard, goal, example_output are all dynamic parts that get filled in at runtime
        // parent_class_context, agent_field_extracted and selector_selection are constructed here based on the classes through ClassFieldDescriptor.Describe (field and class Attributes)

        [SerializeField]
        private TextAsset ControlUnityPromptTemplate;
        
        private const string blackboard_introduction =
@"You are part of a larger System that is supposed to help creating ingame content for a video game from natural language via scriptable object composition in unity.

You will be presented with a goal and a public Blackboard that contains a list of elements that make up the content so far. Look at the elements as a chain of elements like puzzle pieces that together form the final content. 

You will also likely see a private blackboard that contains detailed information only you as an expert should see. Look at your public blackboard entry and try to resolve the todos that are listed there, by modifying your private blackboard. Try to solve as many todos as possible, and only create new ones if you are missing other elements that you need to reference, or if you can not come up with good values for your element.
";

        private const string selector_task =
@"You are responsible for a group of Experts. It is your job to either Select a specific Expert that masters a subclass that fits the goal, or return a question to find out about missing information.
";
        [SerializeField]
        private TextAsset expertPromptTemplate;
        [SerializeField]
        private TextAsset subExpertPromptTemplate;
        [SerializeField]
        private TextAsset selectorPromptTemplate;

        /// <summary>
        /// Generates a mapping of agent class names to their respective indices.
        /// </summary>
        /// <returns>a json dictionary mapping agent class names to indices</returns>
        public Dictionary<string, int> GetAgentNameToIndexMapping()
        {
            var mapping = new Dictionary<string, int>();
            for (int i = 0; i < Agents.Count; i++)
            {
                var agent = Agents[i];
                if (agent != null && !string.IsNullOrWhiteSpace(agent.agentName))
                {
                    mapping[agent.agentName] = i;
                }
            }

            return mapping;
        }

        /// <summary>
        /// returns a list of integers representing the indices of selector agents in the Agents list
        /// </summary>
        /// <returns></returns>
        public List<int> GetSelectorAgentIndices()
        {
            var indices = new List<int>();
            for (int i = 0; i < Agents.Count; i++)
            {
                var agent = Agents[i];
                if (selectorAgents.Contains(agent))
                {
                    indices.Add(i);
                }
            }

            return indices;
        }

        [ContextMenu("Create Agents")]
        private void CreateAgents()
        {
#if UNITY_EDITOR
            if (classGroups == null || classGroups.Count == 0)
            {
                Debug.LogWarning($"BlackboardWorkflow '{name}' has no class groups configured.");
                return;
            }

            var workflowAssetPath = AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(workflowAssetPath))
            {
                Debug.LogError($"BlackboardWorkflow '{name}': Unable to resolve asset path for workflow.");
                return;
            }

            var workflowDirectory = Path.GetDirectoryName(workflowAssetPath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(workflowDirectory))
            {
                Debug.LogError($"BlackboardWorkflow '{name}': Unable to resolve workflow directory.");
                return;
            }

            var agentsFolderName = $"{Path.GetFileNameWithoutExtension(workflowAssetPath)}_Agents";
            var agentsFolderAssetPath = $"{workflowDirectory}/{agentsFolderName}";

            if (!AssetDatabase.IsValidFolder(agentsFolderAssetPath))
            {
                AssetDatabase.CreateFolder(workflowDirectory, agentsFolderName);
            }

            var createdAgents = new List<AgentBase>();
            var introduction = NormalizeLineEndings(blackboard_introduction).Trim();
            var workflowContext = string.IsNullOrWhiteSpace(workflow_common_concept)
                ? "(No additional project context provided.)"
                : NormalizeLineEndings(workflow_common_concept).Trim();           
           
            foreach (var group in classGroups)
            {
                if (group == null)
                {
                    continue;
                }

                var candidates = BuildAgentCandidates(group);
                if (candidates.Count == 0)
                {
                    Debug.LogWarning($"BlackboardWorkflow '{name}': Group '{group.GroupLabel}' has no valid implementations.");
                    continue;
                }

                var parentContextLabel = string.IsNullOrWhiteSpace(group.GroupLabel)
                    ? "Parent Class Context"
                    : $"Group: {group.GroupLabel}";

           

                if (candidates.Count > 1)
                {
                    var selectorName = string.IsNullOrWhiteSpace(group.GroupLabel)
                        ? $"{group.BaseClassScript.name} Selector"
                        : $"{group.GroupLabel} Selector";

                    var selectorDescription = string.IsNullOrWhiteSpace(group.GroupLabel)
                        ? "Selector agent responsible for choosing the most suitable expert."
                        : $"Selector agent responsible for choosing the most suitable {group.GroupLabel} expert.";

                    var selectorTaskText = NormalizeLineEndings(selector_task).Trim();        
                    var json_string = ClassFieldDescriptor.Describe(group.BaseClassScript.GetClass());
                    // load json string as object and get class summary                
                    var descriptor = JsonConvert.DeserializeObject<ClassFieldDescriptorData>(json_string).classDescription;

                    // append class summary to selector task
                    if (!string.IsNullOrWhiteSpace(descriptor))
                    {
                        selectorTaskText += $"\n\nyou are an expert for variants of {group.BaseClassScript.name}:\n{descriptor}";
                    }

                    var selectorPrompt = BuildPrompt(selectorPromptTemplate.text, new Dictionary<string, string>
                    {
                        ["blackboard_introduction"] = introduction,
                        ["workflow_common_concept"] = workflowContext,
                        ["selector_task"] = selectorTaskText,
                        ["selector_selection"] = ComposeSelectorSelection(candidates)
                    });

                    var selectorAgent = CreateAgentAsset(agentsFolderAssetPath, selectorName, selectorDescription, selectorPrompt);
                    createdAgents.Add(selectorAgent);
                    selectorAgents.Add(selectorAgent);
                }

                foreach (var candidate in candidates)
                {
                    
                    var agentContext = ComposeDescriptorSummary(candidate.Script, $"Expert Focus: {candidate.DisplayName}", candidate.DescriptionOverride);
                    if (string.IsNullOrWhiteSpace(agentContext))
                    {
                        agentContext = $"Expert Focus: {candidate.DisplayName}\nNo descriptor information available.";
                    }

                    string expertPrompt;

                    if (candidate.IsChild)
                    {                              
                        var json_string = ClassFieldDescriptor.Describe(group.BaseClassScript.GetClass());
                        // load json string as object and get class summary                
                        var parentContext = $"you are an expert for a subclass of {group.BaseClassScript.name}:\n" + JsonConvert.DeserializeObject<ClassFieldDescriptorData>(json_string).classDescription;
                        
                        if (string.IsNullOrWhiteSpace(parentContext))
                        {
                            parentContext = "No parent class context configured.";
                        }

                        var exampleOutput = AnnotatedSoJson.ToJson(candidate.ExampleInstance);

                        expertPrompt = BuildPrompt(subExpertPromptTemplate.text, new Dictionary<string, string>
                        {
                            ["blackboard_introduction"] = introduction,
                            ["workflow_common_concept"] = workflowContext,
                            ["parent_class_context"] = parentContext,
                            ["agent_field_extracted"] = agentContext,
                            ["example_object_data"] = exampleOutput
                        });
                    }else
                    {
                        var exampleOutput = AnnotatedSoJson.ToJson(candidate.ExampleInstance);

                        expertPrompt = BuildPrompt(expertPromptTemplate.text, new Dictionary<string, string>
                        {
                            ["blackboard_introduction"] = introduction,
                            ["workflow_common_concept"] = workflowContext,
                            ["agent_field_extracted"] = agentContext,
                            ["example_object_data"] = exampleOutput
                        });
                    }
                  

                    var description = !string.IsNullOrWhiteSpace(candidate.DescriptionOverride)
                        ? candidate.DescriptionOverride
                        : $"Expert agent for {candidate.Type?.Name ?? candidate.DisplayName}.";

                    var expertAgent = CreateAgentAsset(agentsFolderAssetPath, candidate.DisplayName, description, expertPrompt);
                    createdAgents.Add(expertAgent);
                }
            }

            RootAgent = CreateControlUnitAgent(agentsFolderAssetPath, createdAgents, workflowContext);
            
            // add agents to workflow, but make sure control unit is first and root agent is set
            this.Agents = createdAgents;
            

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"BlackboardWorkflow '{name}': Created {createdAgents.Count} agent assets in '{agentsFolderAssetPath}'.");
#endif
        }

        private AgentBase CreateControlUnitAgent(string agentsFolderAssetPath, List<AgentBase> createdAgents, string workflowContext)
        {
            // the control unit prompt template contains the following placeholders: workflow_common_concept, agent_list

            // the control unit only get a list of the top level experts available

            var agent_overview_builder = new StringBuilder();
            for (int i = 0; i < classGroups.Count; i++)
            {
                BlackboardClassGroup group = classGroups[i];
                // insert the class description for the base class by using field descriptor
                var baseType = group.BaseClassScript != null ? group.BaseClassScript.GetClass() : null;
                if (baseType == null)
                {
                    continue;
                }
                var class_json_string = ClassFieldDescriptor.Describe(baseType);
                var class_description = JsonConvert.DeserializeObject<ClassFieldDescriptorData>(class_json_string).classDescription;    

                var agent_description = $"{i}) {group.GroupLabel}: {class_description}\n";
                agent_overview_builder.AppendLine(agent_description);
            }

            var controlUnitPrompt = BuildPrompt(ControlUnityPromptTemplate.text, new Dictionary<string, string>
            {
                ["workflow_common_concept"] = workflowContext,
                ["agent_list"] = agent_overview_builder.ToString().Trim()
            });

            var controlUnitAgent = CreateAgentAsset(agentsFolderAssetPath, "Control Unit", "The controlling agent responsible for managing expert agents and the blackboard workflow.", controlUnitPrompt);
            createdAgents.Add(controlUnitAgent);
            return controlUnitAgent;
            
        }

        private static List<AgentCandidate> BuildAgentCandidates(BlackboardClassGroup group)
        {
            var result = new List<AgentCandidate>();

            if (group?.Implementations != null)
            {
                foreach (var entry in group.Implementations)
                {
                    if (entry?.TargetScript == null)
                    {
                        continue;
                    }

                    var type = entry.TargetScript.GetClass();
                    if (type == null)
                    {
                        Debug.LogWarning($"BlackboardWorkflow: Unable to resolve class for MonoScript '{entry.TargetScript.name}'.");
                        continue;
                    }

                    var displayName = !string.IsNullOrWhiteSpace(entry.DisplayName)
                        ? entry.DisplayName.Trim()
                        : type.Name + "-Expert";

                    var descriptionOverride = string.IsNullOrWhiteSpace(entry.DescriptionOverride)
                        ? null
                        : entry.DescriptionOverride.Trim();

                    result.Add(new AgentCandidate(displayName, entry.TargetScript, type, descriptionOverride, isChild: true, exampleInstance: entry.ExampleInstance));
                }
            }

            if (result.Count == 0 && group?.BaseClassScript != null)
            {
                var fallbackType = group.BaseClassScript.GetClass();
                if (fallbackType != null)
                {
                    var fallbackName = string.IsNullOrWhiteSpace(group.GroupLabel)
                        ? fallbackType.Name + "-Expert"
                        : group.GroupLabel;
                    result.Add(new AgentCandidate(fallbackName, group.BaseClassScript, fallbackType, null, isChild: false, exampleInstance: group.ExampleInstance));
                }
            }

            return result;
        }

        private static string ComposeSelectorSelection(List<AgentCandidate> candidates)
        {
            var builder = new StringBuilder();

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                builder.AppendLine($"{i}) {candidate.DisplayName}:");


                var json_string = ClassFieldDescriptor.Describe(candidate.Type);
                // load json string as object and get class summary                
                var descriptor = JsonConvert.DeserializeObject<ClassFieldDescriptorData>(json_string).classDescription;
                if (!string.IsNullOrWhiteSpace(descriptor))
                {
                    builder.AppendLine(descriptor);
                }
                else
                {
                    builder.AppendLine("No descriptor information available.");
                }

                if (i < candidates.Count - 1)
                {
                    builder.AppendLine();
                }
            }

            var selection = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(selection) ? "No selectable experts defined." : selection;
        }

        private AgentBase CreateAgentAsset(string folderAssetPath, string agentName, string description, string promptContent)
        {
            var sanitizedName = SanitizeFileName(agentName);
            var agentAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderAssetPath}/{sanitizedName}.asset");

            var agentAsset = ScriptableObject.CreateInstance<AgentBase>();
            agentAsset.name = agentName;
            agentAsset.agentName = agentName;
            agentAsset.description = description;
            agentAsset.tools = new List<ToolBase>();
            agentAsset.status = "Generated";
            agentAsset.lastUpdated = DateTime.UtcNow;

            AssetDatabase.CreateAsset(agentAsset, agentAssetPath);

            var promptAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderAssetPath}/{sanitizedName}_Prompt.md");
            WriteTextAsset(promptAssetPath, promptContent);

            var promptAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(promptAssetPath);
            agentAsset.promptTextFile = promptAsset;

            EditorUtility.SetDirty(agentAsset);
            return agentAsset;
        }

        private static void WriteTextAsset(string assetPath, string content)
        {
            var absolutePath = GetAbsolutePath(assetPath);
            var directory = Path.GetDirectoryName(absolutePath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(absolutePath, NormalizeLineEndings(content ?? string.Empty), Encoding.UTF8);
            AssetDatabase.ImportAsset(assetPath);
        }

        private static string BuildPrompt(string template, IDictionary<string, string> replacements)
        {
            var result = template;

            foreach (var pair in replacements)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                result = result.Replace($"{{{pair.Key}}}", pair.Value);
            }

            return NormalizeLineEndings(result);
        }

        private static string ComposeDescriptorSummary(MonoScript script, string title, string descriptionOverride = null)
        {
            var descriptorCore = BuildDescriptorCore(script, descriptionOverride);

            if (string.IsNullOrWhiteSpace(descriptorCore))
            {
                return string.IsNullOrWhiteSpace(title)
                    ? string.Empty
                    : $"{title}\nNo descriptor information available.";
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return descriptorCore;
            }

            var builder = new StringBuilder();
            builder.AppendLine(title);
            builder.AppendLine(descriptorCore);
            return builder.ToString().Trim();
        }

        private static string BuildDescriptorCore(MonoScript script, string descriptionOverride)
        {
            if (script == null)
            {
                return string.Empty;
            }

            var type = script.GetClass();
            if (type == null)
            {
                return $"Unable to resolve type for MonoScript '{script.name}'.";
            }

            string descriptorJson;
            try
            {
                descriptorJson = ClassFieldDescriptor.Describe(type);
            }
            catch (Exception ex)
            {
                Debug.LogError($"BlackboardWorkflow: Failed to build descriptor for {type.FullName}: {ex.Message}");
                return $"Class: {type.FullName}\nDescriptor generation failed.";
            }

            if (string.IsNullOrWhiteSpace(descriptorJson))
            {
                return $"Class: {type.FullName}\nNo serialized field information available.";
            }

            ClassFieldDescriptorData descriptorData;
            try
            {
                descriptorData = JsonConvert.DeserializeObject<ClassFieldDescriptorData>(descriptorJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"BlackboardWorkflow: Failed to parse descriptor JSON for {type.FullName}: {ex.Message}");
                return $"Class: {type.FullName}\nDescriptor parsing failed.";
            }
            return descriptorJson;
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Agent";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());

            return string.IsNullOrWhiteSpace(cleaned) ? "Agent" : cleaned;
        }

        private static string NormalizeLineEndings(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static string GetAbsolutePath(string assetPath)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot ?? string.Empty, assetPath);
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

    [Serializable]
    public class BlackboardClassGroup
    {
        [SerializeField]
        private MonoScript baseClass;

        [SerializeField]
        private ScriptableObject exampleInstance;

        [SerializeField]
        private string groupLabel;

        [SerializeField]
        private List<BlackboardClassEntry> implementations;

        public ScriptableObject ExampleInstance => exampleInstance;

        public MonoScript BaseClassScript => baseClass;
        // if group label is empty use baseclass name + Expert suffix
        public string GroupLabel => string.IsNullOrWhiteSpace(groupLabel)
            ? (baseClass != null ? baseClass.name + " Expert" : "Unnamed Group")
            : groupLabel;
        public IReadOnlyList<BlackboardClassEntry> Implementations => implementations;

        private static bool IsValidTarget(Type type)
        {
            return type != null && typeof(ScriptableObject).IsAssignableFrom(type);
        }
    }

    [Serializable]
    public class BlackboardClassEntry
    {
        [SerializeField]
        private MonoScript targetScript;
        
        [SerializeField]
        private ScriptableObject exampleInstance;

        [SerializeField]
        private string displayName;

        [SerializeField, TextArea]
        private string descriptionOverride;        

        public ScriptableObject ExampleInstance => exampleInstance;

        public MonoScript TargetScript => targetScript;
        // return display name or use target script name + Expert suffix
        public string DisplayName => displayName ?? (targetScript != null ? targetScript.name + " Expert" : "Unnamed Implementation");
        public string DescriptionOverride => descriptionOverride;
    }


}