using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DLBeastSaveManager.ViewModels;

using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace DLBeastSaveManager.Views;

public sealed class StateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ProtectionState state
            ? state switch
            {
                ProtectionState.Protected => new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50)),
                ProtectionState.Warning => new SolidColorBrush(Color.FromRgb(0xE3, 0xA0, 0x08)),
                _ => new SolidColorBrush(Color.FromRgb(0xA2, 0xA9, 0xB2))
            }
            : Brushes.Gray;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}
