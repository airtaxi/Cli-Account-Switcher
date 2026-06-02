using CliAccountSwitcher.Api.Providers.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CliAccountSwitcher.WinUI.Models;

public sealed partial class SkillItem : ObservableObject
{
    [ObservableProperty]
    public partial CliProviderKind ProviderKind { get; set; } = CliProviderKind.Codex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchText))]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchText))]
    public partial string DirectoryName { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchText))]
    public partial string FullPath { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchText))]
    public partial string Description { get; set; } = "";

    [ObservableProperty]
    public partial int FileCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastModifiedText))]
    public partial DateTime LastModified { get; set; }

    public string LastModifiedText => LastModified.ToString("g");

    public string SearchText => $"{Name} {DirectoryName} {Description} {FullPath}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public void Update(SkillItem skillItem)
    {
        ProviderKind = skillItem.ProviderKind;
        Name = skillItem.Name;
        DirectoryName = skillItem.DirectoryName;
        FullPath = skillItem.FullPath;
        Description = skillItem.Description;
        FileCount = skillItem.FileCount;
        LastModified = skillItem.LastModified;
    }
}
