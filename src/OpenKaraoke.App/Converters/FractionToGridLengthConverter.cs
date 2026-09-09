using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OpenKaraoke.App.Converters;

/// <summary>
/// Maps a 0..1 fraction to a star-sized <see cref="GridLength"/>. Pass
/// ConverterParameter="inverse" to receive the remaining (1 - fraction) share,
/// which lets a progress fill and its empty remainder share a track proportionally.
/// </summary>
public sealed class FractionToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double fraction = value is double d ? Math.Clamp(d, 0, 1) : 0;
        if (Equals(parameter, "inverse"))
        {
            fraction = 1 - fraction;
        }

        return new GridLength(fraction, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
