using System.Collections.Immutable;
using System.Reactive.Subjects;

namespace RxPlayground.RxInteractive
{
    public record SubscriptionState(
        DateTimeOffset TimeOfSubscription,
        ImmutableList<ObservableEmission> Emissions
    );

    public interface IInteractiveObserver
    {
        DataFlowEdgeId Id { get; }

        IObservable<RxInteractiveEvent> Events { get; }
    }

    public class InteractiveObserver<T> : IInteractiveObserver, IObserver<T>
    {
        private readonly Subject<RxInteractiveEvent> eventsSubject = new();
        private readonly IObserver<T>? underlyingObserver;
        private readonly ITimeProvider timeProvider;

        public DataFlowEdgeId Id { get; }

        public IObservable<RxInteractiveEvent> Events => eventsSubject;

        public InteractiveObserver(DataFlowEdgeId id, ITimeProvider timeProvider, IObserver<T>? underlyingObserver = null)
        {
            Id = id;
            this.timeProvider = timeProvider;
            this.underlyingObserver = underlyingObserver;
        }

        public void OnCompleted()
        {
            underlyingObserver?.OnCompleted();

            eventsSubject.OnNext(new RxInteractiveEvent.ValueEmitted(
                Timestamp: timeProvider.GetTimestamp(),
                EdgeId: Id,
                Emission: new ObservableEmission.Completed()));
        }

        public void OnError(Exception error)
        {
            underlyingObserver?.OnError(error);

            eventsSubject.OnNext(new RxInteractiveEvent.ValueEmitted(
                Timestamp: timeProvider.GetTimestamp(),
                EdgeId: Id,
                Emission: new ObservableEmission.Error(error)));
        }

        public void OnNext(T value)
        {
            underlyingObserver?.OnNext(value);

            eventsSubject.OnNext(new RxInteractiveEvent.ValueEmitted(
                Timestamp: timeProvider.GetTimestamp(),
                EdgeId: Id,
                Emission: new ObservableEmission.Next(value!)));
        }
    }
}
