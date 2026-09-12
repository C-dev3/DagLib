using System.Text.Json.Serialization;

namespace DagLib.Serialization;

/// <summary>
/// The JSON-serializable form of an edge, referencing its endpoints by
/// position rather than by value.
/// </summary>
/// <remarks>
/// <see cref="From"/> and <see cref="To"/> are indices into the accompanying
/// <see cref="GraphDto{T}.Nodes"/> array, so the same Guid never has to be
/// written out twice for a single edge. Edges carry no payload of their own,
/// which is why this type stays non-generic even though the graph is not.
/// </remarks>
/// <param name="From">Index into <see cref="GraphDto{T}.Nodes"/> identifying the edge's source node.</param>
/// <param name="To">Index into <see cref="GraphDto{T}.Nodes"/> identifying the edge's destination node.</param>
public sealed record EdgeDto(
    [property: JsonPropertyName("f")] int From,
    [property: JsonPropertyName("t")] int To);
