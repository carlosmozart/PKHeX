using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>De onde vem uma informacao da Pokedex: um save (aberto ou da pasta do Save Manager) ou o bank.</summary>
public sealed record DexSource(string Id, string Name, bool IsOpenSave, bool IsBank, ushort MaxSpecies);

/// <summary>Um Pokemon possuido: fonte, local legivel e dados para "ir ate ele" (so no save aberto).</summary>
public sealed record DexLocation(DexSource Source, string Where, string FormName, bool IsShiny, bool IsAlpha, int Box, int Slot);

/// <summary>
/// Uma entrada da Pokedex: a especie inteira (<see cref="IsFormEntry"/> falso: qualquer forma conta)
/// ou uma forma/genero especifico (lista "Formas e generos").
/// </summary>
public sealed class DexEntry(ushort species, byte form, sbyte gender, string name, string formName, int generation, byte type1, byte type2, EntityContext context, bool isFormEntry)
{
    public ushort Species { get; } = species;
    public byte Form { get; } = form;
    /// <summary>-1 = qualquer genero; 0/1 = macho/femea (so especies com sprite diferente por genero).</summary>
    public sbyte Gender { get; } = gender;
    public string Name { get; } = name;
    public string FormName { get; } = formName;
    public int Generation { get; } = generation;
    public byte Type1 { get; } = type1;
    public byte Type2 { get; } = type2;
    /// <summary>Jogo de referencia da forma (para o sprite e o nome da forma).</summary>
    public EntityContext Context { get; } = context;
    public bool IsFormEntry { get; } = isFormEntry;
    /// <summary>Fontes cuja Pokedex marca a especie como vista / capturada (a Pokedex dos jogos e por especie).</summary>
    public List<DexSource> SeenIn { get; private set; } = [];
    public List<DexSource> CaughtIn { get; private set; } = [];
    public List<DexLocation> Owned { get; } = [];

    /// <summary>Formas compartilham as listas de visto/capturado da especie (ate a leitura por forma, em <see cref="PokedexService.Build"/>).</summary>
    internal void ShareFlags(DexEntry species)
    {
        SeenIn = species.SeenIn;
        CaughtIn = species.CaughtIn;
    }

    /// <summary>A forma passa a ter listas proprias (jogos que guardam visto/capturado por forma).</summary>
    internal void OwnFlags()
    {
        if (SeenIn.Count == 0 && CaughtIn.Count == 0)
        {
            SeenIn = [];
            CaughtIn = [];
            return;
        }
        SeenIn = [.. SeenIn];
        CaughtIn = [.. CaughtIn];
    }
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

    // Tabelas de dados (da mais nova para a mais antiga) e o jogo de cada uma: tipos e formas vem da primeira que tem a especie.
    private static readonly (IPersonalTable Table, EntityContext Context)[] Tables =
    [
        (PersonalTable.SV, EntityContext.Gen9), (PersonalTable.ZA, EntityContext.Gen9a), (PersonalTable.LA, EntityContext.Gen8a),
        (PersonalTable.BDSP, EntityContext.Gen8b), (PersonalTable.SWSH, EntityContext.Gen8), (PersonalTable.GG, EntityContext.Gen7b),
        (PersonalTable.USUM, EntityContext.Gen7), (PersonalTable.AO, EntityContext.Gen6), (PersonalTable.B2W2, EntityContext.Gen5),
        (PersonalTable.HGSS, EntityContext.Gen4), (PersonalTable.E, EntityContext.Gen3),
    ];

    /// <summary>Especies cujo sprite muda com o genero (as mesmas do gerador de sprites do PKHeX).</summary>
    private static readonly HashSet<ushort> GenderedSprite =
    [
        (ushort)Species.Hippopotas, (ushort)Species.Hippowdon, (ushort)Species.Unfezant,
        (ushort)Species.Frillish, (ushort)Species.Jellicent, (ushort)Species.Pyroar,
    ];

