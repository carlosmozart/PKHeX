using System.Collections.Generic;
using System.IO;
using PKHeX.Core;
using static PKHeX.Core.GameVersion;

namespace PKHeX.Modern.Services;

/// <summary>
/// Versao e idioma dos saves da Gen 1-3 pelo nome. Esses saves nao gravam qual jogo do par sao (Red/Blue,
/// Gold/Silver, Ruby/Sapphire, FireRed/LeafGreen) nem o idioma ocidental. O PKHeX.Core so olha o nome do arquivo
/// (e nao olha em saves dentro de zip); aqui tambem vale a entrada do zip, o nome do zip e a pasta.
/// Sem pista, o save fica com a versao do par (RB, GS, RS, FRLG) e o app mostra o selo duplo.
/// </summary>
public static class SaveNameHint
{
    /// <summary>Aplica a primeira pista valida. Retorna true se mudou versao ou idioma.</summary>
    public static bool Apply(SaveFile sav, string? path)
    {
        if (path is null || sav.Generation > 3 || GetHint(sav) is not { } hint)
            return false;
        foreach (var name in Candidates(path))
        {
            var r = sav.Generation switch
            {
                1 => SaveLanguage.InferFrom1(name, hint),
                2 => SaveLanguage.InferFrom2(name, hint),
                _ => SaveLanguage.InferFrom3(name, hint),
            };
            if (!IsValid(sav, r))
                continue;
            bool changed = sav.Version != r.Version || sav.Language != (int)r.Language;
            sav.Language = (int)r.Language;
            if (sav is SAV3FRLG frlg)
                frlg.ResetPersonal(r.Version);
            else
                sav.Version = r.Version;
            return changed;
        }
        return false;
    }

    /// <summary>Familia de versoes possiveis para o tipo de save (null: o tipo ja diz o jogo e o idioma).</summary>
    private static GameVersion? GetHint(SaveFile sav) => sav switch
    {
        SAV1 s => s.Version == YW ? YW : s.Japanese ? RBY : RB,
        SAV2 s => s.Korean ? null : s.Version == C ? C : GS,
        SAV3RS => RS,
        SAV3E => E,
        SAV3FRLG => FRLG,
        _ => null,
    };

    private static bool IsValid(SaveFile sav, SaveLanguageResult r)
    {
        if (r == default)
            return false;
        return sav switch
        {
            SAV1 s => s.Japanese == (r.Language == LanguageID.Japanese) && (r.Version != BU || s.Japanese),
            SAV2 s => s.Japanese == (r.Language == LanguageID.Japanese) && r.Language != LanguageID.Korean,
            SAV3 s when s.Japanese != (r.Language == LanguageID.Japanese) => false,
            SAV3RS => r.Version is R or S,
            SAV3E => r.Version is E,
            SAV3FRLG => r.Version is FR or LG,
            _ => false,
        };
    }

    /// <summary>
    /// Nomes a testar, do mais especifico ao mais geral: arquivo (ou entrada do zip), pasta da entrada, nome do zip
    /// e a pasta onde esta o arquivo. Sem extensao, para ".sav"/".srm" nao contarem.
    /// </summary>
    public static IEnumerable<string> Candidates(string path)
    {
        string file;
        if (ZipSaves.IsZipPath(path, out var zip, out var entry))
        {
            var e = entry.Replace('\\', '/');
            yield return Path.GetFileNameWithoutExtension(e);
            if (Path.GetDirectoryName(e) is { Length: > 0 } entryDir)
                yield return Path.GetFileName(entryDir);
            yield return Path.GetFileNameWithoutExtension(zip);
            file = zip;
        }
        else
        {
            yield return Path.GetFileNameWithoutExtension(path);
            file = path;
        }
        if (Path.GetFileName(Path.GetDirectoryName(file)) is { Length: > 0 } folder)
            yield return folder;
    }

    /// <summary>True se o save e de um par que o app nao conseguiu separar (mostra o selo duplo).</summary>
    public static bool IsPair(GameVersion version) => version is RB or RBY or GS or RS or FRLG;
}
