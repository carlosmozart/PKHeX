using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using PKHeX.Modern.ViewModels;

namespace PKHeX.Modern.Views;

public sealed partial class SaveManagerView : UserControl
{
    public SaveManagerView() => InitializeComponent();

    private SaveManagerViewModel? VM => DataContext as SaveManagerViewModel;
    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border card && e.GetCurrentPoint(card).Properties.IsLeftButtonPressed
            && (e.Source as Control)?.FindAncestorOfType<Button>() is null && e.Source is not Button)
            card.Focus();
    }

    /// <summary>Duplo clique no cartao abre o save.</summary>
    private void OnCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SaveEntryViewModel entry)
            VM?.OpenCommand.Execute(entry);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.F5)
        {
            VM?.RefreshCommand.Execute(null);
            e.Handled = true;
        }
        if (e.Handled || VM is not { ShowSaves: true } vm || e.KeyModifiers != KeyModifiers.None) return;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is TextBox or ComboBox or CheckBox) return;
        var cards = this.GetVisualDescendants().OfType<Border>()
            .Where(c => c.Classes.Contains("saveCard") && c.IsEffectivelyVisible && c.DataContext is SaveEntryViewModel).ToArray();
        if (cards.Length == 0) return;
        var current = focused as Border;
        if (e.Key == Key.Enter && current?.DataContext is SaveEntryViewModel entry)
        {
            vm.OpenCommand.Execute(entry); e.Handled = true; return;
        }
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
        if (focused is Button && current is null) return;
        Border? next = cards.FirstOrDefault();
        if (current is not null && cards.Contains(current) && current.TranslatePoint(new(current.Bounds.Width / 2, current.Bounds.Height / 2), this) is { } origin)
        {
            next = cards.Where(c => c != current).Select(c => (Card: c, Point: c.TranslatePoint(new(c.Bounds.Width / 2, c.Bounds.Height / 2), this)))
                .Where(p => p.Point is { } pt && (e.Key switch { Key.Left => pt.X < origin.X - 1, Key.Right => pt.X > origin.X + 1, Key.Up => pt.Y < origin.Y - 1, _ => pt.Y > origin.Y + 1 }))
                .OrderBy(p => { var pt = p.Point!.Value; double dx = System.Math.Abs(pt.X - origin.X), dy = System.Math.Abs(pt.Y - origin.Y); return e.Key is Key.Left or Key.Right ? dx + dy * 4 : dy + dx * 4; })
                .Select(p => p.Card).FirstOrDefault();
        }
        if (next is not null) { next.Focus(); next.BringIntoView(); }
        e.Handled = true;
    }

    /// <summary>Restaurar como...: escolhe o arquivo de destino (sugere a pasta do save de origem e o nome original).</summary>
    private async void OnRestoreAs(object? sender, RoutedEventArgs e)
    {
        if (VM is not { } vm || (sender as Control)?.DataContext is not BackupEntryViewModel backup
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var start = backup.Source is { } src && System.IO.Path.GetDirectoryName(src) is { } dir && System.IO.Directory.Exists(dir)
            ? await storage.TryGetFolderFromPathAsync(dir) : null;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Restaurar backup como",
            SuggestedFileName = backup.SaveName,
            SuggestedStartLocation = start,
        });
        if (file?.TryGetLocalPath() is { } path)
            await vm.RestoreToAsync(backup, path);
    }

    private async void OnChooseFolder(object? sender, RoutedEventArgs e)
    {
        if (VM is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pasta de saves", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
            vm.Folder = path;
    }
}
