using System;
using System.IO;

namespace DeadlockServerPicker.Services;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeadlockServerPicker");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
}
