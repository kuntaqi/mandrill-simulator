namespace MandrillSimulator.Models;

public class SimulatorSettings
{
    public int Port { get; set; } = 8025;
    public bool AutoStart { get; set; } = true;
    public bool AutoSelectNewest { get; set; } = true;
    public bool DarkTheme { get; set; }
    public List<ConnectedProject> RecentProjects { get; set; } = [];
}
