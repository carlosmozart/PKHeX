using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using PKHeX.Core;
using PKHeX.Modern.Services;

var work=args[0]; Directory.CreateDirectory(work); int fails=0;
BankStorage.Root=Path.Combine(work,"bank"); SaveBackup.Folder=Path.Combine(work,"backups");
void Check(string name,bool ok){Console.WriteLine($"{(ok?"OK":"FAIL")} {name}");if(!ok)fails++;}
var sav=BlankSaveFile.Get(GameVersion.B); sav.OT="DEMO";
var pk=new PK5 {Species=25,CurrentLevel=10,OriginalTrainerName="DEMO",Version=GameVersion.B}; pk.RefreshChecksum();
sav.SetBoxSlotAtIndex(pk,0,0,EntityImportSettings.None);
var path=Path.Combine(work,"Black.sav");File.WriteAllBytes(path,sav.Write().ToArray());
var original=SHA256.HashData(File.ReadAllBytes(path));
var bankParty=(PK5)pk.Clone(); bankParty.Stat_HPCurrent=1;
Check("party data does not affect stored equality",StoredPokemon.Equal(pk,bankParty));
var exported=Path.Combine(work,"Pikachu.pk5");CoreAdapter.ExportEntity(bankParty,exported);
Check("exported party file equals box entry",CoreAdapter.LoadEntityFile(sav,exported) is {} filePokemon&&StoredPokemon.Equal(filePokemon,sav.GetBoxSlotAtIndex(0,0)));
var different=pk.Clone();different.IV_ATK=10;Check("same species and IDs are not equality",!StoredPokemon.Equal(pk,different));
var before=pk.Data.ToArray();_ = StoredPokemon.Index(pk);Check("no normalization",before.SequenceEqual(pk.Data));
var edited=sav.Clone();var changed=pk.Clone();changed.CurrentLevel=20;edited.SetBoxSlotAtIndex(changed,0,0,EntityImportSettings.None);
var reader=new CollectionSources();
var entries=await reader.ReadAsync([(path,edited)],work,false,CancellationToken.None);
Check("open memory snapshot counted once",entries.Count==1&&entries[0].Pkm.CurrentLevel==20);
Check("snapshot not caller save",!ReferenceEquals(entries[0].Source.Save,edited));
var zip=Path.Combine(work,"backup.zip");using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)){var e=archive.CreateEntry("main");using var stream=e.Open();stream.Write(File.ReadAllBytes(path));}
reader.Invalidate();entries=await reader.ReadAsync([(path,sav)],work,false,CancellationToken.None);
Check("zip entry is distinct stable source",entries.Count==2&&entries.Any(e=>e.Source.Id.EndsWith("|main")));
using(var archive=ZipFile.Open(zip,ZipArchiveMode.Update)){var e=archive.CreateEntry("MAIN");using var stream=e.Open();stream.Write(File.ReadAllBytes(path));}
reader.Invalidate();entries=await reader.ReadAsync([(ZipSaves.Combine(zip,"main"),sav)],work,false,CancellationToken.None);
var cached=await reader.ReadAsync([(ZipSaves.Combine(zip,"main"),sav)],work,false,CancellationToken.None);
Check("case-distinct ZIP entries survive cache",entries.Count==3&&cached.Count==3&&cached.Any(e=>e.Source.Id.EndsWith("|MAIN"))&&!StoredPokemon.SameSource(ZipSaves.Combine(zip,"main"),ZipSaves.Combine(zip,"MAIN")));
var cancellation=new CancellationTokenSource();
bool canceled=false;try{await reader.ReadAsync([(path,sav)],work,false,cancellation.Token,_=>cancellation.Cancel());}catch(OperationCanceledException){canceled=true;}
Check("cancellation returns no partial result",canceled);
Check("no files or banks created",!Directory.Exists(BankStorage.Root)&&original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))));
Console.WriteLine(fails==0?"TUDO OK":$"{fails} FALHAS");return fails==0?0:1;
