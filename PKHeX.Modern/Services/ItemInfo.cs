using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

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
    public static string? GetTooltip(string? itemName, int generation)
    {
        if (string.IsNullOrWhiteSpace(itemName) || !Items.TryGetValue(Key(itemName), out var e))
            return null;
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
