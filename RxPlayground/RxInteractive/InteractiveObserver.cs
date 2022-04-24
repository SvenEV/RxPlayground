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
        ObserverId Id { get; }

        IObservable<RxInteractiveEvent> Events { get; }
    }

    public class InteractiveObserver<T> : IInteractiveObserver, IObserver<T>
    {
        private readonly Subject<RxInteractiveEvent> eventsSubject = new();
        private readonly IObserver<T>? underlyingObserver;
        private readonly ITimeProvider timeProvider;

        public ObserverId Id { get; }

        public IObservable<RxInteractiveEvent> Events => eventsSubject;

        public InteractiveObserver(ObserverId id, ITimeProvider timeProvider, IObserver<T>? underlyingObserver = null)
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
                ObserverId: Id,
                Emission: new ObservableEmission.Completed()));
        }

        public void OnError(Exception error)
        {
            underlyingObserver?.OnError(error);

            eventsSubject.OnNext(new RxInteractiveEvent.ValueEmitted(
                Timestamp: timeProvider.GetTimestamp(),
                ObserverId: Id,
                Emission: new ObservableEmission.Error(error)));
        }

        public void OnNext(T value)
        {
            underlyingObserver?.OnNext(value);

            eventsSubject.OnNext(new RxInteractiveEvent.ValueEmitted(
                Timestamp: timeProvider.GetTimestamp(),
                ObserverId: Id,
                Emission: new ObservableEmission.Next(value!)));
        }
    }
}
