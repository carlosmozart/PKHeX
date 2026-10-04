using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PKHeX.Modern;
using PKHeX.Modern.Services;
using PKHeX.Modern.ViewModels;
using PKHeX.Modern.Views;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int fails = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "OK" : "FAIL")} {name}"); if (!ok) fails++; }
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
var release = UpdateChecker.ParseLatest("""
[{"tag_name":"modern-v99.0.0","name":"Novidades","html_url":"https://github.com/carlosmozart/PKHeX/releases/tag/modern-v99.0.0","body":"## Caixas\n- **Renomear caixas** e escolher papel de parede.\n\n## Saves\n- Navegação por teclado.","assets":[]}]
""")!;
Check("API preserva notas", release.Notes!.Contains("Renomear caixas"));
var vm = new MainViewModel();
var win = new MainWindow { DataContext = vm, Width = 1100, Height = 720 }; win.Show();
vm.Help.SetLatest(release);
var pending = vm.Help.InstallUpdateAsync(); Pump();
Check("notas antes de baixar", !pending.IsCompleted && vm.Dialog is { HasDetails: true } && !vm.Help.IsDownloading);
Check("conteúdo legível", vm.Dialog!.Details[1] == "- Renomear caixas e escolher papel de parede.");
Check("comandos bloqueados durante janela", !vm.Help.InstallUpdateCommand.CanExecute(null) && !vm.Help.OpenLatestCommand.CanExecute(null));
await vm.Help.InstallUpdateAsync();
win.CaptureRenderedFrame()!.Save(Path.Combine(args[0], "update-notes.png"));
vm.Dialog!.Complete(false); pending.GetAwaiter().GetResult(); Pump();
Check("Depois cancela e mantém aviso", vm.Dialog is null && vm.Help.ShowUpdateBanner && !vm.Help.IsDownloading && !vm.Help.IsReviewingUpdate);
vm.Help.SetLatest(release with { Notes = null });
pending = vm.Help.InstallUpdateAsync(); Pump();
Check("release sem notas tem alternativa", vm.Dialog!.Details[0].Contains("não tem notas"));
vm.Dialog.Complete(false); pending.GetAwaiter().GetResult();
var other = vm.ConfirmAsync("Outra pergunta", "Mensagem", "OK");
pending = vm.Help.InstallUpdateAsync(); pending.GetAwaiter().GetResult();
Check("atualização preserva pergunta existente", vm.Dialog!.Title == "Outra pergunta");
vm.Dialog.Complete(false); other.GetAwaiter().GetResult();
vm.Help.SetLatest(release with { Version = UpdateChecker.Current });
await vm.Help.InstallUpdateAsync();
Check("versão atual não abre janela", vm.Dialog is null);
// Pacotes simulados: nenhuma rede e nenhum executavel da instalacao real e alterado.
Check("asset Windows preservado", AutoUpdater.GetAssetName(OSPlatform.Windows, Architecture.X64) == "PKHeX.Modern-win-x64.zip");
Check("asset Linux", AutoUpdater.GetAssetName(OSPlatform.Linux, Architecture.X64) == "PKHeX.Modern-linux-x64.zip");
Check("asset macOS ARM", AutoUpdater.GetAssetName(OSPlatform.OSX, Architecture.Arm64) == "PKHeX.Modern-osx-arm64.zip");
Check("asset macOS Intel", AutoUpdater.GetAssetName(OSPlatform.OSX, Architecture.X64) == "PKHeX.Modern-osx-x64.zip");
Check("asset Android ARM", AutoUpdater.GetAssetName(OSPlatform.Create("Android"), Architecture.Arm64) == "PKHeX.Modern-Android.apk");
Check("asset Android x64 (emulador)", AutoUpdater.GetAssetName(OSPlatform.Create("Android"), Architecture.X64) == "PKHeX.Modern-Android.apk");
Check("desktop nao atualiza por instalador do Android", !AutoUpdater.InstallsByHandoff);
Check("arquitetura sem pacote nao usa Windows", AutoUpdater.GetAssetName(OSPlatform.Linux, Architecture.Arm64) is null);
foreach (var asset in new[] { "PKHeX.Modern-win-x64.zip", "PKHeX.Modern-linux-x64.zip", "PKHeX.Modern-osx-arm64.zip", "PKHeX.Modern-osx-x64.zip", "PKHeX.Modern-Android.apk" })
{
    var entries = new[] { "PKHeX.Modern-win-x64.zip", "PKHeX.Modern-linux-x64.zip", "PKHeX.Modern-osx-arm64.zip", "PKHeX.Modern-osx-x64.zip", "PKHeX.Modern-Android.apk" };
    var assets = Array.ConvertAll(entries, a => new { name = a, browser_download_url = "https://github.com/carlosmozart/PKHeX/releases/download/modern-v99.0.0/" + a, digest = "sha256:abc" });
    var json = JsonSerializer.Serialize(new[] { new { tag_name = "modern-v99.0.0", name = "Teste", html_url = "https://github.com/carlosmozart/PKHeX/releases", assets } });
    var parsed = UpdateChecker.ParseLatest(json, asset)!;
    Check("API seleciona " + asset, parsed.AssetUrl!.EndsWith(asset) && parsed.AssetDigest == "sha256:abc");
    Check("API sem pacote oferece link", UpdateChecker.ParseLatest(json, null)!.AssetUrl is null);
}
Check("dotnet run nao se atualiza", !AutoUpdater.CanSelfUpdate);
Check("arquivo unico Windows", AutoUpdater.CanUpdateExecutable("", Path.Combine(args[0], "PKHeX.Modern.exe"), OSPlatform.Windows, Architecture.X64));
Check("exe renomeado no Windows ainda se atualiza", AutoUpdater.CanUpdateExecutable("", Path.Combine(args[0], "PKHeX.Modern (1).exe"), OSPlatform.Windows, Architecture.X64));
Check("binario renomeado no Linux ainda se atualiza", AutoUpdater.CanUpdateExecutable("", Path.Combine(args[0], "pkhex-modern"), OSPlatform.Linux, Architecture.X64));
Check("DLL Windows nao se atualiza", !AutoUpdater.CanUpdateExecutable("app.dll", "PKHeX.Modern.exe", OSPlatform.Windows, Architecture.X64));
Check("arquivo unico Linux", AutoUpdater.CanUpdateExecutable("", Path.Combine(args[0], "PKHeX.Modern"), OSPlatform.Linux, Architecture.X64));
Check("host dotnet nao se atualiza", !AutoUpdater.CanUpdateExecutable("", Path.Combine(args[0], "dotnet"), OSPlatform.Linux, Architecture.X64));
var macExe = Path.Combine(args[0], "PKHeX Modern.app", "Contents", "MacOS", "PKHeX.Modern");
Check("arquivo unico bundle macOS", AutoUpdater.CanUpdateExecutable("", macExe, OSPlatform.OSX, Architecture.Arm64));
Check("macOS fora do bundle nao se atualiza", !AutoUpdater.CanUpdateExecutable("", Path.Combine(args[0], "PKHeX.Modern"), OSPlatform.OSX, Architecture.X64));

