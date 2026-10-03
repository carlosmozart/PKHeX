using PKHeX.Core;
using PKHeX.Modern.AndroidPoc;

var save = BlankSaveFile.Get(GameVersion.B);
save.OT = "ANDROID";
var pk = save.BlankPKM;
pk.Species = 25;
pk.CurrentLevel = 42;
save.PartyData = new List<PKM> { pk };
var data = save.Write().ToArray();
if (args is ["--export", var exportPath])
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(exportPath))!);
    File.WriteAllBytes(exportPath, data);
    Console.WriteLine("Save sintetico: " + Path.GetFullPath(exportPath));
}
var summary = SaveSummary.Describe(data, "synthetic-black.sav");
foreach (var expected in new[] { "Black", "ANDROID", "Pikachu", "Nv. 42" })
    if (!summary.Contains(expected, StringComparison.Ordinal))
        throw new Exception($"Ausente no resumo: {expected}\n{summary}");
save.PartyData = new List<PKM>();
if (!SaveSummary.Describe(save.Write().ToArray(), "empty.sav").Contains("Equipe vazia."))
    throw new Exception("Equipe vazia não reconhecida.");
try
{
    SaveSummary.Describe(new byte[123], "invalid.bin");
    throw new Exception("Arquivo inválido foi aceito.");
}
catch (InvalidDataException) { }
Console.WriteLine("OK: save sintético Black, treinador, Pikachu nível 42, equipe vazia e arquivo inválido.");
