[![NuGet Version](https://img.shields.io/nuget/v/DagLib)](https://www.nuget.org/packages/DagLib/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/DagLib)](https://www.nuget.org/packages/DagLib/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

# DagLib

A generic DAG (directed acyclic graph) library for .NET, built around Guid-based node identity with cycle detection, topological sorting, and ancestor/descendant traversal baked in. It's designed to sit behind immediate-mode node editor UIs like ImNodes, but has no dependency on any UI library itself.

## Why

Node editors need a graph model that's easy to mutate interactively — add a node here, drag a link there — while never letting the underlying structure become invalid. DagLib rejects edges that would create a cycle at the moment you try to add them, so the UI layer never has to deal with an inconsistent graph in the first place. A full-graph cycle scan is also available for the one case interactive checks can't cover: data coming in from an external source, like a save file.

## Features

- **Typed payloads** — `Node<T>` and `DagGraph<T>` let each node carry any data type: a label, a record with position and color, a delegate, whatever the application needs.
- **Two layers of cycle safety** — a cheap, single-edge check (`WouldCreateCycle`) for interactive use, and a full-graph sweep (`HasCycle`) for validating data loaded from elsewhere.
- **Topological sort** via Kahn's algorithm.
- **Traversal helpers** — `GetAncestors` and `GetDescendants` for impact analysis ("what does this feed into?" / "what feeds into this?").
- **ID bridging** — `NodeIdMapper` maps each node's persistent `Guid` to a throwaway `int` for int-keyed APIs like ImNodes, without ever discarding the original identity.
- **ImNodes-facing helper** — `ImNodesGraphView<T>` combines a `DagGraph<T>` and a `NodeIdMapper` into a single facade for wiring up ImNodes events by int ID, so glue code isn't duplicated at every call site.
- **JSON persistence** — `DagLib.Serialization.GraphSerializer` saves and loads graphs as JSON, encoding edges as array indices rather than repeated Guids to keep files small, with an optional GZip-compressed format for larger graphs.

## Install

```
dotnet add package DagLib
```

## Usage

```csharp
using DagLib;

var graph = new DagGraph<string>();
var a = graph.AddNode("A");
var b = graph.AddNode("B");
var c = graph.AddNode("C");

graph.AddEdge(a, b);
graph.AddEdge(a, c);

var order = graph.TopologicalSort();
```

### Saving and loading JSON

```csharp
using DagLib.Serialization;

GraphSerializer.Save(graph, "graph.json");
GraphSerializer.Save(graph, "graph.json.gz", compress: true);

var loaded = GraphSerializer.Load<string>("graph.json");
```

### Working with ImNodes

```csharp
using DagLib;

var mapper = new NodeIdMapper();
int imNodesId = mapper.GetOrCreateImNodesId(a.Id);
// pass imNodesId into the native API, e.g. ImNodes.BeginNode(imNodesId);
```

For a tighter integration, `ImNodesGraphView<T>` wraps a `DagGraph<T>` and a `NodeIdMapper` together, so ImNodes event handlers can work directly with int IDs instead of manually translating back and forth. Besides the constructor that takes an existing graph and mapper, two lighter-weight constructors are available for common setups: one that takes no arguments and creates both a new `DagGraph<T>` and a new `NodeIdMapper` internally, and one that takes only an existing `graph` and creates a fresh `NodeIdMapper` for it (handy when reopening a graph loaded from storage in a new UI session).

```csharp
using DagLib;

var view = new ImNodesGraphView<string>(graph, mapper); // wrap an existing graph and mapper
var view = new ImNodesGraphView<string>(graph);          // wrap an existing graph, fresh mapper
var view = new ImNodesGraphView<string>();               // start from scratch

// Handling an IsLinkCreated event
view.AddEdge(fromImNodesId, toImNodesId);

// Handling a node deletion — removes it from both the graph and the ID mapping
view.RemoveNode(imNodesId);

// Driving a render loop
foreach (var (node, id) in view.GetRenderableNodes())
{
    ImNodes.BeginNode(id);
    // ... build UI from node.Data ...
    ImNodes.EndNode();
}
```

### Step-by-step traversal (branching execution)

`TopologicalSort()` resolves an entire execution order up front, which works well for pure data-flow graphs but doesn't fit graphs with runtime branching — an if/else node, a sequence node — where the next step depends on a condition that isn't known ahead of time. For those cases, `GetNextNodes` walks the graph one step at a time instead:

```csharp
var pending = new Queue<Node<ExecNode>>();
pending.Enqueue(start);

while (pending.Count > 0)
{
    var node = pending.Dequeue();
    node.Data.Run?.Invoke();

    // No selector: just follow every outgoing edge as-is.
    // Pass a selector to pick a subset instead — e.g. only one branch of an if/else.
    var next = node == branch
        ? graph.GetNextNodes(node, (n, outputs) => [conditionResult ? outputs[0] : outputs[1]])
        : graph.GetNextNodes(node);

    foreach (var n in next)
        pending.Enqueue(n);
}
```

A selector can also return more than one node — useful for a "sequence" node that should continue down every outgoing edge at once rather than picking a single branch.

## License

MIT
