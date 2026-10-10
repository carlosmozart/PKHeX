using System.Collections.Generic;
using Avalonia;

namespace PKHeX.Modern.Theme;

/// <summary>
/// Tamanho dos sprites nas grades (caixas, equipe do outro save, bank e Pokedex). Os <c>Image</c> dessas grades usam
/// DynamicResource (SlotSpriteSize, CompactSpriteSize, DexSpriteSize), entao a troca vale na hora.
/// </summary>
public static class SpriteScale
{
    public static IReadOnlyList<string> Names { get; } = ["Pequeno", "Normal", "Grande", "Enorme"];
    private static readonly double[] Factors = [0.75, 1, 1.25, 1.5];

    /// <summary>Lado do cartao da Pokedex no tamanho atual (a grade usa como celula).</summary>
    public static double DexCardSize { get; private set; } = 104;
    /// <summary>Tamanho trocado (a grade da Pokedex se refaz).</summary>
    public static event System.EventHandler? Changed;

    public static int Clamp(int index) => System.Math.Clamp(index, 0, Factors.Length - 1);

    public static void Apply(int index)
    {
        if (Application.Current is not { } app)
            return;
        var f = Factors[Clamp(index)];
        app.Resources["SlotSpriteSize"] = 84 * f;
        app.Resources["CompactSpriteSize"] = 48 * f;
        app.Resources["DexSpriteSize"] = 48 * f;
        // Os cartoes crescem e encolhem junto com o sprite (sem isso, no Pequeno sobrava cartao e no Enorme cortava).
        // No Pequeno, texto e espacos menores tambem: so o sprite menor quase nao muda a altura do cartao.
        bool small = f < 1;
        app.Resources["SlotCardMinHeight"] = small ? 0 : 150 + 84 * (f - 1);
        app.Resources["SlotCardPadding"] = small ? new Thickness(6, 4, 6, 5) : new Thickness(8, 6, 8, 8);
        app.Resources["SlotSpriteMargin"] = small ? new Thickness(3, 1) : new Thickness(6, 4);
        app.Resources["SlotNameSize"] = small ? 12.0 : 14.0;
        app.Resources["SlotLevelSize"] = small ? 11.0 : 12.0;
        app.Resources["CompactCardHeight"] = small ? 92.0 : 104 + 48 * (f - 1);
        DexCardSize = small ? 96.0 : 104 + 48 * (f - 1);
        app.Resources["DexCardHeight"] = DexCardSize;
        app.Resources["DexCardWidth"] = DexCardSize;
        Changed?.Invoke(null, System.EventArgs.Empty);
    }
}
