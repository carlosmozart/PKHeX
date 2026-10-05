using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PKHeX.Core;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed partial class MainViewModel
{
    public Func<byte[], string, Task<bool>>? SaveBoxReport { get; set; }
    private bool _reportBusy;
    public bool IsReportBusy { get => _reportBusy; private set => Set(ref _reportBusy, value); }

    public async Task<bool> ExportBoxReportAsync(bool bank)
    {
        if (IsReportBusy || SaveBoxReport is null || !bank && _sav is null || bank && Bank.SelectedBank is null) return false;
        IsReportBusy = true;
        var active = _sav;
        var snapshot = bank ? null : active!.Clone();
        var selectedBank = Bank.SelectedBank;
        try
        {
            Status = "Lendo Pokémon para o relatório...";
            var entries = await Task.Run(() => bank ? BoxReport.ReadBank(selectedBank!) : BoxReport.ReadSave(snapshot!));
            var valid = new List<bool>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                // Keep the global Core parsing context on the UI thread, restored before each yield.
                if (!ReferenceEquals(_sav, active)) { Status = "O save ativo mudou. Exporte o relatório novamente."; return false; }
                var pk = entries[i].Pokemon;
                valid.Add(bank ? BankInspection.Analyze(pk, active).Valid : new LegalityAnalysis(pk).Valid);
                if ((i + 1) % 8 == 0)
                {
                    Status = string.Format(Loc.T("Relatório: {0}/{1} Pokémon"), i + 1, entries.Count);
                    await Task.Delay(1);
                }
            }
            var bytes = await Task.Run(() => BoxReport.Encode(entries, valid));
            if (!await SaveBoxReport(bytes, bank ? "bank-report.csv" : "save-report.csv")) return false;
            Status = string.Format(Loc.T("Relatório exportado: {0} Pokémon."), entries.Count);
            return true;
        }
        catch (Exception ex) { Status = Loc.T("Não foi possível exportar o relatório: ") + ex.Message; return false; }
        finally { IsReportBusy = false; }
    }
}
