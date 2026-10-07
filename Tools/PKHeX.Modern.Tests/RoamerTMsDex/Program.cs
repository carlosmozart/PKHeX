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

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().SetupWithoutStarting();
var work=args[0];int fails=0;
BankStorage.Root=Path.Combine(work,"bank");SaveBackup.Folder=Path.Combine(work,"backups");
void Check(string n,bool ok){Console.WriteLine($"{(ok?"OK":"FAIL")} {n}");if(!ok)fails++;}
var frPath=Path.Combine(work,"fr.sav");
if(!File.Exists(frPath)){Console.WriteLine("PULADO: sem fr.sav");return 0;}
var fr=CoreAdapter.LoadSave(frPath)!;CoreAdapter.Activate(fr);

// Roamer: Entei da FireRed com IVs sorteados (antes vinha sempre com IVs zerados) e legal.
var entei=EncounterDatabase.SearchEncounters(fr,(ushort)Species.Entei,true).First(e=>RoamerIVs3.IsTruncated(e));
var made=Enumerable.Range(0,12).Select(_=>(PK3)EncounterDatabase.ToEntity(fr,entei,out string? _)!).ToList();
Check("roamer gerado",made.All(p=>p is PK3));
Check("roamer legal",made.All(p=>new LegalityAnalysis(p).Valid));
Check("IVs nao zerados",made.Count(p=>p.IV32==0)<=1&&made.Select(p=>p.IV32).Distinct().Count()>3);
Check("so 8 bits de IV (bug do jogo)",made.All(p=>(p.IV32&0x3FFFFFFF)<=0xFF));
var shinyCrit=EncounterCriteria.Unrestricted with{Shiny=Shiny.Always};
var shiny=EncounterDatabase.ToEntity(fr,entei,shinyCrit,out _)!;
Check("roamer shiny legal",shiny.IsShiny&&new LegalityAnalysis(shiny).Valid);

// Mochila: todos os TMs.
string? msg=null;var bag=new BagPageViewModel(s=>msg=s);bag.Load(fr);
Check("botao TMs habilitado",bag.GiveAllTMsCommand.CanExecute(null));
bag.GiveAllTMsCommand.Execute(null);var tmMsg=msg;Console.WriteLine(tmMsg);
var b=CoreAdapter.GetBag(fr);bag.SaveCommand.Execute(null);b=CoreAdapter.GetBag(fr);
var tm=b.Pouches.First(p=>p.Type==InventoryType.TMHMs);
Check("50 TMs e 8 HMs na Gen 3",tm.Items.Count(i=>i.Index!=0&&i.Count>0)>=58);
var tms=tm.Items.Where(i=>i.Index!=0).ToList();
Check("99 de cada TM, HMs com 1",tms.Count(i=>i.Count==99)==50&&tms.Count(i=>i.Count==1)==8);
Check("ordem numerica",tms.Select(i=>i.Index).SequenceEqual(tms.Select(i=>i.Index).OrderBy(x=>x)));
Check("mensagem",tmMsg is not null&&tmMsg.Contains("TM"));
var gen1=BlankSaveFile.Get(GameVersion.RD);var bag1=new BagPageViewModel(_=>{});bag1.Load(gen1);
Check("sem bolso de TMs: desabilitado",!bag1.GiveAllTMsCommand.CanExecute(null));

// Pokedex: so o save aberto.
var folder=Path.Combine(work,"folder");Directory.CreateDirectory(folder);
var settings=new AppSettings{SavesFolder=folder};
var em=Path.Combine(work,"em.sav");if(File.Exists(em))File.Copy(em,Path.Combine(folder,"em.sav"),true);
var dex=new PokedexPageViewModel(settings,(_,_)=>{});dex.Load(fr);
var t=dex.RefreshAsync();while(!t.IsCompleted)Dispatcher.UIThread.RunJobs();
var all=dex.Stats;
Check("tem save aberto",dex.HasOpenSave&&!dex.OnlyOpenSave);
dex.OnlyOpenSave=true;
Check("fonte = save aberto",dex.OnlyOpenSave&&dex.SourceIndex>0);
Check("estatistica muda",File.Exists(em)?dex.Stats!=all:true);
dex.OnlyOpenSave=false;Check("volta para tudo",dex.SourceIndex==0&&dex.Stats==all);
Console.WriteLine(fails==0?"TUDO OK":$"{fails} FALHA(S)");return fails==0?0:1;
