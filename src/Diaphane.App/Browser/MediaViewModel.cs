using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diaphane.Shell.Media;
using Diaphane.Shell.Tabs;

namespace Diaphane.App.Browser;

public sealed record CodecRow(string Name, string Playback, string MediaSource);
public sealed record KeySystemRow(string Name, string Status);

/// <summary>
/// diaphane://media — asks the live engine what it can actually decode by running
/// <see cref="MediaProbe.Script"/> in the active tab. Nothing leaves the machine.
/// </summary>
public sealed partial class MediaViewModel : ObservableObject
{
    private readonly Func<TabModel?> _activeTab;

    public MediaViewModel(Func<TabModel?> activeTab)
    {
        _activeTab = activeTab;
        _status = "Run the probe to see what this build can play.";
    }

    public ObservableCollection<CodecRow> Codecs { get; } = new();
    public ObservableCollection<KeySystemRow> KeySystems { get; } = new();

    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status;

    [RelayCommand]
    private async Task ProbeAsync()
    {
        var tab = _activeTab();
        if (tab is null) { Status = "Open a tab first."; return; }

        Busy = true;
        Status = "Probing…";
        try
        {
            var json = await tab.EvaluateJavaScriptAsync(MediaProbe.Script);
            var report = MediaProbe.Parse(json);

            Codecs.Clear();
            foreach (var c in report.Codecs)
                Codecs.Add(new CodecRow(c.Name, c.CanPlayType, c.MediaSource));

            KeySystems.Clear();
            foreach (var k in report.KeySystems)
                KeySystems.Add(new KeySystemRow(k.Key, k.Value));

            Status = $"Probed {Codecs.Count} codecs on “{tab.Title}”.";
        }
        catch (Exception ex)
        {
            Status = "Probe failed: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }
}
