using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>
/// Descricao em portugues e onde conseguir cada item, vindas do AllGenWiki (Assets/item-info.json,
/// gerado por Tools/build_item_info.py). Cobre parte dos itens; sem dados, nao ha dica.
/// </summary>
public static class ItemInfo
{
    private sealed record Entry(string Name, Dictionary<string, string> Desc, Dictionary<string, List<string>> Where);

    private static Dictionary<string, Entry>? _items;
    private static readonly object Lock = new();

    private static Dictionary<string, Entry> Items
    {
        get
        {
            lock (Lock)
                return _items ??= Load();
        }
    }

    private static Dictionary<string, Entry> Load()
    {
        try
        {
            using var stream = typeof(ItemInfo).Assembly.GetManifestResourceStream("item-info.json");
            if (stream is null)
                return [];
            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stream) ?? [];
            var result = new Dictionary<string, Entry>(raw.Count);
            foreach (var (key, e) in raw)
            {
                result[key] = new Entry(
                    e.GetProperty("name").GetString() ?? key,
                    e.GetProperty("desc").Deserialize<Dictionary<string, string>>() ?? [],
                    e.GetProperty("where").Deserialize<Dictionary<string, List<string>>>() ?? []);
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Chave do item: nome em ingles so com letras e numeros ("King's Rock" -> "kingsrock").</summary>
    private static string Key(string name) => new([.. name.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit)]);

    /// <summary>
    /// Dica do item para a geracao do save: descricao (a da geracao ou a mais proxima) e onde conseguir
    /// (so os locais desta geracao). Null se o AllGenWiki nao tiver dados do item.
    /// </summary>
    public static string? GetTooltip(string? itemName, int generation, GameVersion version = GameVersion.Any)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return null;
        if (!Items.TryGetValue(Key(itemName), out var e))
            return GameText.IsMachine(itemName) ? GetMachineTooltip(itemName, generation, version) : null;
        var sb = new StringBuilder();
        if (Pick(e.Desc, generation) is { } desc)
            sb.Append(desc);
        if (e.Where.TryGetValue(generation.ToString(), out var where) && where.Count > 0)
        {
            if (sb.Length > 0)
                sb.Append("\n\n");
            sb.Append("Onde conseguir:");
            foreach (var w in where)
                sb.Append("\n• ").Append(w);
        }
        if (sb.Length == 0)
            return null;
        sb.Append("\n\nFonte: AllGenWiki");
        return sb.ToString();
    }

    /// <summary>TM/TR/HM: qual golpe ensina, a descricao dele e onde pegar a maquina neste jogo.</summary>
    private static string? GetMachineTooltip(string itemName, int generation, GameVersion version)
    {
        if (GameText.GetMachineMoveKey(itemName, generation, version) is not { } moveKey)
            return null;
        var names = CoreAdapter.MoveNames;
        string? move = null;
        for (int i = 1; i < names.Count && move is null; i++)
        {
            if (Key(names[i]) == moveKey)
                move = names[i];
        }
        move ??= moveKey;
        var sb = new StringBuilder($"Ensina {move}.");
        if (GameText.GetMove(move, generation) is { } desc)
            sb.Append('\n').Append(desc);
        if (GameText.GetMoveWhere(move, generation, version) is { } where)
            sb.Append("\n\nOnde conseguir:\n").Append(where);
        sb.Append("\n\nFonte: AllGenWiki");
        return sb.ToString();
    }

    /// <summary>Texto da geracao pedida; senao, o da geracao anterior mais proxima; senao, o da seguinte mais proxima.</summary>
    private static string? Pick(Dictionary<string, string> byGen, int generation)
    {
        if (byGen.Count == 0)
            return null;
        var gens = byGen.Keys.Select(k => int.TryParse(k, out var g) ? g : 0).OrderBy(g => g).ToList();
        var best = gens.LastOrDefault(g => g <= generation);
        if (best == 0)
            best = gens[0]; // todas sao de geracoes seguintes: a mais proxima
        return byGen[best.ToString()];
    }
}

/// <summary>
/// Textos em portugues do AllGenWiki (Assets/text-info.json, gerado por Tools/build_item_info.py):
/// descricao de golpes e habilidades por geracao (guardada so quando muda), onde conseguir cada TM e
/// onde fica cada tutor (por grupo de versoes) e qual golpe cada TM/TR/HM ensina.
/// </summary>
public static class GameText
{
    private sealed class Store
    {
        public Dictionary<string, Dictionary<string, string>> Moves = [];
        public Dictionary<string, Dictionary<string, string>> Abilities = [];
        public Dictionary<string, Dictionary<string, Dictionary<string, string>>> Tm = [];
        public Dictionary<string, Dictionary<string, Dictionary<string, string>>> Tutor = [];
        public Dictionary<string, Dictionary<string, Dictionary<string, string>>> Machines = [];
    }

    private static Store? _data;
    private static readonly object Lock = new();

    private static Store Data
    {
        get
        {
            lock (Lock)
                return _data ??= Load();
        }
    }

    private static Store Load()
    {
        var store = new Store();
        try
        {
            using var stream = typeof(GameText).Assembly.GetManifestResourceStream("text-info.json");
            if (stream is null)
                return store;
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            T Read<T>(string name) where T : new()
                => root.TryGetProperty(name, out var e) ? e.Deserialize<T>() ?? new T() : new T();
            store.Moves = Read<Dictionary<string, Dictionary<string, string>>>("moves");
            store.Abilities = Read<Dictionary<string, Dictionary<string, string>>>("abilities");
            store.Tm = Read<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>("tm");
            store.Tutor = Read<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>("tutor");
            store.Machines = Read<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>("machines");
        }
        catch
        {
            // sem dados: nenhuma dica
        }
        return store;
    }

