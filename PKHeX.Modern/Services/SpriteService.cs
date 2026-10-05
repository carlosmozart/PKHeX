using System.Drawing;
using System.Runtime.InteropServices;
using PKHeX.Core;
using PKHeX.Modern.Sprites;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace PKHeX.Modern.Services;

/// <summary>
/// Sprites do PKHeX (PKHeX.Modern.Sprites: os mesmos PNGs e a mesma montagem do PKHeX, sem System.Drawing, entao
/// funciona fora do Windows) convertidos para bitmaps do Avalonia.
/// </summary>
public static class SpriteService
{
    public static AvaloniaBitmap GetBoxWallpaper(SaveFile sav, int box) => ToAvalonia(WallpaperUtil.GetWallpaper(sav, box), 4);

    /// <summary>Uma geracao de sprite por vez: o estilo atual e o estado do gerador sao globais, e a Pokedex gera em segundo plano.</summary>
    private static readonly object SpeciesLock = new();

    /// <summary>
    /// Area do icone de brilho que o PKHeX desenha no canto superior esquerdo dos shiny.
    /// O recorte ignora essa area (os cartoes mostram a propria estrela).
    /// </summary>
    private static readonly Rectangle ShinyIconArea;

    static SpriteService()
    {
        // O WinForms liga isto nas configuracoes (Sprite.ShinySprites). Desligado, o PKHeX usa o sprite
        // com as cores normais e so desenha o icone de brilho por cima.
        PKHeX.Drawing.PokeSprite.SpriteName.AllowShinySprite = true;

        var a = SpriteResources.Required("rare_icon_alt");
        var b = SpriteResources.Required("rare_icon_alt_2");
        ShinyIconArea = new Rectangle(0, 0, System.Math.Max(a.Width, b.Width), System.Math.Max(a.Height, b.Height));
    }

    /// <summary>Escolhe o estilo dos sprites pelo save ativo (classico, arte no Scarlet/Violet e Z-A, circulo no Legends: Arceus).</summary>
    public static void Activate(SaveFile sav)
    {
        lock (SpeciesLock)
            SpriteUtil.Initialize(sav);
    }

    public static AvaloniaBitmap? GetSprite(PKM pk, SaveFile sav, int box, int slot)
    {
        if (CoreAdapter.IsEmpty(pk))
            return null;
        lock (SpeciesLock)
            return Convert(SpriteUtil.GetSprite(pk, sav, box, slot), pk.IsShiny);
    }

