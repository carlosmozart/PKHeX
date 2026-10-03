using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>De onde veio um Pokemon da pesquisa: um save (aberto numa aba ou so na pasta) ou um banco do bank.</summary>
public sealed record DbSource(string Id, string Name, GameVersion Version, bool IsOpen, string? Bank = null)
{
    public bool IsBank => Bank is not null;
}

/// <summary>Um Pokemon encontrado, com os textos ja montados para filtrar rapido.</summary>
public sealed class DbEntry
{
    public DbEntry(DbSource source, PKM pk, string where, int box, int slot)
    {
        Source = source;
        Pkm = pk;
        Where = where;
        Box = box;
        Slot = slot;
        Species = Name(CoreAdapter.SpeciesNames, pk.Species);
        Nickname = pk.IsNicknamed && !pk.IsEgg ? pk.Nickname : "";
        Moves = [.. new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4 }.Where(m => m != 0).Select(m => Name(CoreAdapter.MoveNames, m))];
        Item = pk.HeldItem == 0 ? "" : CoreAdapter.GetHeldItemName(pk);
        Ability = pk.Format >= 3 ? Name(CoreAdapter.AbilityNames, pk.Ability) : "";
        Nature = pk.Format >= 3 ? Name(CoreAdapter.NatureNames, (int)pk.Nature) : "";
        Ball = pk.Format >= 3 || pk.Ball != 0 ? Name(GameInfo.Strings.balllist, pk.Ball) : "";
        OT = pk.OriginalTrainerName;
        int max = pk.MaxIV;
        Span<int> ivs = stackalloc int[6];
        pk.GetIVs(ivs);
        foreach (var iv in ivs)
        {
            IvTotal += iv;
            if (iv >= max)
                PerfectIVs++;
        }
        Origin = CoreAdapter.GetVersionName(pk.Version);
        Text = string.Join('\n', new[] { Species, Nickname, Item, Ability, Nature, Ball, OT, Origin, source.Name, where }.Concat(Moves));
    }

    public DbSource Source { get; }
    public PKM Pkm { get; }
    /// <summary>"Caixa 3 · 12", "Equipe · 2" ou "Principal › Caixa 1 · 5".</summary>
    public string Where { get; }
    /// <summary>Posicao: caixa (-1 = equipe) e slot no save; no bank, indice da caixa do banco e slot.</summary>
    public int Box { get; }
    public int Slot { get; }

    public string Species { get; }
    public string Nickname { get; }
    public IReadOnlyList<string> Moves { get; }
    public string Item { get; }
    public string Ability { get; }
    public string Nature { get; }
    public string Ball { get; }
    public string OT { get; }
    public string Origin { get; }
    public int IvTotal { get; }
    public int PerfectIVs { get; }
    /// <summary>Tudo que a busca por texto olha, numa string so.</summary>
    public string Text { get; }

    private static string Name(IReadOnlyList<string> list, int index) => (uint)index < (uint)list.Count ? list[index] : "";
}

