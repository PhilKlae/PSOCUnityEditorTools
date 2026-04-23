using System;
using System.Collections.Generic;
using System.Linq;

namespace Packages.PSOC.Workflows.Graph
{
    /// <summary>
    /// Represents a simple directed graph node for reference tracking.
    /// A node represents a referencable class.
    /// </summary>
    public class GraphNode
    {
        public string ClassName { get; set; }

        public GraphNode(string className)
        {
            ClassName = className;
        }

        public override bool Equals(object obj)
        {
            return obj is GraphNode node && ClassName == node.ClassName;
        }

        public override int GetHashCode()
        {
            return ClassName?.GetHashCode() ?? 0;
        }

        public override string ToString()
        {
            return ClassName;
        }
    }

    /// <summary>
    /// Represents a directed edge in the reference graph.
    /// An edge goes from a source class to a target class through a specific field.
    /// </summary>
    public class ReferenceGraphEdge
    {
        /// <summary>
        /// The class that contains the reference field.
        /// </summary>
        public GraphNode SourceNode { get; set; }

        /// <summary>
        /// The class that can be referenced by the field.
        /// </summary>
        public GraphNode TargetNode { get; set; }

        /// <summary>
        /// The name of the field that creates this reference.
        /// </summary>
        public string FieldName { get; set; }

        /// <summary>
        /// The fully qualified name of the target type as specified in the FieldRefAttribute.
        /// This represents which specific type (among possibly multiple) is being referenced.
        /// </summary>
        public string ReferencedTypeName { get; set; }

        public ReferenceGraphEdge(GraphNode sourceNode, GraphNode targetNode, string fieldName, string referencedTypeName)
        {
            SourceNode = sourceNode ?? throw new ArgumentNullException(nameof(sourceNode));
            TargetNode = targetNode ?? throw new ArgumentNullException(nameof(targetNode));
            FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
            ReferencedTypeName = referencedTypeName ?? throw new ArgumentNullException(nameof(referencedTypeName));
        }

        public override bool Equals(object obj)
        {
            return obj is ReferenceGraphEdge edge &&
                   Equals(SourceNode, edge.SourceNode) &&
                   Equals(TargetNode, edge.TargetNode) &&
                   FieldName == edge.FieldName &&
                   ReferencedTypeName == edge.ReferencedTypeName;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SourceNode, TargetNode, FieldName, ReferencedTypeName);
        }

