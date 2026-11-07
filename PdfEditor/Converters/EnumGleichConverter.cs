using System.Globalization;
using System.Windows.Data;

namespace PdfEditor.Converters;

/// <summary>
/// Bindet einen Enum-Wert an eine RadioButton-Gruppe: true, wenn der Wert dem
/// ConverterParameter (Name des Enum-Members) entspricht; Rückweg setzt diesen Wert.
/// </summary>
public class EnumGleichConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is string name && value.ToString() == name;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string name
            ? Enum.Parse(Nullable.GetUnderlyingType(targetType) ?? targetType, name)
            : Binding.DoNothing;
}
