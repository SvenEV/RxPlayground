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

        public DataFlowEdgeId Id { get; }

        public IObservable<RxInteractiveEvent> Events => eventsSubject;

        public InteractiveObserver(DataFlowEdgeId id, IObserver<T>? underlyingObserver = null)
        {
            Id = id;
            this.underlyingObserver = underlyingObserver;
        }

        public async void OnCompleted()
        {
            eventsSubject.OnNext(new RxInteractiveEvent.ValueEmitted(
                EdgeId: Id,
                Emission: new ObservableEmission.Completed()));

            await Task.Delay(RxInteractiveSession.TimelineLength);
            underlyingObserver?.OnCompleted();
        }

        public async void OnError(Exception error)
        {
            eventsSubject.OnNext(new RxInteractiveEvent.ValueEmitted(
                EdgeId: Id,
                Emission: new ObservableEmission.Error(error)));

            await Task.Delay(RxInteractiveSession.TimelineLength);
            underlyingObserver?.OnError(error);
        }

        public async void OnNext(T value)
        {
            eventsSubject.OnNext(new RxInteractiveEvent.ValueEmitted(
                EdgeId: Id,
                Emission: new ObservableEmission.Next(value!)));

            await Task.Delay(RxInteractiveSession.TimelineLength);
            underlyingObserver?.OnNext(value);
        }
    }
}
