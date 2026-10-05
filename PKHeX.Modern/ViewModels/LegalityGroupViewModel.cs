using System.Collections.Generic;
using Avalonia.Media;
using PKHeX.Modern.Services;

namespace PKHeX.Modern.ViewModels;

public sealed class LegalityGroupViewModel(LegalityTopic topic, bool expanded, RelayCommand? action, string actionText) : ViewModelBase
{
    public string Topic => topic.Name;
    public string Header => $"{(topic.Invalid ? "✕" : "⚠")} {Loc.T(Topic)} · {topic.Issues.Count}";
    public bool Invalid => topic.Invalid;
    public IBrush SeverityBrush => new SolidColorBrush(topic.Invalid ? Color.Parse("#E86666") : Color.Parse("#E3B84F"));
    public IReadOnlyList<string> Issues => topic.Issues;
    private bool _expanded = expanded;
    public bool Expanded { get => _expanded; set => Set(ref _expanded, value); }
    public RelayCommand? Action => action;
    public string ActionText => actionText;
    public bool HasAction => action is not null;
}
