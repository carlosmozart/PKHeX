using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Core;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.Theme;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var work=args[0]; int fails=0;
BankStorage.Root=Path.Combine(work,"bank"); SaveBackup.Folder=Path.Combine(work,"backups");
void Check(string n,bool ok) { Console.WriteLine($"{(ok?"OK":"FAIL")} {n}"); if(!ok) fails++; }
void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
var hashes=new System.Collections.Generic.HashSet<string>();
var originalTheme=AppTheme.Current;
foreach(var dark in new[]{true,false})
foreach(var theme in AppTheme.Presets)
{
    var bitmap=ThemePreview.Get(theme,dark); using var bytes=new MemoryStream(); bitmap.Save(bytes);
    var data=bytes.ToArray(); hashes.Add(Convert.ToHexString(SHA256.HashData(data)));
    Check($"miniatura {theme.Key} {(dark?"dark":"light")} renderizada",data.Length>1000 && bitmap.PixelSize==new PixelSize(232,126));
    Check("miniatura em cache",ReferenceEquals(bitmap,ThemePreview.Get(theme,dark)));
    bitmap.Save(Path.Combine(work,$"theme-{theme.Key}-{(dark?"dark":"light")}.png"));
}
Check("uma miniatura distinta por tema e variante",hashes.Count==AppTheme.Presets.Count*2);
Check("prévia não troca tema do app",ReferenceEquals(originalTheme,AppTheme.Current));
var sav=BlankSaveFile.Get(GameVersion.B); sav.OT="DEMO"; sav.SetBoxSlotAtIndex(new PK5{Species=25,Version=GameVersion.B,CurrentLevel=10},0,0);
var path=Path.Combine(work,"Black.sav"); File.WriteAllBytes(path,sav.Write().ToArray());
var settings=new AppSettings{CheckForUpdates=false}; var vm=new MainViewModel(settings); vm.Open(path); vm.CurrentPage=vm.Boxes;
var window=new MainWindow{DataContext=vm,Width=1440,Height=950}; window.Show(); Pump();
var image=window.GetVisualDescendants().OfType<Image>().Single(i=>i.Name=="BoxWallpaperImage");
Check("intensidade normal",image.Opacity==0.25);
vm.WallpaperIntensityIndex=1; Pump(); Check("intensidade suave",image.Opacity==0.10);
vm.WallpaperIntensityIndex=2; Pump(); Check("papel desligado",image.Opacity==0);
var again=new MainViewModel(settings); Check("preferência ao reabrir",again.WallpaperIntensityIndex==2 && again.Boxes.WallpaperOpacity==0 && !settings.Persist);
Check("papel do jogo preservado",vm.ActiveTab!.Sav is IBoxDetailWallpaper wp && wp.GetBoxWallpaper(0)==((IBoxDetailWallpaper)sav).GetBoxWallpaper(0) && !vm.IsDirty);
var before=vm.ThemeOptions[0].Preview; vm.ToggleThemeCommand.Execute(null); Pump(); Check("miniatura acompanha claro/escuro",!ReferenceEquals(before,vm.ThemeOptions[0].Preview));
var prefs=window.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="PrefsButton"); prefs.Flyout!.ShowAt(prefs); Pump(); window.UpdateLayout(); for(int i=0;i<8;i++)Pump();
window.CaptureRenderedFrame()?.Save(Path.Combine(work,"theme-picker.png")); prefs.Flyout.Hide(); window.Close();
Console.WriteLine(fails==0?"TUDO OK":$"{fails} FALHAS"); return fails==0?0:1;
