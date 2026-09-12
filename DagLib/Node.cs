namespace DagLib;

/// <summary>
/// A single vertex in a directed acyclic graph, carrying an arbitrary
/// application-defined payload alongside its graph-structural links.
/// </summary>
/// <remarks>
/// <typeparamref name="T"/> is left open so the same node type can back very
/// different graphs — a plain label string, a record with position and color
/// for a visual editor, a delegate representing a unit of work, and so on.
/// </remarks>
/// <typeparam name="T">The type of payload attached to the node.</typeparam>
public sealed class Node<T>(Guid id, T data)
{
    /// <summary>
    /// A stable identifier for the node that remains valid across saves,
    /// reloads, and merges from collaborating editors.
    /// </summary>
    public Guid Id { get; } = id;

    /// <summary>
    /// The payload this node carries, entirely separate from its position in
    /// the graph — a name, coordinates, a callback, or whatever else the
    /// application needs to associate with the node.
    /// </summary>
    public T Data { get; set; } = data;

    /// <summary>
    /// The nodes this one points to.
    /// </summary>
    /// <remarks>
    /// Walk this list when traversing the graph forward, e.g. for topological
    /// sorts or descendant queries. Treat it as read-only from the outside;
    /// go through <see cref="DagGraph{T}.AddEdge"/> and friends to change it,
    /// so the corresponding <see cref="Inputs"/> entry stays in sync.
    /// </remarks>
    public List<Node<T>> Outputs { get; } = [];

    /// <summary>
    /// The nodes that point to this one.
    /// </summary>
    /// <remarks>
    /// Walk this list when traversing the graph backward, e.g. for ancestor
    /// queries or cleaning up references when a node is deleted. Treat it as
    /// read-only from the outside; go through <see cref="DagGraph{T}.AddEdge"/>
    /// and friends to change it, so the corresponding <see cref="Outputs"/>
    /// entry stays in sync.
    /// </remarks>
    public List<Node<T>> Inputs { get; } = [];

    /// <summary>
    /// Whether this node has no dependencies of its own, i.e. no incoming edges.
    /// </summary>
    public bool IsRoot => Inputs.Count == 0;

    /// <summary>
    /// Whether nothing depends on this node, i.e. it has no outgoing edges.
    /// </summary>
    public bool IsLeaf => Outputs.Count == 0;

    public override string ToString() => $"{Data} ({Id})";
}
