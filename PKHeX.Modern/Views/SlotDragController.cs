using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;

namespace PKHeX.Modern.Views;

/// <summary>
/// Arrastar e soltar de slots:
/// - slot → slot: move/troca (Ctrl ou Shift = copiar; Alt = sobrescrever, origem fica vazia);
/// - pairar sobre as setas de caixa troca de caixa durante o arraste;
/// - arquivo .pk* ou Mystery Gift → slot: importa; arquivo de save em qualquer outro lugar: abre o save;
/// - slot → fora da janela (Explorer, desktop): exporta como arquivo .pk*;
/// - Ctrl+clique marca/desmarca, Shift+clique marca um intervalo (selecao multipla); arrastar um marcado leva o grupo.
/// </summary>
public sealed class SlotDragController
{
    // Avalonia so aceita letras, digitos e pontos no identificador (com "/" ou "-" lanca excecao).
    private const string Format = "PKHeX.Modern.Slot";
    private static readonly DataFormat<string> SlotFormat = DataFormat.CreateStringApplicationFormat(Format);
    private const double Threshold = 6;

    private readonly Control _window;
    private readonly Func<MainViewModel> _vm;
    private readonly DispatcherTimer _boxHover = new() { Interval = TimeSpan.FromMilliseconds(550) };

    private SlotViewModel? _pressed;
    private bool _released;
    private Point _pressPoint;
    private KeyModifiers _pressMods;
    private SlotViewModel? _dragging;
    private SlotViewModel? _hoverTarget;
    private Button? _hoverArrow;

    public SlotDragController(Control window, Func<MainViewModel> vm)
    {
        _window = window;
        _vm = vm;
        window.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
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
            .FirstOrDefault(b => b.Classes.Contains("boxArrow") || b.Classes.Contains("boxTab"));

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed = null;
        if (!e.GetCurrentPoint(_window).Properties.IsLeftButtonPressed)
            return;
        if (SlotAt(e.Source) is { IsEmpty: false } slot)
        {
            _pressed = slot;
            _released = false;
            _pressPoint = e.GetPosition(_window);
            _pressMods = e.KeyModifiers;
        }
    }

    /// <summary>Soltou sem arrastar com Ctrl/Shift: marca o slot em vez de abrir no editor.</summary>
    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pressed = _pressed;
        _pressed = null;
        _released = true;
        if (pressed is null || _dragging is not null || pressed.IsParty || SlotAt(e.Source) != pressed)
            return;
        bool shift = _pressMods.HasFlag(KeyModifiers.Shift), ctrl = _pressMods.HasFlag(KeyModifiers.Control);
        if (!shift && !ctrl || _pressMods.HasFlag(KeyModifiers.Alt))
            return;
        _vm().ToggleMark(pressed, range: shift);
        e.Handled = true; // o botao nao recebe o clique (nao abre no editor)
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
            var item = DataTransferItem.Create(SlotFormat, "slot");
            if (await ExportTempFile(_dragging) is { } file)
                item.SetFile(file); // permite soltar no Explorer
            if (_released)
                return; // o botao foi solto enquanto o arquivo era gravado (ex.: duplo clique rapido)
            data.Add(item);
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move | DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex); // um arraste que falha nao pode derrubar o app
        }
        finally
        {
            _dragging = null;
            SetHover(null, null);
        }
    }

    /// <summary>Grava o Pokemon num arquivo temporario para o arraste para fora da janela.</summary>
    private async Task<IStorageFile?> ExportTempFile(SlotViewModel slot)
    {
        if (slot.Pkm is not { } pk)
            return null;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "PKHeX.Modern", "drag");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, CoreAdapter.GetEntityFileName(pk));
            CoreAdapter.ExportEntity(pk, path);
            return TopLevel.GetTopLevel(_window) is { } top ? await top.StorageProvider.TryGetFileFromPathAsync(path) : null;
        }
        catch (Exception)
        {
            return null; // sem arquivo, o arraste interno continua funcionando
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var slot = SlotAt(e.Source);
        var arrow = _dragging is not null ? ArrowAt(e.Source) : null;
        SetHover(slot, arrow);

        if (_dragging is not null)
            e.DragEffects = slot is null ? DragDropEffects.None
                : GetMode(e.KeyModifiers) == DropMode.Copy ? DragDropEffects.Copy : DragDropEffects.Move;
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
                _ = _vm().MoveSlotAsync(src, target, GetMode(e.KeyModifiers));
            return;
        }

        if (e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath() is not { } path)
            return;
        if (target is not null && _vm().HasSave)
            _ = _vm().ImportFileAsync(target, path);
        else
            _ = _vm().OpenAsync(path);
    }

    /// <summary>Ctrl ou Shift = copiar; Alt = sobrescrever (origem fica vazia); sem tecla = mover/trocar.</summary>
    private static DropMode GetMode(KeyModifiers keys)
        => keys.HasFlag(KeyModifiers.Alt) ? DropMode.Overwrite
            : keys.HasFlag(KeyModifiers.Control) || keys.HasFlag(KeyModifiers.Shift) ? DropMode.Copy
            : DropMode.Move;

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
