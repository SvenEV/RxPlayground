using System.Collections.Immutable;

namespace RxPlayground.RxInteractive
{
    public record Node<V, EKey>(
        V Value,
        ImmutableList<EKey> InEdges,
        ImmutableList<EKey> OutEdges);

    public record Edge<E, VKey>(
        E Value,
        VKey Source,
        VKey Target);

    public record Graph<V, VKey, E, EKey>(
        ImmutableDictionary<VKey, Node<V, EKey>> Nodes,
        ImmutableDictionary<EKey, Edge<E, VKey>> Edges)
        where VKey : notnull
        where EKey : notnull
    {
        public static readonly Graph<V, VKey, E, EKey> Empty = new(
            Nodes: ImmutableDictionary<VKey, Node<V, EKey>>.Empty,
            Edges: ImmutableDictionary<EKey, Edge<E, VKey>>.Empty
        );

        public Graph<V, VKey, E, EKey> TryAddNode(VKey key, V value) =>
            Nodes.ContainsKey(key)
                ? this
                : this with
                {
                    Nodes = Nodes.Add(key, new Node<V, EKey>(
                        value,
                        ImmutableList<EKey>.Empty,
                        ImmutableList<EKey>.Empty)
                    )
                };

        public Graph<V, VKey, E, EKey> RemoveNode(VKey key) =>
            Nodes.TryGetValue(key, out var edge)
                ? this with
                {
                    Nodes = Nodes.Remove(key),
                    Edges = Edges.RemoveRange(edge.InEdges.Concat(edge.OutEdges))
                }
                : this;

        public Graph<V, VKey, E, EKey> AddEdge(EKey key, E value, VKey source, VKey target) =>
            (Edges.ContainsKey(key), Nodes.TryGetValue(source, out var sourceNode), Nodes.TryGetValue(target, out var targetNode)) switch
            {
                (true, _, _) => throw new ArgumentException($"An edge with key '{key}' already exists"),
                (_, false, _) => throw new ArgumentException($"The source node with key '{source}' does not exist"),
                (_, _, false) => throw new ArgumentException($"The target node with key '{target}' does not exist"),

                (false, true, true) => this with
                {
                    Nodes = Nodes
                        .SetItem(source, sourceNode! with { OutEdges = sourceNode.OutEdges.Add(key) })
                        .SetItem(target, targetNode! with { InEdges = targetNode.InEdges.Add(key) }),
                    Edges = Edges.Add(key, new Edge<E, VKey>(value, source, target))
                }
            };

        public Graph<V, VKey, E, EKey> RemoveEdge(EKey key) =>
            Edges.TryGetValue(key, out var edge) &&
                Nodes[edge.Source] is var sourceNode &&
                Nodes[edge.Target] is var targetNode
                ? this with
                {
                    Nodes = Nodes
                        .SetItem(edge.Source, sourceNode with { OutEdges = sourceNode.OutEdges.Remove(key) })
                        .SetItem(edge.Target, targetNode with { InEdges = targetNode.InEdges.Remove(key) }),
                    Edges = Edges.Remove(key)
                }
                : this;

        public Graph<V, VKey, E, EKey> UpdateNode(VKey key, Func<V, V> valueSelector) =>
            Nodes.TryGetValue(key, out var node)
                ? this with
                {
                    Nodes = Nodes.SetItem(key, node with { Value = valueSelector(node.Value) })
                }
                : throw new ArgumentException($"The node with key '{key}' does not exist");

        public Graph<V, VKey, E, EKey> UpdateEdge(EKey key, Func<E, E> valueSelector) =>
            Edges.TryGetValue(key, out var edge)
                ? this with
                {
                    Edges = Edges.SetItem(key, edge with { Value = valueSelector(edge.Value) })
                }
                : throw new ArgumentException($"The edge with key '{key}' does not exist");
    }
}
