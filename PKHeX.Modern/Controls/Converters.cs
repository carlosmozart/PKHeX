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

/// <summary>Uma linha de uma grade virtualizada: os itens dela e quantas colunas a linha tem.</summary>
public sealed record GridRow(System.Collections.Generic.IReadOnlyList<object> Items, int Columns);

/// <summary>
/// Agrupa uma lista em linhas de N colunas (N pela largura: "larguraMinima,maximoDeColunas"). Com um ListBox de linhas,
/// so as linhas visiveis sao criadas: grades grandes (mochila com 260 slots) abrem na hora em vez de montar tudo.
/// Valores: [lista, largura].
/// </summary>
public sealed class ChunkRowsConverter : IMultiValueConverter
{
    public static readonly ChunkRowsConverter Instance = new();

    public object? Convert(System.Collections.Generic.IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not System.Collections.IEnumerable list)
            return null;
        var columns = (int)WidthToColumnsConverter.Instance.Convert(values[1], typeof(int), parameter, culture);
        var rows = new System.Collections.Generic.List<GridRow>();
        var current = new System.Collections.Generic.List<object>(columns);
        foreach (var item in list)
        {
            current.Add(item!);
            if (current.Count == columns)
            {
                rows.Add(new GridRow(current, columns));
                current = new System.Collections.Generic.List<object>(columns);
            }
        }
        if (current.Count > 0)
            rows.Add(new GridRow(current, columns));
        return rows;
    }
}
