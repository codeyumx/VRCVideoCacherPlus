using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.ViewModels;

public partial class EditRuleViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Edit Rule";

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _pattern = string.Empty;

    [ObservableProperty]
    private RuleAction _selectedAction = RuleAction.Resolve;

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private bool _cache = false;

    [ObservableProperty]
    private int _maxResolution = 1080;

    [ObservableProperty]
    private int _maxDurationMinutes = 120;

    [ObservableProperty]
    private string _redirectTarget = string.Empty;

    [ObservableProperty]
    private string? _selectedIntegration = null;

    [ObservableProperty]
    private string _patternError = string.Empty;

    [ObservableProperty]
    private bool _isValidPattern = true;

    public RuleAction[] AvailableActions { get; } =
    [
        RuleAction.Direct,
        RuleAction.Resolve,
        RuleAction.Redirect,
        RuleAction.Rewrite,
        RuleAction.Block
    ];

    public string?[] AvailableIntegrations { get; } = [null, "YouTube", "PyPyDance", "VRDancing"];

    public int[] ResolutionOptions { get; } = [0, 720, 1080, 1440, 2160];

    public bool IsCacheOptionVisible => SelectedAction == RuleAction.Resolve;
    public bool IsCacheAction => SelectedAction == RuleAction.Resolve && Cache;
    public bool IsRedirectAction => SelectedAction == RuleAction.Redirect || SelectedAction == RuleAction.Rewrite;
    public string TargetUrlLabel => SelectedAction == RuleAction.Rewrite
        ? Jeek.Avalonia.Localization.Localizer.Get("RewriteTarget")
        : Jeek.Avalonia.Localization.Localizer.Get("RedirectTarget");

    public UriRule RuleResult { get; private set; }

    public event Action<bool>? CloseRequested;

    public EditRuleViewModel(UriRule? ruleToEdit = null)
    {
        if (ruleToEdit != null)
        {
            Title = "Edit Rule";
            RuleResult = ruleToEdit.Clone();
            Name = ruleToEdit.Name;
            Pattern = ruleToEdit.Pattern;
            SelectedAction = ruleToEdit.Action;
            Enabled = ruleToEdit.Enabled;
            Cache = ruleToEdit.Cache;
            MaxResolution = ruleToEdit.MaxResolution ?? 1080;
            MaxDurationMinutes = ruleToEdit.MaxDurationMinutes ?? 120;
            RedirectTarget = ruleToEdit.RedirectTarget;
            SelectedIntegration = ruleToEdit.Integration;
        }
        else
        {
            Title = "Add Rule";
            RuleResult = new UriRule();
            Name = "New Rule";
            Pattern = @"^https?://";
            SelectedAction = RuleAction.Resolve;
            Enabled = true;
            Cache = false;
            MaxResolution = 1080;
            MaxDurationMinutes = 120;
            SelectedIntegration = null;
        }

        ValidatePattern();
    }

    partial void OnPatternChanged(string value)
    {
        ValidatePattern();
    }

    partial void OnSelectedActionChanged(RuleAction value)
    {
        OnPropertyChanged(nameof(IsCacheOptionVisible));
        OnPropertyChanged(nameof(IsCacheAction));
        OnPropertyChanged(nameof(IsRedirectAction));
        OnPropertyChanged(nameof(TargetUrlLabel));
    }

    partial void OnCacheChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCacheAction));
    }

    private void ValidatePattern()
    {
        if (string.IsNullOrWhiteSpace(Pattern))
        {
            IsValidPattern = false;
            PatternError = "Pattern cannot be empty.";
            return;
        }

        try
        {
            _ = new Regex(Pattern);
            IsValidPattern = true;
            PatternError = string.Empty;
        }
        catch (Exception ex)
        {
            IsValidPattern = false;
            PatternError = $"Invalid Regex: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Save()
    {
        ValidatePattern();
        if (!IsValidPattern) return;

        RuleResult.Name = Name;
        RuleResult.Pattern = Pattern;
        RuleResult.Action = SelectedAction;
        RuleResult.Enabled = Enabled;
        RuleResult.Cache = IsCacheAction;
        RuleResult.MaxResolution = IsCacheAction ? MaxResolution : null;
        RuleResult.MaxDurationMinutes = IsCacheAction ? MaxDurationMinutes : null;
        RuleResult.RedirectTarget = IsRedirectAction ? RedirectTarget : string.Empty;
        RuleResult.Integration = SelectedIntegration;

        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(false);
    }
}
