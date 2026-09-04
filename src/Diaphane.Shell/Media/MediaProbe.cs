using System.Text.Json;

namespace Diaphane.Shell.Media;

public sealed record MediaCodecStatus(string Name, string CanPlayType, string MediaSource);

public sealed record MediaReport(
    IReadOnlyList<MediaCodecStatus> Codecs,
    IReadOnlyList<KeyValuePair<string, string>> KeySystems);

/// <summary>
/// Probes the live engine for actual media capability — which codecs the built-in
/// ffmpeg can decode, MSE support, and which EME key systems are reachable.
/// Runs entirely in-page (Runtime.evaluate); contacts nothing.
/// </summary>
public static class MediaProbe
{
    /// <summary>A self-contained expression returning the capability object.</summary>
    public const string Script = """
        (() => {
          const v = document.createElement('video');
          const a = document.createElement('audio');
          const cpt = (el, t) => { const r = el.canPlayType(t); return r === '' ? 'No' : (r === 'probably' ? 'Probably' : 'Maybe'); };
          const mse = (t) => (window.MediaSource && MediaSource.isTypeSupported(t)) ? 'Yes' : 'No';
          const checks = [
            ['H.264 (AVC)',   'video/mp4; codecs="avc1.42E01E"', true],
            ['H.265 (HEVC)',  'video/mp4; codecs="hvc1.1.6.L93.90"', true],
            ['VP9',           'video/webm; codecs="vp9"', true],
            ['AV1',           'video/mp4; codecs="av01.0.04M.08"', true],
            ['AAC',           'audio/mp4; codecs="mp4a.40.2"', false],
            ['MP3',           'audio/mpeg', false],
            ['Opus',          'audio/webm; codecs="opus"', false],
            ['FLAC',          'audio/flac', false],
            ['Vorbis',        'audio/webm; codecs="vorbis"', false]
          ];
          const codecs = checks.map(([name, t, isVideo]) => ({
            name, canPlayType: cpt(isVideo ? v : a, t), mediaSource: mse(t)
          }));
          const systems = ['com.widevine.alpha', 'com.microsoft.playready.recommendation', 'org.w3.clearkey'];
          return (async () => {
            const keySystems = {};
            for (const s of systems) {
              try {
                await navigator.requestMediaKeySystemAccess(s, [{
                  initDataTypes: ['cenc'],
                  videoCapabilities: [{ contentType: 'video/mp4; codecs="avc1.42E01E"' }]
                }]);
                keySystems[s] = 'Available';
              } catch (e) { keySystems[s] = 'Not available'; }
            }
            return { codecs, keySystems };
          })();
        })()
        """;

    /// <summary>Parse the CDP <c>Runtime.evaluate</c> result JSON produced by running <see cref="Script"/>.</summary>
    public static MediaReport Parse(string cdpResultJson)
    {
        using var doc = JsonDocument.Parse(cdpResultJson);
        // CDP shape: { "result": { "type": "object", "value": { codecs: [...], keySystems: {...} } } }
        var root = doc.RootElement;
        var value = root.TryGetProperty("result", out var res) && res.TryGetProperty("value", out var v)
            ? v
            : root; // tolerate being handed the bare value object

        var codecs = new List<MediaCodecStatus>();
        if (value.TryGetProperty("codecs", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var c in arr.EnumerateArray())
                codecs.Add(new MediaCodecStatus(
                    c.GetProperty("name").GetString() ?? "",
                    c.GetProperty("canPlayType").GetString() ?? "",
                    c.GetProperty("mediaSource").GetString() ?? ""));

        var keys = new List<KeyValuePair<string, string>>();
        if (value.TryGetProperty("keySystems", out var ks) && ks.ValueKind == JsonValueKind.Object)
            foreach (var p in ks.EnumerateObject())
                keys.Add(new(FriendlyKeySystem(p.Name), p.Value.GetString() ?? ""));

        return new MediaReport(codecs, keys);
    }

    private static string FriendlyKeySystem(string id) => id switch
    {
        "com.widevine.alpha" => "Widevine",
        "com.microsoft.playready.recommendation" => "PlayReady",
        "org.w3.clearkey" => "Clear Key",
        _ => id,
    };
}
