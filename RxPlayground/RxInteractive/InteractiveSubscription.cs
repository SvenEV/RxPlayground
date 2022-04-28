using System.Collections.Immutable;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace RxPlayground.RxInteractive
{
    public interface IInteractiveSubscription : IInteractiveNode, IDisposable
    {
        /// <summary>
        /// The single parent/upstream observable.
        /// </summary>
        IInteractiveObservablePort TargetPort { get; }

        void Subscribe();
    }

    public class InteractiveSubscription<T> : IInteractiveSubscription, IObserver<T>
    {
        private readonly Subject<RxInteractiveEvent> eventsSubject = new();
        private readonly InteractiveObserver<T> observer;
        private IDisposable? subscription;

        public DataFlowNodeId AggregateNodeId { get; }

        public VisualOptions VisualOptions { get; }

        public IInteractiveObservablePort<T> Upstream { get; }

        public ImmutableList<IInteractiveObservablePort> Upstreams { get; }

        public IObservable<RxInteractiveEvent> Events { get; }

        IInteractiveObservablePort IInteractiveSubscription.TargetPort => Upstream;


        public InteractiveSubscription(IInteractiveObservablePort<T> upstream)
        {
            AggregateNodeId = new DataFlowNodeId(this);
            VisualOptions = new("Subscription");
            Upstream = upstream;
            Upstreams = ImmutableList.Create<IInteractiveObservablePort>(Upstream);

            Upstream.SetTarget(this);

            var edgeId = new DataFlowEdgeId.SubscriptionEdgeId(upstream.Owner.AggregateNodeId, AggregateNodeId, 0);
            observer = new InteractiveObserver<T>(edgeId, this);

            Events = observer.Events.Merge(eventsSubject);
        }

        public void Subscribe()
        {
            if (subscription is not null)
                throw new InvalidOperationException("Already subscribed");

            subscription = Upstream.Subscribe(observer);
        }

        public void Dispose()
        {
            if (subscription is not null)
                subscription.Dispose();

            eventsSubject.OnCompleted();
            eventsSubject.Dispose();
        }

        public override string ToString() => "Subscription";

        public void OnCompleted() => subscription?.Dispose();

        public void OnError(Exception error) => subscription?.Dispose();

        public void OnNext(T value) { }
    }
}
