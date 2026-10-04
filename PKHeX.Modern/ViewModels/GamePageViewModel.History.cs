using System;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class GamePageViewModel
{
    public GameHistory? History { get; private set; }
    public Action? HistoryChanged { get; set; }
    public void Edit(string description, Action action)
    {
        if (History?.Execute(description, action) != true) return;
        Changed?.Invoke(); HistoryChanged?.Invoke();
    }
    public string? UndoGame() => RestoreGame(false);
    public string? RedoGame() => RestoreGame(true);
    private string? RestoreGame(bool redo)
    {
        var description = redo ? History?.Redo() : History?.Undo();
        if (description is null || _sav is null) return null;
        var tab = Tab; var query = Query; var category = CategoryIndex; var unnamed = ShowUnnamed; var only = OnlySet;
        Load(_sav); Tab = tab; Query = query; CategoryIndex = category; ShowUnnamed = unnamed; OnlySet = only;
        AfterShortcut?.Invoke(); Changed?.Invoke(); HistoryChanged?.Invoke(); return description;
    }
}
