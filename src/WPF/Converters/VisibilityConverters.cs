using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GameLanguageExplorer.WPF.Converters;

/// <summary>
/// <c>true</c> değerini <see cref="Visibility.Visible"/>, <c>false</c> değerini
/// <see cref="Visibility.Collapsed"/> olarak döndürür.
/// <see cref="Invert"/> ile ters çevrilebilir.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (Invert)
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isVisible = value is Visibility visibility && visibility == Visibility.Visible;
        return Invert ? !isVisible : isVisible;
    }
}

/// <summary>
/// Null veya boş olmayan bir metin değerini <see cref="Visibility.Visible"/> olarak döndürür.
/// Hata mesajı gibi "içerik varsa göster" senaryoları için kullanılır.
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasText = !string.IsNullOrWhiteSpace(value as string);
        if (Invert)
        {
            hasText = !hasText;
        }

        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(StringToVisibilityConverter)} yalnızca tek yönlü bağlamada kullanılabilir.");
}

/// <summary>
/// Sıfırdan büyük tamsayı değerlerini <see cref="Visibility.Visible"/> olarak döndürür.
/// Senkronizasyon istatistikleri gibi "veri varsa göster" senaryoları için kullanılır.
/// </summary>
public sealed class IntToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value is int i && i > 0;
        if (Invert)
        {
            hasValue = !hasValue;
        }

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(IntToVisibilityConverter)} yalnızca tek yönlü bağlamada kullanılabilir.");
}
