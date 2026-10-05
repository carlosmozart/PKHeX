using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>
/// Pagina "Bank": duas telas lado a lado — o bank local (esquerda) e a caixa/equipe do save aberto (direita).
/// Os Pokemon passam de um lado para o outro arrastando; a conversao de geracao e feita ao entrar no save.
/// </summary>
public sealed class BankPageViewModel : SlotPageViewModel
{
    private BankDetailsViewModel? _details;
    public BankDetailsViewModel? Details { get => _details; set { if (Set(ref _details, value)) Raise(nameof(HasDetails)); } }
    public bool HasDetails => Details is not null;
    public Func<Task>? ExportDetails { get; set; }
    public Func<string, Task>? CopyDetails { get; set; }
    private readonly Func<string, string, string, Task<string?>> _prompt;
    private readonly Func<string, string, string, Task<bool>> _confirm;
    private readonly Action<string> _status;

    /// <param name="select">Selecionar um slot (o mesmo fluxo das caixas).</param>
    /// <param name="prompt">Pergunta com campo de texto (titulo, mensagem, valor inicial) → texto ou null.</param>
    /// <param name="confirm">Confirmacao de acao destrutiva (titulo, mensagem, botao) → true/false.</param>
    public BankPageViewModel(Action<SlotViewModel> select, Func<string, string, string, Task<string?>> prompt,
        Func<string, string, string, Task<bool>> confirm, Action<string> status, AppSettings? settings = null, OtherSaveViewModel? other = null) : base(select)
    {
        _prompt = prompt;
        _confirm = confirm;
        _status = status;
        _settings = settings ?? new AppSettings();
        Other = other;
        ShowBankCommand = new RelayCommand(() => ShowOther = false);
        ShowOtherCommand = new RelayCommand(() => { ShowOther = true; Other?.RefreshOptions(); });
        AddFolderCommand = new RelayCommand(() => _ = AddFolderAsync());
        RemoveFolderCommand = new RelayCommand(() => _ = RemoveFolderAsync(), () => IsExternal);
        PreviousBoxCommand = new RelayCommand(() => BoxIndex--, () => BoxIndex > 0);
        NextBoxCommand = new RelayCommand(() => BoxIndex++, () => BoxIndex < Boxes.Count - 1);
        NewBankCommand = new RelayCommand(() => _ = NewBankAsync());
        RenameBankCommand = new RelayCommand(() => _ = RenameBankAsync(), () => !IsExternal);
        DeleteBankCommand = new RelayCommand(() => _ = DeleteBankAsync(), () => !IsExternal && Banks.Count(b => !BankStorage.IsExternalBank(b)) > 1);
        NewBoxCommand = new RelayCommand(() => _ = NewBoxAsync(), () => !IsExternal);
        RenameBoxCommand = new RelayCommand(() => _ = RenameBoxAsync(), () => !IsExternal);
        DeleteBoxCommand = new RelayCommand(() => _ = DeleteBoxAsync(), () => !IsExternal && Boxes.Count > 1);
        OpenFolderCommand = new RelayCommand(() =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(CurrentBox?.Folder ?? BankStorage.Root) { UseShellExecute = true }); }
            catch { /* sem explorador */ }
        });
        SortOptions = [.. CoreAdapter.GetBoxSortOptions(null).Select(o => new SortOptionViewModel(o.Name, new RelayCommand(() => SortBox(o))))];
    }

    /// <summary>Criterios do menu "Ordenar" (so a caixa atual do bank).</summary>
    public IReadOnlyList<SortOptionViewModel> SortOptions { get; }
    /// <summary>Chamado depois de ordenar (a selecao multipla aponta para posicoes que mudaram).</summary>
    public Action? Sorted { get; set; }

    private void SortBox(CoreAdapter.BoxSortOption option)
    {
        if (CurrentBox is not { } box)
            return;
        try
        {
            var count = BankStorage.SortBox(box, option);
            _status($"Caixa \"{box.Name}\" do bank ordenada: {option.Name} ({count} Pokémon).");
        }
        catch (Exception ex)
        {
            _status($"Não deu para ordenar: {ex.Message}");
        }
        Sorted?.Invoke();
        LoadBox();
    }

    private readonly AppSettings _settings;

    /// <summary>Painel "Outro save": um segundo save no lugar do bank, para mover Pokemon entre dois saves.</summary>
    public OtherSaveViewModel? Other { get; }
    private bool _showOther;
    /// <summary>O painel da esquerda mostra o outro save (true) ou o bank (false).</summary>
    public bool ShowOther { get => _showOther; set { if (Set(ref _showOther, value)) Raise(nameof(ShowBank)); } }
    public bool ShowBank => !ShowOther;
    public RelayCommand ShowBankCommand { get; }
    public RelayCommand ShowOtherCommand { get; }

    // Pastas externas
    /// <summary>Escolher uma pasta (a janela liga o seletor de pastas).</summary>
    public Func<Task<string?>>? PickFolder { get; set; }
    /// <summary>Pasta externa saiu da lista (Android: apaga a copia privada dela).</summary>
    public Action<string>? FolderRemoved { get; set; }
    /// <summary>Nome mostrado da pasta (Android: o nome da pasta original, nao o caminho da copia privada).</summary>
    public Func<string, string> DescribeFolder { get; set; } = f => f;
    public RelayCommand AddFolderCommand { get; }
    public RelayCommand RemoveFolderCommand { get; }
    /// <summary>O banco escolhido e uma pasta externa de arquivos .pk*.</summary>
    public bool IsExternal => _bank is not null && BankStorage.IsExternalBank(_bank);
    public bool IsLocal => !IsExternal;

    private async Task AddFolderAsync()
    {
        if (PickFolder is null || await PickFolder() is not { } folder)
            return;
        folder = System.IO.Path.GetFullPath(folder);
        if (folder.StartsWith(System.IO.Path.GetFullPath(BankStorage.Root), StringComparison.OrdinalIgnoreCase))
        {
            _status("Essa pasta já faz parte do bank do app.");
            return;
        }
        if (!_settings.ExternalBankFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            _settings.ExternalBankFolders.Add(folder);
            _settings.Save();
        }
        _bank = BankStorage.GetExternalBankName(folder);
        _boxIndex = 0;
        Reload();
        _status($"Pasta {DescribeFolder(folder)} adicionada como banco. Os arquivos .pk* dela aparecem em caixas de 30; o que você soltar aqui vira um arquivo novo na pasta.");
    }

    private async Task RemoveFolderAsync()
    {
        if (_bank is null || BankStorage.GetExternalPath(_bank) is not { } folder)
            return;
        if (!await _confirm("Remover pasta da lista?", $"A pasta {DescribeFolder(folder)} sai da lista de bancos. Os arquivos continuam lá, nada é apagado.", "Remover"))
            return;
        _settings.ExternalBankFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        FolderRemoved?.Invoke(folder);
        _bank = null;
        Reload();
    }

    public override string Title => "Bank";
    public override string Icon => "🏦";

    public RelayCommand PreviousBoxCommand { get; }
    public RelayCommand NextBoxCommand { get; }
    public RelayCommand NewBankCommand { get; }
    public RelayCommand RenameBankCommand { get; }
    public RelayCommand DeleteBankCommand { get; }
    public RelayCommand NewBoxCommand { get; }
    public RelayCommand RenameBoxCommand { get; }
    public RelayCommand DeleteBoxCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    public ObservableCollection<string> Banks { get; } = [];
    public IReadOnlyList<BankBox> Boxes { get; private set; } = [];
    /// <summary>Nomes na ComboBox. Guardado (nao recriado a cada leitura), senao a ComboBox perde a selecao.</summary>
    public IReadOnlyList<string> BoxNames { get; private set; } = [];

    private string? _bank;
    private bool _reselectBank;
    public string? SelectedBank
    {
        get => _reselectBank ? null : _bank;
        set
        {
            if (value is null || !Set(ref _bank, value))
                return;
            _boxIndex = 0;
            RaiseBankKind();
            ReloadBoxes();
        }
    }

    private int _boxIndex;
    private bool _reselect;
    public int BoxIndex
    {
        get => _reselect ? -1 : _boxIndex;
        set
        {
            if (value < 0 || value >= Boxes.Count || !Set(ref _boxIndex, value))
                return;
            LoadBox();
        }
    }

    public BankBox? CurrentBox => (uint)_boxIndex < (uint)Boxes.Count ? Boxes[_boxIndex] : null;
    public string Summary { get; private set; } = "";

    /// <summary>O bank nao depende do save; so recarrega (o save aberto aparece na tela da direita).</summary>
    public override void Load(SaveFile sav) => Reload();

    /// <summary>Rele bancos, caixas e a caixa atual do disco.</summary>
    public void Reload()
    {
        BankStorage.ExternalFolders = _settings.ExternalBankFolders;
        List<string> banks = [.. BankStorage.GetBanks(), .. _settings.ExternalBankFolders.Select(BankStorage.GetExternalBankName)];
        Banks.Clear();
        foreach (var b in banks)
            Banks.Add(b);
        if (_bank is null || !banks.Contains(_bank))
            _bank = banks[0];
        Raise(nameof(SelectedBank));
        // A lista foi refeita e a ComboBox perde a selecao: passa por "nenhum" e volta (igual a lista de caixas).
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _reselectBank = true;
            Raise(nameof(SelectedBank));
            _reselectBank = false;
            Raise(nameof(SelectedBank));
        }, Avalonia.Threading.DispatcherPriority.Background);
        RaiseBankKind();
        ReloadBoxes();
    }

    private void RaiseBankKind()
    {
        Raise(nameof(IsExternal));
        Raise(nameof(IsLocal));
        foreach (var c in (RelayCommand[])[RenameBankCommand, DeleteBankCommand, NewBoxCommand, RenameBoxCommand, DeleteBoxCommand, RemoveFolderCommand])
            c.NotifyCanExecuteChanged();
    }

    private void ReloadBoxes()
    {
        if (_bank is null)
            return;
        Boxes = BankStorage.GetBoxes(_bank);
        BoxNames = [.. Boxes.Select((b, i) => $"{i + 1}. {b.Name}")];
        if (_boxIndex >= Boxes.Count)
            _boxIndex = Boxes.Count - 1;
        Raise(nameof(Boxes));
        Raise(nameof(BoxNames));
        Raise(nameof(BoxIndex));
        // A ComboBox aplica a lista nova depois e zera a selecao. Reafirmar o mesmo valor nao basta (a ligacao acha que
        // nada mudou), entao passa por "sem selecao" e volta para a caixa atual.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _reselect = true;
            Raise(nameof(BoxIndex));
            _reselect = false;
            Raise(nameof(BoxIndex));
        }, Avalonia.Threading.DispatcherPriority.Background);
        DeleteBoxCommand.NotifyCanExecuteChanged();
        LoadBox();
    }

    /// <summary>Recarrega so a caixa atual (depois de mover Pokemon).</summary>
    private bool _attachMode;
    /// <summary>
    /// "Anexar ao trazer": levar um Pokemon do bank para um save copia em vez de mover; o original fica no bank,
    /// ligado ao save, e "Atualizar anexados" traz de volta a versao do jogo.
    /// </summary>
    public bool AttachMode { get => _attachMode; set => Set(ref _attachMode, value); }

    /// <summary>Variantes do anexado selecionado (a versao de cada jogo de outra geracao).</summary>
    public ObservableCollection<BankVariantViewModel> Variants { get; } = [];
    public bool HasVariants => Variants.Count > 0;
    private string _variantsTitle = "";
    public string VariantsTitle { get => _variantsTitle; private set => Set(ref _variantsTitle, value); }
    private PKM? _variantOwner;

    /// <summary>"Abrir no editor do save" de uma variante (definido pelo MainViewModel): original do bank e a variante.</summary>
    public Func<PKM, BankVariant, Task>? UseVariant { get; set; }

    /// <summary>Mostra as variantes do Pokemon selecionado no bank (some se ele nao for anexado ou nao tiver variantes).</summary>
    public void ShowVariants(SlotViewModel? slot)
    {
        Variants.Clear();
        _variantOwner = null;
        if (slot is { IsBank: true, IsAttached: true, Pkm: { } pk } && !CoreAdapter.IsEmpty(pk))
        {
            _variantOwner = pk;
            foreach (var v in BankLinks.GetVariants(pk))
                Variants.Add(new BankVariantViewModel(v,
                    new RelayCommand(() => { if (UseVariant is { } use) _ = use(pk, v); }),
                    new RelayCommand(() => _ = DeleteVariantAsync(pk, v))));
            VariantsTitle = $"VARIANTES DE {(pk.IsEgg ? "OVO" : CoreAdapter.SpeciesNames[pk.Species].ToUpperInvariant())} · ORIGINAL {pk.Extension.ToUpperInvariant()}";
        }
        Raise(nameof(HasVariants));
    }

    private async Task DeleteVariantAsync(PKM original, BankVariant variant)
    {
        if (!await _confirm("Excluir variante?",
                $"A versão {variant.Pk.Extension.ToUpperInvariant()} de {CoreAdapter.SpeciesNames[variant.Pk.Species]} (Nv. {variant.Pk.CurrentLevel}) será apagada. O original do bank não muda.",
                "Excluir"))
            return;
        try
        {
            BankLinks.DeleteVariant(original, variant);
            _status($"Variante {variant.Pk.Extension.ToUpperInvariant()} excluída.");
        }
        catch (Exception ex)
        {
            _status($"Não deu para excluir a variante: {ex.Message}");
        }
        LoadBox();
        var slot = Slots.FirstOrDefault(s => s.Pkm is { } p && !CoreAdapter.IsEmpty(p) && BankLinks.IdOf(p) == BankLinks.IdOf(original));
        ShowVariants(slot);
    }
    public string AttachedText => BankLinks.All.Count == 0 ? "🔗  Atualizar anexados" : $"🔗  Atualizar anexados ({BankLinks.All.Count})";

    public void LoadBox()
    {
        Details = null;
        Slots.Clear();
        if (Variants.Count > 0)
            ShowVariants(null); // a selecao some junto com os slots
        if (CurrentBox is not { } box || _bank is null)
            return;
        var data = BankStorage.ReadBox(box);
        for (int i = 0; i < BankStorage.SlotsPerBox; i++)
        {
            var s = SlotViewModel.ForBank(box, _bank, i);
            s.LoadEntity(data[i]);
            s.IsAttached = !box.IsExternal && BankLinks.IsAttached(data[i]);
            if (s.IsAttached && BankLinks.Find(data[i]!) is { } link)
                s.AttachInfo = $"Anexado a {link.SaveName}"
                    + (link.LastSync is { } t ? $" · atualizado em {t:dd/MM/yyyy HH:mm}" : " · ainda não atualizado")
                    + (link.Variants.Count > 0 ? $" · variantes: {string.Join(", ", link.Variants.Select(v => v.ToUpperInvariant()))}" : "")
                    + ". Atualizar anexados traz a versão do jogo.";
            Slots.Add(s);
        }
        Summary = IsExternal
            ? $"{BankStorage.CountBank(_bank)} arquivo(s) · {box.Folder}"
            : $"{BankStorage.CountBank(_bank)} Pokémon no banco · caixa {_boxIndex + 1} de {Boxes.Count}";
        Raise(nameof(Summary));
        Raise(nameof(CurrentBox));
        Raise(nameof(AttachedText));
        PreviousBoxCommand.NotifyCanExecuteChanged();
        NextBoxCommand.NotifyCanExecuteChanged();
        SlotsLoaded?.Invoke();
    }

    private async Task NewBankAsync()
    {
        if (await _prompt("Novo banco", "Nome do banco (ex.: Shinies, Lendários, Gen 3):", "") is not { } name)
            return;
        if (BankStorage.CreateBank(name) is { } error) { _status(error); return; }
        _bank = name.Trim();
        Reload();
        _status($"Banco \"{name.Trim()}\" criado.");
    }

    private async Task RenameBankAsync()
    {
        if (_bank is null || await _prompt("Renomear banco", "Novo nome do banco:", _bank) is not { } name || name.Trim() == _bank)
            return;
        if (BankStorage.RenameBank(_bank, name) is { } error) { _status(error); return; }
        _bank = name.Trim();
        Reload();
    }

    private async Task DeleteBankAsync()
    {
        if (_bank is null || Banks.Count <= 1)
            return;
        var count = BankStorage.CountBank(_bank);
        if (!await _confirm("Excluir banco?", $"O banco \"{_bank}\" e todas as caixas dele serão apagados ({count} Pokémon). Isso não pode ser desfeito.", "Excluir banco"))
            return;
        BankStorage.DeleteBank(_bank);
        _bank = null;
        Reload();
    }

    private async Task NewBoxAsync()
    {
        if (_bank is null || await _prompt("Nova caixa", "Nome da caixa:", $"Caixa {Boxes.Count + 1}") is not { } name)
            return;
        if (BankStorage.CreateBox(_bank, name) is { } error) { _status(error); return; }
        _boxIndex = Boxes.Count; // a nova fica no fim
        ReloadBoxes();
    }

    private async Task RenameBoxAsync()
    {
        if (CurrentBox is not { } box || await _prompt("Renomear caixa", "Novo nome da caixa:", box.Name) is not { } name)
            return;
        if (BankStorage.RenameBox(box, name) is { } error) { _status(error); return; }
        ReloadBoxes();
    }

    private async Task DeleteBoxAsync()
    {
        if (CurrentBox is not { } box || Boxes.Count <= 1)
            return;
        var count = Slots.Count(s => !s.IsEmpty);
        if (count > 0 && !await _confirm("Excluir caixa?", $"A caixa \"{box.Name}\" tem {count} Pokémon, que serão apagados. Isso não pode ser desfeito.", "Excluir caixa"))
            return;
        BankStorage.DeleteBox(box);
        ReloadBoxes();
    }
}

/// <summary>Uma variante no painel do bank: a versao do Pokemon que voltou de um jogo de outra geracao.</summary>
public sealed class BankVariantViewModel(BankVariant variant, RelayCommand use, RelayCommand delete)
{
    public BankVariant Variant { get; } = variant;
    public Avalonia.Media.Imaging.Bitmap? Sprite { get; } = SpriteService.GetSprite(variant.Pk);
    public string Format { get; } = variant.Pk.Extension.ToUpperInvariant();
    public string Title { get; } = $"{(variant.Pk.IsEgg ? "Ovo" : CoreAdapter.SpeciesNames[variant.Pk.Species])} · Nv. {variant.Pk.CurrentLevel}";
    public string Detail { get; } = $"Formato da Gen {variant.Pk.Format} · origem {GameInfo.GetVersionName(variant.Pk.Version)} · guardada em {variant.Saved:dd/MM/yyyy HH:mm}";
    public RelayCommand UseCommand { get; } = use;
    public RelayCommand DeleteCommand { get; } = delete;
}