var payload = new byte[1_000_001];
payload[0] = 42;
string CreateZip(string name, params (string Name, byte[] Data)[] entries)
{
    var path = Path.Combine(args[0], name);
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var e in entries)
    {
        using var output = archive.CreateEntry(e.Name).Open();
        output.Write(e.Data);
    }
    return path;
}
bool Rejected(Action action)
{
    try { action(); return false; }
    catch (InvalidDataException) { return true; }
    catch (OperationCanceledException) { return true; }
}
foreach (var platform in new[] { OSPlatform.Windows, OSPlatform.Linux })
{
    var folder = Path.Combine(args[0], platform.ToString());
    Directory.CreateDirectory(folder);
    var name = platform == OSPlatform.Windows ? "PKHeX.Modern.exe" : "PKHeX.Modern";
    var target = Path.Combine(folder, name);
    File.WriteAllText(target, "original");
    var zip = CreateZip(platform + ".zip", (name, payload));
    var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip)));
    Check(platform + " rejeita SHA incorreto", Rejected(() => AutoUpdater.InstallPackage(zip, target, platform, "sha256:0000")) && File.ReadAllText(target) == "original");
    Check(platform + " cancelamento preserva original", Rejected(() => AutoUpdater.InstallPackage(zip, target, platform, digest, new CancellationToken(true))) && File.ReadAllText(target) == "original");
    var invalid = CreateZip(platform + "-invalid.zip", (name, new byte[10]));
    Check(platform + " rejeita arquivo incompleto", Rejected(() => AutoUpdater.InstallPackage(invalid, target, platform)) && File.ReadAllText(target) == "original");
    var missing = CreateZip(platform + "-missing.zip", ("outro", payload));
    Check(platform + " rejeita estrutura errada", Rejected(() => AutoUpdater.InstallPackage(missing, target, platform)) && File.ReadAllText(target) == "original");
    AutoUpdater.InstallPackage(zip, target, platform, digest);
    Check(platform + " troca e preserva old", File.ReadAllBytes(target)[0] == 42 && File.ReadAllText(target + ".old") == "original");
    Check(platform + " limpa staging", Directory.GetFiles(folder, "*.new-*").Length == 0);
    if (platform == OSPlatform.Linux && !OperatingSystem.IsWindows())
        Check("Linux restaura permissao de execucao", File.GetUnixFileMode(target).HasFlag(UnixFileMode.UserExecute));
}
Directory.CreateDirectory(Path.GetDirectoryName(macExe)!);
File.WriteAllText(macExe, "mac original");
var contents = Path.GetDirectoryName(Path.GetDirectoryName(macExe))!;
File.WriteAllText(Path.Combine(contents, "Info.plist"), "versao antiga");
var newPlist = System.Text.Encoding.UTF8.GetBytes("versao nova");
var userSaves = Path.Combine(Path.GetDirectoryName(macExe)!, "saves");
Directory.CreateDirectory(userSaves);
File.WriteAllText(Path.Combine(userSaves, "synthetic.sav"), "save local" );
var macZip = CreateZip("mac.zip", ("PKHeX Modern.app/Contents/MacOS/PKHeX.Modern", payload), ("PKHeX Modern.app/Contents/Info.plist", newPlist));
var badMac = CreateZip("mac-bad.zip", ("PKHeX Modern.app/Contents/MacOS/PKHeX.Modern", payload), ("PKHeX Modern.app/Contents/Info.plist", newPlist), ("PKHeX Modern.app/../escape", payload));
var linkedMac = CreateZip("mac-linked.zip", ("PKHeX Modern.app/Contents/MacOS/PKHeX.Modern", payload), ("PKHeX Modern.app/Contents/Info.plist", newPlist));
using (var archive = ZipFile.Open(linkedMac, ZipArchiveMode.Update))
    archive.GetEntry("PKHeX Modern.app/Contents/MacOS/PKHeX.Modern")!.ExternalAttributes = 0xA1FF << 16;
