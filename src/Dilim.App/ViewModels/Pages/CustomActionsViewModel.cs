using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dilim.Core.Localization;
using Dilim.Core.Presets;

namespace Dilim.App.ViewModels.Pages;

/// <summary>One of the user's own slices in the list: its name, what it does, and a remove button.</summary>
public sealed record CustomActionRow(CustomAction Action, string Kind, string Detail);

/// <summary>
/// The user's own slices on the file actions pie (Settings → Shortcuts): a list, and a small form that adds one
/// (a name, what it does, the address or program, and a command's arguments).
/// </summary>
public sealed partial class CustomActionsViewModel : ObservableObject
{
    private readonly List<CustomAction> _actions;
    private readonly ILocalizer _loc;
    private readonly Action _save;

    public CustomActionsViewModel(List<CustomAction> actions, ILocalizer loc, Action save)
    {
        _actions = actions;
        _loc = loc;
        _save = save;
        Kinds = Enum.GetValues<CustomActionKind>().Select(k => new Choice<CustomActionKind>(k, loc[$"tool.custom.kind_{k}"])).ToList();
        _selectedKind = Kinds[0];
        Refresh();
    }

    public ObservableCollection<CustomActionRow> Rows { get; } = [];
    public IReadOnlyList<Choice<CustomActionKind>> Kinds { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _target = "";
    [ObservableProperty] private string _arguments = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsCommand), nameof(TargetHint))] private Choice<CustomActionKind> _selectedKind;
    [ObservableProperty] private string? _error;

    public bool IsCommand => SelectedKind.Value == CustomActionKind.RunCommand;
    public string TargetHint => _loc[$"tool.custom.target_{SelectedKind.Value}"];
    public bool IsEmpty => Rows.Count == 0;

    [RelayCommand]
    private void Add()
    {
        var action = new CustomAction
        {
            Name = Name.Trim(),
            Kind = SelectedKind.Value,
            Target = Target.Trim().Trim('"'),
            Arguments = IsCommand ? Arguments.Trim() : "",
        };
        if (action.Problem() is { } problem)
        {
            Error = _loc[problem];
            return;
        }
        _actions.Add(action);
        _save();
        Name = Target = Arguments = "";
        Error = null;
        Refresh();
    }

    [RelayCommand]
    private void Remove(CustomActionRow? row)
    {
        if (row is null) return;
        _actions.Remove(row.Action);
        _save();
        Refresh();
    }

    private void Refresh()
    {
        Rows.Clear();
        foreach (var action in _actions)
            Rows.Add(new CustomActionRow(action, _loc[$"tool.custom.kind_{action.Kind}"],
                action.Kind == CustomActionKind.RunCommand && action.Arguments.Length > 0 ? action.Target + " " + action.Arguments : action.Target));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
