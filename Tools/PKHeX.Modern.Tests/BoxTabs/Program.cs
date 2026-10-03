using System;
using System.IO;
using System.Linq;
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

// Abas das caixas: centralizadas quando cabem; quando nao cabem, rolam ate a caixa atual. Save sintetico (Black, 24 caixas).
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok, string extra = "") { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name} {extra}"); if (!ok) fails++; }
var work = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "pkhex-boxtabs-" + Guid.NewGuid()); Directory.CreateDirectory(work);
BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
var path = Path.Combine(work, "Black.sav");
File.WriteAllBytes(path, BlankSaveFile.Get(GameVersion.B).Write().ToArray());

var vm = new MainViewModel(new AppSettings { CheckForUpdates = false });
var win = new MainWindow { DataContext = vm, Width = 1440, Height = 900 }; win.Show();
void Pump(int n = 12) { for (int i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
vm.Open(path); Pump();
vm.CurrentPage = vm.Boxes; Pump();

ScrollViewer Tabs() => win.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.GetVisualDescendants().OfType<Button>().Any(b => b.Classes.Contains("boxTab")));
Button Current() => Tabs().GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("current"));
double CenterX(Visual v) => v.TranslatePoint(new Point(v.Bounds.Width / 2, 0), win)!.Value.X;
TextBlock Title() => win.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("title") && t.Text == vm.Boxes.BoxName);

// 1) Janela estreita: 24 caixas nao cabem
var s = Tabs();
Check("24 caixas não cabem (rola)", s.Extent.Width > s.Viewport.Width + 1, $"extent {s.Extent.Width:0} viewport {s.Viewport.Width:0}");
vm.Boxes.CurrentBox = 15; Pump(20);
var cur = Current(); var sv = Tabs();
var mid = CenterX(cur); var viewMid = CenterX(sv);
Check("caixa 16 rolada para o centro", Math.Abs(mid - viewMid) < 40, $"aba {mid:0} centro {viewMid:0} offset {sv.Offset.X:0}");
vm.Boxes.CurrentBox = 23; Pump(20);
sv = Tabs(); cur = Current();
Check("última caixa visível (rolagem no fim)", cur.TranslatePoint(new Point(cur.Bounds.Width, 0), sv)!.Value.X <= sv.Viewport.Width + 1, $"offset {sv.Offset.X:0}");
vm.Boxes.CurrentBox = 0; Pump(20);
Check("primeira caixa volta ao começo", Tabs().Offset.X < 1, $"offset {Tabs().Offset.X:0}");
win.CaptureRenderedFrame()?.Save(Path.Combine(work, "narrow.png"));

// 2) Janela larga: tudo cabe e fica centralizado com o título da caixa
win.Width = 2600; Pump(20);
sv = Tabs();
var items = sv.GetVisualDescendants().OfType<ItemsControl>().First();
Check("24 caixas cabem na janela larga", sv.Extent.Width <= sv.Viewport.Width + 1, $"extent {sv.Extent.Width:0} viewport {sv.Viewport.Width:0}");
Check("abas centralizadas com o título", Math.Abs(CenterX(items) - CenterX(Title())) < 3, $"abas {CenterX(items):0} título {CenterX(Title()):0}");
win.CaptureRenderedFrame()?.Save(Path.Combine(work, "wide.png"));
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
return fails == 0 ? 0 : 1;
