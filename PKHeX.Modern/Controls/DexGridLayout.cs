using System;
using Avalonia.Layout;

namespace PKHeX.Modern.Controls;

/// <summary>
/// Grade da Pokedex que acompanha o tamanho dos sprites (Theme.SpriteScale). O UniformGridLayout nao aceita
/// DynamicResource e guarda o tamanho da celula, entao a troca de tamanho e aplicada aqui.
/// </summary>
public sealed class DexGridLayout : UniformGridLayout
{
    public DexGridLayout()
    {
        MinColumnSpacing = 6;
        MinRowSpacing = 6;
        Update();
        Theme.SpriteScale.Changed += OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        MinItemWidth = Theme.SpriteScale.DexCardSize;
        MinItemHeight = Theme.SpriteScale.DexCardSize;
    }
}
