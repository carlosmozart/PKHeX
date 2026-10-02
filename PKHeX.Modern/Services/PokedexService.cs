using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>De onde vem uma informacao da Pokedex: um save (aberto ou da pasta do Save Manager) ou o bank.</summary>
public sealed record DexSource(string Id, string Name, bool IsOpenSave, bool IsBank, ushort MaxSpecies);

/// <summary>Um Pokemon possuido: fonte, local legivel e dados para "ir ate ele" (so no save aberto).</summary>
public sealed record DexLocation(DexSource Source, string Where, string FormName, bool IsShiny, int Box, int Slot);

/// <summary>Uma especie na Pokedex centralizada.</summary>
public sealed class DexEntry(ushort species, string name, int generation, byte type1, byte type2)
{
    public ushort Species { get; } = species;
    public string Name { get; } = name;
    public int Generation { get; } = generation;
    public byte Type1 { get; } = type1;
    public byte Type2 { get; } = type2;
    /// <summary>Fontes cuja Pokedex marca a especie como vista / capturada.</summary>
    public List<DexSource> SeenIn { get; } = [];
    public List<DexSource> CaughtIn { get; } = [];
    public List<DexLocation> Owned { get; } = [];
}

/// <summary>
/// Pokedex centralizada (ideia do PKVault): junta a Pokedex de cada save (visto/capturado) com os Pokemon que
/// existem de fato nas caixas e equipes de todos os saves da pasta do Save Manager, do save aberto e do bank.
/// Le os saves direto pelo Core, sem mexer no estado global do app (lista de itens, sprites do save aberto).
/// </summary>
public static class PokedexService
{
    public static ushort MaxSpecies => (ushort)(Species.MAX_COUNT - 1);

    // Ultima especie de cada geracao (1..9).
    private static readonly ushort[] GenerationEnd = [151, 251, 386, 493, 649, 721, 809, 905, ushort.MaxValue];

    public static int GetGeneration(ushort species)
    {
        for (int i = 0; i < GenerationEnd.Length; i++)
            if (species <= GenerationEnd[i])
                return i + 1;
        return GenerationEnd.Length;
    }

    /// <summary>Tipos da especie (forma base), pela tabela mais recente que tem a especie.</summary>
    public static (byte Type1, byte Type2) GetTypes(ushort species)
    {
        IPersonalTable[] tables = [PersonalTable.SV, PersonalTable.SWSH, PersonalTable.LA, PersonalTable.BDSP, PersonalTable.USUM];
        foreach (var t in tables)
        {
            if (species > t.MaxSpeciesID || !t.IsPresentInGame(species, 0))
                continue;
            var p = t.GetFormEntry(species, 0);
            return (p.Type1, p.Type2);
        }
        var any = PersonalTable.SV.GetFormEntry(species, 0);
        return (any.Type1, any.Type2);
    }

    /// <summary>Saves da pasta (e subpastas), lidos sem alterar o estado do app. Caminhos iguais ao do save aberto sao pulados.</summary>
    public static IEnumerable<(string Path, SaveFile Sav)> ReadFolder(string folder, string? skipPath)
    {
        if (!Directory.Exists(folder))
            yield break;
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (skipPath is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(skipPath), StringComparison.OrdinalIgnoreCase))
                continue;
            if (TryRead(path) is { } sav)
                yield return (path, sav);
        }
    }

    /// <summary>Le um save sem alterar o estado do app. Null se nao for um save (ou ilegivel).</summary>
    public static SaveFile? TryRead(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length is > 0 and < 64 * 1024 * 1024 && SaveUtil.TryGetSaveFile(path, out var sav))
                return sav;
        }
        catch
        {
            // arquivo ilegivel: ignora
        }
        return null;
    }

    /// <summary>Monta a Pokedex: uma entrada por especie (1..<see cref="MaxSpecies"/>), com o que cada fonte tem.</summary>
    /// <param name="saves">Saves (o aberto primeiro, marcado com isOpen).</param>
    /// <param name="includeBank">Inclui os Pokemon de todos os bancos do bank.</param>
    public static (IReadOnlyList<DexEntry> Entries, IReadOnlyList<DexSource> Sources) Build(
        IEnumerable<(string Path, SaveFile Sav, bool IsOpen)> saves, bool includeBank)
    {
        var names = CoreAdapter.SpeciesNames;
        var entries = new DexEntry[MaxSpecies + 1];
        for (ushort s = 1; s <= MaxSpecies; s++)
        {
            var (t1, t2) = GetTypes(s);
            entries[s] = new DexEntry(s, s < names.Count ? names[s] : $"#{s}", GetGeneration(s), t1, t2);
        }

        var sources = new List<DexSource>();
        foreach (var (path, sav, isOpen) in saves)
        {
            var source = new DexSource(path, $"{GameInfo.GetVersionName(sav.Version)} · {sav.OT}" + (isOpen ? " (aberto)" : ""), isOpen, false, sav.MaxSpeciesID);
            sources.Add(source);
            AddPokedexFlags(entries, sav, source);
            AddOwned(entries, sav, source);
        }
        if (includeBank)
        {
            var bank = new DexSource("bank", "Bank", false, true, MaxSpecies);
            sources.Add(bank);
            foreach (var name in BankStorage.GetBanks())
            {
                foreach (var box in BankStorage.GetBoxes(name))
                {
                    var data = BankStorage.ReadBox(box);
                    for (int i = 0; i < data.Length; i++)
                        if (data[i] is { } pk)
                            Add(entries, bank, pk, $"{name} › {box.Name} · {i + 1}", -1, i);
                }
            }
        }
        return ([.. entries.Skip(1)], sources);
    }

    private static void AddPokedexFlags(DexEntry[] entries, SaveFile sav, DexSource source)
    {
        if (!sav.HasPokeDex)
            return;
        var max = Math.Min(sav.MaxSpeciesID, MaxSpecies);
        for (ushort s = 1; s <= max; s++)
        {
            try
            {
                if (sav.GetCaught(s))
                {
                    entries[s].CaughtIn.Add(source);
                    entries[s].SeenIn.Add(source);
                }
                else if (sav.GetSeen(s))
                {
                    entries[s].SeenIn.Add(source);
                }
            }
            catch
            {
                // alguns jogos nao tem todas as especies na Pokedex
            }
        }
    }

    private static void AddOwned(DexEntry[] entries, SaveFile sav, DexSource source)
    {
        try
        {
            for (int b = 0; b < sav.BoxCount; b++)
            {
                var boxName = CoreAdapter.GetBoxName(sav, b);
                for (int i = 0; i < sav.BoxSlotCount; i++)
                    Add(entries, source, sav.GetBoxSlotAtIndex(b, i), $"{boxName} · {i + 1}", b, i);
            }
            if (sav.HasParty)
                for (int i = 0; i < sav.PartyCount; i++)
                    Add(entries, source, sav.GetPartySlotAtIndex(i), $"Equipe · {i + 1}", -1, i);
        }
        catch
        {
            // save com caixas ilegiveis: fica so com a Pokedex
        }
    }

    private static void Add(DexEntry[] entries, DexSource source, PKM pk, string where, int box, int slot)
    {
        if (pk.Species == 0 || pk.Species > MaxSpecies || pk.IsEgg)
            return;
        var form = pk.Form == 0 ? "" : EncounterDatabase.GetSpeciesFormName(pk.Species, pk.Form, pk.Context);
        entries[pk.Species].Owned.Add(new DexLocation(source, where, form, pk.IsShiny, box, slot));
    }
}
