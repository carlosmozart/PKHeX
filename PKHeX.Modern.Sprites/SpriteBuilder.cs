using PKHeX.Core;
using PKHeX.Drawing.PokeSprite;

namespace PKHeX.Modern.Sprites;

/// <summary>Estilo dos sprites 68x56 do PKHeX: classico ("b_"), arte ("a_", Scarlet/Violet e Z-A) ou circulo ("c_", Legends: Arceus).</summary>
public enum SpriteStyle
{
    Classic,
    Artwork,
    Circle,
}

/// <summary>
/// Porte do <c>PKHeX.Drawing.PokeSprite.SpriteBuilder</c> (e dos tres 5668) sem System.Drawing. Mesma ordem de camadas,
/// mesmas posicoes e mesmas opacidades, com as configuracoes padrao do PKHeX (ovo como item segurado).
/// </summary>
public sealed class SpriteBuilder(SpriteStyle style)
{
    public SpriteStyle Style { get; } = style;

    public int Width => 68;
    public int Height => 56;

    private const int ItemShiftX = 2;
    private const int ItemShiftY = 2;
    private const int ItemMaxSize = 32;
    private const int EggItemShiftX = 18;
    private const int EggItemShiftY = 1;

    private const double UnknownFormTransparency = 0.5;
    private const double ShinyTransparency = 0.7;
    private const double EggUnderLayerTransparency = 0.33;

    /// <summary>Ovo desenhado como item segurado (padrao do PKHeX); falso = Pokemon apagado com o ovo por cima.</summary>
    public static bool ShowEggSpriteAsItem { get; set; } = true;

    private char Prefix => Style switch { SpriteStyle.Artwork => 'a', SpriteStyle.Circle => 'c', _ => 'b' };
    private char SecondaryPrefix => Style == SpriteStyle.Classic ? 'c' : 'b';
    private string ItemPrefix => Style == SpriteStyle.Artwork ? "aitem_" : "bitem_";

    public SpriteImage None => SpriteResources.Required("b_0");
    private static SpriteImage Unknown => SpriteResources.Required("b_unknown");
    public SpriteImage UnknownItem => SpriteResources.Required("bitem_unk");
    public SpriteImage ItemTM => SpriteResources.Required(Style == SpriteStyle.Artwork ? "aitem_tm" : "bitem_tm");
    public SpriteImage ItemTR => SpriteResources.Required("bitem_tr");
    public SpriteImage ShadowLugia => SpriteResources.Required("b_249x");

    private SpriteImage GetEggSprite(ushort species)
    {
        var p = Style == SpriteStyle.Artwork ? 'a' : 'b';
        return SpriteResources.Required(species == (int)Species.Manaphy ? $"{p}_490_e" : $"{p}_egg");
    }

    private string GetSpriteStringSpeciesOnly(ushort species) => $"{Prefix}_{species}";
    private string GetSpriteAll(ushort species, byte form, byte gender, uint formarg, bool shiny, EntityContext context)
        => Prefix + SpriteName.GetResourceStringSprite(species, form, gender, formarg, context, shiny);
    private string GetSpriteAllSecondary(ushort species, byte form, byte gender, uint formarg, bool shiny, EntityContext context)
        => SecondaryPrefix + SpriteName.GetResourceStringSprite(species, form, gender, formarg, context, shiny);

    /// <summary>O estilo tem o sprite dessa especie (o classico nao tem as especies da Gen 9).</summary>
    public bool HasSpecies(ushort species) => SpriteResources.Exists(GetSpriteStringSpeciesOnly(species));

    private GameVersion _version;

    /// <summary>Prepara para o save (Gen 3: a forma do Deoxys depende da versao).</summary>
    public void Initialize(SaveFile sav)
    {
        if (sav.Generation != 3)
            return;
        _version = sav.Version;
        if (_version == GameVersion.FRLG)
            _version = ReferenceEquals(sav.Personal, PersonalTable.FR) ? GameVersion.FR : GameVersion.LG;
    }

    private static byte GetDeoxysForm(GameVersion version) => version switch
    {
        GameVersion.FR => 1,
        GameVersion.LG => 2,
        GameVersion.E => 3,
        _ => 0,
    };

    private static byte GetArceusForm4(byte form) => form switch
    {
        > 9 => --form,
        9 => byte.MaxValue, // tipo ??? (Curse): sem sprite, mostra como forma desconhecida
        _ => form,
    };

