using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>Um Pokemon do bank anexado a um save: a copia que esta no jogo atualiza a do bank ao sincronizar.</summary>
public sealed class BankLink
{
    /// <summary>Identidade do Pokemon (EC, PID e ID do treinador), a mesma depois de evoluir ou subir de nivel.</summary>
    public string Id { get; set; } = "";
    public string SavePath { get; set; } = "";
    public string SaveName { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime Linked { get; set; }
    public DateTime? LastSync { get; set; }
    /// <summary>Formatos guardados como variante (ex.: "pk5"), quando o jogo e de outra geracao que o arquivo do bank.</summary>
    public List<string> Variants { get; set; } = [];
}

/// <summary>Uma variante guardada: a versao do Pokemon que veio de um jogo de outra geracao.</summary>
public sealed record BankVariant(string File, PKM Pk, DateTime Saved);

/// <summary>Resultado de "Atualizar anexados" para um Pokemon.</summary>
public sealed record BankLinkResult(BankLink Link, string Message, bool Lost);

/// <summary>
/// Pokemon anexados (ideia do PKVault): o bank guarda o original e o save recebe uma copia ligada a ele. Ao atualizar,
/// a versao do jogo volta para o bank: no mesmo formato, substitui o arquivo; em outro formato (convertido para outra
/// geracao), fica guardada como variante daquela geracao, sem perder o original. Lista em bank\anexados.json,
/// variantes em bank\.variantes\&lt;id&gt;\.
/// </summary>
public static class BankLinks
{
    private static string IndexFile => Path.Combine(BankStorage.Root, "anexados.json");
    private static string VariantRoot => Path.Combine(BankStorage.Root, ".variantes");
    private static List<BankLink>? _cache;
    private static HashSet<string>? _ids;

    /// <summary>Identidade do Pokemon. Gen 1/2 nao tem PID/EC: usa os DVs e o nome do treinador.</summary>
    public static string IdOf(PKM pk) => pk.Format <= 2
        ? $"G12-{pk.TID16:00000}-{pk.OriginalTrainerName}-{pk.IV_ATK}{pk.IV_DEF}{pk.IV_SPE}{pk.IV_SPA}"
        : $"{pk.EncryptionConstant:X8}-{pk.PID:X8}-{pk.TID16:00000}-{pk.SID16:00000}";

    public static IReadOnlyList<BankLink> All => Load();

    private static List<BankLink> Load()
    {
        if (_cache is not null)
            return _cache;
        try
        {
            _cache = File.Exists(IndexFile) ? JsonSerializer.Deserialize<List<BankLink>>(File.ReadAllText(IndexFile)) ?? [] : [];
        }
        catch
        {
            _cache = [];
        }
        _ids = [.. _cache.Select(l => l.Id)];
        return _cache;
    }