    /// <summary>Tipos da especie/forma, pela tabela mais recente que a tem.</summary>
    public static (byte Type1, byte Type2) GetTypes(ushort species, byte form = 0)
    {
        foreach (var (t, _) in Tables)
        {
            if (species > t.MaxSpeciesID || !t.IsPresentInGame(species, form))
                continue;
            var p = t.GetFormEntry(species, form);
            return (p.Type1, p.Type2);
        }
        var any = PersonalTable.SV.GetFormEntry(species, 0);
        return (any.Type1, any.Type2);
    }

    /// <summary>
    /// Formas "colecionaveis" da especie (juntando todos os jogos), sem as que so existem em batalha (Mega, Gigantamax...),
    /// as de Totem e as dos nobres de Legends. Cada uma com o jogo de referencia.
    /// </summary>
    public static IReadOnlyList<(byte Form, EntityContext Context)> GetForms(ushort species)
    {
        var forms = new SortedDictionary<byte, EntityContext>();
        foreach (var (t, context) in Tables)
        {
            if (species > t.MaxSpeciesID)
                continue;
            var count = t.GetFormEntry(species, 0).FormCount;
            for (byte f = 0; f < count; f++)
            {
                if (forms.ContainsKey(f) || !t.IsPresentInGame(species, f))
                    continue;
                if (f > 0 && (FormInfo.IsBattleOnlyForm(species, f, context.Generation) || FormInfo.IsTotemForm(species, f) || FormInfo.IsLordForm(species, f, context)))
                    continue;
                forms[f] = context;
            }
        }
        if (forms.Count == 0)
            forms[0] = EntityContext.Gen9;
        return [.. forms.Select(kv => (kv.Key, kv.Value))];
    }

    private static string FormName(ushort species, byte form, EntityContext context)
    {
        try { return FormConverter.GetStringFromForm(species, form, GameInfo.Strings, context); }
        catch { return form == 0 ? "" : $"Forma {form}"; }
    }

