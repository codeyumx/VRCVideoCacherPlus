using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Models;
using VRCVideoCacher.Views;

namespace VRCVideoCacher.ViewModels;

public partial class RuleEntryViewModel : ObservableObject
{
    public UriRule Rule { get; }
    public Action? OnEnabledChanged;

    public string Name => Rule.Name;
    public string Pattern => Rule.Pattern;
    public RuleAction Action => Rule.Action;
    public string ActionSummary => Rule.GetActionSummary();
    public string? Integration => Rule.Integration;
    public string IntegrationDisplay => string.IsNullOrEmpty(Rule.Integration) ? string.Empty : "*";

    [ObservableProperty]
    private bool _isMatched;

    private double _flashOpacity = 0.0;
    private CancellationTokenSource? _flashCts;

    public string RowBackground
    {
        get
        {
            if (IsMatched)
                return "#1E4D2B";

            if (_flashOpacity > 0.0)
            {
                int alpha = (int)(_flashOpacity * 255);
                return $"#{alpha:X2}1DB954";
            }

            return "Transparent";
        }
    }

    public void TriggerFlash()
    {
        _flashCts?.Cancel();
        _flashCts = new CancellationTokenSource();
        var token = _flashCts.Token;

        _flashOpacity = 1.0;
        OnPropertyChanged(nameof(RowBackground));

        Task.Run(async () =>
        {
            const int totalSteps = 40;
            const int stepDelayMs = 50;

            for (int i = 1; i <= totalSteps; i++)
            {
                if (token.IsCancellationRequested)
                    return;

                try
                {
                    await Task.Delay(stepDelayMs, token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                _flashOpacity = 1.0 - ((double)i / totalSteps);

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    OnPropertyChanged(nameof(RowBackground));
                });
            }

            _flashOpacity = 0.0;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(RowBackground));
            });
        }, token);
    }

    partial void OnIsMatchedChanged(bool value)
    {
        OnPropertyChanged(nameof(RowBackground));
    }

    public bool Enabled
    {
        get => Rule.Enabled;
        set
        {
            if (Rule.Enabled != value)
            {
                Rule.Enabled = value;
                OnPropertyChanged();
                OnEnabledChanged?.Invoke();
            }
        }
    }

    public RuleEntryViewModel(UriRule rule)
    {
        Rule = rule;
    }

    public void RefreshProperties()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Pattern));
        OnPropertyChanged(nameof(Action));
        OnPropertyChanged(nameof(ActionSummary));
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(Integration));
        OnPropertyChanged(nameof(IntegrationDisplay));
    }
}

public partial class RulesViewModel : ViewModelBase
{
    private bool _isLoading;

    public ObservableCollection<RuleEntryViewModel> Rules { get; } = [];

    [ObservableProperty]
    private string _testUrl = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessageColor = "#81C784";

    [ObservableProperty]
    private bool _hasChanges;

    public RulesViewModel()
    {
        ConfigManager.OnConfigChanged += LoadFromConfig;
        Services.RuleEngine.OnRuleMatched += HandleRuleMatched;
        LoadFromConfig();
    }

