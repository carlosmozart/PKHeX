using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace PKHeX.Modern.Theme;

/// <summary>Cores de um tema numa variante (claro ou escuro).</summary>
public sealed record ThemePalette(string Background, string Sidebar, string Card, string Slot, string SlotHover, string Border, string Muted);

/// <summary>
/// Um tema completo: fundo, paineis, cartoes, bordas, arredondamento e fonte, nas versoes clara e escura.
/// Cada tema sugere uma cor de destaque (aplicada ao escolher o tema; da para trocar depois).
/// </summary>
public sealed record AppThemePreset(string Key, string Name, string Description, ThemePalette Dark, ThemePalette Light,
    double RadiusScale, string? FontFamily, string AccentKey, double BorderThickness = 1)
{
    /// <summary>Nome curto para o botao da barra lateral ("Z-A (Lumiose)" vira "Z-A").</summary>
    public string ShortName => Name.Split(' ')[0];
    /// <summary>Amostra para o menu: fundo do cartao com a borda do tema.</summary>
    public IBrush SwatchBackground { get; } = new SolidColorBrush(Color.Parse(Dark.Card));
    public IBrush SwatchBorder { get; } = new SolidColorBrush(Color.Parse(Dark.Border));
}

/// <summary>Uma fonte que o usuario pode escolher (Key "theme" = a do tema).</summary>
/// <param name="HeadingsOnly">Fonte larga: so nos titulos (16 px ou mais); o texto normal (14-15 px) fica na Inter.</param>
public sealed record AppFontChoice(string Key, string Name, string Description, string? Family, bool HeadingsOnly = false);

/// <summary>
/// Temas completos. Sobrescreve os pinceis do Palette.axaml nos dicionarios de tema do Application, os raios
/// (Radius3..Radius16, CardRadius, SlotRadius) e a fonte (AppFontFamily); tudo usa DynamicResource, entao muda na hora.
/// </summary>
public static class AppTheme
{
    /// <summary>Fonte pixelada embutida (Pixelify Sans, OFL); simbolos que ela nao tem vem da fonte padrao.</summary>
    private const string PixelFont = "avares://PKHeX.Modern.UI/Assets/Fonts#Pixelify Sans, $Default";
    private const string InterFont = "fonts:Inter#Inter, $Default";
    /// <summary>Recriacao da fonte dos Pokemon de Game Boy (Pokemon Classic, CC BY-SA 3.0: Assets/Fonts/PokemonClassic-LICENSE.txt).</summary>
    private const string PokemonGbFont = "avares://PKHeX.Modern.UI/Assets/Fonts#Pokemon Classic, $Default";

    /// <summary>Fontes da interface (menu ⚙ › Fonte). "Do tema" segue o tema completo (Pixel usa a Pixelify Sans).</summary>
    public static IReadOnlyList<AppFontChoice> Fonts { get; } =
    [
        new("theme", "Do tema", "Usa a fonte do tema escolhido (Pixelify Sans no tema Pixel, Inter nos outros).", null),
        new("inter", "Inter", "Fonte moderna e legível, a padrão do app.", InterFont),
        new("pixelify", "Pixelify Sans", "Fonte pixelada com acentos e negrito (OFL).", PixelFont),
        new("pokemongb", "Pokémon GB/GBC", "Recriação da fonte de Red/Blue/Yellow/Gold/Silver/Crystal (Pokemon Classic, de TheLouster115). Como as letras são largas, vale nos títulos; o texto normal fica na Inter.", PokemonGbFont, HeadingsOnly: true),
    ];

    /// <summary>Fonte escolhida (chave de <see cref="Fonts"/>); "theme" ou null = a do tema.</summary>
    public static string FontKey { get; private set; } = "theme";

    public static AppFontChoice FindFont(string? key) => Fonts.FirstOrDefault(f => f.Key == key) ?? Fonts[0];

    /// <summary>Troca a fonte na hora (sem mudar o resto do tema).</summary>
    public static void SetFont(string? key)
    {
        FontKey = FindFont(key).Key;
        if (Application.Current is { } app)
            ApplyFont(app);
    }

