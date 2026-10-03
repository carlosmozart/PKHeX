using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PKHeX.Modern.Controls;

/// <summary>
/// Abas das caixas centralizadas: quando cabem, ficam no centro da pagina (o <see cref="SpacerProperty"/>, do
/// tamanho do botao Ordenar, equilibra o outro lado); quando nao cabem, o espaco some e a lista rola para deixar a
/// aba atual (classe <c>current</c>) no meio.
/// </summary>
public static class CenteredTabs
{
    public static readonly AttachedProperty<Control?> SpacerProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, Control?>("Spacer", typeof(CenteredTabs));

    public static Control? GetSpacer(ScrollViewer s) => s.GetValue(SpacerProperty);
    public static void SetSpacer(ScrollViewer s, Control? value) => s.SetValue(SpacerProperty, value);

    static CenteredTabs()
    {
        SpacerProperty.Changed.AddClassHandler<ScrollViewer>((s, _) => Attach(s));
    }

    private static void Attach(ScrollViewer s)
    {
        Control? lastCurrent = null;
        s.LayoutUpdated += (_, _) =>
        {
            var spacer = GetSpacer(s);
            if (spacer is not null)
            {
                // Largura total da linha = area visivel + o espaco (se estiver aparecendo).
                double room = spacer.Bounds.Width + spacer.Margin.Left + spacer.Margin.Right;
                double total = s.Viewport.Width + (spacer.IsVisible ? room : 0);
                bool fits = s.Extent.Width <= total - room + 0.5;
                if (spacer.IsVisible != fits)
                {
                    spacer.IsVisible = fits;
                    return; // o layout roda de novo
                }
            }
            if (s.Extent.Width <= s.Viewport.Width + 0.5)
            {
                lastCurrent = null;
                return;
            }
            var current = s.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("current"));
            if (current is null || ReferenceEquals(current, lastCurrent) || s.Content is not Visual content)
                return;
            lastCurrent = current;
            if (current.TranslatePoint(new Point(current.Bounds.Width / 2, 0), content) is not { } mid)
                return;
            var x = Math.Clamp(mid.X - s.Viewport.Width / 2, 0, Math.Max(0, s.Extent.Width - s.Viewport.Width));
            Dispatcher.UIThread.Post(() => s.Offset = new Vector(x, s.Offset.Y));
        };
    }
}
