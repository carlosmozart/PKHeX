using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PKHeX.Modern.Services;

/// <summary>Baixa o pacote da plataforma, confere o SHA-256 e troca a instalacao publicada.</summary>
public static class AutoUpdater
{
    private const string ExecutableName = "PKHeX.Modern";
    private const string BundleName = "PKHeX Modern.app";
    private const string DownloadPrefix = "https://github.com/" + UpdateChecker.Repo + "/releases/download/";
    private static readonly string? ProcessExecutable = Environment.ProcessPath;
    private static OSPlatform CurrentPlatform => OperatingSystem.IsAndroid() ? OSPlatform.Create("Android") : OperatingSystem.IsWindows() ? OSPlatform.Windows
        : OperatingSystem.IsLinux() ? OSPlatform.Linux : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Create("Unsupported");

    public static string? AssetName => GetAssetName(CurrentPlatform, RuntimeInformation.OSArchitecture);
    public static string? ExePath => ProcessExecutable;

    /// <summary>So ha pacotes para as arquiteturas publicadas; outras usam o link da release.</summary>
    public static string? GetAssetName(OSPlatform platform, Architecture architecture) => (platform, architecture) switch
    {
        (var os, Architecture.X64) when os == OSPlatform.Windows => "PKHeX.Modern-win-x64.zip",
        (var os, Architecture.X64) when os == OSPlatform.Linux => "PKHeX.Modern-linux-x64.zip",
        (var os, Architecture.Arm64) when os == OSPlatform.OSX => "PKHeX.Modern-osx-arm64.zip",
        (var os, Architecture.X64) when os == OSPlatform.OSX => "PKHeX.Modern-osx-x64.zip",
        _ => null,
    };

    public static bool CanSelfUpdate => CanUpdateExecutable((System.Reflection.Assembly.GetEntryAssembly()?.Location ?? typeof(AutoUpdater).Assembly.Location), ExePath,
        CurrentPlatform, RuntimeInformation.OSArchitecture);

    /// <summary>
    /// Assembly sem caminho identifica arquivo unico. O nome do executavel pode ter sido trocado pelo usuario
    /// ("PKHeX.Modern (1).exe", como nas versoes anteriores): vale qualquer nome, menos o host do .NET; no Windows precisa
    /// ser .exe e no macOS precisa estar dentro de um bundle .app.
    /// </summary>
    public static bool CanUpdateExecutable(string? assemblyLocation, string? exe, OSPlatform platform, Architecture architecture)
    {
        if (assemblyLocation != "" || exe is null || GetAssetName(platform, architecture) is null)
            return false;
        var name = Path.GetFileNameWithoutExtension(exe);
        if (name.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return false;
        if (platform == OSPlatform.Windows)
            return Path.GetExtension(exe).Equals(".exe", StringComparison.OrdinalIgnoreCase);
        return platform != OSPlatform.OSX || GetBundlePath(exe) is not null;
    }

    /// <summary>Apaga a copia antiga deixada pela atualizacao, quando ela ja nao esta em uso.</summary>
    public static bool CleanupOld()
    {
        try
        {
            if (ExePath is not { } exe)
                return false;
            if (OperatingSystem.IsMacOS() && GetBundlePath(exe) is { } bundle)
            {
                var oldBundle = bundle + ".old";
                if (!Directory.Exists(oldBundle)) return false;
                Directory.Delete(oldBundle, recursive: true);
                return true;
            }
            if (File.Exists(exe + ".old"))
            {
                File.Delete(exe + ".old");
                return true;
            }
        }
        catch
        {
            // Ainda em uso: fica para a proxima abertura.
        }
        return false;
    }

    public static Task InstallAsync(ReleaseInfo release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!CanSelfUpdate || ExePath is not { } exe)
            throw new InvalidOperationException("esta cópia não pode se atualizar sozinha (rodando pelo código?)");
        return InstallToAsync(release, exe, progress, ct);
    }

    /// <summary>Instala no destino indicado; o download continua restrito as releases deste repositorio.</summary>
    public static async Task InstallToAsync(ReleaseInfo release, string exe, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (release.AssetUrl is not { } url || !url.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"a release não tem o arquivo {AssetName}");
        // Nome unico: duas instancias nao compartilham um download temporario.
        var zip = Path.Combine(Path.GetTempPath(), $"PKHeX.Modern-{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadAsync(url, zip, progress, ct).ConfigureAwait(false);
            InstallPackage(zip, exe, CurrentPlatform, release.AssetDigest, ct);
        }
        finally { TryDelete(zip); }
    }

