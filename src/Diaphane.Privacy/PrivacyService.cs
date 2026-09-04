namespace Diaphane.Privacy;

/// <summary>
/// The one place the shell asks to wipe data. Owns the persisted
/// <see cref="PrivacySettings"/>, turns a <see cref="ClearTimeRange"/> into a
/// concrete cut-off, and drives <see cref="DataClearer"/>. Sandbox contexts are
/// never involved — they hold nothing on disk.
/// </summary>
public sealed class PrivacyService
{
    private readonly DataClearer _clearer;
    private readonly PrivacySettingsStore _store;
    private readonly Func<DateTimeOffset> _now;

    public PrivacyService(DataClearer clearer, PrivacySettingsStore store, Func<DateTimeOffset>? now = null)
    {
        _clearer = clearer;
        _store = store;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        Settings = store.Load();
    }

    public PrivacySettings Settings { get; }

    /// <summary>User pressed "Clear now" with an explicit scope and range.</summary>
    public Task ClearAsync(ClearScope scope, ClearTimeRange range) =>
        scope == ClearScope.None
            ? Task.CompletedTask
            : _clearer.ClearAsync(new ClearRequest(scope, range.Since(_now())));

    /// <summary>Wipe everything in <paramref name="scope"/> with no time bound (panic hotkey).</summary>
    public Task ClearAllTimeAsync(ClearScope scope) =>
        scope == ClearScope.None
            ? Task.CompletedTask
            : _clearer.ClearAsync(new ClearRequest(scope, Since: null));

    public void SetClearOnExit(ClearScope scope)
    {
        Settings.ClearOnExit = scope;
        _store.Save(Settings);
    }

    public void SetAllowWidevine(bool allow)
    {
        Settings.EnableWidevine = allow;
        _store.Save(Settings);
    }

    public void SaveDefaults(ClearScope scope, ClearTimeRange range)
    {
        Settings.DefaultClearScope = scope;
        Settings.DefaultTimeRange = range;
        _store.Save(Settings);
    }

    /// <summary>Run the configured clear-on-exit wipe. No-op when disabled.</summary>
    public Task RunClearOnExitAsync() => ClearAllTimeAsync(Settings.ClearOnExit);
}
