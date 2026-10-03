using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

// Salvar com o cursor ainda num campo (Nivel, PID, dinheiro): o valor digitado precisa entrar no arquivo.
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
var work = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-savefocus-" + Guid.NewGuid()); Directory.CreateDirectory(work);
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
var sav = BlankSaveFile.Get(GameVersion.B); sav.OT = "Demo"; CoreAdapter.Activate(sav);
var enc = EncounterDatabase.SearchEncounters(sav, 25, true).First(e => e.Species == 25);
var pk = EncounterDatabase.ToEntity(sav, enc, out _)!; sav.SetBoxSlotAtIndex(pk, 0, 0);
var path = Path.Combine(work, "black.sav"); File.WriteAllBytes(path, sav.Write().ToArray());

var vm = new MainViewModel(new AppSettings { CheckForUpdates = false, LegalMode = false });
var win = new MainWindow { DataContext = vm, Width = 1500, Height = 1000 }; win.Show();
void Pump(int n = 10) { for (int i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
vm.Open(path); Pump(); vm.CurrentPage = vm.Boxes; Pump();
vm.SelectSlotAsync(vm.Boxes.Slots[0]).GetAwaiter().GetResult(); Pump();

TextBox LevelBox() => win.GetVisualDescendants().OfType<NumericUpDown>().First(n => n.IsEffectivelyVisible && n.Maximum == 100 && n.Minimum == 1)
    .GetVisualDescendants().OfType<TextBox>().First();
void TypeInto(TextBox box, string text) { box.Focus(); Pump(); box.SelectAll(); win.KeyTextInput(text); Pump(); }
int SavedLevel() => CoreAdapter.LoadSave(path)!.GetBoxSlotAtIndex(0, 0).CurrentLevel;

// 1) Ctrl+S com o cursor no campo Nivel.
TypeInto(LevelBox(), "55");
win.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s"); win.KeyRelease(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s"); Pump(20);
Check("Ctrl+S com cursor no Nível grava o valor digitado", SavedLevel() == 55);

// 2) Clique em Salvar com o cursor no campo Nivel.
TypeInto(LevelBox(), "66");
var save = win.GetVisualDescendants().OfType<Button>().First(b => b.Content?.ToString() == "💾  Salvar" && b.IsEffectivelyVisible);
var point = save.TranslatePoint(new Point(save.Bounds.Width / 2, save.Bounds.Height / 2), win)!.Value;
win.MouseDown(point, MouseButton.Left); win.MouseUp(point, MouseButton.Left); Pump(20);
Check("clique em Salvar com cursor no Nível grava o valor digitado", SavedLevel() == 66);

// 3) Treinador: dinheiro digitado e Ctrl+S.
vm.CurrentPage = vm.Pages.First(p => p is TrainerPageViewModel); Pump(20);
var money = win.GetVisualDescendants().OfType<NumericUpDown>().First(n => n.IsEffectivelyVisible && n.FormatString == "N0").GetVisualDescendants().OfType<TextBox>().First();
TypeInto(money, "12345");
win.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s"); win.KeyRelease(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s"); Pump(20);
Check("Ctrl+S com cursor no Dinheiro grava o valor", CoreAdapter.LoadSave(path)!.Money == 12345);
// 4) Mochila: mudar um item sem "Aplicar mochila" e clicar em Salvar.
var bagPage = vm.Pages.OfType<BagPageViewModel>().First();
vm.CurrentPage = bagPage; Pump(20);
var pouch = bagPage.Pouches.First(p => p.Items.Count > 0 && p.Options.Count > 1);
var first = pouch.Items[0]; var option = pouch.Options.First(o => o.Id != 0);
first.Selected = option; first.Count = 7; Pump();
Check("mexer na mochila marca alterações", vm.IsDirty && bagPage.HasPendingChanges);
win.UpdateLayout(); Pump();
// O caso do bug: o mouse parado no Salvar abre a dica dele; o clique precisa chegar ao botao, nao a dica.
var saveBtn = win.GetVisualDescendants().OfType<Button>().First(b => b.Content?.ToString() == "💾  Salvar" && b.IsEffectivelyVisible);
var pt2 = saveBtn.TranslatePoint(new Point(saveBtn.Bounds.Width / 2, saveBtn.Bounds.Height / 2), win)!.Value;
win.MouseMove(pt2); ToolTip.SetIsOpen(saveBtn, true); Pump(20);
Check("dica do Salvar aberta não cobre o botão", win.InputHitTest(pt2) is Visual hit && (hit == saveBtn || hit.GetVisualAncestors().Contains(saveBtn)));
win.MouseDown(pt2, MouseButton.Left); win.MouseUp(pt2, MouseButton.Left); Pump(20);
var reloaded = CoreAdapter.GetBag(CoreAdapter.LoadSave(path)!);
var saved = reloaded.Pouches.SelectMany(p => p.Items).FirstOrDefault(i => i.Index == option.Id);
Check("Salvar grava a mochila sem Aplicar mochila", saved is { Count: 7 } && !vm.IsDirty && !bagPage.HasPendingChanges);
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
