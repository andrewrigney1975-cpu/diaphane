using Diaphane.Shell.Tabs;
using Xunit;

namespace Diaphane.Shell.Tests;

public class TabVisibilityTests
{
    [Fact]
    public void Activate_MakesOnlyThatTabVisible()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);

        var a = tabs.NewStandardTab("https://a.example");
        var b = tabs.NewStandardTab("https://b.example"); // activates b

        Assert.False(tabs.IsVisible(a));
        Assert.True(tabs.IsVisible(b));
        Assert.Equal(new[] { b }, tabs.VisibleTabs);
        Assert.Same(b, tabs.Active);
    }

    [Fact]
    public void SetVisible_CanShowSeveralTabsAtOnce()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);

        var a = tabs.NewStandardTab("https://a.example");
        var b = tabs.NewStandardTab("https://b.example");
        var c = tabs.NewStandardTab("https://c.example");

        tabs.SetVisible(new[] { a, c });

        Assert.True(tabs.IsVisible(a));
        Assert.False(tabs.IsVisible(b));
        Assert.True(tabs.IsVisible(c));
    }

    [Fact]
    public void SetVisible_ReplacesThePreviousVisibleSet()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);

        var a = tabs.NewStandardTab("https://a.example");
        var b = tabs.NewStandardTab("https://b.example");

        tabs.SetVisible(new[] { a, b });
        tabs.SetVisible(new[] { b });

        Assert.False(tabs.IsVisible(a));
        Assert.True(tabs.IsVisible(b));
    }

    [Fact]
    public void Close_RemovesTabFromVisibleSetEvenWhenNotActive()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);

        var a = tabs.NewStandardTab("https://a.example");
        var b = tabs.NewStandardTab("https://b.example"); // b is Active
        tabs.SetVisible(new[] { a, b }); // a visible too, e.g. pinned in a Multiview pane

        tabs.Close(a);

        Assert.DoesNotContain(a, tabs.VisibleTabs);
    }

    [Fact]
    public void Activate_Null_HidesEveryTab()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);
        var a = tabs.NewStandardTab("https://a.example");

        tabs.Activate(null);

        Assert.Empty(tabs.VisibleTabs);
        Assert.Null(tabs.Active);
    }
}
