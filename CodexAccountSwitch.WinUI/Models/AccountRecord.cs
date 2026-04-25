namespace CodexAccountSwitch.WinUI.Models;

public sealed class AccountRecord
{
    public string Name { get; set; } = "";

    public string Group { get; set; } = "personal";

    public string AccountIdentifier { get; set; } = "";

    public string AuthenticationFilePath { get; set; } = "";

    public string AccessTokenPreview { get; set; } = "";

    public string UpdatedAtText { get; set; } = "";

    public bool IsCurrent { get; set; }

    public bool HasAuthenticationFile { get; set; }

    public string StatusText { get; set; } = "";

    public string DisplayName => string.IsNullOrWhiteSpace(Group) || Group == "personal" ? Name : $"{Group} / {Name}";
}
