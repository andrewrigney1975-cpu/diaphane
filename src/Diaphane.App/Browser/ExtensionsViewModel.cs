using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diaphane.Data;
using Diaphane.Shell.Extensions;

namespace Diaphane.App.Browser;

/// <summary>One row in the diaphane://extensions list.</summary>
public sealed partial class ExtensionRow : ObservableObject
{
    private readonly Action<ExtensionRow, bool> _onToggle;

    public ExtensionRow(InstalledExtension e, Action<ExtensionRow, bool> onToggle)
    {
        _onToggle = onToggle;
        Id = e.Id;
        Path = e.Path;
        Name = e.Name;
        Version = e.Version;
        Permissions = e.Permissions.Count == 0 ? "no special permissions" : string.Join(", ", e.Permissions);
        _enabled = e.Enabled;
    }

    public string Id { get; }
    public string Path { get; }
    public string Name { get; }

    [ObservableProperty] private string _version;
    public string Permissions { get; }

    [ObservableProperty] private bool _enabled;
    partial void OnEnabledChanged(bool value) => _onToggle(this, value);
}

/// <summary>
/// Manages the user's unpacked extensions. Loading and enable/disable take effect
/// on the next launch (the engine reads the enabled set via --load-extension at
/// startup). Update checks are manual and never touch the network.
/// </summary>
public sealed partial class ExtensionsViewModel : ObservableObject
{
    private readonly ExtensionStore _store;
    private bool _muteToggle;

    public ExtensionsViewModel(ExtensionStore store)
    {
        _store = store;
        _status = "";
        Reload();
    }

    public ObservableCollection<ExtensionRow> Items { get; } = new();

    [ObservableProperty] private bool _restartNeeded;
    [ObservableProperty] private string _status;

    private void Reload()
    {
        _muteToggle = true;
        Items.Clear();
        foreach (var e in _store.All())
            Items.Add(new ExtensionRow(e, OnRowToggled));
        _muteToggle = false;
    }

    private void OnRowToggled(ExtensionRow row, bool enabled)
    {
        if (_muteToggle) return;
        _store.SetEnabled(row.Id, enabled);
        RestartNeeded = true;
        Status = $"“{row.Name}” will be {(enabled ? "enabled" : "disabled")} after you restart diaphane.";
    }

    /// <summary>Load an unpacked extension from a folder that contains manifest.json.</summary>
    public void LoadUnpacked(string dir)
    {
        try
        {
            var m = ExtensionManifest.FromDirectory(dir);
            _store.Upsert(new InstalledExtension(
                m.Id, m.Path, m.Name, m.Version, Enabled: true, m.Permissions, DateTimeOffset.UtcNow));
            Reload();
            RestartNeeded = true;
            Status = m.UpdateUrl is null
                ? $"Added “{m.Name}” {m.Version}. Restart diaphane to load it."
                : $"Added “{m.Name}” {m.Version}. Restart to load it. Note: this extension lists an update URL — diaphane will never contact it automatically.";
        }
        catch (Exception ex)
        {
            Status = "Couldn't load that folder: " + ex.Message;
        }
    }

    [RelayCommand]
    private void Remove(ExtensionRow? row)
    {
        if (row is null) return;
        _store.Remove(row.Id);
        Items.Remove(row);
        RestartNeeded = true;
        Status = $"Removed “{row.Name}”. Restart diaphane to unload it.";
    }

    [RelayCommand]
    private void CheckForUpdates()
    {
        int changed = 0, missing = 0;
        foreach (var row in Items)
        {
            try
            {
                var m = ExtensionManifest.FromDirectory(row.Path);
                if (m.Version != row.Version)
                {
                    _store.Upsert(new InstalledExtension(
                        m.Id, m.Path, m.Name, m.Version, row.Enabled, m.Permissions, DateTimeOffset.UtcNow));
                    row.Version = m.Version;
                    changed++;
                }
            }
            catch
            {
                missing++;
            }
        }

        RestartNeeded |= changed > 0;
        Status = (changed, missing) switch
        {
            (0, 0) => "All extensions are up to date. (diaphane only re-reads the folder on disk — it never contacts an update server.)",
            (_, 0) => $"Updated {changed} extension(s) from disk. Restart diaphane to apply.",
            (0, _) => $"{missing} extension folder(s) are missing or invalid.",
            _ => $"Updated {changed}; {missing} folder(s) missing or invalid.",
        };
    }
}
