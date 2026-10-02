using Avalonia;
using Avalonia.Media;
using PKHeX.Core;
using static PKHeX.Core.GameVersion;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace PKHeX.Modern.Services;

/// <summary>Arte de identificacao de um jogo: Pokemon da capa, cores da versao e sigla.</summary>
public sealed record GameArt(ushort Species, byte Form, uint Color1, uint Color2, string Short)
{
    /// <summary>Fundo do selo: degrade diagonal entre as duas cores da versao.</summary>
    public IBrush Background { get; } = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = [new GradientStop(Color.FromUInt32(Color1), 0), new GradientStop(Color.FromUInt32(Color2), 1)],
    };

    /// <summary>Sprite do Pokemon da capa (cacheado pelo SpriteService).</summary>
    public AvaloniaBitmap? Sprite => Species == 0 ? null : SpriteService.GetSpeciesSprite(Species, false, Form);

    private static readonly GameArt Unknown = new(0, 0, 0xFF5A6270, 0xFF2E333B, "?");

    /// <summary>
    /// Selo da versao. Usa so os sprites que o PKHeX ja traz (sem capas oficiais): o lendario/mascote da capa
    /// sobre as cores da versao, para reconhecer o jogo de relance.
    /// </summary>
    public static GameArt Get(GameVersion version) => version switch
    {
        RD => new(6, 0, 0xFFE53935, 0xFF7F1010, "Red"),
        GN => new(3, 0, 0xFF43A047, 0xFF1B5E20, "Green"),
        BU => new(9, 0, 0xFF1E88E5, 0xFF0D3C78, "Blue"),
        YW => new(25, 0, 0xFFFDD835, 0xFFC08A00, "Yellow"),
        RB or RBY or Gen1 => new(25, 0, 0xFFE53935, 0xFF1E88E5, "RBY"),
        GD => new(250, 0, 0xFFE0B23A, 0xFF8A5A10, "Gold"),
        SI => new(249, 0, 0xFFB8C2CC, 0xFF56606C, "Silver"),
        C => new(245, 0, 0xFF4FC3F7, 0xFF7E57C2, "Crystal"),
        GS or GSC or Gen2 => new(250, 0, 0xFFE0B23A, 0xFF56606C, "GSC"),
        R => new(383, 0, 0xFFD32F2F, 0xFF6D1010, "Ruby"),
        S => new(382, 0, 0xFF1E63C9, 0xFF0B2E6B, "Sapphire"),
        E => new(384, 0, 0xFF2E9E5B, 0xFF0F4D2A, "Emerald"),
        RS or RSE or Gen3 => new(383, 0, 0xFFD32F2F, 0xFF1E63C9, "RSE"),
        FR => new(6, 0, 0xFFF4511E, 0xFF8C1F05, "FireRed"),
        LG => new(3, 0, 0xFF7CB342, 0xFF2F5E12, "LeafGreen"),
        FRLG => new(6, 0, 0xFFF4511E, 0xFF7CB342, "FRLG"),
        CXD or COLO => new(196, 0, 0xFF8E5BB5, 0xFF2B1840, "Colosseum"),
        XD => new(249, 0, 0xFF5E4B8B, 0xFF1A1230, "XD"),
        D => new(483, 0, 0xFF5C8DC9, 0xFF233A63, "Diamond"),
        P => new(484, 0, 0xFFE58FB4, 0xFF7A3556, "Pearl"),
        Pt => new(487, 1, 0xFF9AA1AB, 0xFF3A2F45, "Platinum"),
        DP or DPPt or Gen4 => new(483, 0, 0xFF5C8DC9, 0xFFE58FB4, "DPPt"),
        HG => new(250, 0, 0xFFE0A030, 0xFF9A3412, "HeartGold"),
        SS => new(249, 0, 0xFFC8D0DA, 0xFF4A5A75, "SoulSilver"),
        HGSS => new(250, 0, 0xFFE0A030, 0xFF4A5A75, "HGSS"),
        BATREV => new(25, 0, 0xFF6A7FA8, 0xFF2B3550, "Battle Rev."),
        B => new(643, 0, 0xFF3A3F47, 0xFF0E1013, "Black"),
        W => new(644, 0, 0xFFF2F4F7, 0xFF9AA4B2, "White"),
        B2 => new(646, 2, 0xFF2F3540, 0xFF1C4F7A, "Black 2"),
        W2 => new(646, 1, 0xFFF2F4F7, 0xFFC0473F, "White 2"),
        BW or B2W2 or Gen5 => new(643, 0, 0xFF3A3F47, 0xFFC9CED6, "BW"),
        X => new(716, 0, 0xFF2D6CC0, 0xFF0F2B57, "X"),
        Y => new(717, 0, 0xFFD3302F, 0xFF5E0F0F, "Y"),
        XY or Gen6 => new(716, 0, 0xFF2D6CC0, 0xFFD3302F, "XY"),
        OR => new(383, 1, 0xFFE0402A, 0xFF6B1408, "Omega Ruby"),
        AS => new(382, 1, 0xFF2B6CD6, 0xFF0B2A66, "Alpha Sapphire"),
        ORAS or ORASDEMO => new(383, 1, 0xFFE0402A, 0xFF2B6CD6, "ORAS"),
        SN => new(791, 0, 0xFFF5A623, 0xFF9C4A06, "Sun"),
        MN => new(792, 0, 0xFF6A4FC8, 0xFF1D1450, "Moon"),
        US => new(800, 1, 0xFFF08A24, 0xFF8A2C08, "Ultra Sun"),
        UM => new(800, 2, 0xFF4E68D8, 0xFF1A1F5E, "Ultra Moon"),
        SM or USUM or Gen7 => new(791, 0, 0xFFF5A623, 0xFF6A4FC8, "SM"),
        GP => new(25, 0, 0xFFF7D02C, 0xFFC28B00, "Let's Go Pikachu"),
        GE => new(133, 0, 0xFFC28A52, 0xFF6E4320, "Let's Go Eevee"),
        GG or Gen7b => new(25, 0, 0xFFF7D02C, 0xFFC28A52, "Let's Go"),
        GO => new(25, 0, 0xFF2F80ED, 0xFF15A089, "GO"),
        SW => new(888, 0, 0xFF16A3E0, 0xFF0B4A7A, "Sword"),
        SH => new(889, 0, 0xFFE0245E, 0xFF6E0C2B, "Shield"),
        SWSH or Gen8 => new(888, 0, 0xFF16A3E0, 0xFFE0245E, "SwSh"),
        BD => new(483, 0, 0xFF3F7BD8, 0xFF132E63, "Brilliant Diamond"),
        SP => new(484, 0, 0xFFE57AA8, 0xFF6E2347, "Shining Pearl"),
        BDSP => new(483, 0, 0xFF3F7BD8, 0xFFE57AA8, "BDSP"),
        PLA => new(493, 0, 0xFFC9B27A, 0xFF4A3E22, "Legends Arceus"),
        SL => new(1007, 0, 0xFFE8432F, 0xFF7A1A0C, "Scarlet"),
        VL => new(1008, 0, 0xFF8B4FD1, 0xFF331566, "Violet"),
        SV or Gen9 => new(1007, 0, 0xFFE8432F, 0xFF8B4FD1, "SV"),
        ZA => new(718, 0, 0xFF3FB58A, 0xFF113D2E, "Legends Z-A"),
        _ => Unknown,
    };
}
