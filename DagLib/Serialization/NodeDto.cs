using System.Text.Json.Serialization;

namespace DagLib.Serialization;

/// <summary>
/// The JSON-serializable form of a single node: its identity plus whatever
/// payload it carries.
/// </summary>
/// <typeparam name="T">The type of payload attached to the node.</typeparam>
/// <param name="Id">The node's persistent Guid.</param>
/// <param name="Data">The node's payload.</param>
public sealed record NodeDto<T>(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("data")] T Data);
