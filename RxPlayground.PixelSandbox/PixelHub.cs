using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;

namespace RxPlayground.PixelSandbox
{
    public class PixelHub : Hub
    {
        private readonly PixelService pixelService;

        public PixelHub(PixelService pixelService)
        {
            this.pixelService = pixelService;
        }

        public ChannelReader<PixelChangedEvent> Events(CancellationToken cancellationToken)
        {
            var channel = Channel.CreateUnbounded<PixelChangedEvent>();
            
            pixelService.Events.Subscribe(
                ev => channel.Writer.TryWrite(ev),
                ex => channel.Writer.TryComplete(ex),
                () => channel.Writer.TryComplete(),
                cancellationToken);

            return channel.Reader;
        }

        public void SetPixel(int x, int y, string color)
        {
            pixelService.SetPixel(x, y, color);
        }
    }
}
