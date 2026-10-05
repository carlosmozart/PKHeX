using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

/// <summary>Read-only snapshot; no editable PKM fields or write operations.</summary>
public sealed class BankDetailsViewModel
{
    public BankDetailsViewModel(SlotViewModel slot, SaveFile? active, RelayCommand open, RelayCommand export,
        RelayCommand variants, RelayCommand copy, RelayCommand close)
    {
        var pk = slot.Pkm!.Clone();
        var source = new DbSource(slot.BankBox!.Folder, slot.BankName, pk.Version, false, slot.BankName);
        var entry = new DbEntry(source, pk, slot.Location, slot.Box, slot.Slot);
        var form = FormConverter.GetStringFromForm(pk.Species, pk.Form, GameInfo.Strings, pk.Context);
        Species = pk.Species; Level = pk.CurrentLevel;
        Title = entry.Species + (string.IsNullOrWhiteSpace(form) ? "" : " · " + form);
        Nickname = pk.Nickname; Sprite = SpriteService.GetSprite(pk);
        Location = slot.Location;
        Folder = App.ShowShortcuts ? slot.BankBox.Folder : slot.BankName + " › " + slot.BankBox.Name;
        var analysis = BankInspection.Analyze(pk, active);
        Legal = analysis.Valid; LegalityReport = analysis.Report;
        Types = string.Join(" / ", CoreAdapter.GetTypes(pk).Select(t => t.Name));
        Fields = [new("Nível", Level.ToString()), new("Gênero", CoreAdapter.GetGenderSymbol(pk)), new("Tipos", Types),
            new("Natureza", entry.Nature), new("Habilidade", entry.Ability), new("Item", entry.Item), new("Pokébola", entry.Ball),
            new("Treinador de origem", pk.OriginalTrainerName), new("Jogo de origem", CoreAdapter.GetVersionName(pk.Version))];
        Moves = entry.Moves;
        int[] ivs = [pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE];
        int[] evs = [pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPA, pk.EV_SPD, pk.EV_SPE];
        IVs = string.Join(" / ", ivs); EVs = string.Join(" / ", evs);
        RadarValues = ivs.Select(v => (double)v / Math.Max(1, pk.MaxIV)).ToArray();
        CanOpen = active is not null && CoreAdapter.ConvertForSave(active, pk.Clone(), out _) is not null;
        HasVariants = BankLinks.GetVariants(pk).Count > 0;
        Showdown = CoreAdapter.ToShowdown(pk);
        OpenCommand = open; ExportCommand = export; VariantsCommand = variants; CopyCommand = copy; CloseCommand = close;
    }
    public ushort Species { get; }
    public int Level { get; }
    public string Title { get; }
    public string Nickname { get; }
    public Bitmap? Sprite { get; }
    public string Types { get; }
    public string Location { get; }
    public string Folder { get; }
    public bool Legal { get; }
    public string LegalityReport { get; }
    public string LegalityText => Loc.T(Legal ? "✓ Legal" : "⚠ Ilegal");
    public IReadOnlyList<BankDetailField> Fields { get; }
    public IReadOnlyList<string> Moves { get; }
    public string IVs { get; }
    public string EVs { get; }
    public IReadOnlyList<double> RadarValues { get; }
    public IReadOnlyList<string> StatLabels { get; } = ["HP", "Atk", "Def", "SpA", "SpD", "Spe"];
    public bool CanOpen { get; }
    public bool HasVariants { get; }
    public string Showdown { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand VariantsCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand CloseCommand { get; }
}

public sealed record BankDetailField(string Label, string Value);
