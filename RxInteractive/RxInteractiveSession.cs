using System.Collections.Immutable;
using System.Reactive.Subjects;

using RxGraph = RxPlayground.RxInteractive.Graph<
    RxPlayground.RxInteractive.DataFlowNode,
    RxPlayground.RxInteractive.DataFlowNodeId,
    RxPlayground.RxInteractive.DataFlowEdge,
    RxPlayground.RxInteractive.DataFlowEdgeId>;

namespace RxPlayground.RxInteractive
{
    public class RxInteractiveSession : IObservable<RxInteractiveSessionState>
    {
        public static readonly TimeSpan TimelineLength = TimeSpan.FromSeconds(3);

        private readonly BehaviorSubject<RxInteractiveSessionState> state;
        private readonly object stateLock = new();
        private readonly ILogger logger;

        public ITimeProvider TimeProvider { get; }

        public RxInteractiveSession(ITimeProvider timeProvider, ILogger logger)
        {
            state = new(
                new RxInteractiveSessionState(
                    Timestamp: timeProvider.GetTimestamp(),
                    Graph: RxGraph.Empty
                )
            );
            TimeProvider = timeProvider;
            this.logger = logger;
        }

        public IDisposable AddEventSource(IObservable<RxInteractiveEvent> eventSource)
        {
            logger.LogInformation("Add event source");
            return eventSource.Subscribe(HandleEvent);
        }

        private void HandleEvent(RxInteractiveEvent ev)
        {
            logger.LogInformation("Handle event '{EventType}'", ev.GetType().Name);

            lock (stateLock)
            {
                state.OnNext(ev switch
                {
                    RxInteractiveEvent.ObservableCreated e => HandleObservableCreated(state.Value, e),
                    RxInteractiveEvent.Subscribed e => HandleSubscribed(state.Value, e),
                    RxInteractiveEvent.ValueEmitted e => HandleValueEmitted(state.Value, e),
                    _ => throw new NotImplementedException($"{nameof(RxInteractiveEvent)}.{ev.GetType().Name}")
                });
            }

            static RxInteractiveSessionState HandleObservableCreated(RxInteractiveSessionState state, RxInteractiveEvent.ObservableCreated ev) => state with
            {
                Timestamp = ev.Timestamp,
                Graph = ev.Observable.Parents.Aggregate(
                    seed: state.Graph
                        .AddNode(
                            new DataFlowNodeId.ObservableNode(ev.Observable.Id),
                            new DataFlowNode(ev.Observable.Source.GetType().Name, state.Graph.Nodes.Count * 100, 0)
                        )
                        .Layout(),
                    func: (graph, parent) => graph
                )
            };

            static RxInteractiveSessionState HandleSubscribed(RxInteractiveSessionState state, RxInteractiveEvent.Subscribed ev) => state with
            {
                Timestamp = ev.Timestamp,
                Graph = state.Graph
                    .AddNode(
                        new DataFlowNodeId.ObserverNode(ev.Observer.Id),
                        new DataFlowNode("Subscription", state.Graph.Nodes.Count * 100, 0)
                    )
                    .AddEdge(
                        key: new DataFlowEdgeId(ev.Observer.Id),
                        value: new DataFlowEdge(ImmutableList<ObservableEmissionWithTimestamp>.Empty),
                        source: new DataFlowNodeId.ObservableNode(ev.ObservableId),
                        target: new DataFlowNodeId.ObserverNode(ev.Observer.Id)
                    )
                    .Layout()
            };

            static RxInteractiveSessionState HandleValueEmitted(RxInteractiveSessionState state, RxInteractiveEvent.ValueEmitted ev) => state with
            {
                Timestamp = ev.Timestamp,
                Graph = state.Graph
                    .UpdateEdge(
                        new DataFlowEdgeId(ev.ObserverId),
                        edge => AddEmission(edge, ev.Emission, ev.Timestamp)
                    )
            };

            static DataFlowEdge AddEmission(DataFlowEdge edge, ObservableEmission emission, DateTimeOffset timestamp)
            {
                var timeLimit = timestamp - TimelineLength;

                return edge with
                {
                    Emissions = edge.Emissions
                        .SkipWhile(e => e.Timestamp < timeLimit)
                        .Append(new ObservableEmissionWithTimestamp(timestamp, emission))
                        .ToImmutableList()
                };
            }
        }

        public void OnCompleted() => throw new NotImplementedException();

        public void OnError(Exception error) => throw new NotImplementedException();

