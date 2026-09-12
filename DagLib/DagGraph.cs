using DagLib.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace DagLib;

/// <summary>
/// Owns a set of nodes and the edges between them, enforcing the one
/// invariant that makes a graph a DAG: no path may ever loop back on itself.
/// </summary>
/// <typeparam name="T">The payload type carried by every node in the graph.</typeparam>
public sealed class DagGraph<T>
{
    private readonly Dictionary<Guid, Node<T>> _nodes = [];

    /// <summary>
    /// A read-only view over every node currently in the graph, keyed by ID.
    /// </summary>
    public IReadOnlyDictionary<Guid, Node<T>> Nodes => _nodes;

    /// <summary>
    /// Creates a brand-new node with a freshly generated ID and inserts it into the graph.
    /// </summary>
    /// <param name="data">The payload to attach to the new node.</param>
    /// <returns>The newly created node.</returns>
    public Node<T> AddNode(T data)
    {
        var node = new Node<T>(Guid.NewGuid(), data);
        _nodes[node.Id] = node;
        return node;
    }

    /// <summary>
    /// Inserts a node under a caller-supplied ID rather than generating one.
    /// </summary>
    /// <remarks>
    /// Intended for restoring nodes whose Guid was already assigned in a
    /// previous session, such as when deserializing a saved graph.
    /// </remarks>
    /// <param name="id">The ID to assign to the node.</param>
    /// <param name="data">The payload to attach to the new node.</param>
    /// <returns>The newly created node.</returns>
    /// <exception cref="ArgumentException">A node with <paramref name="id"/> is already in the graph.</exception>
    public Node<T> AddNode(Guid id, T data)
    {
        var node = new Node<T>(id, data);
        if (!_nodes.TryAdd(id, node))
            throw new ArgumentException($"A node with ID {id} already exists.", nameof(id));

        return node;
    }

    /// <summary>
    /// Connects two nodes with a directed edge, provided doing so wouldn't
    /// self-loop, duplicate an existing edge, or introduce a cycle.
    /// </summary>
    /// <param name="from">The edge's source node.</param>
    /// <param name="to">The edge's destination node.</param>
    /// <returns><c>true</c> if the edge was added; <c>false</c> if it was rejected.</returns>
    public bool AddEdge(Node<T> from, Node<T> to)
    {
        if (from.Id == to.Id)
            return false; // no self-loops

        if (from.Outputs.Any(n => n.Id == to.Id))
            return false; // edge already exists

        if (WouldCreateCycle(from, to))
            return false; // rejected: would create a cycle

        from.Outputs.Add(to);
        to.Inputs.Add(from);
        return true;
    }

    /// <summary>
    /// Disconnects two nodes by removing the edge between them, if one exists.
    /// </summary>
    /// <remarks>
    /// Meant to be wired up to UI events such as ImNodes' <c>IsLinkDestroyed</c>.
    /// </remarks>
    /// <param name="from">The edge's source node.</param>
    /// <param name="to">The edge's destination node.</param>
    public void RemoveEdge(Node<T> from, Node<T> to)
    {
        from.Outputs.RemoveAll(n => n.Id == to.Id);
        to.Inputs.RemoveAll(n => n.Id == from.Id);
    }

    /// <summary>
    /// Deletes a node from the graph, cleaning up every edge that referenced it.
    /// </summary>
    /// <param name="target">The node to delete.</param>
    public void RemoveNode(Node<T> target)
    {
        foreach (var predecessor in target.Inputs)
            predecessor.Outputs.RemoveAll(n => n.Id == target.Id);

        foreach (var successor in target.Outputs)
            successor.Inputs.RemoveAll(n => n.Id == target.Id);

        _nodes.Remove(target.Id);
    }

    /// <summary>
    /// Checks, before committing to it, whether adding an edge from
    /// <paramref name="from"/> to <paramref name="to"/> would close a cycle.
    /// </summary>
    /// <remarks>
    /// Implemented as a simple reachability search: the edge would cycle back
    /// exactly when <paramref name="to"/> can already reach <paramref name="from"/>.
    /// Cheap enough to call on every single link the user draws interactively.
    /// </remarks>
    /// <param name="from">The prospective edge's source node.</param>
    /// <param name="to">The prospective edge's destination node.</param>
    /// <returns><c>true</c> if adding the edge would create a cycle.</returns>
    public bool WouldCreateCycle(Node<T> from, Node<T> to)
    {
        var visited = new HashSet<Guid>();
        return CanReach(to, from, visited);
    }

