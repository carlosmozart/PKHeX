using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class MainViewModel
{
    public bool ShowBoxFolderActions => HasSave && !IsHelpOpen && CurrentPage == Boxes;
    private bool _exportAllBoxes = true, _boxSubfolders = true, _importFirstBox, _clearImportBoxes, _overwriteImportSlots;
    public bool ExportAllBoxes { get => _exportAllBoxes; set => Set(ref _exportAllBoxes, value); }
    public bool BoxSubfolders { get => _boxSubfolders; set => Set(ref _boxSubfolders, value); }
    public bool ImportFirstBox { get => _importFirstBox; set => Set(ref _importFirstBox, value); }
    public bool ClearImportBoxes { get => _clearImportBoxes; set => Set(ref _clearImportBoxes, value); }
    public bool OverwriteImportSlots { get => _overwriteImportSlots; set => Set(ref _overwriteImportSlots, value); }

    public int ExportBoxesToFolder(string path)
    {
        if (_sav is null || !_sav.HasBox) return 0;
        Directory.CreateDirectory(path);
        if (ExportAllBoxes) return _sav.DumpBoxes(path, BoxSubfolders);
        if (BoxSubfolders)
        {
            path = Path.Combine(path, PathUtil.CleanFileName(CoreAdapter.GetBoxName(_sav, Boxes.CurrentBox)));
            Directory.CreateDirectory(path);
        }
        return _sav.DumpBox(path, Boxes.CurrentBox);
    }

    public async Task ImportBoxFolderAsync(IEnumerable<string> files)
    {
        var sav = _sav;
        if (sav is null || !sav.HasBox || !await ConfirmDiscardEditAsync()) return;
        int start = ImportFirstBox ? 0 : Boxes.CurrentBox;
        bool clear = ClearImportBoxes, overwrite = OverwriteImportSlots;
        var imported = new List<PKM>(); var rejected = new List<string>();
        foreach (var path in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var obj = FileUtil.GetSupportedFile(path, sav);
                var raw = obj switch { PKM pk => pk, MysteryGift { IsEntity: true } gift => gift.ConvertToPKM(sav), _ => null };
                PKM? pkConverted = null;
                string? reason = raw is null ? "Arquivo sem Pokémon reconhecido." : null;
                if (raw is not null) pkConverted = CoreAdapter.ConvertForSave(sav, raw, out reason);
                if (pkConverted is not null)
                {
                    var compatibility = sav.EvaluateCompatibility(pkConverted);
                    if (compatibility.Count > 0) reason = string.Join(" ", compatibility);
                    else if (sav is ILangDeviantSave language && !EntityConverter.IsCompatibleGB(raw!, language.Japanese, pkConverted.Japanese))
                        reason = "Idioma incompatível com este save.";
                    else { imported.Add(pkConverted); continue; }
                }
                rejected.Add(Path.GetFileName(path) + ": " + (reason ?? "Formato incompatível com este jogo."));
            }
            catch (Exception ex) { rejected.Add(Path.GetFileName(path) + ": " + ex.Message); }
        }
        if (imported.Count == 0) { Status = $"Importados: 0; recusados: {rejected.Count}."; await ShowBoxImportRefusalsAsync(rejected); return; }
        if ((clear || overwrite) && !await ConfirmAsync("Importar caixas da pasta?",
                "As opções escolhidas podem limpar caixas ou substituir Pokémon a partir da caixa inicial. A importação inteira pode ser desfeita.", "Importar", isDanger: true)) return;
        var illegal = imported.Select((pk, i) => (pk, i)).Where(x => !new LegalityAnalysis(x.pk).Valid).ToList();
        if (LegalMode && illegal.Count > 0 && await ConfirmAsync($"{illegal.Count} Pokémon ilegais",
                "O lote contém Pokémon ilegais neste jogo. Legalizar tenta gerar cada um a partir de um encontro real; se falhar, mantém o original.",
                "✨ Legalizar", "Trazer como estão", details: [.. illegal.Select(x => CoreAdapter.SpeciesNames[x.pk.Species])], icon: "🛡"))
            foreach (var (pk, i) in illegal) imported[i] = (await LegalizeOutsideAsync(pk)).Pk;
        if (!ReferenceEquals(_sav, sav)) { Status = "O save ativo mudou. Repita a importação."; return; }

        // Prepara os arquivos convertidos e simula no clone antes de alterar o save aberto.
        var temp = Path.Combine(Path.GetTempPath(), "PKHeX.Modern", "box-import", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var staged = sav.Clone();
            int capacity = Enumerable.Range(start * sav.BoxSlotCount, sav.SlotCount - start * sav.BoxSlotCount)
                .Count(i => !sav.IsBoxSlotOverwriteProtected(i) && (clear || overwrite || CoreAdapter.IsEmpty(sav.GetBoxSlotAtIndex(i))));
            int count = Math.Min(imported.Count, capacity);
            if (count == 0) { Status = "Não há slots disponíveis para importar."; return; }
            var prepared = new List<string>();
            for (int i = 0; i < count; i++)
            {
                var path = Path.Combine(temp, i.ToString("D6") + "." + imported[i].Extension);
                CoreAdapter.ExportEntity(imported[i], path); prepared.Add(path);
            }
            if (staged.LoadBoxes(prepared, out _, start, clear, overwrite) < 0)
            {
                Status = "Nenhum Pokémon compatível foi importado.";
                return;
            }
            var keys = Enumerable.Range(start, sav.BoxCount - start).SelectMany(b => Enumerable.Range(0, sav.BoxSlotCount).Select(s => new SlotHistory.Key(b, s))).ToArray();
            _history!.Record("importar caixas da pasta", keys);
            var before = keys.Select(k => sav.GetBoxSlotAtIndex(k.Box, k.Slot)).ToArray();
            try { sav.LoadBoxes(prepared, out _, start, clear, overwrite); }
            catch
            {
                for (int i = 0; i < keys.Length; i++) sav.SetBoxSlotAtIndex(before[i], keys[i].Box, keys[i].Slot, EntityImportSettings.None);
                _history.Discard(); throw;
            }
            OnHistoryChanged(); IsDirty = true; RefreshSlots();
            int refusedCount = rejected.Count + imported.Count - count;
            if (count < imported.Count) rejected.Add($"Sem espaço nas caixas: {imported.Count - count} Pokémon.");
            Status = $"Importados: {count}; recusados: {refusedCount}.";
            await ShowBoxImportRefusalsAsync(rejected);
        }
        finally { Directory.Delete(temp, true); }
    }

    private Task ShowBoxImportRefusalsAsync(List<string> reasons) => reasons.Count == 0 ? Task.CompletedTask
        : ConfirmAsync("Arquivos recusados", "Estes arquivos não foram importados:", "OK", cancelText: "", details: reasons, icon: "⚠");
}
