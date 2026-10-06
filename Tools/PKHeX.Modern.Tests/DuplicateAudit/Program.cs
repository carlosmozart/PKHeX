using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().SetupWithoutStarting();
var work=args[0];Directory.CreateDirectory(work);int fails=0;
BankStorage.Root=Path.Combine(work,"bank");SaveBackup.Folder=Path.Combine(work,"backups");BankLinks.Reset();
void Check(string n,bool ok){Console.WriteLine($"{(ok?"OK":"FAIL")} {n}");if(!ok)fails++;}
var folder=Path.Combine(work,"saves");Directory.CreateDirectory(folder);
var save=BlankSaveFile.Get(GameVersion.B);save.OT="DEMO";
var pk=new PK5 {Species=25,CurrentLevel=12,OriginalTrainerName="DEMO",Version=GameVersion.B,PID=42,EncryptionConstant=42,TID16=7};pk.RefreshChecksum();
save.SetBoxSlotAtIndex(pk,0,0,EntityImportSettings.None);save.SetBoxSlotAtIndex(pk.Clone(),0,1,EntityImportSettings.None);
var path=Path.Combine(folder,"Black.sav");File.WriteAllBytes(path,save.Write().ToArray());
var src1=new DbSource(path,"Black",GameVersion.B,true){Save=save};var src2=new DbSource(Path.Combine(work,"Other.sav"),"Other",GameVersion.B,false){Save=save.Clone()};
var a=new DbEntry(src1,pk,"Box 1 · 1",0,0);var b=new DbEntry(src2,pk.Clone(),"Box 1 · 1",0,0);
var result=DuplicateAudit.Build([a,b]);Check("two saves one identical group",result.Groups.Count==1&&result.Groups[0].Entries.Count==2);
// Two plain saves (e.g. Switch "main" copied into dated folders) sharing many identical Pokemon: one pair, no loose groups.
var many=Enumerable.Range(0,12).Select(i=>{var p=pk.Clone();p.PID=(uint)(1000+i);p.EncryptionConstant=p.PID;p.RefreshChecksum();return p;}).ToArray();
var dayA=new DbSource(Path.Combine(work,"2026-10-01","main"),"Black A",GameVersion.B,false);var dayB=new DbSource(Path.Combine(work,"2026-10-02","main"),"Black B",GameVersion.B,false);
var folderPairs=DuplicateAudit.Build([..many.Select((p,i)=>new DbEntry(dayA,p,$"Box 1 · {i+1}",0,i)),..many.Select((p,i)=>new DbEntry(dayB,p.Clone(),$"Box 1 · {i+1}",0,i))]);
Check("folder copies collapsed into one pair",folderPairs.Groups.Count==1&&folderPairs.Groups[0].IsBackupPair&&folderPairs.Groups[0].Children!.Count==12);
var few=DuplicateAudit.Build([..many.Take(3).Select((p,i)=>new DbEntry(dayA,p,$"Box 1 · {i+1}",0,i)),..many.Take(3).Select((p,i)=>new DbEntry(dayB,p.Clone(),$"Box 1 · {i+1}",0,i))]);
Check("few shared Pokemon stay as loose groups",few.Groups.Count==3&&few.Groups.All(g=>!g.IsBackupPair));
var different=pk.Clone();different.IV_ATK=11;Check("two different Pikachu no group",DuplicateAudit.Build([a,new(src2,different,"Box 1",0,0)]).Groups.Count==0);
var zip=Path.Combine(folder,"backup.zip");using(var z=ZipFile.Open(zip,ZipArchiveMode.Create)){var e=z.CreateEntry("main");using var s=e.Open();s.Write(File.ReadAllBytes(path));}
var data=PokemonDatabase.Build([(path,save.Clone())],folder,new(),includeBank:false,readOnly:true);
result=DuplicateAudit.Build(data);Check("backup collapsed by pair",result.Groups.Count(g=>g.IsBackupPair)==1&&result.Groups.Single(g=>g.IsBackupPair).Children!.Count==1&&result.Groups.Single(g=>g.IsBackupPair).MatchedCount==2);
BankStorage.CreateBank("Principal");var box=BankStorage.GetBoxes("Principal")[0];BankStorage.WriteSlot(box,0,pk);
BankLinks.Attach(pk,path,"Black");
var modified=save.GetBoxSlotAtIndex(0,0);modified.CurrentLevel=20;save.SetBoxSlotAtIndex(modified,0,0,EntityImportSettings.None);
BankLinks.Sync(path,save); // generate a real persisted attachment; restore the bank original to make it stale.
BankStorage.WriteSlot(box,0,pk);
var bankSource=new DbSource("bank:Principal","Bank",GameVersion.Any,false,"Principal");
var originalBankBytes=pk.Data.ToArray();
var variantPk=new PK4{Species=25,CurrentLevel=12,Version=GameVersion.Pt,PID=42,TID16=7,OriginalTrainerName="DEMO"};
var foreignSave=BlankSaveFile.Get(GameVersion.Pt);foreignSave.SetBoxSlotAtIndex(variantPk,0,0,EntityImportSettings.None);
BankLinks.Sync(path,foreignSave);
result=DuplicateAudit.Build([new(src1,modified,"Box 1",0,0),new(bankSource,pk,"Box 1",0,0)]);
Check("registered attachment group",result.Groups.Any(g=>g.Kind=="Anexado"&&g.CanCompare));
Check("registered variant included",result.Groups.Any(g=>g.Kind=="Anexado"&&g.Entries.Any(e=>e.Box==-2)));
Check("identical copies cannot compare",DuplicateAudit.Build([a,b]).Groups.All(g=>!g.CanCompare));
string Hashes()=>string.Join("\n",Directory.GetFiles(work,"*",SearchOption.AllDirectories).Order().Select(f=>f+":"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))));
var before=Hashes();_ = DuplicateAudit.Build(data);Check("audit never writes",before==Hashes());
var vm=new MainViewModel(new AppSettings{SavesFolder=folder,CheckForUpdates=false});vm.Open(path);vm.CurrentPage=vm.Search;
void Pump(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();}
var end=DateTime.UtcNow.AddSeconds(60);while(vm.Search.IsBusy&&DateTime.UtcNow<end){Pump();System.Threading.Thread.Sleep(10);}vm.Search.ShowDuplicates=true;
Check("UI duplicate groups",vm.Search.DuplicateGroups.Count>0);
var desk=new PKHeX.Modern.Views.MainWindow{DataContext=vm,Width=1440,Height=950};desk.Show();for(int i=0;i<10;i++)Pump();desk.CaptureRenderedFrame()?.Save(Path.Combine(work,"duplicates-desktop.png"));desk.Hide();
App.ShowShortcuts=false;var phone=new Avalonia.Controls.Window{Width=892,Height=412,Content=new PKHeX.Modern.Views.MobileShell(vm)};phone.Show();for(int i=0;i<10;i++)Pump();phone.CaptureRenderedFrame()?.Save(Path.Combine(work,"duplicates-mobile.png"));
var refresh=vm.Search.RefreshAsync();end=DateTime.UtcNow.AddSeconds(60);while(!refresh.IsCompleted&&DateTime.UtcNow<end){Pump();System.Threading.Thread.Sleep(10);}
Check("groups after refresh",vm.Search.DuplicateGroups.Count>0);
phone.CaptureRenderedFrame()?.Save(Path.Combine(work,"duplicates-mobile.png"));
vm.Search.DuplicateGroups.First().SelectCommand.Execute(null);for(int i=0;i<10;i++)Pump();phone.CaptureRenderedFrame()?.Save(Path.Combine(work,"duplicates-backup-mobile.png"));
if(vm.Search.SelectedDuplicate is {HasChildren:true} group)group.Children.First().SelectCommand.Execute(null);
for(int i=0;i<10;i++)Pump();phone.CaptureRenderedFrame()?.Save(Path.Combine(work,"duplicates-detail-mobile.png"));phone.Close();
Console.WriteLine(fails==0?"TUDO OK":$"{fails} FALHAS");return fails==0?0:1;
