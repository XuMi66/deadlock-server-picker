using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace DeadlockServerPicker;

/// <summary>Only one copy runs; a second launch asks the first one to show its window.</summary>
public static class SingleInstance
{
    private const string MutexName = "DeadlockServerPicker.Instance.v1";
    private const string PipeName = "DeadlockServerPicker.Activate.v1";

    private static Mutex? _mutex;

    public static bool TryAcquire(Action onActivateRequested)
    {
        _mutex = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        _ = Task.Run(() => Listen(onActivateRequested));
        return true;
    }

    public static void SignalExisting()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(800);
            using var writer = new StreamWriter(client);
            writer.Write("show");
            writer.Flush();
        }
        catch
        {
            // the other copy is closing; nothing to do
        }
    }

    private static void Listen(Action onActivateRequested)
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                server.WaitForConnection();
                using var reader = new StreamReader(server);
                reader.ReadToEnd();
                onActivateRequested();
            }
            catch
            {
                Thread.Sleep(250);
            }
        }
    }
}
