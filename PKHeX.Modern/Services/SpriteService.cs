using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using PKHeX.Core;
using PKHeX.Drawing.PokeSprite;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace PKHeX.Modern.Services;

/// <summary>Reaproveita o gerador de sprites do PKHeX e converte para bitmaps do Avalonia.</summary>
public static class SpriteService
{
    public static AvaloniaBitmap? GetSprite(PKM pk, SaveFile sav, int box, int slot)
    {
        if (CoreAdapter.IsEmpty(pk))
            return null;
        using var gdi = pk.Sprite(sav, box, slot);
        return Convert(gdi);
    }

    /// <summary>Sprite de um encontro ou Mystery Gift (banco de encontros/eventos).</summary>
    public static AvaloniaBitmap? GetSprite(IEncounterTemplate enc)
    {
        // Sem o icone da bola que o Core desenha no canto: assim o recorte deixa o Pokemon grande no cartao.
        var shiny = enc.IsShiny ? Shiny.Always : Shiny.Never;
        using var gdi = SpriteUtil.GetSprite(enc.Species, enc.Form, 0, 0, 0, enc.IsEgg, shiny, enc.Context);
        return Convert(gdi);
    }

    public static AvaloniaBitmap? GetSprite(PKM pk)
    {
        if (CoreAdapter.IsEmpty(pk))
            return null;
        using var gdi = pk.Sprite();
        return Convert(gdi);
    }

    /// <summary>
    /// Os sprites do PKHeX tem bastante borda transparente; recortamos para que o Pokemon
    /// ocupe todo o espaco disponivel ao ser ampliado na interface.
    /// </summary>
    private static AvaloniaBitmap Convert(GdiBitmap gdi)
    {
        var bounds = GetOpaqueBounds(gdi);
        using var cropped = bounds.IsEmpty || bounds.Size == gdi.Size ? null : gdi.Clone(bounds, PixelFormat.Format32bppArgb);
        using var ms = new MemoryStream();
        (cropped ?? gdi).Save(ms, ImageFormat.Png);
        ms.Position = 0;
        return new AvaloniaBitmap(ms);
    }

    private static GdiRectangle GetOpaqueBounds(GdiBitmap bmp)
    {
        var rect = new GdiRectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new int[bmp.Width * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
                Marshal.Copy(data.Scan0 + (y * data.Stride), pixels, y * bmp.Width, bmp.Width);

            int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    if ((pixels[(y * bmp.Width) + x] >>> 24) < 16)
                        continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            return maxX < 0 ? GdiRectangle.Empty : GdiRectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
