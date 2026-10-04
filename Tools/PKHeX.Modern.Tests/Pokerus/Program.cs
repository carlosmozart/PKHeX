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
var work = args[0];
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
PKM Make(SaveFile sav)
{
    foreach (var enc in EncounterDatabase.SearchEncounters(sav, 25, true).Where(e => e.Species == 25))
        if (EncounterDatabase.ToEntity(sav, enc, out _) is { IsEgg: false } pk && new LegalityAnalysis(pk).Valid) return pk;
    throw new Exception("Sem Pikachu legal");
}
foreach (var file in new[] { "fr.sav", "moon.sav" })
{
    var path = Path.Combine(work, file); var sav = CoreAdapter.LoadSave(path)!; CoreAdapter.Activate(sav);
    var source = Make(sav); source.PokerusStrain = source.PokerusDays = 0;
    var editor = new PokemonEditorViewModel(source, "Teste", pk => sav.SetBoxSlotAtIndex(pk, 0, 0), Console.WriteLine, sav: sav, legalMode: true);
    Check(file + " seção editável", editor.HasPokerus && editor.CanEditPokerus);
    editor.GivePokerusCommand.Execute(null);
    Check(file + " cópia pendente e legal", editor.IsModified && editor.IsLegal && source.PokerusStrain == 0);
    editor.ApplyCommand.Execute(null); File.WriteAllBytes(path, sav.Write().ToArray());
    var read = CoreAdapter.LoadSave(path)!; var got = read.GetBoxSlotAtIndex(0, 0);
    Check(file + " infectado após reabrir", read.ChecksumsValid && got.PokerusStrain == 1 && got.PokerusDays == Pokerus.GetMaxDuration(1));
    editor.PokerusState = 2; editor.ApplyCommand.Execute(null); File.WriteAllBytes(path, sav.Write().ToArray());
    read = CoreAdapter.LoadSave(path)!; got = read.GetBoxSlotAtIndex(0, 0);
    Check(file + " curado após reabrir", read.ChecksumsValid && got.IsPokerusCured && got.PokerusDays == 0 && new LegalityAnalysis(got).Valid);
    editor.PokerusState = 0; Check(file + " sem Pokérus", editor.PokerusStrain == 0 && editor.PokerusDays == 0);
}
foreach (var pk in new PKM[] { new PK1(), new PB7(), new PK9(), new PA9() })
    Check(pk.GetType().Name + " seção oculta", !new PokemonEditorViewModel(pk, "Teste", _ => { }, _ => { }).HasPokerus);
var crystal = BlankSaveFile.Get(GameVersion.C); CoreAdapter.Activate(crystal);
var gb = new PokemonEditorViewModel(Make(crystal), "Teste", _ => { }, Console.WriteLine, sav: crystal, legalMode: true);
gb.PokerusState = 1; gb.PokerusStrain = 15; gb.PokerusDays = 9;
Check("Gen2 limita cepa e duração", gb.PokerusStrain == 8 && gb.PokerusDays <= Pokerus.GetMaxDuration(8) && gb.IsLegal);
var immune = new PokemonEditorViewModel(new PA8 { Species = 25 }, "Teste", _ => { }, Console.WriteLine, legalMode: true);
immune.GivePokerusCommand.Execute(null);
Check("PLA bloqueia sem visita a outro jogo", immune.HasPokerus && !immune.CanEditPokerus && immune.PokerusStrain == 0);

// Captura sintetica; nenhum dado dos saves pessoais aparece.
var demo = BlankSaveFile.Get(GameVersion.B); CoreAdapter.Activate(demo); demo.SetBoxSlotAtIndex(Make(demo), 0, 0);
var demoPath = Path.Combine(work, "demo.sav"); File.WriteAllBytes(demoPath, demo.Write().ToArray());
var main = new MainViewModel(); main.Open(demoPath); main.CurrentPage = main.Boxes; main.SelectSlotAsync(main.Boxes.Slots[0]).GetAwaiter().GetResult(); main.Editor!.SelectedTab = 5;
var win = new MainWindow { DataContext = main, Width = 1440, Height = 1000 }; win.Show();
for (int i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
win.CaptureRenderedFrame()!.Save(Path.Combine(work, "pokerus.png")); win.Close();
Console.WriteLine("Captura: " + work); Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