    /// <summary>
    /// Depth-first search for a path from <paramref name="current"/> to <paramref name="target"/>.
    /// </summary>
    private static bool CanReach(Node<T> current, Node<T> target, HashSet<Guid> visited)
    {
        if (current.Id == target.Id)
            return true;

        if (!visited.Add(current.Id))
            return false;

        foreach (var next in current.Outputs)
        {
            if (CanReach(next, target, visited))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Scans the entire graph for a cycle in one pass, using the classic
    /// white/gray/black depth-first search.
    /// </summary>
    /// <remarks>
    /// Useful as a one-time sanity check — for example, right after loading a
    /// graph from disk — rather than for validating edges one at a time.
    /// </remarks>
    /// <param name="cyclePath">
    /// When a cycle is found, the sequence of nodes that make it up; otherwise <c>null</c>.
    /// </param>
    /// <returns><c>true</c> if the graph contains a cycle.</returns>
    public bool HasCycle(out IReadOnlyList<Node<T>>? cyclePath)
    {
        var state = new Dictionary<Guid, VisitState>();
        foreach (var node in _nodes.Values)
            state[node.Id] = VisitState.White;

        foreach (var node in _nodes.Values)
        {
            if (state[node.Id] != VisitState.White)
                continue;

            var path = new List<Node<T>>();
            if (Visit(node, state, path))
            {
                cyclePath = path;
                return true;
            }
        }

        cyclePath = null;
        return false;
    }

    /// <summary>
    /// Recursive step of the three-color DFS used by <see cref="HasCycle"/>;
    /// returns <c>true</c> the moment it walks back into a gray (in-progress) node.
    /// </summary>
    private static bool Visit(Node<T> node, Dictionary<Guid, VisitState> state, List<Node<T>> path)
    {
        state[node.Id] = VisitState.Gray;
        path.Add(node);

        foreach (var next in node.Outputs)
        {
            if (state[next.Id] == VisitState.Gray)
                return true;

            if (state[next.Id] == VisitState.White && Visit(next, state, path))
                return true;
        }

        state[node.Id] = VisitState.Black;
        path.RemoveAt(path.Count - 1);
        return false;
    }

    /// <summary>
    /// Marks a node's progress through the three-color DFS used by <see cref="HasCycle"/>.
    /// </summary>
    private enum VisitState
    {
        /// <summary>Not yet visited.</summary>
        White,

        /// <summary>Currently on the active search path — reaching one again means a cycle.</summary>
        Gray,

        /// <summary>Fully explored; no cycle can be found through this node.</summary>
        Black,
    }

    /// <summary>
    /// Orders every node so that each one appears only after everything it depends on.
    /// </summary>
    /// <remarks>
    /// Kahn's algorithm: repeatedly peel off nodes whose remaining in-degree
    /// (<see cref="Node{T}.Inputs"/> count) has dropped to zero.
    /// </remarks>
    /// <returns>All nodes in a valid dependency-respecting execution order.</returns>
    /// <exception cref="InvalidOperationException">The graph contains a cycle and has no valid ordering.</exception>
    public List<Node<T>> TopologicalSort()
    {
        var remaining = _nodes.Values.ToDictionary(n => n.Id, n => n.Inputs.Count);
        var queue = new Queue<Node<T>>(_nodes.Values.Where(n => remaining[n.Id] == 0));
        var result = new List<Node<T>>(_nodes.Count);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node);

            foreach (var next in node.Outputs)
            {
                remaining[next.Id]--;
                if (remaining[next.Id] == 0)
                    queue.Enqueue(next);
            }
        }

        if (result.Count != _nodes.Count)
            throw new InvalidOperationException("Cannot topologically sort a graph that contains a cycle.");

        return result;
    }

    /// <summary>
    /// Finds every node reachable by following edges forward from <paramref name="start"/>.
    /// </summary>
    /// <remarks>
    /// Answers "what would be affected if I changed this node?"
    /// </remarks>
    /// <param name="start">The node to search from.</param>
    /// <returns>The set of downstream nodes, excluding <paramref name="start"/> itself.</returns>
    public HashSet<Node<T>> GetDescendants(Node<T> start) => Traverse(start, n => n.Outputs);