/// <summary>
/// Banco de dados da pesquisa: todos os Pokemon dos saves abertos (com as alteracoes nao salvas), dos saves da pasta
/// do Save Manager (inclusive dentro de .zip) e do bank. So le: nada e gravado.
/// </summary>
public static class PokemonDatabase
{
    /// <param name="open">Saves abertos nas abas (caminho, save).</param>
    /// <param name="folder">Pasta do Save Manager (saves fechados); null = nenhuma.</param>
    /// <param name="cache">Saves da pasta ja lidos (caminho → data + save), reaproveitados enquanto o arquivo nao mudar.</param>
    public static List<DbEntry> Build(IReadOnlyList<(string Path, SaveFile Sav)> open, string? folder,
        Dictionary<string, (DateTime Write, SaveFile Sav)> cache, bool includeBank = true)
    {
        var list = new List<DbEntry>();
        var openPaths = new HashSet<string>(open.Select(o => Normalize(o.Path)), StringComparer.OrdinalIgnoreCase);
        foreach (var (path, sav) in open)
            AddSave(list, new DbSource(path, $"{CoreAdapter.GetGameName(sav)} · {sav.OT} (aberto)", sav.Version, true), sav);

        if (folder is not null && Directory.Exists(folder))
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in SafeFiles(folder))
            {
                DateTime write;
                try { write = File.GetLastWriteTimeUtc(file); } catch { continue; }
                IEnumerable<(string Path, SaveFile Sav)> saves;
                if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    saves = ReadCached(cache, seen, file, write, () => ZipSaves.ReadAll(file).ToList());
                else
                    saves = ReadCached(cache, seen, file, write, () => PokedexService.TryRead(file) is { } s ? [(file, s)] : []);
                foreach (var (path, sav) in saves)
                {
                    if (openPaths.Contains(Normalize(path)))
                        continue; // o aberto entra com as alteracoes da aba
                    AddSave(list, new DbSource(path, $"{CoreAdapter.GetGameName(sav)} · {sav.OT}", sav.Version, false), sav);
                }
            }
            foreach (var gone in cache.Keys.Where(k => !seen.Contains(ZipSaves.FileOf(k))).ToList())
                cache.Remove(gone);
        }

        if (includeBank)
        {
            foreach (var bank in BankStorage.GetBanks().Concat(BankStorage.ExternalFolders.Select(BankStorage.GetExternalBankName)))
            {
                var source = new DbSource("bank:" + bank, $"Bank › {bank}", GameVersion.Any, false, bank);
                var boxes = BankStorage.GetBoxes(bank);
                for (int b = 0; b < boxes.Count; b++)
                {
                    PKM?[] data;
                    try { data = BankStorage.ReadBox(boxes[b]); } catch { continue; }
                    for (int i = 0; i < data.Length; i++)
                        if (data[i] is { Species: > 0 } pk)
                            list.Add(new DbEntry(source, pk, $"{boxes[b].Name} · {i + 1}", b, i));
                }
            }
        }
        return list;
    }

    // Saves de um arquivo (um .sav ou todos os de um .zip), relidos so quando o arquivo muda.
    private static IEnumerable<(string, SaveFile)> ReadCached(Dictionary<string, (DateTime Write, SaveFile Sav)> cache, HashSet<string> seen,
        string file, DateTime write, Func<List<(string, SaveFile)>> read)
    {
        seen.Add(file);
        var hits = cache.Where(kv => string.Equals(ZipSaves.FileOf(kv.Key), file, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hits.Count > 0 && hits.All(h => h.Value.Write == write))
            return hits.Select(h => (h.Key, h.Value.Sav)).ToList();
        foreach (var h in hits)
            cache.Remove(h.Key);
        var saves = read();
        foreach (var (path, sav) in saves)
            cache[path] = (write, sav);
        return saves;
    }

    private static IEnumerable<string> SafeFiles(string folder)
    {
        try { return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList(); }
        catch { return []; }
    }

    private static void AddSave(List<DbEntry> list, DbSource source, SaveFile sav)
    {
        try
        {
            if (sav.HasParty)
                for (int i = 0; i < sav.PartyCount; i++)
                    if (sav.GetPartySlotAtIndex(i) is { Species: > 0 } pk)
                        list.Add(new DbEntry(source, pk, $"Equipe · {i + 1}", -1, i));
            for (int b = 0; b < sav.BoxCount; b++)
            {
                var boxName = CoreAdapter.GetBoxName(sav, b);
                for (int i = 0; i < sav.BoxSlotCount; i++)
                    if (sav.GetBoxSlotAtIndex(b, i) is { Species: > 0 } pk)
                        list.Add(new DbEntry(source, pk, $"{boxName} · {i + 1}", b, i));
            }
        }
        catch
        {
            // save com caixas ilegiveis: fica com o que deu para ler
        }
    }

    private static string Normalize(string path) => ZipSaves.IsZipPath(path, out var zip, out var entry)
        ? ZipSaves.Combine(Path.GetFullPath(zip), entry)
        : Path.GetFullPath(path);
}
