using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work = args[0]; BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups"); int fails = 0, confirms = 0; bool accept = true;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
var sav = (SAV9SV)CoreAdapter.LoadSave(Path.Combine(work, "sv.sav"))!; CoreAdapter.Activate(sav);
var page = new GamePageViewModel((_, _, _) => { confirms++; return Task.FromResult(accept); }, Console.WriteLine); page.Load(sav); page.Tab = 10;
Check("SV oferece roupas", page.HasFashion); var fashion = page.Fashion!; var original = sav.Write().ToArray();
var row = fashion.Rows.First(); uint id = row.Id; row.Unlocked = false; row.Unlocked = true; Check("libera peça", row.Unlocked);
fashion.Query = id.ToString(); Check("busca ID", fashion.Rows.Count == 1); fashion.Query = "Eyewear"; Check("busca nome categoria inglesa", fashion.Rows.Count > 1); fashion.Query = "";
// Ensure bulk changes several pieces even if the fixture has an already unlocked inventory.
foreach (var item in fashion.Rows.Take(3)) item.Unlocked = false;
var before = sav.Write().ToArray(); int steps = page.History!.Count; accept = false; await fashion.UnlockAsync(false);
Check("cancelar preserva bytes", sav.Write().Span.SequenceEqual(before)); accept = true; await fashion.UnlockAsync(false);
Check("categoria inteira um passo", fashion.Rows.All(r => r.Unlocked) && page.History.Count == steps + 1 && confirms == 2);
page.UndoGame(); Check("desfaz categoria byte a byte", sav.Write().Span.SequenceEqual(before)); page.RedoGame();
fashion = page.Fashion!; fashion.CategoryIndex = 2; await fashion.UnlockAsync(true);
Check("libera catálogo completo", fashion.Categories.All(c => { fashion.CategoryIndex = fashion.Categories.ToList().IndexOf(c); return fashion.Rows.All(r => r.Unlocked); }));
string output = Path.Combine(work, "out-sv.sav"); File.WriteAllBytes(output, sav.Write().ToArray()); var read = (SAV9SV)CoreAdapter.LoadSave(output)!;
Check("reabre com checksums e peça", read.ChecksumsValid && FashionItem9.GetArray(read.Blocks.GetBlock(SaveBlockAccessor9SV.KFashionUnlockedEyewear).Data).Any(i => i.Value == id));
foreach (byte gender in new byte[] { 0, 1 })
{
    var demo = (SAV9SV)BlankSaveFile.Get(GameVersion.SL); demo.Gender = gender; CoreAdapter.Activate(demo); page.Load(demo); page.Tab = 10; page.Fashion!.CategoryIndex = 6;
    Check("Jogo disponível mesmo sem flags nomeadas " + gender, page.IsAvailable);
    Check("catálogo conforme gênero " + gender, page.Fashion.Rows.Any(r => r.Id == (gender == 0 ? 7004 : 7000)) && !page.Fashion.Rows.Any(r => r.Id == (gender == 0 ? 7000 : 7004)));
    if (gender == 0) { var win = new Window { Content = new ContentControl { Content = page, Margin = new Thickness(16) }, Width = 948, Height = 620 }; win.Show(); for (int i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } win.CaptureRenderedFrame()!.Save(Path.Combine(work, "fashion.png")); win.Close(); }
}
page.Load(BlankSaveFile.Get(GameVersion.ZA)); Check("ZA esconde roupas opcionais", !page.HasFashion);
Console.WriteLine("Captura: " + work); Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
