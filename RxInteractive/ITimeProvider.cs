namespace RxPlayground.RxInteractive
{
    public interface ITimeProvider
    {
        DateTimeOffset GetTimestamp();
    }
}
