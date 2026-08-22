using System.Text.Json.Serialization;
using Serilog;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Services;

public class VvcConfigService
{
    public static VvcConfig CurrentConfig = new();
    public static event Action? OnApiConfigChanged;
    public static ILogger Logger = Log.ForContext<VvcConfigService>();

    // Short timeout: this runs on the startup path and there is nothing here worth
    // delaying the application for. The 100s default would have stalled launch that long
    // if the endpoint hung.
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", $"VRCVideoCacher v{Program.Version}" } },
        Timeout = TimeSpan.FromSeconds(10)
    };

    public static async Task GetConfig()
    {
        try
        {
            var req = await HttpClient.GetAsync("https://vvc.ellyvr.dev/api/v1/config");
            if (req.IsSuccessStatusCode)
            {
                var deserialized = Json.Deserialize<VvcConfig>(await req.Content.ReadAsStringAsync());
                if (deserialized != null)
                {
                    CurrentConfig = deserialized;
                    OnApiConfigChanged?.Invoke();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to get config from Video Cacher API.");
        }
        
    }
}

public class VvcConfig
{
    [JsonPropertyName("motd")]
    public string Motd { get; set; } = string.Empty;

    // Intentionally not consumed: ApiController pins the prefetch retry count locally
    // rather than taking it from an upstream server. Kept so the payload shape is
    // documented and an unknown-property change is visible here.
    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 7;
}