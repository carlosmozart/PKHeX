using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.Theme;

/// <summary>Real Avalonia controls rendered offscreen. Previewing never applies a theme to the running app.</summary>
public static class ThemePreview
{
    private static readonly Dictionary<(string Key, bool Dark), RenderTargetBitmap> Cache = [];
    public static RenderTargetBitmap Get(AppThemePreset theme, bool dark)
    {
        if (Cache.TryGetValue((theme.Key, dark), out var cached)) return cached;
        var palette = dark ? theme.Dark : theme.Light;
        IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));
        var foreground = dark ? Brushes.White : Brushes.Black;
        var font = FontFamily.Parse(theme.FontFamily ?? "fonts:Inter#Inter, $Default");
        Border Card(Control content, string color, int radius) => new()
        {
            Background = Brush(color), BorderBrush = Brush(palette.Border), BorderThickness = new Thickness(theme.BorderThickness),
            CornerRadius = new CornerRadius(Math.Round(radius * theme.RadiusScale)), Padding = new Thickness(5), Child = content,
        };
        var body = new StackPanel { Spacing = 5 };
        body.Children.Add(Card(new TextBlock { Text = "Pikachu · 25", FontFamily = font, FontSize = 15,
            FontWeight = FontWeight.Bold, Foreground = foreground }, palette.Card, 12));
        var slots = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 5 };
        ushort[] species = [1,4,7];
        for (int i = 0; i < species.Length; i++)
        {
            var content = new StackPanel { Spacing = 2 };
            content.Children.Add(new Image { Source = SpriteService.GetSprite(new PK8 { Species = species[i] }), Height = 40, Stretch = Stretch.Uniform });
            content.Children.Add(new TextBlock { Text = CoreAdapter.SpeciesNames[species[i]], FontSize = 9, FontFamily = font,
                Foreground = foreground, HorizontalAlignment = HorizontalAlignment.Center });
            var slot = Card(content, palette.Slot, 8); Grid.SetColumn(slot, i); slots.Children.Add(slot);
        }
        body.Children.Add(slots);
        var sample = new Border { Width = 232, Height = 126, Background = Brush(palette.Background), Padding = new Thickness(7), Child = body };
        sample.Measure(new Size(232,126)); sample.Arrange(new Rect(0,0,232,126)); sample.UpdateLayout();
        var bitmap = new RenderTargetBitmap(new PixelSize(232,126), new Vector(96,96)); bitmap.Render(sample);
        Cache[(theme.Key, dark)] = bitmap;
        return bitmap;
    }
}