Check("macOS rejeita link simbolico no pacote", Rejected(() => AutoUpdater.InstallPackage(linkedMac, macExe, OSPlatform.OSX)) && File.ReadAllText(macExe) == "mac original");
Check("macOS rejeita caminho fora do bundle", Rejected(() => AutoUpdater.InstallPackage(badMac, macExe, OSPlatform.OSX)) && File.ReadAllText(macExe) == "mac original");
var noPlist = CreateZip("mac-no-plist.zip", ("PKHeX Modern.app/Contents/MacOS/PKHeX.Modern", payload));
Check("macOS exige Info.plist", Rejected(() => AutoUpdater.InstallPackage(noPlist, macExe, OSPlatform.OSX)) && File.ReadAllText(macExe) == "mac original");
AutoUpdater.InstallPackage(macZip, macExe, OSPlatform.OSX);
Check("macOS troca executavel e plist juntos", File.ReadAllBytes(macExe)[0] == 42 && File.ReadAllText(Path.Combine(contents, "Info.plist")) == "versao nova");
Check("macOS conserva save local dentro do bundle", File.ReadAllText(Path.Combine(userSaves, "synthetic.sav")) == "save local");
Check("macOS conserva bundle antigo", File.ReadAllText(Path.Combine(args[0], "PKHeX Modern.app.old", "Contents", "MacOS", "PKHeX.Modern")) == "mac original");
Check("macOS limpa staging", Directory.GetDirectories(args[0], "*.new-*").Length == 0);
if (!OperatingSystem.IsWindows())
    Check("macOS restaura permissao de execucao", File.GetUnixFileMode(macExe).HasFlag(UnixFileMode.UserExecute));
Console.WriteLine(fails == 0 ? "TUDO OK" : $"{fails} FALHAS");
Console.WriteLine("Capturas: " + args[0]);
return fails == 0 ? 0 : 1;
