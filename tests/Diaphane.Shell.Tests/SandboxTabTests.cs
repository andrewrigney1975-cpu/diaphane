using Diaphane.Shell.Tabs;
using Xunit;

namespace Diaphane.Shell.Tests;

public class SandboxTabTests
{
    [Fact]
    public void SandboxTabGetsItsOwnContext_NotTheStandardOne()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);

        var normal = tabs.NewStandardTab("https://a.example");
        var sandbox = tabs.NewSandboxTab(url: "https://b.example");

        Assert.Equal(engine.StandardContext.Id, normal.ContextId);
        Assert.NotEqual(engine.StandardContext.Id, sandbox.ContextId);
        Assert.True(sandbox.IsSandbox);
    }

    [Fact]
    public void ClosingLastSandboxTab_DisposesItsContext()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);

        var sandbox = tabs.NewSandboxTab();
        var ctx = (FakeContext)engine.Contexts.Single(c => c.Id == sandbox.ContextId);

        tabs.Close(sandbox);

        Assert.True(ctx.Disposed); // in-memory partition released — no trace left
    }

    [Fact]
    public void SandboxTabsInSameGroup_ShareOneContext()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);

        var first = tabs.NewSandboxTab();
        var second = tabs.NewSandboxTab(group: first.ContextId);

        Assert.Equal(first.ContextId, second.ContextId);
    }
}
