using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work = args[0]; BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
PKM Make(SaveFile sav)
{
    foreach (var enc in EncounterDatabase.SearchEncounters(sav, 25, true).Where(e => e.Species == 25))
        if (EncounterDatabase.ToEntity(sav, enc, out _) is { IsEgg: false } pk && new LegalityAnalysis(pk).Valid) return pk;
    throw new Exception("Sem Pikachu legal");
}
foreach (var file in new[] { "fr.sav", "y.sav", "cr.sav" })
{
    var path = Path.Combine(work, file); var sav = CoreAdapter.LoadSave(path)!; CoreAdapter.Activate(sav);
    var source = Make(sav);
    for (int type = 0; type < 16; type++)
    {
        string status = ""; PKM? applied = null;
        var editor = new PokemonEditorViewModel(source, "Teste", pk => applied = pk, s => status = s, sav: sav, legalMode: true);
        var before = editor.Stats.Select(s => s.IV).ToArray(); var originalType = editor.HiddenPowerType;
        editor.HiddenPowerType = type;
        bool accepted = editor.HiddenPowerType == type && editor.IsLegal;
        bool refused = originalType == editor.HiddenPowerType && editor.Stats.Select(s => s.IV).SequenceEqual(before) && status.Length > 0;
        Check(file + " tipo " + type + " legal ou recusado com motivo", accepted || refused);
        editor.ApplyCommand.Execute(null); Check(file + " aplica Pokémon legal", applied is not null && new LegalityAnalysis(applied).Valid);
        sav.SetBoxSlotAtIndex(applied!, 0, 0); File.WriteAllBytes(path, sav.Write().ToArray());
        var read = CoreAdapter.LoadSave(path)!;
        Check(file + " reabre sem alterar tipo e com checksum", read.ChecksumsValid && read.GetBoxSlotAtIndex(0, 0).HPType == editor.HiddenPowerType);
    }
    // Modo livre permite calcular todos os tipos; parte de IVs maximos para o metodo do Core.
    source.SetIVs(Enumerable.Repeat(source.MaxIV, 6).ToArray());
    for (int type = 0; type < 16; type++)
    {
        var editor = new PokemonEditorViewModel(source, "Teste", _ => { }, Console.WriteLine);
        editor.HiddenPowerType = type;
        Check(file + " cálculo livre " + type, editor.HiddenPowerType == type);
    }
}
foreach (var pk in new PKM[] { new PK1(), new PK8(), new PK9() })
    Check(pk.GetType().Name + " linha oculta", !new PokemonEditorViewModel(pk, "Teste", _ => { }, _ => { }).HasHiddenPower);
Check("Let's Go mostra tipo", new PokemonEditorViewModel(new PB7(), "Teste", _ => { }, _ => { }).HasHiddenPower);

var demo = BlankSaveFile.Get(GameVersion.B); CoreAdapter.Activate(demo); var demoPk = Make(demo);
demoPk.Move1 = (ushort)Move.HiddenPower; demoPk.Move1_PP = demoPk.GetMovePP(demoPk.Move1, 0); demo.SetBoxSlotAtIndex(demoPk, 0, 0);
var pathDemo = Path.Combine(work, "demo.sav"); File.WriteAllBytes(pathDemo, demo.Write().ToArray());
var main = new MainViewModel(new AppSettings { LegalMode = false }); main.Open(pathDemo); main.CurrentPage = main.Boxes;
main.SelectSlotAsync(main.Boxes.Slots[0]).GetAwaiter().GetResult(); var edit = main.Editor!; edit.SelectedTab = 1;
Check("golpe mostra tipo calculado", edit.Moves[0].TypeName == edit.HiddenPowerChip.Name);
edit.Stats[0].IV = 31 - edit.Stats[0].IV;
Check("tipo acompanha edição de IV", edit.Moves[0].TypeName == edit.HiddenPowerChip.Name);
var win = new MainWindow { DataContext = main, Width = 1500, Height = 1100 }; win.Show();
for (int i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
win.CaptureRenderedFrame()!.Save(Path.Combine(work, "hidden-power.png")); win.Close();
Console.WriteLine("Captura: " + work); Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
