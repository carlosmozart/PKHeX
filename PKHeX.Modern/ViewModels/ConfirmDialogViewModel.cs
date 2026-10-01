using System.Threading.Tasks;

namespace PKHeX.Modern.ViewModels;

/// <summary>
/// Pergunta exibida dentro da janela (sobreposicao), no lugar de caixas de dialogo do Windows.
/// Use <see cref="MainViewModel.ConfirmAsync"/>; Enter confirma e Esc cancela.
/// </summary>
public sealed class ConfirmDialogViewModel : ViewModelBase
{
    private readonly TaskCompletionSource<bool> _result = new();

    public ConfirmDialogViewModel(string title, string message, string confirmText, string cancelText, bool isDanger)
    {
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

    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    public Task<bool> Result => _result.Task;

    public void Complete(bool confirmed) => _result.TrySetResult(confirmed);
}
