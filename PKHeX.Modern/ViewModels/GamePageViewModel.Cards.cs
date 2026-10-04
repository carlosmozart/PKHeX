using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class GamePageViewModel
{
    public bool HasCards => _sav is not null && WonderCards.Supported(_sav);
    public bool IsCardsTab => Tab == 5;
    public string CardsNote => _sav is SAV7b
        ? "Let's Go guarda apenas registros de eventos recebidos; não há cartões esperando entrega."
        : "Cartões guardados no save para receber dentro do jogo. As alterações serão gravadas com Salvar.";
    public IReadOnlyList<WonderCardRowViewModel> CardRows { get; private set; } = [];
    private WonderCardRowViewModel? _selectedCard;
    public WonderCardRowViewModel? SelectedCard
    {
        get => _selectedCard;
        set
        {
            if (!Set(ref _selectedCard, value)) return;
            Raise(nameof(CanUseCard)); Raise(nameof(ExportCardCommand)); Raise(nameof(DeleteCardCommand)); Raise(nameof(UnusedCardCommand));
        }
    }
    public bool CanUseCard => SelectedCard is { Gift.IsEmpty: false };
    public Func<Task<DataMysteryGift?>>? PickCardFile { get; set; }
    public Func<DataMysteryGift, Task>? ExportCardFile { get; set; }
    public RelayCommand ImportCardCommand => new(() => _ = ImportCardAsync());
    public RelayCommand ExportCardCommand => new(() => { if (SelectedCard is { } card && ExportCardFile is { } export) _ = export(card.Gift.Clone()); }, () => CanUseCard);
    public RelayCommand DeleteCardCommand => new(() => _ = DeleteCardAsync(), () => CanUseCard);
    public RelayCommand UnusedCardCommand => new(() => _ = UnusedCardAsync(), () => CanUseCard && SelectedCard!.Gift is not WR7);
    private void RefreshCards()
    {
        CardRows = _sav is null ? [] : System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(WonderCards.Read(_sav),
            (gift, index) => new WonderCardRowViewModel(index, gift)));
        SelectedCard = null; Raise(nameof(CardRows)); Raise(nameof(HasCards)); Raise(nameof(CardsNote));
    }
    public bool AddCard(DataMysteryGift gift)
    {
        if (_sav is null) return false;
        int slot = WonderCards.Add(_sav, gift, out var reason);
        if (slot < 0) { _status("Cartão recusado: " + reason); return false; }
        RefreshCards(); SelectedCard = CardRows[slot]; Changed?.Invoke(); Tab = 5;
        _status($"Cartão adicionado na posição {slot + 1}. Salve para gravar."); return true;
    }
    private async Task ImportCardAsync()
    {
        if (PickCardFile is null) return;
        var sav = _sav; var gift = await PickCardFile();
        if (!ReferenceEquals(_sav, sav)) return;
        if (gift is not null) AddCard(gift);
    }
    public async Task DeleteCardAsync()
    {
        if (_sav is null || SelectedCard is not { } card || card.Gift.IsEmpty) return;
        var sav = _sav;
        if (!await _confirm("Apagar cartão?", "O cartão será removido do save. Esta edição não tem desfazer.", "Apagar") || !ReferenceEquals(_sav, sav)) return;
        WonderCards.Delete(sav, card.Index); RefreshCards(); Changed?.Invoke(); _status("Cartão apagado. Salve para gravar.");
    }
    public async Task UnusedCardAsync()
    {
        if (_sav is null || SelectedCard is not { } card || card.Gift is WR7 || card.Gift.IsEmpty) return;
        var sav = _sav;
        if (!await _confirm("Receber o cartão novamente?", "O cartão e sua flag de recebido serão marcados como não recebidos.", "Marcar como não recebido") || !ReferenceEquals(_sav, sav)) return;
        WonderCards.MarkUnused(sav, card.Index); RefreshCards(); Changed?.Invoke(); _status("Cartão marcado como não recebido. Salve para gravar.");
    }
}

public sealed class WonderCardRowViewModel(int index, DataMysteryGift gift)
{
    public int Index { get; } = index;
    public DataMysteryGift Gift { get; } = gift;
    public string Title => $"#{Index + 1} · {(Gift.IsEmpty ? "Vazio" : Gift.CardTitle)}";
    public string Details => Gift.IsEmpty ? Gift.Extension.ToUpperInvariant() : $"{Gift.Extension.ToUpperInvariant()} · #{Gift.CardID} · {(Gift is WR7 || Gift.GiftUsed ? "Recebido" : "Pendente")} · {Date}";
    private string Date => (Gift switch { PGF g => g.Date, WC6 g => g.Date, WC7 g => g.Date, WR7 g => g.Date, _ => null })?.ToString("dd/MM/yyyy") ?? "";
    public Bitmap? Sprite => Gift.IsEmpty ? null : Gift.IsItem ? SpriteService.GetItemSprite(Gift.ItemID, Gift.Context) : SpriteService.GetSprite(Gift);
}
