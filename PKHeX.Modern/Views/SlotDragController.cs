using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Modern.ViewModels;

namespace PKHeX.Modern.Views;

/// <summary>
/// Arrastar e soltar de slots:
/// - slot → slot: move/troca (Ctrl = copiar);
/// - pairar sobre as setas de caixa troca de caixa durante o arraste;
/// - arquivo .pk* → slot: importa; arquivo de save em qualquer outro lugar: abre o save.
/// </summary>
public sealed class SlotDragController
{
    private const string Format = "pkhex-modern/slot";
    private const double Threshold = 6;

    private readonly Window _window;
    private readonly Func<MainViewModel> _vm;
    private readonly DispatcherTimer _boxHover = new() { Interval = TimeSpan.FromMilliseconds(550) };

    private SlotViewModel? _pressed;
    private Point _pressPoint;
    private SlotViewModel? _dragging;
    private SlotViewModel? _hoverTarget;
    private Button? _hoverArrow;

    public SlotDragController(Window window, Func<MainViewModel> vm)
    {
        _window = window;
        _vm = vm;
        window.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.PointerReleasedEvent, (_, _) => _pressed = null, RoutingStrategies.Tunnel);
        window.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        window.AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetHover(null, null));
        window.AddHandler(DragDrop.DropEvent, OnDrop);
        _boxHover.Tick += (_, _) =>
        {
            _boxHover.Stop();
            if (_hoverArrow?.Command is { } cmd && cmd.CanExecute(null))
            {
                cmd.Execute(null);
                _boxHover.Start(); // continua passando caixas enquanto estiver sobre a seta
            }
        };
    }

    private static SlotViewModel? SlotAt(object? source)
        => (source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>()
            .FirstOrDefault(b => b.DataContext is SlotViewModel)?.DataContext as SlotViewModel;

    private static Button? ArrowAt(object? source)
        => (source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("boxArrow"));

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed = null;
        if (!e.GetCurrentPoint(_window).Properties.IsLeftButtonPressed)
            return;
        if (SlotAt(e.Source) is { IsEmpty: false } slot)
        {
            _pressed = slot;
            _pressPoint = e.GetPosition(_window);
        }
    }

    private async void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is null || _dragging is not null)
            return;
        var d = e.GetPosition(_window) - _pressPoint;
        if (Math.Abs(d.X) < Threshold && Math.Abs(d.Y) < Threshold)
            return;

        _dragging = _pressed;
        _pressed = null;
        try
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(DataFormat.CreateStringApplicationFormat(Format), "slot"));
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move | DragDropEffects.Copy);
        }
        finally
        {
            _dragging = null;
            SetHover(null, null);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var slot = SlotAt(e.Source);
        var arrow = _dragging is not null ? ArrowAt(e.Source) : null;
        SetHover(slot, arrow);

        if (_dragging is not null)
            e.DragEffects = slot is null ? DragDropEffects.None
                : e.KeyModifiers.HasFlag(KeyModifiers.Control) ? DragDropEffects.Copy : DragDropEffects.Move;
        else
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var target = SlotAt(e.Source);
        SetHover(null, null);
        e.Handled = true;

        if (_dragging is { } src)
        {
            if (target is not null)
                _vm().MoveSlot(src, target, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            return;
        }

        if (e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath() is not { } path)
            return;
        if (target is not null && _vm().HasSave)
            _vm().ImportFile(target, path);
        else
            _vm().Open(path);
    }

    private void SetHover(SlotViewModel? slot, Button? arrow)
    {
        if (_hoverTarget != slot)
        {
            if (_hoverTarget is not null)
                _hoverTarget.IsDropTarget = false;
            _hoverTarget = slot;
            if (slot is not null && slot != _dragging)
                slot.IsDropTarget = true;
        }
        if (_hoverArrow != arrow)
        {
            _hoverArrow = arrow;
            _boxHover.Stop();
            if (arrow is not null)
                _boxHover.Start();
        }
    }
}
