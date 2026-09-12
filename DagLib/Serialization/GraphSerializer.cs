using System.IO.Compression;
using System.Text.Json;

namespace DagLib.Serialization;

/// <summary>
/// Converts between a live <see cref="DagGraph{T}"/> and its flattened JSON
/// form (<see cref="GraphDto{T}"/>), and handles reading and writing that
/// JSON to disk.
/// </summary>
/// <remarks>
/// The class itself isn't generic; each method takes its own <c>T</c> type
/// parameter instead, so callers get type inference for free rather than
/// having to write <c>GraphSerializer&lt;MyType&gt;</c> everywhere.
/// </remarks>
public static class GraphSerializer
{
    /// <summary>
    /// Serializer settings used whenever a caller doesn't supply their own.
    /// </summary>
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = false, // keep output compact; no need for human-readable whitespace
        MaxDepth = 4096, // default is 64; raised so deeply-nested graphs don't hit a spurious depth exception
    };

    /// <summary>
    /// Flattens a live graph into the index-based <see cref="GraphDto{T}"/> shape used for storage.
    /// </summary>
    /// <typeparam name="T">The payload type carried by each node.</typeparam>
    /// <param name="graph">The graph to flatten.</param>
    /// <returns>A DTO ready to be serialized to JSON.</returns>
    public static GraphDto<T> ToDto<T>(DagGraph<T> graph)
    {
        var nodeList = graph.Nodes.Values.ToList();

        var indexOf = new Dictionary<Guid, int>(nodeList.Count);
        for (int i = 0; i < nodeList.Count; i++)
            indexOf[nodeList[i].Id] = i;

        var nodeDtos = nodeList
            .Select(n => new NodeDto<T>(n.Id, n.Data))
            .ToList();

        var edgeDtos = nodeList
            .SelectMany(n => n.Outputs.Select(to => new EdgeDto(indexOf[n.Id], indexOf[to.Id])))
            .ToList();

        return new GraphDto<T>(nodeDtos, edgeDtos);
    }

    /// <summary>
    /// Rebuilds a live graph from its flattened DTO form.
    /// </summary>
    /// <remarks>
    /// Runs in two passes — every node is created first, then edges are
    /// wired up against the now-complete node set — followed by a single
    /// whole-graph cycle check so corrupt or hand-edited data can't sneak in a loop.
    /// </remarks>
    /// <typeparam name="T">The payload type carried by each node.</typeparam>
    /// <param name="dto">The DTO to rebuild the graph from.</param>
    /// <returns>The reconstructed graph.</returns>
    /// <exception cref="InvalidDataException">
    /// The reconstructed graph contains a cycle, or an edge refers to an out-of-range node index.
    /// </exception>
    public static DagGraph<T> FromDto<T>(GraphDto<T> dto)
    {
        var graph = new DagGraph<T>();

        var nodesByIndex = new Node<T>[dto.Nodes.Count];
        for (int i = 0; i < dto.Nodes.Count; i++)
        {
            var nodeDto = dto.Nodes[i];
            nodesByIndex[i] = graph.AddNode(nodeDto.Id, nodeDto.Data);
        }

        foreach (var edge in dto.Edges)
        {
            if (edge.From < 0 || edge.From >= nodesByIndex.Length ||
                edge.To < 0 || edge.To >= nodesByIndex.Length)
            {
                throw new InvalidDataException(
                    $"Edge references an out-of-range index (From={edge.From}, To={edge.To}).");
            }

            var from = nodesByIndex[edge.From];
            var to = nodesByIndex[edge.To];

            from.Outputs.Add(to);
            to.Inputs.Add(from);
        }

        if (graph.HasCycle(out var cyclePath))
        {
            var pathText = string.Join(" -> ", cyclePath!.Select(n => n.ToString()));
            throw new InvalidDataException($"Loaded data contains a cycle: {pathText}");
        }

        return graph;
    }

    /// <summary>
    /// Serializes a graph to JSON and writes it out to a file.
    /// </summary>
    /// <typeparam name="T">The payload type carried by each node.</typeparam>
    /// <param name="graph">The graph to save.</param>
    /// <param name="path">The destination file path.</param>
    /// <param name="compress">
    /// When <c>true</c>, the JSON is GZip-compressed before being written.
    /// The savings grow with the number of nodes and edges.
    /// </param>
    /// <param name="options">
    /// Options controlling JSON serialization. Falls back to a compact
    /// (<c>WriteIndented = false</c>) default when omitted. Supply your own
    /// when <typeparamref name="T"/> needs custom
    /// <see cref="JsonSerializerOptions.Converters"/>.
    /// </param>
    public static void Save<T>(
        DagGraph<T> graph,
        string path,
        bool compress = false,
        JsonSerializerOptions? options = null)
    {
        var dto = ToDto(graph);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(dto, options ?? DefaultOptions);

        if (!compress)
        {
            File.WriteAllBytes(path, jsonBytes);
            return;
        }

        using var fileStream = File.Create(path);
        using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
        gzipStream.Write(jsonBytes);
    }

    /// <summary>
    /// Reads a graph back from a JSON file previously written by <see cref="Save{T}"/>.
    /// </summary>
    /// <typeparam name="T">The payload type carried by each node.</typeparam>
    /// <param name="path">The source file path.</param>
    /// <param name="compressed">
    /// Set to <c>true</c> if the file was GZip-compressed — this must match
    /// the <c>compress</c> value originally passed to <see cref="Save{T}"/>.
    /// </param>
    /// <param name="options">Options controlling JSON deserialization. Falls back to the default settings when omitted.</param>
    /// <returns>The reconstructed graph.</returns>
    public static DagGraph<T> Load<T>(
        string path,
        bool compressed = false,
        JsonSerializerOptions? options = null)
    {
        byte[] jsonBytes;

        if (!compressed)
        {
            jsonBytes = File.ReadAllBytes(path);
        }
        else
        {
            using var fileStream = File.OpenRead(path);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var memoryStream = new MemoryStream();
            gzipStream.CopyTo(memoryStream);
            jsonBytes = memoryStream.ToArray();
        }

        var dto = JsonSerializer.Deserialize<GraphDto<T>>(jsonBytes, options ?? DefaultOptions)
                        ?? throw new InvalidDataException("Failed to deserialize the JSON.");

        return FromDto(dto);
    }

    /// <summary>
    /// Asynchronously serializes a graph to JSON and writes it out to a file.
    /// </summary>
    /// <remarks>
    /// Behaves identically to <see cref="Save{T}"/> but avoids blocking the
    /// calling thread on file I/O, which matters most when called from a UI thread.
    /// </remarks>
    /// <typeparam name="T">The payload type carried by each node.</typeparam>
    /// <param name="graph">The graph to save.</param>
    /// <param name="path">The destination file path.</param>
    /// <param name="compress">
    /// When <c>true</c>, the JSON is GZip-compressed before being written.
    /// The savings grow with the number of nodes and edges.
    /// </param>
    /// <param name="options">
    /// Options controlling JSON serialization. Falls back to a compact
    /// (<c>WriteIndented = false</c>) default when omitted. Supply your own
    /// when <typeparamref name="T"/> needs custom
    /// <see cref="JsonSerializerOptions.Converters"/>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static async Task SaveAsync<T>(
        DagGraph<T> graph,
        string path,
        bool compress = false,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var dto = ToDto(graph);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(dto, options ?? DefaultOptions);

        if (!compress)
        {
            await File.WriteAllBytesAsync(path, jsonBytes, cancellationToken);
            return;
        }

        await using var fileStream = File.Create(path);
        await using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
        await gzipStream.WriteAsync(jsonBytes, cancellationToken);
    }

    /// <summary>
    /// Asynchronously reads a graph back from a JSON file previously written by
    /// <see cref="Save{T}"/> or <see cref="SaveAsync{T}"/>.
    /// </summary>
    /// <remarks>
    /// Behaves identically to <see cref="Load{T}"/> but avoids blocking the
    /// calling thread on file I/O, which matters most when called from a UI thread.
    /// </remarks>
    /// <typeparam name="T">The payload type carried by each node.</typeparam>
    /// <param name="path">The source file path.</param>
    /// <param name="compressed">
    /// Set to <c>true</c> if the file was GZip-compressed — this must match
    /// the <c>compress</c> value originally passed to <see cref="Save{T}"/> or <see cref="SaveAsync{T}"/>.
    /// </param>
    /// <param name="options">Options controlling JSON deserialization. Falls back to the default settings when omitted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The reconstructed graph.</returns>
    public static async Task<DagGraph<T>> LoadAsync<T>(
        string path,
        bool compressed = false,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        byte[] jsonBytes;

        if (!compressed)
        {
            jsonBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        else
        {
            await using var fileStream = File.OpenRead(path);
            await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var memoryStream = new MemoryStream();
            await gzipStream.CopyToAsync(memoryStream, cancellationToken);
            jsonBytes = memoryStream.ToArray();
        }

        var dto = JsonSerializer.Deserialize<GraphDto<T>>(jsonBytes, options ?? DefaultOptions)
            ?? throw new InvalidDataException("Failed to deserialize the JSON.");

        return FromDto(dto);
    }

    /// <summary>
    /// Serializes a graph directly to a JSON string, without touching the file system.
    /// </summary>
    /// <remarks>
    /// Useful for scenarios other than saving to disk, such as copy/paste or
    /// sending a graph over the network.
    /// </remarks>
    /// <typeparam name="T">The payload type carried by each node.</typeparam>
    /// <param name="graph">The graph to serialize.</param>
    /// <param name="options">
    /// Options controlling JSON serialization. Falls back to a compact
    /// (<c>WriteIndented = false</c>) default when omitted.
    /// </param>
    /// <returns>The graph's JSON representation.</returns>
    public static string ToJson<T>(DagGraph<T> graph, JsonSerializerOptions? options = null) =>
    JsonSerializer.Serialize(ToDto(graph), options ?? DefaultOptions);

    /// <summary>
    /// Reconstructs a graph from a JSON string previously produced by <see cref="ToJson{T}"/>.
    /// </summary>
    /// <typeparam name="T">The payload type carried by each node.</typeparam>
    /// <param name="json">The JSON text to parse.</param>
    /// <param name="options">Options controlling JSON deserialization. Falls back to the default settings when omitted.</param>
    /// <returns>The reconstructed graph.</returns>
    /// <exception cref="InvalidDataException">The reconstructed graph contains a cycle, or the JSON could not be parsed.</exception>
    public static DagGraph<T> FromJson<T>(string json, JsonSerializerOptions? options = null)
    {
        var dto = JsonSerializer.Deserialize<GraphDto<T>>(json, options ?? DefaultOptions)
            ?? throw new InvalidDataException("Failed to deserialize the JSON.");
        return FromDto(dto);
    }
}
