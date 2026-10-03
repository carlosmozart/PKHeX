using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>
/// Uma pagina da barra lateral. Para adicionar uma nova funcao:
/// crie uma classe derivada, registre em <see cref="MainViewModel"/> e um DataTemplate em App.axaml.
/// </summary>
public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }
    public abstract string Icon { get; }
    /// <summary>Atalho exibido na barra lateral (Ctrl+1, Ctrl+2...). Definido pelo MainViewModel.</summary>
    public string Shortcut { get; set; } = "";
    /// <summary>Chamado quando a pagina altera o save (marca alteracoes nao exportadas).</summary>
    public Action? Changed { get; set; }
    /// <summary>Indica se a pagina se aplica ao save atual (ex.: jogos sem mochila).</summary>
    public virtual bool IsAvailable => true;
    public abstract void Load(SaveFile sav);
}

/// <summary>Base para paginas que exibem slots de Pokemon (caixas e equipe).</summary>
public abstract class SlotPageViewModel(Action<SlotViewModel> select) : PageViewModel
{
    public ObservableCollection<SlotViewModel> Slots { get; } = [];
    /// <summary>Chamado depois que os slots sao recarregados (a busca global reaplica o destaque).</summary>
    public Action? SlotsLoaded { get; set; }
    public RelayCommand SelectSlotCommand { get; } = new(p => { if (p is SlotViewModel s) select(s); });
}

public sealed class BoxesPageViewModel : SlotPageViewModel
{
    private SaveFile? _sav;

    public BoxesPageViewModel(Action<SlotViewModel> select) : base(select)
    {
        PreviousBoxCommand = new RelayCommand(() => CurrentBox--, () => CurrentBox > 0);
        NextBoxCommand = new RelayCommand(() => CurrentBox++, () => _sav is not null && CurrentBox < _sav.BoxCount - 1);
    }

    /// <summary>Faixa da equipe exibida acima das caixas (permite arrastar entre caixa e equipe).</summary>
    public PartyPageViewModel? Party { get; init; }
    public bool ShowParty => Party is { Slots.Count: > 0 };

    /// <summary>Ordenar (definido pelo MainViewModel, que guarda o desfazer): criterio e se vale para todas as caixas.</summary>
    public Action<CoreAdapter.BoxSortOption, bool>? Sort { get; set; }
    /// <summary>Criterios do menu "Ordenar" (cada um com "esta caixa" e "todas as caixas").</summary>
    public IReadOnlyList<SortOptionViewModel> SortOptions { get; private set; } = [];

    public override string Title => "Caixas";
    public override string Icon => "▦";
    public RelayCommand PreviousBoxCommand { get; }
    public RelayCommand NextBoxCommand { get; }

    private int _currentBox;
    public int CurrentBox
    {
        get => _currentBox;
        set
        {
            if (_sav is null || value < 0 || value >= _sav.BoxCount || !Set(ref _currentBox, value))
                return;
            LoadBox();
        }
    }

    public string BoxName => _sav is null ? "" : CoreAdapter.GetBoxName(_sav, CurrentBox);

    /// <summary>Abas com o nome de cada caixa (clique ou pare em cima durante o arraste para trocar).</summary>
    public ObservableCollection<BoxTabViewModel> BoxTabs { get; } = [];
    public int FilledCount => Slots.Count(s => !s.IsEmpty);
    public string BoxLabel => _sav is null ? "" : $"Caixa {CurrentBox + 1} de {_sav.BoxCount} · {FilledCount}/{Slots.Count} Pokémon";

    public override void Load(SaveFile sav)
    {
        _sav = sav;
        _currentBox = 0;
        BoxTabs.Clear();
        for (int i = 0; i < sav.BoxCount; i++)
        {
            int box = i;
            BoxTabs.Add(new BoxTabViewModel(CoreAdapter.GetBoxName(sav, i), new RelayCommand(() => CurrentBox = box)));
        }
        SortOptions = [.. CoreAdapter.GetBoxSortOptions(sav).Select(o => new SortOptionViewModel(o.Name,
            new RelayCommand(() => Sort?.Invoke(o, false)), new RelayCommand(() => Sort?.Invoke(o, true))))];
        Raise(nameof(SortOptions));
        Raise(nameof(CurrentBox));
        LoadBox();
        Raise(nameof(ShowParty));
    }