    /// <summary>Instala um ZIP local (tambem usado pelos testes, sem download nem alterar o app em uso).</summary>
    public static void InstallPackage(string zip, string exe, OSPlatform platform, string? digest = null, CancellationToken ct = default)
    {
        if (platform != OSPlatform.Windows && platform != OSPlatform.Linux && platform != OSPlatform.OSX)
            throw new PlatformNotSupportedException("sistema sem pacote de atualização");
        exe = Path.GetFullPath(exe);
        if (digest is not null) VerifySha256(zip, digest);
        PreloadForRestart();
        try
        {
            if (platform == OSPlatform.OSX)
                InstallBundle(zip, exe, ct);
            else
                InstallExecutable(zip, exe, platform, ct);
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"sem permissão para gravar em {Path.GetDirectoryName(exe)}. Baixe pelo link da release");
        }
    }

    private static void InstallExecutable(string zip, string exe, OSPlatform platform, CancellationToken ct)
    {
        var fresh = exe + $".new-{Guid.NewGuid():N}";
        var old = exe + ".old";
        try
        {
            using (var archive = ZipFile.OpenRead(zip))
            {
                var name = platform == OSPlatform.Windows ? ExecutableName + ".exe" : ExecutableName;
                var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"o zip não tem o {name}");
                RejectSymlink(entry);
                entry.ExtractToFile(fresh, overwrite: false);
            }
            ValidateExecutable(fresh);
            if (platform == OSPlatform.Linux) MakeExecutable(fresh);
            ct.ThrowIfCancellationRequested();
            if (platform == OSPlatform.Linux)
            {
                // Copia de recuperacao primeiro; rename substitui o destino sem janela de ausencia.
                File.Copy(exe, old, overwrite: true);
                File.Move(fresh, exe, overwrite: true);
            }
            else
            {
                // Windows permite renomear o exe aberto, mas nao sobrescreve-lo.
                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);
                try { File.Move(fresh, exe); }
                catch { File.Move(old, exe); throw; }
            }
        }
        finally { TryDelete(fresh); }
    }

    private static void InstallBundle(string zip, string exe, CancellationToken ct)
    {
        var bundle = GetBundlePath(exe) ?? throw new InvalidDataException("o executável não está dentro de um bundle .app");
        var old = bundle + ".old";
        // Staging unico ao lado do bundle: rename permanece no mesmo volume.
        var fresh = bundle + $".new-{Guid.NewGuid():N}";
        try
        {
            CopyBundle(bundle, fresh);
            using (var archive = ZipFile.OpenRead(zip))
            {
                const string prefix = BundleName + "/";
                if (archive.GetEntry(prefix + "Contents/Info.plist") is null
                    || archive.GetEntry(prefix + "Contents/MacOS/" + ExecutableName) is null)
                    throw new InvalidDataException("o pacote não contém o executável e Info.plist do bundle");
                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal))
                        throw new InvalidDataException("entrada fora do bundle no pacote");
                    RejectSymlink(entry);
                    var relative = entry.FullName[prefix.Length..];
                    if (relative.Length == 0 && entry.FullName.EndsWith('/')) continue;
                    if (relative.Contains('\\')) throw new InvalidDataException("caminho inválido no pacote");
                    var target = Path.GetFullPath(Path.Combine(fresh, relative));
                    if (!target.StartsWith(fresh + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw new InvalidDataException("caminho fora do bundle no pacote");
                    if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            }
            var newExe = Path.Combine(fresh, "Contents", "MacOS", ExecutableName);
            ValidateExecutable(newExe);
            if (!File.Exists(Path.Combine(fresh, "Contents", "Info.plist")))
                throw new InvalidDataException("o bundle baixado não tem Info.plist");
            MakeExecutable(newExe);
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(old)) Directory.Delete(old, recursive: true);
            Directory.Move(bundle, old);
            try { Directory.Move(fresh, bundle); }
            catch { Directory.Move(old, bundle); throw; }
        }
        finally
        {
            if (Directory.Exists(fresh)) Directory.Delete(fresh, recursive: true);
        }
    }

    // Saves ao lado do executavel podem estar dentro do bundle. Preserva arquivos locais
    // que nao aparecem na release; os arquivos publicados sao sobrepostos no staging.
    private static void CopyBundle(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("a instalação contém um link simbólico; atualize pelo download manual");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("a instalação contém um link simbólico; atualize pelo download manual");
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyBundle(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
    private static string? GetBundlePath(string exe)
    {
        var macos = Path.GetDirectoryName(Path.GetFullPath(exe));
        var contents = macos is null ? null : Path.GetDirectoryName(macos);
        var bundle = contents is null ? null : Path.GetDirectoryName(contents);
        return Path.GetFileName(macos) == "MacOS" && Path.GetFileName(contents) == "Contents"
            && bundle is not null && bundle.EndsWith(".app", StringComparison.Ordinal) ? bundle : null;
    }

    private static void ValidateExecutable(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < 1_000_000)
            throw new InvalidDataException("o executável baixado parece incompleto");
    }

    private static void RejectSymlink(ZipArchiveEntry entry)
    {
        if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            throw new InvalidDataException("o pacote contém um link simbólico");
    }

    private static void MakeExecutable(string path)
    {
        // Os testes de estrutura podem simular os pacotes Unix no Windows.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    /// <summary>Carrega antes da troca os tipos que o reinicio do arquivo unico vai usar.</summary>
    public static void PreloadForRestart()
    {
        using var current = Process.GetCurrentProcess();
        var info = new ProcessStartInfo(current.MainModule?.FileName ?? "x") { UseShellExecute = false };
        info.ArgumentList.Add("x");
        _ = info.WorkingDirectory;
    }

    /// <summary>Reabre o executavel na localizacao original (o bundle novo no macOS).</summary>
    public static bool Restart(string? savePath)
    {
        if (ExePath is not { } exe) return false;
        try
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
            if (savePath is not null && File.Exists(savePath)) info.ArgumentList.Add(savePath);
            Process.Start(info);
            return true;
        }
        catch { return false; }
    }

    private static async Task DownloadAsync(string url, string path, IProgress<double>? progress, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"PKHeX.Modern/{UpdateChecker.CurrentText}");
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = File.Create(path);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            if (total > 0) progress?.Report((double)done / total);
        }
    }

    private static void VerifySha256(string path, string digest)
    {
        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (!hash.Equals(digest[prefix.Length..], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("o arquivo baixado não confere com o da release (SHA-256 diferente)");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
