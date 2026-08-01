using System.Globalization;
using System.Windows.Data;

namespace J2P.App.Converters;

/// <summary>int プロパティと ConverterParameter の一致を bool にする（ラジオボタン用）。</summary>
public sealed class IndexMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int i && parameter is string s && int.TryParse(s, out int p) && i == p;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string s && int.TryParse(s, out int p))
            return p;
        return Binding.DoNothing;
    }
}
