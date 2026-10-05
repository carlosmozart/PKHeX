using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Text.RegularExpressions;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.Controls;

/// <summary>
/// Touch explanations: holding a button shows its tooltip text without invoking the button's action,
/// as Android does. Slots are left to SlotDragController (holding a slot starts the touch selection).
/// </summary>
public static class TouchHelp
{
    private const double MoveTolerance = 12;
    private static readonly DispatcherTimer Hold = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private static Button? _target, _suppress;
    private static Point _start;

    public static bool TouchObserved { get; private set; }
    public static bool Enabled => !App.ShowShortcuts || TouchObserved;
    /// <summary>Last explanation shown (tests and diagnostics).</summary>
    public static string? LastDescription { get; private set; }

    static TouchHelp()
    {
        Control.LoadedEvent.AddClassHandler<ScrollViewer>((scroll, _) => Enhance(scroll));
        InputElement.PointerPressedEvent.AddClassHandler<Button>(OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<Button>(OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<Button>(OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerCaptureLostEvent.AddClassHandler<Button>((button, _) => { if (button == _target) Cancel(); });
        Hold.Tick += (_, _) =>
        {
            Hold.Stop();
            if (_target is { } button && ToolTip.GetTip(button) is string tip)
            {
                _suppress = button; // the release that follows must not click
                Show(button, tip);
            }
            _target = null;
        };
    }

    public static void Initialize() { }

    /// <summary>First real touch on a desktop with a touch screen: enables the touch behaviour for the loaded lists.</summary>
    public static void ObserveTouch(Control root)
    {
        if (TouchObserved) return;
        TouchObserved = true;
        foreach (var visual in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root))
            if (visual is ScrollViewer scroll) Enhance(scroll);
    }

    /// <summary>Shows a description in a flyout, without the keyboard shortcuts when there is no keyboard.</summary>
    public static void Show(Control owner, string description)
    {
        description = Loc.T(description);
        if (!App.ShowShortcuts)
            description = Regex.Replace(description, @"\s*\([^)]*(?:Ctrl|Shift|Delete|Esc|F[1-9])[^)]*\)|\s*Ctrl\+[^\s,.);]+|\s*Shift\+[^\s,.);]+", "").Trim();
        LastDescription = description;
        var text = new TextBlock { Text = description, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 300 };
        new Flyout { Content = text }.ShowAt(owner);
    }

    private static void Enhance(ScrollViewer scroll)
    {
        if (!Enabled) return;
        ScrollViewer.SetIsScrollInertiaEnabled(scroll, true);
        scroll.AllowAutoHide = false;
    }

    private static bool IsTouch(PointerEventArgs e) => e.Pointer.Type == PointerType.Touch || !App.ShowShortcuts;

    private static void OnPressed(Button button, PointerPressedEventArgs e)
    {
        if (!IsTouch(e) || button is TouchHint || button.DataContext is ViewModels.SlotViewModel
            || ToolTip.GetTip(button) is not string { Length: > 0 })
            return;
        // A nested button (e.g. inside a card) is reached after its parent in the tunnel: the innermost wins.
        Hold.Stop();
        _target = button; _suppress = null;
        _start = e.GetPosition(null);
        Hold.Start();
    }

    private static void OnMoved(Button button, PointerEventArgs e)
    {
        if (button != _target) return;
        var d = e.GetPosition(null) - _start;
        if (Math.Abs(d.X) > MoveTolerance || Math.Abs(d.Y) > MoveTolerance) Cancel(); // scrolling, not holding
    }

    private static void OnReleased(Button button, PointerReleasedEventArgs e)
    {
        if (button == _target) Cancel();
        if (button != _suppress) return;
        _suppress = null;
        e.Handled = true; // the explanation was shown: the button does not run its action
    }

    private static void Cancel() { Hold.Stop(); _target = null; }
}

/// <summary>Explicit ⓘ for places where holding is not discoverable (theme cards, touch toolbar).</summary>
public sealed class TouchHint : Button
{
    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<TouchHint, string>(nameof(Description), "");
    public string Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public TouchHint()
    {
        Content = "ⓘ"; Classes.Add("touchInfo"); Classes.Add("ghost");
        Padding = new Thickness(6, 2); MinWidth = 32; MinHeight = 30;
        Click += (_, e) => { e.Handled = true; TouchHelp.Show(this, Description); };
    }
}
