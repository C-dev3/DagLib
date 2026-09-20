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
/// <para>
/// This view caches the (node, ImNodes-id) and (from-id, to-id) pairs it
/// hands out, and only rebuilds them when it knows the graph's structure
/// changed. Structural changes made through this view's own methods
/// (<see cref="AddNode(T)"/>, <see cref="AddEdge"/>, <see cref="RemoveEdge"/>,
/// <see cref="RemoveNode"/>) are tracked automatically. If <see cref="Graph"/>
/// is mutated directly instead — e.g. calling <c>Graph.AddNode(...)</c> or
/// <c>Graph.AddEdge(...)</c> — call <see cref="MarkDirty"/> afterwards, or the
/// cached results will silently go stale.
/// </para>
/// </remarks>
/// <typeparam name="T">The payload type carried by each node.</typeparam>
public sealed class ImNodesGraphView<T>(DagGraph<T> graph, NodeIdMapper mapper)
{
    private readonly List<(Node<T> Node, int ImNodesId)> _nodeCache = [];
    private readonly List<(int FromImNodesId, int ToImNodesId)> _edgeCache = [];
    private readonly List<(Node<T> Node, int ImNodesId)> _renderBuffer = [];

    private bool _nodeCacheDirty = true;
    private bool _edgeCacheDirty = true;

    public DagGraph<T> Graph { get; } = graph;
    public NodeIdMapper Mapper { get; } = mapper;

    /// <summary>
    /// Starts with a brand-new, empty <see cref="DagGraph{T}"/> and a fresh
    /// <see cref="NodeIdMapper"/>.
    /// </summary>
    public ImNodesGraphView() : this(new DagGraph<T>(), new NodeIdMapper()) { }

    /// <summary>
    /// Wraps an existing <see cref="DagGraph{T}"/> while starting with a fresh
    /// <see cref="NodeIdMapper"/>. Useful when reopening a previously loaded
    /// graph in a new UI session, where the node data should carry over but
    /// the ImNodes int-ID assignments should start clean.
    /// </summary>
    /// <param name="graph">The existing graph to wrap.</param>
    public ImNodesGraphView(DagGraph<T> graph) : this(graph, new NodeIdMapper()) { }

    /// <summary>
    /// Forces both the node cache and the edge cache to be rebuilt on next
    /// access. Call this after mutating <see cref="Graph"/> directly, bypassing
    /// this view's own <c>Add</c>/<c>Remove</c> methods.
    /// </summary>
    public void MarkDirty()
    {
        _nodeCacheDirty = true;
        _edgeCacheDirty = true;
    }

    /// <summary>
    /// Creates a new node and adds it to <see cref="Graph"/>, invalidating the
    /// node cache so the next render pass picks it up.
    /// </summary>
    /// <param name="data">The payload to store on the new node.</param>
    /// <returns>The newly created node.</returns>
    public Node<T> AddNode(T data)
    {
        var node = Graph.AddNode(data);
        _nodeCacheDirty = true;
        return node;
    }

    /// <summary>
    /// Restores a node with a previously known ID and adds it to
    /// <see cref="Graph"/>, invalidating the node cache so the next render
    /// pass picks it up. Intended for restoring a graph loaded from storage.
    /// </summary>
    /// <param name="id">The node's previously assigned ID.</param>
    /// <param name="data">The payload to store on the node.</param>
    /// <returns>The newly created node.</returns>
    public Node<T> AddNode(Guid id, T data)
    {
        var node = Graph.AddNode(id, data);
        _nodeCacheDirty = true;
        return node;
    }

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

        if (!Graph.AddEdge(from, to))
            return false;

        _edgeCacheDirty = true;
        return true;
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
            _edgeCacheDirty = true;
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
            Graph.RemoveNode(node); // also detaches every edge touching this node

        Mapper.Remove(guid);

