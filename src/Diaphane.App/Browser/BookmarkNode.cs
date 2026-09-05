using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Diaphane.Data;
using Microsoft.UI.Xaml.Media;

namespace Diaphane.App.Browser;

/// <summary>A folder ("group") or leaf in the bookmarks tree, for the left bookmarks panel's TreeView.</summary>
public sealed partial class BookmarkNode : ObservableObject
{
    // Segoe Fluent Icons codepoints: folder (group) vs. a plain link glyph for a leaf bookmark.
    private const string FolderGlyph = "";
    private const string LinkGlyph = "";

    public required Bookmark Model { get; init; }
    public bool IsFolder => Model.IsFolder;
    public string DisplayName => Model.Title;
    public string Glyph => IsFolder ? FolderGlyph : LinkGlyph;

    /// <summary>The group's colour chip — null for a plain bookmark (only groups carry a colour).
    /// A rebuilt-from-scratch tree (every edit calls RefreshBookmarkTree) is what keeps this in
    /// sync, same as DisplayName/Glyph — Model is otherwise immutable once set.</summary>
    public SolidColorBrush? ColorBrush => IsFolder && Model.Color is { } hex ? new SolidColorBrush(HexColor.Parse(hex)) : null;

    public ObservableCollection<BookmarkNode> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded = true;
}