    private void HandleRuleMatched(string ruleId)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var entry = Rules.FirstOrDefault(r => r.Rule.Id == ruleId);
            entry?.TriggerFlash();
        });
    }

    private RuleEntryViewModel CreateEntry(UriRule rule)
    {
        var entry = new RuleEntryViewModel(rule);
        entry.OnEnabledChanged += () =>
        {
            if (_isLoading) return;
            EvaluateTestUrl();
            SetHasChanges();
        };
        return entry;
    }

    partial void OnTestUrlChanged(string value)
    {
        EvaluateTestUrl();
    }

    public void EvaluateTestUrl()
    {
        var url = TestUrl?.Trim();
        bool foundMatch = false;

        foreach (var entry in Rules)
        {
            if (!foundMatch && !string.IsNullOrWhiteSpace(url) && entry.Enabled && !string.IsNullOrWhiteSpace(entry.Pattern))
            {
                try
                {
                    // Shared cache with the request path, so typing in the test box doesn't
                    // recompile every rule on every keystroke — and so the live matcher
                    // uses exactly the regex options the real evaluation does.
                    if (Services.RuleEngine.GetRegex(entry.Pattern).IsMatch(url))
                    {
                        entry.IsMatched = true;
                        foundMatch = true;
                        continue;
                    }
                }
                catch
                {
                    // Invalid regex in rule
                }
            }
            entry.IsMatched = false;
        }
    }

    public void LoadFromConfig()
    {
        _isLoading = true;
        Rules.Clear();

        var configRules = PlusConfigManager.Config.UriRules;
        if (configRules == null || configRules.Count == 0)
        {
            PlusConfigManager.EnsureDefaultRules();
            configRules = PlusConfigManager.Config.UriRules;
        }

        // Clone on the way in. The entries used to hold the very objects stored in
        // PlusConfigManager.Config.UriRules, so editing a rule or flipping its checkbox
        // mutated the live config immediately — RuleEngine picked the change up before the
        // user pressed Save, and "Discard" re-read those same mutated objects and appeared
        // to do nothing. Working on copies is what makes the unsaved-changes guard real.
        foreach (var rule in configRules)
        {
            Rules.Add(CreateEntry(rule.Clone()));
        }

        HasChanges = false;
        StatusMessage = string.Empty;
        _isLoading = false;
        EvaluateTestUrl();
    }

    private void SetHasChanges()
    {
        if (_isLoading) return;
        HasChanges = true;
        StatusMessage = Localizer.Get("SettingsUnsavedChanges");
        StatusMessageColor = "#FFB74D";
    }

    public void SaveToConfig()
    {
        // Clone on the way out too, so the entries keep their own instances and the next
        // edit doesn't reach straight back into the saved config.
        PlusConfigManager.Config.UriRules = Rules.Select(r => r.Rule.Clone()).ToList();
        PlusConfigManager.TrySaveConfig();
        HasChanges = false;
        StatusMessage = Localizer.Get("SettingsSaved");
        StatusMessageColor = "#81C784";
        EvaluateTestUrl();
    }

    public async Task<bool> CheckUnsavedChangesAsync(Window? parentWindow)
    {
        if (!HasChanges) return true;

        // With no window to parent the dialog on we cannot ask, and the old code fell
        // through to dialog.Confirmed == false and discarded silently. Keep the edits
        // pending in the view model instead — the user can still save them later.
        if (parentWindow == null)
            return true;

        var message = Localizer.Get("UnsavedRulesMessage");
        var dialog = Views.PopupWindow.CreateConfirm(message, Localizer.Get("Save"), Localizer.Get("Discard"));
        await dialog.ShowDialog(parentWindow);

        if (dialog.Confirmed)
        {
            SaveToConfig();
        }
        else
        {
            LoadFromConfig();
        }
        return true;
    }

    [RelayCommand]
    private async Task AddRule()
    {
        var editVm = new EditRuleViewModel(null);
        var window = new EditRuleWindow(editVm);

        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var parentWindow = lifetime?.MainWindow;

        var result = parentWindow != null
            ? await window.ShowDialog<bool>(parentWindow)
            : false;

        if (result)
        {
            var newEntry = CreateEntry(editVm.RuleResult);
            Rules.Add(newEntry);
            EvaluateTestUrl();
            SetHasChanges();
        }
    }

    public async Task AddRuleWithPattern(string pattern)
    {
        var rule = new UriRule
        {
            Name = "New Rule",
            Pattern = pattern,
            Action = RuleAction.Block,
            Enabled = true
        };
        var editVm = new EditRuleViewModel(rule);
        var window = new EditRuleWindow(editVm);

        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var parentWindow = lifetime?.MainWindow;

        var result = parentWindow != null
            ? await window.ShowDialog<bool>(parentWindow)
            : false;

        if (result)
        {
            var newEntry = CreateEntry(editVm.RuleResult);
            Rules.Add(newEntry);
            EvaluateTestUrl();
            SetHasChanges();
        }
    }

    [RelayCommand]
    private async Task EditRule(RuleEntryViewModel? entry)
    {
        if (entry == null) return;

        var editVm = new EditRuleViewModel(entry.Rule);
        var window = new EditRuleWindow(editVm);

        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var parentWindow = lifetime?.MainWindow;

        var result = parentWindow != null
            ? await window.ShowDialog<bool>(parentWindow)
            : false;

        if (result)
        {
            entry.Rule.Name = editVm.RuleResult.Name;
            entry.Rule.Pattern = editVm.RuleResult.Pattern;
            entry.Rule.Action = editVm.RuleResult.Action;
            entry.Rule.Enabled = editVm.RuleResult.Enabled;
            entry.Rule.Cache = editVm.RuleResult.Cache;
            entry.Rule.MaxResolution = editVm.RuleResult.MaxResolution;
            entry.Rule.MaxDurationMinutes = editVm.RuleResult.MaxDurationMinutes;
            entry.Rule.RedirectTarget = editVm.RuleResult.RedirectTarget;
            entry.Rule.Integration = editVm.RuleResult.Integration;

            entry.RefreshProperties();
            EvaluateTestUrl();
            SetHasChanges();
        }
    }

    [RelayCommand]
    private void MoveRuleUp(RuleEntryViewModel? entry)
    {
        if (entry == null) return;
        var index = Rules.IndexOf(entry);
        if (index > 0)
        {
            Rules.RemoveAt(index);
            Rules.Insert(index - 1, entry);
            EvaluateTestUrl();
            SetHasChanges();
        }
    }

    [RelayCommand]
    private void MoveRuleDown(RuleEntryViewModel? entry)
    {
        if (entry == null) return;
        var index = Rules.IndexOf(entry);
        if (index >= 0 && index < Rules.Count - 1)
        {
            Rules.RemoveAt(index);
            Rules.Insert(index + 1, entry);
            EvaluateTestUrl();
            SetHasChanges();
        }
    }

    [RelayCommand]
    private void MoveRuleToTop(RuleEntryViewModel? entry)
    {
        if (entry == null) return;
        var index = Rules.IndexOf(entry);
        if (index > 0)
        {
            Rules.RemoveAt(index);
            Rules.Insert(0, entry);
            EvaluateTestUrl();
            SetHasChanges();
        }
    }

    [RelayCommand]
    private void MoveRuleToBottom(RuleEntryViewModel? entry)
    {
        if (entry == null) return;
        var index = Rules.IndexOf(entry);
        if (index >= 0 && index < Rules.Count - 1)
        {
            Rules.RemoveAt(index);
            Rules.Add(entry);
            EvaluateTestUrl();
            SetHasChanges();
        }
    }

    [RelayCommand]
    private void DeleteRule(RuleEntryViewModel? entry)
    {
        if (entry == null) return;
        Rules.Remove(entry);
        EvaluateTestUrl();
        SetHasChanges();
    }

    [RelayCommand]
    private async Task ResetDefaultRulesAsync(Window? parentWindow)
    {
        var message = Localizer.Get("ConfirmResetDefaultsMessage");
        var dialog = Views.PopupWindow.CreateConfirm(message, Localizer.Get("Yes"), Localizer.Get("No"));

        if (parentWindow != null)
        {
            await dialog.ShowDialog(parentWindow);
        }

        if (dialog.Confirmed)
        {
            _isLoading = true;
            Rules.Clear();
            foreach (var rule in ConfigModel.GetDefaultRules())
            {
                Rules.Add(CreateEntry(rule));
            }
            _isLoading = false;
            EvaluateTestUrl();
            SetHasChanges();
        }
    }

    [RelayCommand]
    private void SaveRules()
    {
        SaveToConfig();
    }
}
