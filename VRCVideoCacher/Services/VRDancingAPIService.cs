using Newtonsoft.Json;
using Serilog;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.Services;

public class VRDancingAPIService
{
    private const string VRDancingAPIBaseURL = "https://dbapi.vrdancing.club/";
    private static readonly ILogger Logger = Program.Logger.ForContext<VRDancingAPIService>();
    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri(VRDancingAPIBaseURL),
        DefaultRequestHeaders = { { "User-Agent", $"VRCVideoCacher {Program.Version}" } },
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static async Task<VRDSongInfo?> GetVideoInfo(string code)
    {
        var req = await HttpClient.GetAsync($"/api/v1/public/getsong?code={code}");
        var str = await req.Content.ReadAsStringAsync();
        // API sometimes returns a non-JSON error page (e.g. "error...") instead of a 4xx
        if (!req.IsSuccessStatusCode || !str.TrimStart().StartsWith('{'))
        {
            Logger.Warning("VRDancing API returned non-JSON response ({Status}) for code {Code}: {Body}",
                (int)req.StatusCode, code, str.Length > 200 ? str[..200] : str);
            return null;
        }
        return JsonConvert.DeserializeObject<VRDSongInfo>(str);
    }

    public static async Task DownloadMetadata(string code, string videoId)
    {
        // Prefer the local sheet-backed table — covers entries the API doesn't return.
        var sheet = DatabaseManager.GetVRDancingTitle(code);
        if (sheet != null && (!string.IsNullOrEmpty(sheet.Song) || !string.IsNullOrEmpty(sheet.Artist)))
        {
            DatabaseManager.AddVideoInfoCache(new VideoInfoCache
            {
                Id = videoId,
                Title = sheet.Song,
                Author = sheet.Artist,
                Type = UrlType.VRDancing
            });
        }

        try
        {
            var vrdData = await GetVideoInfo(code);
            if (vrdData == null)
                return;

            await ThumbnailManager.TrySaveThumbnail(videoId, vrdData.ThumbnailURL);
            DatabaseManager.AddVideoInfoCache(new VideoInfoCache
            {
                Id = videoId,
                Title = vrdData.Song,
                Author = vrdData.Artist,
                Type = UrlType.VRDancing
            });
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to download video metadata: {Ex}", ex.ToString());
        }
    }
}

public class VRDSongInfo
{
    public string Artist = string.Empty;
    public string Song = string.Empty;
    public string Instructor = string.Empty;
    public string ThumbnailURL = string.Empty;
    public string Hash = string.Empty;
}