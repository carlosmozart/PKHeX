using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PKHeX.Modern.Services;

/// <summary>
/// Atualizacao automatica do exe publicado: baixa o zip da release, confere o SHA-256 informado pelo GitHub,
/// renomeia o exe em uso para ".old" (o Windows deixa renomear um exe aberto, mas nao sobrescrever) e poe o novo
/// no lugar. A versao nova vale ao reiniciar; o ".old" e apagado na proxima abertura.
/// </summary>
public static class AutoUpdater
{
    public const string AssetName = "PKHeX.Modern-win-x64.zip";
    private const string ExeName = "PKHeX.Modern.exe";
    private const string DownloadPrefix = "https://github.com/" + UpdateChecker.Repo + "/releases/download/";

    public static string? ExePath => Environment.ProcessPath;
    private static string? OldPath => ExePath is { } p ? p + ".old" : null;

    /// <summary>
    /// So o exe publicado (arquivo unico) se atualiza: rodando pelo dotnet run/build a assembly tem caminho
    /// proprio e o processo e o host do .NET, entao nada e trocado.
    /// </summary>
    public static bool CanSelfUpdate =>
        string.IsNullOrEmpty(typeof(AutoUpdater).Assembly.Location)
        && ExePath is { } p
        && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Apaga o exe antigo deixado pela ultima atualizacao. True se havia um (o app acabou de ser atualizado).</summary>
    public static bool CleanupOld()
    {
        try
        {
            if (OldPath is { } old && File.Exists(old))
            {
                File.Delete(old);
                return true;
            }
        }
        catch
        {
            // ainda em uso (a versao anterior esta fechando): fica para a proxima
        }
        return false;
    }

    /// <summary>Baixa e instala a release. Lanca excecao com mensagem em portugues se algo der errado (nada e trocado).</summary>
    public static Task InstallAsync(ReleaseInfo release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!CanSelfUpdate || ExePath is not { } exe)
            throw new InvalidOperationException("esta cópia não pode se atualizar sozinha (rodando pelo código?)");
        return InstallToAsync(release, exe, progress, ct);
    }

    /// <summary>Instala a release no lugar de <paramref name="exe"/> (separado para testar sem trocar o exe em uso).</summary>
    public static async Task InstallToAsync(ReleaseInfo release, string exe, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        PreloadForRestart();
        var old = exe + ".old";
        if (release.AssetUrl is not { } url || !url.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"a release não tem o arquivo {AssetName}");

        var dir = Path.GetDirectoryName(exe)!;
        var zip = Path.Combine(Path.GetTempPath(), $"PKHeX.Modern-{UpdateChecker.Format(release.Version)}.zip");
        var fresh = exe + ".new"; // na mesma pasta: a troca e so renomear
        try
        {
            await DownloadAsync(url, zip, progress, ct).ConfigureAwait(false);
            if (release.AssetDigest is { } digest)
                VerifySha256(zip, digest);

            using (var archive = ZipFile.OpenRead(zip))
            {
                var entry = archive.GetEntry(ExeName) ?? throw new InvalidDataException($"o zip não tem o {ExeName}");
                entry.ExtractToFile(fresh, overwrite: true);
            }
            if (new FileInfo(fresh).Length < 1_000_000)
                throw new InvalidDataException("o executável baixado parece incompleto");

            if (File.Exists(old))
                File.Delete(old);
            File.Move(exe, old);
            try
            {
                File.Move(fresh, exe);
            }
            catch
            {
                File.Move(old, exe); // devolve o original
                throw;
            }
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"sem permissão para gravar em {dir}. Baixe pelo link da release");
        }
        finally
        {
            TryDelete(zip);
            TryDelete(fresh);
        }
    }

    /// <summary>
    /// O exe publicado e um arquivo unico: as bibliotecas do .NET ficam dentro dele e so sao carregadas quando usadas.
    /// Depois da troca, o arquivo no caminho original ja e o exe novo, e o processo antigo nao consegue mais carregar
    /// nada dele (o Reiniciar falhava com FileNotFoundException de System.Diagnostics.Process). Por isso o que o
    /// reinicio usa e carregado antes de trocar o exe.
    /// </summary>
    public static void PreloadForRestart()
    {
        using var current = Process.GetCurrentProcess();
        var info = new ProcessStartInfo(current.MainModule?.FileName ?? "x") { UseShellExecute = false };
        info.ArgumentList.Add("x");
        _ = info.WorkingDirectory;
    }

    /// <summary>Abre o exe (ja atualizado) de novo, reabrindo <paramref name="savePath"/> se houver.</summary>
    public static bool Restart(string? savePath)
    {
        if (ExePath is not { } exe)
            return false;
        try
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
            if (savePath is not null && File.Exists(savePath))
                info.ArgumentList.Add(savePath);
            Process.Start(info);
            return true;
        }
        catch
        {
            return false;
        }
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
            if (total > 0)
                progress?.Report((double)done / total);
        }
    }

    /// <summary>Confere o hash do arquivo com o "digest" da API do GitHub ("sha256:...").</summary>
    private static void VerifySha256(string path, string digest)
    {
        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return;
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
