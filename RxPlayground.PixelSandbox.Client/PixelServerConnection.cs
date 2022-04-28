using Microsoft.AspNetCore.SignalR.Client;

namespace RxPlayground.PixelSandbox.Client
{
    public class PixelServerConnection
    {
        private readonly HubConnection hubConnection;

        public IObservable<PixelChangedEvent> Events { get; }

        public static async Task<PixelServerConnection> CreateAsync(string url)
        {
            Console.WriteLine($"Connecting to {url}...");

            var hubConnection = new HubConnectionBuilder()
                .WithUrl(url)
                .WithAutomaticReconnect()
                .Build();

            await hubConnection.StartAsync();

            Console.WriteLine("Connected!");

            return new(hubConnection);
        }

        public PixelServerConnection(HubConnection hubConnection)
        {
            this.hubConnection = hubConnection;
            Events = hubConnection.StreamAsync<PixelChangedEvent>("Events").ToObservable();
        }

        public async Task SetPixelAsync(int x, int y, string color)
        {
            await hubConnection.InvokeAsync("SetPixel", x, y, color);
        }
    }
}
