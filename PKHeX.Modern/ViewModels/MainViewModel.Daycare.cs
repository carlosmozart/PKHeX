using System.Threading.Tasks;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class MainViewModel
{
    public async Task EditDaycareAsync(DaycareSlotViewModel slot)
    {
        if (!ReferenceEquals(slot.Save, _sav) || !slot.Occupied || !await ConfirmDiscardEditAsync()) return;
        if (_selectedSlot is { } selected) selected.IsSelected = false;
        _selectedSlot = null; RaiseSelectionChanged();
        var sav = slot.Save; var tab = Editor?.SelectedTab ?? 0;
        Editor = new PokemonEditorViewModel(slot.Pokemon, $"Creche {slot.Area + 1}, posição {slot.Slot + 1}", pk =>
        {
            if (!ReferenceEquals(_sav, sav) || pk.Species == 0) return;
            _history!.Record("editar creche", SlotHistory.Key.Daycare);
            Daycares.Write(sav, slot.Storage, slot.Slot, pk);
            IsDirty = true; OnHistoryChanged(); Party.RefreshDaycare();
            Status = "Pokémon da creche atualizado. Salve para gravar.";
        }, s => Status = s, sav: sav, legalMode: LegalMode) { SelectedTab = tab, ShowQr = ShowPokemonQr, Confirm = (t, m, ok) => ConfirmAsync(t, m, ok) };
    }

    public async Task DepositDaycareAsync(DaycareSlotViewModel destination)
    {
        if (!ReferenceEquals(destination.Save, _sav) || destination.Occupied) return;
        if (_selectedSlot is not { IsEmpty: false, IsBank: false, IsOther: false } source)
        { Status = "Selecione um Pokémon das caixas ou da equipe para pôr na creche."; return; }
        if (!await ConfirmDiscardEditAsync()) return;
        var pk = source.Pkm!.Clone();
        if (pk.IsEgg) { Status = "Ovos não podem ser postos na creche."; return; }
        if (LegalMode && CoreAdapter.IsLegal(pk) != true) { Status = "Modo legal: legalize o Pokémon antes de pôr na creche."; return; }
        _history!.Record("pôr na creche", SlotHistory.Key.Daycare, SlotHistory.KeyOf(source.Box, source.Slot));
        var error = CoreAdapter.DeleteSlot(_sav!, CoreAdapter.GetSlotInfo(_sav!, source.Box, source.Slot));
        if (error is not null) { _history.Discard(); Status = error; return; }
        Daycares.Write(_sav!, destination.Storage, destination.Slot, pk);
        if (destination.Storage is IDaycareExperience exp) exp.SetDaycareEXP(destination.Slot, 0);
        DaycareMoved("Pokémon posto na creche. Desfazer restaura a origem.");
    }

    public async Task WithdrawDaycareAsync(DaycareSlotViewModel source)
    {
        if (!ReferenceEquals(source.Save, _sav) || !source.Occupied || !await ConfirmDiscardEditAsync()) return;
        var sav = source.Save; int index = sav.NextOpenBoxSlot();
        if (index < 0) { Status = "Não há espaço nas caixas para tirar o Pokémon da creche."; return; }
        var pk = source.Pokemon;
        _history!.Record("tirar da creche", SlotHistory.Key.Daycare, new(index / sav.BoxSlotCount, index % sav.BoxSlotCount));
        sav.SetBoxSlotAtIndex(pk, index);
        Daycares.Write(sav, source.Storage, source.Slot, sav.BlankPKM);
        if (source.Storage is IDaycareExperience exp) exp.SetDaycareEXP(source.Slot, 0);
        DaycareMoved("Pokémon retirado para o primeiro espaço livre das caixas.");
    }
    private void DaycareMoved(string status)
    {
        Editor = null; _selectedSlot = null; IsDirty = true; OnHistoryChanged(); RefreshSlots(); RaiseSelectionChanged(); Status = status;
    }
}
