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
    private readonly Func<string, string, string, Task<string?>> _prompt;
    private readonly Func<string, string, string, Task<bool>> _confirm;
    private readonly Action<string> _status;

    /// <param name="select">Selecionar um slot (o mesmo fluxo das caixas).</param>
    /// <param name="prompt">Pergunta com campo de texto (titulo, mensagem, valor inicial) → texto ou null.</param>
    /// <param name="confirm">Confirmacao de acao destrutiva (titulo, mensagem, botao) → true/false.</param>
    public BankPageViewModel(Action<SlotViewModel> select, Func<string, string, string, Task<string?>> prompt,
        Func<string, string, string, Task<bool>> confirm, Action<string> status) : base(select)
    {
        _prompt = prompt;
        _confirm = confirm;
        _status = status;
        PreviousBoxCommand = new RelayCommand(() => BoxIndex--, () => BoxIndex > 0);
        NextBoxCommand = new RelayCommand(() => BoxIndex++, () => BoxIndex < Boxes.Count - 1);
        NewBankCommand = new RelayCommand(() => _ = NewBankAsync());
        RenameBankCommand = new RelayCommand(() => _ = RenameBankAsync());
        DeleteBankCommand = new RelayCommand(() => _ = DeleteBankAsync(), () => Banks.Count > 1);
        NewBoxCommand = new RelayCommand(() => _ = NewBoxAsync());
        RenameBoxCommand = new RelayCommand(() => _ = RenameBoxAsync());
        DeleteBoxCommand = new RelayCommand(() => _ = DeleteBoxAsync(), () => Boxes.Count > 1);
        OpenFolderCommand = new RelayCommand(() =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(CurrentBox?.Folder ?? BankStorage.Root) { UseShellExecute = true }); }
            catch { /* sem explorador */ }
        });
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
    public IReadOnlyList<string> BoxNames => [.. Boxes.Select((b, i) => $"{i + 1}. {b.Name}")];

    private string? _bank;
    public string? SelectedBank
    {
        get => _bank;
        set
        {
            if (value is null || !Set(ref _bank, value))
                return;
            _boxIndex = 0;
            ReloadBoxes();
        }
    }

    private int _boxIndex;
    public int BoxIndex
    {
        get => _boxIndex;
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
        var banks = BankStorage.GetBanks();
        Banks.Clear();
        foreach (var b in banks)
            Banks.Add(b);
        if (_bank is null || !banks.Contains(_bank))
            _bank = banks[0];
        Raise(nameof(SelectedBank));
        DeleteBankCommand.NotifyCanExecuteChanged();
        ReloadBoxes();
    }

    private void ReloadBoxes()
    {
        if (_bank is null)
            return;
        Boxes = BankStorage.GetBoxes(_bank);
        if (_boxIndex >= Boxes.Count)
            _boxIndex = Boxes.Count - 1;
        Raise(nameof(Boxes));
        Raise(nameof(BoxNames));
        Raise(nameof(BoxIndex));
        DeleteBoxCommand.NotifyCanExecuteChanged();
        LoadBox();
    }

    /// <summary>Recarrega so a caixa atual (depois de mover Pokemon).</summary>
    public void LoadBox()
    {
        Slots.Clear();
        if (CurrentBox is not { } box || _bank is null)
            return;
        var data = BankStorage.ReadBox(box);
        for (int i = 0; i < BankStorage.SlotsPerBox; i++)
        {
            var s = SlotViewModel.ForBank(box, _bank, i);
            s.LoadEntity(data[i]);
            Slots.Add(s);
        }
        Summary = $"{BankStorage.CountBank(_bank)} Pokémon no banco · caixa {_boxIndex + 1} de {Boxes.Count}";
        Raise(nameof(Summary));
        Raise(nameof(CurrentBox));
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
