using System.Text.Json;
using Bluetech.Display.Agent.Logging;
using Bluetech.Display.Agent.Server;
using Microsoft.Extensions.FileProviders;

namespace Bluetech.Display.Agent;

/// <summary>
/// L'applicazione: il servizio che legge la cassa e il server per i display.
/// - <c>/</c>: il display (la build di display/ nella cartella <see cref="AgentOptions.CartellaDisplay"/>);
///   <c>/display</c> rimanda lì, perché è l'indirizzo predefinito del Player;
/// - <c>/ws</c>: i messaggi del protocollo, via WebSocket;
/// - <c>/diagnostica</c>: cosa arriva dalla cassa, dal vivo; <c>/api/stato</c>: lo stesso in JSON.
/// </summary>
public static class AgentApp
{
    public const string ServiceName = "BluetechDisplayAgent";

    static readonly Dictionary<string, string> Switches = new()
    {
        ["--file"] = "Agent:FileCassa",
        ["--porta"] = "Agent:Porta",
        ["--replay"] = "Agent:Replay",
        ["--velocita"] = "Agent:VelocitaReplay",
        ["--ripeti"] = "Agent:RipetiReplay",
    };

    static readonly Lazy<string> DiagnosticPage = new(() =>
    {
        using var stream = typeof(AgentApp).Assembly.GetManifestResourceStream("diagnostica.html")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <param name="configure">Per i test: altra configurazione prima di costruire l'applicazione.</param>
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,   // da servizio la cartella corrente è System32
        });
        builder.Configuration.AddCommandLine(args, Switches);
        builder.Host.UseWindowsService(o => o.ServiceName = ServiceName);
        configure?.Invoke(builder);

        var options = builder.Configuration.GetSection("Agent").Get<AgentOptions>() ?? new AgentOptions();
        builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
        builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(AppContext.BaseDirectory, options.CartellaLog), options.GiorniLog));
        builder.WebHost.UseUrls(options.Porta == 0
            ? "http://127.0.0.1:0"
            : options.ApertoInRete ? $"http://*:{options.Porta}" : $"http://localhost:{options.Porta}");

        builder.Services.AddSingleton<DisplayHub>();
        builder.Services.AddSingleton<AgentEngine>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<AgentEngine>());

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

        app.Map("/ws", async (HttpContext context, AgentEngine engine, DisplayHub hub) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Qui serve un collegamento WebSocket.");
                return;
            }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var client = engine.Connect(socket, context.Connection.RemoteIpAddress?.ToString() ?? "?");
            await hub.RunAsync(client, context.RequestAborted);
        });
        app.MapGet("/api/stato", (AgentEngine engine) =>
            Results.Text(engine.Status().ToJsonString(Indented), "application/json; charset=utf-8"));
        app.MapGet("/diagnostica", () => Results.Text(DiagnosticPage.Value, "text/html; charset=utf-8"));
        app.MapGet("/display", () => Results.Redirect("/"));
        app.MapGet("/display/{**rest}", () => Results.Redirect("/"));

        var displayFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, options.CartellaDisplay));
        if (File.Exists(Path.Combine(displayFolder, "index.html")))
        {
            var files = new PhysicalFileProvider(displayFolder);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
        }
        else
        {
            app.MapGet("/", () => Results.Text(Placeholder(displayFolder), "text/html; charset=utf-8"));
        }
        return app;
    }

    static string Placeholder(string folder) => $$"""
        <!doctype html>
        <html lang="it"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Bluetech Display Agent</title><link rel="icon" href="data:,">
        <style>body{font:16px/1.5 system-ui,sans-serif;background:#0b1017;color:#e6eaf2;margin:0;padding:48px}a{color:#8fb8ff}</style>
        </head><body>
        <h1>Bluetech Display Agent</h1>
        <p>L'Agent è in funzione, ma il display non è installato in <code>{{System.Net.WebUtility.HtmlEncode(folder)}}</code>.</p>
        <p>Cosa arriva dalla cassa: <a href="/diagnostica">diagnostica</a>.</p>
        </body></html>
        """;
}
