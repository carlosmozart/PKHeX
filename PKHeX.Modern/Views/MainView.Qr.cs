using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.Views;

public sealed partial class MainView
{
    private async Task SaveQrImageAsync(byte[] png)
    {
        try
        {
            var file = await Storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Loc.T("Salvar imagem"), SuggestedFileName = "pokemon-qr.png", DefaultExtension = "png" });
            await WriteAsync(file, path => { File.WriteAllBytes(path, png); return true; }, "Não foi possível salvar o QR");
        }
        catch (Exception ex) { await ShowErrorAsync("Não foi possível salvar o QR", ex); }
    }
}
