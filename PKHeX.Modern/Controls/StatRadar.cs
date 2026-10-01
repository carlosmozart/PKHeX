using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace PKHeX.Modern.Controls;

/// <summary>Grafico hexagonal (radar) dos 6 atributos.</summary>
public sealed class StatRadar : Avalonia.Controls.Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<StatRadar, IReadOnlyList<double>?>(nameof(Values));
    public static readonly StyledProperty<IReadOnlyList<string>?> LabelsProperty =
        AvaloniaProperty.Register<StatRadar, IReadOnlyList<string>?>(nameof(Labels));
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<StatRadar, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<StatRadar, IBrush?>(nameof(GridBrush));
    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<StatRadar, IBrush?>(nameof(LabelBrush));

    static StatRadar() => AffectsRender<StatRadar>(ValuesProperty, LabelsProperty, FillProperty, GridBrushProperty, LabelBrushProperty);

    /// <summary>Valores normalizados (0..1), ordem PS, Atq, Def, AtE, DeE, Vel.</summary>
    public IReadOnlyList<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IReadOnlyList<string>? Labels { get => GetValue(LabelsProperty); set => SetValue(LabelsProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush? LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    // Ordem no hexagono (sentido horario a partir do topo): PS, Atq, Def, Vel, DeE, AtE
    private static readonly int[] Order = [0, 1, 2, 5, 4, 3];

    public override void Render(DrawingContext ctx)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = (size / 2) - 22;
        if (radius <= 0)
            return;

        Point At(int i, double r)
        {
            var angle = (-Math.PI / 2) + (i * Math.PI / 3);
            return new Point(center.X + (r * Math.Cos(angle)), center.Y + (r * Math.Sin(angle)));
        }

        var gridPen = new Pen(GridBrush ?? Brushes.Gray, 1);
        foreach (var f in (double[])[0.25, 0.5, 0.75, 1])
            ctx.DrawGeometry(null, gridPen, Polygon(i => At(i, radius * f)));
        for (int i = 0; i < 6; i++)
            ctx.DrawLine(gridPen, center, At(i, radius));

        if (Values is { Count: 6 } v)
        {
            var fill = Fill ?? Brushes.CornflowerBlue;
            var geo = Polygon(i => At(i, radius * Math.Clamp(v[Order[i]], 0.04, 1)));
            ctx.DrawGeometry(new SolidColorBrush(((ISolidColorBrush)fill).Color, 0.35), new Pen(fill, 2), geo);
        }

        if (Labels is { Count: 6 } l)
        {
            for (int i = 0; i < 6; i++)
            {
                var text = new FormattedText(l[Order[i]], CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    Typeface.Default, 11, LabelBrush ?? Brushes.Gray);
                var p = At(i, radius + 13);
                ctx.DrawText(text, new Point(p.X - (text.Width / 2), p.Y - (text.Height / 2)));
            }
        }
    }

    private static StreamGeometry Polygon(Func<int, Point> point)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(point(0), true);
        for (int i = 1; i < 6; i++)
            g.LineTo(point(i));
        g.EndFigure(true);
        return geo;
    }
}
