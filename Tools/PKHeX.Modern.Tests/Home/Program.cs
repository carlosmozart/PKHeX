using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.Theme;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

// Tela inicial (Inicio) e temas completos, com saves sinteticos.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
var work = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-home-" + Guid.NewGuid()); Directory.CreateDirectory(work);
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
PKM Make(SaveFile sav, ushort species)
{
    foreach (var enc in EncounterDatabase.SearchEncounters(sav, species, true).Where(e => e.Species == species))
        if (EncounterDatabase.ToEntity(sav, enc, out _) is { IsEgg: false } pk && new LegalityAnalysis(pk).Valid) return pk;
    throw new Exception("Sem encontro legal: " + species);
}
var lib = Path.Combine(work, "saves"); Directory.CreateDirectory(lib);
var sav = BlankSaveFile.Get(GameVersion.B); sav.OT = "Demo"; CoreAdapter.Activate(sav);
int slot = 0;
foreach (ushort sp in new ushort[] { 495, 498, 501, 25, 570, 349 }) { var pk = Make(sav, sp); sav.SetPartySlotAtIndex(pk, slot); sav.SetBoxSlotAtIndex(pk.Clone(), 0, slot++); }
var black = Path.Combine(lib, "Black.sav"); File.WriteAllBytes(black, sav.Write().ToArray());
var white = Path.Combine(lib, "White.sav"); var w = BlankSaveFile.Get(GameVersion.W); w.OT = "Demo"; File.WriteAllBytes(white, w.Write().ToArray());

var settings = new AppSettings { SavesFolder = lib, CheckForUpdates = false };
var vm = new MainViewModel(settings);
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 900 }; win.Show();
void Pump(int n = 10) { for (int i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
void Shot(string name) { Pump(15); win.UpdateLayout(); Pump(5); win.CaptureRenderedFrame()?.Save(Path.Combine(work, name + ".png")); }

vm.Open(white); Pump();
vm.Open(black); Pump();
Check("save recém-aberto cai no Início", vm.CurrentPage == vm.Home && vm.IsHomeActive && !vm.ShowEditorPanel);
Check("Início: jogo e treinador", vm.Home.GameName.Contains("Black") && vm.Home.Trainer.StartsWith("Demo"));
Check("Início: equipe com 6", vm.Home.Party.Count == 6 && vm.Home.Party[3].Name == "Pikachu");
Check("Início: um atalho por página", vm.Home.Tiles.Count == vm.Pages.Count && vm.Home.Tiles[0].Title == "Caixas" && vm.Home.Tiles[0].Detail.Contains("6 Pokémon"));
Check("Início: recente sem o save atual", vm.Home.Recent.Count == 1 && vm.Home.Recent[0].Game.Contains("White"));
Check("recentes nas preferências", settings.RecentSaves.Count == 2 && settings.RecentSaves[0].EndsWith("Black.sav"));
vm.Home.Tiles[0].Command.Execute(null); Pump();
Check("atalho abre a página", vm.CurrentPage == vm.Boxes && !vm.IsHomeActive);
vm.GoToPage(-1); Pump();
Check("Ctrl+0 volta ao Início", vm.CurrentPage == vm.Home);
vm.Home.Party[0].OpenCommand.Execute(null); Pump();
Check("clique na equipe abre a Equipe", vm.CurrentPage == vm.Party);
vm.GoToPage(-1); Pump();
Shot("home_default");

// Temas: cada um troca os pinceis, os raios e a fonte; a cor de destaque segue a sugestao do tema.
Color Card() => Application.Current!.TryGetResource("CardBackground", ThemeVariant.Dark, out var v) && v is ISolidColorBrush b ? b.Color : default;
CornerRadius Radius() => Application.Current!.TryGetResource("CardRadius", null, out var v) && v is CornerRadius r ? r : default;
foreach (var opt in vm.ThemeOptions)
{
    opt.SelectCommand.Execute(null); Pump();
    var t = opt.Preset;
    Check($"tema {t.Key}: cartão, raio e destaque", Card() == Color.Parse(t.Dark.Card) && Radius().TopLeft == Math.Round(12 * t.RadiusScale)
        && settings.ThemeKey == t.Key && settings.AccentColor == t.AccentKey && vm.ThemeText.Contains(t.ShortName));
    Shot("home_" + t.Key);
    if (t.Key == "pixel") { Application.Current!.RequestedThemeVariant = ThemeVariant.Light; Shot("home_pixel_light"); Application.Current.RequestedThemeVariant = ThemeVariant.Dark; }
}
vm.ThemeOptions.First(o => o.Preset.Key == "default").SelectCommand.Execute(null); Pump();
Check("volta ao Padrão", Card() == Color.Parse(AppTheme.Default.Dark.Card) && Radius().TopLeft == 12);
Console.WriteLine("Capturas: " + work);
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
