using System.Collections.Generic;
using System.Threading.Tasks;

namespace PKHeX.Modern.ViewModels;

/// <summary>
/// Pergunta exibida dentro da janela (sobreposicao), no lugar de caixas de dialogo do Windows.
/// Use <see cref="MainViewModel.ConfirmAsync"/>; Enter confirma e Esc cancela.
/// </summary>
public sealed class ConfirmDialogViewModel : ViewModelBase
{
    private readonly TaskCompletionSource<bool> _result = new();

    public ConfirmDialogViewModel(string title, string message, string confirmText, string cancelText, bool isDanger,
        IReadOnlyList<string>? details = null, string icon = "")
    {
        Details = details ?? [];
        Icon = icon;
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
        IsDanger = isDanger;
        ConfirmCommand = new RelayCommand(() => Complete(true));
        CancelCommand = new RelayCommand(() => Complete(false));
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmText { get; }
    public string CancelText { get; }
    /// <summary>Acao que descarta dados: botao de confirmar em vermelho.</summary>
    public bool IsDanger { get; }
    /// <summary>Linhas extras (ex.: lista de Pokemon com problema), exibidas numa lista rolavel.</summary>
    public IReadOnlyList<string> Details { get; }
    public bool HasDetails => Details.Count > 0;
    public bool DetailsExpanded { get; init; } = true;
    /// <summary>Simbolo grande ao lado do titulo (✓, ⚠...).</summary>
    public string Icon { get; }
    public bool HasIcon => Icon.Length > 0;
    private string? _input;
    /// <summary>Campo de texto (null = sem campo). Usado para dar nome a bancos e caixas.</summary>
    public string? Input { get => _input; set => Set(ref _input, value); }
    public bool HasInput { get; init; }

    /// <summary>Sem texto de cancelar = aviso com um botao so.</summary>
    public bool HasCancel => CancelText.Length > 0;

    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    public Task<bool> Result => _result.Task;

    public void Complete(bool confirmed) => _result.TrySetResult(confirmed);
}
