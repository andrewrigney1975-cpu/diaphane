using Diaphane.Shell.Multiview;
using Diaphane.Shell.Tabs;
using Xunit;

namespace Diaphane.Shell.Tests;

public class PaneTreeTests
{
    [Fact]
    public void NewTree_IsOneFollowActiveLeaf()
    {
        var tree = new PaneTree();

        var leaf = Assert.Single(tree.Leaves());
        Assert.Same(tree.Root, leaf);
        Assert.Equal(LeafMode.FollowActiveTab, leaf.Mode);
        Assert.Null(leaf.PinnedTab);
    }

    [Fact]
    public void SplitLeaf_FirstKeepsContent_SecondStartsEmpty()
    {
        var tree = new PaneTree();
        var rootId = tree.Root.Id;

        var second = tree.SplitLeaf(rootId, SplitOrientation.SideBySide);

        var root = tree.Find(rootId)!;
        Assert.False(root.IsLeaf);
        Assert.Equal(SplitOrientation.SideBySide, root.Split!.Orientation);
        Assert.Equal(LeafMode.FollowActiveTab, root.Split.First.Mode);
        Assert.Equal(LeafMode.Empty, root.Split.Second.Mode);
        Assert.Same(second, root.Split.Second);
        Assert.Equal(0.5, root.Split.Ratio);
    }

    [Fact]
    public void SplitLeaf_PreservesPinnedTabOnFirstSide()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);
        var tab = tabs.NewStandardTab("https://a.example");

        var tree = new PaneTree();
        tree.Pin(tree.Root.Id, tab);
        tree.SplitLeaf(tree.Root.Id, SplitOrientation.Stacked);

        var root = tree.Find(tree.Root.Id)!;
        Assert.Equal(LeafMode.Pinned, root.Split!.First.Mode);
        Assert.Same(tab, root.Split.First.PinnedTab);
        Assert.Equal(LeafMode.Empty, root.Split.Second.Mode);
    }

    [Fact]
    public void SplitLeaf_OnNonLeaf_Throws()
    {
        var tree = new PaneTree();
        tree.SplitLeaf(tree.Root.Id, SplitOrientation.SideBySide);

        Assert.Throws<InvalidOperationException>(() => tree.SplitLeaf(tree.Root.Id, SplitOrientation.SideBySide));
    }

    [Fact]
    public void Pin_MarksLeafPinned()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);
        var tab = tabs.NewStandardTab("https://a.example");

        var tree = new PaneTree();
        tree.Pin(tree.Root.Id, tab);

        var leaf = Assert.Single(tree.Leaves());
        Assert.Equal(LeafMode.Pinned, leaf.Mode);
        Assert.Same(tab, leaf.PinnedTab);
    }

    [Fact]
    public void Unpin_RevertsToFollowActiveTab()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);
        var tab = tabs.NewStandardTab("https://a.example");

        var tree = new PaneTree();
        tree.Pin(tree.Root.Id, tab);
        tree.Unpin(tree.Root.Id);

        var leaf = Assert.Single(tree.Leaves());
        Assert.Equal(LeafMode.FollowActiveTab, leaf.Mode);
        Assert.Null(leaf.PinnedTab);
    }

    [Fact]
    public void RemoveSplit_CollapsesToSingleFollowLeaf_AndReturnsUnpinnedTabs()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);
        var left = tabs.NewStandardTab("https://a.example");
        var right = tabs.NewStandardTab("https://b.example");

        var tree = new PaneTree();
        var rootId = tree.Root.Id;
        var second = tree.SplitLeaf(rootId, SplitOrientation.SideBySide);
        tree.Pin(tree.Find(rootId)!.Split!.First.Id, left);
        tree.Pin(second.Id, right);

        var unpinned = tree.RemoveSplit(rootId);

        Assert.Equal(new[] { left, right }, unpinned);
        var leaf = Assert.Single(tree.Leaves());
        Assert.Same(tree.Root, leaf);
        Assert.Equal(LeafMode.FollowActiveTab, leaf.Mode);
    }

    [Fact]
    public void RemoveSplit_UnpinsTabsFromNestedSplitsToo()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);
        var a = tabs.NewStandardTab("https://a.example");
        var b = tabs.NewStandardTab("https://b.example");

        var tree = new PaneTree();
        var rootId = tree.Root.Id;
        var right = tree.SplitLeaf(rootId, SplitOrientation.SideBySide);
        // Split the *first* (left) side again, nesting a second split under the root.
        var root = tree.Find(rootId)!;
        var leftId = root.Split!.First.Id;
        var nestedBottom = tree.SplitLeaf(leftId, SplitOrientation.Stacked);
        tree.Pin(nestedBottom.Id, a);
        tree.Pin(right.Id, b);

        var unpinned = tree.RemoveSplit(rootId);

        Assert.Equal(new[] { a, b }, unpinned);
        Assert.Single(tree.Leaves());
    }

    [Fact]
    public void RemoveSplit_OnLeaf_Throws()
    {
        var tree = new PaneTree();
        Assert.Throws<InvalidOperationException>(() => tree.RemoveSplit(tree.Root.Id));
    }

    [Fact]
    public void TabClosed_PinnedPaneRevertsToEmpty_NotFollowActiveTab()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);
        var tab = tabs.NewStandardTab("https://a.example");

        var tree = new PaneTree();
        tree.Pin(tree.Root.Id, tab);
        tree.TabClosed(tab);

        var leaf = Assert.Single(tree.Leaves());
        Assert.Equal(LeafMode.Empty, leaf.Mode);
        Assert.Null(leaf.PinnedTab);
    }

    [Fact]
    public void TabClosed_OnlyAffectsPanesPinnedToThatTab()
    {
        var engine = new FakeEngine();
        using var tabs = new TabManager(engine, hostHwnd: 0);
        var a = tabs.NewStandardTab("https://a.example");
        var b = tabs.NewStandardTab("https://b.example");

        var tree = new PaneTree();
        var second = tree.SplitLeaf(tree.Root.Id, SplitOrientation.SideBySide);
        var root = tree.Find(tree.Root.Id)!;
        tree.Pin(root.Split!.First.Id, a);
        tree.Pin(second.Id, b);

        tree.TabClosed(a);

        Assert.Equal(LeafMode.Empty, root.Split.First.Mode);
        Assert.Equal(LeafMode.Pinned, root.Split.Second.Mode);
        Assert.Same(b, root.Split.Second.PinnedTab);
    }

    [Fact]
    public void SetRatio_ClampsToRange()
    {
        var tree = new PaneTree();
        tree.SplitLeaf(tree.Root.Id, SplitOrientation.SideBySide);

        tree.SetRatio(tree.Root.Id, 1.5);
        Assert.Equal(0.95, tree.Root.Split!.Ratio);

        tree.SetRatio(tree.Root.Id, -1);
        Assert.Equal(0.05, tree.Root.Split.Ratio);
    }

    [Fact]
    public void Changed_FiresOnMutation()
    {
        var tree = new PaneTree();
        var fireCount = 0;
        tree.Changed += () => fireCount++;

        tree.SplitLeaf(tree.Root.Id, SplitOrientation.SideBySide);
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Find_ReturnsNullForUnknownId()
    {
        var tree = new PaneTree();
        Assert.Null(tree.Find(Guid.NewGuid()));
    }
}