    private static string Key(string name) => new([.. name.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit)]);

    public static string? GetMove(string? name, int generation) => Pick(Data.Moves, name, generation);
    public static string? GetAbility(string? name, int generation) => Pick(Data.Abilities, name, generation);

    /// <summary>Texto da geracao pedida ou da ultima mudanca antes dela; se so existe depois, o primeiro texto.</summary>
    private static string? Pick(Dictionary<string, Dictionary<string, string>> all, string? name, int generation)
    {
        if (string.IsNullOrWhiteSpace(name) || !all.TryGetValue(Key(name), out var byGen) || byGen.Count == 0)
            return null;
        var gens = byGen.Keys.Select(k => int.TryParse(k, out var g) ? g : 0).OrderBy(g => g).ToList();
        var best = gens.LastOrDefault(g => g <= generation);
        return byGen[(best == 0 ? gens[0] : best).ToString()];
    }

    /// <summary>Grupo de versoes do AllGenWiki para o jogo (ex.: HeartGold -> "heartgold-soulsilver").</summary>
    public static string? GroupOf(GameVersion version) => version switch
    {
        GameVersion.R or GameVersion.S => "ruby-sapphire",
        GameVersion.E => "emerald",
        GameVersion.FR or GameVersion.LG => "firered-leafgreen",
        GameVersion.D or GameVersion.P => "diamond-pearl",
        GameVersion.Pt => "platinum",
        GameVersion.HG or GameVersion.SS => "heartgold-soulsilver",
        GameVersion.B or GameVersion.W => "black-white",
        GameVersion.B2 or GameVersion.W2 => "black-2-white-2",
        GameVersion.X or GameVersion.Y => "x-y",
        GameVersion.OR or GameVersion.AS => "omega-ruby-alpha-sapphire",
        GameVersion.SN or GameVersion.MN => "sun-moon",
        GameVersion.US or GameVersion.UM => "ultra-sun-ultra-moon",
        GameVersion.GP or GameVersion.GE => "lets-go-pikachu-lets-go-eevee",
        GameVersion.SW or GameVersion.SH => "sword-shield",
        GameVersion.BD or GameVersion.SP => "brilliant-diamond-shining-pearl",
        GameVersion.PLA => "legends-arceus",
        GameVersion.SL or GameVersion.VL => "scarlet-violet",
        GameVersion.ZA => "legends-za",
        _ => null,
    };

    private static string GroupLabel(string group)
        => string.Join(' ', group.Split('-').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));

    /// <summary>
    /// Onde aprender o golpe neste jogo: TM (de onde pegar) e tutor. Se o jogo nao tiver grupo conhecido,
    /// lista todos os grupos da geracao. Null se o AllGenWiki nao tiver dados.
    /// </summary>
    public static string? GetMoveWhere(string? moveName, int generation, GameVersion version)
    {
        if (string.IsNullOrWhiteSpace(moveName))
            return null;
        var key = Key(moveName);
        var group = GroupOf(version);
        var lines = new List<string>();
        void Add(Dictionary<string, Dictionary<string, Dictionary<string, string>>> source, string label)
        {
            if (!source.TryGetValue(generation.ToString(), out var byMove) || !byMove.TryGetValue(key, out var byGroup))
                return;
            if (group is not null)
            {
                if (byGroup.TryGetValue(group, out var text))
                    lines.Add($"{label}: {text}");
                return;
            }
            foreach (var (g, text) in byGroup)
                lines.Add($"{label} ({GroupLabel(g)}): {text}");
        }
        Add(Data.Tm, "TM");
        Add(Data.Tutor, "Tutor");
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>Golpe que a TM/TR/HM ensina neste jogo (ex.: "TM02" -> "dragonclaw"), na chave do AllGenWiki.</summary>
    public static string? GetMachineMoveKey(string? itemName, int generation, GameVersion version)
    {
        if (string.IsNullOrWhiteSpace(itemName) || !Data.Machines.TryGetValue(generation.ToString(), out var byId))
            return null;
        var id = itemName.Trim().ToUpperInvariant();
        if (!byId.TryGetValue(id, out var byGroup) && !byId.TryGetValue(NormalizeMachineId(id, byId), out byGroup))
            return null;
        if (GroupOf(version) is { } group && byGroup.TryGetValue(group, out var move))
            return move;
        return byGroup.TryGetValue("*", out move) ? move : byGroup.Values.FirstOrDefault();
    }

    /// <summary>"TM1" / "TM001" -> o formato usado nos dados da geracao ("TM01" ou "TM001").</summary>
    private static string NormalizeMachineId(string id, Dictionary<string, Dictionary<string, string>> byId)
    {
        var prefix = new string([.. id.TakeWhile(char.IsLetter)]);
        if (!int.TryParse(id[prefix.Length..], out var n))
            return id;
        foreach (var width in (int[])[2, 3])
        {
            var candidate = prefix + n.ToString().PadLeft(width, '0');
            if (byId.ContainsKey(candidate))
                return candidate;
        }
        return id;
    }

    /// <summary>Nome de item de maquina ("TM01", "TR12", "HM03").</summary>
    public static bool IsMachine(string? itemName)
        => itemName is { Length: >= 3 } n && char.IsDigit(n[2])
           && (n.StartsWith("TM", StringComparison.OrdinalIgnoreCase) || n.StartsWith("TR", StringComparison.OrdinalIgnoreCase) || n.StartsWith("HM", StringComparison.OrdinalIgnoreCase));
}
