using Newtonsoft.Json;
using Serilog;

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
                var deserialized = JsonConvert.DeserializeObject<VvcConfig>(await req.Content.ReadAsStringAsync());
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

// Deserialized with Newtonsoft, so the mapping attributes have to be Newtonsoft's.
// These were [JsonPropertyName], which is System.Text.Json's and is ignored entirely by
// JsonConvert — the properties only bound at all because Newtonsoft matches names
// case-insensitively by default. Renaming either property would have silently stopped it
// binding, with no error anywhere.
public class VvcConfig
{
    [JsonProperty("motd")]
    public string Motd { get; set; } = string.Empty;

    // Intentionally not consumed: ApiController pins the prefetch retry count locally
    // rather than taking it from an upstream server. Kept so the payload shape is
    // documented and an unknown-property change is visible here.
    [JsonProperty("retryCount")]
    public int RetryCount { get; set; } = 7;
}