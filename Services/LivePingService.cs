using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using DeadlockServerPicker.Models;

namespace DeadlockServerPicker.Services;

/// <summary>Rtt: >=0 measured, -1 no reply, -2 skipped (nothing to probe right now).</summary>
public sealed record PingResult(ServerInfo Server, int Rtt, bool Estimated);

public sealed class LivePingService
{
    public int TimeoutMs { get; set; } = 1200;
    public int MaxConcurrency { get; set; } = 16;

    private static readonly int[] NeighbourOctets =
    {
        2, 4, 6, 8, 10, 12, 14, 16, 18, 20,
        100, 102, 104, 106, 108, 110, 112, 114, 116, 118,
        130, 132, 134, 136, 138, 140, 142, 144, 148, 150,
        200, 202, 204, 206,
    };

    private readonly SemaphoreSlim _gate = new(16, 16);
    private readonly SemaphoreSlim _discoveryGate = new(10, 10);

    public async Task<PingResult[]> PingAllAsync(ServerInfo[] servers, int cycle, CancellationToken cancellationToken)
    {
        var tasks = servers.Select(server => PingServerAsync(server, cycle, cancellationToken)).ToArray();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<PingResult> PingServerAsync(ServerInfo server, int cycle, CancellationToken cancellationToken)
    {
        if (server.Relays.Count == 0) return new PingResult(server, -2, false);

        if (!server.IsBlocked)
        {
            var count = Math.Min(2, server.Relays.Count);
            var offset = Math.Abs(cycle) * count;
            var tasks = new Task<int>[count];
            for (var i = 0; i < count; i++)
            {
                var relay = server.Relays[(offset + i) % server.Relays.Count];
                tasks[i] = PingOneAsync(relay, cancellationToken);
            }

            var rtts = await Task.WhenAll(tasks).ConfigureAwait(false);
            var best = -1;
            foreach (var rtt in rtts)
            {
                if (rtt < 0) continue;
                if (best < 0 || rtt < best) best = rtt;
            }

            return new PingResult(server, best, false);
        }

        if (string.IsNullOrEmpty(server.ProbeIp)) return new PingResult(server, -2, true);
        var probe = await PingOneAsync(server.ProbeIp, cancellationToken).ConfigureAwait(false);
        return new PingResult(server, probe, true);
    }

    public async Task<string?> DiscoverProbeIpAsync(ServerInfo server, IReadOnlySet<string> knownRelayIps, CancellationToken cancellationToken)
    {
        await _discoveryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var candidate in BuildCandidates(server, knownRelayIps))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rtt = await PingOneAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (rtt >= 0) return candidate;
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _discoveryGate.Release();
        }
    }

    private static IEnumerable<string> BuildCandidates(ServerInfo server, IReadOnlySet<string> knownRelayIps)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relay in server.Relays)
        {
            var cut = relay.LastIndexOf('.');
            if (cut <= 0) continue;
            var prefix = relay.Substring(0, cut);
            foreach (var octet in NeighbourOctets)
            {
                var ip = prefix + "." + octet;
                if (knownRelayIps.Contains(ip)) continue;
                if (seen.Add(ip)) yield return ip;
            }
        }
    }

    private async Task<int> PingOneAsync(string ip, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, TimeoutMs).WaitAsync(cancellationToken).ConfigureAwait(false);
            return reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : -1;
        }
        catch
        {
            return -1;
        }
        finally
        {
            _gate.Release();
        }
    }
}
