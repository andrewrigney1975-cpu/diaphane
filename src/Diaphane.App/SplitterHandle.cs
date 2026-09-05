using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Diaphane.App;

/// <summary>A draggable divider. Plain <see cref="Microsoft.UI.Xaml.UIElement.ProtectedCursor"/> is
/// only settable from within a UIElement subclass — this exists solely to expose that as a public
/// method so the splitter can show a resize cursor on hover.</summary>
public sealed class SplitterHandle : Grid
{
    /// <param name="sideBySide">True for a vertical divider between side-by-side panes (drags
    /// left/right — the west-east cursor); false for a horizontal divider between stacked panes
    /// (drags up/down — the north-south cursor). Every existing caller resizes a column, hence
    /// the default.</param>
    public void SetResizeCursor(bool hovering, bool sideBySide = true) =>
        ProtectedCursor = hovering
            ? InputSystemCursor.Create(sideBySide ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth)
            : null;
}
