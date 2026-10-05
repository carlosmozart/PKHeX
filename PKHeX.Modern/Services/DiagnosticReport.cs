using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PKHeX.Modern.Services;

public sealed record SaveFolderCounts(int Files, int Read, int Ignored, int Errors);

/// <summary>Only diagnostic metadata and sanitized stack traces. No save data is read or exported.</summary>
public static class DiagnosticReport
{
    public static Func<string>? PlatformDescription { get; set; }
    public static Func<string, bool>? ShareText { get; set; }

    public static string Build(AppSettings settings, SaveFolderCounts? folderCounts = null, string? logPath = null,
        IEnumerable<string>? privateNames = null)
    {
        var secrets = (privateNames ?? []).Append(Environment.UserName).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
        var lines = new List<string>
        {
            "PKHeX Modern · " + UpdateChecker.CurrentText,
            Loc.T("Sistema") + ": " + (PlatformDescription?.Invoke() ?? $"{RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture}"),
            Loc.T("Idioma") + ": " + settings.UiLanguage,
            Loc.T("Tema") + $": {settings.ThemeKey ?? "default"} · " + Loc.T(settings.DarkTheme ? "Escuro" : "Claro"),
        };
        if (folderCounts is { } counts)
        {
            // Structured aggregates never contain filenames, trainer names or provider error messages.
            var summary = $"{counts.Files} arquivo(s), {counts.Read} lido(s) como possível save, {counts.Ignored} ignorado(s) pelo tamanho"
                + (counts.Errors > 0 ? $", {counts.Errors} com erro" : "");
            lines.Add(Loc.T("Pasta de saves") + ": " + Loc.T(summary));
        }
        lines.Add(Loc.T("Registro recente (mensagens e dados privados omitidos)"));
        try
        {
            var path = logPath ?? CrashLog.FilePath;
            // A bounded queue avoids loading a growing crash log into memory.
            var tail = new Queue<string>();
            if (File.Exists(path))
                foreach (var line in File.ReadLines(path)) { tail.Enqueue(line); if (tail.Count > 50) tail.Dequeue(); }
            lines.AddRange(tail.Select(SafeLogLine));
            if (tail.Count == 0) lines.Add(Loc.T("Nenhum erro registrado."));
        }
        catch { lines.Add(Loc.T("Registro indisponível.")); }
        return string.Join(Environment.NewLine, lines.Select(line => Redact(line, secrets)));
    }

    private static string SafeLogLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return "";
        // Exception messages are arbitrary strings and can contain trainers, serialized saves, or paths.
        var exception = Regex.Match(line, @"^(?<stamp>\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]\s*)?(?:\s*--->\s*)?(?<type>[\w.]+Exception)(?::.*)?$");
        if (exception.Success) return exception.Groups["stamp"].Value + exception.Groups["type"].Value + ": " + Loc.T("[mensagem omitida]");
        var frame = Regex.Match(line, @"^\s*(?:at|em)\s+(?<method>[\w.<>+`]+)\([^\r\n]*?\)(?<location>\s+(?:in|na|em)\s+.*)?$");
        if (frame.Success)
        {
            var number = Regex.Match(frame.Groups["location"].Value, @"(?:line|linha)\s+(\d+)$");
            return "   at " + frame.Groups["method"].Value + "(…)" + (number.Success ? " …/:" + number.Groups[1].Value : "");
        }
        if (line.TrimStart().StartsWith("---", StringComparison.Ordinal)) return "---";
        return Loc.T("[linha omitida]");
    }

    private static string Redact(string line, IEnumerable<string> secrets)
    {
        foreach (var secret in secrets) line = line.Replace(secret, "…", StringComparison.OrdinalIgnoreCase);
        // Defense for platform descriptions and metadata hooks, including UNC, Unix and content URIs.
        return Regex.Replace(line, @"(?:[A-Za-z]:[\\/]|\\\\|(?:content|file)://|/(?:Users|home|data|storage)/)[^\r\n]*", "…/");
    }
}
