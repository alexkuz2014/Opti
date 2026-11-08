using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Opti.App.Controls;

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is Visibility.Collapsed;
}

/// <summary>Пустая строка или null — скрыть.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrWhiteSpace(s) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Пустая коллекция или null — скрыть.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            int n => n > 0 ? Visibility.Visible : Visibility.Collapsed,
            ICollection c => c.Count > 0 ? Visibility.Visible : Visibility.Collapsed,
            IEnumerable e => e.Cast<object>().Any() ? Visibility.Visible : Visibility.Collapsed,
            _ => Visibility.Collapsed,
        };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
