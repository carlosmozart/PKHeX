using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PKHeX.Core;

namespace PKHeX.Modern.Services;

public sealed record BoxReportEntry(string Location, PKM Pokemon, bool IsBank);

/// <summary>Read-only snapshots and RFC 4180 CSV (UTF-8 BOM). Legality is supplied by the UI in bounded chunks.</summary>
public static class BoxReport
{
    public static IReadOnlyList<BoxReportEntry> ReadSave(SaveFile snapshot)
    {
        var entries = new List<BoxReportEntry>();
        if (snapshot.HasParty)
            for (int i = 0; i < snapshot.PartyCount; i++) Add(string.Format(Loc.T("Equipe · slot {0}"), i + 1), snapshot.GetPartySlotAtIndex(i));
        for (int b = 0; b < snapshot.BoxCount; b++)
            for (int i = 0; i < snapshot.BoxSlotCount; i++) Add(string.Format(Loc.T("Caixa {0} · slot {1}"), b + 1, i + 1), snapshot.GetBoxSlotAtIndex(b, i));
        return entries;
        void Add(string location, PKM pk) { if (pk.Species > 0) entries.Add(new(location, pk, false)); }
    }

    public static IReadOnlyList<BoxReportEntry> ReadBank(string bank)
    {
        var entries = new List<BoxReportEntry>();
        foreach (var box in BankStorage.GetBoxes(bank, create: false))
            for (int i = 0; i < BankStorage.SlotsPerBox; i++)
                if (BankStorage.ReadSlot(box, i) is { Species: > 0 } pk)
                    entries.Add(new(bank + " · " + box.Name + string.Format(Loc.T(" · slot {0}"), i + 1), pk, true));
        return entries;
    }

    public static IReadOnlyList<string> Headers => new[]
    {
        "Local", "Espécie", "Forma", "Apelido", "Nível", "Gênero", "Shiny", "Natureza", "Habilidade", "Item", "Pokébola",
        "IV HP", "IV Attack", "IV Defense", "IV Sp. Atk", "IV Sp. Def", "IV Speed",
        "EV HP", "EV Attack", "EV Defense", "EV Sp. Atk", "EV Sp. Def", "EV Speed",
        "Golpe 1", "Golpe 2", "Golpe 3", "Golpe 4", "Treinador original", "Jogo de origem", "Legalidade",
    }.Select(Loc.T).ToArray();

    public static byte[] Encode(IReadOnlyList<BoxReportEntry> entries, IReadOnlyList<bool> legalities)
    {
        if (entries.Count != legalities.Count) throw new ArgumentException(nameof(legalities));
        var text = new StringBuilder();
        Line(Headers);
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i]; var p = e.Pokemon;
            Line(new[]
            {
                e.Location, Name(CoreAdapter.SpeciesNames, p.Species), Form(p), p.Nickname, N(p.CurrentLevel),
                p.Gender switch { 0 => "Male", 1 => "Female", _ => "Genderless" }, Loc.T(p.IsShiny ? "Sim" : "Não"),
                p.Format >= 3 ? Name(CoreAdapter.NatureNames, (int)p.Nature) : "", p.Format >= 3 ? Name(CoreAdapter.AbilityNames, p.Ability) : "", CoreAdapter.GetHeldItemName(p), Name(GameInfo.Strings.balllist, p.Ball),
                N(p.IV_HP), N(p.IV_ATK), N(p.IV_DEF), N(p.IV_SPA), N(p.IV_SPD), N(p.IV_SPE),
                N(p.EV_HP), N(p.EV_ATK), N(p.EV_DEF), N(p.EV_SPA), N(p.EV_SPD), N(p.EV_SPE),
                Move(p.Move1), Move(p.Move2), Move(p.Move3), Move(p.Move4), p.OriginalTrainerName, CoreAdapter.GetVersionName(p.Version), Loc.T(legalities[i] ? "Legal" : "Ilegal"),
            });
        }
        var encoding = new UTF8Encoding(true);
        return [.. encoding.GetPreamble(), .. encoding.GetBytes(text.ToString())];
        void Line(IEnumerable<string> cells) => text.AppendJoin(',', cells.Select(Escape)).Append("\r\n");
    }

    public static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Name(IReadOnlyList<string> list, int id) => id >= 0 && id < list.Count ? list[id] : N(id);
    private static string Move(int id) => id == 0 ? "" : Name(CoreAdapter.MoveNames, id);
    private static string Form(PKM pk) => FormConverter.GetFormList(pk.Species, GameInfo.Strings.types, GameInfo.Strings.forms, GameInfo.GenderSymbolASCII, pk.Context) is { } list && pk.Form < list.Length ? list[pk.Form] : N(pk.Form);
}
