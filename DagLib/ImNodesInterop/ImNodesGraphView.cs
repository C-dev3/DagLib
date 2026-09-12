using System.Diagnostics.CodeAnalysis;

namespace DagLib.ImNodesInterop;

/// <summary>
/// A thin facade combining a <see cref="DagGraph{T}"/> with a
/// <see cref="NodeIdMapper"/>, providing int-ID based operations for
/// wiring up ImNodes-style UI events without duplicating glue code at every call site.
/// </summary>
/// <remarks>
/// This does not own either component — both are expected to live and be
/// managed independently. Use this only for the ImNodes-facing operations;
/// prefer the underlying <see cref="Graph"/> directly for everything else.
/// </remarks>
/// <typeparam name="T">The payload type carried by each node.</typeparam>
public sealed class ImNodesGraphView<T>(DagGraph<T> graph, NodeIdMapper mapper)
{
    public DagGraph<T> Graph { get; } = graph;
    public NodeIdMapper Mapper { get; } = mapper;

    /// <summary>
    /// Connects two nodes identified by their ImNodes int IDs, as when
    /// handling an <c>IsLinkCreated</c> event.
    /// </summary>
    public bool AddEdge(int fromImNodesId, int toImNodesId)
    {
        if (!Mapper.TryGetGuid(fromImNodesId, out var fromGuid) ||
            !Mapper.TryGetGuid(toImNodesId, out var toGuid))
            return false;

        if (!Graph.TryGetNode(fromGuid, out var from) ||
            !Graph.TryGetNode(toGuid, out var to))
            return false;

        return Graph.AddEdge(from, to);
    }

    /// <summary>
    /// Disconnects two nodes identified by their ImNodes int IDs, as when
    /// handling an <c>IsLinkDestroyed</c> event.
    /// </summary>
    public void RemoveEdge(int fromImNodesId, int toImNodesId)
    {
        if (Mapper.TryGetGuid(fromImNodesId, out var fromGuid) &&
            Mapper.TryGetGuid(toImNodesId, out var toGuid) &&
            Graph.TryGetNode(fromGuid, out var from) &&
            Graph.TryGetNode(toGuid, out var to))
        {
            Graph.RemoveEdge(from, to);
        }
    }

    /// <summary>
    /// Removes a node identified by its ImNodes int ID, cleaning up both the
    /// graph and the ID mapping in one call.
    /// </summary>
    public void RemoveNode(int imNodesId)
    {
        if (!Mapper.TryGetGuid(imNodesId, out var guid))
            return;

        if (Graph.TryGetNode(guid, out var node))
            Graph.RemoveNode(node);

        Mapper.Remove(guid);
    }

    /// <summary>
    /// Looks up the node corresponding to an ImNodes int ID in one step,
    /// bridging the mapper and the graph.
    /// </summary>
    /// <param name="imNodesId">An integer ID previously returned by <see cref="NodeIdMapper.GetOrCreateImNodesId"/>.</param>
    /// <param name="node">The matching node, or <c>null</c> if the ID isn't registered or the node no longer exists in the graph.</param>
    /// <returns><c>true</c> if a node was found.</returns>
    public bool TryGetNode(int imNodesId, [NotNullWhen(true)] out Node<T>? node)
    {
        node = null;
        return Mapper.TryGetGuid(imNodesId, out var guid) && Graph.TryGetNode(guid, out node);
    }

    /// <summary>
    /// Gets (or lazily assigns) the ImNodes int ID for a given node.
    /// </summary>
    /// <param name="node">The node to look up.</param>
    /// <returns>The int ID to pass into ImNodes.</returns>
    public int GetImNodesId(Node<T> node) => Mapper.GetOrCreateImNodesId(node.Id);

    /// <summary>
    /// Gets the ImNodes int ID for every node currently in the graph,
    /// assigning new IDs to any node that doesn't have one yet.
    /// </summary>
    /// <remarks>
    /// Intended for driving a per-frame render loop, where every node needs
    /// its int ID resolved before calling into ImNodes.
    /// </remarks>
    /// <returns>A sequence of <c>(Node, ImNodesId)</c> pairs, one for every node in the graph.</returns>
    public IEnumerable<(Node<T> Node, int ImNodesId)> GetAllImNodesIds() =>
        Graph.Nodes.Values.Select(n => (n, Mapper.GetOrCreateImNodesId(n.Id)));

    /// <summary>
    /// Gets a filtered and ordered subset of nodes paired with their ImNodes
    /// int IDs, for render loops that need to skip or reorder certain nodes.
    /// </summary>
    /// <param name="predicate">
    /// Optional filter selecting which nodes to include. When omitted, every node is included.
    /// </param>
    /// <param name="orderBy">
    /// Optional key used to sort the result (e.g. drawing selected nodes last
    /// so they render on top). When omitted, nodes are returned in the graph's internal order.
    /// </param>
    /// <returns>A sequence of <c>(Node, ImNodesId)</c> pairs for the matching nodes.</returns>
    public IEnumerable<(Node<T> Node, int ImNodesId)> GetRenderableNodes(
        Func<Node<T>, bool>? predicate = null,
        Func<Node<T>, IComparable>? orderBy = null)
    {
        var nodes = predicate is null
            ? Graph.Nodes.Values.AsEnumerable()
            : Graph.Nodes.Values.Where(predicate);

        if (orderBy is not null)
            nodes = nodes.OrderBy(orderBy);

        return nodes.Select(n => (n, Mapper.GetOrCreateImNodesId(n.Id)));
    }

    /// <summary>
    /// Gets every edge in the graph as a pair of ImNodes int IDs, ready to pass
    /// into <c>ImNodes.Link</c>.
    /// </summary>
    /// <returns>A sequence of <c>(FromImNodesId, ToImNodesId)</c> pairs, one for every edge.</returns>
    public IEnumerable<(int FromImNodesId, int ToImNodesId)> GetRenderableEdges() =>
        Graph.GetEdges().Select(e => (
            Mapper.GetOrCreateImNodesId(e.From.Id),
            Mapper.GetOrCreateImNodesId(e.To.Id)));
}