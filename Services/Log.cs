using System;
using System.IO;

namespace DeadlockServerPicker.Services;

public static class Log
{
    private static readonly object Gate = new();
    private static string _path = string.Empty;

    public static void Init(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "log.txt");
        }
        catch
        {
            _path = string.Empty;
        }
    }

    public static void Info(string message) => Write("INF", message);

    public static void Error(string message, Exception? exception = null)
        => Write("ERR", exception is null ? message : message + " :: " + exception.GetType().Name + ": " + exception.Message);

    private static void Write(string level, string message)
    {
        if (string.IsNullOrEmpty(_path)) return;
        try
        {
            lock (Gate)
            {
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // logging must never take the app down
        }
    }
}