    /// <summary>
    /// AppFontFamily = fonte dos titulos e do resto; AppBodyFontFamily = texto normal (14-15 px, Styles.axaml), que
    /// so difere nas fontes largas (<see cref="AppFontChoice.HeadingsOnly"/>).
    /// </summary>
    private static void ApplyFont(Application app)
    {
        var family = FontFamily.Parse(CurrentFontFamily);
        app.Resources["AppFontFamily"] = family;
        app.Resources["AppBodyFontFamily"] = FindFont(FontKey).HeadingsOnly ? FontFamily.Parse(InterFont) : family;
    }

    /// <summary>Familia em uso: a escolhida ou, em "Do tema", a do tema atual.</summary>
    public static string CurrentFontFamily => FindFont(FontKey).Family ?? Current.FontFamily ?? InterFont;

    public static IReadOnlyList<AppThemePreset> Presets { get; } =
    [
        new("default", "Padrão", "Escuro neutro com cantos arredondados (o visual de sempre).",
            new("#14161B", "#1B1E25", "#20242C", "#2A2F39", "#353B48", "#2E333D", "#8C93A3"),
            new("#F4F5F8", "#FFFFFF", "#FFFFFF", "#EEF0F4", "#E1E4EB", "#E2E5EB", "#6B7280"),
            1, null, "red"),
        new("pss", "PSS (X/Y)", "Azul do Player Search System de X/Y, com cantos bem arredondados.",
            new("#0C1828", "#10213A", "#152C4A", "#1C3A5E", "#244A74", "#2B5784", "#8FB3D9"),
            new("#E4F1FB", "#FFFFFF", "#FFFFFF", "#D9EAF7", "#C6DEF2", "#B5D3EC", "#4A6B8C"),
            1.6, null, "cyan"),
        new("pixel", "Pixel (GBA)", "Menus do Game Boy Advance: cantos retos, bordas grossas e fonte pixelada.",
            new("#121812", "#1A231A", "#202D20", "#293A29", "#334A33", "#55735A", "#9DB79D"),
            new("#E6DEC6", "#F7F2E4", "#FFFCF3", "#EEE4C9", "#E2D5B0", "#5C5446", "#6E6452"),
            0, PixelFont, "green", 2),
        new("za", "Z-A (Lumiose)", "Preto e lima da Lumiose de Legends: Z-A, com cantos quase retos.",
            new("#0D0D0E", "#141416", "#1A1A1D", "#242428", "#2F2F34", "#38383E", "#9A9AA0"),
            new("#F1F1EC", "#FFFFFF", "#FFFFFF", "#E8E8E1", "#DCDCD3", "#CFCFC4", "#66665F"),
            0.35, null, "lime"),
    ];

    public static AppThemePreset Default => Presets[0];
    public static AppThemePreset Current { get; private set; } = Presets[0];

    public static AppThemePreset Find(string? key) => Presets.FirstOrDefault(p => p.Key == key) ?? Default;

    /// <summary>Aplica o tema (as duas variantes). Depois chame <see cref="AccentTheme.Apply"/> para a cor de destaque.</summary>
    public static void Apply(AppThemePreset theme)
    {
        Current = theme;
        if (Application.Current is not { } app)
            return;
        Fill(AccentTheme.GetThemeDictionary(app, ThemeVariant.Dark), theme.Dark);
        Fill(AccentTheme.GetThemeDictionary(app, ThemeVariant.Light), theme.Light);
        foreach (var r in new[] { 3, 4, 6, 8, 10, 12, 16 })
            app.Resources[$"Radius{r}"] = new CornerRadius(System.Math.Round(r * theme.RadiusScale));
        app.Resources["CardRadius"] = new CornerRadius(System.Math.Round(12 * theme.RadiusScale));
        app.Resources["SlotRadius"] = new CornerRadius(System.Math.Round(8 * theme.RadiusScale));
        app.Resources["CardBorderThickness"] = new Thickness(theme.BorderThickness);
        ApplyFont(app);
    }

    private static void Fill(ResourceDictionary d, ThemePalette p)
    {
        d["AppBackground"] = Brush(p.Background);
        d["SidebarBackground"] = Brush(p.Sidebar);
        d["CardBackground"] = Brush(p.Card);
        d["SlotBackground"] = Brush(p.Slot);
        d["SlotHover"] = Brush(p.SlotHover);
        d["CardBorder"] = Brush(p.Border);
        d["TextMuted"] = Brush(p.Muted);
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
