namespace MandrillSimulator.Models;

// A project this simulator has been pointed at. Deliberately generic: any JSON
// config file, any key path - nothing here is specific to one codebase.
public class ConnectedProject
{
    public string ConfigPath { get; set; } = string.Empty;
    public string KeyPath { get; set; } = "AppSetting:MandrillBaseUrl";
    public string? OriginalValue { get; set; }
    public bool OriginalKeyExisted { get; set; }
    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.Now;

    public string DisplayName => System.IO.Path.GetFileName(ConfigPath) is { Length: > 0 } n
        ? $"{n}  ({System.IO.Path.GetDirectoryName(ConfigPath)})"
        : ConfigPath;
}
