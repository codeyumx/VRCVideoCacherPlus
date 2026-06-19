using Serilog;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;

namespace VRCVideoCacher.Services;

public static class VRDancingSheetService
{
    private const string SheetCsvUrl =
        "https://docs.google.com/spreadsheets/d/14Nh9M1r__S-BHS00j6hi0otF4A63LXhoX9ISFZv7nrs/export?format=csv&gid=0";

    private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(24);
    private static readonly string MarkerPath = Path.Join(Database.Database.CacheDir, "VRDancingSheet.lastsync");
    private static readonly ILogger Logger = Program.Logger.ForContext(typeof(VRDancingSheetService));

    // Hard caps so a sheet can't OOM the app.
    // The real sheet is well under 1 MB and a few thousand rows.
    private const long MaxResponseBytes = 8L * 1024 * 1024;   // 8 MB
    private const int MaxRows = 50_000;
    private const int MaxFieldLength = 1024;                  // chars

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        MaxResponseContentBufferSize = MaxResponseBytes
    };

    public static void StartBackgroundSync()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    if (IsStale())
                        await SyncOnceAsync();
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "VRDancing sheet sync failed");
                }

                await Task.Delay(SyncInterval);
            }
            // ReSharper disable once FunctionNeverReturns
        });
    }

    private static bool IsStale()
    {
        if (!File.Exists(MarkerPath)) return true;
        return DateTime.UtcNow - File.GetLastWriteTimeUtc(MarkerPath) >= SyncInterval;
    }

    private static async Task SyncOnceAsync()
    {
        Logger.Information("Syncing VRDancing title sheet...");

        // Stream-read with a hard byte ceiling so a sheet can't OOM us.
        using var resp = await HttpClient.GetAsync(SheetCsvUrl, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength is { } len && len > MaxResponseBytes)
        {
            Logger.Warning("VRDancing sheet rejected: Content-Length {Len} exceeds {Max}", len, MaxResponseBytes);
            return;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var ms = new MemoryStream();
        var buf = new byte[16 * 1024];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buf)) > 0)
        {
            total += read;
            if (total > MaxResponseBytes)
            {
                Logger.Warning("VRDancing sheet rejected: body exceeds {Max} bytes", MaxResponseBytes);
                return;
            }
            ms.Write(buf, 0, read);
        }

        var csv = System.Text.Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        var rows = ParseCsv(csv);

        // Find header row: instructor,song,artist,code
        int headerIdx = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Count >= 4 &&
                r[0].Trim().Equals("instructor", StringComparison.OrdinalIgnoreCase) &&
                r[1].Trim().Equals("song", StringComparison.OrdinalIgnoreCase) &&
                r[2].Trim().Equals("artist", StringComparison.OrdinalIgnoreCase) &&
                r[3].Trim().Equals("code", StringComparison.OrdinalIgnoreCase))
            {
                headerIdx = i;
                break;
            }
        }
        if (headerIdx < 0)
        {
            Logger.Warning("VRDancing sheet: header row not found, skipping sync.");
            return;
        }

        var titles = new Dictionary<string, VRDancingTitle>();
        for (var i = headerIdx + 1; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Count < 4) continue;
            var code = SanitizeField(r[3]);
            if (string.IsNullOrEmpty(code)) continue;
            titles[code] = new VRDancingTitle
            {
                Code = code,
                Instructor = SanitizeField(r[0]),
                Song = SanitizeField(r[1]),
                Artist = SanitizeField(r[2])
            };
        }

        DatabaseManager.ReplaceVRDancingTitles(titles.Values);
        File.WriteAllText(MarkerPath, DateTime.UtcNow.ToString("O"));
        Logger.Information("VRDancing sheet synced: {Count} entries", titles.Count);
    }

    // Trim, cap length, and strip control chars (incl. embedded NUL/newlines that would corrupt UI rendering).
    private static string SanitizeField(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var trimmed = raw.Trim();
        if (trimmed.Length > MaxFieldLength) trimmed = trimmed[..MaxFieldLength];
        var sb = new System.Text.StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            if (!char.IsControl(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    // RFC 4180-ish CSV parser: handles quoted fields with embedded commas, quotes, and newlines.
    // Bounded by MaxRows and MaxFieldLength so a pathological sheet can't OOM the parser.
    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var current = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;

        void CommitField()
        {
            current.Add(field.Length > MaxFieldLength ? field.ToString(0, MaxFieldLength) : field.ToString());
            field.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        if (field.Length < MaxFieldLength) field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (field.Length < MaxFieldLength) field.Append(c);
                }
            }
            else
            {
                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        CommitField();
                        break;
                    case '\r':
                        break;
                    case '\n':
                        CommitField();
                        rows.Add(current);
                        if (rows.Count >= MaxRows) return rows;
                        current = new List<string>();
                        break;
                    default:
                        if (field.Length < MaxFieldLength) field.Append(c);
                        break;
                }
            }
        }

        if (field.Length > 0 || current.Count > 0)
        {
            CommitField();
            rows.Add(current);
        }
        return rows;
    }
}
