using System;
using System.IO;
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
var work = args[0]; BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups"); int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
foreach (var file in new[] { "em.sav", "sa.sav", "fr.sav", "y.sav" })
{
    var sav = CoreAdapter.LoadSave(Path.Combine(work, file))!; CoreAdapter.Activate(sav); string status = ""; bool legal = true;
    var page = new GamePageViewModel((_, _, _) => Task.FromResult(true), s => { status = s; Console.WriteLine(s); }) { IsLegalMode = () => legal }; page.Load(sav); page.Tab = 8;
    Check(file + " oferece errante", page.HasRoamers && page.IsRoamersTab); var editor = page.Roamer!; var original = sav.Write().ToArray();
    if (editor.IsGen3)
    {
        editor.GenerateCommand.Execute(null); editor.Active = true; editor.ApplyCommand.Execute(null);
        Check(file + " geração e aplicação legal", new Roamer3(((SAV3)sav).LargeBlock).IsActive && page.History!.CanUndo);
        var before = sav.Write().ToArray(); int level = editor.Level; editor.Level = 1; editor.ApplyCommand.Execute(null);
        Check(file + " bloqueia nível ilegal sem alterar save", sav.Write().Span.SequenceEqual(before) && status.Contains("Modo legal")); editor.Level = level;
        editor.Pid = "FFFFFFFF"; foreach (var row in editor.IvRows) row.Value = 31; editor.ApplyCommand.Execute(null);
        Check(file + " bloqueia PID IV incompatíveis", sav.Write().Span.SequenceEqual(before) && status.Contains("PID"));
        editor.GenerateCommand.Execute(null); editor.Active = false; editor.ApplyCommand.Execute(null); var inactive = sav.Write().ToArray();
        editor.ReappearCommand.Execute(null); Check(file + " reaparece", new Roamer3(((SAV3)sav).LargeBlock).IsActive);
        page.UndoGame(); Check(file + " desfaz reaparecer byte a byte", sav.Write().Span.SequenceEqual(inactive)); page.RedoGame();
        Check(file + " informa bug correto", page.Roamer!.HasIvBug == (sav is not SAV3E));
    }
    else
    {
        legal = false; editor.ReappearCommand.Execute(null); editor.StateIndex = 4; editor.TimesEncountered = 12;
        Check("Y estado e encontros", ((SAV6XY)sav).Encount.Roamer is { RoamStatus: Roamer6State.Captured, TimesEncountered: 12 });
        page.UndoGame(); page.RedoGame(); legal = true; var species = page.Roamer!.SpeciesName;
        page.Roamer.SpeciesName = species == "Articuno" ? "Moltres" : "Articuno";
        Check("Y bloqueia ave de outro inicial", page.Roamer.SpeciesName == species && status.Contains("Modo legal"));
    }
    var output = Path.Combine(work, "out-" + file); File.WriteAllBytes(output, sav.Write().ToArray()); var read = CoreAdapter.LoadSave(output)!;
    Check(file + " reabre com checksum", read.ChecksumsValid);
    if (sav is SAV3 s3 && read is SAV3 r3) Check(file + " bytes do errante persistem", s3.LargeBlock.RoamerData.Span.SequenceEqual(r3.LargeBlock.RoamerData.Span));
    if (read is SAV6XY xy) Check("Y estado persiste", xy.Encount.Roamer.RoamStatus == Roamer6State.Captured && xy.Encount.Roamer.TimesEncountered == 12);
}
foreach (var version in new[] { GameVersion.FR, GameVersion.Y })
{
    var sav = BlankSaveFile.Get(version); CoreAdapter.Activate(sav); var page = new GamePageViewModel((_, _, _) => Task.FromResult(true), _ => { }); page.Load(sav); page.Tab = 8;
    page.Roamer!.ReappearCommand.Execute(null);
    var win = new Window { Content = new ContentControl { Content = page, Margin = new Thickness(16) }, Width = 948, Height = 620 }; win.Show();
    for (int i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } win.CaptureRenderedFrame()!.Save(Path.Combine(work, "roamer-" + version + ".png")); win.Close();
}
var unsupported = new GamePageViewModel((_, _, _) => Task.FromResult(true), _ => { }); unsupported.Load(BlankSaveFile.Get(GameVersion.OR)); Check("OR esconde errantes XY", !unsupported.HasRoamers);
Console.WriteLine("Captura: " + work); Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
