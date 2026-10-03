using PKHeX.Core;
using static PKHeX.Core.GameVersion;

namespace PKHeX.Modern.Sprites;

/// <summary>Porte do <c>PKHeX.Drawing.Misc.WallpaperUtil</c>: papel de parede de cada caixa, pelo jogo e pelo indice.</summary>
public static class WallpaperUtil
{
    private const string DefaultWallpaper = "box_wp16xy";

    public static SpriteImage GetWallpaper(SaveFile sav, int box)
    {
        if (sav is not IBoxDetailWallpaper wp)
            return SpriteResources.Required(DefaultWallpaper);
        if (sav is SAV9ZA) // cidade (Lumiose)
            return SpriteResources.Required("box_wp02bdsp");
        if (sav is SAV8LA) // pasto
            return SpriteResources.Required("box_wp01bdsp");

        int wallpaper = wp.GetBoxWallpaper(box);
        return SpriteResources.Get(GetWallpaperResourceName(sav.Version, wallpaper)) ?? SpriteResources.Required(DefaultWallpaper);
    }

    public static string GetWallpaperResourceName(GameVersion version, int index)
    {
        index++; // os arquivos comecam em 1
        var suffix = GetResourceSuffix(version, index);
        var variant = version switch
        {
            SL when index is 20 => "_n", // Naranja
            VL when index is 20 => "_u", // Uva
            _ => string.Empty,
        };
        return $"box_wp{index:00}{suffix}{variant}";
    }

    private static string GetResourceSuffix(GameVersion version, int index) => version.Context switch
    {
        EntityContext.Gen3 when version == E => "e",
        EntityContext.Gen3 when FRLG.Contains(version) && index > 12 => "frlg",
        EntityContext.Gen3 => "rs",

        EntityContext.Gen4 when index <= 16 => "dp",
        EntityContext.Gen4 when version == Pt => "pt",
        EntityContext.Gen4 when HGSS.Contains(version) => "hgss",

        EntityContext.Gen5 => B2W2.Contains(version) && index > 16 ? "b2w2" : "bw",
        EntityContext.Gen6 => ORAS.Contains(version) && index > 16 ? "ao" : "xy",
        EntityContext.Gen7 => "xy", // parecido; o PKHeX usa o do X/Y
        EntityContext.Gen8b => "bdsp",
        EntityContext.Gen8 => "swsh",
        EntityContext.Gen9 => "sv",
        _ => string.Empty,
    };
}
