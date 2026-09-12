using System.Text.Json.Serialization;

namespace DagLib.Serialization;

/// <summary>
/// The on-disk (or on-wire) shape of an entire <see cref="DagGraph{T}"/>, flattened for JSON.
/// </summary>
/// <remarks>
/// Deliberately asymmetric compared to the live graph: only the outgoing
/// direction of each edge is stored, and the corresponding
/// <see cref="Node{T}.Inputs"/> back-references are rebuilt on load rather
/// than serialized redundantly.
/// </remarks>
/// <typeparam name="T">The payload type carried by each node.</typeparam>
/// <param name="Nodes">Every node in the graph.</param>
/// <param name="Edges">Every edge in the graph, expressed as index pairs into <see cref="Nodes"/>.</param>
public sealed record GraphDto<T>(
    [property: JsonPropertyName("nodes")] List<NodeDto<T>> Nodes,
    [property: JsonPropertyName("edges")] List<EdgeDto> Edges);
