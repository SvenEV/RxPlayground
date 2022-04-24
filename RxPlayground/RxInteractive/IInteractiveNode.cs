using System.Collections.Immutable;

namespace RxPlayground.RxInteractive
{
    public interface IInteractiveNode
    {
        DataFlowNodeId AggregateNodeId { get; }
        ImmutableList<IInteractiveObservablePort> Upstreams { get; }
        IObservable<RxInteractiveEvent> Events { get; }
    }
}
