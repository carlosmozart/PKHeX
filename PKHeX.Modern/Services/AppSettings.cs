using System;
using System.IO;
using System.Text.Json;

namespace PKHeX.Modern.Services;

/// <summary>Preferencias do usuario, salvas em %APPDATA%\PKHeX.Modern\settings.json.</summary>
public sealed class AppSettings
{
    public string? LastSavePath { get; set; }
    public bool OpenLastSaveOnStartup { get; set; }
    public bool DarkTheme { get; set; } = true;
    /// <summary>Cor de destaque (chave de Theme.AccentTheme.Presets: red, cyan, blue...).</summary>
    public string? AccentColor { get; set; }
    /// <summary>Pasta do Save Manager (null = pasta "saves" ao lado do exe).</summary>
    public string? SavesFolder { get; set; }
    /// <summary>Pastas de arquivos .pk* usadas como bancos externos (pagina Bank).</summary>
    public System.Collections.Generic.List<string> ExternalBankFolders { get; set; } = [];

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