        public void OnNext(RxInteractiveEvent value) => HandleEvent(value);

        public IDisposable Subscribe(IObserver<RxInteractiveSessionState> observer) => state.Subscribe(observer);
    }

    public record RxInteractiveSessionState(
        DateTimeOffset Timestamp,
        RxGraph Graph)
    {
        public static readonly RxInteractiveSessionState Empty = new(
            Timestamp: DateTimeOffset.MinValue,
            Graph: RxGraph.Empty);
    }

    public static class RxGraphExtension
    {
        public static RxGraph Layout(this RxGraph graph)
        {
            //return graph; // TODO

            var roots = graph.Nodes.Where(n => n.Value.InEdges.IsEmpty).ToList();

            var dict = new Dictionary<DataFlowNodeId, (int, int)>();

            Iterate(roots, 0, 0);

            (int, int) Iterate(IEnumerable<KeyValuePair<DataFlowNodeId, Node<DataFlowNode, DataFlowEdgeId>>> nodes, int x, int y)
            {
                foreach (var node in nodes)
                {
                    dict[node.Key] = (x, y);

                    var children = node.Value.OutEdges
                        .Select(edgeId => new KeyValuePair<DataFlowNodeId, Node<DataFlowNode, DataFlowEdgeId>>(graph.Edges[edgeId].Target, graph.Nodes[graph.Edges[edgeId].Target]))
                        .ToList();

                    var (afterX, afterY) = Iterate(children, x, y + 1);
                    x = afterX + 1;
                }

                return (x, y);
            }

            return dict.Aggregate(graph, (g, pos) => g.UpdateNode(pos.Key, n => n with { X = pos.Value.Item1 * 100, Y = pos.Value.Item2 * 100 }));
        }
    }

    public record ObservableId(Guid Value)
    {
        public string DebugString => $"{Value.ToString()[0..8]}";
    }

    public record ObserverId(Guid Value)
    {
        public string DebugString => $"{Value.ToString()[0..8]}";
    }

    public record DataFlowEdge(
        ImmutableList<ObservableEmissionWithTimestamp> Emissions)
    {
        public string DebugString => $"{Emissions.Count} emissions";
    }

    public record ObservableEmissionWithTimestamp(
        DateTimeOffset Timestamp,
        ObservableEmission Emission);

    public record DataFlowNode(string DisplayName, double X, double Y)
    {
        public string DebugString => DisplayName;
    }

    public abstract record DataFlowNodeId
    {
        private DataFlowNodeId() { }
        public record ObservableNode(ObservableId Id) : DataFlowNodeId;
        public record ObserverNode(ObserverId Id) : DataFlowNodeId;
        // public record CommentNode(...)

        public string DebugString => this switch
        {
            ObservableNode o => $"ObservableNode({o.Id.DebugString})",
            ObserverNode o => $"ObserverNode({o.Id.DebugString})",
            _ => throw new NotImplementedException()
        };
    }

    /// <summary>
    /// Assumption: An ObserverNode is not the target of multiple subscriptions.
    /// Otherwise, ObserverId is not unique (think of multiple edges between an ObservableNode and an ObserverNode).
    /// Multiple edges between two ObservableNodes are possible because each subscription uses a separate InteractiveObserver.
    /// </summary>
    public record DataFlowEdgeId(ObserverId Value)
    {
        public string DebugString => $"Edge({Value.DebugString})";
    }


    public abstract record RxInteractiveEvent
    {
        public DateTimeOffset Timestamp { get; }

        private RxInteractiveEvent(DateTimeOffset Timestamp) => this.Timestamp = Timestamp;

        public record ObservableCreated(
            DateTimeOffset Timestamp,
            IInteractiveObservable Observable) : RxInteractiveEvent(Timestamp);

        public record Subscribed(
            DateTimeOffset Timestamp,
            ObservableId ObservableId,
            IInteractiveObserver Observer) : RxInteractiveEvent(Timestamp);

        public record Unsubscribed(
            DateTimeOffset Timestamp,
            ObserverId ObserverId) : RxInteractiveEvent(Timestamp);

        public record ValueEmitted(
            DateTimeOffset Timestamp,
            ObserverId ObserverId,
            ObservableEmission Emission) : RxInteractiveEvent(Timestamp);
    }

    public abstract record ObservableEmission
    {
        private ObservableEmission() { }

        public record Next(object Value) : ObservableEmission();
        public record Error(Exception Exception) : ObservableEmission();
        public record Completed() : ObservableEmission();
    }
}
