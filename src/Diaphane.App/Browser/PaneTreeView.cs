using Diaphane.Shell.Multiview;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Diaphane.App.Browser;

/// <summary>Renders a <see cref="PaneTree"/> into a host <see cref="Grid"/>: one nested Grid +
/// <see cref="SplitterHandle"/> per split, one leaf-content element (supplied by the caller) per
/// leaf. Rebuilds the whole subtree on any structural change (<see cref="PaneTree.Changed"/>) —
/// fine for how rarely splits are created/removed, but a drag must never fire that event on every
/// pointer move, or it would tear down (and lose pointer capture on) the very handle being
/// dragged. A drag instead mutates the host Grid's own row/column sizes directly and only commits
/// into the model — via <see cref="PaneTree.SetRatio"/> — on release.</summary>
internal sealed class PaneTreeView
{
    private readonly Grid _host;
    private readonly PaneTree _tree;
    private readonly Func<PaneNode, FrameworkElement> _buildLeafContent;

    public PaneTreeView(Grid host, PaneTree tree, Func<PaneNode, FrameworkElement> buildLeafContent)
    {
        _host = host;
        _tree = tree;
        _buildLeafContent = buildLeafContent;
        _tree.Changed += Rebuild;
        Rebuild();
    }

    private void Rebuild()
    {
        _host.Children.Clear();
        var built = BuildNode(_tree.Root);
        _host.Children.Add(built);
    }

    private FrameworkElement BuildNode(PaneNode node)
    {
        if (node.IsLeaf) return _buildLeafContent(node);

        var split = node.Split!;
        var grid = new Grid();
        var handle = new SplitterHandle
        {
            Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
        };

        bool sideBySide = split.Orientation == SplitOrientation.SideBySide;
        if (sideBySide)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(split.Ratio, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - split.Ratio, GridUnitType.Star) });
            handle.Width = 6;
            handle.VerticalAlignment = VerticalAlignment.Stretch;
            Grid.SetColumn(handle, 1);
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(split.Ratio, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - split.Ratio, GridUnitType.Star) });
            handle.Height = 6;
            handle.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetRow(handle, 1);
        }

        var first = BuildNode(split.First);
        var second = BuildNode(split.Second);
        if (sideBySide) { Grid.SetColumn(first, 0); Grid.SetColumn(second, 2); }
        else { Grid.SetRow(first, 0); Grid.SetRow(second, 2); }

        grid.Children.Add(first);
        grid.Children.Add(handle);
        grid.Children.Add(second);

        WireSplitter(handle, grid, node.Id, sideBySide);
        return grid;
    }

    private void WireSplitter(SplitterHandle handle, Grid grid, Guid nodeId, bool sideBySide)
    {
        var dragging = false;

        handle.PointerEntered += (_, _) => handle.SetResizeCursor(true);
        handle.PointerExited += (_, _) => handle.SetResizeCursor(false);

        handle.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(handle).Properties.PointerUpdateKind is not PointerUpdateKind.LeftButtonPressed)
                return;
            if (InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                    .HasFlag(VirtualKeyStates.Down))
            {
                _tree.RemoveSplit(nodeId); // fires Changed -> Rebuild; a real structural change
                return;
            }
            dragging = true;
            handle.CapturePointer(e.Pointer);
        };

        handle.PointerMoved += (_, e) =>
        {
            if (!dragging) return;
            var pos = e.GetCurrentPoint(grid).Position;
            var ratio = Math.Clamp(
                sideBySide ? pos.X / Math.Max(1, grid.ActualWidth) : pos.Y / Math.Max(1, grid.ActualHeight),
                0.05, 0.95);
            // Live feedback only — no model mutation (and so no Rebuild) until release.
            if (sideBySide)
            {
                grid.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star);
                grid.ColumnDefinitions[2].Width = new GridLength(1 - ratio, GridUnitType.Star);
            }
            else
            {
                grid.RowDefinitions[0].Height = new GridLength(ratio, GridUnitType.Star);
                grid.RowDefinitions[2].Height = new GridLength(1 - ratio, GridUnitType.Star);
            }
        };

        handle.PointerReleased += (_, e) =>
        {
            if (!dragging) return;
            dragging = false;
            handle.ReleasePointerCapture(e.Pointer);
            var ratio = sideBySide
                ? grid.ColumnDefinitions[0].Width.Value / (grid.ColumnDefinitions[0].Width.Value + grid.ColumnDefinitions[2].Width.Value)
                : grid.RowDefinitions[0].Height.Value / (grid.RowDefinitions[0].Height.Value + grid.RowDefinitions[2].Height.Value);
            _tree.SetRatio(nodeId, ratio); // commits + fires Changed once
        };
    }
}
