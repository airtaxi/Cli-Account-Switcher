using CommunityToolkit.Mvvm.ComponentModel;

namespace CliAccountSwitcher.WinUI.Models;

public sealed partial class SkillItem : ObservableObject
{
    public required string Name { get; init; }

    public required string DirectoryName { get; init; }

    public required string FullPath { get; init; }

    public required string Description { get; init; }

    public required int FileCount { get; init; }

    public required DateTime LastModified { get; init; }

    public string LastModifiedText => LastModified.ToString("g");

    public string SearchText => $"{Name} {DirectoryName} {Description} {FullPath}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
