using System.Reactive.Subjects;

namespace RxPlayground.RxInteractive
{
    public class DelegateSubject<TSource, TResult> : ISubject<TSource, TResult>
    {
        private readonly Func<IObserver<TResult>, IDisposable> subscribe;
        private readonly Action<TSource> onNext;
        private readonly Action<Exception> onError;
        private readonly Action onCompleted;

        public DelegateSubject(
            Func<IObserver<TResult>, IDisposable> subscribe,
            Action<TSource> onNext,
            Action<Exception> onError,
            Action onCompleted)
        {
            this.subscribe = subscribe;
            this.onNext = onNext;
            this.onError = onError;
            this.onCompleted = onCompleted;
        }

        public void OnCompleted() => onCompleted();
        public void OnError(Exception error) => onError(error);
        public void OnNext(TSource value) => onNext(value);
        public IDisposable Subscribe(IObserver<TResult> observer) => subscribe(observer);
    }
}
