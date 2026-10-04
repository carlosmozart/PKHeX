using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.Views;

public sealed partial class MainView
{
    private async Task<DataMysteryGift?> PickCardFileAsync()
    {
        try
        {
            var file = (await Storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Loc.T("Importar cartão"), AllowMultiple = false })).FirstOrDefault();
            if (await LocalPathAsync(file) is not { } path) return null;
            var data = await File.ReadAllBytesAsync(path);
            var gift = Path.GetExtension(path).Equals(".wr7", StringComparison.OrdinalIgnoreCase) && data.Length == WR7.Size
                ? new WR7(data) : MysteryGift.GetMysteryGift(data, Path.GetExtension(path)) as DataMysteryGift;
            if (gift is null) VM.Status = "Arquivo de cartão não reconhecido.";
            return gift;
        }
        catch (Exception ex) { await ShowErrorAsync("Não foi possível importar o cartão", ex); return null; }
    }
    private async Task ExportCardFileAsync(DataMysteryGift gift)
    {
        try
        {
            var file = await Storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Loc.T("Exportar cartão"), SuggestedFileName = PathUtil.CleanFileName(gift.FileName) });
            await WriteAsync(file, path => { File.WriteAllBytes(path, gift.Write().ToArray()); return true; }, "Não foi possível exportar o cartão");
        }
        catch (Exception ex) { await ShowErrorAsync("Não foi possível exportar o cartão", ex); }
    }
}
