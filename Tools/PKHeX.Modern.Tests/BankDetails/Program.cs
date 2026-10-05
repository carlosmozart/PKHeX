using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing=false }).WithInterFont().SetupWithoutStarting();
var work=args[0]; int fails=0; BankStorage.Root=Path.Combine(work,"bank"); SaveBackup.Folder=Path.Combine(work,"backups");
void Check(string n,bool ok){Console.WriteLine($"{(ok?"OK":"FAIL")} {n}");if(!ok)fails++;}
void Pump(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();}
void Wait(Func<bool> done){var sw=Stopwatch.StartNew();while(!done()&&sw.ElapsedMilliseconds<10000){Pump();System.Threading.Thread.Sleep(10);}Check("operação terminou",done());Pump();}
var sav=BlankSaveFile.Get(GameVersion.B); sav.OT="DEMO";
var path=Path.Combine(work,"Black.sav");File.WriteAllBytes(path,sav.Write().ToArray());
var pk=new PK5{Species=25,Version=GameVersion.B,CurrentLevel=42,Nature=Nature.Modest,Nickname="Sparky",IsNicknamed=true,OriginalTrainerName="DEMO",Ability=9,Ball=4,IV_HP=31,EV_ATK=4,Move1=85};pk.SetShiny();
var box=BankStorage.GetBoxes(BankStorage.GetBanks()[0])[0];BankStorage.WriteSlot(box,0,pk);
var vm=new MainViewModel(new AppSettings{CheckForUpdates=false});var win=new MainWindow{DataContext=vm,Width=1440,Height=950};win.Show();vm.Open(path);vm.CurrentPage=vm.Bank;Pump();
var files=Directory.EnumerateFiles(BankStorage.Root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
var trainer=typeof(ParseSettings).GetProperty("ActiveTrainer",BindingFlags.Static|BindingFlags.NonPublic)!;
var beforeTrainer=trainer.GetValue(null);var gb=ParseSettings.AllowGBEraEvents;var gba=ParseSettings.AllowGBACrossTransferRSE(pk);var swGba=ParseSettings.AllowGen3EventTicketsAll(pk);
var select=vm.SelectSlotAsync(vm.Bank.Slots[0]);Wait(()=>select.IsCompleted);var detail=vm.Bank.Details!;
Check("dados do Bank",detail.Species==25&&detail.Level==42&&detail.Types=="Electric"&&detail.Nickname=="Sparky");
Check("local correto",detail.Location.Contains(box.Name)&&detail.Location.Contains("1")&&detail.Folder==box.Folder);
Check("IVs EVs golpes",detail.IVs.StartsWith("31 /")&&detail.EVs.Contains("4")&&detail.Moves.Contains("Thunderbolt"));
Check("contexto ativo preservado",ReferenceEquals(beforeTrainer,trainer.GetValue(null))&&gb==ParseSettings.AllowGBEraEvents&&gba==ParseSettings.AllowGBACrossTransferRSE(pk)&&swGba==ParseSettings.AllowGen3EventTicketsAll(pk));
string? copied=null;vm.Bank.CopyDetails=text=>{copied=text;return System.Threading.Tasks.Task.CompletedTask;};detail.CopyCommand.Execute(null);Check("Showdown do original",copied==CoreAdapter.ToShowdown(pk));
var exportPath=Path.Combine(work,"export.pk5");vm.Bank.ExportDetails=()=>{vm.ExportEntity(exportPath);return System.Threading.Tasks.Task.CompletedTask;};detail.ExportCommand.Execute(null);Check("exportação mantém formato",File.Exists(exportPath)&&File.ReadAllBytes(exportPath).Length==pk.SIZE_PARTY&&CoreAdapter.LoadEntityFile(sav,exportPath)?.Species==25);
win.UpdateLayout();for(int i=0;i<12;i++)Pump();win.CaptureRenderedFrame()?.Save(Path.Combine(work,"bank-details.png"));
detail.OpenCommand.Execute(null);Wait(()=>vm.Editor is not null||vm.Dialog is not null);
while(vm.Dialog is{} dialog){dialog.Complete(true);Pump();}
Check("abrir cópia sem aplicar",vm.Editor?.SpeciesName=="Pikachu"&&!vm.IsDirty&&vm.ActiveTab!.Sav.GetBoxSlotAtIndex(0,0).Species==0);
var after=Directory.EnumerateFiles(BankStorage.Root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
Check("SHA-256 do Bank intacto",files.Count==after.Count&&files.All(f=>after.TryGetValue(f.Key,out var hash)&&hash==f.Value));
win.Close();Console.WriteLine(fails==0?"TUDO OK":$"{fails} FALHAS");return fails==0?0:1;
