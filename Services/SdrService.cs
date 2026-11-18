using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DeadlockServerPicker.Models;

namespace DeadlockServerPicker.Services;

public sealed class SdrResult
{
    public List<ServerInfo> Servers { get; init; } = new();
    public string Revision { get; init; } = string.Empty;
    public string? Error { get; init; }
}

public static class SdrService
{
    public const int DeadlockAppId = 1422450;

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DeadlockServerPicker/1.0");
        client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        return client;
    }

    public static async Task<SdrResult> FetchAsync(CancellationToken cancellationToken)
    {
        var url = "https://api.steampowered.com/ISteamApps/GetSDRConfig/v1/?appid=" + DeadlockAppId + "&_=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            using var client = CreateClient();
            var json = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var revision = root.TryGetProperty("revision", out var revisionElement) ? revisionElement.ToString() : string.Empty;

            var servers = new List<ServerInfo>();
            if (root.TryGetProperty("pops", out var pops) && pops.ValueKind == JsonValueKind.Object)
            {
                foreach (var pop in pops.EnumerateObject())
                {
                    if (!pop.Value.TryGetProperty("relays", out var relays) || relays.ValueKind != JsonValueKind.Array) continue;

                    var ips = new List<string>();
                    foreach (var relay in relays.EnumerateArray())
                    {
                        if (relay.TryGetProperty("ipv4", out var ip) && ip.ValueKind == JsonValueKind.String)
                        {
                            var value = ip.GetString();
                            if (!string.IsNullOrWhiteSpace(value)) ips.Add(value);
                        }
                    }

                    if (ips.Count == 0) continue;

                    var description = pop.Value.TryGetProperty("desc", out var desc) && desc.ValueKind == JsonValueKind.String
                        ? desc.GetString()!
                        : pop.Name;

                    var area = RegionMap.AreaFor(pop.Name);
                    servers.Add(new ServerInfo
                    {
                        PopKey = pop.Name,
                        Name = description,
                        RegionKey = RegionMap.BucketFor(pop.Name),
                        AreaZh = area.Zh,
                        AreaEn = area.En,
                        Relays = ips,
                        Flag = FlagService.GetFlag(description),
                    });
                }
            }

            servers.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return new SdrResult { Servers = servers, Revision = revision };
        }
        catch (Exception exception)
        {
            Log.Error("GetSDRConfig failed", exception);
            return new SdrResult { Error = exception.Message };
        }
    }
}