        public override string ToString()
        {
            return $"{SourceNode.ClassName}.{FieldName} -> {TargetNode.ClassName} (type: {ReferencedTypeName})";
        }
    }

    /// <summary>
    /// A simple directed graph for tracking references between classes.
    /// Each edge represents a possible reference from one class to another through a specific field.
    /// If a field can reference multiple types, there will be multiple edges for that field.
    /// </summary>
    public class ReferenceGraph
    {
        private readonly HashSet<GraphNode> _nodes = new HashSet<GraphNode>();
        private readonly List<ReferenceGraphEdge> _edges = new List<ReferenceGraphEdge>();

        /// <summary>
        /// Gets a read-only view of all nodes in the graph.
        /// </summary>
        public IReadOnlyList<GraphNode> Nodes => _nodes.ToList();

        /// <summary>
        /// Gets a read-only view of all edges in the graph.
        /// </summary>
        public IReadOnlyList<ReferenceGraphEdge> Edges => _edges.AsReadOnly();

        /// <summary>
        /// Adds a node to the graph if it doesn't already exist.
        /// </summary>
        public GraphNode AddNode(string className)
        {
            var node = new GraphNode(className);
            _nodes.Add(node);
            return node;
        }

        /// <summary>
        /// Gets or creates a node with the specified class name.
        /// </summary>
        public GraphNode GetOrAddNode(string className)
        {
            var existing = _nodes.FirstOrDefault(n => n.ClassName == className);
            if (existing != null)
                return existing;

            return AddNode(className);
        }

        /// <summary>
        /// Adds an edge to the graph.
        /// </summary>
        public void AddEdge(GraphNode sourceNode, GraphNode targetNode, string fieldName, string referencedTypeName)
        {
            if (sourceNode == null) throw new ArgumentNullException(nameof(sourceNode));
            if (targetNode == null) throw new ArgumentNullException(nameof(targetNode));

            var edge = new ReferenceGraphEdge(sourceNode, targetNode, fieldName, referencedTypeName);
            _edges.Add(edge);
        }

        /// <summary>
        /// Gets all outgoing edges from a source node.
        /// </summary>
        public IEnumerable<ReferenceGraphEdge> GetOutgoingEdges(GraphNode node)
        {
            return _edges.Where(e => e.SourceNode.Equals(node));
        }

        /// <summary>
        /// Gets all incoming edges to a target node.
        /// </summary>
        public IEnumerable<ReferenceGraphEdge> GetIncomingEdges(GraphNode node)
        {
            return _edges.Where(e => e.TargetNode.Equals(node));
        }

        /// <summary>
        /// Derives a simplified graph where duplicates are removed and each edge
        /// only represents a possible reference type without field-level details.
        /// </summary>
        public Dictionary<string, List<string>> DeriveSimplifiedGraph()
        {
            var dict = new Dictionary<string, List<string>>();

            foreach (var node in _nodes)
            {
                var targetTypes = new HashSet<string>(StringComparer.Ordinal);
                var outgoingEdges = GetOutgoingEdges(node);

                foreach (var edge in outgoingEdges)
                {
                    targetTypes.Add(edge.TargetNode.ClassName);
                }

                dict[node.ClassName] = targetTypes.OrderBy(t => t).ToList();
            }

            return dict;
        }

        /// <summary>
        /// Gets all edges for a specific field name across all nodes.
        /// </summary>
        public IEnumerable<ReferenceGraphEdge> GetEdgesForField(string fieldName)
        {
            return _edges.Where(e => e.FieldName == fieldName);
        }

        /// <summary>
        /// Prints the graph as a tree structure, expanding each node only once to avoid duplication.
        /// Returns the tree representation as a string.
        /// </summary>
        public string PrintAsTree()
        {
            var output = new System.Text.StringBuilder();
            var expandedNodes = new HashSet<string>();

            foreach (var node in _nodes.OrderBy(n => n.ClassName))
            {
                PrintNodeAsTree(node, output, expandedNodes, "", isLastChild: true);
                output.AppendLine();
            }

            return output.ToString();
        }

        /// <summary>
        /// Recursively prints a node and its children as a tree.
        /// </summary>
        private void PrintNodeAsTree(GraphNode node, System.Text.StringBuilder output, 
            HashSet<string> expandedNodes, string indent, bool isLastChild)
        {
            // Print the current node
            string prefix = isLastChild ? "└── " : "├── ";
            output.Append(indent + prefix + node.ClassName);

            // Check if this node has already been expanded
            if (expandedNodes.Contains(node.ClassName))
            {
                output.AppendLine(" (already expanded)");
                return;
            }

            output.AppendLine();
            expandedNodes.Add(node.ClassName);

            // Get outgoing edges from this node
            var outgoingEdges = GetOutgoingEdges(node).OrderBy(e => e.TargetNode.ClassName).ToList();

            // Print each child
            for (int i = 0; i < outgoingEdges.Count; i++)
            {
                var edge = outgoingEdges[i];
                bool isLast = i == outgoingEdges.Count - 1;
                
                // Build the new indent for children
                string newIndent = indent + (isLastChild ? "    " : "│   ");
                
                PrintNodeAsTree(edge.TargetNode, output, expandedNodes, newIndent, isLast);
            }
        }

        /// <summary>
        /// Clears all nodes and edges from the graph.
        /// </summary>
        public void Clear()
        {
            _nodes.Clear();
            _edges.Clear();
        }

        public override string ToString()
        {
            return $"ReferenceGraph(Nodes: {_nodes.Count}, Edges: {_edges.Count})";
        }
    }
}
