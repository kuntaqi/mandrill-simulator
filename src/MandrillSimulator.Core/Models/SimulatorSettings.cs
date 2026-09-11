namespace MandrillSimulator.Models;

public class SimulatorSettings
{
    public int Port { get; set; } = 8025;
    public bool AutoStart { get; set; } = true;
    public bool AutoSelectNewest { get; set; } = true;
    public bool DarkTheme { get; set; }
    // The tool updates itself from its own releases; override for a private feed.
    public string? UpdateFeedUrl { get; set; } = "https://github.com/kuntaqi/mandrill-simulator";
    public string? UpdateFeedToken { get; set; }
    public List<ConnectedProject> RecentProjects { get; set; } = [];
}
