using System.Collections.Immutable;
using System.Reactive.Linq;
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

    public class InteractiveObserver<T> : IInteractiveObserver, IObserver<T>, IDisposable
    {
        public static readonly TimeSpan DelayBetweenEmissions = TimeSpan.FromMilliseconds(100);

        private readonly IObserver<T>? underlyingObserver;
        private readonly Subject<RxInteractiveEvent.ValueEmitted> eventsSubject = new();
        private readonly IDisposable eventsSubscription;

        public DataFlowEdgeId Id { get; }

        public IObservable<RxInteractiveEvent> Events { get; }

        public InteractiveObserver(DataFlowEdgeId id, IObserver<T>? underlyingObserver = null)
        {
            Id = id;
            this.underlyingObserver = underlyingObserver;

            var emissionsWithDelayInBetween = eventsSubject
                .Select(emission => Observable.Return(emission).Concat(Observable.Empty<RxInteractiveEvent.ValueEmitted>().Delay(DelayBetweenEmissions)))
                .Concat();

            Events = emissionsWithDelayInBetween;

            eventsSubscription = emissionsWithDelayInBetween
                .Delay(RxInteractiveSession.TimelineLength)
                .Subscribe(ev =>
                {
                    switch (ev.Emission)
                    {
                        case ObservableEmission.Next next:
                            underlyingObserver?.OnNext((T)next.Value);
                            break;

                        case ObservableEmission.Completed:
                            underlyingObserver?.OnCompleted();
                            break;

                        case ObservableEmission.Error error:
                            underlyingObserver?.OnError(error.Exception);
                            break;
                    }
                });
        }

        public async void OnCompleted()
        {
            eventsSubject.OnNext(new RxInteractiveEvent.ValueEmitted(
                EdgeId: Id,
                Emission: new ObservableEmission.Completed()));

            //await Task.Delay(RxInteractiveSession.TimelineLength);
            //underlyingObserver?.OnCompleted();
        }

        public async void OnError(Exception error)
        {
            eventsSubject.OnNext(new RxInteractiveEvent.ValueEmitted(
                EdgeId: Id,
                Emission: new ObservableEmission.Error(error)));

            //await Task.Delay(RxInteractiveSession.TimelineLength);
            //underlyingObserver?.OnError(error);
        }

        public async void OnNext(T value)
        {
            eventsSubject.OnNext(new RxInteractiveEvent.ValueEmitted(
                EdgeId: Id,
                Emission: new ObservableEmission.Next(value!)));

            //await Task.Delay(RxInteractiveSession.TimelineLength);
            //underlyingObserver?.OnNext(value);
        }

        public void Dispose()
        {
            eventsSubscription.Dispose();
            eventsSubject.Dispose();
        }
    }
}
