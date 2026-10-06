using System;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class MainViewModel
{
    private PKM? ReadDexOrigin(DbEntry entry)
    {
        if (entry.EntityFile is { } file) return BankStorage.ReadEntity(file);
        var tab = FindTab(entry.Source.Id);
        var source = tab == _activeTab ? _sav : tab?.Sav;
        source ??= CoreAdapter.LoadSave(entry.Source.Id);
        if (source is null) return null;
        return entry.Box < 0 ? source.GetPartySlotAtIndex(entry.Slot) : source.GetBoxSlotAtIndex(entry.Box, entry.Slot);
    }

    private async Task<bool> ApplyLivingDexAsync(LivingDexApplicationPlan preview)
    {
        if (_sav is not { } sav || _history is null || !LivingDexApplication.Validate(preview, sav, ReadDexOrigin))
        { Status = "A origem ou o destino mudou. Refazer a prévia é necessário."; return false; }
        if (!await ConfirmDiscardEditAsync()) return false;
        if (!await ConfirmAsync("Aplicar Living Dex", preview.Summary, "Aplicar", details: Pokedex.LivingDex!.ApplicationDetails, icon: "📖")) return false;
        try
        {
            // Re-evaluate mode in case it changed while the user reviewed the dialog.
            if (LegalMode && preview.Placements.Any(p => p.Pokemon is { } pk && !new LegalityAnalysis(pk, sav.Personal, StorageSlotType.Box).Valid))
                throw new InvalidOperationException(Loc.T("O modo legal bloqueou um candidato. Refazer a prévia é necessário."));
            if (!ReferenceEquals(_sav, sav)) return false;
            LivingDexApplication.Apply(preview, sav, _history, ReadDexOrigin);
            IsDirty = true; OnHistoryChanged(); RefreshSlots(); Pokedex.Load(sav);
            Status = "Living Dex aplicada em memória. Use Salvar para gravar o arquivo.";
            return true;
        }
        catch (Exception ex) { Status = Loc.T("Não foi possível aplicar a Living Dex: ") + ex.Message; RefreshSlots(); return false; }
    }
}
