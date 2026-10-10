using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LogiSteam.Common.Config;
using LogiSteam.Common.Dashboard;
using LogiSteam.Common.Tray;
using LogiSteam.Roles;

namespace LogiSteam.UI;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void Raise(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class StepViewModel : ObservableObject
{
    private string _title = string.Empty;
    private string _detail = string.Empty;
    private StepState _state;
    private bool _isLast;

    public string Title { get => _title; set => Set(ref _title, value); }
    public string Detail { get => _detail; set => Set(ref _detail, value); }
    public StepState State { get => _state; set => Set(ref _state, value); }
    public bool IsLast { get => _isLast; set => Set(ref _isLast, value); }
}

/// <summary>
/// Everything the main window binds to. The app pushes a fresh <see cref="DashboardState"/> in
/// about once a second; <see cref="Apply"/> updates in place rather than rebuilding the lists,
/// so nothing flickers and running animations (the "searching" spinner) aren't restarted.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private bool _isChoosingRole;
    private bool _isSwitching;
    private AppRole? _role;
    private string _headline = "Starting...";
    private string _detail = string.Empty;
    private TrayStatusLevel _level = TrayStatusLevel.Idle;
    private string? _detectedWheelHint;

    /// <summary>The user picked a role (the switch, or a first-run card).</summary>
    public event Action<AppRole>? RoleRequested;

    /// <summary>The user clicked a warning's button; the argument is the warning's id.</summary>
    public event Action<string>? WarningActionRequested;

    public ObservableCollection<StepViewModel> Steps { get; } = [];
    public ObservableCollection<DashboardWarning> Warnings { get; } = [];
    public ObservableCollection<RoleAction> AdvancedActions { get; } = [];

    public string VersionText { get; } =
        $"LogiSteam {typeof(MainViewModel).Assembly.GetName().Version?.ToString(2) ?? string.Empty}".TrimEnd();

    /// <summary>First run: no role chosen yet, so the window asks which PC this is.</summary>
    public bool IsChoosingRole { get => _isChoosingRole; set => Set(ref _isChoosingRole, value); }

    /// <summary>A role switch is in flight; the switch is disabled until it settles.</summary>
    public bool IsSwitching
    {
        get => _isSwitching;
        set
        {
            if (Set(ref _isSwitching, value))
            {
                Raise(nameof(CanSwitch));
            }
        }
    }

    public bool CanSwitch => !_isSwitching;

    public AppRole? Role
    {
        get => _role;
        set
        {
            if (Set(ref _role, value))
            {
                Raise(nameof(IsWheelRole));
                Raise(nameof(IsGameRole));
                Raise(nameof(RoleCaption));
            }
        }
    }

    // Two-way for the switch's radio buttons: checking one asks for that role; the switch then
    // shows whatever Role actually becomes.
    public bool IsWheelRole { get => _role == AppRole.Wheel; set => RequestIfChecked(value, AppRole.Wheel); }
    public bool IsGameRole { get => _role == AppRole.Game; set => RequestIfChecked(value, AppRole.Game); }

    private void RequestIfChecked(bool isChecked, AppRole role)
    {
        if (isChecked && _role != role)
        {
            RequestRole(role);
        }
    }

    public string RoleCaption => _role switch
    {
        AppRole.Wheel => "Your racing wheel is plugged into this PC",
        AppRole.Game => "Your games run on this PC",
        _ => string.Empty,
    };

    public string Headline { get => _headline; set => Set(ref _headline, value); }
    public string Detail { get => _detail; set => Set(ref _detail, value); }
    public TrayStatusLevel Level { get => _level; set => Set(ref _level, value); }

    /// <summary>First run only: the name of a racing wheel found on this PC, to nudge the right choice.</summary>
    public string? DetectedWheelHint { get => _detectedWheelHint; set => Set(ref _detectedWheelHint, value); }

    public void RequestRole(AppRole role) => RoleRequested?.Invoke(role);

    public void RequestWarningAction(string warningId) => WarningActionRequested?.Invoke(warningId);

    public void Apply(DashboardState state, IReadOnlyList<RoleAction> advancedActions)
    {
        Headline = state.Headline;
        Detail = state.Detail;
        Level = state.Level;

        while (Steps.Count > state.Steps.Count)
        {
            Steps.RemoveAt(Steps.Count - 1);
        }

        for (var i = 0; i < state.Steps.Count; i++)
        {
            if (i == Steps.Count)
            {
                Steps.Add(new StepViewModel());
            }

            Steps[i].Title = state.Steps[i].Title;
            Steps[i].Detail = state.Steps[i].Detail;
            Steps[i].State = state.Steps[i].State;
            Steps[i].IsLast = i == state.Steps.Count - 1;
        }

        if (!Warnings.SequenceEqual(state.Warnings))
        {
            Warnings.Clear();
            foreach (var warning in state.Warnings)
            {
                Warnings.Add(warning);
            }
        }

        // RoleAction carries a delegate, so compare by what's shown rather than by value.
        if (!AdvancedActions.Select(a => (a.Title, a.Description)).SequenceEqual(advancedActions.Select(a => (a.Title, a.Description))))
        {
            AdvancedActions.Clear();
            foreach (var action in advancedActions)
            {
                AdvancedActions.Add(action);
            }
        }
    }
}
