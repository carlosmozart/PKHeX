using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>Resumo de um save encontrado na pasta do Save Manager (lido sem abrir no app).</summary>
public sealed record SaveEntry(
    string Path,
    string Group,
    int GroupOrder,
    string Game,
    byte Generation,
    string Trainer,
    bool TrainerIsFemale,
    string Ids,
    string PlayTime,
    uint Money,
    int Caught,
    DateTime LastWrite,
    IReadOnlyList<PKM> Party,
    GameVersion Version = GameVersion.Any,
    int Language = -1,
    DateTime? AdventureStart = null,
    uint TrainerId = 0);

/// <summary>
/// Varre a pasta de saves (e subpastas) e le um resumo de cada arquivo reconhecido pelo Core.
/// Cada subpasta de primeiro nivel vira um grupo; nomes comuns de console ganham um titulo.
/// </summary>
public static class SaveLibrary
{
    /// <summary>Saves maiores que isso sao ignorados (o maior save conhecido tem poucos MB).</summary>
    private const long MaxFileSize = 64 * 1024 * 1024;

    public static string DefaultFolder => Path.Combine(AppContext.BaseDirectory, "saves");

    // Ordem: do console mais antigo ao mais novo. Pastas desconhecidas vao para o fim, em ordem alfabetica.
    private static readonly (string[] Names, string Title)[] Consoles =
    [
        (["gb"], "Game Boy"),
        (["gbc"], "Game Boy Color"),
        (["gba"], "Game Boy Advance"),
        (["gc", "gamecube", "ngc"], "GameCube"),
        (["ds", "nds"], "Nintendo DS"),
        (["3ds"], "Nintendo 3DS"),
        (["switch", "nx"], "Nintendo Switch"),
    ];

    /// <summary>Le todos os saves da pasta. <paramref name="skipped"/> = arquivos que nao sao saves.</summary>
    public static IReadOnlyList<SaveEntry> Scan(string folder, out int skipped)
    {
        skipped = 0;
        var result = new List<SaveEntry>();
        if (!Directory.Exists(folder))
            return result;

        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            // Zip (ex.: backup do JKSV): cada save la dentro vira um cartao "arquivo.zip › entrada".
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var before = result.Count;
                var write = File.GetLastWriteTime(path);
                foreach (var (zipPath, sav) in ZipSaves.ReadAll(path))
                    if (Summarize(folder, zipPath, sav, write) is { } zippedEntry)
                        result.Add(zippedEntry);
                if (result.Count == before)
                    skipped++;
                continue;
            }
            if (TryRead(folder, path) is { } entry)
                result.Add(entry);
            else
                skipped++;
        }
        return [.. result
            .OrderBy(e => e.GroupOrder).ThenBy(e => e.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Generation).ThenBy(e => e.Game).ThenBy(e => System.IO.Path.GetFileName(e.Path))];
    }

    /// <summary>Resumo de um arquivo avulso (ex.: um backup). Null se nao for um save.</summary>
    public static SaveEntry? ReadOne(string path) => TryRead(System.IO.Path.GetDirectoryName(ZipSaves.FileOf(path)) ?? "", path);

    private static SaveEntry? TryRead(string folder, string path)
    {
        try
        {
            if (ZipSaves.IsZipPath(path, out var zip, out _))
                return ZipSaves.Load(path) is { } zipped ? Summarize(folder, path, zipped, File.GetLastWriteTime(zip)) : null;
            var info = new FileInfo(path);
            if (info.Length == 0 || info.Length > MaxFileSize)
                return null;
            if (!SaveUtil.TryGetSaveFile(path, out var sav))
                return null;
            return Summarize(folder, path, sav, info.LastWriteTime);
        }
        catch
        {
            return null; // arquivo ilegivel ou formato inesperado
        }
    }

    private static SaveEntry? Summarize(string folder, string path, SaveFile sav, DateTime lastWrite)
    {
        try
        {
            var (group, order) = GetGroup(folder, ZipSaves.FileOf(path));
            int caught;
            try { caught = sav.CaughtCount; } catch { caught = 0; }
            IReadOnlyList<PKM> party;
            try { party = sav.HasParty ? [.. sav.PartyData.Select(p => p.Clone())] : []; } catch { party = []; }

            return new SaveEntry(
                path, group, order,
                GameInfo.GetVersionName(sav.Version),
                sav.Generation,
                sav.OT,
                sav.Gender == 1,
                $"TID {sav.DisplayTID} · SID {sav.DisplaySID}",
                $"{sav.PlayedHours}h {sav.PlayedMinutes:00}m",
                sav.Money,
                caught,
                lastWrite,
                party,
                sav.Version, sav.Language, CoreAdapter.GetAdventureStart(sav), sav.DisplayTID);
        }
        catch
        {
            return null; // arquivo ilegivel ou formato inesperado
        }
    }

    private static (string Title, int Order) GetGroup(string folder, string path)
    {
        var relative = System.IO.Path.GetRelativePath(folder, path);
        var parts = relative.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        if (parts.Length < 2)
            return ("Saves", -1); // arquivos soltos na raiz aparecem primeiro

        var name = parts[0];
        for (int i = 0; i < Consoles.Length; i++)
        {
            if (Consoles[i].Names.Contains(name, StringComparer.OrdinalIgnoreCase))
                return (Consoles[i].Title, i);
        }
        return (name, Consoles.Length);
    }
}