    public SpriteImage GetSprite(ushort species, byte form, byte gender, uint formarg, int heldItem, bool isEgg, Shiny shiny = Shiny.Never, EntityContext context = EntityContext.None)
    {
        if (species == 0)
            return None;

        if (context == EntityContext.Gen3 && species == (int)Species.Deoxys)
            form = GetDeoxysForm(_version);
        else if (context == EntityContext.Gen4 && species == (int)Species.Arceus)
            form = GetArceusForm4(form);

        var baseImage = GetBaseImage(species, form, gender, formarg, shiny.IsShiny(), context);
        return GetSprite(baseImage, species, heldItem, isEgg, shiny, context);
    }

    public SpriteImage GetSprite(SpriteImage baseSprite, ushort species, int heldItem, bool isEgg, Shiny shiny, EntityContext context = EntityContext.None)
    {
        if (isEgg)
            baseSprite = LayerOverImageEgg(baseSprite, species, heldItem != 0);
        if (heldItem > 0)
            baseSprite = LayerOverImageItem(baseSprite, heldItem, context);
        if (shiny.IsShiny())
        {
            if (shiny == Shiny.AlwaysSquare && !context.IsSquareShinyDifferentiated)
                shiny = Shiny.Always;
            baseSprite = LayerOverImageShiny(baseSprite, shiny);
        }
        return baseSprite;
    }

    private SpriteImage GetBaseImage(ushort species, byte form, byte gender, uint formarg, bool shiny, EntityContext context)
    {
        var img = FormInfo.IsTotemForm(species, form, context)
            ? GetBaseImageTotem(species, form, gender, formarg, shiny, context)
            : GetBaseImageDefault(species, form, gender, formarg, shiny, context);
        return img ?? GetBaseImageFallback(species, form, gender, formarg, shiny, context);
    }

    private SpriteImage? GetBaseImageTotem(ushort species, byte form, byte gender, uint formarg, bool shiny, EntityContext context)
    {
        var baseform = FormInfo.GetTotemBaseForm(species, form);
        var b = GetBaseImageDefault(species, baseform, gender, formarg, shiny, context);
        if (b is null)
            return null;
        var layer = SpriteUtil.GetSpriteGlow(b, 0, 165, 255, true);
        return SpriteImage.LayerImage(b, layer, 0, 0);
    }

    private SpriteImage? GetBaseImageDefault(ushort species, byte form, byte gender, uint formarg, bool shiny, EntityContext context)
    {
        return SpriteResources.Get(GetSpriteAll(species, form, gender, formarg, shiny, context))
            ?? SpriteResources.Get(GetSpriteAllSecondary(species, form, gender, formarg, shiny, context));
    }

    private SpriteImage GetBaseImageFallback(ushort species, byte form, byte gender, uint formarg, bool shiny, EntityContext context)
    {
        if (shiny)
        {
            var img = GetBaseImageDefault(species, form, gender, formarg, false, context);
            if (img is not null)
                return img;
        }
        var baseImage = SpriteResources.Get(GetSpriteStringSpeciesOnly(species));
        if (baseImage is null)
            return Unknown;
        return SpriteImage.LayerImage(baseImage, Unknown, 0, 0, UnknownFormTransparency);
    }

    private SpriteImage LayerOverImageItem(SpriteImage baseImage, int item, EntityContext context)
    {
        var itemimg = GetItemSprite(item, context);
        int x = baseImage.Width - itemimg.Width - ((ItemMaxSize - itemimg.Width) / 4) - ItemShiftX;
        int y = baseImage.Height - itemimg.Height - ItemShiftY;
        return SpriteImage.LayerImage(baseImage, itemimg, x, y);
    }

    /// <summary>Icone do item (numeracao dos sprites); TMs/TRs viram o icone generico, como no PKHeX.</summary>
    public SpriteImage GetItemSprite(int item, EntityContext context)
    {
        var lump = HeldItemLumpUtil.GetIsLump(item, context);
        return lump switch
        {
            HeldItemLumpImage.TechnicalMachine => ItemTM,
            HeldItemLumpImage.TechnicalRecord => ItemTR,
            _ => SpriteResources.Get(ItemPrefix + item) ?? UnknownItem,
        };
    }

    private static SpriteImage LayerOverImageShiny(SpriteImage baseImage, Shiny shiny)
    {
        var rare = SpriteResources.Required(shiny is Shiny.AlwaysSquare ? "rare_icon_alt_2" : "rare_icon_alt");
        return SpriteImage.LayerImage(baseImage, rare, 0, 0, ShinyTransparency);
    }

    private SpriteImage LayerOverImageEgg(SpriteImage baseImage, ushort species, bool hasItem)
    {
        if (ShowEggSpriteAsItem && !hasItem)
            return SpriteImage.LayerImage(baseImage, GetEggSprite(species), EggItemShiftX, EggItemShiftY);
        baseImage.ChangeOpacity(EggUnderLayerTransparency);
        return SpriteImage.LayerImage(baseImage, GetEggSprite(species), 0, 0);
    }
}