    private static void Save()
    {
        var list = Load();
        Directory.CreateDirectory(BankStorage.Root);
        File.WriteAllText(IndexFile, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        _ids = [.. list.Select(l => l.Id)];
        BankStorage.NotifyCollectionChanged();
    }

    /// <summary>Esquece a lista em memoria (testes trocam a pasta do bank).</summary>
    public static void Reset()
    {
        _cache = null;
        _ids = null;
    }

    public static bool IsAttached(PKM? pk)
    {
        if (pk is null || CoreAdapter.IsEmpty(pk))
            return false;
        Load();
        return _ids!.Contains(IdOf(pk));
    }

    public static BankLink? Find(PKM pk) => Load().FirstOrDefault(l => l.Id == IdOf(pk));

    /// <summary>Anexa o Pokemon do bank ao save (substitui um vinculo antigo do mesmo Pokemon).</summary>
    public static void Attach(PKM bankPk, string savePath, string saveName)
    {
        var list = Load();
        var id = IdOf(bankPk);
        var variants = list.FirstOrDefault(l => l.Id == id)?.Variants ?? []; // reanexar nao perde as variantes
        list.RemoveAll(l => l.Id == id);
        list.Add(new BankLink
        {
            Id = id, SavePath = savePath, SaveName = saveName,
            Name = bankPk.IsEgg ? "Ovo" : CoreAdapter.SpeciesNames[bankPk.Species],
            Linked = DateTime.Now,
            Variants = variants,
        });
        Save();
    }

    /// <summary>Desfaz o vinculo (o Pokemon saiu do bank ou o usuario desanexou). As variantes vao junto.</summary>
    public static void Detach(PKM pk) => Detach(IdOf(pk));

    public static void Detach(string id)
    {
        var list = Load();
        if (list.RemoveAll(l => l.Id == id) == 0)
            return;
        Save();
        try
        {
            var dir = Path.Combine(VariantRoot, Clean(id));
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch
        {
            // variante em uso: fica para depois
        }
    }

    /// <summary>Variantes guardadas deste Pokemon (outros formatos que vieram dos jogos).</summary>
    public static IReadOnlyList<string> GetVariantFiles(PKM pk)
    {
        var dir = Path.Combine(VariantRoot, Clean(IdOf(pk)));
        return Directory.Exists(dir) ? Directory.GetFiles(dir) : [];
    }

    /// <summary>Variantes guardadas deste Pokemon, lidas do disco (as ilegiveis ficam de fora).</summary>
    public static IReadOnlyList<BankVariant> GetVariants(PKM pk)
    {
        var list = new List<BankVariant>();
        foreach (var file in GetVariantFiles(pk))
            if (BankStorage.ReadEntity(file) is { } v && !CoreAdapter.IsEmpty(v))
                list.Add(new BankVariant(file, v, File.GetLastWriteTime(file)));
        return [.. list.OrderBy(v => v.Pk.Format)];
    }

    /// <summary>Apaga uma variante (o original do bank nao muda).</summary>
    public static void DeleteVariant(PKM original, BankVariant variant)
    {
        File.Delete(variant.File);
        if (Find(original) is not { } link)
            return;
        var ext = variant.Pk.Extension;
        if (!GetVariantFiles(original).Any(f => Path.GetExtension(f).Equals("." + ext, StringComparison.OrdinalIgnoreCase)))
        {
            link.Variants.Remove(ext);
            Save();
        }
    }

    /// <summary>Liga o anexado a outro save (a variante foi levada para la), mantendo as variantes.</summary>
    public static void Relink(PKM original, string savePath, string saveName)
    {
        if (Find(original) is not { } link)
            return;
        link.SavePath = savePath;
        link.SaveName = saveName;
        link.Linked = DateTime.Now;
        link.LastSync = null;
        Save();
    }

    private static string Clean(string id) => PathUtil.CleanFileName(id);

    /// <summary>
    /// Atualiza os anexados: procura cada um no save vinculado (o aberto em memoria, ou o arquivo) e traz a versao do
    /// jogo para o bank. <paramref name="openPath"/>/<paramref name="openSav"/> = save aberto (usa a versao em memoria).
    /// </summary>
    public static IReadOnlyList<BankLinkResult> Sync(string? openPath, SaveFile? openSav)
    {
        var results = new List<BankLinkResult>();
        var list = Load();
        if (list.Count == 0)
            return results;

        // Onde esta cada Pokemon no bank (so nos bancos do app; pastas externas nao tem vinculo).
        var inBank = new Dictionary<string, (BankBox Box, int Slot, PKM Pk)>();
        foreach (var bank in BankStorage.GetBanks())
            foreach (var box in BankStorage.GetBoxes(bank))
            {
                var data = BankStorage.ReadBox(box);
                for (int i = 0; i < data.Length; i++)
                    if (data[i] is { } pk && !CoreAdapter.IsEmpty(pk))
                        inBank.TryAdd(IdOf(pk), (box, i, pk));
            }

        var saves = new Dictionary<string, SaveFile?>(StringComparer.OrdinalIgnoreCase);
        SaveFile? GetSave(string path)
        {
            if (openPath is not null && openSav is not null && string.Equals(path, openPath, StringComparison.OrdinalIgnoreCase))
                return openSav;
            if (!saves.TryGetValue(path, out var sav))
                saves[path] = sav = PokedexService.TryRead(path);
            return sav;
        }

        foreach (var link in list.ToList())
        {
            if (!inBank.TryGetValue(link.Id, out var bankSlot))
            {
                results.Add(new BankLinkResult(link, "não está mais no bank", true));
                continue;
            }
            var sav = GetSave(link.SavePath);
            if (sav is null)
            {
                results.Add(new BankLinkResult(link, $"save não encontrado ({ZipSaves.DisplayName(link.SavePath)})", true));
                continue;
            }
            var found = FindInSave(sav, link.Id);
            if (found is null)
            {
                results.Add(new BankLinkResult(link, $"não está mais em {link.SaveName} (trocado ou solto?)", true));
                continue;
            }
            var bankPk = bankSlot.Pk;
            link.Name = found.IsEgg ? "Ovo" : CoreAdapter.SpeciesNames[found.Species];
            if (found.GetType() == bankPk.GetType())
            {
                if (found.Data[..found.SIZE_STORED].SequenceEqual(bankPk.Data[..bankPk.SIZE_STORED]))
                {
                    results.Add(new BankLinkResult(link, "já estava igual", false));
                }
                else
                {
                    BankStorage.WriteSlot(bankSlot.Box, bankSlot.Slot, found);
                    results.Add(new BankLinkResult(link, $"atualizado com a versão de {link.SaveName} (Nv. {found.CurrentLevel})", false));
                }
            }
            else
            {
                // Outra geracao: guarda como variante, sem mexer no original do bank.
                var dir = Path.Combine(VariantRoot, Clean(link.Id));
                Directory.CreateDirectory(dir);
                CoreAdapter.ExportEntity(found, Path.Combine(dir, $"{link.Name}.{found.Extension}"));
                if (!link.Variants.Contains(found.Extension))
                    link.Variants.Add(found.Extension);
                results.Add(new BankLinkResult(link, $"variante {found.Extension.ToUpperInvariant()} guardada (Nv. {found.CurrentLevel}); o original {bankPk.Extension.ToUpperInvariant()} continua no bank", false));
            }
            link.LastSync = DateTime.Now;
        }
        Save();
        return results;
    }

    private static PKM? FindInSave(SaveFile sav, string id)
    {
        try
        {
            if (sav.HasParty)
                for (int i = 0; i < sav.PartyCount; i++)
                    if (sav.GetPartySlotAtIndex(i) is { } p && !CoreAdapter.IsEmpty(p) && IdOf(p) == id)
                        return p;
            for (int b = 0; b < sav.BoxCount; b++)
                for (int i = 0; i < sav.BoxSlotCount; i++)
                    if (sav.GetBoxSlotAtIndex(b, i) is { } p && !CoreAdapter.IsEmpty(p) && IdOf(p) == id)
                        return p;
        }
        catch
        {
            // save com caixas ilegiveis
        }
        return null;
    }
}
