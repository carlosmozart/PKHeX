using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed class QrPreviewViewModel : IDisposable
{
    public string Title { get; }
    public string Note { get; }
    public string Message { get; }
    public byte[] Png { get; }
    public Bitmap Image { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand SaveCommand { get; }
    public QrPreviewViewModel(string title, string note, string message, Action close, Func<byte[], Task> save)
    {
        Title = title; Note = note; Message = message; Png = QrImages.Png(message);
        using var stream = new MemoryStream(Png); Image = new Bitmap(stream);
        CloseCommand = new(close); SaveCommand = new(() => _ = save(Png));
    }
    public void Dispose() => Image.Dispose();
}
