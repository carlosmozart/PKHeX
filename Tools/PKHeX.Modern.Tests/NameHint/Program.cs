using System; using System.IO; using System.IO.Compression; using Avalonia; using Avalonia.Headless; using Avalonia.Threading;
using PKHeX.Core; using PKHeX.Modern; using PKHeX.Modern.Services; using PKHeX.Modern.ViewModels; using PKHeX.Modern.Views;
// Versao e idioma da Gen 1-3 pelo nome do arquivo, da pasta e do zip; selo duplo quando nao da para saber.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var dir = args[0]; int fails = 0;
void Check(string n, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {n} {extra}"); if (!ok) fails++; }
SaveBackup.Folder = Path.Combine(dir, "backups");
BankStorage.Root = Path.Combine(dir, "bank");

string Place(string src, string folder, string name)
{
    var d = Path.Combine(dir, folder); Directory.CreateDirectory(d);
    var p = Path.Combine(d, name); File.Copy(Path.Combine(dir, src), p, true); return p;
}
string Zip(string src, string zipName, string entry)
{
    var z = Path.Combine(dir, "zips", zipName); Directory.CreateDirectory(Path.GetDirectoryName(z)!);
    using (var a = ZipFile.Open(z, ZipArchiveMode.Create)) a.CreateEntryFromFile(Path.Combine(dir, src), entry);
    return z + "|" + entry;
}
void Expect(string label, string path, GameVersion ver, LanguageID? lang = null)
{
    var sav = CoreAdapter.LoadSave(path);
    var ok = sav is not null && sav.Version == ver && (lang is null || sav.Language == (int)lang);
    Check(label, ok, sav is null ? "(não abriu)" : $"{sav.Version} {(LanguageID)sav.Language}");
}

Expect("Ruby com nome neutro numa pasta neutra = par", Place("r.sav", "neutro", "jogo.sav"), GameVersion.RS);
Expect("pasta 'Pokemon Sapphire' = Sapphire", Place("r.sav", "Pokemon Sapphire", "jogo.sav"), GameVersion.S);
Expect("pasta 'Rubis' = Ruby francês", Place("r.sav", "Rubis", "jogo.sav"), GameVersion.R, LanguageID.French);
Expect("zip 'Pokemon Ruby.zip' com entrada 'main' = Ruby", Zip("r.sav", "Pokemon Ruby.zip", "main"), GameVersion.R);
Expect("entrada do zip 'Sapphire/main' = Sapphire", Zip("r.sav", "backup.zip", "Sapphire/main"), GameVersion.S);
Expect("Red com nome neutro = par Red/Blue", Place("red.sav", "neutro2", "jogo.sav"), GameVersion.RB);
Expect("pasta 'Rot' = Red alemão", Place("red.sav", "Pokemon Rot", "jogo.sav"), GameVersion.RD, LanguageID.German);
Expect("arquivo 'Pokemon Blue.sav' = Blue (GN internacional)", Place("red.sav", "neutro3", "Pokemon Blue.sav"), GameVersion.GN, LanguageID.English);
Expect("FireRed com nome neutro = par", Place("fr.sav", "neutro4", "jogo.sav"), GameVersion.FRLG);
Expect("pasta 'LeafGreen' = LeafGreen", Place("fr.sav", "LeafGreen", "jogo.sav"), GameVersion.LG);
Expect("arquivo diz mais que a pasta", Place("r.sav", "Pokemon Ruby", "Sapphire.sav"), GameVersion.S);
Expect("Crystal continua Crystal", Place("cr.sav", "Pokemon Gold", "jogo.sav"), GameVersion.C);

// Selo duplo na interface
var vm = new MainViewModel();
var win = new MainWindow { DataContext = vm, Width = 1500, Height = 950 }; win.Show();
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
vm.Open(Path.Combine(dir, "neutro", "jogo.sav")); Pump();
Check("selo duplo para Ruby/Sapphire", vm.GameArt is { IsDual: true, Species: 383, Species2: 382 } && vm.GameArt.Sprite2 is not null, vm.GameName);
vm.Open(Path.Combine(dir, "Pokemon Sapphire", "jogo.sav")); Pump();
Check("selo simples quando a versão é conhecida", vm.GameArt is { IsDual: false, Species: 382 }, vm.GameName);
vm.Open(Path.Combine(dir, "neutro", "jogo.sav")); Pump(); vm.GoHomeCommand.Execute(null); Pump();
Check("nome do par", vm.GameName == "Ruby / Sapphire", vm.GameName);
var shot = win.CaptureRenderedFrame(); shot?.Save(Path.Combine(dir, "namehint.png"));
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