        // Removing a node can also remove edges attached to it, so both caches
        // must be rebuilt, not just the node cache.
        _nodeCacheDirty = true;
        _edgeCacheDirty = true;
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
    /// Intended for driving a per-frame render loop. The result is cached and
    /// only rebuilt when the node set has actually changed, so calling this
    /// every frame is cheap on frames where nothing was added or removed.
    /// The returned list is reused internally — don't hold onto it past the
    /// next call that might invalidate the cache.
    /// </remarks>
    /// <returns>A list of <c>(Node, ImNodesId)</c> pairs, one for every node in the graph.</returns>
    public IReadOnlyList<(Node<T> Node, int ImNodesId)> GetAllImNodesIds()
    {
        RebuildNodeCacheIfDirty();
        return _nodeCache;
    }

    /// <summary>
    /// Gets a filtered and ordered subset of nodes paired with their ImNodes
    /// int IDs, for render loops that need to skip or reorder certain nodes.
    /// </summary>
    /// <remarks>
    /// The underlying (unfiltered) node list is cached the same way as
    /// <see cref="GetAllImNodesIds"/>, so applying a predicate or ordering
    /// here does not re-resolve any ImNodes IDs — it only filters/sorts the
    /// already-cached pairs into a reusable scratch buffer. The predicate and
    /// sort themselves still run on every call (they can differ each time),
    /// so this is not free, just cheaper than rebuilding the ID mapping from scratch.
    /// The returned list is reused internally — don't hold onto it past the next call.
    /// </remarks>
    /// <typeparam name="TKey">
    /// The sort key type. Constrained to <see cref="IComparable{TKey}"/> so
    /// value-type keys (e.g. <see langword="float"/>) don't get boxed.
    /// </typeparam>
    /// <param name="predicate">
    /// Optional filter selecting which nodes to include. When omitted, every node is included.
    /// </param>
    /// <param name="orderBy">
    /// Optional key selector used to sort the result (e.g. drawing selected
    /// nodes last so they render on top). When omitted, nodes are returned in
    /// the graph's internal order.
    /// </param>
    /// <returns>A list of <c>(Node, ImNodesId)</c> pairs for the matching nodes.</returns>
    public IReadOnlyList<(Node<T> Node, int ImNodesId)> GetRenderableNodes<TKey>(
        Func<Node<T>, bool>? predicate = null,
        Func<Node<T>, TKey>? orderBy = null)
        where TKey : IComparable<TKey>
    {
        RebuildNodeCacheIfDirty();

        _renderBuffer.Clear();
        foreach (var pair in _nodeCache)
        {
            if (predicate is null || predicate(pair.Node))
                _renderBuffer.Add(pair);
        }

        if (orderBy is not null)
            _renderBuffer.Sort((a, b) => orderBy(a.Node).CompareTo(orderBy(b.Node)));

        return _renderBuffer;
    }

    /// <summary>
    /// Gets every edge in the graph as a pair of ImNodes int IDs, ready to pass
    /// into <c>ImNodes.Link</c>.
    /// </summary>
    /// <remarks>
    /// Intended for driving a per-frame render loop. The result is cached and
    /// only rebuilt when an edge was added or removed (or a node carrying
    /// edges was removed), so calling this every frame is cheap on frames
    /// where the connections haven't changed. The returned list is reused
    /// internally — don't hold onto it past the next call that might
    /// invalidate the cache.
    /// </remarks>
    /// <returns>A list of <c>(FromImNodesId, ToImNodesId)</c> pairs, one for every edge.</returns>
    public IReadOnlyList<(int FromImNodesId, int ToImNodesId)> GetRenderableEdges()
    {
        if (!_edgeCacheDirty)
            return _edgeCache;

        _edgeCache.Clear();
        foreach (var (From, To) in Graph.GetEdges())
        {
            _edgeCache.Add((
                Mapper.GetOrCreateImNodesId(From.Id),
                Mapper.GetOrCreateImNodesId(To.Id)));
        }

        _edgeCacheDirty = false;
        return _edgeCache;
    }

    private void RebuildNodeCacheIfDirty()
    {
        if (!_nodeCacheDirty)
            return;

        _nodeCache.Clear();
        foreach (var node in Graph.Nodes.Values)
            _nodeCache.Add((node, Mapper.GetOrCreateImNodesId(node.Id)));

        _nodeCacheDirty = false;
    }
}