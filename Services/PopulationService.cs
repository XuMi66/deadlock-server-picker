using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DeadlockServerPicker.Services;

public sealed class RegionStat
{
    public int Matches { get; set; }
    public int Players { get; set; }
}

public sealed class PopulationSnapshot
{
    public Dictionary<string, RegionStat> Regions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int TotalMatches { get; set; }
    public int TotalPlayers { get; set; }
}

public static class PopulationService
{
    private const string ActiveMatchesUrl = "https://api.deadlock-api.com/v1/matches/active";
    private const string InfoUrl = "https://api.deadlock-api.com/v1/info";
    private const string RecentlyFetchedUrl = "https://api.deadlock-api.com/v1/matches/recently-fetched";

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        var client = new HttpClient(handler) { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DeadlockServerPicker/1.0");
        return client;
    }

    /// <summary>Matches the API can actually see right now. This is only a slice of the game.</summary>
    public static async Task<PopulationSnapshot?> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(TimeSpan.FromSeconds(25));
            var json = await client.GetStringAsync(ActiveMatchesUrl, cancellationToken).ConfigureAwait(false);

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;

            var snapshot = new PopulationSnapshot();
            foreach (var match in document.RootElement.EnumerateArray())
            {
                var region = match.TryGetProperty("region_mode_parsed", out var regionElement) && regionElement.ValueKind == JsonValueKind.String
                    ? regionElement.GetString()
                    : null;
                if (string.IsNullOrEmpty(region)) region = "row";

                var players = match.TryGetProperty("players", out var playersElement) && playersElement.ValueKind == JsonValueKind.Array
                    ? playersElement.GetArrayLength()
                    : 0;

                if (!snapshot.Regions.TryGetValue(region!, out var stat))
                {
                    stat = new RegionStat();
                    snapshot.Regions[region!] = stat;
                }

                stat.Matches++;
                stat.Players += players;
                snapshot.TotalMatches++;
                snapshot.TotalPlayers += players;
            }

            return snapshot;
        }
        catch (Exception exception)
        {
            Log.Error("Population fetch failed", exception);
            return null;
        }
    }

    /// <summary>How many matches the ingest sees per day; used to estimate the real concurrent player count.</summary>
    public static async Task<int> FetchDailyMatchesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(TimeSpan.FromSeconds(15));
            var json = await client.GetStringAsync(InfoUrl, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("fetched_matches_per_day", out var value) && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetInt32();
            }

            return 0;
        }
        catch (Exception exception)
        {
            Log.Error("Info fetch failed", exception);
            return 0;
        }
    }

    /// <summary>Average finished match length in minutes, taken from recently ingested matches.</summary>
    public static async Task<double> FetchAverageMatchMinutesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(TimeSpan.FromSeconds(25));
            var json = await client.GetStringAsync(RecentlyFetchedUrl, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return 0;

            double total = 0;
            var count = 0;
            foreach (var match in document.RootElement.EnumerateArray())
            {
                if (!match.TryGetProperty("duration_s", out var duration) || duration.ValueKind != JsonValueKind.Number) continue;
                var seconds = duration.GetDouble();
                if (seconds < 300) continue;
                total += seconds;
                count++;
            }

            return count == 0 ? 0 : total / count / 60.0;
        }
        catch (Exception exception)
        {
            Log.Error("Average duration fetch failed", exception);
            return 0;
        }
    }
}
