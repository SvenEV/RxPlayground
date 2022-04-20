using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace RxPlayground.RxInteractive
{
    public class InteractiveSubscription<T> : IDisposable
    {
        private readonly Subject<RxInteractiveEvent> eventsSubject = new();
        private readonly ITimeProvider timeProvider;
        private IDisposable? subscription;

        public IInteractiveObservable<T> Observable { get; }

        public InteractiveObserver<T> Observer { get; }

        public IObservable<RxInteractiveEvent> Events { get; }

        public InteractiveSubscription(IInteractiveObservable<T> observable, ObserverId observerId, ITimeProvider timeProvider)
        {
            this.timeProvider = timeProvider;

            Observable = observable;
            Observer = new InteractiveObserver<T>(observerId, timeProvider);
            Events = Observer.Events.Merge(eventsSubject);
        }

        public void Subscribe()
        {
            if (subscription is not null)
                throw new InvalidOperationException("Already subscribed");

            eventsSubject.OnNext(new RxInteractiveEvent.Subscribed(
                Timestamp: timeProvider.GetTimestamp(),
                ObservableId: Observable.Id,
                Observer: Observer));

            subscription = Observable.Subscribe(Observer);
        }

        public void Dispose()
        {
            if (subscription is not null)
            {
                subscription.Dispose();

                eventsSubject.OnNext(new RxInteractiveEvent.Unsubscribed(
                    Timestamp: timeProvider.GetTimestamp(),
                    ObserverId: Observer.Id));
            }

            eventsSubject.OnCompleted();
            eventsSubject.Dispose();
        }
    }
}
