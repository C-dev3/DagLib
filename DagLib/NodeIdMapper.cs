namespace DagLib;

/// <summary>
/// Bridges persistent <see cref="Guid"/>-based node identity with the transient
/// integer IDs expected by int-keyed UI libraries like ImNodes.
/// </summary>
/// <remarks>
/// IDs are handed out as an incrementing counter and cached in a bidirectional
/// lookup for the lifetime of the session; nothing about the original Guid is
/// discarded or derived, it's simply given a throwaway integer alias.
/// Because only a Guid is required, this class works independently of the
/// generic parameter used by <see cref="Node{T}"/>.
/// </remarks>
public sealed class NodeIdMapper
{
    private readonly Dictionary<Guid, int> _guidToInt = [];
    private readonly Dictionary<int, Guid> _intToGuid = [];
    private int _nextId = 1;

    /// <summary>
    /// Resolves the ImNodes-compatible integer ID for a given node, allocating
    /// a fresh one on first use and reusing it on every subsequent call.
    /// </summary>
    /// <param name="guid">The persistent identifier of the node.</param>
    /// <returns>An integer ID suitable for passing into ImNodes.</returns>
    public int GetOrCreateImNodesId(Guid guid)
    {
        if (_guidToInt.TryGetValue(guid, out var existingId))
            return existingId;

        var newId = _nextId++;
        _guidToInt[guid] = newId;
        _intToGuid[newId] = guid;
        return newId;
    }

    /// <summary>
    /// Reverses the mapping, recovering the node's <see cref="Guid"/> from an
    /// integer ID handed back by ImNodes.
    /// </summary>
    /// <remarks>
    /// Typically called from callbacks such as <c>IsLinkCreated</c>, where
    /// ImNodes only knows about integer IDs and the original node must be
    /// looked up before the event can be acted on.
    /// </remarks>
    /// <param name="imNodesId">An integer ID previously returned by <see cref="GetOrCreateImNodesId"/>.</param>
    /// <returns>The node's persistent Guid.</returns>
    /// <exception cref="KeyNotFoundException">No node is registered under <paramref name="imNodesId"/>.</exception>
    public Guid GetGuid(int imNodesId) => _intToGuid[imNodesId];

    /// <summary>
    /// Looks up the Guid for an ImNodes int ID without throwing if it isn't found.
    /// </summary>
    /// <param name="imNodesId">An integer ID previously returned by <see cref="GetOrCreateImNodesId"/>.</param>
    /// <param name="guid">The matching Guid, or <see cref="Guid.Empty"/> if none exists.</param>
    /// <returns><c>true</c> if a node is registered under <paramref name="imNodesId"/>.</returns>
    public bool TryGetGuid(int imNodesId, out Guid guid) =>
    _intToGuid.TryGetValue(imNodesId, out guid);

    /// <summary>
    /// Drops a node's entry from both lookup directions once it has been deleted.
    /// </summary>
    /// <remarks>
    /// Skipping this call leaks entries indefinitely, since integer IDs are
    /// never otherwise recycled.
    /// </remarks>
    /// <param name="guid">The persistent identifier of the node to forget.</param>
    public void Remove(Guid guid)
    {
        if (_guidToInt.TryGetValue(guid, out var imNodesId))
        {
            _guidToInt.Remove(guid);
            _intToGuid.Remove(imNodesId);
        }
    }

    /// <summary>
    /// Discards every registered mapping and resets ID allocation back to the start.
    /// </summary>
    /// <remarks>
    /// Call this when reusing the same <see cref="NodeIdMapper"/> instance
    /// across an unrelated graph — for example, after loading a new file —
    /// so stale entries don't linger and <c>int</c> IDs don't grow unbounded.
    /// </remarks>
    public void Clear()
    {
        _guidToInt.Clear();
        _intToGuid.Clear();
        _nextId = 1;
    }
}
