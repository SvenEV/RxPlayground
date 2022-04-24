using System.Collections.Immutable;
using System.Numerics;
using System.Reactive.Linq;
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
        public static readonly TimeSpan FrameLength = TimeSpan.FromSeconds(.0167);

        private readonly Subject<IObservable<RxInteractiveEvent>> eventSourceAdditions = new();
        private readonly BehaviorSubject<RxInteractiveSessionState> state;
        private readonly object stateLock = new();
        private readonly ILogger logger;
        private readonly IDisposable eventSubscription;

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

            eventSubscription = eventSourceAdditions
                .Scan(ImmutableList<IObservable<RxInteractiveEvent>>.Empty, (eventSources, newEventSource) => eventSources.Add(newEventSource))
                .Select(eventSources => eventSources.Merge())
                .Switch()
                .Buffer(FrameLength)
                .Subscribe(HandleEventBatch);
        }

        private void AddEventSource(IObservable<RxInteractiveEvent> eventSource)
        {
            logger.LogInformation("Add event source");
            eventSourceAdditions.OnNext(eventSource);
        }

        private void HandleEventBatch(IList<RxInteractiveEvent> events)
        {
            if (events.Count == 0)
            {
                lock(stateLock)
                {
                    state.OnNext(state.Value with { Timestamp = TimeProvider.GetTimestamp() });
                }
            }
            else
            {
                foreach (var ev in events)
                    HandleEvent(ev);
            }
        }

        private void HandleEvent(RxInteractiveEvent ev)
        {
            logger.LogInformation("Handle event '{EventType}'", ev.GetType().Name);

            lock (stateLock)
            {
                state.OnNext(ev switch
                {
                    RxInteractiveEvent.ObservableCreated e => HandleObservableCreated(state.Value, e),
                    RxInteractiveEvent.SubscriberCreated e => HandleSubscriberCreated(state.Value, e),
                    RxInteractiveEvent.Subscribed e => HandleSubscribed(state.Value, e),
                    RxInteractiveEvent.ValueEmitted e => HandleValueEmitted(state.Value, e),
                    _ => throw new NotImplementedException($"{nameof(RxInteractiveEvent)}.{ev.GetType().Name}")
                });

                if (ev is RxInteractiveEvent.ObservableCreated ev1)
                    AddEventSource(ev1.Observable.Events);

                if (ev is RxInteractiveEvent.SubscriberCreated ev2)
                    AddEventSource(ev2.Subscriber.Events);
            }

            static RxInteractiveSessionState HandleObservableCreated(RxInteractiveSessionState state, RxInteractiveEvent.ObservableCreated ev) => state with
            {
                Timestamp = ev.Timestamp,
                Graph = ev.Observable.Parents.Aggregate(
                    seed: state.Graph
                        .TryAddNode(
                            ev.Observable.AggregateNodeId,
                            new DataFlowNode(ev.Observable.UnderlyingObservable.GetType().Name, Vector2.Zero)
                        )
                        .Layout(),
                    func: (graph, parent) => graph
                )
            };

            static RxInteractiveSessionState HandleSubscriberCreated(RxInteractiveSessionState state, RxInteractiveEvent.SubscriberCreated ev) => state with
            {
                Timestamp = ev.Timestamp,
                Graph = state.Graph
                    .TryAddNode(
                        new DataFlowNodeId(ev.Subscriber),
                        new DataFlowNode("Subscription", Vector2.Zero)
                    )
                    .Layout()
            };

            static RxInteractiveSessionState HandleSubscribed(RxInteractiveSessionState state, RxInteractiveEvent.Subscribed ev) => state with
            {
                Timestamp = ev.Timestamp,
                Graph = state.Graph
                    .AddEdge(
                        key: ev.EdgeId,
                        value: new DataFlowEdge(ImmutableList<ObservableEmissionWithTimestamp>.Empty),
                        source: ev.EdgeId.SourceId,
                        target: ev.EdgeId.TargetId
                    )
                    .Layout()
            };

            static RxInteractiveSessionState HandleValueEmitted(RxInteractiveSessionState state, RxInteractiveEvent.ValueEmitted ev) => state with
            {
                Timestamp = ev.Timestamp,
                Graph = state.Graph
                    .UpdateEdge(
                        ev.EdgeId,
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

            var dict = new Dictionary<DataFlowNodeId, Vector2>();

            Iterate(roots, Vector2.Zero);

            Vector2 Iterate(IEnumerable<KeyValuePair<DataFlowNodeId, Node<DataFlowNode, DataFlowEdgeId>>> nodes, Vector2 p)
            {
                foreach (var node in nodes)
                {
                    dict[node.Key] = p;

                    var children = node.Value.OutEdges
                        .Select(edgeId => new KeyValuePair<DataFlowNodeId, Node<DataFlowNode, DataFlowEdgeId>>(graph.Edges[edgeId].Target, graph.Nodes[graph.Edges[edgeId].Target]))
                        .ToList();

                    var pAfter = Iterate(children, p + Vector2.UnitY);
                    p = new(pAfter.X + 1, p.Y);
                }

                return p;
            }

            return dict.Aggregate(graph, (g, pos) => g.UpdateNode(pos.Key, n => n with { Position = pos.Value * 100 }));
        }
    }

    public record DataFlowEdge(
        ImmutableList<ObservableEmissionWithTimestamp> Emissions)
    {
        public override string ToString() => $"{Emissions.Count} emissions";
    }

    public record ObservableEmissionWithTimestamp(
        DateTimeOffset Timestamp,
        ObservableEmission Emission);

    public record DataFlowNode(string DisplayName, Vector2 Position)
    {
        public override string ToString() => DisplayName;
    }

    public record DataFlowNodeId(object Identity)
    {
        public override string ToString() => $"Node({Identity.GetType().Name}-{Identity.GetHashCode()})";
    }

    /// <summary>
    /// Assumption: An ObserverNode is not the target of multiple subscriptions.
    /// Otherwise, ObserverId is not unique (think of multiple edges between an ObservableNode and an ObserverNode).
    /// Multiple edges between two ObservableNodes are possible because each subscription uses a separate InteractiveObserver.
    /// </summary>
    public record DataFlowEdgeId(
        DataFlowNodeId SourceId,
        DataFlowNodeId TargetId,
        int SequenceNumber)
    {
        public override string ToString() => $"Edge({SourceId} → {TargetId} | {SequenceNumber})";
    }


    public abstract record RxInteractiveEvent
    {
        public DateTimeOffset Timestamp { get; }

        private RxInteractiveEvent(DateTimeOffset Timestamp) => this.Timestamp = Timestamp;

        public record ObservableCreated(
            DateTimeOffset Timestamp,
            IInteractiveObservable Observable) : RxInteractiveEvent(Timestamp);

        public record SubscriberCreated(
            DateTimeOffset Timestamp,
            IInteractiveSubscription Subscriber) : RxInteractiveEvent(Timestamp);

        public record Subscribed(
            DateTimeOffset Timestamp,
            DataFlowEdgeId EdgeId,
            IInteractiveObserver Observer) : RxInteractiveEvent(Timestamp);

        public record Unsubscribed(
            DateTimeOffset Timestamp,
            DataFlowEdgeId EdgeId) : RxInteractiveEvent(Timestamp);

        public record ValueEmitted(
            DateTimeOffset Timestamp,
            DataFlowEdgeId EdgeId,
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
