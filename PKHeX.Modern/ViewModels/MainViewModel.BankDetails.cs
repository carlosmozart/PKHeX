using System.Threading.Tasks;
using System.Linq;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class MainViewModel
{
    private void InspectBank(SlotViewModel slot)
    {
        Bank.Details = slot is not { IsBank: true, IsEmpty: false } ? null : new BankDetailsViewModel(slot, _sav,
            new RelayCommand(() => _ = OpenBankEditorAsync(slot)),
            new RelayCommand(() => { if (Bank.ExportDetails is { } export) _ = export(); }),
            new RelayCommand(() => Bank.ShowVariants(slot)),
            new RelayCommand(() => { if (Bank.CopyDetails is { } copy && Bank.Details is { } details) _ = copy(details.Showdown); }),
            new RelayCommand(() => Bank.Details = null));
    }

    private async Task OpenBankEditorAsync(SlotViewModel slot)
    {
        if (_sav is null || slot.Pkm is not { } original) return;
        var converted = CoreAdapter.ConvertForSave(_sav, original.Clone(), out var error);
        if (converted is null) { Status = error ?? "Este Pokémon não cabe no save aberto."; return; }
        if (!await ConfirmDiscardEditAsync() || !await ConfirmTransferAsync([(original, converted)])) return;
        var destination = Boxes.Slots.FirstOrDefault(s => s.IsEmpty);
        if (destination is null) { Status = "A caixa atual está cheia. Escolha uma caixa com espaço e tente de novo."; return; }
        CurrentPage = Boxes;
        SelectSlot(destination, converted);
        Status = "Cópia do Bank aberta no editor. Confira e clique em Aplicar; o original do Bank é preservado.";
    }
}
