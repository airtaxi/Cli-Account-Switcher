using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;

namespace CliAccountSwitcher.WinUI.ViewModels;

public sealed partial class SkillsPageViewModel : ObservableObject, IDisposable
{
    private readonly ApplicationSettings _applicationSettings;
    private readonly LocalizationService _localizationService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly SkillService _skillService;
    private readonly HashSet<string> _selectedSkillDirectoryNames = new(StringComparer.Ordinal);
    private bool _isSynchronizingSkillSelection;
    private bool _disposed;

    public SkillsPageViewModel(SkillService skillService, ApplicationSettings applicationSettings, LocalizationService localizationService, DispatcherQueue dispatcherQueue)
    {
        _skillService = skillService;
        _applicationSettings = applicationSettings;
        _localizationService = localizationService;
        _dispatcherQueue = dispatcherQueue;
        SelectedProviderKind = _applicationSettings.SelectedProviderKind;
        _applicationSettings.PropertyChanged += OnApplicationSettingsPropertyChanged;
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<CliProviderKind>>(this, OnProviderKindChangedMessageReceived);
        ReloadSkills();
    }

    public ObservableCollection<SkillItem> Skills { get; } = [];

    public ObservableCollection<SkillItem> FilteredSkills { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCodexProviderSelected))]
    [NotifyPropertyChangedFor(nameof(IsClaudeCodeProviderSelected))]
    [NotifyPropertyChangedFor(nameof(DescriptionText))]
    public partial CliProviderKind SelectedProviderKind { get; set; } = CliProviderKind.Codex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSkills))]
    [NotifyPropertyChangedFor(nameof(HasNoSkills))]
    [NotifyPropertyChangedFor(nameof(HasNoFilteredSkills))]
    public partial int SkillCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilteredSkills))]
    [NotifyPropertyChangedFor(nameof(HasNoFilteredSkills))]
    public partial int FilteredSkillCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSkills))]
    [NotifyPropertyChangedFor(nameof(SelectedSkillCountText))]
    public partial IReadOnlyList<string> SelectedSkillDirectoryNames { get; set; } = [];

    [ObservableProperty]
    public partial bool? FilteredSkillsSelectionState { get; set; } = false;

    public bool HasSkills => SkillCount > 0;

    public bool HasNoSkills => SkillCount == 0;

    public bool HasFilteredSkills => FilteredSkillCount > 0;

    public bool HasNoFilteredSkills => SkillCount > 0 && FilteredSkillCount == 0;

    public bool HasSelectedSkills => SelectedSkillDirectoryNames.Count > 0;

    public string SelectedSkillCountText => SelectedSkillDirectoryNames.Count == 0 ? _localizationService.GetLocalizedString("SkillsPageViewModel_NoSelectedSkills") : _localizationService.GetFormattedString("SkillsPageViewModel_SelectedSkillCountFormat", SelectedSkillDirectoryNames.Count);

    public bool IsCodexProviderSelected => SelectedProviderKind == CliProviderKind.Codex;

    public bool IsClaudeCodeProviderSelected => SelectedProviderKind == CliProviderKind.ClaudeCode;

    public string DescriptionText => _localizationService.GetFormattedString("SkillsPageViewModel_DescriptionFormat", GetProviderDisplayName(SelectedProviderKind));

    public void ReloadSkills() => ReloadSkills(_applicationSettings.SelectedProviderKind);

    public Task ReloadSkillsAsync()
    {
        ReloadSkills();
        return Task.CompletedTask;
    }

    public void SetSelectedSkillDirectoryNames(IEnumerable<string> skillDirectoryNames)
    {
        _selectedSkillDirectoryNames.Clear();
        foreach (var skillDirectoryName in skillDirectoryNames.Where(skillDirectoryName => !string.IsNullOrWhiteSpace(skillDirectoryName))) _selectedSkillDirectoryNames.Add(skillDirectoryName);
        _isSynchronizingSkillSelection = true;
        try
        {
            foreach (var skillItem in Skills) skillItem.IsSelected = skillItem.ProviderKind == SelectedProviderKind && _selectedSkillDirectoryNames.Contains(skillItem.DirectoryName);
        }
        finally { _isSynchronizingSkillSelection = false; }
        SelectedSkillDirectoryNames = [.. _selectedSkillDirectoryNames];
        RefreshFilteredSkillsSelectionState();
    }

    public void SetFilteredSkillsSelection(bool isSelected)
    {
        _isSynchronizingSkillSelection = true;
        try
        {
            foreach (var skillItem in FilteredSkills) skillItem.IsSelected = isSelected;
        }
        finally { _isSynchronizingSkillSelection = false; }

        RefreshSelectedSkillDirectoryNamesFromSkillItems();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _applicationSettings.PropertyChanged -= OnApplicationSettingsPropertyChanged;
        foreach (var skillItem in Skills) skillItem.PropertyChanged -= OnSkillItemPropertyChanged;
        WeakReferenceMessenger.Default.Unregister<ValueChangedMessage<CliProviderKind>>(this);
    }

    private void ReloadSkills(CliProviderKind providerKind)
    {
        SelectedProviderKind = providerKind;
        SynchronizeSkills(_skillService.ScanSkills(providerKind));
        ApplyFilter();
        RefreshSkillStateProperties();
    }

    private void OnApplicationSettingsPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs propertyChangedEventArguments)
    {
        if (propertyChangedEventArguments.PropertyName is not nameof(ApplicationSettings.SelectedProviderKind)) return;
        if (_dispatcherQueue.HasThreadAccess) ApplyProviderKindChange(_applicationSettings.SelectedProviderKind);
        else _dispatcherQueue.TryEnqueue(() => ApplyProviderKindChange(_applicationSettings.SelectedProviderKind));
    }

    private void OnProviderKindChangedMessageReceived(object recipient, ValueChangedMessage<CliProviderKind> valueChangedMessage)
    {
        if (_dispatcherQueue.HasThreadAccess) ApplyProviderKindChange(valueChangedMessage.Value);
        else _dispatcherQueue.TryEnqueue(() => ApplyProviderKindChange(valueChangedMessage.Value));
    }

    private void OnSkillItemPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs propertyChangedEventArguments)
    {
        if (propertyChangedEventArguments.PropertyName != nameof(SkillItem.IsSelected)) return;
        if (_isSynchronizingSkillSelection) return;

        RefreshSelectedSkillDirectoryNamesFromSkillItems();
    }

    private void SynchronizeSkills(IReadOnlyList<SkillItem> skillItems)
    {
        var skillKeys = skillItems.Select(CreateSkillKey).ToHashSet();
        _isSynchronizingSkillSelection = true;
        try
        {
            for (var skillIndex = Skills.Count - 1; skillIndex >= 0; skillIndex--)
            {
                var skillItem = Skills[skillIndex];
                if (skillKeys.Contains(CreateSkillKey(skillItem))) continue;
                skillItem.PropertyChanged -= OnSkillItemPropertyChanged;
                _selectedSkillDirectoryNames.Remove(skillItem.DirectoryName);
                Skills.RemoveAt(skillIndex);
            }

            for (var skillIndex = 0; skillIndex < skillItems.Count; skillIndex++)
            {
                var skillItem = skillItems[skillIndex];
                var existingSkillItem = Skills.FirstOrDefault(candidateSkillItem => IsSameSkill(candidateSkillItem, skillItem));
                if (existingSkillItem is null)
                {
                    Skills.Insert(skillIndex, CreateSkillItemViewModel(skillItem));
                    continue;
                }

                existingSkillItem.Update(skillItem);
                existingSkillItem.IsSelected = _selectedSkillDirectoryNames.Contains(skillItem.DirectoryName);
                var currentSkillIndex = Skills.IndexOf(existingSkillItem);
                if (currentSkillIndex != skillIndex) Skills.Move(currentSkillIndex, skillIndex);
            }
        }
        finally { _isSynchronizingSkillSelection = false; }

        RefreshSelectedSkillDirectoryNamesFromSkillItems();
    }

    private SkillItem CreateSkillItemViewModel(SkillItem skillItem)
    {
        var viewModel = new SkillItem
        {
            ProviderKind = skillItem.ProviderKind,
            Name = skillItem.Name,
            DirectoryName = skillItem.DirectoryName,
            FullPath = skillItem.FullPath,
            Description = skillItem.Description,
            FileCount = skillItem.FileCount,
            LastModified = skillItem.LastModified,
            IsSelected = _selectedSkillDirectoryNames.Contains(skillItem.DirectoryName)
        };
        viewModel.PropertyChanged += OnSkillItemPropertyChanged;
        return viewModel;
    }

    private void ApplyFilter()
    {
        var normalizedSearchText = (SearchText ?? "").Trim();
        var filteredSkillItems = Skills.Where(skillItem => skillItem.ProviderKind == SelectedProviderKind && IsSkillVisible(skillItem, normalizedSearchText)).ToList();
        var filteredSkillItemSet = filteredSkillItems.ToHashSet();

        for (var skillIndex = FilteredSkills.Count - 1; skillIndex >= 0; skillIndex--)
        {
            if (!filteredSkillItemSet.Contains(FilteredSkills[skillIndex])) FilteredSkills.RemoveAt(skillIndex);
        }

        for (var skillIndex = 0; skillIndex < filteredSkillItems.Count; skillIndex++)
        {
            var skillItem = filteredSkillItems[skillIndex];
            var currentSkillIndex = FilteredSkills.IndexOf(skillItem);

            if (currentSkillIndex < 0) FilteredSkills.Insert(skillIndex, skillItem);
            else if (currentSkillIndex != skillIndex) FilteredSkills.Move(currentSkillIndex, skillIndex);
        }

        RefreshSkillCounts();
        RefreshFilteredSkillsSelectionState();
    }

    private static bool IsSkillVisible(SkillItem skillItem, string normalizedSearchText) => string.IsNullOrWhiteSpace(normalizedSearchText) || skillItem.SearchText.Contains(normalizedSearchText, StringComparison.CurrentCultureIgnoreCase);

    private void RefreshSkillStateProperties() => RefreshSkillCounts();

    private void RefreshSelectedSkillDirectoryNamesFromSkillItems()
    {
        _selectedSkillDirectoryNames.Clear();
        foreach (var skillItem in Skills.Where(skillItem => skillItem.ProviderKind == SelectedProviderKind && skillItem.IsSelected)) _selectedSkillDirectoryNames.Add(skillItem.DirectoryName);

        SelectedSkillDirectoryNames = [.. _selectedSkillDirectoryNames];
        RefreshFilteredSkillsSelectionState();
    }

    private void RefreshFilteredSkillsSelectionState()
    {
        if (FilteredSkills.Count == 0)
        {
            FilteredSkillsSelectionState = false;
            return;
        }

        var selectedFilteredSkillCount = FilteredSkills.Count(skillItem => skillItem.IsSelected);
        FilteredSkillsSelectionState = selectedFilteredSkillCount == 0 ? false : selectedFilteredSkillCount == FilteredSkills.Count ? true : null;
    }

    private void RefreshSkillCounts()
    {
        SkillCount = Skills.Count;
        FilteredSkillCount = FilteredSkills.Count;
    }

    private void ApplyProviderKindChange(CliProviderKind providerKind)
    {
        if (SelectedProviderKind == providerKind) return;

        SetSelectedSkillDirectoryNames([]);
        ReloadSkills(providerKind);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private string GetProviderDisplayName(CliProviderKind providerKind) => providerKind switch { CliProviderKind.ClaudeCode => _localizationService.GetLocalizedString("Provider_ClaudeCodeDisplayName"), _ => _localizationService.GetLocalizedString("Provider_CodexDisplayName") };

    private static (CliProviderKind providerKind, string directoryName) CreateSkillKey(SkillItem skillItem) => (skillItem.ProviderKind, skillItem.DirectoryName);

    private static bool IsSameSkill(SkillItem firstSkillItem, SkillItem secondSkillItem) => firstSkillItem.ProviderKind == secondSkillItem.ProviderKind && string.Equals(firstSkillItem.DirectoryName, secondSkillItem.DirectoryName, StringComparison.Ordinal);

}
