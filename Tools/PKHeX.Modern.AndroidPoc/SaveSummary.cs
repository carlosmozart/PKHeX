using PKHeX.Core;

namespace PKHeX.Modern.AndroidPoc;

public static class SaveSummary
{
    public static string Describe(byte[] data, string name)
    {
        if (!SaveUtil.TryGetSaveFile(data, out var save))
            throw new InvalidDataException("Arquivo não reconhecido como save. ZIPs não são aceitos neste protótipo.");
        var strings = GameInfo.GetStrings("en");
        var game = save.Version switch
        {
            GameVersion.RB => "Red / Blue",
            GameVersion.GS => "Gold / Silver",
            GameVersion.RS => "Ruby / Sapphire",
            GameVersion.FRLG => "FireRed / LeafGreen",
            _ => GameInfo.GetVersionName(save.Version),
        };
        var party = save.PartyData;
        var lines = new List<string> { $"Arquivo: {name}", $"Jogo: {game}", $"Treinador: {save.OT}", "", "Equipe:" };
        for (int i = 0; i < party.Count; i++)
        {
            var pk = party[i];
            var species = pk.Species < strings.specieslist.Length ? strings.specieslist[pk.Species] : $"Espécie {pk.Species}";
            lines.Add($"{i + 1}. {species} — Nv. {pk.CurrentLevel}");
        }
        if (party.Count == 0)
            lines.Add("Equipe vazia.");
        return string.Join(Environment.NewLine, lines);
    }
}
