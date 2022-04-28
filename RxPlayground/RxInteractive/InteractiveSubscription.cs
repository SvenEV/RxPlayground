using System.Collections.Immutable;
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
        private readonly IObserver<T>? underlyingObserver;
        private readonly Action<IDisposable>? onSubscribe;
        private IDisposable? subscription;

        public DataFlowNodeId AggregateNodeId { get; }

        public VisualOptions VisualOptions { get; }

        public IInteractiveObservablePort<T> Upstream { get; }

        public ImmutableList<IInteractiveObservablePort> Upstreams { get; }

        public IObservable<RxInteractiveEvent> Events => eventsSubject;

        IInteractiveObservablePort IInteractiveSubscription.TargetPort => Upstream;

        public InteractiveSubscription(IInteractiveObservablePort<T> upstream, IObserver<T>? underlyingObserver = null, string displayName = "Subscription", Action<IDisposable>? onSubscribe = null)
        {
            AggregateNodeId = new DataFlowNodeId(this);
            VisualOptions = new(displayName);
            Upstream = upstream;
            this.underlyingObserver = underlyingObserver;
            this.onSubscribe = onSubscribe;
            Upstreams = ImmutableList.Create<IInteractiveObservablePort>(Upstream);
            Upstream.SetTarget(this);
        }

        public void Subscribe()
        {
            if (subscription is not null)
                throw new InvalidOperationException("Already subscribed");

            subscription = Upstream.Subscribe(this);
            onSubscribe?.Invoke(subscription);
        }

        public void Dispose()
        {
            if (subscription is not null)
                subscription.Dispose();

            eventsSubject.OnCompleted();
            eventsSubject.Dispose();
        }

        public override string ToString() => "Subscription";

        public void OnCompleted()
        {
            underlyingObserver?.OnCompleted();
            subscription?.Dispose();
        }

        public void OnError(Exception error)
        {
            underlyingObserver?.OnError(error);
            subscription?.Dispose();
        }

        public void OnNext(T value)
        {
            underlyingObserver?.OnNext(value);
        }
    }
}
