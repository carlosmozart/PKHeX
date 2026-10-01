using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PKHeX.Modern.Controls;

/// <summary>
/// Converte a largura disponivel em numero de colunas.
/// Parametro: "larguraMinimaDoCartao,maximoDeColunas" (ex.: "130,6").
/// </summary>
public sealed class WidthToColumnsConverter : IValueConverter
{
    public static readonly WidthToColumnsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "130,6").Split(',');
        var min = double.Parse(parts[0], CultureInfo.InvariantCulture);
        var max = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var width = value is double d && d > 0 ? d : min * max;
        return Math.Clamp((int)(width / min), 1, max);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
