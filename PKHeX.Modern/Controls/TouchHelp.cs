using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using System.Text.RegularExpressions;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.Controls;

/// <summary>Touch explanations use the existing tooltip text, without invoking the owner's action.</summary>
public static class TouchHelp
{
    public static bool TouchObserved { get; private set; }
    public static bool Enabled => !App.ShowShortcuts || TouchObserved;

    static TouchHelp()
    {
        Control.LoadedEvent.AddClassHandler<Button>((button, _) => Enhance(button));
        Control.LoadedEvent.AddClassHandler<ScrollViewer>((scroll, _) => Enhance(scroll));
    }

    public static void Initialize() { }
    public static void ObserveTouch(Control root)
    {
        TouchObserved = true;
        foreach (var visual in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root))
        {
            if (visual is Button button) Enhance(button);
            if (visual is ScrollViewer scroll) Enhance(scroll);
        }
    }

    private static void Enhance(ScrollViewer scroll)
    {
        if (!Enabled) return;
        ScrollViewer.SetIsScrollInertiaEnabled(scroll, true);
        scroll.AllowAutoHide = false;
    }

    private static void Enhance(Button button)
    {
        if (!Enabled || button.Content is not string || ToolTip.GetTip(button) is not string tip || tip.Length < 25
            || button.Classes.Contains("boxTab") || button.Classes.Contains("boxArrow") || button.Classes.Contains("tabClose") || button.Classes.Contains("tabNew")
            || button.ContentTemplate is not null || button.Classes.Contains("touchInfo")) return;
        button.ContentTemplate = new FuncDataTemplate<object>((content, _) =>
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
            grid.Children.Add(new TextBlock { Text = content?.ToString(), VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            var info = new TouchHint { Description = ToolTip.GetTip(button)?.ToString() ?? "" };
            Grid.SetColumn(info, 1); grid.Children.Add(info);
            return grid;
        });
    }
}

public sealed class TouchHint : Button
{
    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<TouchHint, string>(nameof(Description), "");
    public string Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public TouchHint()
    {
        Content = "ⓘ"; Classes.Add("touchInfo"); Classes.Add("ghost");
        Padding = new Thickness(6, 2); MinWidth = 32; MinHeight = 30;
        Click += (_, e) =>
        {
            e.Handled = true;
            var description = Loc.T(Description);
            if (!App.ShowShortcuts)
                description = Regex.Replace(description, @"\([^)]*(?:Ctrl|Shift|Delete|Esc|F[1-9])[^)]*\)|Ctrl\+[^\s,.);]+|Shift\+[^\s,.);]+", "").Trim();
            var text = new TextBlock { Text = description, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 300 };
            var flyout = new Flyout { Content = text };
            flyout.ShowAt(this);
        };
    }
}
