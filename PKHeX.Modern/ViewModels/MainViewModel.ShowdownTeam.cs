using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>Equipe Showdown: colar varios sets na equipe ou na caixa atual e copiar a equipe/caixa inteira.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Area de transferencia (definida pela View).</summary>
    public Func<Task<string?>>? ReadClipboard { get; set; }
    public Func<string, Task>? WriteClipboard { get; set; }

    private void InitializeShowdownTeam()
    {
        Party.PasteShowdownCommand = new RelayCommand(() => _ = PasteShowdownTeamAsync(toParty: true));
        Party.CopyShowdownCommand = new RelayCommand(() => _ = CopyShowdownAsync(toParty: true));
        Boxes.PasteShowdownCommand = new RelayCommand(() => _ = PasteShowdownTeamAsync(toParty: false));
        Boxes.CopyShowdownCommand = new RelayCommand(() => _ = CopyShowdownAsync(toParty: false));
    }

    private async Task CopyShowdownAsync(bool toParty)
    {
        if (_sav is not { } sav || WriteClipboard is null)
            return;
        IEnumerable<PKM> pokemon = toParty
            ? Enumerable.Range(0, sav.PartyCount).Select(sav.GetPartySlotAtIndex)
            : Enumerable.Range(0, sav.BoxSlotCount).Select(i => sav.GetBoxSlotAtIndex(Boxes.CurrentBox, i));
        var list = pokemon.Where(p => p.Species > 0 && !p.IsEgg).ToList();
        if (list.Count == 0)
        {
            Status = toParty ? "A equipe está vazia." : "Esta caixa não tem Pokémon para copiar.";
            return;
        }
        await WriteClipboard(ShowdownBatch.Export(list));
        Status = string.Format(Loc.T(toParty ? "Equipe copiada no formato Showdown ({0} Pokémon)." : "Caixa copiada no formato Showdown ({0} Pokémon)."), list.Count);
    }

    /// <summary>Destinos livres: vagas da equipe (indices a partir do fim) ou slots vazios e nao travados da caixa atual.</summary>
    private List<int> FreeShowdownSlots(SaveFile sav, bool toParty)
    {
        if (toParty)
            return sav.HasParty ? [.. Enumerable.Range(sav.PartyCount, 6 - sav.PartyCount)] : [];
        int box = Boxes.CurrentBox;
        return [.. Enumerable.Range(0, sav.BoxSlotCount).Where(i => sav.GetBoxSlotAtIndex(box, i).Species == 0
            && !sav.GetBoxSlotFlags(box, i).IsOverwriteProtected())];
    }

    public async Task PasteShowdownTeamAsync(bool toParty)
    {
        if (_sav is not { } sav || ReadClipboard is null)
            return;
        var sets = ShowdownBatch.Split(await ReadClipboard());
        if (sets.Count == 0)
        {
            Status = "Nenhum set Showdown na área de transferência.";
            return;
        }
        int box = Boxes.CurrentBox;
        var free = FreeShowdownSlots(sav, toParty);
        var where = toParty ? Loc.T("a equipe") : CoreAdapter.GetBoxName(sav, box);
        if (sets.Count > free.Count)
        {
            Status = string.Format(Loc.T("{0} tem {1} vaga(s) e a área de transferência tem {2} set(s). Libere espaço ou escolha outra caixa."), where, free.Count, sets.Count);
            return;
        }
        if (!await ConfirmDiscardEditAsync())
            return;

        bool legalMode = LegalMode;
        Status = string.Format(Loc.T("Lendo {0} set(s) Showdown (legalizar pode levar alguns segundos por Pokémon)..."), sets.Count);
        List<ShowdownBatchItem> items;
        items = await Task.Run(() => sets.Select(s => ShowdownBatch.Build(sav, s, legalMode)).ToList());
        if (!ReferenceEquals(_sav, sav) || Boxes.CurrentBox != box || !free.SequenceEqual(FreeShowdownSlots(sav, toParty)))
        {
            Status = "O save mudou durante a leitura. Cole a equipe de novo.";
            return;
        }

        var ready = items.Where(i => i.Pk is not null).ToList();
        var details = items.Select(i => (i.Pk is null ? "✗ " : i.Legal ? "✓ " : "⚠ ") + i.Name
            + (i.Note.Length > 0 ? " — " + Loc.T(i.Note) : "")).ToList();
        if (ready.Count == 0)
        {
            await ConfirmAsync("Colar equipe Showdown", "Nenhum set pôde ser importado.", "OK", cancelText: "", details: details, icon: "📋", detailsExpanded: true);
            return;
        }
        var message = string.Format(Loc.T("{0} de {1} Pokémon vão para {2}. Dá para desfazer com Ctrl+Z."), ready.Count, items.Count, where);
        if (!await ConfirmAsync("Colar equipe Showdown", message, "Colar", details: details, icon: "📋", detailsExpanded: true))
            return;
        if (!ReferenceEquals(_sav, sav) || Boxes.CurrentBox != box || !free.SequenceEqual(FreeShowdownSlots(sav, toParty)))
        {
            Status = "O save mudou antes de colar. Cole a equipe de novo.";
            return;
        }

        var keys = toParty ? [SlotHistory.Key.Party] : free.Take(ready.Count).Select(i => new SlotHistory.Key(box, i)).ToArray();
        _history!.Record(string.Format(Loc.T("colar {0} set(s) Showdown"), ready.Count), keys);
        try
        {
            for (int n = 0; n < ready.Count; n++)
            {
                var pk = ready[n].Pk!;
                if (toParty) CoreAdapter.SetPartySlot(sav, pk, free[n]);
                else CoreAdapter.SetBoxSlot(sav, pk, box, free[n]);
            }
        }
        catch (Exception ex)
        {
            _history.Rollback(); // nada pela metade: volta os slots como estavam, sem deixar um refazer
            Status = "Erro ao colar a equipe: " + ex.Message;
            OnHistoryChanged();
            RefreshSlots();
            return;
        }
        IsDirty = true;
        OnHistoryChanged();
        RefreshSlots();
        CurrentPage = toParty ? Party : Boxes;
        Status = string.Format(Loc.T("{0} Pokémon colados em {1}. Ctrl+Z desfaz; Salvar grava no arquivo."), ready.Count, where);
    }
}
