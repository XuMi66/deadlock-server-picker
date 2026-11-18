using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeadlockServerPicker.Models;

namespace DeadlockServerPicker.Services;

public sealed class FirewallResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public List<string> Applied { get; init; } = new();
    public List<string> Failed { get; init; } = new();
}

public sealed class FirewallService
{
    public const string RulePrefix = "CS2ServerPicker_";

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public async Task<HashSet<string>> GetBlockedNamesAsync(CancellationToken cancellationToken)
    {
        var script = new StringBuilder();
        script.AppendLine("$ProgressPreference = 'SilentlyContinue'; [Console]::OutputEncoding = [System.Text.Encoding]::UTF8;");
        script.AppendLine($"Get-NetFirewallRule -DisplayName '{RulePrefix}*' -ErrorAction SilentlyContinue | ForEach-Object {{ $_.DisplayName }}");

        var (exitCode, output, error) = await RunAsync(script.ToString(), cancellationToken).ConfigureAwait(false);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (exitCode != 0)
        {
            Log.Error("Get-NetFirewallRule exit " + exitCode + " " + error);
            return names;
        }

        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = line.Trim();
            if (name.StartsWith(RulePrefix, StringComparison.OrdinalIgnoreCase)) names.Add(name);
        }

        return names;
    }

    public async Task<FirewallResult> ApplyAsync(IReadOnlyList<ServerInfo> blockedServers, CancellationToken cancellationToken)
    {
        var script = new StringBuilder();
        script.AppendLine("$ProgressPreference = 'SilentlyContinue'; [Console]::OutputEncoding = [System.Text.Encoding]::UTF8;");
        script.AppendLine($"Remove-NetFirewallRule -DisplayName '{RulePrefix}*' -ErrorAction SilentlyContinue");

        foreach (var server in blockedServers)
        {
            var addresses = string.Join(",", server.Relays.Select(Quote));
            script.AppendLine("try {");
            script.AppendLine($"  New-NetFirewallRule -DisplayName {Quote(RulePrefix + server.Name)} -Direction Outbound -Action Block -RemoteAddress @({addresses}) -Profile Any -Enabled True -ErrorAction Stop | Out-Null");
            script.AppendLine($"  Write-Output ('OK ' + {Quote(server.Name)})");
            script.AppendLine("} catch {");
            script.AppendLine($"  Write-Output ('FAIL ' + {Quote(server.Name)} + ' :: ' + $_.Exception.Message)");
            script.AppendLine("}");
        }

        var (exitCode, output, error) = await RunAsync(script.ToString(), cancellationToken).ConfigureAwait(false);
        if (exitCode != 0 && string.IsNullOrWhiteSpace(output))
        {
            Log.Error("New-NetFirewallRule exit " + exitCode + " " + error);
            return new FirewallResult { Ok = false, Error = string.IsNullOrWhiteSpace(error) ? "PowerShell 退出码 " + exitCode : error.Trim() };
        }

        var applied = new List<string>();
        var failed = new List<string>();
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var text = line.Trim();
            if (text.Length == 0) continue;
            if (text.StartsWith("OK ", StringComparison.Ordinal)) applied.Add(text.Substring(3));
            else if (text.StartsWith("FAIL ", StringComparison.Ordinal)) failed.Add(text.Substring(5));
        }

        return new FirewallResult { Ok = failed.Count == 0, Applied = applied, Failed = failed, Error = failed.Count == 0 ? null : failed[0] };
    }

    public async Task<bool> ResetAsync(CancellationToken cancellationToken)
    {
        var script = new StringBuilder();
        script.AppendLine("$ProgressPreference = 'SilentlyContinue'; [Console]::OutputEncoding = [System.Text.Encoding]::UTF8;");
        script.AppendLine($"Remove-NetFirewallRule -DisplayName '{RulePrefix}*' -ErrorAction SilentlyContinue");
        script.AppendLine("Write-Output 'DONE'");

        var (exitCode, output, error) = await RunAsync(script.ToString(), cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            Log.Error("Remove-NetFirewallRule exit " + exitCode + " " + error);
            return false;
        }

        return output.Contains("DONE", StringComparison.Ordinal);
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string script, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return (-1, string.Empty, "无法启动 powershell.exe");

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            Log.Error("powershell run failed", exception);
            return (-1, string.Empty, exception.Message);
        }
    }
}
