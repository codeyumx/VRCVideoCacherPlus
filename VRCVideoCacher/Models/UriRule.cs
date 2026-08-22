using System.Text.Json.Serialization;

namespace VRCVideoCacher.Models;

public enum RuleAction
{
    Direct,
    Resolve,
    Redirect,
    Rewrite,
    Block
}

public class UriRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public RuleAction Action { get; set; } = RuleAction.Resolve;

    // Cache options
    public bool Cache { get; set; } = false;
    public int? MaxResolution { get; set; } // e.g. 1080
    public int? MaxDurationMinutes { get; set; } // e.g. 120

    // Redirect / Rewrite action option
    public string RedirectTarget { get; set; } = string.Empty;

    // Site Integration Name (YouTube, PyPyDance, VRDancing, etc.)
    public string? Integration { get; set; } = null;

    public UriRule Clone()
    {
        return new UriRule
        {
            Id = Id,
            Enabled = Enabled,
            Name = Name,
            Pattern = Pattern,
            Action = Action,
            Cache = Cache,
            MaxResolution = MaxResolution,
            MaxDurationMinutes = MaxDurationMinutes,
            RedirectTarget = RedirectTarget,
            Integration = Integration
        };
    }

    public string GetActionSummary()
    {
        switch (Action)
        {
            case RuleAction.Direct:
                return "Direct";

            case RuleAction.Resolve:
                if (Cache)
                {
                    var parts = new List<string>();
                    if (MaxResolution.HasValue && MaxResolution.Value > 0)
                        parts.Add($"<{MaxResolution.Value}p");
                    if (MaxDurationMinutes.HasValue && MaxDurationMinutes.Value > 0)
                        parts.Add($"<{MaxDurationMinutes.Value}m");
                    if (parts.Count > 0)
                        return $"Resolve & Cache ({string.Join(", ", parts)})";
                    return "Resolve & Cache";
                }
                return "Resolve";

            case RuleAction.Redirect:
                return string.IsNullOrWhiteSpace(RedirectTarget)
                    ? "Redirect"
                    : $"Redirect to {RedirectTarget}";

            case RuleAction.Rewrite:
                return string.IsNullOrWhiteSpace(RedirectTarget)
                    ? "Rewrite"
                    : $"Rewrite to {RedirectTarget}";

            case RuleAction.Block:
                return "Block";

            default:
                return Action.ToString();
        }
    }
}
