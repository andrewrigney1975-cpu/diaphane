using Diaphane.Data;
using Xunit;

namespace Diaphane.Shell.Tests;

public class BookmarkStoreTests
{
    [Fact]
    public void Get_ReturnsAddedBookmark()
    {
        var path = NewDbPath();
        using var store = new BookmarkStore(path);
        try
        {
            var id = store.Add("Example", "https://example.com");

            var got = store.Get(id);

            Assert.NotNull(got);
            Assert.Equal("Example", got!.Title);
            Assert.Equal("https://example.com", got.Url);
            Assert.Null(got.ParentId);
            Assert.False(got.IsFolder);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Get_UnknownId_ReturnsNull()
    {
        var path = NewDbPath();
        using var store = new BookmarkStore(path);
        try
        {
            Assert.Null(store.Get(12345));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void MoveTo_ReparentsBookmarkIntoGroup()
    {
        var path = NewDbPath();
        using var store = new BookmarkStore(path);
        try
        {
            var groupId = store.Add("Work", null, isFolder: true);
            var bookmarkId = store.Add("Example", "https://example.com");

            store.MoveTo(bookmarkId, groupId);

            Assert.DoesNotContain(store.Children(null), b => b.Id == bookmarkId);
            Assert.Single(store.Children(groupId), b => b.Id == bookmarkId);
            Assert.Equal(groupId, store.Get(bookmarkId)!.ParentId);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void MoveTo_BackToRoot_ClearsParent()
    {
        var path = NewDbPath();
        using var store = new BookmarkStore(path);
        try
        {
            var groupId = store.Add("Work", null, isFolder: true);
            var bookmarkId = store.Add("Example", "https://example.com", groupId);

            store.MoveTo(bookmarkId, null);

            Assert.Null(store.Get(bookmarkId)!.ParentId);
            Assert.Single(store.Children(null), b => b.Id == bookmarkId);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Update_RenamesGroup_LeavesUrlNull()
    {
        var path = NewDbPath();
        using var store = new BookmarkStore(path);
        try
        {
            var groupId = store.Add("Work", null, isFolder: true);

            store.Update(groupId, "Personal", null);

            var got = store.Get(groupId)!;
            Assert.Equal("Personal", got.Title);
            Assert.Null(got.Url);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Update_RetitlesAndRepointsBookmark()
    {
        var path = NewDbPath();
        using var store = new BookmarkStore(path);
        try
        {
            var id = store.Add("Example", "https://example.com");

            store.Update(id, "Example (new)", "https://example.org");

            var got = store.Get(id)!;
            Assert.Equal("Example (new)", got.Title);
            Assert.Equal("https://example.org", got.Url);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Remove_Group_CascadesToChildren()
    {
        var path = NewDbPath();
        using var store = new BookmarkStore(path);
        try
        {
            var groupId = store.Add("Work", null, isFolder: true);
            var bookmarkId = store.Add("Example", "https://example.com", groupId);

            store.Remove(groupId);

            Assert.Null(store.Get(groupId));
            Assert.Null(store.Get(bookmarkId));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Add_Folder_GetsARandomColorFromThePalette()
    {
        var path = NewDbPath();
        using var store = new BookmarkStore(path);
        try
        {
            var groupId = store.Add("Work", null, isFolder: true);

            var color = store.Get(groupId)!.Color;

            Assert.NotNull(color);
            Assert.Contains(color, BookmarkStore.GroupColorPalette);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Add_PlainBookmark_HasNoColor()
    {
        var path = NewDbPath();
        using var store = new BookmarkStore(path);
        try
        {
            var id = store.Add("Example", "https://example.com");

            Assert.Null(store.Get(id)!.Color);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Add_Folder_ExplicitColor_IsUsedInsteadOfRandom()
    {
        var path = NewDbPath();
        using var store = new BookmarkStore(path);
        try
        {
            var groupId = store.Add("Work", null, isFolder: true, color: "#123456");

            Assert.Equal("#123456", store.Get(groupId)!.Color);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Update_WithColor_ChangesIt()
    {
        var path = NewDbPath();
        using var store = new BookmarkStore(path);
        try
        {
            var groupId = store.Add("Work", null, isFolder: true, color: "#111111");

            store.Update(groupId, "Renamed", null, "#ABCDEF");

            var got = store.Get(groupId)!;
            Assert.Equal("Renamed", got.Title);
            Assert.Equal("#ABCDEF", got.Color);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Update_WithoutColor_LeavesItUnchanged()
    {
        var path = NewDbPath();
        using var store = new BookmarkStore(path);
        try
        {
            var groupId = store.Add("Work", null, isFolder: true, color: "#111111");

            store.Update(groupId, "Renamed", null);

            Assert.Equal("#111111", store.Get(groupId)!.Color);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"diaphane-bookmark-test-{Guid.NewGuid():N}.db");
}
