[![NuGet Version](https://img.shields.io/nuget/v/DagLib)](https://www.nuget.org/packages/DagLib/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/DagLib)](https://www.nuget.org/packages/DagLib/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

# DagLib

.NET向けの汎用DAG(有向非巡回グラフ)ライブラリです。Guidによるノード識別を軸に、
循環検知・トポロジカルソート・祖先/子孫探索といった機能を標準で備えています。
ImNodesのようなイミディエイトモードのノードエディタUIの裏側として使うことを
想定して設計していますが、UIライブラリへの依存は一切ありません。

## なぜ作ったか

ノードエディタでは、ノードを置いたりリンクをドラッグしたりといった対話的な
編集を自由に行いつつ、グラフの構造が壊れないことが求められます。DagLibは
循環を生むエッジを追加しようとした瞬間に拒否するため、UI層は不整合なグラフ
状態を一切気にせずに済みます。また、対話的なチェックではカバーしきれない
ケース——セーブファイルなど外部から読み込んだデータの検証——のために、
グラフ全体を一括で検査する機能も用意しています。

## 特徴

- **任意のデータ型を保持** — `Node<T>` と `DagGraph<T>` により、ラベル文字列、
  座標や色を含むレコード、デリゲートなど、アプリケーションに応じた任意の
  データをノードに持たせられます。
- **2段階の循環対策** — 対話的な操作向けの軽量な単一エッジチェック
  (`WouldCreateCycle`) と、外部データ検証向けのグラフ全体一括チェック
  (`HasCycle`) の両方を提供します。
- **トポロジカルソート** — Kahnのアルゴリズムによる実装。
- **探索ヘルパー** — `GetAncestors`(祖先取得)と `GetDescendants`(子孫取得)
  により、「このノードは何に影響するか」「このノードは何から影響を受けるか」
  を調べられます。
- **IDのブリッジ** — `NodeIdMapper` が各ノードの永続的な `Guid` を、
  ImNodesのようなintベースのAPI向けの使い捨てintIDに変換します。元のGuidの
  情報が失われることはありません。
- **ImNodes連携ヘルパー** — `ImNodesGraphView<T>` は `DagGraph<T>` と
  `NodeIdMapper` を1つにまとめたファサードで、ImNodesのイベントをintIDの
  まま扱えるようにします。呼び出し側で毎回グルーコードを書く必要が
  なくなります。
- **JSONでの永続化** — `DagLib.Serialization.GraphSerializer` により
  グラフをJSONとして保存・読み込みできます。エッジはGuidの繰り返しではなく
  配列インデックスとして記録することでファイルサイズを抑えており、
  大きなグラフ向けにGZip圧縮オプションも用意しています。

## インストール

```
dotnet add package DagLib
```

## 使い方

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

### JSONへの保存/読み込み

```csharp
using DagLib.Serialization;

GraphSerializer.Save(graph, "graph.json");
GraphSerializer.Save(graph, "graph.json.gz", compress: true);

var loaded = GraphSerializer.Load<string>("graph.json");
```

### ImNodesとの連携

```csharp
using DagLib;

var mapper = new NodeIdMapper();
int imNodesId = mapper.GetOrCreateImNodesId(a.Id);
// ImNodes.BeginNode(imNodesId); のようにネイティブAPIへ渡す
```

より密接に連携させたい場合は、`ImNodesGraphView<T>` が `DagGraph<T>` と
`NodeIdMapper` をまとめてラップしており、ImNodesのイベントハンドラを
intIDのまま直接扱えます。呼び出し側で手動の相互変換を書く必要がありません。

```csharp
using DagLib;

var view = new ImNodesGraphView<string>(graph, mapper);

// IsLinkCreated イベントの処理
view.AddEdge(fromImNodesId, toImNodesId);

// ノード削除時 — グラフとIDマッピングの両方から一度に削除される
view.RemoveNode(imNodesId);

// 描画ループを回す
foreach (var (node, id) in view.GetRenderableNodes())
{
    ImNodes.BeginNode(id);
    // ... node.Data を使ってUIを組み立てる ...
    ImNodes.EndNode();
}
```

## ライセンス

MIT