    public void Reload() => LoadBox();

    private void LoadBox()
    {
        if (_sav is null)
            return;
        Slots.Clear();
        for (int i = 0; i < _sav.BoxSlotCount; i++)
        {
            var s = new SlotViewModel(CurrentBox, i);
            s.Load(_sav);
            Slots.Add(s);
        }
        Raise(nameof(BoxName));
        Raise(nameof(BoxLabel));
        for (int i = 0; i < BoxTabs.Count; i++)
            BoxTabs[i].IsCurrent = i == CurrentBox;
        SlotsLoaded?.Invoke();
        PreviousBoxCommand.NotifyCanExecuteChanged();
        NextBoxCommand.NotifyCanExecuteChanged();
    }
}

public sealed class PartyPageViewModel(Action<SlotViewModel> select) : SlotPageViewModel(select)
{
    private SaveFile? _sav;
    public override string Title => "Equipe";
    public override string Icon => "◉";
    public override bool IsAvailable => _sav is null || CoreAdapter.GetPartyCount(_sav) > 0;

    public override void Load(SaveFile sav)
    {
        _sav = sav;
        Slots.Clear();
        for (int i = 0; i < CoreAdapter.GetPartyCount(sav); i++)
        {
            var s = new SlotViewModel(SlotViewModel.Party, i);
            s.Load(sav);
            Slots.Add(s);
        }
        SlotsLoaded?.Invoke();
        Raise(nameof(IsAvailable));
    }
}

public sealed class TrainerPageViewModel : PageViewModel
{
    private SaveFile? _sav;
    public override string Title => "Treinador";
    public override string Icon => "♟";

    public override void Load(SaveFile sav)
    {
        _sav = sav;
        Raise(string.Empty);
    }

    public string Game => _sav is null ? "" : CoreAdapter.GetGameName(_sav);
    public string Generation => _sav is null ? "" : $"Geração {_sav.Generation}";
    public string Checksum => _sav is null ? "" : _sav.ChecksumsValid ? "Válidos" : _sav.ChecksumInfo;
    public int MaxMoney => _sav?.MaxMoney ?? 0;

    public string OT { get => _sav?.OT ?? ""; set { if (_sav is not null) { _sav.OT = value; Changed?.Invoke(); } Raise(); } }
    public decimal TID { get => _sav?.DisplayTID ?? 0; set { if (_sav is not null) { _sav.DisplayTID = (uint)value; Changed?.Invoke(); } Raise(); } }
    public decimal SID { get => _sav?.DisplaySID ?? 0; set { if (_sav is not null) { _sav.DisplaySID = (uint)value; Changed?.Invoke(); } Raise(); } }
    public decimal Money { get => _sav?.Money ?? 0; set { if (_sav is not null) { _sav.Money = (uint)Math.Min(value, MaxMoney); Changed?.Invoke(); } Raise(); } }
    public decimal Hours { get => _sav?.PlayedHours ?? 0; set { if (_sav is not null) { _sav.PlayedHours = (int)value; Changed?.Invoke(); } Raise(); } }
    public decimal Minutes { get => _sav?.PlayedMinutes ?? 0; set { if (_sav is not null) { _sav.PlayedMinutes = (int)value; Changed?.Invoke(); } Raise(); } }
    public decimal Seconds { get => _sav?.PlayedSeconds ?? 0; set { if (_sav is not null) { _sav.PlayedSeconds = (int)value; Changed?.Invoke(); } Raise(); } }
}

public sealed class BagPageViewModel(Action<string> status) : PageViewModel
{
    private SaveFile? _sav;
    private PlayerBag? _bag;

    public override string Title => "Mochila";
    public override string Icon => "🎒";
    public override bool IsAvailable => _bag is null || Pouches.Count > 0;

    public ObservableCollection<PouchViewModel> Pouches { get; } = [];

    private PouchViewModel? _selected;
    public PouchViewModel? SelectedPouch { get => _selected; set => Set(ref _selected, value); }

    public RelayCommand SaveCommand => new(Save);

