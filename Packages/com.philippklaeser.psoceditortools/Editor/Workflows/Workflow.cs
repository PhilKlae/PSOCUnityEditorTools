using System;
using System.Collections.Generic;
using UnityEngine;

namespace Packages.PSOC.Workflows
{
    /// <summary>
    /// Workflows consist of multiple agents that work together to achieve a common goal and hand eachother control and context
    /// Workflows are used to create complex behaviors and interactions between agents.
    /// <\summary>
    [CreateAssetMenu(fileName = "Workflow", menuName = "PSOC/Workflow")]
    public class Workflow : PSOCQueryable
    {
        /*
    class AgentWorkflow(DbModel):
        """
        Multiple agents can be chained together to form a workflow, and hand eachother control to achieve a goal.
        """
        _collection: ClassVar = "Workflows"
        name: str = Field(None, min_length=3, max_length=50)
        description: Optional[str] = None
        agents: List[AgentCreate | Id] = Field(..., description="List of agents to be used in the workflow")
        root_agent: AgentCreate | Id = Field(..., description="The root agent of the workflow")
     */
        public List<AgentBase> Agents;
        public AgentBase RootAgent;
        public string Description;
        public string Name;
        public string workflowId;
        public DateTime lastUpdated;
        public string status;

        public string GetPromptText(string queryText)
        {
            return queryText;
        }
    }
}
