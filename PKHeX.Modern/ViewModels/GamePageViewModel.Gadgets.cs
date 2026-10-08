using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Modern.ViewModels;

public sealed partial class GamePageViewModel
{
    /// <summary>Pokétch (Diamond/Pearl/Platinum) ou Pokéwalker (HeartGold/SoulSilver).</summary>
    public bool HasGadget => _sav is SAV4Sinnoh or SAV4HGSS;
    public bool IsGadgetTab => Tab == 12;
    public string GadgetTabText => _sav is SAV4HGSS ? "🚶 Pokéwalker" : "⌚ Pokétch";
    public PoketchEditorViewModel? Poketch { get; private set; }
    public PokewalkerEditorViewModel? Pokewalker { get; private set; }
    private void RefreshGadgets()
    {
        Poketch = _sav is SAV4Sinnoh s ? new(s, Edit) : null;
        Pokewalker = _sav is SAV4HGSS h ? new(h, Edit) : null;
        foreach (var p in (string[])[nameof(Poketch), nameof(Pokewalker), nameof(HasGadget), nameof(GadgetTabText)]) Raise(p);
    }
}

/// <summary>Aplicativos do Pokétch: quais estão liberados e qual aparece na tela.</summary>
public sealed class PoketchEditorViewModel : ViewModelBase
{
    private readonly SAV4Sinnoh _sav;
    private readonly Action<string, Action> _edit;

    public PoketchEditorViewModel(SAV4Sinnoh sav, Action<string, Action> edit)
    {
        _sav = sav; _edit = edit;
        var names = GameInfo.Strings.poketchapps;
        Apps = [.. Enumerable.Range(0, (int)PoketchApp.Alarm_Clock + 1)
            .Select(i => new PoketchAppViewModel($"{i + 1:00} · {(i < names.Length ? names[i] : $"App {i + 1}")}", (PoketchApp)i, this))];
        AppNames = [.. Apps.Select(a => a.Name)];
    }

    public IReadOnlyList<PoketchAppViewModel> Apps { get; }
    public IReadOnlyList<string> AppNames { get; }
    public string Summary => $"{Apps.Count(a => a.Unlocked)} de {Apps.Count} aplicativos liberados";

    public int CurrentApp
    {
        get => Math.Clamp((int)_sav.CurrentPoketchApp, 0, Apps.Count - 1);
        set { if (value < 0 || value >= Apps.Count || value == CurrentApp) return; _edit("Aplicativo atual do Pokétch", () => _sav.CurrentPoketchApp = (sbyte)value); Raise(); }
    }

    public RelayCommand UnlockAllCommand => new(() =>
    {
        _edit("Liberar todos os aplicativos do Pokétch", () => { foreach (var a in Apps) _sav.SetPoketchAppUnlocked(a.App, true); UpdateCount(); });
        foreach (var a in Apps) a.Refresh();
        Raise(nameof(Summary));
    });

    internal bool Get(PoketchApp app) => _sav.GetPoketchAppUnlocked(app);
    internal void Set(PoketchApp app, bool value)
    {
        _edit(value ? "Liberar aplicativo do Pokétch" : "Bloquear aplicativo do Pokétch", () => { _sav.SetPoketchAppUnlocked(app, value); UpdateCount(); });
        Raise(nameof(Summary));
    }
    // O jogo guarda tambem quantos estao liberados (como o PKHeX faz ao gravar).
    private void UpdateCount() => _sav.PoketchUnlockedCount = (byte)Apps.Count(a => _sav.GetPoketchAppUnlocked(a.App));
}

public sealed class PoketchAppViewModel(string name, PoketchApp app, PoketchEditorViewModel owner) : ViewModelBase
{
    public string Name => name;
    public PoketchApp App => app;
    public bool Unlocked { get => owner.Get(app); set { if (value == Unlocked) return; owner.Set(app, value); Raise(); } }
    internal void Refresh() => Raise(nameof(Unlocked));
}

/// <summary>Pokéwalker: watts, passos e rotas liberadas.</summary>
public sealed class PokewalkerEditorViewModel : ViewModelBase
{
    private readonly SAV4HGSS _sav;
    private readonly Action<string, Action> _edit;

    public PokewalkerEditorViewModel(SAV4HGSS sav, Action<string, Action> edit)
    {
        _sav = sav; _edit = edit;
        var names = GameInfo.Strings.walkercourses;
        Courses = [.. Enumerable.Range(0, Math.Min(names.Length, SAV4HGSS.PokewalkerCourseFlagCount))
            .Select(i => new PokewalkerCourseViewModel($"{i + 1:00} · {names[i]}", i, this))];
    }

    public IReadOnlyList<PokewalkerCourseViewModel> Courses { get; }
    public string Summary => $"{Courses.Count(c => c.Unlocked)} de {Courses.Count} rotas liberadas";

    public long Watts
    {
        get => _sav.PokewalkerWatts;
        set { value = Math.Clamp(value, 0, 9999); if (value == Watts) return; _edit("Watts do Pokéwalker", () => _sav.PokewalkerWatts = (uint)value); Raise(); }
    }
    public long Steps
    {
        get => _sav.PokewalkerSteps;
        set { value = Math.Clamp(value, 0, uint.MaxValue); if (value == Steps) return; _edit("Passos do Pokéwalker", () => _sav.PokewalkerSteps = (uint)value); Raise(); }
    }

    /// <summary>Libera as rotas que existem no idioma do save (a japonesa tem mais; "todas" do PKHeX inclui as dela).</summary>
    public RelayCommand UnlockAllCommand => new(() =>
    {
        var mask = SAV4HGSS.GetPossiblePokewalkerCourseUnlock(_sav.Language);
        _edit("Liberar todas as rotas do Pokéwalker", () => { Span<bool> flags = stackalloc bool[SAV4HGSS.PokewalkerCourseFlagCount]; _sav.GetPokewalkerCoursesUnlocked(flags); for (int i = 0; i < flags.Length; i++) flags[i] |= (mask >> i & 1) != 0; _sav.SetPokewalkerCoursesUnlocked(flags); });
        foreach (var c in Courses) c.Refresh();
        Raise(nameof(Summary));
    });

    internal bool Get(int index)
    {
        Span<bool> flags = stackalloc bool[SAV4HGSS.PokewalkerCourseFlagCount];
        _sav.GetPokewalkerCoursesUnlocked(flags);
        return flags[index];
    }
    internal void Set(int index, bool value)
    {
        _edit(value ? "Liberar rota do Pokéwalker" : "Bloquear rota do Pokéwalker", () =>
        {
            Span<bool> flags = stackalloc bool[SAV4HGSS.PokewalkerCourseFlagCount];
            _sav.GetPokewalkerCoursesUnlocked(flags);
            flags[index] = value;
            _sav.SetPokewalkerCoursesUnlocked(flags);
        });
        Raise(nameof(Summary));
    }
}

public sealed class PokewalkerCourseViewModel(string name, int index, PokewalkerEditorViewModel owner) : ViewModelBase
{
    public string Name => name;
    public bool Unlocked { get => owner.Get(index); set { if (value == Unlocked) return; owner.Set(index, value); Raise(); } }
    internal void Refresh() => Raise(nameof(Unlocked));
}