    public override void Load(SaveFile sav)
    {
        _sav = sav;
        Pouches.Clear();
        try
        {
            _bag = CoreAdapter.GetBag(sav);
            bool editable = CoreAdapter.IsBagItemIdEditable(sav);
            foreach (var p in _bag.Pouches)
                Pouches.Add(new PouchViewModel(_bag, p, editable, CoreAdapter.GetItemNames(sav), sav.Context, sav.Version));
        }
        catch (Exception ex)
        {
            status($"Mochila indisponível: {ex.Message}");
        }
        SelectedPouch = Pouches.FirstOrDefault();
        Raise(nameof(IsAvailable));
    }

    private void Save()
    {
        if (_sav is null || _bag is null)
            return;
        CoreAdapter.SaveBag(_sav, _bag);
        Changed?.Invoke();
        status("Mochila gravada. Lembre-se de exportar o save.");
    }
}

public sealed class PouchViewModel : ViewModelBase
{
    /// <param name="names">Nomes na numeracao do save (Gen 1-3 tem numeracao propria).</param>
    public PouchViewModel(PlayerBag bag, InventoryPouch pouch, bool editable, IReadOnlyList<string> names, EntityContext context, GameVersion version = GameVersion.Any)
    {
        Name = pouch.Type.ToString();
        Options = [.. pouch.GetAllItems().ToArray().Prepend((ushort)0).Distinct()
            .Select(id => new ItemOption(id, id == 0 ? "(nenhum)" : id < names.Count && names[id].Length > 0 ? names[id] : $"Item #{id}", context, version))];
        Items = [.. pouch.Items.Select(it => new BagItemViewModel(bag, pouch.Type, it, Options, editable))];
    }

    public string Name { get; }
    public IReadOnlyList<ItemOption> Options { get; }
    public IReadOnlyList<BagItemViewModel> Items { get; }
}

/// <summary>Um item da lista da mochila; o icone e gerado so quando aparece na tela.</summary>
public sealed record ItemOption(int Id, string Name, EntityContext Context = EntityContext.None, GameVersion Version = GameVersion.Any)
{
    public Avalonia.Media.Imaging.Bitmap? Icon => SpriteService.GetItemSprite(Id, Context);
    /// <summary>Descricao e onde conseguir (AllGenWiki), quando houver.</summary>
    public string? Tip => Id == 0 ? null : ItemInfo.GetTooltip(Name, Context.Generation, Version);
    public override string ToString() => Name;
}

public sealed class BagItemViewModel(PlayerBag bag, InventoryType type, InventoryItem item, IReadOnlyList<ItemOption> options, bool editable) : ViewModelBase
{
    public IReadOnlyList<ItemOption> Options { get; } = options;
    public bool IsEditable { get; } = editable;

    public ItemOption? Selected
    {
        get => Options.FirstOrDefault(o => o.Id == item.Index);
        set
        {
            if (value is null)
                return;
            item.Index = value.Id;
            if (value.Id == 0)
                item.Count = 0;
            Raise();
            Raise(nameof(Count));
            Raise(nameof(Icon));
            Raise(nameof(IsEmptySlot));
            Raise(nameof(Tip));
        }
    }

    public Avalonia.Media.Imaging.Bitmap? Icon => Selected?.Icon;
    /// <summary>Slot sem item (cartao apagado na grade da mochila).</summary>
    public bool IsEmptySlot => Selected is null or { Id: 0 };
    public string? Tip => Selected?.Tip;

    public decimal Count
    {
        get => item.Count;
        set { item.Count = item.Index == 0 ? 0 : bag.Clamp(type, item.Index, (int)value); Raise(); }
    }
}

/// <summary>Uma aba de caixa no topo da pagina Caixas.</summary>
public sealed class BoxTabViewModel(string name, RelayCommand go) : ViewModelBase
{
    public string Name { get; } = name;
    public RelayCommand GoCommand { get; } = go;
    private bool _isCurrent;
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
}

/// <summary>Um criterio do menu "Ordenar".</summary>
public sealed class SortOptionViewModel(string name, RelayCommand sortCurrent, RelayCommand? sortAll = null)
{
    public string Name { get; } = name;
    public RelayCommand SortCurrentCommand { get; } = sortCurrent;
    public RelayCommand? SortAllCommand { get; } = sortAll;
}
