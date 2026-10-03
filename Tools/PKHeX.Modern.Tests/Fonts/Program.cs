using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.Theme;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

// Seletor de fonte (⚙ › Fonte): independente do tema, salvo nas preferencias. Save sintetico (Black).
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name} {extra}"); if (!ok) fails++; }
var work = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-fonts-" + Guid.NewGuid()); Directory.CreateDirectory(work);
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
var path = Path.Combine(work, "Black.sav");
File.WriteAllBytes(path, BlankSaveFile.Get(GameVersion.B).Write().ToArray());

var settings = new AppSettings { CheckForUpdates = false };
var vm = new MainViewModel(settings);
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 900 }; win.Show();
void Pump(int n = 12) { for (int i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
vm.Open(path); Pump();
string Used() => Application.Current!.Resources["AppFontFamily"] is FontFamily f ? f.Name : "?";
string NavFont() => win.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Caixas").FontFamily.Name;
FontOptionViewModel Opt(string key) => vm.FontOptions.First(o => o.Font.Key == key);

Check("começa com a fonte do tema (Inter no Padrão)", Opt("theme").IsSelected && NavFont().Contains("Inter"), NavFont());
Opt("pixelify").SelectCommand.Execute(null); Pump();
Check("Pixelify no tema Padrão", NavFont().Contains("Pixelify") && settings.FontKey == "pixelify" && Opt("pixelify").IsSelected && !Opt("theme").IsSelected, $"{NavFont()} / {settings.FontKey}");
var small = win.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Ctrl+1");
Check("legenda pequena continua na Inter", small.FontFamily.Name.Contains("Inter"), small.FontFamily.Name);
vm.ThemeOptions.First(t => t.Preset.Key == "pixel").SelectCommand.Execute(null); Pump();
Opt("inter").SelectCommand.Execute(null); Pump();
Check("Inter no tema Pixel (fonte independente do tema)", NavFont().Contains("Inter") && settings.FontKey == "inter", NavFont());
Opt("theme").SelectCommand.Execute(null); Pump();
Check("Do tema volta à Pixelify no tema Pixel", NavFont().Contains("Pixelify") && settings.FontKey is null, NavFont());
vm.ThemeOptions.First(t => t.Preset.Key == "default").SelectCommand.Execute(null); Pump();
Check("Do tema segue a troca de tema (Inter no Padrão)", NavFont().Contains("Inter"), NavFont());

// Pokemon GB/GBC: letras largas, so nos titulos
vm.CurrentPage = vm.Boxes; Pump();
Opt("pokemongb").SelectCommand.Execute(null); Pump();
var boxTitle = win.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("title") && t.Text == vm.Boxes.BoxName);
Check("Pokémon GB nos títulos", boxTitle.FontFamily.Name.Contains("Pokemon Classic"), boxTitle.FontFamily.Name);
Check("Pokémon GB: texto normal fica na Inter", NavFont().Contains("Inter"), NavFont());
var logo = win.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "PKHeX");
Check("Pokémon GB no logo", logo.FontFamily.Name.Contains("Pokemon Classic"), logo.FontFamily.Name);
win.CaptureRenderedFrame()?.Save(Path.Combine(work, "pokemongb.png"));
Opt("theme").SelectCommand.Execute(null); Pump();
Check("volta ao tema: título na Inter", win.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("title") && t.Text == vm.Boxes.BoxName).FontFamily.Name.Contains("Inter"));

// menu ⚙ com a secao Fonte
var prefs = win.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PrefsButton");
prefs.Flyout!.ShowAt(prefs); Pump(20);
var items = win.GetVisualDescendants().Concat(TopLevel.GetTopLevel(win)!.GetVisualDescendants()).OfType<Button>().Count(b => b.DataContext is FontOptionViewModel);
win.CaptureRenderedFrame()?.Save(Path.Combine(work, "prefs.png"));
Check("menu ⚙ lista as fontes", items >= 3 || vm.FontOptions.Count >= 3, $"{items} botões");
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
