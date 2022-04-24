using System.Collections.Immutable;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;

namespace RxPlayground.RxInteractive
{
    public interface IInteractiveObservable : IInteractiveNode
    {
        object UnderlyingObservable { get; }
        IInteractiveNode? Child { get; }
        void SetChild(IInteractiveNode child);
    }

    public interface IInteractiveObservable<T> : IInteractiveObservable, IObservable<T>
    {
        new IObservable<T> UnderlyingObservable { get; }
    }

    public static class InteractiveObservableExtensions
    {
        public static IInteractiveObservable<T> Inspect<T>(this IObservable<T> source, RxInteractiveSession session)
        {
            var wrappedObservable = new InteractiveObservable<T>(source, session.TimeProvider);

            session.OnNext(new RxInteractiveEvent.ObservableCreated(
                Timestamp: session.TimeProvider.GetTimestamp(),
                Observable: wrappedObservable));

            return wrappedObservable;
        }

        public static IDisposable Subscribe<T>(this IInteractiveObservable<T> source, RxInteractiveSession session)
        {
            var subscription = new InteractiveSubscription<T>(source, session.TimeProvider);

            session.OnNext(new RxInteractiveEvent.SubscriberCreated(
                Timestamp: session.TimeProvider.GetTimestamp(),
                Subscriber: subscription));

            subscription.Subscribe();
            return subscription;
        }
    }

    public class InteractiveObservable<T> : IInteractiveObservable<T>
    {
        private readonly BehaviorSubject<ImmutableList<InteractiveObserver<T>>> observersSubject =
            new(ImmutableList<InteractiveObserver<T>>.Empty);

        private readonly ITimeProvider timeProvider;
        private readonly Subject<RxInteractiveEvent> eventsSubject = new();

        public DataFlowNodeId AggregateNodeId { get; }

        public ImmutableDictionary<string, IInteractiveObservable> Parents { get; }

        public IInteractiveNode? Child { get; private set; }

        public IObservable<RxInteractiveEvent> Events { get; }

        public IObservable<T> UnderlyingObservable { get; }

        object IInteractiveObservable.UnderlyingObservable => UnderlyingObservable;

        public InteractiveObservable(IObservable<T> source)
        {
            UnderlyingObservable = source;
        }

        [Obsolete]
        public InteractiveObservable(IObservable<T> source, ITimeProvider timeProvider)
        {
            this.timeProvider = timeProvider;

            UnderlyingObservable = source;

            AggregateNodeId = new DataFlowNodeId(UnderlyingObservable);

            Parents = Introspection.GetParentsThroughReflection(source);

            foreach (var parent in Parents)
                parent.Value.SetChild(this);

            Events = eventsSubject
                .Merge(observersSubject
                    .Select(observers => observers.Select(ob => ob.Events).Merge())
                    .Switch()
                );
        }

        public void SetChild(IInteractiveNode child)
        {
            if (Child is not null)
                throw new InvalidOperationException($"Interactive observable already has a child. It cannot have multiple children.");

            Child = child;
        }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            if (Child is null)
                throw new InvalidOperationException("Inconsistent state: Received Subscribe() call from downstream observable, but Child is null");

            lock (observersSubject)
            {
                var edgeId = new DataFlowEdgeId(AggregateNodeId, Child.AggregateNodeId, observersSubject.Value.Count);
                var wrappedObserver = new InteractiveObserver<T>(edgeId, timeProvider, observer);

                eventsSubject.OnNext(new RxInteractiveEvent.Subscribed(
                    Timestamp: timeProvider.GetTimestamp(),
                    EdgeId: edgeId,
                    Observer: wrappedObserver));

                var subscription = UnderlyingObservable.Subscribe(wrappedObserver);

                observersSubject.OnNext(observersSubject.Value.Add(wrappedObserver));

                return Disposable.Create(() =>
                {
                    subscription.Dispose();

                    eventsSubject.OnNext(new RxInteractiveEvent.Unsubscribed(
                        Timestamp: timeProvider.GetTimestamp(),
                        EdgeId: edgeId));

                    lock (observersSubject)
                        observersSubject.OnNext(observersSubject.Value.Remove(wrappedObserver));
                });
            }
        }

        private static ImmutableDictionary<string, IInteractiveObservable> GetParentsThroughReflection(IObservable<T> observable)
        {
            var fields = observable.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

            return fields
                .Select(field => new { FieldName = field.Name, Value = field.GetValue(observable) })
                .Where(o => o.Value is IInteractiveObservable)
                .ToImmutableDictionary(o => o.FieldName, o => (IInteractiveObservable)o.Value!);
        }
    }
}