    /// <summary>
    /// Gerador de sprites para a especie. No modo classico (saves ate a Gen 8) o PKHeX nao tem as especies da Gen 9
    /// (Koraidon, Miraidon...): elas so existem no modo arte. Nesse caso usa o gerador de arte, senao o selo do
    /// Scarlet/Violet, a Pokedex e o bank mostrariam o sprite "desconhecido".
    /// </summary>
    private static SpriteBuilder BuilderFor(ushort species)
    {
        var current = SpriteUtil.Spriter;
        if (current != SpriteUtil.Classic || species == 0 || current.HasSpecies(species))
            return current;
        return SpriteUtil.Artwork;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<byte, AvaloniaBitmap?> Balls = new();

    /// <summary>Icone da bola (cacheado por tipo de bola).</summary>
    public static AvaloniaBitmap? GetBallSprite(byte ball)
    {
        if (ball == 0)
            return null;
        return Balls.GetOrAdd(ball, static b =>
        {
            try { return ToAvalonia(SpriteUtil.GetBallSprite(b), PixelScale); } // sem recorte: a bola ja ocupa a imagem
            catch { return null; }
        });
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<byte, AvaloniaBitmap?> GrayBalls = new();

    /// <summary>Icone da bola em cinza e meio apagado (bolas ainda "vazias" da animacao de atualizacao).</summary>
    public static AvaloniaBitmap? GetGrayBallSprite(byte ball)
    {
        if (ball == 0)
            return null;
        return GrayBalls.GetOrAdd(ball, static b =>
        {
            try
            {
                var img = SpriteUtil.GetBallSprite(b).Clone();
                var px = img.Pixels;
                for (int i = 0; i + 3 < px.Length; i += 4)
                {
                    int y = (px[i] * 29 + px[i + 1] * 150 + px[i + 2] * 77) >> 8;
                    px[i] = px[i + 1] = px[i + 2] = (byte)(96 + y / 3);
                    px[i + 3] = (byte)(px[i + 3] * 3 / 5);
                }
                return ToAvalonia(img, PixelScale);
            }
            catch { return null; }
        });
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
                SpriteImage img;
                lock (SpeciesLock)
                    img = SpriteUtil.Spriter.GetItemSprite(display, key.Item2);
                return ToAvalonia(img, PixelScale);
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
        // Sem o icone da bola que o PKHeX desenha no canto: assim o recorte deixa o Pokemon grande no cartao.
        var shiny = enc.IsShiny ? Shiny.Always : Shiny.Never;
        lock (SpeciesLock)
            return Convert(BuilderFor(enc.Species).GetSprite(enc.Species, enc.Form, 0, 0, 0, enc.IsEgg, shiny, enc.Context), enc.IsShiny);
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
                var img = BuilderFor(species).GetSprite(species, form, (byte)gender, 0, 0, false, shiny ? Shiny.Always : Shiny.Never, context);
                bmp = Convert(img, shiny, CacheScale);
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
            var builder = BuilderFor(pk.Species);
            var img = builder == SpriteUtil.Spriter
                ? SpriteUtil.GetSprite(pk)
                : builder.GetSprite(pk.Species, pk.Form, pk.Gender, pk is IFormArgument f ? f.FormArgument : 0, pk.SpriteItem, pk.IsEgg,
                    pk.IsShiny ? Shiny.Always : Shiny.Never, pk.Context);
            return Convert(img, pk.IsShiny);
        }
    }

    /// <summary>
    /// Os sprites do PKHeX tem bastante borda transparente; recortamos para que o Pokemon
    /// ocupe todo o espaco disponivel ao ser ampliado na interface.
    /// </summary>
    private static AvaloniaBitmap Convert(SpriteImage img, bool shiny = false, int scale = PixelScale)
    {
        var bounds = img.GetOpaqueBounds(shiny ? ShinyIconArea : Rectangle.Empty);
        var cropped = bounds.IsEmpty || (bounds.Width == img.Width && bounds.Height == img.Height) ? img : img.Crop(bounds);
        return ToAvalonia(cropped, scale);
    }

    /// <summary>Ampliacao inteira (nearest neighbor) dos sprites. Ver <see cref="ToAvalonia"/>.</summary>
    private const int PixelScale = 4;
    /// <summary>Ampliacao dos sprites guardados em cache (Pokedex: ~1350 especies, exibidas pequenas).</summary>
    private const int CacheScale = 2;

    /// <summary>
    /// Converte para o Avalonia ampliando <paramref name="scale"/> vezes com nearest neighbor. Na tela, qualquer escala
    /// final (inclusive 125%/150% do Windows) vira uma pequena reducao de um bitmap grande, e os pixels saem nitidos e
    /// uniformes em vez de borrados ou tortos. Toda Image de sprite precisa de tamanho fixo (Width/Height ou moldura).
    /// O DPI fica em 96: com DPI maior, a Image do Avalonia 11.3 desenha so o canto superior esquerdo.
    /// </summary>
    private static AvaloniaBitmap ToAvalonia(SpriteImage src, int scale)
    {
        var big = src.Scale(scale);
        var handle = GCHandle.Alloc(big.Pixels, GCHandleType.Pinned);
        try
        {
            // BGRA sem pre-multiplicacao (o mesmo formato do System.Drawing 32bppArgb).
            return new AvaloniaBitmap(Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul,
                handle.AddrOfPinnedObject(), new Avalonia.PixelSize(big.Width, big.Height), new Avalonia.Vector(96, 96), big.Width * 4);
        }
        finally
        {
            handle.Free();
        }
    }
}
