using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace RxPlayground.PixelSandbox
{
    public class PixelService
    {
        private readonly string[,] colors;
        private readonly Subject<PixelChangedEvent> eventsSubject = new();

        public IObservable<PixelChangedEvent> Events { get; }

        public int Width { get; } = 32;

        public int Height { get; } = 32;

        public PixelService()
        {
            colors = new string[Width, Height];

            var initialState =
                (
                    from y in Enumerable.Range(0, Height)
                    from x in Enumerable.Range(0, Width)
                    select new PixelChangedEvent(x, y, colors[x, y])
                ).ToObservable();

            Events = initialState.Concat(eventsSubject);

            DoStuff();

            async Task DoStuff()
            {
                var random = new Random();

                while (true)
                {
                    SetPixel(random.Next(0, Width), random.Next(0, Height), "red");
                    await Task.Delay(1000);
                }
            }
        }

        public string GetPixel(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return "black";

            return colors[x, y];
        }

        public void SetPixel(int x, int y, string color)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return;

            lock (colors)
            {
                colors[x, y] = color;
                eventsSubject.OnNext(new PixelChangedEvent(x, y, color));
            }
        }
    }
}
