using System.Text.RegularExpressions;

namespace Diaphane.Shell.Omnibox;

public enum OmniboxIntentKind { Url, Search, InternalPage }

public sealed record OmniboxIntent(OmniboxIntentKind Kind, string Target)
{
    /// <summary>The resolved URL to hand the engine.</summary>
    public string ToNavigationUrl(ISearchEngine engine) => Kind switch
    {
        OmniboxIntentKind.Url => Target,
        OmniboxIntentKind.InternalPage => Target,
        OmniboxIntentKind.Search => engine.BuildSearchUrl(Target),
        _ => Target,
    };
}

public interface ISearchEngine
{
    string Name { get; }
    string BuildSearchUrl(string query);
}

/// <summary>Privacy-respecting default. No suggest calls unless separately opted in.</summary>
public sealed class DuckDuckGoEngine : ISearchEngine
{
    public string Name => "DuckDuckGo";
    public string BuildSearchUrl(string query) =>
        "https://duckduckgo.com/?q=" + Uri.EscapeDataString(query);
}

/// <summary>
/// Decides whether typed text is a URL, a search, or a diaphane:// page.
/// Deliberately conservative: anything ambiguous with a space, or without a
/// dot / known scheme, becomes a search rather than a bad DNS lookup that
/// leaks the query.
/// </summary>
public static class OmniboxParser
{
    private static readonly Regex SchemeRe = new(@"^([a-z][a-z0-9+.\-]*):", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HostishRe = new(@"^[^\s/]+\.[^\s/]{2,}(?:[:/]|$)", RegexOptions.Compiled);
    private static readonly Regex Ipv4Re = new(@"^\d{1,3}(\.\d{1,3}){3}(?::\d+)?(?:/|$)", RegexOptions.Compiled);

    public static OmniboxIntent Parse(string raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0) return new(OmniboxIntentKind.Search, string.Empty);

        if (text.StartsWith("diaphane://", StringComparison.OrdinalIgnoreCase))
            return new(OmniboxIntentKind.InternalPage, text);

        if (text is "localhost" || text.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase))
            return new(OmniboxIntentKind.Url, "http://" + text);

        var scheme = SchemeRe.Match(text);
        if (scheme.Success)
        {
            var s = scheme.Groups[1].Value.ToLowerInvariant();
            if (s is "http" or "https" or "file" or "ftp" or "about")
                return new(OmniboxIntentKind.Url, text);
            // unknown scheme with a space is almost certainly a search
            return text.Contains(' ')
                ? new(OmniboxIntentKind.Search, text)
                : new(OmniboxIntentKind.Url, text);
        }

        if (text.Contains(' '))
            return new(OmniboxIntentKind.Search, text);

        if (Ipv4Re.IsMatch(text) || HostishRe.IsMatch(text))
            return new(OmniboxIntentKind.Url, "https://" + text);

        return new(OmniboxIntentKind.Search, text);
    }
}
