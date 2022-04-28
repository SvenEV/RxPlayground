using RxPlayground.PixelSandbox.Client;
using System.Reactive.Linq;

var server = await PixelServerConnection.CreateAsync("http://localhost:5000/pixels");

server.Events
    .Where(ev => ev.Color == "red")
    .Subscribe(async ev =>
    {
        Console.Title = ev.ToString();
        await server.SetPixelAsync(ev.X, ev.Y + 1, "yellow");
    });

Console.ReadLine(); // to keep program running