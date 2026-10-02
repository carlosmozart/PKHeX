using System;
using System.IO;
using System.Linq;

namespace PKHeX.Modern.Services;

/// <summary>
/// Backup automatico: antes de salvar por cima de um arquivo existente, copia o original para
/// %APPDATA%\PKHeX.Modern\backups (nome original + data/hora). Guarda os mais recentes de cada save.
/// </summary>
public static class SaveBackup
{
    /// <summary>Quantos backups manter por nome de arquivo.</summary>
    private const int KeepPerFile = 20;

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PKHeX.Modern", "backups");

    /// <summary>Copia o arquivo atual (se existir) antes de ser sobrescrito. Retorna o caminho do backup ou null.</summary>
    public static string? BeforeOverwrite(string path)
    {
        if (!File.Exists(path))
            return null;
        Directory.CreateDirectory(Folder);
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var stamp = $"{name} {DateTime.Now:yyyy-MM-dd HH-mm-ss}";
        var backup = Path.Combine(Folder, stamp + ext);
        for (int i = 2; File.Exists(backup); i++) // dois saves no mesmo segundo: nunca sobrescreve um backup
            backup = Path.Combine(Folder, $"{stamp} ({i}){ext}");
        File.Copy(path, backup);
        Prune(name, ext);
        return backup;
    }

    /// <summary>Apaga os backups mais antigos desse save, mantendo os <see cref="KeepPerFile"/> mais recentes.</summary>
    private static void Prune(string name, string ext)
    {
        try
        {
            var old = new DirectoryInfo(Folder).GetFiles($"{name} ????-??-?? ??-??-??*{ext}")
                .OrderByDescending(f => f.CreationTimeUtc) // File.Copy mantem a data de modificacao do save; a de criacao e a do backup
                .Skip(KeepPerFile);
            foreach (var f in old)
                f.Delete();
        }
        catch
        {
            // limpeza nao e critica
        }
    }
}
