using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class LivingDexViewModel
{
    public Func<bool>? LegalMode { get; set; }
    public Func<LivingDexApplicationPlan, Task<bool>>? ApplyPlan { get; set; }
    public LivingDexApplicationPlan? ApplicationPreview { get; private set; }
    public bool CanApplyPlan => ApplicationPreview is { Blocked: null, Changes.Count: > 0 } && !IsBusy;
    public RelayCommand PreviewApplicationCommand => new(() => _ = PreviewApplicationAsync());
    public RelayCommand ApplyPlanCommand => new(() => _ = ApplyPreviewAsync());
    private decimal? _firstBox = 1;
    public decimal? FirstBox { get => _firstBox; set { if (Set(ref _firstBox, value)) Invalidate(); } }
    private bool _reserve = true;
    public bool ReserveMissing { get => _reserve; set { if (Set(ref _reserve, value)) Invalidate(); } }
    public IReadOnlyList<string> ApplicationDetails { get; private set; } = [];

    public async Task PreviewApplicationAsync()
    {
        if (IsBusy || _activeSave() is not { } target) return;
        IsBusy = true; _reading = new(); var revision = _revision;
        ApplicationPreview = null; Raise(nameof(CanApplyPlan));
        try
        {
            var allOpen = _openSaves();
            var active = allOpen.FirstOrDefault(e => ReferenceEquals(e.Sav, target));
            if (active.Sav is null) throw new InvalidOperationException(Loc.T("Abra um save para aplicar a Living Dex."));
            var open = IncludeOpen ? allOpen : new[] { active };
            var entries = await _reader.ReadAsync(open, IncludeFolder ? _settings.SavesFolder ?? SaveLibrary.DefaultFolder : null, IncludeBank, _reading.Token,
                n => Dispatcher.UIThread.Post(() => { if (IsBusy) Progress = $"Lendo fontes: {n}"; }));
            var audit = await Task.Run(() => DuplicateAudit.Build(entries, _reading.Token), _reading.Token);
            var converted = new Dictionary<DbEntry, (PKM? Pokemon, bool Legal, string? Error)>();
            int i = 0;
            foreach (var entry in entries.Where(e => LivingDexPlanner.Eligible(e, target, ShinyOnly)))
            {
                _reading.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_activeSave(), target) || revision != _revision) return;
                if (!entry.Source.IsBank && StoredPokemon.SameSource(entry.Source.Id, active.Path))
                {
                    var pk = entry.Pkm.Clone(); var la = new LegalityAnalysis(pk, target.Personal, entry.Box < 0 ? StorageSlotType.Party : StorageSlotType.Box);
                    converted[entry] = (pk, la.Valid, la.Valid ? null : la.Report());
                }
                else converted[entry] = LivingDexApplication.Convert(entry, target);
                if (++i % 8 == 0) { Progress = string.Format(Loc.T("Preparando a prévia: {0} candidatos"), i); await Task.Delay(1); }
            }
            var preview = LivingDexApplication.Prepare(target, active.Path, entries, IncludeForms, ShinyOnly, LegalMode?.Invoke() == true,
                (int)(_firstBox ?? 1) - 1, ReserveMissing, converted);
            if (revision != _revision || !ReferenceEquals(_activeSave(), target)) return;
            ApplicationPreview = preview;
            Summary = preview.Summary;
            _rows = preview.Placements.Select(p => new LivingDexRowViewModel(new LivingDexRow(p.Species, p.Form, CoreAdapter.SpeciesNames[p.Species], p.Source,
                p.Source is not null && converted.GetValueOrDefault(p.Source).Legal, [], p.Box + 1, p.Slot + 1),
                new RelayCommand(() => _find(p.Species, target.Version)), p.Source is null ? null : new RelayCommand(() => _ = _open(p.Source)),
                p.Source is not null ? audit.Labels.GetValueOrDefault(p.Source.LocationId, "") : p.Note)).ToArray();
            Filter();
            ApplicationDetails = [preview.Summary, .. preview.Skipped, .. preview.Placements.Select(p =>
                string.Format(Loc.T("Caixa {0} · slot {1}"), p.Box + 1, p.Slot + 1) + " — " +
                (p.Source is null ? p.Note : p.Source.Species + " · " + (p.Copy
                    ? string.Format(Loc.T("Cria uma cópia; o original continua em {0}"), p.Source.Source.Name + " · " + p.Source.Where)
                    : string.Format(Loc.T("Mover de {0}"), p.Source.Where)))),
                .. preview.Relocations.Select(r => string.Format(Loc.T("Realocar: caixa {0}, slot {1} → caixa {2}, slot {3}"), r.FromBox + 1, r.FromSlot + 1, r.ToBox + 1, r.ToSlot + 1))];
            var details = ApplicationDetails.ToList();
            foreach (var placement in preview.Placements.Where(p => p.Copy && p.Pokemon is not null && p.Source is not null && !StoredPokemon.Equal(p.Source.Pkm, p.Pokemon)))
            {
                details.Add(string.Format(Loc.T("Conversão para caixa {0}, slot {1}"), placement.Box + 1, placement.Slot + 1));
                details.AddRange(PokemonDiff.Details(placement.Source!.Pkm, placement.Pokemon!));
            }
            ApplicationDetails = details;
            Progress = preview.Blocked ?? Loc.T("Prévia pronta. Confira as posições antes de aplicar.");
            Raise(nameof(ApplicationPreview)); Raise(nameof(ApplicationDetails));
        }
        catch (OperationCanceledException) { Progress = "Leitura cancelada. Atualize para obter um resultado completo."; }
        catch (Exception ex) { Progress = Loc.T("Não foi possível preparar a prévia: ") + ex.Message; }
        finally { IsBusy = false; Raise(nameof(CanApplyPlan)); }
    }
    public async Task<bool> ApplyPreviewAsync()
    {
        if (!CanApplyPlan || ApplicationPreview is not { } preview || ApplyPlan is null) return false;
        return await ApplyPlan(preview);
    }
}
