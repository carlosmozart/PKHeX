using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>Um save que pode ser aberto no painel "Outro save" da pagina Bank.</summary>
public sealed record OtherSaveOption(string Path, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Segundo save, aberto no painel esquerdo da pagina Bank (no lugar do bank) para mover Pokemon entre dois saves.
/// E lido direto pelo Core, sem trocar o save principal nem o estado global do app. As alteracoes ficam na memoria
/// ate clicar em "Salvar este save" (com backup do arquivo anterior); nao entram no Ctrl+Z.
/// </summary>
public sealed class OtherSaveViewModel : SlotPageViewModel
{
    private readonly Func<IEnumerable<OtherSaveOption>> _options;
    private readonly Func<string, string, string, Task<bool>> _confirm;
    private readonly Action<string> _status;

    /// <param name="options">Saves oferecidos na lista (os da pasta do Save Manager, menos o aberto).</param>
    public OtherSaveViewModel(Action<SlotViewModel> select, Func<IEnumerable<OtherSaveOption>> options,
        Func<string, string, string, Task<bool>> confirm, Action<string> status) : base(select)
    {
        _options = options;
        _confirm = confirm;
        _status = status;
        PreviousBoxCommand = new RelayCommand(() => BoxIndex--, () => BoxIndex > 0);
        NextBoxCommand = new RelayCommand(() => BoxIndex++, () => Sav is not null && BoxIndex < Sav.BoxCount - 1);
        SaveCommand = new RelayCommand(() => _ = SaveAsync(), () => IsDirty);
        CloseCommand = new RelayCommand(() => _ = CloseAsync(), () => HasSave);
        UndoCommand = new RelayCommand(Undo, () => History?.CanUndo == true);
        RedoCommand = new RelayCommand(Redo, () => History?.CanRedo == true);
    }

    /// <summary>Desfazer/refazer proprio deste save (o Ctrl+Z da janela vale para o save aberto).</summary>
    public SlotHistory? History { get; private set; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public string UndoTip => History?.UndoDescription is { } d ? $"Desfazer no outro save: {d}" : "Nada para desfazer neste save";
    public string RedoTip => History?.RedoDescription is { } d ? $"Refazer no outro save: {d}" : "Nada para refazer neste save";

    public void OnHistoryChanged()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        Raise(nameof(UndoTip));
        Raise(nameof(RedoTip));
    }

    private void Undo()
    {
        if (History?.Undo() is not { } what)
            return;
        MarkDirty();
        LoadBox();
        OnHistoryChanged();
        _status($"Outro save: desfeito ({what}).");
    }

    private void Redo()
    {
        if (History?.Redo() is not { } what)
            return;
        MarkDirty();
        LoadBox();
        OnHistoryChanged();
        _status($"Outro save: refeito ({what}).");
    }

    public override string Title => "Outro save";
    public override string Icon => "💾";
    public override void Load(SaveFile sav) => RefreshOptions();

    public RelayCommand PreviousBoxCommand { get; }
    public RelayCommand NextBoxCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CloseCommand { get; }
    /// <summary>Escolher um arquivo fora da pasta do Save Manager (a janela liga o seletor de arquivos).</summary>
    public Func<Task<string?>>? PickFile { get; set; }
    public RelayCommand BrowseCommand => new(() => _ = BrowseAsync());

    public SaveFile? Sav { get; private set; }
    public string? FilePath { get; private set; }
    public bool HasSave => Sav is not null;
    public bool HasNoSave => Sav is null;
    public string GameName => Sav is null ? "" : $"{CoreAdapter.GetGameName(Sav)} · {Sav.OT}";
    public string FileName => FilePath is null ? "" : ZipSaves.DisplayName(FilePath);

    public IReadOnlyList<OtherSaveOption> Options { get; private set; } = [];
    private OtherSaveOption? _selectedOption;
    /// <summary>Escolher na lista abre o save (pergunta antes de descartar alteracoes do anterior).</summary>
    public OtherSaveOption? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (value is null || value == _selectedOption)
                return;
            _selectedOption = value;
            Raise();
            _ = OpenAsync(value.Path, MainPath);
        }
    }

    public void RefreshOptions()
    {
        Options = [.. _options()];
        Raise(nameof(Options));
    }

    private bool _isDirty;
    /// <summary>Ha alteracoes neste save que ainda nao foram gravadas no arquivo.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (Set(ref _isDirty, value))
                SaveCommand.NotifyCanExecuteChanged();
        }
    }
    public void MarkDirty() => IsDirty = true;

    private int _boxIndex;
    public int BoxIndex
    {
        get => _boxIndex;
        set
        {
            if (Sav is null || value < 0 || value >= Sav.BoxCount || !Set(ref _boxIndex, value))
                return;
            LoadBox();
        }
    }
    public string BoxName => Sav is null ? "" : CoreAdapter.GetBoxName(Sav, BoxIndex);
    public string BoxLabel => Sav is null ? "" : $"Caixa {BoxIndex + 1} de {Sav.BoxCount} · {Slots.Count(s => !s.IsEmpty)}/{Slots.Count} Pokémon";

    /// <summary>Abre um save neste painel. Nao aceita o mesmo arquivo do save principal.</summary>
    public async Task OpenAsync(string path, string? mainPath = null)
    {
        if (IsDirty && !await _confirm("Descartar alterações?", $"O save {FileName} tem alterações que ainda não foram gravadas.", "Descartar"))
        {
            _selectedOption = Options.FirstOrDefault(o => o.Path == FilePath);
            Raise(nameof(SelectedOption));
            return;
        }
        if (mainPath is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(mainPath), StringComparison.OrdinalIgnoreCase))
        {
            _status("Esse save já está aberto no painel da direita. Escolha outro.");
            return;
        }
        var sav = await Task.Run(() => PokedexService.TryRead(path));
        if (sav is null)
        {
            _status($"{Path.GetFileName(path)} não foi reconhecido como save.");
            return;
        }
        Sav = sav;
        FilePath = path;
        IsDirty = false;
        History = new SlotHistory(sav);
        OnHistoryChanged();
        _boxIndex = 0;
        _selectedOption = Options.FirstOrDefault(o => string.Equals(o.Path, path, StringComparison.OrdinalIgnoreCase));
        foreach (var p in (string[])[nameof(HasSave), nameof(HasNoSave), nameof(GameName), nameof(FileName), nameof(BoxIndex), nameof(SelectedOption)])
            Raise(p);
        CloseCommand.NotifyCanExecuteChanged();
        LoadBox();
        _status($"Outro save aberto: {GameName}. Arraste Pokémon entre os dois saves; depois salve cada um.");
    }

    /// <summary>Opcao de abrir pelo seletor (usada pelo botao "Abrir arquivo...").</summary>
    public string? MainPath { get; set; }

    private async Task BrowseAsync()
    {
        if (PickFile is not null && await PickFile() is { } path)
            await OpenAsync(path, MainPath);
    }

    public void LoadBox()
    {
        Slots.Clear();
        if (Sav is null)
            return;
        for (int i = 0; i < Sav.BoxSlotCount; i++)
        {
            var s = SlotViewModel.ForOther(Sav, BoxIndex, i);
            s.Load(Sav);
            Slots.Add(s);
        }
        Raise(nameof(BoxName));
        Raise(nameof(BoxLabel));
        PreviousBoxCommand.NotifyCanExecuteChanged();
        NextBoxCommand.NotifyCanExecuteChanged();
        SlotsLoaded?.Invoke();
    }

    /// <summary>Grava no arquivo (o arquivo anterior vai para os backups).</summary>
    public async Task SaveAsync()
    {
        if (Sav is null || FilePath is null)
            return;
        try
        {
            var backup = SaveBackup.BeforeOverwrite(ZipSaves.FileOf(FilePath), "Salvar", CoreAdapter.GetVersionName(Sav.Version));
            var sav = Sav;
            var path = FilePath;
            await Task.Run(() => CoreAdapter.ExportSave(sav, path));
            IsDirty = false;
            _status($"{FileName} salvo." + (backup is not null ? $" Backup do arquivo anterior: {Path.GetFileName(backup)}." : ""));
        }
        catch (Exception ex)
        {
            _status($"Erro ao salvar {FileName}: {ex.Message}");
        }
    }

    private async Task CloseAsync()
    {
        if (IsDirty && !await _confirm("Fechar sem salvar?", $"O save {FileName} tem alterações que ainda não foram gravadas.", "Fechar sem salvar"))
            return;
        Sav = null;
        FilePath = null;
        IsDirty = false;
        History = null;
        OnHistoryChanged();
        _selectedOption = null;
        Slots.Clear();
        foreach (var p in (string[])[nameof(HasSave), nameof(HasNoSave), nameof(GameName), nameof(FileName), nameof(SelectedOption), nameof(BoxName), nameof(BoxLabel)])
            Raise(p);
        CloseCommand.NotifyCanExecuteChanged();
    }
}
