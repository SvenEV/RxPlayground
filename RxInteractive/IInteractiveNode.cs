using System.Collections.Immutable;

namespace RxPlayground.RxInteractive
{
    public interface IInteractiveNode
    {
        DataFlowNodeId AggregateNodeId { get; }
        ImmutableDictionary<string, IInteractiveObservable> Parents { get; }
        IObservable<RxInteractiveEvent> Events { get; }
    }
}
