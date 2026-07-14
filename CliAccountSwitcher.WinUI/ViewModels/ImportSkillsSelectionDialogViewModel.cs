using CliAccountSwitcher.WinUI.Models;
using CliAccountSwitcher.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace CliAccountSwitcher.WinUI.ViewModels;

public sealed partial class ImportSkillsSelectionDialogViewModel : ObservableObject
{
    private readonly LocalizationService _localizationService;
    private readonly ObservableCollection<SkillItem> _skills = [];
    private readonly ObservableCollection<SkillItem> _filteredSkills = [];
    private bool _isSynchronizingSelection;

    public ImportSkillsSelectionDialogViewModel(LocalizationService localizationService) => _localizationService = localizationService;

    public ObservableCollection<SkillItem> Skills => _skills;

    public ObservableCollection<SkillItem> FilteredSkills => _filteredSkills;

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSkills))]
    [NotifyPropertyChangedFor(nameof(IsPrimaryButtonEnabled))]
    [NotifyPropertyChangedFor(nameof(SelectedCountText))]
    public partial IReadOnlyList<string> SelectedSkillDirectoryNames { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSkills))]
    [NotifyPropertyChangedFor(nameof(HasNoSkills))]
    public partial int SkillCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilteredSkills))]
    public partial int FilteredSkillCount { get; set; }

    [ObservableProperty]
    public partial bool? AllSkillsSelectionState { get; set; } = true;

    public bool HasSkills => SkillCount > 0;

    public bool HasNoSkills => SkillCount == 0;

    public bool HasFilteredSkills => FilteredSkillCount > 0;

    public bool HasSelectedSkills => SelectedSkillDirectoryNames.Count > 0;

    public bool IsPrimaryButtonEnabled => HasSelectedSkills;

    public string SelectedCountText => SelectedSkillDirectoryNames.Count == 0 ? _localizationService.GetLocalizedString("ImportSkillsSelectionDialogViewModel_NoSelectedSkills") : _localizationService.GetFormattedString("ImportSkillsSelectionDialogViewModel_SelectedSkillCountFormat", SelectedSkillDirectoryNames.Count);

    public void LoadSkills(IReadOnlyList<SkillItem> skillItems)
    {
        _skills.Clear();
        foreach (var skillItem in skillItems)
        {
            skillItem.IsSelected = true;
            skillItem.PropertyChanged += OnSkillItemPropertyChanged;
            _skills.Add(skillItem);
        }

        SkillCount = _skills.Count;
        ApplyFilter();
        RefreshSelectedSkillDirectoryNamesFromSkillItems();
    }

    public void SetAllSelection(bool isSelected)
    {
        _isSynchronizingSelection = true;
        try
        {
            foreach (var skillItem in _filteredSkills)
            {
                skillItem.IsSelected = isSelected;
            }
        }
        finally { _isSynchronizingSelection = false; }

        RefreshSelectedSkillDirectoryNamesFromSkillItems();
    }

    public IReadOnlyList<string> GetSelectedSkillDirectoryNames() =>
    [.. _skills.Where(skillItem => skillItem.IsSelected).Select(skillItem => skillItem.DirectoryName)];

    private void OnSkillItemPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs propertyChangedEventArguments)
    {
        if (propertyChangedEventArguments.PropertyName != nameof(SkillItem.IsSelected)) return;
        if (_isSynchronizingSelection) return;

        RefreshSelectedSkillDirectoryNamesFromSkillItems();
    }

    private void ApplyFilter()
    {
        var normalizedSearchText = (SearchText ?? "").Trim();
        var filteredSkillItems = _skills.Where(skillItem => IsSkillVisible(skillItem, normalizedSearchText)).ToList();
        var filteredSkillItemSet = filteredSkillItems.ToHashSet();

        for (var skillIndex = _filteredSkills.Count - 1; skillIndex >= 0; skillIndex--)
        {
            if (!filteredSkillItemSet.Contains(_filteredSkills[skillIndex]))
            {
                _filteredSkills.RemoveAt(skillIndex);
            }
        }

        for (var skillIndex = 0; skillIndex < filteredSkillItems.Count; skillIndex++)
        {
            var skillItem = filteredSkillItems[skillIndex];
            var currentSkillIndex = _filteredSkills.IndexOf(skillItem);

            if (currentSkillIndex < 0) _filteredSkills.Insert(skillIndex, skillItem);
            else if (currentSkillIndex != skillIndex) _filteredSkills.Move(currentSkillIndex, skillIndex);
        }

        FilteredSkillCount = _filteredSkills.Count;
        RefreshAllSkillsSelectionState();
    }

    private static bool IsSkillVisible(SkillItem skillItem, string normalizedSearchText) => string.IsNullOrWhiteSpace(normalizedSearchText) || skillItem.SearchText.Contains(normalizedSearchText, StringComparison.CurrentCultureIgnoreCase);

    private void RefreshSelectedSkillDirectoryNamesFromSkillItems()
    {
        var selectedDirectoryNames = _skills.Where(skillItem => skillItem.IsSelected).Select(skillItem => skillItem.DirectoryName).ToArray();
        SelectedSkillDirectoryNames = selectedDirectoryNames;
        RefreshAllSkillsSelectionState();
    }

    private void RefreshAllSkillsSelectionState()
    {
        if (_filteredSkills.Count == 0)
        {
            AllSkillsSelectionState = false;
            return;
        }

        var selectedFilteredSkillCount = _filteredSkills.Count(skillItem => skillItem.IsSelected);
        AllSkillsSelectionState = selectedFilteredSkillCount == 0 ? false : selectedFilteredSkillCount == _filteredSkills.Count ? true : null;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
}