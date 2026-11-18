using System.Collections.Generic;

namespace DeadlockServerPicker.Models;

public class AppSettings
{
    public Dictionary<string, string> ProbeIps { get; set; } = new();
    public string LastServerRevision { get; set; } = string.Empty;
    public bool LivePingEnabled { get; set; } = true;
    public double LivePingIntervalSeconds { get; set; } = 2.5;
    public double PopulationIntervalSeconds { get; set; } = 60;
    public string Language { get; set; } = string.Empty;
}
