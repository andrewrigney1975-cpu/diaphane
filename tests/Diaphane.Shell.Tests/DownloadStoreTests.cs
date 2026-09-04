using Diaphane.Data;
using Xunit;

namespace Diaphane.Shell.Tests;

public class DownloadStoreTests
{
    [Fact]
    public void Start_RecordsAnInProgressDownload()
    {
        var path = NewDbPath();
        using var store = new DownloadStore(path);
        try
        {
            var id = store.Start("https://example.com/file.zip", "file.zip", "C:\\Downloads\\file.zip", 1000);

            var got = Assert.Single(store.Recent());
            Assert.Equal(id, got.Id);
            Assert.Equal("file.zip", got.FileName);
            Assert.Equal(0, got.ReceivedBytes);
            Assert.Equal(1000, got.TotalBytes);
            Assert.Equal(DownloadState.InProgress, got.State);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void UpdateProgress_ChangesReceivedBytesAndState()
    {
        var path = NewDbPath();
        using var store = new DownloadStore(path);
        try
        {
            var id = store.Start("https://example.com/file.zip", "file.zip", "C:\\Downloads\\file.zip", 1000);

            store.UpdateProgress(id, 1000, 1000, DownloadState.Complete);

            var got = Assert.Single(store.Recent());
            Assert.Equal(1000, got.ReceivedBytes);
            Assert.Equal(DownloadState.Complete, got.State);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Recent_OrdersMostRecentFirst()
    {
        var path = NewDbPath();
        using var store = new DownloadStore(path);
        try
        {
            var first = store.Start("https://example.com/a", "a.zip", "C:\\a.zip", 10);
            var second = store.Start("https://example.com/b", "b.zip", "C:\\b.zip", 10);

            var recent = store.Recent();

            Assert.Equal(second, recent[0].Id);
            Assert.Equal(first, recent[1].Id);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Remove_DeletesOneEntry()
    {
        var path = NewDbPath();
        using var store = new DownloadStore(path);
        try
        {
            var keep = store.Start("https://example.com/a", "a.zip", "C:\\a.zip", 10);
            var drop = store.Start("https://example.com/b", "b.zip", "C:\\b.zip", 10);

            store.Remove(drop);

            var got = Assert.Single(store.Recent());
            Assert.Equal(keep, got.Id);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var path = NewDbPath();
        using var store = new DownloadStore(path);
        try
        {
            store.Start("https://example.com/a", "a.zip", "C:\\a.zip", 10);
            store.Start("https://example.com/b", "b.zip", "C:\\b.zip", 10);

            store.Clear();

            Assert.Empty(store.Recent());
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"diaphane-download-test-{Guid.NewGuid():N}.db");
}
