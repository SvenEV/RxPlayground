using System.Collections.Immutable;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using RxGraph = RxPlayground.RxInteractive.Graph<
    RxPlayground.RxInteractive.DataFlowNode,
    RxPlayground.RxInteractive.DataFlowNodeId,
    RxPlayground.RxInteractive.DataFlowEdge,
    RxPlayground.RxInteractive.DataFlowEdgeId>;

namespace RxPlayground.RxInteractive
{
    public class RxInteractiveSession : IIntrospectionCache, IObservable<RxInteractiveSessionState>, IDisposable
    {
        public static readonly TimeSpan TimelineLength = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan FrameLength = TimeSpan.FromSeconds(.0167);

        private readonly Subject<IInteractiveNode> eventSourceAdditions = new();
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
                    InteractiveObservables: ImmutableDictionary<object, IInteractiveObservable>.Empty,
                    InteractiveSubscriptions: ImmutableList<IInteractiveSubscription>.Empty,
                    Graph: RxGraph.Empty
                )
            );

            TimeProvider = timeProvider;
            this.logger = logger;

            eventSubscription = eventSourceAdditions
                .Scan(ImmutableHashSet<IInteractiveNode>.Empty, (eventSources, newEventSource) => eventSources.Add(newEventSource))
                .Select(eventSources => eventSources.Select(source => source.Events.Timestamped(timeProvider)).Merge())
                .Switch()
                .Buffer(FrameLength)
                .Subscribe(HandleEventBatch);
        }

        private void AddEventSourcesFromNode(IInteractiveNode node)
        {
            eventSourceAdditions.OnNext(node);
            foreach (var upstreamNode in node.Upstreams)
                AddEventSourcesFromNode(upstreamNode.Owner);
        }

        private void HandleEventBatch(IList<Timestamped<RxInteractiveEvent>> events)
        {
            if (events.Count == 0)
            {
                lock (stateLock)
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

        private void HandleEvent(Timestamped<RxInteractiveEvent> ev)
        {
            logger.LogInformation("Handle event '{EventType}'", ev.Value.GetType().Name);

            lock (stateLock)
            {
                state.OnNext(ev.Value switch
                {
                    RxInteractiveEvent.ObservableCreated e => HandleObservableCreated(state.Value, ev.Timestamp, e),
                    RxInteractiveEvent.SubscriberCreated e => HandleSubscriberCreated(state.Value, ev.Timestamp, e),
                    RxInteractiveEvent.Subscribed e => HandleSubscribed(state.Value, ev.Timestamp, e),
                    RxInteractiveEvent.Unsubscribed e => HandleUnsubscribed(state.Value, ev.Timestamp, e),
                    RxInteractiveEvent.ValueEmitted e => HandleValueEmitted(state.Value, ev.Timestamp, e),
                    _ => throw new NotImplementedException($"{nameof(RxInteractiveEvent)}.{ev.GetType().Name}")
                });
            }

            if (ev.Value is RxInteractiveEvent.ObservableCreated ev1)
                AddEventSourcesFromNode(ev1.Observable);

            if (ev.Value is RxInteractiveEvent.SubscriberCreated ev2)
            {
                AddEventSourcesFromNode(ev2.Subscriber);
                ev2.Subscriber.Subscribe();
            }

            static RxInteractiveSessionState HandleObservableCreated(RxInteractiveSessionState state, DateTimeOffset timestamp, RxInteractiveEvent.ObservableCreated ev) => state with
            {
                Timestamp = timestamp,
                Graph = ev.Observable.Upstreams.Aggregate(
                    seed: state.Graph
                        .AddNodeAndUpstream(ev.Observable)
                        .Layout(),
                    func: (graph, parent) => graph
                )
            };

            static RxInteractiveSessionState HandleSubscriberCreated(RxInteractiveSessionState state, DateTimeOffset timestamp, RxInteractiveEvent.SubscriberCreated ev) => state with
            {
                Timestamp = timestamp,
                InteractiveSubscriptions = state.InteractiveSubscriptions.Add(ev.Subscriber),
                Graph = state.Graph
                    .AddNodeAndUpstream(ev.Subscriber)
                    .Layout()
            };

            static RxInteractiveSessionState HandleSubscribed(RxInteractiveSessionState state, DateTimeOffset timestamp, RxInteractiveEvent.Subscribed ev) => state with
            {
                Timestamp = timestamp,
                Graph = state.Graph
                    .AddEdge(
                        key: ev.EdgeId,
                        value: new DataFlowEdge.SubscriptionEdge(ImmutableList<ObservableEmissionWithTimestamp>.Empty),
                        source: ev.EdgeId.SourceId,
                        target: ev.EdgeId.TargetId
                    )
                    .Layout()
            };

            static RxInteractiveSessionState HandleUnsubscribed(RxInteractiveSessionState state, DateTimeOffset timestamp, RxInteractiveEvent.Unsubscribed ev) => state with
            {
                Timestamp = timestamp,
                Graph = state.Graph
                    .RemoveEdge(ev.EdgeId)
                    .Layout()
            };

            static RxInteractiveSessionState HandleValueEmitted(RxInteractiveSessionState state, DateTimeOffset timestamp, RxInteractiveEvent.ValueEmitted ev) => state with
            {
                Timestamp = timestamp,
                Graph = state.Graph
                    .UpdateEdge(
                        ev.EdgeId,
                        edge => AddEmission((DataFlowEdge.SubscriptionEdge)edge, ev.Emission, timestamp)
                    )
            };

            static DataFlowEdge.SubscriptionEdge AddEmission(DataFlowEdge.SubscriptionEdge edge, ObservableEmission emission, DateTimeOffset timestamp)
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

        public void DeclareObservable(object observable)
        {
            var observableInstrumented = Introspection.InjectInspector(observable, this);

            if (observableInstrumented.ObservableRaw is IInteractiveObservablePort port)
            {
                HandleEvent(new Timestamped<RxInteractiveEvent>(
                    new RxInteractiveEvent.ObservableCreated(port.Owner),
                    TimeProvider.GetTimestamp()
                ));
            }
        }

        public void DeclareSubscription(object observable, object observer, Action<IDisposable> onSubscribed)
        {
            var observableInstrumented = Introspection.InjectInspector(observable, this);

            if (observableInstrumented.Upstreams.Count != 1)
                throw new InvalidOperationException("Cannot subscribe to an observable with multiple upstreams");

            var observableElementType = Introspection.GetObservableElementType(observable);
            var upstreamElementType = Introspection.GetObservableElementType(observableInstrumented.Upstreams[0]);

            if (upstreamElementType != observableElementType)
                throw new InvalidOperationException("Cannot subscribe if upstream is not of type T");

            var subscription = (IInteractiveSubscription)Activator.CreateInstance(
                typeof(InteractiveSubscription<>).MakeGenericType(observableElementType),
                observableInstrumented.Upstreams[0],
                observer,
                onSubscribed)!;

            HandleEvent(new Timestamped<RxInteractiveEvent>(
                new RxInteractiveEvent.SubscriberCreated(subscription),
                TimeProvider.GetTimestamp()
            ));
        }

        IInteractiveObservable IIntrospectionCache.GetOrAdd(object observable, Func<IInteractiveObservable> factoryFunc)
        {
            lock (stateLock)
            {
                if (state.Value.InteractiveObservables.TryGetValue(observable, out var interactiveObservable))
                    return interactiveObservable;

                var newInteractiveObservable = factoryFunc();

                state.OnNext(state.Value with
                {
                    Timestamp = TimeProvider.GetTimestamp(),
                    InteractiveObservables = state.Value.InteractiveObservables.Add(observable, newInteractiveObservable)
                });

                return newInteractiveObservable;
            }
        }

        bool IIntrospectionCache.ShouldInspect(object observable) =>
            observable is IVisualizerObservable or IInteractiveSubscription;

        public IDisposable Subscribe(IObserver<RxInteractiveSessionState> observer) => state.Subscribe(observer);

        public void Dispose()
        {
            eventSourceAdditions.Dispose();
            eventSubscription.Dispose();
            var lastState = state.Value;
            state.Dispose();

            foreach (var sub in lastState.InteractiveSubscriptions)
                sub.Dispose();
        }
    }

    public record RxInteractiveSessionState(
        DateTimeOffset Timestamp,
        ImmutableDictionary<object, IInteractiveObservable> InteractiveObservables,
        ImmutableList<IInteractiveSubscription> InteractiveSubscriptions,
        RxGraph Graph)
    {
        public static readonly RxInteractiveSessionState Empty = new(
            Timestamp: DateTimeOffset.MinValue,
            InteractiveObservables: ImmutableDictionary<object, IInteractiveObservable>.Empty,
            InteractiveSubscriptions: ImmutableList<IInteractiveSubscription>.Empty,
            Graph: RxGraph.Empty);
    }

    public static class RxGraphExtensions
    {
        public static RxGraph Layout(this RxGraph graph)
        {
            var random = new Random();

            var (dict, _) = Enumerable.Range(0, 100)
                .Select(_ => ComputeLayout())
                .MinBy(result => result.Item2);

            (Dictionary<DataFlowNodeId, Vector2>, int) ComputeLayout()
            {
                var roots = graph.Nodes.Where(n => n.Value.InEdges.IsEmpty).ToList();
                var dict = new Dictionary<DataFlowNodeId, Vector2>();

                Iterate(roots, Vector2.Zero, ImmutableHashSet<DataFlowNodeId>.Empty);

                Vector2 Iterate(IEnumerable<KeyValuePair<DataFlowNodeId, Node<DataFlowNode, DataFlowEdgeId>>> nodes, Vector2 p, ImmutableHashSet<DataFlowNodeId> path)
                {
                    foreach (var node in nodes.OrderBy(_ => random.Next()))
                    {
                        //if (dict.ContainsKey(node.Key))
                        //    throw new InvalidOperationException("Node visited twice!");

                        dict[node.Key] = p;

                        var children = node.Value.OutEdges
                            .Select(edgeId => new KeyValuePair<DataFlowNodeId, Node<DataFlowNode, DataFlowEdgeId>>(graph.Edges[edgeId].Target, graph.Nodes[graph.Edges[edgeId].Target]))
                            .Distinct()
                            .ToList();

                        var pAfter = Iterate(children, p + Vector2.UnitY, path.Add(node.Key));
                        p = new(pAfter.X + 1, p.Y);
                    }

                    return p;
                }

                // https://stackoverflow.com/a/9997374
                bool ccw(Vector2 a, Vector2 b, Vector2 c) =>
                    (c.Y - a.Y) * (b.X - a.X) > (b.Y - a.Y) * (c.X - a.X);
                bool intersect(Vector2 A, Vector2 B, Vector2 C, Vector2 D) =>
                    ccw(A, C, D) != ccw(B, C, D) && ccw(A, B, C) != ccw(A, B, D);

                var numEdgeCrossings = (
                    from a in graph.Edges.Values
                    from b in graph.Edges.Values
                    select intersect(dict[a.Source], dict[a.Target], dict[b.Source], dict[b.Target]) ? 1 : 0).Sum();

                return (dict, numEdgeCrossings);
            }

            return dict.Aggregate(graph, (g, pos) => g.UpdateNode(pos.Key, n => n with { Position = pos.Value * 100 }));
        }

        public static RxGraph AddNodeAndUpstream(this RxGraph graph, IInteractiveNode node)
        {
            var result = AddRecursively(graph, node);
            return result.NewGraph;

            static (RxGraph NewGraph, bool NewNodeCreated) AddRecursively(RxGraph graph, IInteractiveNode node)
            {
                var newNodeCreated = !graph.Nodes.ContainsKey(node.AggregateNodeId);
                graph = graph.TryAddNode(node.AggregateNodeId, new DataFlowNode(node.VisualOptions, Vector2.Zero));

                foreach (var upstreamNode in node.Upstreams.OrderBy(n => n.Owner.VisualOptions.Name))
                {
                    var result = AddRecursively(graph, upstreamNode.Owner);
                    var edgeId = new DataFlowEdgeId.StaticEdgeId(upstreamNode.Owner.AggregateNodeId, node.AggregateNodeId);

                    graph = newNodeCreated
                        ? result.NewGraph.AddEdge(
                            edgeId,
                            new DataFlowEdge.StaticEdge(),
                            edgeId.SourceId,
                            edgeId.TargetId)
                        : result.NewGraph;
                }

                return (graph, newNodeCreated);
            }
        }

        public static Vector4 GetBoundingBox(this RxGraph graph)
        {
            var min = graph.Nodes.Select(n => n.Value.Value.Position - (n.Value.Value.Options.Size / 2) * Vector2.One).Min();
            var max = graph.Nodes.Select(n => n.Value.Value.Position + (n.Value.Value.Options.Size / 2) * Vector2.One).Max();
            return new(min.X, min.Y, max.X - min.X, max.Y - min.Y);
        }
    }

    public abstract record DataFlowEdge
    {
        public record StaticEdge() : DataFlowEdge
        {
            public override string ToString() => "Static";
        }

        public record SubscriptionEdge(ImmutableList<ObservableEmissionWithTimestamp> Emissions) : DataFlowEdge
        {
            public override string ToString() => $"{Emissions.Count} emissions";
        }
    }

    public record ObservableEmissionWithTimestamp(
        DateTimeOffset Timestamp,
        ObservableEmission Emission);

    public record DataFlowNode(VisualOptions Options, Vector2 Position)
    {
        public override string ToString() => Options.Name;
    }

    public record DataFlowNodeId(IInteractiveNode Identity)
    {
        public override string ToString() => $"Node({Identity}-{Identity.GetHashCode()})";
    }

    /// <summary>
    /// Assumption: An ObserverNode is not the target of multiple subscriptions.
    /// Otherwise, ObserverId is not unique (think of multiple edges between an ObservableNode and an ObserverNode).
    /// Multiple edges between two ObservableNodes are possible because each subscription uses a separate InteractiveObserver.
    /// </summary>
    public abstract record DataFlowEdgeId
    {
        public record StaticEdgeId(
            DataFlowNodeId SourceId,
            DataFlowNodeId TargetId) : DataFlowEdgeId
        {
            public override string ToString() => $"StaticEdge({SourceId} → {TargetId})";
        }

        public record SubscriptionEdgeId(
            DataFlowNodeId SourceId,
            DataFlowNodeId TargetId,
            int SequenceNumber) : DataFlowEdgeId
        {
            public int VisualOffset => (SequenceNumber % 2 * 2 - 1) * (SequenceNumber / 2) * 10;
            public override string ToString() => $"SubscriptionEdge({SourceId} → {TargetId} | {SequenceNumber})";
        }
    }


    public abstract record RxInteractiveEvent
    {
        private RxInteractiveEvent() { }

        public record ObservableCreated(
            IInteractiveObservable Observable) : RxInteractiveEvent;

        public record SubscriberCreated(
            IInteractiveSubscription Subscriber) : RxInteractiveEvent;

        public record Subscribed(
            DataFlowEdgeId.SubscriptionEdgeId EdgeId,
            IInteractiveObserver Observer) : RxInteractiveEvent;

        public record Unsubscribed(
            DataFlowEdgeId.SubscriptionEdgeId EdgeId) : RxInteractiveEvent;

        public record ValueEmitted(
            DataFlowEdgeId EdgeId,
            ObservableEmission Emission) : RxInteractiveEvent;
    }

    public abstract record ObservableEmission
    {
        private ObservableEmission() { }

        public record Next(object Value) : ObservableEmission();
        public record Error(Exception Exception) : ObservableEmission();
        public record Completed() : ObservableEmission();
    }
}
