using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>
/// Um save aberto numa aba. Guarda o estado que pertence ao save (o arquivo na memoria, desfazer/refazer,
/// alteracoes nao exportadas, caixa e pagina) para que trocar de aba nao perca nada. So uma aba fica ativa:
/// o <see cref="MainViewModel"/> carrega o estado dela nas paginas e devolve ao trocar.
/// </summary>
public sealed class SaveTabViewModel : ViewModelBase
{
    public SaveTabViewModel(SaveFile sav, string path)
    {
        Sav = sav;
        Path = path;
        History = new SlotHistory(sav);
    }

    public SaveFile Sav { get; private set; }
    /// <summary>Caminho do arquivo (ou "zip|entrada" para saves dentro de .zip).</summary>
    public string Path { get; }
    public SlotHistory History { get; private set; }

    /// <summary>Tamanho do historico no ultimo salvar/abrir (o que vem depois sao alteracoes pendentes).</summary>
    public int HistoryAtSave { get; set; }
    /// <summary>Caixa e pagina abertas quando a aba deixou de ser a ativa.</summary>
    public int CurrentBox { get; set; }
    public PageViewModel? LastPage { get; set; }

    private bool _isDirty;
    public bool IsDirty { get => _isDirty; set => Set(ref _isDirty, value); }

    private bool _isActive;
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    public string Game => CoreAdapter.GetGameName(Sav);
    public string FileName => ZipSaves.DisplayName(Path);
    public string Trainer => $"{Sav.OT} · TID {Sav.DisplayTID}";
    public GameArt? Art => GameArt.Get(Sav.Version);
    public string Tooltip => $"{Game} · {Trainer}\n{Path}";

    public RelayCommand? SelectCommand { get; init; }
    public RelayCommand? CloseCommand { get; init; }

    /// <summary>Recarregado do disco (ex.: backup restaurado): descarta o estado em memoria.</summary>
    public void Replace(SaveFile sav)
    {
        Sav = sav;
        History = new SlotHistory(sav);
        HistoryAtSave = 0;
        IsDirty = false;
        foreach (var p in (string[])[nameof(Game), nameof(Trainer), nameof(Art), nameof(Tooltip)])
            Raise(p);
    }
}
