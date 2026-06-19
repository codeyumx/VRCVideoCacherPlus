using EmbedIO;
using EmbedIO.Files;
using EmbedIO.WebApi;
using Swan.Logging;
using ILogger = Serilog.ILogger;

namespace VRCVideoCacher.API;

public class WebServer
{
    private static EmbedIO.WebServer? _server;
    public static readonly ILogger Log = Program.Logger.ForContext<WebServer>();

    public static void Init()
    {
        _server?.Dispose();

        var indexPath = Path.Join(CacheManager.CachePath, "index.html");
        if (!File.Exists(indexPath))
            File.WriteAllText(indexPath, "VRCVideoCacher");

        _server = CreateWebServer(ConfigManager.Config.YtdlpWebServerUrl);

        // RunAsync returns a long-running task that completes when the server stops.
        // Fire-and-forget, but observe faults so port-in-use errors get logged.
        _server.RunAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                var ex = t.Exception?.GetBaseException();
                Log.Error(ex, "Web server failed: {Message}", ex?.Message);
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private static EmbedIO.WebServer CreateWebServer(string url)
    {
        try { Logger.UnregisterLogger<ConsoleLogger>(); } catch { /* Not registered */ }
        try { Logger.UnregisterLogger<WebServerLogger>(); } catch { /* Not registered */ }
        Logger.RegisterLogger<WebServerLogger>();

        var urls = new List<string>
        {
            "http://localhost:9696",
            "http://127.0.0.1:9696"
        };
        if (!urls.Contains(url))
            urls.Add(url);

        var server = new EmbedIO.WebServer(o => o
                .WithUrlPrefixes(urls)
                .WithMode(HttpListenerMode.EmbedIO))
            // First, we will configure our web server by adding Modules.
            .WithWebApi("/api", m => m
                .WithController<ApiController>())
            .WithStaticFolder("/", CacheManager.CachePath, true, m => m
                .WithContentCaching(true));

        // Listen for state changes.
        server.StateChanged += (_, e) => $"WebServer State: {e.NewState}".Info();
        server.OnUnhandledException += OnUnhandledException;
        server.OnHttpException += OnHttpException;
        return server;
    }

    public static void Stop()
    {
        try
        {
            _server?.Dispose();
            _server = null;
        }
        catch (Exception ex)
        {
            Log.Warning("Error stopping web server: {Message}", ex.Message);
        }
    }

    private static Task OnHttpException(IHttpContext context, IHttpException httpException)
    {
        Log.Information("OnHttpException Error Occured: {ErrorMessage}", httpException.Message!);
        return Task.CompletedTask;
    }

    private static Task OnUnhandledException(IHttpContext context, Exception exception)
    {
        Log.Information(exception, "OnUnhandledException Error Occured");
        return Task.CompletedTask;
    }
}