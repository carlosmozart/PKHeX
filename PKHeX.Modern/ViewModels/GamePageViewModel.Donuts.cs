using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;

namespace PKHeX.Modern.ViewModels;

public sealed partial class GamePageViewModel
{
    public bool HasDonuts => _sav is SAV9ZA za && za.AllBlocks.Any(b => b.Key == 0xBE007476 && b.Data.Length >= DonutPocket9a.MaxCount * Donut9a.Size);
    public bool IsDonutsTab => Tab == 9;
    public DonutPocketViewModel? Donuts { get; private set; }
    private void RefreshDonuts()
    {
        var save = _sav;
        Donuts = HasDonuts ? new(((SAV9ZA)_sav!).Donuts, Edit, _confirm, _status, () => ReferenceEquals(_sav, save)) : null;
        Raise(nameof(Donuts)); Raise(nameof(HasDonuts));
    }
}

public sealed record DonutChoice(ulong Hash, string Name) { public override string ToString() => Name; }
public sealed record DonutRow(int Index, string Name) { public override string ToString() => Name; }
public sealed record DonutBerryChoice(ushort Id, string Name) { public override string ToString() => Name; }

public sealed class DonutPocketViewModel : ViewModelBase
{
    private readonly DonutPocket9a _pocket;
    private readonly Action<string, Action> _edit;
    private readonly Func<string, string, string, Task<bool>> _confirm;
    private readonly Action<string> _status;
    private readonly Func<bool> _active;
    public const string Explanation = "Edite a cópia e use Aplicar donut. Os poderes usam os nomes e limites do PKHeX. Gerar por efeito usa o modelo do gerador do PKHeX. Encher substitui todos os 999 espaços, com confirmação e um passo de desfazer. Salvar grava o arquivo.";
    public string Note => Explanation;
    public IReadOnlyList<DonutChoice> Effects { get; } = [new(0, "Sem poder"), .. DonutInfo.Flavors.Select((f, i) => new DonutChoice(f.Hash, GameInfo.GetStrings("en").donutFlavor[i]))];
    public IReadOnlyList<string> Types { get; } = GameInfo.GetStrings("en").donutName;
    public IReadOnlyList<DonutRow> Rows { get; private set; } = [];
    private DonutRow? _selected;
    public DonutRow? Selected { get => _selected; set { if (Set(ref _selected, value)) { Draft = value is null ? null : new(_pocket.GetDonut(value.Index).Data.ToArray(), Effects, Types); Raise(nameof(Draft)); } } }
    public DonutDraftViewModel? Draft { get; private set; }
    private DonutChoice? _effect;
    public DonutChoice? Effect { get => _effect; set => Set(ref _effect, value); }
    public string Count => $"{Rows.Count} / 999 donuts";
    public DonutPocketViewModel(DonutPocket9a pocket, Action<string, Action> edit, Func<string, string, string, Task<bool>> confirm, Action<string> status, Func<bool> active)
    { _pocket = pocket; _edit = edit; _confirm = confirm; _status = status; _active = active; Effect = Effects[1]; Refresh(); }
    // IsEmpty in this Core revision is inverted; the generator sets this timestamp on occupied entries.
    public static bool Occupied(Donut9a donut) => donut.MillisecondsSince1970 != 0;
    private string Summary(int i)
    {
        var d = _pocket.GetDonut(i);
        string name = d.Donut < Types.Count ? Types[d.Donut] : $"Donut #{d.Donut}";
        return $"#{i + 1} · {name} · {d.Stars} ★ · " + string.Join(" / ", d.GetFlavors().ToArray().Where(h => h != 0).Select(h => Effects.FirstOrDefault(e => e.Hash == h)?.Name ?? h.ToString("X16")));
    }
    public void Refresh(int selected = -1)
    {
        selected = selected < 0 ? Selected?.Index ?? -1 : selected;
        Rows = Enumerable.Range(0, DonutPocket9a.MaxCount).Where(i => Occupied(_pocket.GetDonut(i))).Select(i => new DonutRow(i, Summary(i))).ToArray();
        Raise(nameof(Rows)); Raise(nameof(Count)); Selected = Rows.FirstOrDefault(r => r.Index == selected) ?? Rows.FirstOrDefault();
    }
    private int FreeSlot() => Enumerable.Range(0, DonutPocket9a.MaxCount).FirstOrDefault(i => !Occupied(_pocket.GetDonut(i)), -1);
    public RelayCommand ApplyCommand => new(() =>
    {
        if (Selected is not { } row || Draft is not { } draft || !_active()) return;
        var hashes = draft.Data.GetFlavors().ToArray();
        if (hashes.Any(h => h != 0 && !Effects.Any(e => e.Hash == h)) || draft.Stars is < 0 or > 5) { _status("Donut inválido: use estrelas de 0 a 5 e poderes conhecidos."); return; }
        _edit($"Aplicar donut #{row.Index + 1}", () => draft.Data.CopyTo(_pocket.GetDonut(row.Index))); Refresh(row.Index);
    });
    public RelayCommand GenerateCommand => new(() =>
    {
        if (Effect is not { Hash: not 0 } effect || !_active()) return; int slot = FreeSlot();
        if (slot < 0) { _status("A bolsa de donuts está cheia."); return; }
        _edit("Gerar donut", () => _pocket.SetRandomShinyTemplateRange([effect.Hash], slot, slot + 1)); Refresh(slot);
    });
    public RelayCommand DuplicateCommand => new(() =>
    {
        if (Selected is not { } row || !_active()) return; int slot = FreeSlot(); if (slot < 0) { _status("A bolsa de donuts está cheia."); return; }
        _edit($"Duplicar donut #{row.Index + 1}", () => { var d = _pocket.GetDonut(slot); _pocket.GetDonut(row.Index).CopyTo(d); d.MillisecondsSince1970 = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (ulong)slot; d.DateTime1900.Timestamp = DateTime.Now; }); Refresh(slot);
    });
    public RelayCommand DeleteCommand => new(() => _ = DeleteAsync());
    public async Task DeleteAsync()
    {
        if (Selected is not { } row || !await _confirm("Apagar donut", "Apagar o donut selecionado? A operação pode ser desfeita.", "Apagar") || !_active()) return;
        _edit($"Apagar donut #{row.Index + 1}", () => { _pocket.GetDonut(row.Index).Clear(); int write = 0; for (int read = 0; read < DonutPocket9a.MaxCount; read++) { var d = _pocket.GetDonut(read); if (!Occupied(d)) continue; if (write != read) { d.CopyTo(_pocket.GetDonut(write)); d.Clear(); } write++; } }); Refresh();
    }
    public RelayCommand FillCommand => new(() => _ = FillAsync());
    public async Task FillAsync()
    {
        if (Effect is not { Hash: not 0 } effect || !await _confirm("Encher bolsa de donuts", $"Substituir os 999 espaços por donuts com {effect.Name}? A operação pode ser desfeita.", "Encher") || !_active()) return;
        _edit("Encher bolsa de donuts", () => _pocket.SetRandomShinyTemplateRange([effect.Hash], 0, DonutPocket9a.MaxCount)); Refresh(0);
    }
}

