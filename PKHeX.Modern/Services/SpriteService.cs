using System.IO;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Drawing.PokeSprite;

namespace PKHeX.Modern.Services;

/// <summary>Reaproveita o gerador de sprites do PKHeX e converte para bitmaps do Avalonia.</summary>
public static class SpriteService
{
    public static Bitmap? GetSprite(PKM pk, SaveFile sav, int box, int slot)
    {
        if (CoreAdapter.IsEmpty(pk))
            return null;
        using var gdi = pk.Sprite(sav, box, slot);
        return Convert(gdi);
    }

    public static Bitmap? GetSprite(PKM pk)
    {
        if (CoreAdapter.IsEmpty(pk))
            return null;
        using var gdi = pk.Sprite();
        return Convert(gdi);
    }

    private static Bitmap Convert(System.Drawing.Bitmap gdi)
    {
        using var ms = new MemoryStream();
        gdi.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        return new Bitmap(ms);
    }
}