    /// <summary>
    /// Finds every node reachable by following edges backward from <paramref name="start"/>.
    /// </summary>
    /// <remarks>
    /// Answers "what feeds into this node's result?"
    /// </remarks>
    /// <param name="start">The node to search from.</param>
    /// <returns>The set of upstream nodes, excluding <paramref name="start"/> itself.</returns>
    public HashSet<Node<T>> GetAncestors(Node<T> start) => Traverse(start, n => n.Inputs);

    /// <summary>
    /// Shared iterative graph walk backing both <see cref="GetDescendants"/> and
    /// <see cref="GetAncestors"/>; which direction it walks depends on <paramref name="next"/>.
    /// </summary>
    private static HashSet<Node<T>> Traverse(Node<T> start, Func<Node<T>, List<Node<T>>> next)
    {
        var visited = new HashSet<Guid>();
        var result = new HashSet<Node<T>>();
        var stack = new Stack<Node<T>>(next(start));

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node.Id))
                continue;

            result.Add(node);
            foreach (var n in next(node))
                stack.Push(n);
        }

        return result;
    }

    /// <summary>
    /// Looks up a node by its persistent identifier.
    /// </summary>
    /// <param name="id">The Guid of the node to find.</param>
    /// <returns>The node registered under <paramref name="id"/>.</returns>
    /// <exception cref="KeyNotFoundException">No node with <paramref name="id"/> exists in the graph.</exception>
    public Node<T> GetNode(Guid id) =>
    _nodes.TryGetValue(id, out var node)
        ? node
        : throw new KeyNotFoundException($"No node with ID {id}.");

    /// <summary>
    /// Looks up a node by its persistent identifier without throwing if it isn't found.
    /// </summary>
    /// <param name="id">The Guid of the node to find.</param>
    /// <param name="node">The matching node, or <c>null</c> if none exists.</param>
    /// <returns><c>true</c> if a node with <paramref name="id"/> was found.</returns>
    public bool TryGetNode(Guid id, [NotNullWhen(true)] out Node<T>? node) =>
        _nodes.TryGetValue(id, out node);

    /// <summary>
    /// Finds every node with no incoming edges — the graph's entry points.
    /// </summary>
    /// <returns>All nodes for which <see cref="Node{T}.IsRoot"/> is <c>true</c>.</returns>
    public IEnumerable<Node<T>> GetRoots() => _nodes.Values.Where(n => n.IsRoot);

    /// <summary>
    /// Finds every node with no outgoing edges — the graph's terminal points.
    /// </summary>
    /// <returns>All nodes for which <see cref="Node{T}.IsLeaf"/> is <c>true</c>.</returns>
    public IEnumerable<Node<T>> GetLeaves() => _nodes.Values.Where(n => n.IsLeaf);

    /// <summary>
    /// Enumerates every edge in the graph as source/destination node pairs.
    /// </summary>
    /// <remarks>
    /// Useful for debugging, exporting, or building custom views of the
    /// graph's structure without going through <see cref="DagLib.Serialization.GraphSerializer"/>.
    /// </remarks>
    /// <returns>Every edge, as a <c>(From, To)</c> tuple of the nodes it connects.</returns>
    public IEnumerable<(Node<T> From, Node<T> To)> GetEdges() =>
    _nodes.Values.SelectMany(n => n.Outputs.Select(to => (n, to)));

    /// <summary>
    /// The number of nodes currently in the graph.
    /// </summary>
    public int NodeCount => _nodes.Count;

    /// <summary>
    /// The number of edges currently in the graph.
    /// </summary>
    /// <remarks>
    /// Computed by summing each node's outgoing edge count, so this is an
    /// O(n) operation rather than a cached value.
    /// </remarks>
    public int EdgeCount => _nodes.Values.Sum(n => n.Outputs.Count);

    /// <summary>
    /// Checks whether an edge already exists from one node to another, without
    /// attempting to add or modify anything.
    /// </summary>
    /// <param name="from">The edge's source node.</param>
    /// <param name="to">The edge's destination node.</param>
    /// <returns><c>true</c> if an edge from <paramref name="from"/> to <paramref name="to"/> exists.</returns>
    public bool HasEdge(Node<T> from, Node<T> to) =>
    from.Outputs.Any(n => n.Id == to.Id);

    /// <summary>
    /// Creates an independent copy of the graph, preserving every node's Guid and every edge.
    /// </summary>
    /// <remarks>
    /// Useful for undo/redo or speculative edits: snapshot the graph, try a
    /// change, and fall back to the clone if it doesn't work out. Node
    /// payloads are copied by reference — if <typeparamref name="T"/> is a
    /// mutable reference type and a true deep copy is needed, the caller is
    /// responsible for cloning <see cref="Node{T}.Data"/> itself.
    /// </remarks>
    /// <returns>A new graph with the same nodes and edges as this one.</returns>
    public DagGraph<T> Clone()
    {
        var copy = new DagGraph<T>();
        foreach (var node in _nodes.Values)
            copy.AddNode(node.Id, node.Data);

        foreach (var node in _nodes.Values)
            foreach (var to in node.Outputs)
                copy.AddEdge(copy.GetNode(node.Id), copy.GetNode(to.Id));

        return copy;
    }

    /// <summary>
    /// Finds a single directed path from one node to another, if one exists.
    /// </summary>
    /// <remarks>
    /// Returns the first path found via depth-first search, not necessarily
    /// the shortest one — this library has no concept of edge weight.
    /// When multiple paths exist, which one comes back is unspecified.
    /// </remarks>
    /// <param name="from">The node to start the search from.</param>
    /// <param name="to">The node the path should end at.</param>
    /// <param name="predicate">
    /// An optional filter restricting which intermediate nodes the path may
    /// pass through (e.g. skipping disabled or muted nodes). <paramref name="from"/>
    /// and <paramref name="to"/> are exempt from this check even if the
    /// filter would otherwise exclude them.
    /// </param>
    /// <returns>
    /// The sequence of nodes from <paramref name="from"/> to <paramref name="to"/>
    /// inclusive, or <c>null</c> if no matching path exists.
    /// </returns>
    public IReadOnlyList<Node<T>>? GetPath(Node<T> from, Node<T> to, Func<Node<T>, bool>? predicate = null)
    {
        var path = new List<Node<T>> { from };
        return FindPath(from, to, predicate, path, new HashSet<Guid>()) ? path : null;
    }

    /// <summary>
    /// Recursive depth-first search backing <see cref="GetPath"/>; builds up
    /// <paramref name="path"/> as it descends and unwinds it on dead ends.
    /// </summary>
    private static bool FindPath(
        Node<T> current,
        Node<T> target,
        Func<Node<T>, bool>? predicate,
        List<Node<T>> path,
        HashSet<Guid> visited)
    {
        if (current.Id == target.Id)
            return true;

        if (!visited.Add(current.Id))
            return false;

        foreach (var next in current.Outputs)
        {
            // Intermediate nodes must satisfy the predicate; from/to are exempt
            // because the caller explicitly asked for a path between them.
            if (next.Id != target.Id && predicate is not null && !predicate(next))
                continue;

            path.Add(next);
            if (FindPath(next, target, predicate, path, visited))
                return true;
            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    /// <summary>
    /// Merges the nodes and edges from a <see cref="GraphDto{T}"/> into this graph,
    /// assigning each imported node a fresh Guid rather than reusing the ones it was saved with.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="GraphSerializer.FromDto{T}"/>, which rebuilds a
    /// standalone graph from scratch, this adds to an already-populated
    /// graph — intended for copy/paste or combining two saved graphs where
    /// the original Guids could otherwise collide with existing nodes.
    /// The source data is assumed to be acyclic already; this method does
    /// not re-run a cycle check after importing.
    /// </remarks>
    /// <param name="dto">The graph data to import.</param>
    /// <returns>The newly created nodes, in the same order as <see cref="GraphDto{T}.Nodes"/>.</returns>
    public IReadOnlyList<Node<T>> Import(GraphDto<T> dto)
    {
        var idMap = new Dictionary<Guid, Guid>(dto.Nodes.Count);
        var imported = new List<Node<T>>(dto.Nodes.Count);

        foreach (var nodeDto in dto.Nodes)
        {
            var newId = Guid.NewGuid();
            idMap[nodeDto.Id] = newId;
            imported.Add(AddNode(newId, nodeDto.Data));
        }

        foreach (var edge in dto.Edges)
        {
            var from = GetNode(idMap[dto.Nodes[edge.From].Id]);
            var to = GetNode(idMap[dto.Nodes[edge.To].Id]);
            AddEdge(from, to);
        }

        return imported;
    }
}
