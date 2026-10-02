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
    /// <summary>Uma geracao de sprite por vez: o gerador do PKHeX (GDI) e chamado tambem fora da thread da interface.</summary>
    private static readonly object SpeciesLock = new();

    /// <summary>
    /// Area do icone de brilho que o PKHeX desenha no canto superior esquerdo dos shiny.
    /// O recorte ignora essa area (os cartoes mostram a propria estrela).
    /// </summary>
    private static readonly GdiRectangle ShinyIconArea;

    static SpriteService()
    {
        // O WinForms liga isto nas configuracoes (Sprite.ShinySprites). Desligado, o PKHeX usa o sprite
        // com as cores normais e so desenha o icone de brilho por cima.
        SpriteName.AllowShinySprite = true;

        var a = PKHeX.Drawing.PokeSprite.Properties.Resources.rare_icon_alt;
        var b = PKHeX.Drawing.PokeSprite.Properties.Resources.rare_icon_alt_2;
        ShinyIconArea = new GdiRectangle(0, 0, System.Math.Max(a.Width, b.Width), System.Math.Max(a.Height, b.Height));
    }

    public static AvaloniaBitmap? GetSprite(PKM pk, SaveFile sav, int box, int slot)
    {
        if (CoreAdapter.IsEmpty(pk))
            return null;
        lock (SpeciesLock)
        {
            using var gdi = pk.Sprite(sav, box, slot);
            return Convert(gdi, pk.IsShiny);
        }
    }

    private static readonly System.Collections.Generic.Dictionary<byte, AvaloniaBitmap?> Balls = [];

    /// <summary>Icone da bola (cacheado por tipo de bola).</summary>
    public static AvaloniaBitmap? GetBallSprite(byte ball)
    {
        if (ball == 0)
            return null;
        if (Balls.TryGetValue(ball, out var cached))
            return cached;
        AvaloniaBitmap? bmp;
        try
        {
            lock (SpeciesLock)
            {
                using var gdi = SpriteUtil.GetBallSprite(ball);
                using var ms = new MemoryStream();
                gdi.Save(ms, ImageFormat.Png); // sem recorte: a bola ja ocupa a imagem
                ms.Position = 0;
                bmp = new AvaloniaBitmap(ms);
            }
        }
        catch
        {
            bmp = null;
        }
        Balls[ball] = bmp;
        return bmp;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int, EntityContext), AvaloniaBitmap?> Items = new();

    /// <summary>
    /// Icone de um item (mochila, item segurado). <paramref name="item"/> na numeracao do save: Gen 1-3 tem numeracao
    /// propria e sao convertidas para a numeracao dos sprites. Cacheado.
    /// </summary>
    public static AvaloniaBitmap? GetItemSprite(int item, EntityContext context)
    {
        if (item <= 0)
            return null;
        return Items.GetOrAdd((item, context), key =>
        {
            try
            {
                var display = ItemConverter.GetItemDisplay(key.Item1, key.Item2);
                if (display <= 0)
                    return null;
                lock (SpeciesLock)
                {
                    var gdi = SpriteUtil.Spriter.GetItemSprite(display, key.Item2); // recurso compartilhado: nao descartar
                    using var ms = new MemoryStream();
                    gdi.Save(ms, ImageFormat.Png);
                    ms.Position = 0;
                    return new AvaloniaBitmap(ms);
                }
            }
            catch
            {
                return null;
            }
        });
    }

    /// <summary>Sprite de um encontro ou Mystery Gift (banco de encontros/eventos).</summary>
    public static AvaloniaBitmap? GetSprite(IEncounterTemplate enc)
    {
        // Sem o icone da bola que o Core desenha no canto: assim o recorte deixa o Pokemon grande no cartao.
        var shiny = enc.IsShiny ? Shiny.Always : Shiny.Never;
        lock (SpeciesLock)
        {
            using var gdi = SpriteUtil.GetSprite(enc.Species, enc.Form, 0, 0, 0, enc.IsEgg, shiny, enc.Context);
            return Convert(gdi, enc.IsShiny);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(ushort, byte, int, bool), AvaloniaBitmap?> Species = new();

    /// <summary>Sprite da especie ja gerado (ou null se ainda nao foi). Nao gera nada.</summary>
    public static bool TryGetCachedSpeciesSprite(ushort species, bool shiny, out AvaloniaBitmap? sprite, byte form = 0, int gender = 0)
        => Species.TryGetValue((species, form, gender, shiny), out sprite);

    /// <summary>
    /// Sprite da especie (forma base), normal ou shiny, sem bola nem item. Cacheado (usado na Pokedex).
    /// Pode ser chamado fora da thread da interface (a Pokedex gera os 1025 em segundo plano).
    /// </summary>
    public static AvaloniaBitmap? GetSpeciesSprite(ushort species, bool shiny, byte form = 0, int gender = 0, EntityContext context = EntityContext.None)
    {
        if (Species.TryGetValue((species, form, gender, shiny), out var cached))
            return cached;
        AvaloniaBitmap? bmp;
        lock (SpeciesLock)
        {
            if (Species.TryGetValue((species, form, gender, shiny), out cached))
                return cached;
            try
            {
                using var gdi = SpriteUtil.GetSprite(species, form, (byte)gender, 0, 0, false, shiny ? Shiny.Always : Shiny.Never, context);
                bmp = Convert(gdi, shiny);
            }
            catch
            {
                bmp = null;
            }
            Species[(species, form, gender, shiny)] = bmp;
        }
        return bmp;
    }

    public static AvaloniaBitmap? GetSprite(PKM pk)
    {
        if (CoreAdapter.IsEmpty(pk))
            return null;
        lock (SpeciesLock)
        {
            using var gdi = pk.Sprite();
            return Convert(gdi, pk.IsShiny);
        }
    }

    /// <summary>
    /// Os sprites do PKHeX tem bastante borda transparente; recortamos para que o Pokemon
    /// ocupe todo o espaco disponivel ao ser ampliado na interface.
    /// </summary>
    private static AvaloniaBitmap Convert(GdiBitmap gdi, bool shiny = false)
    {
        var bounds = GetOpaqueBounds(gdi, shiny ? ShinyIconArea : GdiRectangle.Empty);
        using var cropped = bounds.IsEmpty || bounds.Size == gdi.Size ? null : gdi.Clone(bounds, PixelFormat.Format32bppArgb);
        using var ms = new MemoryStream();
        (cropped ?? gdi).Save(ms, ImageFormat.Png);
        ms.Position = 0;
        return new AvaloniaBitmap(ms);
    }

    /// <param name="ignore">Area cujos pixels nao contam para o recorte (icone de brilho).</param>
    private static GdiRectangle GetOpaqueBounds(GdiBitmap bmp, GdiRectangle ignore)
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
                    if ((pixels[(y * bmp.Width) + x] >>> 24) < 16 || ignore.Contains(x, y))
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
