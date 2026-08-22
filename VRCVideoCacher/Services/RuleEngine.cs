using System.Text.RegularExpressions;
using System.Web;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.Services;

public class RuleEvaluationResult
{
    public UriRule MatchedRule { get; set; } = null!;
    public string FinalUrl { get; set; } = string.Empty;
    public RuleAction Action { get; set; } = RuleAction.Direct;
    public int? MaxResolution { get; set; }
    public int? MaxDurationMinutes { get; set; }
    public string RedirectUrl { get; set; } = string.Empty;
}

public static class RuleEngine
{
    private static readonly Serilog.ILogger Log = Program.Logger.ForContext(typeof(RuleEngine));

    public static event Action<string>? OnRuleMatched;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> RegexCache = new();

    // Rules are few; this only exists so that a config edited into thousands of distinct
    // patterns cannot grow the cache without bound.
    private const int MaxCachedRegexes = 256;

    /// <summary>
    /// Returns a cached <see cref="Regex"/> for a rule pattern.
    ///
    /// Every rule was previously constructed fresh for every URL — a dozen pattern parses
    /// per video request, and another dozen per keystroke in the Rules tab's live matcher.
    /// Compiling once per distinct pattern instead makes evaluation essentially free.
    ///
    /// CultureInvariant matters as much as the caching: with IgnoreCase alone, case folding
    /// follows the current culture, so under a Turkish locale "I" does not match "i" and
    /// patterns like YOUTUBE\.COM quietly stop matching.
    /// </summary>
    public static Regex GetRegex(string pattern)
    {
        if (RegexCache.TryGetValue(pattern, out var cached))
            return cached;

        var regex = new Regex(
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(500));

        if (RegexCache.Count >= MaxCachedRegexes)
            RegexCache.Clear();

        RegexCache[pattern] = regex;
        return regex;
    }

    public static RuleEvaluationResult EvaluateUrl(string requestUrl)
    {
        var currentUrl = requestUrl.Trim();
        var rules = PlusConfigManager.Config.UriRules;

        if (rules == null || rules.Count == 0)
        {
            rules = ConfigModel.GetDefaultRules();
        }

        foreach (var rule in rules)
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Pattern))
                continue;

            try
            {
                var match = GetRegex(rule.Pattern).Match(currentUrl);

                if (!match.Success)
                    continue;

                Log.Information("URL '{URL}' matched rule '{RuleName}' ({Action})", currentUrl, rule.Name, rule.Action);
                OnRuleMatched?.Invoke(rule.Id);

                var result = new RuleEvaluationResult
                {
                    MatchedRule = rule,
                    Action = rule.Action,
                    FinalUrl = currentUrl,
                    MaxResolution = rule.MaxResolution,
                    MaxDurationMinutes = rule.MaxDurationMinutes
                };

                switch (rule.Action)
                {
                    case RuleAction.Rewrite:
                        var expandedRewrite = ExpandTemplate(rule.RedirectTarget, currentUrl, match);
                        Log.Information("Rule '{RuleName}' rewritten URL: '{Original}' -> '{Rewritten}'", rule.Name, currentUrl, expandedRewrite);
                        currentUrl = expandedRewrite;
                        // Continue loop so lower rules evaluate against the rewritten URL
                        break;

                    case RuleAction.Redirect:
                        var expandedRedirect = ExpandTemplate(rule.RedirectTarget, currentUrl, match);
                        Log.Information("Rule redirect expanded: '{Target}'", expandedRedirect);
                        result.RedirectUrl = expandedRedirect;
                        result.FinalUrl = expandedRedirect;
                        return result;

                    case RuleAction.Block:
                    case RuleAction.Resolve:
                    case RuleAction.Direct:
                    default:
                        result.FinalUrl = currentUrl;
                        return result;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error evaluating rule pattern '{Pattern}' for URL '{URL}'", rule.Pattern, currentUrl);
            }
        }

        // Fallback default if no rule matched
        return new RuleEvaluationResult
        {
            MatchedRule = new UriRule { Name = "Everything else", Action = RuleAction.Resolve, Cache = false },
            Action = RuleAction.Resolve,
            FinalUrl = currentUrl
        };
    }

    public static string ExpandTemplate(string template, string originalUrl, Match regexMatch)
    {
        if (string.IsNullOrEmpty(template))
            return originalUrl;

        var result = template;

        // 1. First substitute regex capture groups ($0, $1, $2, etc.)
        if (regexMatch.Success)
        {
            result = regexMatch.Result(result);
        }

        // 2. Parse URL for token substitution {url...}
        Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri);

        // Token replacements
        result = result.Replace("{url}", originalUrl);
        result = result.Replace("{url.raw}", originalUrl);
        result = result.Replace("{url.full}", originalUrl);

        if (uri != null)
        {
            result = result.Replace("{url.scheme}", uri.Scheme);
            result = result.Replace("{url.host}", uri.Host);
            result = result.Replace("{url.domain}", uri.Host);
            result = result.Replace("{url.port}", uri.Port.ToString());
            result = result.Replace("{url.path}", uri.AbsolutePath);
            result = result.Replace("{url.query}", uri.Query);
            result = result.Replace("{url.authority}", uri.Authority);
            result = result.Replace("{url.fragment}", uri.Fragment.TrimStart('#'));
            result = result.Replace("{url.hash}", uri.Fragment.TrimStart('#'));

            // Handle {url.query.PARAM} replacements
            if (result.Contains("{url.query."))
            {
                var queryParams = HttpUtility.ParseQueryString(uri.Query);
                var tokenRegex = new Regex(@"\{url\.query\.([a-zA-Z0-9_\-]+)\}");
                result = tokenRegex.Replace(result, m =>
                {
                    var paramName = m.Groups[1].Value;
                    var paramVal = queryParams[paramName];
                    return paramVal ?? string.Empty;
                });
            }
        }

        return result;
    }
}
