using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

// Interface em ingles: cada pagina e renderizada e os textos visiveis que ficaram em portugues sao listados.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
var work = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-lang-" + Guid.NewGuid()); Directory.CreateDirectory(work);
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");

// Unidade: exato, modelo, frases juntadas, prefixo de icone e texto sem traducao.
Check("carrega en", Loc.Load(Loc.English) && Loc.IsTranslating);
Check("exato", Loc.T("Caixas") == "Boxes");
Check("modelo", Loc.T("Pikachu gravado em caixa 1, slot 2. Lembre-se de exportar o save.") == "Pikachu written to box 1, slot 2. Remember to export the save.");
Check("modelo aninhado", Loc.T("Pikachu ({0})".Replace("{0}", "x")) == "Pikachu (x)");
Check("nome do jogo intacto", Loc.T("Pikachu") == "Pikachu" && Loc.T("Thunderbolt") == "Thunderbolt");
Check("frases juntadas", Loc.T("Nada para desfazer (Ctrl+Z). Nada para refazer (Ctrl+Y).") is var j && !j.Contains("Nada"));
Loc.Hook();

PKM Make(SaveFile sav, ushort species)
{
    foreach (var enc in EncounterDatabase.SearchEncounters(sav, species, true).Where(e => e.Species == species))
        if (EncounterDatabase.ToEntity(sav, enc, out _) is { IsEgg: false } pk && new LegalityAnalysis(pk).Valid) return pk;
    throw new Exception("Sem encontro legal: " + species);
}
var sav = BlankSaveFile.Get(GameVersion.B); sav.OT = "Demo"; CoreAdapter.Activate(sav);
int slot = 0;
foreach (ushort sp in new ushort[] { 495, 498, 501, 25, 570, 349 }) { var pk = Make(sav, sp); sav.SetPartySlotAtIndex(pk, slot); sav.SetBoxSlotAtIndex(pk.Clone(), 0, slot++); }
var lib = Path.Combine(work, "saves"); Directory.CreateDirectory(lib);
var path = Path.Combine(lib, "Black.sav"); File.WriteAllBytes(path, sav.Write().ToArray());

var vm = new MainViewModel(new AppSettings { SavesFolder = lib, UiLanguage = Loc.English });
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 900 }; win.Show();
void Pump(int n = 10) { for (int i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
var pt = new Regex(@"[ãõçâêôáíóúàé]|\b(de|da|não|para|com|uma|os|em|ou|que|caixa|equipe|golpes?|treinador|mochila|salvar|abrir|clique|todos|todas|nenhum|selecione|escolha|arquivo|pasta|novo|nova|voltar|excluir|ordenar|buscar|ajuda|atualizar|vista|capturada)\b", RegexOptions.IgnoreCase);
var gameWords = new Regex(@"Pok[ée]\w*|Poké", RegexOptions.IgnoreCase);
bool IsPortuguese(string t) => pt.IsMatch(gameWords.Replace(t, ""));
var missing = new SortedDictionary<string, string>();
void Scan(string page)
{
    Pump(20); win.UpdateLayout(); Pump(5);
    foreach (var v in win.GetVisualDescendants())
    {
        var texts = new List<string?>();
        if (v is TextBlock tb && tb.IsEffectivelyVisible) texts.Add(tb.Text);
        if (v is Control c && ToolTip.GetTip(c) is string tip) texts.Add(tip);
        if (v is TextBox box) texts.Add(box.Watermark);
        if (v is ComboBox cb) texts.Add(cb.PlaceholderText);
        foreach (var t in texts)
            if (!string.IsNullOrWhiteSpace(t) && IsPortuguese(t) && !missing.ContainsKey(t) && !t.Contains("Português") && !t.StartsWith("¡")) missing[t] = page;
    }
}
Pump(); Scan("Save Manager");
vm.Open(path); Pump(); Scan("Início");
foreach (var page in vm.Pages)
{
    vm.CurrentPage = page; Scan(page.Title);
    if (page == vm.Boxes)
    {
        vm.SelectSlotAsync(vm.Boxes.Slots[5]).GetAwaiter().GetResult();
        for (int t = 0; t < 7; t++) { vm.Editor!.SelectedTab = t; Scan("Editor " + t); }
        vm.Editor!.SelectedTab = 0;
        win.CaptureRenderedFrame()?.Save(Path.Combine(work, "boxes_en.png"));
    }
}
vm.CurrentPage = vm.Pokedex; vm.Pokedex.RefreshAsync().GetAwaiter().GetResult(); Scan("Pokédex");
// Novidades (aba 1) e o changelog: fica em portugues, com um aviso; so a aba Funcoes e Sobre sao conferidas.
vm.OpenHelp(); foreach (var t in new[] { 0, 2 }) { vm.Help.SelectedTab = t; Scan("Help " + t); }
win.CaptureRenderedFrame()?.Save(Path.Combine(work, "help_en.png"));
// Campo editavel nunca e traduzido: um apelido em portugues continua igual.
vm.CloseHelpCommand.Execute(null); vm.CurrentPage = vm.Boxes; vm.SelectSlotAsync(vm.Boxes.Slots[0]).GetAwaiter().GetResult(); Pump();
var nick = win.GetVisualDescendants().OfType<TextBox>().First(b => b.Text == "Snivy" && b.IsEffectivelyVisible);
nick.Text = "Caixas"; Pump();
var nickBox = nick.Text == "Caixas" ? nick : null;
Check("TextBox não é traduzido", nickBox is not null);

// Special Game tabs and QR are shared by desktop and Android.
vm.CurrentPage = vm.Game; vm.Game.Tab = 5; Scan("Stored cards");
var originalContent = win.Content;
foreach (var version in new[] { GameVersion.SH, GameVersion.SL, GameVersion.E, GameVersion.FR, GameVersion.Y, GameVersion.ZA })
{
    var demo = BlankSaveFile.Get(version); CoreAdapter.Activate(demo);
    var page = new GamePageViewModel((_, _, _) => System.Threading.Tasks.Task.FromResult(true), _ => { }); page.Load(demo); page.Tab = version == GameVersion.E ? 7 : version is GameVersion.FR or GameVersion.Y ? 8 : 6;
    win.Content = new ContentControl { Content = page, Margin = new Thickness(16) }; Scan("Raids " + version);
    if (version == GameVersion.ZA) { page.Tab = 9; page.Donuts!.GenerateCommand.Execute(null); Scan("Donuts"); }
    if (version == GameVersion.SL) { page.RaidRegion = page.RaidRegions.Last(); Scan("Seven-star records"); }
}
win.Content = originalContent; vm.ShowPokemonQr(new PK7 { Species = 25 }); Scan("QR"); vm.CloseQr();

File.WriteAllLines(Path.Combine(work, "missing.txt"), missing.Select(m => $"{m.Value}\t{m.Key.Replace("\n", "\n")}"));
Console.WriteLine($"Textos em português na tela: {missing.Count} (lista em {Path.Combine(work, "missing.txt")})");
foreach (var m in missing.Take(400)) Console.WriteLine($"  [{m.Value}] {m.Key.Replace("\n", "\n")}");
Check("sem textos em português na interface", missing.Count == 0);
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
