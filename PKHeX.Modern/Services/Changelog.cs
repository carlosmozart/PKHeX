using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace PKHeX.Modern.Services;

public sealed record ChangelogSection(string Title, IReadOnlyList<string> Items);

/// <summary>Uma versao do changelog: titulo ("0.2.0"), data e secoes com itens.</summary>
public sealed record ChangelogVersion(string Title, string Date, string Intro, IReadOnlyList<ChangelogSection> Sections);

/// <summary>
/// Le o CHANGELOG.md embutido no executavel (pagina Ajuda › Novidades).
/// Formato: "## versao — data", "### secao", "- item" e paragrafos soltos (introducao da versao).
/// </summary>
public static partial class Changelog
{
    private static IReadOnlyList<ChangelogVersion>? _versions;

    public static IReadOnlyList<ChangelogVersion> Versions => _versions ??= Load();

    private static IReadOnlyList<ChangelogVersion> Load()
    {
        using var stream = typeof(Changelog).Assembly.GetManifestResourceStream("CHANGELOG.md");
        if (stream is null)
            return [];
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static IReadOnlyList<ChangelogVersion> Parse(string text)
    {
        var result = new List<ChangelogVersion>();
        string? title = null, date = "", intro = "";
        var sections = new List<ChangelogSection>();
        string? sectionTitle = null;
        var items = new List<string>();

        void CloseSection()
        {
            if (sectionTitle is not null || items.Count > 0)
                sections.Add(new ChangelogSection(sectionTitle ?? "", [.. items]));
            sectionTitle = null;
            items = [];
        }
        void CloseVersion()
        {
            CloseSection();
            if (title is not null)
                result.Add(new ChangelogVersion(title, date, intro.Trim(), [.. sections]));
            sections = [];
            intro = "";
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("## "))
            {
                CloseVersion();
                var parts = line[3..].Split(" — ", 2);
                title = Clean(parts[0]);
                date = parts.Length > 1 ? Clean(parts[1]) : "";
            }
            else if (line.StartsWith("### "))
            {
                CloseSection();
                sectionTitle = Clean(line[4..]);
            }
            else if (line.TrimStart().StartsWith("- "))
            {
                items.Add(Clean(line.TrimStart()[2..]));
            }
            else if (title is not null && sectionTitle is null && items.Count == 0 && line.Trim().Length > 0 && !line.StartsWith('#'))
            {
                intro += Clean(line.Trim()) + " ";
            }
        }
        CloseVersion();
        return result;
    }

    /// <summary>Tira a marcacao Markdown simples (negrito, codigo, links) para mostrar como texto.</summary>
    private static string Clean(string s)
    {
        s = LinkRegex().Replace(s, "$1");
        return s.Replace("**", "").Replace("`", "").Trim();
    }

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex LinkRegex();
}
