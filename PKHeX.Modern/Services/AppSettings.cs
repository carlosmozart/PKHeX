using System;
using System.IO;
using System.Text.Json;

namespace PKHeX.Modern.Services;

/// <summary>Preferencias do usuario, salvas em %APPDATA%\PKHeX.Modern\settings.json.</summary>
public sealed class AppSettings
{
    public string? LastSavePath { get; set; }
    /// <summary>Saves abertos por ultimo (mais recente primeiro), para o Inicio.</summary>
    public System.Collections.Generic.List<string> RecentSaves { get; set; } = [];
    public bool OpenLastSaveOnStartup { get; set; }
    /// <summary>Ao clicar num Pokemon ilegal, perguntar se quer legalizar (desligado = so abre no editor).</summary>
    public bool AskLegalizeOnClick { get; set; } = true;
    public bool DarkTheme { get; set; } = true;
    /// <summary>Cor de destaque (chave de Theme.AccentTheme.Presets: red, cyan, blue...).</summary>
    public string? AccentColor { get; set; }
    /// <summary>Tema completo (chave de Theme.AppTheme.Presets: default, pss, pixel, za).</summary>
    public string? ThemeKey { get; set; }
    /// <summary>Box background intensity: normal, soft, disabled. Does not change the game's wallpaper.</summary>
    public int WallpaperIntensity { get; set; }
    /// <summary>Fonte da interface (chave de Theme.AppTheme.Fonts: theme, inter, pixelify...); null = a do tema.</summary>
    public string? FontKey { get; set; }
    /// <summary>Pasta do Save Manager (null = pasta "saves" ao lado do exe).</summary>
    public string? SavesFolder { get; set; }
    public bool HideSaveSID { get; set; }
    /// <summary>Pastas de arquivos .pk* usadas como bancos externos (pagina Bank).</summary>
    public System.Collections.Generic.List<string> ExternalBankFolders { get; set; } = [];
    /// <summary>Modo legal: o editor so oferece opcoes legais e nao deixa aplicar um Pokemon ilegal.</summary>
    public bool LegalMode { get; set; } = true;
    /// <summary>Verificar ao iniciar se saiu uma release nova no GitHub.</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Ao achar uma versao nova, mostrar as notas e oferecer atualizar (vale ao reiniciar o app).</summary>
    public bool AutoUpdate { get; set; } = true;
    /// <summary>Android: a 0.4.7/0.4.8 gravavam a verificacao desligada; na primeira abertura com atualizacao, religa uma vez.</summary>
    public bool AndroidUpdatesReady { get; set; }
    /// <summary>Idioma da interface (Services.Loc): "pt-BR" (padrao) ou "en". Vale ao reiniciar.</summary>
    public string UiLanguage { get; set; } = Loc.Portuguese;

    /// <summary>So grava em disco instancias carregadas via <see cref="Load"/> (testes usam instancias em memoria).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Persist { get; private set; }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PKHeX.Modern", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
                s.Persist = true;
                return s;
            }
        }
        catch
        {
            // arquivo corrompido: volta ao padrao
        }
        return new() { Persist = true };
    }

    public void Save()
    {
        if (!Persist)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // preferencias nao sao criticas
        }
    }
}
