using VRCVideoCacher.Models;

namespace VRCVideoCacher;

/// <summary>
/// The rule list a fresh installation starts with.
///
/// Deliberately a plain static class with no state and no static constructor: touching
/// PlusConfigManager runs its initialiser, which reads and writes PlusConfig.json in the
/// user's profile. Keeping the defaults here means they can be exercised by tests without
/// any of that happening.
/// </summary>
public static class DefaultRules
{
    // Dropbox share links always carry a dl=0 parameter, but not always as the only one:
    // the current /scl/fi/ format is "?rlkey=...&st=...&dl=0". Flipping that one parameter
    // to dl=1 is what turns the HTML preview page into the actual file, and rlkey must be
    // preserved, so the whole query can't just be replaced.
    internal const string DropboxForceDownloadPattern =
        @"^(https?:\/\/(?:[a-zA-Z0-9-]+\.)*dropbox\.com\/[^#]*[?&])dl=0(&[^#]*)?$";
    internal const string DropboxForceDownloadTarget = "${1}dl=1${2}";

    // Separate rule for a link that has been trimmed down to a bare path: there is no
    // parameter to flip, so dl=1 has to be appended as a new query string. Deliberately
    // does not match a URL that already has a query — one that carries dl=1 or raw=1 is
    // already a direct link, and one that carries neither is left alone rather than guessed at.
    internal const string DropboxAppendDownloadPattern =
        @"^(https?:\/\/(?:[a-zA-Z0-9-]+\.)*dropbox\.com\/[^?#]+)$";
    internal const string DropboxAppendDownloadTarget = "${1}?dl=1";

    // Shipped in earlier versions and wrong: the lazy (.*?) had to expand past the whole
    // query before the anchor could match, so the optional (?:\?dl=0)? never participated
    // for any link with more than one parameter. The target then appended a second "?",
    // producing "...&dl=0?dl=1". Existing installs carry their own copy of the rule list,
    // so the corrected default only reaches them via the migration below.
    internal const string LegacyDropboxPattern =
        @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*dropbox\.com\/(.*?)(?:\?dl=0)?$";

    public static List<UriRule> Create()
    {
        return
        [
            new UriRule
            {
                Name = "VRDancing EU to NA Redirect",
                Pattern = @"^https?:\/\/eu2\.vrdancing\.club\/weekend\/(.*)$",
                Action = RuleAction.Redirect,
                RedirectTarget = "https://na2.vrdancing.club/weekend/$1",
                Enabled = false
            },
            new UriRule
            {
                Name = "YouTube Music Redirect",
                Pattern = @"^https?:\/\/music\.youtube\.com\/(?:watch|playlist)?\?(?:.*?&)?v=([^&]+).*$",
                Action = RuleAction.Redirect,
                RedirectTarget = "https://youtube.com/watch?v=$1",
                Enabled = false
            },
            new UriRule
            {
                Name = "Dropbox Share Rewrite",
                Pattern = DropboxForceDownloadPattern,
                Action = RuleAction.Rewrite,
                RedirectTarget = DropboxForceDownloadTarget,
                Enabled = true
            },
            new UriRule
            {
                Name = "Dropbox Direct Download",
                Pattern = DropboxAppendDownloadPattern,
                Action = RuleAction.Rewrite,
                RedirectTarget = DropboxAppendDownloadTarget,
                Enabled = true
            },
            new UriRule
            {
                Name = "Google Drive File Rewrite",
                Pattern = @"^https?:\/\/drive\.google\.com\/file\/d\/([^\/]+)(?:\/.*)?$",
                Action = RuleAction.Rewrite,
                RedirectTarget = "https://drive.google.com/uc?export=download&id=$1",
                Enabled = true
            },
            new UriRule
            {
                Name = "MightyGym CDN Direct",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*mightygymcdn\.nyc3\.cdn\.digitaloceanspaces\.com(?:[\/?#]|$)",
                Action = RuleAction.Direct,
                Enabled = true
            },
            new UriRule
            {
                Name = "Illumination Media Direct",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*(?:imvrcdn\.com|illumination\.media)(?:[\/?#]|$)",
                Action = RuleAction.Direct,
                Enabled = true
            },
            new UriRule
            {
                Name = "Virtual Film Institute Direct",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*virtualfilm\.institute(?:[\/?#]|$)",
                Action = RuleAction.Direct,
                Enabled = true
            },
            new UriRule
            {
                Name = "Block Rickrolls",
                Pattern = @"^https?://(?:www\.)?youtube\.com/watch\?v=(?:dQw4w9WgXcQ|jzmz6K8K4L0|XfELJU1mRMg)",
                Action = RuleAction.Block,
                // Ships disabled: Block now genuinely prevents playback, and a fresh install
                // silently refusing specific videos is a surprise, not a default. Existing
                // configs keep whatever the user already has.
                Enabled = false
            },
            new UriRule
            {
                Name = "YouTube",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*(?:youtube\.com|youtu\.be|youtube-nocookie\.com)(?:[\/?#]|$)",
                Action = RuleAction.Resolve,
                Cache = true,
                MaxResolution = 1080,
                MaxDurationMinutes = 120,
                Enabled = true,
                Integration = "YouTube"
            },
            new UriRule
            {
                Name = "PyPyDance",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*pypy\.dance(?:[\/?#]|$)",
                Action = RuleAction.Resolve,
                Cache = true,
                Enabled = true,
                Integration = "PyPyDance"
            },
            new UriRule
            {
                Name = "VRDancing",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*vrdancing\.club(?:[\/?#]|$)",
                Action = RuleAction.Resolve,
                Cache = true,
                Enabled = true,
                Integration = "VRDancing"
            },
            new UriRule
            {
                Name = "Everything else",
                Pattern = @".*",
                Action = RuleAction.Resolve,
                Cache = false,
                Enabled = true
            }
        ];
    }
}
