using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

// Pesquisa (banco de dados): saves abertos, saves da pasta (inclusive zip) e bank, com filtros e abrir o resultado.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name} {extra}"); if (!ok) fails++; }
var work = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-search-" + Guid.NewGuid()); Directory.CreateDirectory(work);
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
PKM Make(SaveFile sav, ushort species)
{
    foreach (var enc in EncounterDatabase.SearchEncounters(sav, species, true).Where(e => e.Species == species))
        if (EncounterDatabase.ToEntity(sav, enc, out _) is { IsEgg: false } pk && new LegalityAnalysis(pk).Valid) return pk;
    throw new Exception("Sem encontro legal: " + species);
}
var lib = Path.Combine(work, "saves"); Directory.CreateDirectory(lib);
var black = BlankSaveFile.Get(GameVersion.B); black.OT = "Demo"; CoreAdapter.Activate(black);
int slot = 0;
foreach (ushort sp in new ushort[] { 495, 498, 501, 25 }) black.SetBoxSlotAtIndex(Make(black, sp), 0, slot++);
black.SetPartySlotAtIndex(Make(black, 570), 0);
var blackPath = Path.Combine(lib, "Black.sav"); File.WriteAllBytes(blackPath, black.Write().ToArray());

var white = BlankSaveFile.Get(GameVersion.W); white.OT = "Rival"; CoreAdapter.Activate(white);
var shiny = Make(white, 25); shiny.SetShiny(); shiny.CurrentLevel = 100; white.SetBoxSlotAtIndex(shiny, 2, 7);
var whitePath = Path.Combine(lib, "White.sav"); File.WriteAllBytes(whitePath, white.Write().ToArray());

var em = BlankSaveFile.Get(GameVersion.W2); em.OT = "Zip"; CoreAdapter.Activate(em);
em.SetBoxSlotAtIndex(Make(em, 280), 0, 0);
var emFile = Path.Combine(work, "em.sav"); File.WriteAllBytes(emFile, em.Write().ToArray());
using (var z = ZipFile.Open(Path.Combine(lib, "backup.zip"), ZipArchiveMode.Create)) z.CreateEntryFromFile(emFile, "W2/main");

var banks = BankStorage.GetBanks();
var bankBox = BankStorage.GetBoxes(banks[0])[0];
CoreAdapter.Activate(black);
BankStorage.WriteSlot(bankBox, 3, Make(black, 25));

var settings = new AppSettings { SavesFolder = lib, CheckForUpdates = false };
var vm = new MainViewModel(settings);
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 900 }; win.Show();
void Pump(int n = 10) { for (int i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
bool Wait(Func<bool> done, int ms = 20000) { var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < ms && !done()) { Pump(2); if (vm.Dialog is { } d) d.Complete(true); System.Threading.Thread.Sleep(10); } return done(); }
void Shot(string name) { Pump(15); win.UpdateLayout(); Pump(5); win.CaptureRenderedFrame()?.Save(Path.Combine(work, name + ".png")); }

vm.Open(blackPath); Pump();
Check("Pesquisa está nas páginas, com Ctrl+Shift+F", vm.Pages.Contains(vm.Search) && vm.Search.Shortcut == "Ctrl+Shift+F");
vm.CurrentPage = vm.Search;
var s = vm.Search;
Check("leu tudo", Wait(() => !s.IsBusy && s.Results.Count > 0), s.Summary);
Check("8 Pokémon: 5 no aberto, 1 no White, 1 no zip, 1 no bank", s.Results.Count == 8, s.CountText);
Check("save do zip entra", s.Results.Any(r => r.Entry.Species == "Ralts" && r.Entry.Source.Id.Contains("backup.zip|W2/main")));
Check("bank entra", s.Results.Any(r => r.Entry.Source.IsBank));
Shot("search");

s.Query = "pikachu"; Pump();
Check("texto: pikachu = 3", s.Results.Count == 3, s.CountText);
s.ShinyIndex = 1; Pump();
Check("só shiny = Pikachu do White", s.Results.Count == 1 && s.Results[0].Entry.Source.Name.Contains("Rival"), s.CountText);
s.ShinyIndex = 0; s.Query = ""; s.MinLevel = 100; Pump();
Check("nível mínimo 100 = os 3 Pikachu (evento Nv. 100)", s.Results.Count == 3 && s.Results.All(r => r.Entry.Species == "Pikachu"), s.CountText);
s.MinLevel = null; s.MaxLevel = 10; Pump();
Check("nível máximo 10 exclui os de nível alto", s.Results.Count > 0 && s.Results.All(r => r.Entry.Pkm.CurrentLevel <= 10), s.CountText);
s.ClearCommand.Execute(null); s.GenerationFilter = 4; Pump();
Check("geração 4 = nenhum", s.Results.Count == 0, s.CountText);
s.GenerationFilter = 5; Pump();
Check("geração 5 = todos", s.Results.Count == 8, s.CountText);
s.ClearCommand.Execute(null); s.Query = "#570"; Pump();
Check("#570 = Zorua da equipe", s.Results.Count == 1 && s.Results[0].Entry.Box == -1, s.CountText);
s.Query = "rival"; Pump();
Check("texto pelo treinador", s.Results.Count == 1, s.CountText);
s.SourceIndex = 1; s.Query = ""; Pump();
Check("filtro de origem (save aberto)", s.Results.Count == 5 && s.Results.All(r => r.Entry.Source.IsOpen), s.CountText);

// Abrir um resultado de um save fechado: abre a aba e vai até o slot
s.ClearCommand.Execute(null); s.ShinyIndex = 1; Pump();
s.OpenCommand.Execute(s.Results[0]);
Check("abriu o White numa aba e foi até o Pikachu", Wait(() => vm.Editor?.SpeciesName == "Pikachu" && vm.OpenSaves.Count == 2)
    && vm.CurrentPage == vm.Boxes && vm.Boxes.CurrentBox == 2, $"tabs={vm.OpenSaves.Count} page={vm.CurrentPage?.Title} box={vm.Boxes.CurrentBox}");

// Abrir resultado do bank
vm.CurrentPage = vm.Search; Wait(() => !vm.Search.IsBusy);
Check("o White aberto agora entra como aberto", s.Results.Count == 1 && s.Results[0].Entry.Source.IsOpen, s.Results.FirstOrDefault()?.SourceText ?? "");
s.ClearCommand.Execute(null); Pump();
var bankHit = s.Results.First(r => r.Entry.Source.IsBank);
s.OpenCommand.Execute(bankHit); Pump();
Check("resultado do bank abre a caixa do bank", vm.CurrentPage == vm.Bank && vm.Bank.SelectedBank == bankHit.Entry.Source.Bank);

// Alteração não salva no save aberto aparece na pesquisa
vm.CurrentPage = vm.Search; Wait(() => !vm.Search.IsBusy);
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
