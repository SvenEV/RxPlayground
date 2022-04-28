using System.Numerics;
using System.Reactive.Disposables;
using System.Reactive.Subjects;

namespace RxPlayground.RxInteractive
{
    public record VisualOptions(
        string Name,
        string Color = "blue",
        int Size = 50,
        Vector2 Location = default);

    public interface IVisualizerObservable
    {
        VisualOptions VisualOptions { get; }
    }

    public class VisualizerObservable<T> : IVisualizerObservable, IConnectableObservable<T>
    {
        private readonly IObservable<T> _source;
        private readonly VisualOptions _visualOptions;

        public VisualOptions VisualOptions => _visualOptions;

        public VisualizerObservable(IObservable<T> source, VisualOptions visualOptions)
        {
            _source = source;
            _visualOptions = visualOptions;
        }

        public IDisposable Subscribe(IObserver<T> observer) => _source.Subscribe(observer);

        public IDisposable Connect() => _source is IConnectableObservable<T> connectableSource
            ? connectableSource.Connect()
            : Disposable.Empty;
    }

    public class VisualizerSubject<T> : IVisualizerObservable, ISubject<T>
    {
        private readonly ISubject<T> _source;
        private readonly VisualOptions _visualOptions;

        public VisualOptions VisualOptions => _visualOptions;

        public VisualizerSubject(ISubject<T> source, VisualOptions? visualOptions = null)
        {
            _source = source;
            _visualOptions = visualOptions;
        }

        public IDisposable Subscribe(IObserver<T> observer) => _source.Subscribe(observer);
        public void OnCompleted() => _source.OnCompleted();
        public void OnError(Exception error) => _source.OnError(error);
        public void OnNext(T value) => _source.OnNext(value);
    }

    public static class VisualizerObservableExtensions
    {
        public static IConnectableObservable<T> Visualize<T>(this IObservable<T> source, string name, VisualOptions? options = null) =>
            new VisualizerObservable<T>(source, (options ?? new(name)) with { Name = name });

        public static ISubject<T> Visualize<T>(this ISubject<T> source, string name, VisualOptions? options = null) =>
            new VisualizerSubject<T>(source, (options ?? new(name)) with { Name = name });
    }
}
