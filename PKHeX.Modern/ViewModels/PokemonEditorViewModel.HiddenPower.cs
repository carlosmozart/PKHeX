using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class PokemonEditorViewModel
{
    private bool _hiddenPowerCorrelated;
    public bool HasHiddenPower => _pk.Format is >= 2 and <= 7;
    public bool CanChooseHiddenPower => HasHiddenPower && (!LegalMode || !_hiddenPowerCorrelated);
    public IReadOnlyList<string> HiddenPowerTypes { get; } = [.. GameInfo.Strings.types.Skip(1).Take(16)];
    public TypeChip HiddenPowerChip => CoreAdapter.GetHiddenPowerType(_pk);
    public string HiddenPowerPower => $"Poder: {_pk.HPPower}";
    public string HiddenPowerNote => LegalMode && _hiddenPowerCorrelated
        ? "Modo legal: PID e IVs estão ligados neste encontro. O tipo não pode ser alterado."
        : "Escolher o tipo ajusta os IVs. No modo legal, o resultado precisa continuar legal.";
    public int HiddenPowerType
    {
        get => _pk.HPType;
        set
        {
            if (!HasHiddenPower || HiddenPower.IsInvalidType(value) || value == HiddenPowerType) return;
            if (!CanChooseHiddenPower) { _status(HiddenPowerNote); return; }
            var candidate = _pk.Clone();
            Span<int> ivs = stackalloc int[6]; candidate.GetIVs(ivs);
            if (!HiddenPower.SetIVsForType(value, ivs, candidate.Context))
            {
                _status("Não foi possível obter esse tipo preservando os IVs pelo método do PKHeX.");
                RaiseHiddenPower();
                Avalonia.Threading.Dispatcher.UIThread.Post(RaiseHiddenPower);
                return;
            }
            candidate.SetIVs(ivs);
            var analysis = new LegalityAnalysis(candidate);
            if (LegalMode && !analysis.Valid)
            {
                _status("Modo legal: o tipo escolhido deixaria o Pokémon ilegal. Os IVs foram preservados.");
                RaiseHiddenPower();
                Avalonia.Threading.Dispatcher.UIThread.Post(RaiseHiddenPower);
                return;
            }
            _pk = candidate;
            RaiseAll();
        }
    }

    private void RaiseHiddenPower()
    {
        _hiddenPowerCorrelated = HasHiddenPower && _pk.Format is 3 or 4 && new LegalityAnalysis(_pk).Info.PIDIV.Type != PIDType.None;
        foreach (var name in (string[])[nameof(HasHiddenPower), nameof(CanChooseHiddenPower), nameof(HiddenPowerType),
            nameof(HiddenPowerChip), nameof(HiddenPowerPower), nameof(HiddenPowerNote)]) Raise(name);
        foreach (var move in Moves) move.RaiseAll();
    }
}
