namespace Diaphane.Shell.Omnibox;

public sealed record SearchEngineInfo(string Id, string Name, string UrlTemplate);

/// <summary>
/// The search engines diaphane offers. All are query-string GETs with no
/// suggest/telemetry endpoint — the omnibox never calls out while you type.
/// </summary>
public static class SearchEngines
{
    public const string CustomId = "custom";

    public static readonly IReadOnlyList<SearchEngineInfo> Builtin = new[]
    {
        new SearchEngineInfo("duckduckgo", "DuckDuckGo",    "https://duckduckgo.com/?q={q}"),
        new SearchEngineInfo("startpage",  "Startpage",     "https://www.startpage.com/sp/search?query={q}"),
        new SearchEngineInfo("brave",      "Brave Search",  "https://search.brave.com/search?q={q}"),
        new SearchEngineInfo("wikipedia",  "Wikipedia",     "https://en.wikipedia.org/w/index.php?search={q}"),
        new SearchEngineInfo("mojeek",     "Mojeek",        "https://www.mojeek.com/search?q={q}"),
        new SearchEngineInfo("google",     "Google",        "https://www.google.com/search?q={q}"),
    };

    public static ISearchEngine Resolve(string? id, string? customUrl = null)
    {
        if (id == CustomId && !string.IsNullOrWhiteSpace(customUrl))
            return new TemplateSearchEngine("Custom", customUrl!);

        var info = Builtin.FirstOrDefault(e => e.Id == id) ?? Builtin[0];
        return new TemplateSearchEngine(info.Name, info.UrlTemplate);
    }
}

/// <summary>A search engine defined by a URL template where <c>{q}</c> is the escaped query.</summary>
public sealed class TemplateSearchEngine(string name, string template) : ISearchEngine
{
    public string Name => name;

    public string BuildSearchUrl(string query) =>
        template.Contains("{q}")
            ? template.Replace("{q}", Uri.EscapeDataString(query))
            : template + Uri.EscapeDataString(query);
}
