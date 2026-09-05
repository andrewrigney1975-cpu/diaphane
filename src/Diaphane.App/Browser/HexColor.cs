using Windows.UI;

namespace Diaphane.App.Browser;

/// <summary>Parses a "#RRGGBB" string (as stored for a bookmark group's colour) into a WinUI Color.</summary>
internal static class HexColor
{
    public static Color Parse(string hex)
    {
        var h = hex.TrimStart('#');
        byte r = Convert.ToByte(h[..2], 16);
        byte g = Convert.ToByte(h[2..4], 16);
        byte b = Convert.ToByte(h[4..6], 16);
        return Color.FromArgb(255, r, g, b);
    }
}
