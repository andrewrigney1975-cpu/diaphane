using Diaphane.Shell.Tabs;
using Xunit;

namespace Diaphane.Shell.Tests;

public class TabReorderTests
{
    [Fact]
    public void Reorder_MatchesRequestedOrder()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);

        var a = tabs.NewStandardTab("https://a.example");
        var b = tabs.NewStandardTab("https://b.example");
        var c = tabs.NewStandardTab("https://c.example");

        tabs.Reorder(new[] { c, a, b });

        Assert.Equal(new[] { c, a, b }, tabs.Tabs);
    }

    [Fact]
    public void Reorder_IgnoresMismatchedSet()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);

        var a = tabs.NewStandardTab("https://a.example");
        var b = tabs.NewStandardTab("https://b.example");
        var original = tabs.Tabs.ToList();

        tabs.Reorder(new[] { a }); // wrong count — ignored

        Assert.Equal(original, tabs.Tabs);
    }
}
