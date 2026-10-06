using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>
/// Saves dentro de arquivos .zip (ex.: backups do JKSV, que guardam "main", "SaveData.bin" etc. num zip).
/// Um save no zip e representado pelo caminho "arquivo.zip|entrada" (o "|" nao existe em caminhos do Windows),
/// para o resto do app tratar como qualquer outro caminho. Gravar troca so aquela entrada, com backup do zip antes.
/// </summary>
public static class ZipSaves
{
    public const char Separator = '|';
    /// <summary>Entradas maiores que isso sao ignoradas (o maior save conhecido tem poucos MB).</summary>
    private const long MaxEntrySize = 64 * 1024 * 1024;

    public static string Combine(string zip, string entry) => zip + Separator + entry;

    /// <summary>"backup.zip|main" -> ("backup.zip", "main").</summary>
    public static bool IsZipPath(string? path, out string zip, out string entry)
    {
        zip = entry = "";
        var i = path?.IndexOf(Separator) ?? -1;
        if (i <= 0)
            return false;
        zip = path![..i];
        entry = path[(i + 1)..];
        return entry.Length > 0;
    }

    /// <summary>Arquivo de verdade no disco: o proprio caminho ou, para um save no zip, o zip.</summary>
    public static string FileOf(string path) => IsZipPath(path, out var zip, out _) ? zip : path;

    /// <summary>O arquivo (ou o zip) ainda existe.</summary>
    public static bool Exists(string? path) => path is not null && File.Exists(FileOf(path));

    /// <summary>Nome para mostrar: "backup.zip › main" ou o nome do arquivo.</summary>
    public static string DisplayName(string path) => IsZipPath(path, out var zip, out var entry)
        ? $"{Path.GetFileName(zip)} › {entry}"
        : Path.GetFileName(path);

    /// <summary>Nome sugerido ao exportar (so o nome da entrada, ex.: "main").</summary>
    public static string? EntryFileName(string? path) => IsZipPath(path, out _, out var entry) ? Path.GetFileName(entry) : null;

    /// <summary>Le um save de dentro do zip (null se a entrada nao existir ou nao for um save).</summary>
    public static SaveFile? Load(string path)
    {
        if (!IsZipPath(path, out var zip, out var entryName))
            return null;
        try
        {
            using var archive = ZipFile.OpenRead(zip);
            if (archive.GetEntry(entryName) is not { } entry)
                return null;
            return TryRead(entry, path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Todos os saves reconhecidos dentro de um zip, com o caminho "zip|entrada".</summary>
    public static IEnumerable<(string Path, SaveFile Sav)> ReadAll(string zip, CancellationToken token = default, Action<string>? readError = null)
    {
        var found = new List<(string, SaveFile)>();
        try
        {
            using var archive = ZipFile.OpenRead(zip);
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith('/') || entry.Length == 0 || entry.Length > MaxEntrySize)
                    continue;
                var path = Combine(zip, entry.FullName);
                if (TryRead(entry, path) is { } sav)
                    found.Add((path, sav));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            readError?.Invoke(zip + ": " + ex.Message);
        }
        return found;
    }

    private static SaveFile? TryRead(ZipArchiveEntry entry, string path)
    {
        if (entry.Length == 0 || entry.Length > MaxEntrySize)
            return null;
        var data = new byte[entry.Length];
        using (var stream = entry.Open())
            stream.ReadExactly(data);
        if (!SaveUtil.TryGetSaveFile(data, out var sav))
            return null;
        sav.Metadata.SetExtraInfo(path);
        SaveNameHint.Apply(sav, path);
        return sav;
    }

    /// <summary>Grava o save de volta na mesma entrada do zip (o resto do zip fica igual).</summary>
    public static void Write(SaveFile sav, string path)
    {
        if (!IsZipPath(path, out var zip, out var entryName))
            throw new ArgumentException("não é um save dentro de zip", nameof(path));
        var data = sav.Write().ToArray();
        // Grava numa copia e troca no fim: se algo falhar, o zip original fica intacto.
        var temp = zip + ".tmp";
        File.Copy(zip, temp, overwrite: true);
        try
        {
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Update))
            {
                archive.GetEntry(entryName)?.Delete();
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                entry.LastWriteTime = DateTimeOffset.Now;
                using var stream = entry.Open();
                stream.Write(data);
            }
            File.Move(temp, zip, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