public sealed class DonutDraftViewModel : ViewModelBase
{
    public Donut9a Data { get; }
    public IReadOnlyList<DonutChoice> Effects { get; }
    public IReadOnlyList<string> Types { get; }
    public IReadOnlyList<DonutBerryViewModel> Berries { get; }
    public RelayCommand RecalculateCommand => new(() => { Data.RecalculateDonutStats(); Raise(nameof(Stars)); Raise(nameof(Flavors)); });
    public DonutDraftViewModel(byte[] data, IReadOnlyList<DonutChoice> effects, IReadOnlyList<string> types)
    {
        Data = new(data); Effects = effects; Types = types;
        var choices = new[] { new DonutBerryChoice(0, "Sem fruta") }.Concat(DonutInfo.Berries.Select(b => new DonutBerryChoice(b.Item, GameInfo.GetStrings("en").itemlist[b.Item]))).ToArray();
        Berries = Enumerable.Range(0, 8).Select(i => new DonutBerryViewModel(Data, i, choices, () => Raise(nameof(Flavors)))).ToArray();
    }
    public int Stars { get => Data.Stars; set { if (value is >= 0 and <= 5) { var d = Data; d.Stars = (byte)value; Raise(); } } }
    public int TypeIndex { get => Data.Donut; set { if (value >= 0 && value < Types.Count) { var d = Data; d.Donut = (ushort)value; Raise(); } } }
    private DonutChoice? Get(ulong hash) => Effects.FirstOrDefault(e => e.Hash == hash);
    public DonutChoice? Power1 { get => Get(Data.Flavor0); set { if (value is not null) { var d = Data; d.Flavor0 = value.Hash; Raise(); } } }
    public DonutChoice? Power2 { get => Get(Data.Flavor1); set { if (value is not null) { var d = Data; d.Flavor1 = value.Hash; Raise(); } } }
    public DonutChoice? Power3 { get => Get(Data.Flavor2); set { if (value is not null) { var d = Data; d.Flavor2 = value.Hash; Raise(); } } }
    public string Flavors => string.Join(" · ", new[] { "Spicy", "Fresh", "Sweet", "Bitter", "Sour" }.Select((n, i) => $"{n}: {DataFlavor(i)}"));
    private int DataFlavor(int i) { Span<int> v = stackalloc int[5]; Data.RecalculateDonutFlavors(v); return v[i]; }
}

public sealed class DonutBerryViewModel(Donut9a donut, int index, IReadOnlyList<DonutBerryChoice> choices, Action changed) : ViewModelBase
{
    public string Name => $"Fruta {index + 1}";
    public IReadOnlyList<DonutBerryChoice> Choices => choices;
    public DonutBerryChoice? Selected
    {
        get => choices.FirstOrDefault(c => c.Id == System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(donut.Data.Slice(0x10 + 2 * index, 2)));
        set { if (value is null) return; System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(donut.Data.Slice(0x10 + 2 * index, 2), value.Id); Raise(); changed(); }
    }
}
