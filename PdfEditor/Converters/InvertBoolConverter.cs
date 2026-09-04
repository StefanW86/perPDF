using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PdfEditor.Converters;

/// <summary>
/// Kehrt einen booleschen Wert um (true → false, false → true).
/// Wird im XAML für die Toggle-Buttons der Seitenleiste verwendet.
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
public class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>
/// Konvertiert einen booleschen Wert invertiert in Sichtbarkeit:
/// <c>false</c> → <see cref="Visibility.Visible"/>, <c>true</c> → <see cref="Visibility.Collapsed"/>.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class InvertBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v != Visibility.Visible;
}

/// <summary>
/// Konvertiert einen 0-basierten Seitenindex in eine 1-basierte Seitennummer als Zeichenkette.
/// </summary>
[ValueConversion(typeof(int), typeof(string))]
public class SeitenNummerConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && i >= 0 ? $"S. {i + 1}" : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
