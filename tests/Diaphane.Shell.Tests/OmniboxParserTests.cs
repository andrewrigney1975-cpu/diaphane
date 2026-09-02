using Diaphane.Shell.Omnibox;
using Xunit;

namespace Diaphane.Shell.Tests;

public class OmniboxParserTests
{
    [Theory]
    [InlineData("example.com", OmniboxIntentKind.Url)]
    [InlineData("https://example.com/path", OmniboxIntentKind.Url)]
    [InlineData("192.168.0.1:8080", OmniboxIntentKind.Url)]
    [InlineData("localhost:3000", OmniboxIntentKind.Url)]
    [InlineData("diaphane://privacy", OmniboxIntentKind.InternalPage)]
    [InlineData("how tall is everest", OmniboxIntentKind.Search)]
    [InlineData("weather", OmniboxIntentKind.Search)]
    [InlineData("rust ownership rules", OmniboxIntentKind.Search)]
    public void Classifies(string input, OmniboxIntentKind expected)
        => Assert.Equal(expected, OmniboxParser.Parse(input).Kind);

    [Fact]
    public void BareWordDoesNotBecomeDnsLookup()
        => Assert.Equal(OmniboxIntentKind.Search, OmniboxParser.Parse("intranet").Kind);

    [Fact]
    public void SearchGoesThroughConfiguredEngine()
    {
        var url = OmniboxParser.Parse("privacy tools").ToNavigationUrl(new DuckDuckGoEngine());
        Assert.StartsWith("https://duckduckgo.com/?q=", url);
    }
}
