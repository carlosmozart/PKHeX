using System;
using System.Buffers;
using PKHeX.Core;
using PKHeX.Drawing.PokeSprite;

namespace PKHeX.Modern.Sprites;

/// <summary>
/// Porte do <c>PKHeX.Drawing.PokeSprite.SpriteUtil</c> (so o que o PKHeX.Modern usa) sem System.Drawing: estilo atual
/// dos sprites, sprite de um Pokemon (com sombra, Gigantamax e Alpha), sprite de um slot de caixa (equipe de batalha,
/// travado, equipe no Let's Go, inicial) e bolas.
/// </summary>
public static class SpriteUtil
{
    public static readonly SpriteBuilder Classic = new(SpriteStyle.Classic);
    public static readonly SpriteBuilder Artwork = new(SpriteStyle.Artwork);
    public static readonly SpriteBuilder Circle = new(SpriteStyle.Circle);

    /// <summary>Estilo usado agora (muda com o save ativo).</summary>
    public static SpriteBuilder Spriter { get; private set; } = Classic;

    private const int MaxSlotCount = 30;
    private static int SpriteWidth => Spriter.Width;
    private static int PartyMarkShiftX => SpriteWidth - 16;
    private static int SlotLockShiftX => SpriteWidth - 14;
    private static int SlotTeamShiftX => SpriteWidth - 19;

    /// <summary>Escolhe o estilo pelo save, como o PKHeX: circulo no Legends: Arceus, arte no Scarlet/Violet e Z-A.</summary>
    public static void Initialize(SaveFile sav)
    {
        Spriter = sav switch
        {
            SAV8LA => Circle,
            SAV9SV or SAV9ZA => Artwork,
            _ => Classic,
        };
        Spriter.Initialize(sav);
    }

    public static SpriteImage GetBallSprite(byte ball)
        => SpriteResources.Get(SpriteName.GetResourceStringBall(ball)) ?? SpriteResources.Required("_ball4");

    public static SpriteImage GetSprite(ushort species, byte form, byte gender, uint formarg, int item, bool isegg, Shiny shiny, EntityContext context = EntityContext.None)
        => Spriter.GetSprite(species, form, gender, formarg, item, isegg, shiny, context);

    /// <summary>Sprite do Pokemon com as marcas do PKHeX: sombra (Colosseum/XD), Gigantamax e Alpha.</summary>
    public static SpriteImage GetSprite(PKM pk)
    {
        var formarg = pk is IFormArgument f ? f.FormArgument : 0;
        var shiny = ShinyExtensions.GetType(pk);

        var img = GetSprite(pk.Species, pk.Form, pk.Gender, formarg, pk.SpriteItem, pk.IsEgg, shiny, pk.Context);
        if (pk is IShadowCapture { IsShadow: true })
        {
            const ushort Lugia = (int)Species.Lugia;
            if (pk.Species is Lugia) // Lugia sombrio do XD
                img = Spriter.GetSprite(Spriter.ShadowLugia, Lugia, pk.SpriteItem, pk.IsEgg, shiny, pk.Context);

            var glowImg = GetSpriteGlow(pk, 75, 0, 130, true);
            return SpriteImage.LayerImage(glowImg, img, 0, 0);
        }
        if (pk is IGigantamaxReadOnly { CanGigantamax: true })
        {
            var gm = SpriteResources.Required("dyna");
            return SpriteImage.LayerImage(img, gm, (img.Width - gm.Width) / 2, 0);
        }
        if (pk is IAlphaReadOnly { IsAlpha: true })
            return SpriteImage.LayerImage(img, SpriteResources.Required("alpha_alt"), SlotTeamShiftX, 0);
        return img;
    }

    /// <summary>
    /// Sprite de um slot (caixa ou equipe): o do Pokemon mais as marcas do slot. Com as configuracoes padrao do PKHeX
    /// a faixa do Tera Type tem espessura zero e a barra de experiencia fica desligada, entao nao sao desenhadas.
    /// </summary>
    public static SpriteImage GetSprite(PKM pk, SaveFile sav, int box, int slot)
    {
        bool inBox = (uint)slot < MaxSlotCount;
        var sprite = pk.Species == 0 ? Spriter.None : GetSprite(pk);
        if (inBox)
        {
            var flags = sav.GetBoxSlotFlags(box, slot);
            if (flags.IsBattleTeam() >= 0)
                sprite = SpriteImage.LayerImage(sprite, SpriteResources.Required("team"), SlotTeamShiftX, 0);
            if (flags.HasFlag(StorageSlotSource.Locked))
                sprite = SpriteImage.LayerImage(sprite, SpriteResources.Required("locked"), SlotLockShiftX, 0);
            int party = flags.IsParty();
            if (party >= 0)
                sprite = SpriteImage.LayerImage(sprite, SpriteResources.Required($"party{party + 1}"), PartyMarkShiftX, 0);
            if (flags.HasFlag(StorageSlotSource.Starter))
                sprite = SpriteImage.LayerImage(sprite, SpriteResources.Required("starter"), 0, 0);
        }
        return sprite;
    }

    private static SpriteImage GetSpriteGlow(PKM pk, byte blue, byte green, byte red, bool forceHollow)
    {
        bool egg = pk.IsEgg;
        var formarg = pk is IFormArgument f ? f.FormArgument : 0;
        var shiny = pk.IsShiny ? Shiny.Always : Shiny.Never;
        var baseSprite = GetSprite(pk.Species, pk.Form, pk.Gender, formarg, 0, egg, shiny, pk.Context);
        return GetSpriteGlow(baseSprite, blue, green, red, forceHollow || egg);
    }

    /// <summary>Contorno brilhante em volta do sprite (Totem, sombra). Oco = so o brilho, sem o Pokemon.</summary>
    public static SpriteImage GetSpriteGlow(SpriteImage baseSprite, byte blue, byte green, byte red, bool forceHollow = false)
    {
        var pixels = (byte[])baseSprite.Pixels.Clone();
        if (!forceHollow)
        {
            SpriteImage.GlowEdges(pixels, blue, green, red, baseSprite.Width);
            return new SpriteImage(baseSprite.Width, baseSprite.Height, pixels);
        }

        // Transparencia parcial deixaria o fundo vazar: deixa opaco, gera o brilho e tira os pixels do Pokemon.
        var temp = ArrayPool<byte>.Shared.Rent(pixels.Length);
        var original = temp.AsSpan(0, pixels.Length);
        pixels.CopyTo(original);

        SpriteImage.SetAllUsedPixelsOpaque(pixels);
        SpriteImage.GlowEdges(pixels, blue, green, red, baseSprite.Width);
        SpriteImage.RemovePixels(pixels, original);

        original.Clear();
        ArrayPool<byte>.Shared.Return(temp);
        return new SpriteImage(baseSprite.Width, baseSprite.Height, pixels);
    }
}
