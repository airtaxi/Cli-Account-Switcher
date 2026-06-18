namespace CliAccountSwitcher.Api.Providers.Zai.Models;

public sealed class ZaiChelperConfig
{
    public string ApiKey { get; set; } = "";

    public string Plan { get; set; } = "";

    public string Language { get; set; } = "";

    public bool IsValid => !string.IsNullOrWhiteSpace(ApiKey);

    public bool IsChinaPlan => string.Equals(Plan, "glm_coding_plan_china", StringComparison.Ordinal);

    public static ZaiChelperConfig Parse(string configText)
    {
        var config = new ZaiChelperConfig();
        foreach (var rawLine in configText.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] is '#' or '-') continue;

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0) continue;

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"', '\'');

            if (key == "api_key") config.ApiKey = value;
            else if (key == "plan") config.Plan = value;
            else if (key == "lang") config.Language = value;
        }

        return config;
    }
}
