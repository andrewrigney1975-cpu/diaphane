using Diaphane.Shell.Tabs;

namespace Diaphane.Shell.Multiview;

/// <summary>Side-by-side (a vertical divider) or stacked (a horizontal divider).</summary>
public enum SplitOrientation { SideBySide, Stacked }

/// <summary>A leaf's content. <see cref="FollowActiveTab"/> mirrors whatever the shell's active
/// tab is (this is what a brand-new, never-split content area does — today's exact behavior).
/// <see cref="Empty"/> is a blank drop target, produced only by splitting. <see cref="Pinned"/>
/// always shows the one tab it was pinned to, regardless of tab-strip selection.</summary>
public enum LeafMode { FollowActiveTab, Empty, Pinned }

/// <summary>One node in the pane tree: a leaf (<see cref="Split"/> is null) or an internal split
/// with two children. A node's <see cref="Id"/> is stable across a split — splitting a leaf turns
/// that same node into the internal node, so callers that captured a leaf's id for later use (e.g.
/// "the pane a drag started over") don't need to re-resolve it unless they split it further.</summary>
public sealed class PaneNode
{
    public Guid Id { get; } = Guid.NewGuid();
    public LeafMode Mode { get; internal set; } = LeafMode.FollowActiveTab;
    public TabModel? PinnedTab { get; internal set; }
    public SplitInfo? Split { get; internal set; }
    public bool IsLeaf => Split is null;
}

public sealed class SplitInfo
{
    public SplitOrientation Orientation { get; }
    public PaneNode First { get; }
    public PaneNode Second { get; }

    /// <summary>First's share of the space, 0..1.</summary>
    public double Ratio { get; internal set; }

    internal SplitInfo(SplitOrientation orientation, PaneNode first, PaneNode second, double ratio)
    {
        Orientation = orientation;
        First = first;
        Second = second;
        Ratio = ratio;
    }
}

/// <summary>The content area's split layout. Engine-free and UI-free by design (see
/// IBrowserEngine is the only seam to native) — this is pure tree bookkeeping; a view layer
/// renders whatever tree this holds. Never persisted: every window starts with a single
/// follow-active-tab leaf.</summary>
public sealed class PaneTree
{
    public PaneNode Root { get; private set; } = new();

    public event Action? Changed;
    private void RaiseChanged() => Changed?.Invoke();

    public PaneNode? Find(Guid id) => Find(Root, id);
    private static PaneNode? Find(PaneNode node, Guid id)
    {
        if (node.Id == id) return node;
        return node.Split is { } s ? Find(s.First, id) ?? Find(s.Second, id) : null;
    }

    public IEnumerable<PaneNode> Leaves() => Leaves(Root);
    private static IEnumerable<PaneNode> Leaves(PaneNode node)
    {
        if (node.IsLeaf) { yield return node; yield break; }
        foreach (var l in Leaves(node.Split!.First)) yield return l;
        foreach (var l in Leaves(node.Split!.Second)) yield return l;
    }

    /// <summary>Splits a leaf in two. The existing content stays on the first side; the second
    /// side starts empty, ready for a tab to be dragged onto it. Returns the new empty pane.</summary>
    public PaneNode SplitLeaf(Guid leafId, SplitOrientation orientation)
    {
        var node = Find(leafId) ?? throw new ArgumentException("No pane with that id.", nameof(leafId));
        if (!node.IsLeaf) throw new InvalidOperationException("Only a leaf pane can be split.");

        var first = new PaneNode { Mode = node.Mode, PinnedTab = node.PinnedTab };
        var second = new PaneNode { Mode = LeafMode.Empty };
        node.Mode = LeafMode.FollowActiveTab;
        node.PinnedTab = null;
        node.Split = new SplitInfo(orientation, first, second, 0.5);
        RaiseChanged();
        return second;
    }

    /// <summary>Ctrl+click on a splitter: collapses that split back into a single follow-active-tab
    /// leaf. Returns every tab that was pinned anywhere in the removed subtree, now unpinned and
    /// back to normal (no longer forced into any particular pane).</summary>
    public IReadOnlyList<TabModel> RemoveSplit(Guid internalNodeId)
    {
        var node = Find(internalNodeId) ?? throw new ArgumentException("No pane with that id.", nameof(internalNodeId));
        if (node.Split is not { } split) throw new InvalidOperationException("That pane isn't a split.");

        var unpinned = new List<TabModel>();
        CollectPinnedTabs(split.First, unpinned);
        CollectPinnedTabs(split.Second, unpinned);

        node.Split = null;
        node.Mode = LeafMode.FollowActiveTab;
        node.PinnedTab = null;
        RaiseChanged();
        return unpinned;
    }

    private static void CollectPinnedTabs(PaneNode node, List<TabModel> into)
    {
        if (node.IsLeaf) { if (node.PinnedTab is { } t) into.Add(t); return; }
        CollectPinnedTabs(node.Split!.First, into);
        CollectPinnedTabs(node.Split!.Second, into);
    }

    public void Pin(Guid leafId, TabModel tab)
    {
        var node = Find(leafId) ?? throw new ArgumentException("No pane with that id.", nameof(leafId));
        if (!node.IsLeaf) throw new InvalidOperationException("Only a leaf pane can be pinned.");
        node.Mode = LeafMode.Pinned;
        node.PinnedTab = tab;
        RaiseChanged();
    }

    /// <summary>Reverts a pinned (or empty) leaf to following the active tab.</summary>
    public void Unpin(Guid leafId)
    {
        var node = Find(leafId) ?? throw new ArgumentException("No pane with that id.", nameof(leafId));
        if (!node.IsLeaf) throw new InvalidOperationException("Only a leaf pane can be unpinned.");
        node.Mode = LeafMode.FollowActiveTab;
        node.PinnedTab = null;
        RaiseChanged();
    }

    /// <summary>A tab was closed elsewhere — any pane pinned to it goes back to Empty (not
    /// FollowActiveTab: showing some unrelated tab in a pane the user deliberately pinned would
    /// be more surprising than just leaving it blank again).</summary>
    public void TabClosed(TabModel tab)
    {
        var changed = false;
        foreach (var leaf in Leaves())
        {
            if (!ReferenceEquals(leaf.PinnedTab, tab)) continue;
            leaf.Mode = LeafMode.Empty;
            leaf.PinnedTab = null;
            changed = true;
        }
        if (changed) RaiseChanged();
    }

    /// <summary>Persists a splitter drag's final position. Deliberately does not raise
    /// <see cref="Changed"/> — nothing about the tree's shape changed, only a stored ratio, and
    /// the view already reflects the new position live (it drove the drag in the first place);
    /// forcing a full rebuild here would tear down every pane's rendering for no visual gain.</summary>
    public void SetRatio(Guid internalNodeId, double ratio)
    {
        var node = Find(internalNodeId) ?? throw new ArgumentException("No pane with that id.", nameof(internalNodeId));
        if (node.Split is not { } split) throw new InvalidOperationException("That pane isn't a split.");
        split.Ratio = Math.Clamp(ratio, 0.05, 0.95);
    }
}
