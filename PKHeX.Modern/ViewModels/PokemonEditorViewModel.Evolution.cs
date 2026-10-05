using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class PokemonEditorViewModel
{
    public IReadOnlyList<EvolutionRowViewModel> Evolutions { get; private set; } = [];
    public bool HasEvolutions => Evolutions.Count > 0;
    private void RefreshEvolutions()
    {
        Evolutions = _sav is null ? [] : AssistedEvolution.List(_pk, _sav)
            .Select(e => new EvolutionRowViewModel(e, _pk, new RelayCommand(() => _ = PreviewEvolutionAsync(e), () => !IsLegalizing && e.Blocked is null))).ToArray();
        Raise(nameof(Evolutions)); Raise(nameof(HasEvolutions));
    }

    public async Task<bool> PreviewEvolutionAsync(AssistedEvolutionOption choice)
    {
        if (_sav is null || IsLegalizing || ConfirmPreview is null) return false;
        var before = _pk.Clone();
        IsLegalizing = true;
        try
        {
            var candidate = AssistedEvolution.Build(before, _sav, choice);
            var analysis = new LegalityAnalysis(candidate);
            if (LegalMode && !analysis.Valid)
            {
                _status(Loc.T("Não foi possível evoluir mantendo a legalidade: ") + analysis.Report());
                return false;
            }
            _previewCandidate = candidate;
            IReadOnlyList<string> details = [choice.Requirement,
                Loc.T("Local, horário, clima e ações na equipe não alteram o mundo do save."),
                .. PokemonDiff.Details(before, candidate),
                .. analysis.Valid ? Array.Empty<string>() : new[] { Loc.T("⚠ O resultado é ilegal; modo legal desligado."), analysis.Report() }];
            if (!await ConfirmPreview("Prévia de evolução", "Confira os requisitos e as mudanças. A evolução fica pendente até Aplicar.", "Evoluir", "Cancelar", details)
                || IsCurrentEditor?.Invoke() == false || !_pk.Data.SequenceEqual(before.Data)) return false;
            _pk = candidate;
            _isNew = false;
            RaiseAll();
            _status("Evolução pendente. Clique em Aplicar para gravar no slot.");
            return true;
        }
        catch (Exception ex) { _status(Loc.T("Não evolui: ") + ex.Message); return false; }
        finally { _previewCandidate = null; IsLegalizing = false; }
    }
}

public sealed class EvolutionRowViewModel(AssistedEvolutionOption option, PKM source, RelayCommand command)
{
    public AssistedEvolutionOption Option => option;
    public string Name => option.Name;
    public string Requirement => option.Requirement;
    public string Status => option.Blocked is not null ? "✗ " + option.Blocked : Loc.T(option.Ready ? "✓ Pode evoluir agora" : "⚙ O app cumpre o requisito");
    public bool IsBlocked => option.Blocked is not null;
    public RelayCommand Command => command;
    public Bitmap? Sprite { get; } = SpriteService.GetSpeciesSprite(option.Method.Species, source.IsShiny, option.Form, source.Gender, source.Context);
}
