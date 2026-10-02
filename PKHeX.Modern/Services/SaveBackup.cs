using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PKHeX.Modern.Services;

/// <summary>Um backup na pasta de backups. <see cref="Source"/> = caminho do save original (null em backups antigos, sem registro).</summary>
public sealed record BackupInfo(string Path, string SaveName, string? Source, DateTime Created);

/// <summary>
/// Backup automatico: antes de salvar por cima de um arquivo existente, copia o original para
/// %APPDATA%\PKHeX.Modern\backups (nome original + data/hora). Guarda os mais recentes de cada save.
/// O caminho de origem de cada backup fica em index.json, para o "Restaurar" saber para onde voltar.
/// </summary>
public static partial class SaveBackup
{
    /// <summary>Quantos backups manter por nome de arquivo.</summary>
    private const int KeepPerFile = 20;
    private const string IndexFile = "index.json";

    public static string Folder { get; set; } = Path.Combine(
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
        File.SetCreationTime(backup, DateTime.Now); // File.Copy mantem as datas do save; a de criacao passa a ser a do backup
        UpdateIndex(index => index[Path.GetFileName(backup)] = Path.GetFullPath(path));
        Prune(name, ext);
        return backup;
    }

    /// <summary>Todos os backups, do mais novo para o mais antigo.</summary>
    public static IReadOnlyList<BackupInfo> List()
    {
        if (!Directory.Exists(Folder))
            return [];
        var index = new Dictionary<string, string>(ReadIndex(), StringComparer.OrdinalIgnoreCase);
        return [.. new DirectoryInfo(Folder).GetFiles()
            .Where(f => !f.Name.Equals(IndexFile, StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                var m = StampPattern().Match(Path.GetFileNameWithoutExtension(f.Name));
                var saveName = m.Success ? m.Groups["name"].Value + f.Extension : f.Name;
                var created = m.Success && DateTime.TryParseExact(m.Groups["stamp"].Value, "yyyy-MM-dd HH-mm-ss", null,
                    System.Globalization.DateTimeStyles.None, out var d) ? d : f.CreationTime;
                return new BackupInfo(f.FullName, saveName, index.GetValueOrDefault(f.Name), created);
            })
            .OrderByDescending(b => b.Created)];
    }

    /// <summary>
    /// Restaura o backup em <paramref name="target"/>. O arquivo que estava la ganha um backup antes (da para voltar atras).
    /// Retorna o caminho desse novo backup (ou null se o destino nao existia).
    /// </summary>
    public static string? Restore(BackupInfo backup, string target)
    {
        if (Path.GetFullPath(target).Equals(Path.GetFullPath(backup.Path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("o destino é o próprio backup.");
        var safety = BeforeOverwrite(target);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        File.Copy(backup.Path, target, overwrite: true);
        File.SetLastWriteTime(target, DateTime.Now);
        return safety;
    }

    public static void Delete(BackupInfo backup)
    {
        File.Delete(backup.Path);
        UpdateIndex(index => index.Remove(Path.GetFileName(backup.Path)));
    }

    // "Nome do save 2026-10-02 14-30-05" ou "... (2)"
    [GeneratedRegex(@"^(?<name>.+) (?<stamp>\d{4}-\d{2}-\d{2} \d{2}-\d{2}-\d{2})( \(\d+\))?$")]
    private static partial Regex StampPattern();

    /// <summary>Apaga os backups mais antigos desse save, mantendo os <see cref="KeepPerFile"/> mais recentes.</summary>
    private static void Prune(string name, string ext)
    {
        try
        {
            var old = new DirectoryInfo(Folder).GetFiles($"{name} ????-??-?? ??-??-??*{ext}")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(KeepPerFile)
                .ToList();
            foreach (var f in old)
                f.Delete();
            if (old.Count > 0)
                UpdateIndex(index => { foreach (var f in old) index.Remove(f.Name); });
        }
        catch
        {
            // limpeza nao e critica
        }
    }

    private static Dictionary<string, string> ReadIndex()
    {
        try
        {
            var path = Path.Combine(Folder, IndexFile);
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
        }
        catch
        {
            // indice corrompido: os backups continuam la, so sem a origem
        }
        return [];
    }

    private static void UpdateIndex(Action<Dictionary<string, string>> change)
    {
        try
        {
            var index = new Dictionary<string, string>(ReadIndex(), StringComparer.OrdinalIgnoreCase);
            change(index);
            File.WriteAllText(Path.Combine(Folder, IndexFile), JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // sem indice o backup ainda vale (Restaurar como...)
        }
    }
}
