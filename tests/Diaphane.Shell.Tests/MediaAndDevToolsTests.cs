using Diaphane.Shell.Engine;
using Diaphane.Shell.Media;
using Diaphane.Shell.Tabs;
using Xunit;

namespace Diaphane.Shell.Tests;

public sealed class MediaProbeTests
{
    // A realistic-shaped CDP Runtime.evaluate result for MediaProbe.Script.
    private const string SampleResult = """
        {
          "result": {
            "type": "object",
            "value": {
              "codecs": [
                { "name": "H.264 (AVC)", "canPlayType": "Probably", "mediaSource": "Yes" },
                { "name": "H.265 (HEVC)", "canPlayType": "No", "mediaSource": "No" },
                { "name": "AV1", "canPlayType": "Probably", "mediaSource": "Yes" },
                { "name": "AAC", "canPlayType": "Probably", "mediaSource": "Yes" }
              ],
              "keySystems": {
                "com.widevine.alpha": "Not available",
                "org.w3.clearkey": "Available"
              }
            }
          }
        }
        """;

    [Fact]
    public void Parse_reads_codecs_and_key_systems()
    {
        var report = MediaProbe.Parse(SampleResult);

        Assert.Equal(4, report.Codecs.Count);
        var h264 = report.Codecs[0];
        Assert.Equal("H.264 (AVC)", h264.Name);
        Assert.Equal("Probably", h264.CanPlayType);
        Assert.Equal("Yes", h264.MediaSource);

        Assert.Contains(report.KeySystems, k => k.Key == "Widevine" && k.Value == "Not available");
        Assert.Contains(report.KeySystems, k => k.Key == "Clear Key" && k.Value == "Available");
    }

    [Fact]
    public void Parse_tolerates_a_bare_value_object()
    {
        var report = MediaProbe.Parse("""{ "codecs": [ { "name": "MP3", "canPlayType": "Maybe", "mediaSource": "No" } ], "keySystems": {} }""");
        Assert.Equal("MP3", Assert.Single(report.Codecs).Name);
        Assert.Empty(report.KeySystems);
    }

    [Fact]
    public void Script_is_a_single_expression()
    {
        Assert.StartsWith("(()", MediaProbe.Script.TrimStart());
        Assert.Contains("requestMediaKeySystemAccess", MediaProbe.Script);
        Assert.Contains("canPlayType", MediaProbe.Script);
    }
}

public sealed class DevToolsTabTests
{
    private static TabModel NewTab(out FakeView view)
    {
        view = new FakeView();
        return new TabModel(view, TabKind.Standard, Guid.NewGuid());
    }

    [Fact]
    public async Task OpenDevTools_then_close_tracks_state()
    {
        var tab = NewTab(out var view);

        var dt = await tab.OpenDevToolsAsync(800, 600);
        Assert.NotNull(dt);
        Assert.True(view.DevToolsOpen);
        Assert.True(tab.HasDevTools);
        Assert.Same(dt, tab.DevToolsView);

        tab.CloseDevTools();
        Assert.False(view.DevToolsOpen);
        Assert.Null(tab.DevToolsView);
    }

    [Fact]
    public async Task OpenDevTools_is_idempotent()
    {
        var tab = NewTab(out _);
        var a = await tab.OpenDevToolsAsync(800, 600);
        var b = await tab.OpenDevToolsAsync(400, 300);
        Assert.Same(a, b);
    }

    [Fact]
    public async Task EvaluateJavaScriptAsync_round_trips_through_the_view()
    {
        var tab = NewTab(out var view);
        view.EvalHandler = script => script.Contains("1+1") ? "2" : "null";

        Assert.Equal("2", await tab.EvaluateJavaScriptAsync("1+1"));
    }
}
