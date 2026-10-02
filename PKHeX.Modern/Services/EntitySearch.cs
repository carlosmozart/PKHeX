using System;
using System.Collections.Generic;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

/// <summary>Um Pokemon do save com a posicao dele (Box &lt; 0 = equipe).</summary>
public sealed record StoredEntity(int Box, int Slot, PKM Pkm);

/// <summary>
/// Busca global: procura em todas as caixas e na equipe por especie, apelido, golpe ou item.
/// As palavras "shiny" e "ovo" (ou "egg") filtram por essas caracteristicas.
/// </summary>
public static class EntitySearch
{
    /// <summary>Le todos os Pokemon do save (copia), para buscar varias vezes sem decifrar de novo.</summary>
    public static List<StoredEntity> ReadAll(SaveFile sav)
    {
        var list = new List<StoredEntity>();
        for (int i = 0; i < CoreAdapter.GetPartyCount(sav); i++)
        {
            var pk = CoreAdapter.GetPartySlot(sav, i);
            if (!CoreAdapter.IsEmpty(pk))
                list.Add(new StoredEntity(-1, i, pk));
        }
        for (int box = 0; box < sav.BoxCount; box++)
        {
            for (int slot = 0; slot < sav.BoxSlotCount; slot++)
            {
                var pk = CoreAdapter.GetBoxSlot(sav, box, slot);
                if (!CoreAdapter.IsEmpty(pk))
                    list.Add(new StoredEntity(box, slot, pk));
            }
        }
        return list;
    }

    /// <summary>Motivo do resultado (ex.: "golpe Thunderbolt"), ou null se o Pokemon nao combina com a busca.</summary>
    public static string? Match(PKM pk, string query)
    {
        query = query.Trim();
        if (query.Length == 0)
            return null;
        if (Is(query, "shiny"))
            return pk.IsShiny ? "shiny" : null;
        if (Is(query, "ovo") || Is(query, "egg"))
            return pk.IsEgg ? "ovo" : null;

        if (Contains(Name(CoreAdapter.SpeciesNames, pk.Species), query))
            return "espécie";
        if (pk.IsNicknamed && Contains(pk.Nickname, query))
            return $"apelido {pk.Nickname}";
        Span<ushort> moves = stackalloc ushort[4];
        pk.GetMoves(moves);
        foreach (var move in moves)
        {
            if (move != 0 && Contains(Name(CoreAdapter.MoveNames, move), query))
                return $"golpe {Name(CoreAdapter.MoveNames, move)}";
        }
        var item = CoreAdapter.GetHeldItemName(pk);
        if (item.Length > 0 && Contains(item, query))
            return $"item {item}";
        return null;
    }

    private static string Name(IReadOnlyList<string> list, int index) => (uint)index < (uint)list.Count ? list[index] : "";
    private static bool Contains(string text, string query) => text.Contains(query, StringComparison.OrdinalIgnoreCase);
    private static bool Is(string query, string word) => query.Equals(word, StringComparison.OrdinalIgnoreCase);
}
