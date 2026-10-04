using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PKHeX.Modern.Services;

/// <summary>Uma release publicada no GitHub.</summary>
/// <param name="AssetUrl">Link do zip do exe (para a atualizacao automatica); null se a release nao tiver.</param>
/// <param name="AssetDigest">Hash do zip informado pelo GitHub ("sha256:..."), quando houver.</param>
public sealed record ReleaseInfo(Version Version, string Tag, string Name, string Url, DateTimeOffset? Published,
    string? AssetUrl = null, string? AssetDigest = null, string? Notes = null);

/// <summary>
/// Versao do app e verificacao de releases novas no GitHub (tags <c>modern-v*</c> do fork).
/// So le a lista publica de releases; nada e baixado nem enviado.
/// </summary>
public static class UpdateChecker
{
    public const string Repo = "carlosmozart/PKHeX";
    /// <summary>Nome planejado do repositorio. Ate renomear, a consulta usa <see cref="Repo"/> (que continua valendo depois, por redirecionamento).</summary>
    public const string FutureRepo = "carlosmozart/PKHeX-Modern";
    public const string RepoUrl = "https://github.com/" + Repo + "/tree/modern-ui/PKHeX.Modern";
    public const string ReleasesUrl = "https://github.com/" + Repo + "/releases";
    private const string TagPrefix = "modern-v";

    /// <summary>Versao deste executavel (do &lt;Version&gt; do csproj).</summary>
    public static Version Current { get; } = Normalize(typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0));

    public static string CurrentText => Format(Current);

    public static string Format(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>Release mais recente do PKHeX Modern (ignora rascunhos e pre-releases). Null se nao houver.</summary>
    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"PKHeX.Modern/{CurrentText}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases?per_page=30", ct).ConfigureAwait(false);
        return ParseLatest(json);
    }

    /// <summary>Escolhe a maior versao <c>modern-vX.Y.Z</c> da resposta da API de releases.</summary>
    public static ReleaseInfo? ParseLatest(string json) => ParseLatest(json, AutoUpdater.AssetName);

    /// <summary>Escolhe o asset indicado; separado para testar todas as plataformas sem rede.</summary>
    public static ReleaseInfo? ParseLatest(string json, string? assetName)
    {
        using var doc = JsonDocument.Parse(json);
        ReleaseInfo? best = null;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var d) && d.GetBoolean())
                continue;
            if (r.TryGetProperty("prerelease", out var p) && p.GetBoolean())
                continue;
            var tag = r.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase) || !Version.TryParse(tag[TagPrefix.Length..], out var v))
                continue;
            v = Normalize(v);
            if (best is not null && v <= best.Version)
                continue;
            DateTimeOffset? published = r.TryGetProperty("published_at", out var pa) && pa.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(pa.GetString(), out var dt) ? dt : null;
            string? assetUrl = null, assetDigest = null;
            if (r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    if (assetName is null || a.GetProperty("name").GetString() != assetName)
                        continue;
                    assetUrl = a.GetProperty("browser_download_url").GetString();
                    assetDigest = a.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String ? dg.GetString() : null;
                }
            }
            best = new ReleaseInfo(v, tag, r.GetProperty("name").GetString() ?? tag, r.GetProperty("html_url").GetString() ?? ReleasesUrl, published,
                assetUrl, assetDigest, r.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() : null);
        }
        return best;
    }
}

/// <summary>Abre links no navegador padrao.</summary>
public static class Links
{
    public const string AllGenWiki = "https://allgenwiki.carlosmozartbna.workers.dev/";

    /// <summary>Abre o link pelo sistema quando nao ha Process.Start (Android: Launcher do Avalonia).</summary>
    public static Func<string, bool>? Opener { get; set; }

    public static bool Open(string url)
    {
        if (Opener is { } open)
            return open(url);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
