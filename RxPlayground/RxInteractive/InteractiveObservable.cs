using System.Collections.Immutable;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;

namespace RxPlayground.RxInteractive
{
    public interface IInteractiveObservable : IInteractiveNode
    {
        object UnderlyingObservable { get; }
        IReadOnlyList<IInteractiveObservablePort> Downstreams { get; }
        IInteractiveObservablePort AddDownstream();

        public static IInteractiveObservable Create(object sourceObservable, ImmutableList<IInteractiveObservablePort> upstreams)
        {
            var observableElementType = Introspection.GetObservableElementType(sourceObservable);

            return (IInteractiveObservable)Activator.CreateInstance(
                typeof(InteractiveObservable<>).MakeGenericType(observableElementType),
                sourceObservable,
                upstreams
            )!;
        }
    }

    public interface IInteractiveObservable<T> : IInteractiveObservable
    {
        new IObservable<T> UnderlyingObservable { get; }
        new IInteractiveObservablePort<T> AddDownstream();
    }

    public static class ObservableExtensions
    {
        public static IObservable<Timestamped<T>> Timestamped<T>(this IObservable<T> source, ITimeProvider timeProvider) =>
            source.Select(value => System.Reactive.Timestamped.Create(value, timeProvider.GetTimestamp()));
    }

    public class InteractiveObservable<T> : IInteractiveObservable<T>
    {
        private readonly Subject<IInteractiveObservablePort<T>> portsSubject = new();
        private readonly List<IInteractiveObservablePort> downstreams = new();

        public DataFlowNodeId AggregateNodeId { get; }

        public ImmutableList<IInteractiveObservablePort> Upstreams { get; }

        public IReadOnlyList<IInteractiveObservablePort> Downstreams => downstreams;

        public IObservable<RxInteractiveEvent> Events { get; }

        public IObservable<T> UnderlyingObservable { get; }

        object IInteractiveObservable.UnderlyingObservable => UnderlyingObservable;

        public InteractiveObservable(IObservable<T> source, ImmutableList<IInteractiveObservablePort> upstreams)
        {
            UnderlyingObservable = source;

            AggregateNodeId = new DataFlowNodeId(this);

            Upstreams = upstreams;

            foreach (var up in upstreams)
                up.SetTarget(this);

            Events = portsSubject
                .Scan(ImmutableList<IInteractiveObservablePort<T>>.Empty, (list, newPort) => list.Add(newPort))
                .Select(list => list.Select(port => port.Events).Merge())
                .Switch()
                .Publish()
                .AutoConnect(0);
        }

        public IInteractiveObservablePort<T> AddDownstream()
        {
            var newPort = new InteractiveObservablePort<T>(this);
            downstreams.Add(newPort);
            portsSubject.OnNext(newPort);
            return newPort;
        }

        IInteractiveObservablePort IInteractiveObservable.AddDownstream() => AddDownstream();

        public override string ToString() => UnderlyingObservable.GetType().Name;
    }


    public interface IInteractiveObservablePort
    {
        IInteractiveObservable Owner { get; }
        IInteractiveNode? Target { get; }
        IObservable<RxInteractiveEvent> Events { get; }

        void SetTarget(IInteractiveNode target);
    }

    public interface IInteractiveObservablePort<T> : IInteractiveObservablePort, IConnectableObservable<T>
    {
        new IInteractiveObservable<T> Owner { get; }
    }

    public class InteractiveObservablePort<T> : IInteractiveObservablePort<T>
    {
        private readonly BehaviorSubject<ImmutableList<InteractiveObserver<T>>> observersSubject =
            new(ImmutableList<InteractiveObserver<T>>.Empty);

        private readonly Subject<RxInteractiveEvent> eventsSubject = new();

        public IInteractiveObservable<T> Owner { get; }
        public IInteractiveNode? Target { get; private set; }
        public IObservable<RxInteractiveEvent> Events { get; }

        IInteractiveObservable IInteractiveObservablePort.Owner => Owner;

        public InteractiveObservablePort(IInteractiveObservable<T> owner)
        {
            Owner = owner;

            Events = eventsSubject
                .Merge(observersSubject
                    .Select(observers => observers.Select(ob => ob.Events).Merge())
                    .Switch()
                );
        }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            if (Target is null)
                throw new InvalidOperationException("Inconsistent state: Received Subscribe() call from downstream observable, but Target is null");

            lock (observersSubject)
            {
                var edgeId = new DataFlowEdgeId.SubscriptionEdgeId(Owner.AggregateNodeId, Target.AggregateNodeId, observersSubject.Value.Count);
                var wrappedObserver = new InteractiveObserver<T>(edgeId, observer);

                eventsSubject.OnNext(new RxInteractiveEvent.Subscribed(
                    EdgeId: edgeId,
                    Observer: wrappedObserver));

                var subscription = Owner.UnderlyingObservable.Subscribe(wrappedObserver);

                observersSubject.OnNext(observersSubject.Value.Add(wrappedObserver));

                return Disposable.Create(() =>
                {
                    subscription.Dispose();

                    eventsSubject.OnNext(new RxInteractiveEvent.Unsubscribed(
                        EdgeId: edgeId));

                    lock (observersSubject)
                        observersSubject.OnNext(observersSubject.Value.Remove(wrappedObserver));
                });
            }
        }

        public void SetTarget(IInteractiveNode target)
        {
            if (Target is not null)
                throw new InvalidOperationException("Cannot set target twice");

            Target = target;
        }

        public IDisposable Connect()
        {
            if (Owner.UnderlyingObservable is IConnectableObservable<T> connectableObservable)
                return connectableObservable.Connect();

            return Disposable.Create(() => { });
        }
    }
}
