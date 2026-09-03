using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Diaphane.App;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type t, object p, string l)
    {
        var b = value is bool v && v;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, string l) =>
        value is Visibility vis && vis == Visibility.Visible ^ Invert;
}