    /// <summary>Saves da pasta (e subpastas), lidos sem alterar o estado do app. Caminhos iguais ao do save aberto sao pulados.</summary>
    public static IEnumerable<(string Path, SaveFile Sav)> ReadFolder(string folder, string? skipPath)
    {
        if (!Directory.Exists(folder))
            yield break;
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var (zipPath, zipped) in ZipSaves.ReadAll(path))
                {
                    if (skipPath is null || !string.Equals(zipPath, skipPath, StringComparison.OrdinalIgnoreCase))
                        yield return (zipPath, zipped);
                }
                continue;
            }
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
            if (ZipSaves.IsZipPath(path, out _, out _))
                return ZipSaves.Load(path);
            var info = new FileInfo(path);
            if (info.Length is > 0 and < 64 * 1024 * 1024 && SaveUtil.TryGetSaveFile(path, out var sav))
            {
                SaveNameHint.Apply(sav, path);
                return sav;
            }
        }
        catch
        {
            // arquivo ilegivel: ignora
        }
        return null;
    }

    /// <summary>Resultado: uma entrada por especie, outra lista com cada forma/genero, e as fontes lidas.</summary>
    public sealed record DexData(IReadOnlyList<DexEntry> Species, IReadOnlyList<DexEntry> Forms, IReadOnlyList<DexSource> Sources);

    /// <summary>Monta a Pokedex com o que cada fonte tem.</summary>
    /// <param name="saves">Saves (o aberto primeiro, marcado com isOpen).</param>
    /// <param name="includeBank">Inclui os Pokemon de todos os bancos do bank (e das pastas externas).</param>
    public static DexData Build(IEnumerable<(string Path, SaveFile Sav, bool IsOpen)> saves, bool includeBank)
    {
        var names = CoreAdapter.SpeciesNames;
        var entries = new DexEntry[MaxSpecies + 1];
        var forms = new List<DexEntry>();
        var formIndex = new Dictionary<(ushort, byte, sbyte), DexEntry>();
        for (ushort s = 1; s <= MaxSpecies; s++)
        {
            var name = s < names.Count ? names[s] : $"#{s}";
            var gen = GetGeneration(s);
            var (t1, t2) = GetTypes(s);
            var species = entries[s] = new DexEntry(s, 0, -1, name, "", gen, t1, t2, EntityContext.Gen9, false);

            var list = GetForms(s);
            foreach (var (form, context) in list)
            {
                var (f1, f2) = GetTypes(s, form);
                var formName = list.Count > 1 ? FormName(s, form, context) : "";
                sbyte[] genders = form == 0 && GenderedSprite.Contains(s) ? [0, 1] : [-1];
                foreach (var g in genders)
                {
                    var label = g < 0 ? formName : $"{formName} {(g == 0 ? "♂" : "♀")}".Trim();
                    var entry = new DexEntry(s, form, g, name, label, gen, f1, f2, context, true);
                    entry.ShareFlags(species);
                    forms.Add(entry);
                    formIndex[(s, form, g)] = entry;
                }
            }
        }

        var sources = new List<DexSource>();
        void Add(DexSource source, PKM pk, string where, int box, int slot)
        {
            if (pk.Species == 0 || pk.Species > MaxSpecies || pk.IsEgg)
                return;
            var formName = pk.Form == 0 ? "" : EncounterDatabase.GetSpeciesFormName(pk.Species, pk.Form, pk.Context);
            var location = new DexLocation(source, where, formName, pk.IsShiny, pk is IAlpha { IsAlpha: true }, box, slot);
            entries[pk.Species].Owned.Add(location);
            sbyte gender = pk.Form == 0 && GenderedSprite.Contains(pk.Species) ? (sbyte)(pk.Gender == 1 ? 1 : 0) : (sbyte)-1;
            if (formIndex.TryGetValue((pk.Species, pk.Form, gender), out var formEntry))
                formEntry.Owned.Add(location);
        }

        // Formas de especies com mais de uma forma: listas proprias, preenchidas por save logo abaixo.
        var formsBySpecies = forms.Where(f => f.Form > 0 || forms.Count(x => x.Species == f.Species) > 1)
            .GroupBy(f => f.Species).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var list in formsBySpecies.Values)
            foreach (var f in list)
                f.OwnFlags();

        foreach (var (path, sav, isOpen) in saves)
        {
            var source = new DexSource(path, $"{CoreAdapter.GetGameName(sav)} · {sav.OT}" + (isOpen ? " (aberto)" : ""), isOpen, false, sav.MaxSpeciesID);
            sources.Add(source);
            AddPokedexFlags(entries, sav, source);
            AddFormFlags(formsBySpecies, entries, sav, source);
            try
            {
                for (int b = 0; b < sav.BoxCount; b++)
                {
                    var boxName = CoreAdapter.GetBoxName(sav, b);
                    for (int i = 0; i < sav.BoxSlotCount; i++)
                        Add(source, sav.GetBoxSlotAtIndex(b, i), $"{boxName} · {i + 1}", b, i);
                }
                if (sav.HasParty)
                    for (int i = 0; i < sav.PartyCount; i++)
                        Add(source, sav.GetPartySlotAtIndex(i), $"Equipe · {i + 1}", -1, i);
            }
            catch
            {
                // save com caixas ilegiveis: fica so com a Pokedex
            }
        }
        if (includeBank)
        {
            var bank = new DexSource("bank", "Bank", false, true, MaxSpecies);
            sources.Add(bank);
            foreach (var name in BankStorage.GetBanks().Concat(BankStorage.ExternalFolders.Select(BankStorage.GetExternalBankName)))
            {
                foreach (var box in BankStorage.GetBoxes(name))
                {
                    var data = BankStorage.ReadBox(box);
                    for (int i = 0; i < data.Length; i++)
                        if (data[i] is { } pk)
                            Add(bank, pk, $"{name} › {box.Name} · {i + 1}", -1, i);
                }
            }
        }
        return new DexData([.. entries.Skip(1)], forms, sources);
    }

    /// <summary>
    /// Visto/capturado de cada forma neste save. Nos jogos que guardam a forma (Gen 4, 5 e 6, Sword/Shield, BDSP, Legends Arceus, Scarlet/Violet,
    /// Legends Z-A), a forma so conta se a Pokedex registrou aquela forma; nos outros (Gen 7 e Let's Go so guardam a forma
    /// exibida, nao as vistas), vale o dado da especie.
    /// Onde o jogo so guarda "forma vista", a forma conta como capturada se a especie foi capturada e a forma vista.
    /// </summary>
    private static void AddFormFlags(Dictionary<ushort, List<DexEntry>> formsBySpecies, DexEntry[] entries, SaveFile sav, DexSource source)
    {
        foreach (var (species, list) in formsBySpecies)
        {
            var speciesEntry = entries[species];
            bool seen = speciesEntry.SeenIn.Contains(source), caught = speciesEntry.CaughtIn.Contains(source);
            if (!seen)
                continue;
            foreach (var f in list)
            {
                var (formSeen, formCaught) = GetFormFlags(sav, species, f.Form);
                if (formSeen ?? true)
                    f.SeenIn.Add(source);
                if (caught && (formCaught ?? formSeen ?? true))
                    f.CaughtIn.Add(source);
            }
        }
    }

    /// <summary>Forma vista/capturada na Pokedex do save; null = este jogo nao guarda essa informacao.</summary>
    public static (bool? Seen, bool? Caught) GetFormFlags(SaveFile sav, ushort species, byte form)
    {
        try
        {
            switch (sav)
            {
                case SAV4 s4:
                {
                    var seenForms = s4.Dex.GetForms(species);
                    if (seenForms.Length == 0)
                        return (null, null); // especie sem formas registradas na Pokedex da Gen 4
                    return (seenForms.Any(x => x != Zukan4.FORM_NONE && x == form), null);
                }
                case SAV5 s5:
                {
                    var (index, count) = s5.Zukan.GetFormIndex(species);
                    if (count == 0 || form >= count)
                        return (null, null);
                    return (s5.Zukan.GetFormFlag(index + form, 0) || s5.Zukan.GetFormFlag(index + form, 1), null);
                }
                case SAV6XY xy:
                    return Gen6(xy.Zukan, species, form);
                case SAV6AO ao:
                    return Gen6(ao.Zukan, species, form);
                case SAV8BS bs:
                    if (Zukan8b.GetFormCount(species) <= form)
                        return (null, null);
                    return (bs.Zukan.GetHasFormFlag(species, form, false) || bs.Zukan.GetHasFormFlag(species, form, true), null);
                case SAV9SV sv:
                    if (sv.Zukan.GetRevision() == 0)
                        return (sv.Zukan.DexPaldea.Get(species).GetIsFormSeen(form), null);
                    var kitakami = sv.Zukan.DexKitakami.Get(species);
                    return (kitakami.GetSeenForm(form), kitakami.GetObtainedForm(form));
                case SAV8SWSH swsh:
                {
                    if (!swsh.Zukan.GetEntry(species, out var index))
                        return (null, null); // especie fora das Pokedex de Galar
                    bool seenForm = false;
                    for (int region = 0; region < 4 && !seenForm; region++)
                        seenForm = swsh.Zukan.GetSeenRegion(index, form, region);
                    return (seenForm, null);
                }
                case SAV8LA la:
                {
                    var dex = la.PokedexSave;
                    if (!dex.HasFormStorage(species, form))
                        return (null, null);
                    bool obtained = dex.HasAnyPokeObtainFlags(species, form);
                    return (obtained || dex.HasAnyPokeSeenInWildFlags(species, form), obtained);
                }
                case SAV9ZA za:
                    var entry = za.Zukan.GetEntry(species);
                    return (entry.GetIsFormSeen(form), entry.GetIsFormCaught(form));
            }
        }
        catch
        {
            // especie fora da Pokedex deste jogo
        }
        return (null, null);

        static (bool?, bool?) Gen6(Zukan6 dex, ushort species, byte form)
        {
            var (index, count) = dex.GetFormIndex(species);
            if (count == 0 || form >= count)
                return (null, null);
            return (dex.GetFormFlag(index + form, 0) || dex.GetFormFlag(index + form, 1), null);
        }
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

    /// <summary>
    /// Registra a especie como capturada na Pokedex do save, do jeito que o proprio jogo faz ao receber um Pokemon:
    /// grava um Pokemon temporario num slot livre (o Core atualiza a Pokedex daquele jogo) e devolve o slot como estava.
    /// Retorna true se a especie ficou capturada.
    /// </summary>
    public static bool RegisterCaught(SaveFile sav, ushort species)
    {
        if (species == 0 || species > sav.MaxSpeciesID || !sav.HasPokeDex)
            return false;
        if (FindScratchSlot(sav) is not var (box, slot))
            return false;
        var original = sav.GetBoxSlotAtIndex(box, slot);
        try
        {
            var pk = sav.BlankPKM;
            pk.Species = species;
            pk.Form = 0;
            pk.Gender = pk.GetSaneGender();
            pk.Language = sav.Language;
            pk.CurrentLevel = 5;
            pk.ClearNickname();
            pk.RefreshChecksum();
            var dexOnly = new EntityImportSettings(EntityImportOption.Disable, EntityImportOption.Enable, EntityImportOption.Disable);
            sav.SetBoxSlotAtIndex(pk, box, slot, dexOnly);
        }
        catch
        {
            // especie que este jogo nao aceita
        }
        finally
        {
            sav.SetBoxSlotAtIndex(original, box, slot, EntityImportSettings.None);
        }
        try { return sav.GetCaught(species); } catch { return false; }
    }

    /// <summary>O jogo aceita marcar "so vista"? Testa numa copia do save (a Gen 7, por exemplo, nao aceita).</summary>
    public static bool CanRegisterSeen(SaveFile sav, ushort species)
    {
        try { return RegisterSeen(sav.Clone(), species); }
        catch { return false; }
    }

    /// <summary>Marca a especie como vista (so nos jogos em que o Core permite marcar visto direto). True se ficou vista.</summary>
    public static bool RegisterSeen(SaveFile sav, ushort species)
    {
        if (species == 0 || species > sav.MaxSpeciesID || !sav.HasPokeDex)
            return false;
        try
        {
            // Gen 4 e 5: o "visto" generico do Core nao grava; marca como o jogo marca (genero visto + visto + exibido).
            var pi = sav.Personal.GetFormEntry(species, 0);
            byte gender = pi.Genderless ? (byte)2 : pi.OnlyFemale ? (byte)1 : (byte)0;
            switch (sav)
            {
                case SAV4 s4:
                    s4.Dex.SetSeenGender(species, gender);
                    s4.Dex.SetSeen(species);
                    break;
                case SAV5 s5:
                    s5.Zukan.SetSeen(species, gender, false);
                    if (!s5.Zukan.GetDisplayedAny(species))
                        s5.Zukan.SetDisplayed(species, gender, false);
                    break;
                default:
                    sav.SetSeen(species, true);
                    break;
            }
            return sav.GetSeen(species);
        }
        catch
        {
            return false;
        }
    }

    private static (int Box, int Slot)? FindScratchSlot(SaveFile sav)
    {
        for (int b = 0; b < sav.BoxCount; b++)
            for (int i = 0; i < sav.BoxSlotCount; i++)
                if (new SlotInfoBox(b, i, sav).CanWriteTo(sav))
                    return (b, i);
        return null;
    }
}
