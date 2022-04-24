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
        IInteractiveObservable TargetObservable { get; }
    }

    public class InteractiveSubscription<T> : IInteractiveSubscription
    {
        private readonly Subject<RxInteractiveEvent> eventsSubject = new();
        private readonly InteractiveObserver<T> observer;
        private IDisposable? subscription;

        public DataFlowNodeId AggregateNodeId { get; }

        public IInteractiveObservable<T> TargetObservable { get; }

        public ImmutableDictionary<string, IInteractiveObservable> Parents { get; }

        public IObservable<RxInteractiveEvent> Events { get; }

        IInteractiveObservable IInteractiveSubscription.TargetObservable => TargetObservable;


        public InteractiveSubscription(IInteractiveObservable<T> targetObservable, ITimeProvider timeProvider)
        {
            AggregateNodeId = new DataFlowNodeId(this);
            TargetObservable = targetObservable;
            Parents = ImmutableDictionary<string, IInteractiveObservable>.Empty.Add(nameof(TargetObservable), targetObservable);

            var edgeId = new DataFlowEdgeId(targetObservable.AggregateNodeId, AggregateNodeId, 0);
            observer = new InteractiveObserver<T>(edgeId, timeProvider);

            Events = observer.Events.Merge(eventsSubject);

            targetObservable.SetChild(this);
        }

        public void Subscribe()
        {
            if (subscription is not null)
                throw new InvalidOperationException("Already subscribed");

            subscription = TargetObservable.Subscribe(observer);
        }

        public void Dispose()
        {
            if (subscription is not null)
                subscription.Dispose();

            eventsSubject.OnCompleted();
            eventsSubject.Dispose();
        }
    }
}
