namespace RxPlayground.RxInteractive
{
    public class SystemTimeProvider : ITimeProvider
    {
        public static readonly SystemTimeProvider Instance = new();
        private SystemTimeProvider() { }
        public DateTimeOffset GetTimestamp() => DateTimeOffset.Now;
    }
}
