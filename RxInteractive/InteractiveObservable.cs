using System.Collections.Immutable;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;

namespace RxPlayground.RxInteractive
{
    public interface IInteractiveObservable
    {
        ObservableId Id { get; }
        ImmutableDictionary<string, IInteractiveObservable> Parents { get; }
        IObservable<RxInteractiveEvent> Events { get; }
        object Source { get; }
    }

    public interface IInteractiveObservable<T> : IInteractiveObservable, IObservable<T>
    {
        new IObservable<T> Source { get; }
    }

    public static class InteractiveObservableExtensions
    {
        public static IInteractiveObservable<T> Inspect<T>(this IObservable<T> source, RxInteractiveSession session)
        {
            var id = new ObservableId(Guid.NewGuid());
            var wrappedObservable = new InteractiveObservable<T>(id, source, session.TimeProvider);

            session.AddEventSource(wrappedObservable.Events);

            session.OnNext(new RxInteractiveEvent.ObservableCreated(
                Timestamp: session.TimeProvider.GetTimestamp(),
                Observable: wrappedObservable));

            return wrappedObservable;
        }

        public static IDisposable Subscribe<T>(this IInteractiveObservable<T> source, RxInteractiveSession session)
        {
            var id = new ObserverId(Guid.NewGuid());
            var subscription = new InteractiveSubscription<T>(source, id, session.TimeProvider);
            session.AddEventSource(subscription.Events);
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

        public ObservableId Id { get; }

        public ImmutableDictionary<string, IInteractiveObservable> Parents { get; }

        public IObservable<RxInteractiveEvent> Events { get; }

        public IObservable<T> Source { get; }

        object IInteractiveObservable.Source => Source;

        public InteractiveObservable(ObservableId id, IObservable<T> source, ITimeProvider timeProvider)
        {
            Id = id;
            Source = source;
            this.timeProvider = timeProvider;

            Parents = GetParentsThroughReflection(source);

            Events = eventsSubject
                .Merge(observersSubject
                    .Select(observers => observers.Select(ob => ob.Events).Merge())
                    .Switch()
                );
        }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            var wrappedObserverId = new ObserverId(Guid.NewGuid());
            var wrappedObserver = new InteractiveObserver<T>(wrappedObserverId, timeProvider, observer);

            eventsSubject.OnNext(new RxInteractiveEvent.Subscribed(
                Timestamp: timeProvider.GetTimestamp(),
                ObservableId: Id,
                Observer: wrappedObserver));

            var subscription = Source.Subscribe(wrappedObserver);

            lock (observersSubject)
                observersSubject.OnNext(observersSubject.Value.Add(wrappedObserver));

            return Disposable.Create(() =>
            {
                subscription.Dispose();

                eventsSubject.OnNext(new RxInteractiveEvent.Unsubscribed(
                    Timestamp: timeProvider.GetTimestamp(),
                    ObserverId: wrappedObserverId));

                lock (observersSubject)
                    observersSubject.OnNext(observersSubject.Value.Remove(wrappedObserver));
            });
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
