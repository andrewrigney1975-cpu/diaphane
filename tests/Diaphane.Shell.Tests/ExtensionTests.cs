using Diaphane.Data;
using Diaphane.Shell.Extensions;
using Xunit;

namespace Diaphane.Shell.Tests;

public sealed class ExtensionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "diaphane-ext-" + Guid.NewGuid().ToString("N"));

    public ExtensionTests() => Directory.CreateDirectory(_dir);

    private string MakeExtension(string name, string version, string manifestBody)
    {
        var extDir = Path.Combine(_dir, name);
        Directory.CreateDirectory(extDir);
        File.WriteAllText(Path.Combine(extDir, "manifest.json"), manifestBody);
        return extDir;
    }

    [Fact]
    public void Manifest_reads_name_version_and_permissions()
    {
        var path = MakeExtension("ublock", "1.2.3", """
            {
              "manifest_version": 3,
              "name": "uBlock Origin",
              "version": "1.2.3",
              "permissions": ["storage", "webRequest"],
              "host_permissions": ["<all_urls>"]
            }
            """);

        var m = ExtensionManifest.FromDirectory(path);

        Assert.Equal("uBlock Origin", m.Name);
        Assert.Equal("1.2.3", m.Version);
        Assert.Equal(3, m.ManifestVersion);
        Assert.Contains("storage", m.Permissions);
        Assert.Contains("<all_urls>", m.Permissions);
        Assert.Null(m.UpdateUrl);
    }

    [Fact]
    public void Manifest_tolerates_comments_and_trailing_commas()
    {
        var path = MakeExtension("commented", "0.1", """
            {
              // dev build
              "name": "Commented",
              "version": "0.1",
              "permissions": ["tabs",],
            }
            """);

        var m = ExtensionManifest.FromDirectory(path);
        Assert.Equal("Commented", m.Name);
        Assert.Contains("tabs", m.Permissions);
    }

    [Fact]
    public void Manifest_id_is_stable_for_a_path_and_in_the_a_p_alphabet()
    {
        var path = MakeExtension("stable", "1", """{ "name": "S", "version": "1" }""");

        var a = ExtensionManifest.FromDirectory(path).Id;
        var b = ExtensionManifest.FromDirectory(path).Id;

        Assert.Equal(a, b);
        Assert.Equal(32, a.Length);
        Assert.All(a, c => Assert.InRange(c, 'a', 'p'));
    }

    [Fact]
    public void Manifest_captures_update_url_but_does_not_fetch_it()
    {
        var path = MakeExtension("withupdate", "2.0", """
            { "name": "U", "version": "2.0", "update_url": "https://example.com/updates.xml" }
            """);

        var m = ExtensionManifest.FromDirectory(path);
        Assert.Equal("https://example.com/updates.xml", m.UpdateUrl);
    }

    [Fact]
    public void Manifest_throws_on_missing_manifest()
    {
        var empty = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(empty);
        Assert.Throws<FileNotFoundException>(() => ExtensionManifest.FromDirectory(empty));
    }

    [Fact]
    public void Store_round_trips_and_reports_enabled_paths()
    {
        var dbPath = Path.Combine(_dir, "ext.db");
        var e1 = new InstalledExtension("aaaa", @"C:\ext\one", "One", "1.0", true, new[] { "storage" }, DateTimeOffset.UtcNow);
        var e2 = new InstalledExtension("bbbb", @"C:\ext\two", "Two", "2.0", false, Array.Empty<string>(), DateTimeOffset.UtcNow.AddMinutes(1));

        using (var store = new ExtensionStore(dbPath))
        {
            store.Upsert(e1);
            store.Upsert(e2);
        }

        using (var store = new ExtensionStore(dbPath))
        {
            var all = store.All();
            Assert.Equal(2, all.Count);
            Assert.Equal("One", all[0].Name);
            Assert.Contains("storage", all[0].Permissions);
            Assert.Equal(new[] { @"C:\ext\one" }, store.EnabledPaths());
        }
    }

    [Fact]
    public void Store_upsert_updates_metadata_but_keeps_enabled_flag()
    {
        var dbPath = Path.Combine(_dir, "ext2.db");
        using var store = new ExtensionStore(dbPath);
        store.Upsert(new InstalledExtension("id1", @"C:\e", "Name", "1.0", true, Array.Empty<string>(), DateTimeOffset.UtcNow));
        store.SetEnabled("id1", false);

        store.Upsert(new InstalledExtension("id1", @"C:\e", "Name", "1.1", true, Array.Empty<string>(), DateTimeOffset.UtcNow));

        var row = Assert.Single(store.All());
        Assert.Equal("1.1", row.Version);
        Assert.False(row.Enabled); // conflict-update must not re-enable
    }

    [Fact]
    public void Store_remove_deletes_the_row()
    {
        var dbPath = Path.Combine(_dir, "ext3.db");
        using var store = new ExtensionStore(dbPath);
        store.Upsert(new InstalledExtension("x", @"C:\x", "X", "1", true, Array.Empty<string>(), DateTimeOffset.UtcNow));
        store.Remove("x");
        Assert.Empty(store.All());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
