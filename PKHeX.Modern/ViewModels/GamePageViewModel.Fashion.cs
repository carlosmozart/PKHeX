using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;

namespace PKHeX.Modern.ViewModels;

public sealed partial class GamePageViewModel
{
    public bool HasFashion => _sav is SAV9SV;
    public bool IsFashionTab => Tab == 10;
    public FashionEditorViewModel? Fashion { get; private set; }
    private void RefreshFashion()
    {
        var save = _sav;
        Fashion = save is SAV9SV sv ? new(sv, Edit, _confirm, _status, () => ReferenceEquals(_sav, save)) : null;
        Raise(nameof(Fashion)); Raise(nameof(HasFashion));
    }
}

public sealed record FashionCategory(string Name, string English, uint Key);

public sealed class FashionEditorViewModel : ViewModelBase
{
    private readonly SAV9SV _sav;
    private readonly Action<string, Action> _edit;
    private readonly Func<string, string, string, Task<bool>> _confirm;
    private readonly Action<string> _status;
    private readonly Func<bool> _active;
    private readonly List<FashionRowViewModel> _all = [];
    public const string Explanation = "As peças são identificadas pela categoria em inglês e pelo ID do jogo: o PKHeX não fornece seus nomes comerciais. O catálogo de liberação é o do PKHeX, conforme o gênero do treinador; peças já presentes também aparecem. Liberar todas pede confirmação e tem um passo de desfazer. Salvar grava o arquivo.";
    public string Note => Explanation;
    public IReadOnlyList<FashionCategory> Categories { get; } =
    [
        new("Olhos", "Eyewear", SaveBlockAccessor9SV.KFashionUnlockedEyewear),
        new("Luvas", "Gloves", SaveBlockAccessor9SV.KFashionUnlockedGloves),
        new("Mochilas", "Bag", SaveBlockAccessor9SV.KFashionUnlockedBag),
        new("Sapatos", "Footwear", SaveBlockAccessor9SV.KFashionUnlockedFootwear),
        new("Cabeça", "Headwear", SaveBlockAccessor9SV.KFashionUnlockedHeadwear),
        new("Meias", "Legwear", SaveBlockAccessor9SV.KFashionUnlockedLegwear),
        new("Roupas", "Clothing", SaveBlockAccessor9SV.KFashionUnlockedClothing),
        new("Capas de celular", "Phone case", SaveBlockAccessor9SV.KFashionUnlockedPhoneCase),
    ];
    private int _category;
    public int CategoryIndex { get => _category; set { if (value >= 0 && value < Categories.Count && Set(ref _category, value)) Filter(); } }
    private string _query = "";
    public string Query { get => _query; set { if (Set(ref _query, value ?? "")) Filter(); } }
    public IReadOnlyList<string> CategoryNames => Categories.Select(c => c.Name).ToArray();
    public IReadOnlyList<FashionRowViewModel> Rows { get; private set; } = [];
    public string Count => $"{Rows.Count} peças · {Rows.Count(r => r.Unlocked)} liberadas";
    public FashionEditorViewModel(SAV9SV sav, Action<string, Action> edit, Func<string, string, string, Task<bool>> confirm, Action<string> status, Func<bool> active)
    {
        _sav = sav; _edit = edit; _confirm = confirm; _status = status; _active = active;
        // Ask the Core for its public unlock catalogue using an isolated empty inventory.
        var catalog = (SAV9SV)sav.Clone();
        foreach (var cat in Categories)
        {
            var items = FashionItem9.GetArray(catalog.Blocks.GetBlock(cat.Key).Data);
            foreach (var item in items) item.Clear();
            FashionItem9.SetArray(items, catalog.Blocks.GetBlock(cat.Key).Data);
        }
        PlayerFashionUnlock9.UnlockBase(catalog.Blocks, sav.Gender);
        foreach (var cat in Categories)
        {
            var block = sav.Blocks.GetBlock(cat.Key);
            var ids = FashionItem9.GetArray(catalog.Blocks.GetBlock(cat.Key).Data).Concat(FashionItem9.GetArray(block.Data)).Where(i => i.Value is > 0 and < FashionItem9.None).Select(i => i.Value).Distinct().Order();
            foreach (uint id in ids) _all.Add(new(cat, id, block, Change));
        }
        Filter();
    }
    private void Filter()
    {
        var key = Categories[CategoryIndex].Key;
        Rows = _all.Where(r => r.Category.Key == key && r.Name.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        Raise(nameof(Rows)); Raise(nameof(Count));
    }
    private void Change(FashionRowViewModel row, bool unlocked)
    {
        if (!_active()) return;
        _edit($"{(unlocked ? "Liberar" : "Bloquear")} {row.Name}", () => SetOwned(row, unlocked));
        foreach (var item in _all) item.Refresh(); Raise(nameof(Count));
    }
    private void SetOwned(FashionRowViewModel row, bool state)
    {
        if (state)
        {
            int added = PlayerFashionUnlock9.Add(row.Block.Data, [(ushort)row.Id]);
            if (added == 0 && !row.Unlocked) _status("A categoria de roupas está cheia.");
        }
        else
        {
            var items = FashionItem9.GetArray(row.Block.Data); int write = 0;
            foreach (var item in items) if (item.Value != row.Id && item.Value != FashionItem9.None) items[write++] = item;
            while (write < items.Length) items[write++] = new FashionItem9 { Value = FashionItem9.None };
            FashionItem9.SetArray(items, row.Block.Data);
        }
    }
    public RelayCommand UnlockCategoryCommand => new(() => _ = UnlockAsync(false));
    public RelayCommand UnlockAllCommand => new(() => _ = UnlockAsync(true));
    public async Task UnlockAsync(bool all)
    {
        var cat = Categories[CategoryIndex];
        var rows = _all.Where(r => all || r.Category.Key == cat.Key).ToArray();
        string title = all ? "Liberar todas as roupas" : "Liberar roupas da categoria";
        string message = all ? "Liberar todas as peças do catálogo do PKHeX? A operação pode ser desfeita." : $"Liberar todas as peças de {cat.Name}? A busca não limita a operação. A operação pode ser desfeita.";
        if (!await _confirm(title, message, "Liberar") || !_active()) return;
        _edit(title, () => { foreach (var row in rows) SetOwned(row, true); }); foreach (var row in _all) row.Refresh(); Raise(nameof(Count));
    }
}

public sealed class FashionRowViewModel(FashionCategory category, uint id, SCBlock block, Action<FashionRowViewModel, bool> change) : ViewModelBase
{
    public FashionCategory Category => category;
    public SCBlock Block => block;
    public uint Id => id;
    public string Name => $"{category.English} #{id}";
    public bool Unlocked
    {
        get { for (int offset = 0; offset + FashionItem9.SIZE <= block.Data.Length; offset += FashionItem9.SIZE) if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(block.Data[offset..]) == id) return true; return false; }
        set { if (value != Unlocked) change(this, value); }
    }
    public void Refresh() => Raise(nameof(Unlocked));
}
