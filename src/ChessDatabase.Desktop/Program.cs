using ChessDatabase.Api;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Photino.NET;

namespace ChessDatabase.Desktop;

/// <summary>
/// Desktop shell (ADR 0001): hosts the backend on a private loopback port and shows the
/// Angular UI in a native window (Photino, which uses WebView2 on Windows).
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var builder = ChessDatabaseApp.CreateBuilder(args, new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = ChessDatabaseApp.Build(builder);
        app.StartAsync().GetAwaiter().GetResult();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();

        var window = new PhotinoWindow()
            .SetLogVerbosity(0)
            .SetTitle("Chess Database")
            .SetUseOsDefaultSize(false)
            .SetSize(1440, 900)
            .Center()
            .Load(new Uri(address));

        window.WaitForClose();
        app.StopAsync().GetAwaiter().GetResult();
    }
}
