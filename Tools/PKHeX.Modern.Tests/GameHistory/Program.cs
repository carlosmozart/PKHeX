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
var work = args[0]; BankStorage.Root = Path.Combine(work, "bank"); SaveBackup.Folder = Path.Combine(work, "backups");
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
var vm = new MainViewModel(new AppSettings { CheckForUpdates = false });
void Pump() { for (int i = 0; i < 8; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
void Accept() { Pump(); vm.Dialog?.Complete(true); Pump(); }
vm.Open(Path.Combine(work, "em.sav")); vm.CurrentPage = vm.Game; var sav = vm.ActiveTab!.Sav;
var before = sav.Write().ToArray(); var flag = vm.Game.FlagRows[0]; flag.IsSet = !flag.IsSet; vm.Game.Tab = 1;
var value = vm.Game.WorkRows[0]; value.ValueNumber = value.Value == value.Max ? value.Min : value.Value + 1;
vm.Game.ShortcutRows.First(r => r.Name.Contains("Pokéblocks")).ApplyCommand.Execute(null); Accept();
Check("flag valor atalho são três passos", vm.Game.History!.Count == 3 && vm.UndoCommand.CanExecute(null) && vm.IsDirty);
Check("tooltip descreve próximo passo", vm.UndoTip.Contains("Atalho"));
var changed = sav.Write().ToArray();
for (int i = 0; i < 3; i++) vm.UndoCommand.Execute(null);
Check("Emerald desfaz byte a byte", sav.Write().Span.SequenceEqual(before));
for (int i = 0; i < 3; i++) vm.RedoCommand.Execute(null);
Check("Emerald refaz byte a byte", sav.Write().Span.SequenceEqual(changed));
Check("botões Jogo disponíveis", vm.ShowGameHistoryActions && !vm.ShowSlotActions);
vm.Game.Tab = 0; vm.Game.Query = "#10"; vm.Game.ShowUnnamed = true;
int count = vm.Game.History.Count; vm.Game.SetVisibleCommand.Execute("1"); Accept();
Check("flags em lote um passo", vm.Game.History.Count <= count + 1);
vm.UndoCommand.Execute(null); vm.Game.FlagRows[0].IsSet = !vm.Game.FlagRows[0].IsSet;
Check("nova edição limpa refazer", !vm.Game.History.CanRedo);
var slot = sav.GetBoxSlotAtIndex(0, 0); slot.HeldItem = 1; sav.SetBoxSlotAtIndex(slot, 0, 0);
vm.UndoCommand.Execute(null); Check("undo Jogo preserva caixa", sav.GetBoxSlotAtIndex(0, 0).HeldItem == 1);
vm.Export(Path.Combine(work, "em-out.sav")); Check("Emerald reabre com checksum", CoreAdapter.LoadSave(Path.Combine(work, "em-out.sav"))!.ChecksumsValid);

foreach (var file in new[] { "or.sav", "sh.sav" })
{
    vm.Open(Path.Combine(work, file)); vm.CurrentPage = vm.Game; sav = vm.ActiveTab!.Sav; before = sav.Write().ToArray();
    if (file == "or.sav")
    {
        vm.Game.Tab = 4; vm.Game.SelectedFameMember = vm.Game.FameRows[0].Members[0]; vm.Game.FameSpecies = "Mew"; vm.Game.FameLevel = 50; vm.Game.ApplyFameCommand.Execute(null);
    }
    else { vm.Game.Tab = 6; vm.Game.SetAllRaidsAsync(true); Accept(); }
    Check(file + " alteração registrada", vm.Game.History!.Count == 1); changed = sav.Write().ToArray();
    vm.UndoCommand.Execute(null); Check(file + " desfaz byte a byte", sav.Write().Span.SequenceEqual(before));
    vm.RedoCommand.Execute(null); Check(file + " refaz byte a byte", sav.Write().Span.SequenceEqual(changed));
    Check(file + " exporta e reabre checksum", vm.Export(Path.Combine(work, "out-" + file)) && CoreAdapter.LoadSave(Path.Combine(work, "out-" + file))!.ChecksumsValid);
}
vm.Open(Path.Combine(work, "em.sav")); vm.CurrentPage = vm.Game; Check("troca de save limpa histórico Jogo", !vm.Game.History!.CanUndo);
vm.Game.Query = ""; vm.Game.ShowUnnamed = true; vm.Game.Tab = 0;
vm.Game.FlagRows[0].IsSet = !vm.Game.FlagRows[0].IsSet;
vm.CurrentPage = vm.Boxes; Check("outras páginas usam slots", !vm.UndoCommand.CanExecute(null)); vm.CurrentPage = vm.Game; Check("volta para histórico Jogo", vm.UndoCommand.CanExecute(null));
var synthetic = (SAV9SV)CoreAdapter.LoadSave(Path.Combine(work, "sv.sav"))!; var memory = new PKHeX.Modern.Services.GameHistory(synthetic);
var flagBlock = synthetic.AllBlocks.First(b => b.Type is SCTypeCode.Bool1 or SCTypeCode.Bool2); var oldType = flagBlock.Type;
memory.Execute("Flag SV", () => flagBlock.ChangeBooleanType(oldType == SCTypeCode.Bool1 ? SCTypeCode.Bool2 : SCTypeCode.Bool1)); memory.Undo(); Check("restaura flags SCBlock", flagBlock.Type == oldType); memory.Redo(); Check("refaz flags SCBlock", flagBlock.Type != oldType);
for (int i = 0; i < 40; i++) memory.Execute("Seed", () => synthetic.RaidPaldea.CurrentSeed++);
Check("limite e memória", memory.Count == 30 && memory.StoredBytes < 65536); Console.WriteLine("Histórico SV: " + memory.StoredBytes + " bytes para 30 passos; buffer do save: " + synthetic.Write().Length);
var demo = BlankSaveFile.Get(GameVersion.B); var path = Path.Combine(work, "demo.sav"); File.WriteAllBytes(path, demo.Write().ToArray()); vm.Open(path); vm.CurrentPage = vm.Game;
vm.Game.FlagRows[0].IsSet = !vm.Game.FlagRows[0].IsSet; var win = new MainWindow { DataContext = vm, Width = 1180, Height = 720 }; win.Show(); Pump(); win.CaptureRenderedFrame()!.Save(Path.Combine(work, "game-history.png")); win.Close();
Console.WriteLine("Captura: " + work); Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS"); return fails == 0 ? 0 : 1;
