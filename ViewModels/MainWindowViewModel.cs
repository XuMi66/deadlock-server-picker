using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeadlockServerPicker.Localization;
using DeadlockServerPicker.Models;
using DeadlockServerPicker.Services;

namespace DeadlockServerPicker.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly FirewallService _firewallService = new();
    private readonly LivePingService _pingService = new();
    private readonly SettingsService _settingsService = new();
    private AppSettings _settings;
    private CancellationTokenSource? _livePingCancellation;
    private CancellationTokenSource? _populationCancellation;
    private bool _initializing = true;
    private bool _suppressEnabledAllSync;
    private bool _icmpWarningShown;
    private bool _discoveringProbes;
    private int _dailyMatches;
    private double _averageMatchMinutes = 35;
    private DateTime _dailyMatchesFetchedAt = DateTime.MinValue;
    private PopulationSnapshot? _lastSnapshot;
    private Dictionary<string, RegionStat>? _lastMerged;
    private string _revision = string.Empty;
    private readonly Dictionary<ServerInfo, PingTracker> _pingTrackers = new();

    [ObservableProperty]
    private ObservableCollection<ServerInfo> servers = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecuteCommands))]
    private bool isBusy;

    public UiText T { get; } = new();

    [ObservableProperty] private string subtitleText = L10n.T("正在读取 Steam 中继节点…", "Loading Steam relay pops…");
    [ObservableProperty] private string playerCountText = "—";
    [ObservableProperty] private string recommendRegion = "—";
    [ObservableProperty] private string recommendHint = L10n.T("正在统计活跃区域", "Measuring active regions");
    [ObservableProperty] private string regionSummaryText = string.Empty;
    [ObservableProperty] private string statusText = L10n.T("启动中…", "Starting…");
    [ObservableProperty] private string lastUpdatedText = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string errorText = string.Empty;
    [ObservableProperty] private bool livePingEnabled = true;
    [ObservableProperty] private bool isEnabledAll;
    [ObservableProperty] private bool isAdmin;
    [ObservableProperty] private bool livePingRunning;

    public bool CanExecuteCommands => !IsBusy;

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public MainWindowViewModel()
    {
        Log.Init(AppPaths.DataDirectory);
        Log.Info("Deadlock Server Picker start");
        _settings = _settingsService.Load();
        L10n.Set(L10n.ResolveDefault(AppStartup.Args, _settings.Language));
        L10n.Changed += OnLanguageChanged;
        LivePingEnabled = _settings.LivePingEnabled;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            IsAdmin = FirewallService.IsAdministrator();
            Log.Info("IsAdministrator = " + IsAdmin);

            await LoadServersAsync(true);

            if (!IsAdmin)
            {
                ErrorText = L10n.T(
                    "当前不是管理员权限：能看到现有屏蔽状态，但改不了防火墙。要应用改动，请右键 → 以管理员身份运行。",
                    "Not running as administrator: existing block state is visible but the firewall cannot be changed. Right-click → Run as administrator to apply changes.");
            }

            StartLivePing();
            StartPopulationLoop();
            _ = LoadGameWideStatsAsync();
            _ = DiscoverProbeIpsAsync();
            _initializing = false;
            UpdateStatus();
        }
        catch (Exception exception)
        {
            Log.Error("Initialize", exception);
            StatusText = L10n.T("初始化失败：", "Initialisation failed: ") + exception.Message;
        }
    }

    private async Task LoadServersAsync(bool firstRun)
    {
        IsBusy = true;
        try
        {
            var result = await SdrService.FetchAsync(CancellationToken.None);
            if (result.Servers.Count == 0)
            {
                StatusText = L10n.T("读取 Steam 节点失败：", "Failed to load Steam nodes: ") + (result.Error ?? L10n.T("返回为空", "empty response"));
                Log.Error("LoadServers empty " + result.Error);
                return;
            }

            _pingTrackers.Clear();
            var fresh = new ObservableCollection<ServerInfo>();
            foreach (var server in result.Servers)
            {
                fresh.Add(server);
                _pingTrackers[server] = new PingTracker();
            }

            Servers = fresh;
            _revision = result.Revision;
            SubtitleText = BuildSubtitle();

            await LoadFirewallStateAsync();

            if (firstRun && !string.IsNullOrEmpty(_settings.LastServerRevision) && _settings.LastServerRevision != result.Revision)
            {
                Log.Info("Server revision changed: " + _settings.LastServerRevision + " -> " + result.Revision + ", re-applying");
                await ApplyFirewallAsync();
            }
            else
            {
                RefreshEnabledAllIndicator();
            }

            _settings.LastServerRevision = result.Revision;
            _settingsService.Save(_settings);
            await PingOnceAsync();
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    private async Task LoadFirewallStateAsync()
    {
        if (Servers.Count == 0) return;
        try
        {
            var names = await _firewallService.GetBlockedNamesAsync(CancellationToken.None);
            foreach (var server in Servers)
            {
                var blocked = names.Contains(FirewallService.RulePrefix + server.Name);
                server.IsEnabled = !blocked;
                server.IsBlocked = blocked;
            }

            Log.Info("Firewall rules loaded: " + names.Count + " blocked rules");
            RefreshEnabledAllIndicator();
        }
        catch (Exception exception)
        {
            Log.Error("LoadFirewallState", exception);
            ErrorText = L10n.T("读取现有防火墙规则失败：", "Failed to read firewall rules: ") + exception.Message;
        }
    }

    private async Task PingOnceAsync()
    {
        var servers = Servers.ToArray();
        if (servers.Length == 0) return;
        var results = await _pingService.PingAllAsync(servers, 0, CancellationToken.None);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var result in results) ApplyPingResult(result);
            LastUpdatedText = "延迟 " + DateTime.Now.ToString("HH:mm:ss") + " 更新";
        });
    }

    private void ApplyPingResult(PingResult result)
    {
        var server = result.Server;
        if (result.Rtt == -2) return;
        if (!_pingTrackers.TryGetValue(server, out var tracker)) return;

        if (result.Rtt < 0)
        {
            tracker.Misses++;
            if (tracker.Misses >= 6)
            {
                tracker.Samples.Clear();
                server.Ping = int.MaxValue;
                server.PingAvg = int.MaxValue;
            }

            return;
        }

        tracker.Misses = 0;
        tracker.Samples.Add(result.Rtt);
        while (tracker.Samples.Count > 5) tracker.Samples.RemoveAt(0);
        server.IsEstimatedLatency = result.Estimated;
        server.Ping = tracker.Samples.Min();
        server.PingAvg = (int)Math.Round(tracker.Samples.Average());
    }

    private async Task DiscoverProbeIpsAsync()
    {
        if (_discoveringProbes) return;
        _discoveringProbes = true;
        try
        {
            var known = new HashSet<string>(Servers.SelectMany(server => server.Relays), StringComparer.OrdinalIgnoreCase);
            var pending = new List<ServerInfo>();

            foreach (var server in Servers)
            {
                if (!server.IsBlocked) continue;
                if (!string.IsNullOrEmpty(server.ProbeIp)) continue;
                if (_settings.ProbeIps.TryGetValue(server.PopKey, out var cached) && !string.IsNullOrEmpty(cached))
                {
                    server.ProbeIp = cached;
                    continue;
                }

                pending.Add(server);
            }

            if (pending.Count == 0) return;
            Log.Info("Discovering probe IPs for " + pending.Count + " blocked pops");

            var tasks = pending.Select(async server =>
            {
                var ip = await _pingService.DiscoverProbeIpAsync(server, known, CancellationToken.None);
                if (string.IsNullOrEmpty(ip)) return;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    server.ProbeIp = ip;
                    _settings.ProbeIps[server.PopKey] = ip;
                });
            });

            await Task.WhenAll(tasks);
            _settingsService.Save(_settings);
            Log.Info("Probe IP discovery finished");
        }
        catch (Exception exception)
        {
            Log.Error("DiscoverProbeIps", exception);
        }
        finally
        {
            _discoveringProbes = false;
        }
    }

    private void StartLivePing()
    {
        _livePingCancellation?.Cancel();
        _livePingCancellation?.Dispose();
        _livePingCancellation = null;

        if (!LivePingEnabled)
        {
            LivePingRunning = false;
            UpdateStatus();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _livePingCancellation = cancellation;
        LivePingRunning = true;

        _ = Task.Run(async () =>
        {
            var cycle = 0;
            while (!cancellation.IsCancellationRequested)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var servers = await Dispatcher.UIThread.InvokeAsync(() => Servers.ToArray());
                    if (servers.Length > 0)
                    {
                        var results = await _pingService.PingAllAsync(servers, cycle++, cancellation.Token);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var probed = 0;
                            var succeeded = 0;
                            foreach (var result in results)
                            {
                                if (result.Rtt != -2) probed++;
                                if (result.Rtt >= 0) succeeded++;
                                ApplyPingResult(result);
                            }

                            LastUpdatedText = "延迟 " + DateTime.Now.ToString("HH:mm:ss") + " 更新";

                            if (!_icmpWarningShown && cycle >= 3 && probed >= 8 && succeeded == 0)
                            {
                                _icmpWarningShown = true;
                                StatusText = L10n.T(
                                    "延迟探测全部失败：系统防火墙或安全软件可能拦截了 ICMP，延迟数字会一直显示超时。",
                                    "Every ping probe failed: the firewall or security software is probably blocking ICMP, so latency will always show as timeout.");
                                Log.Error("All ICMP probes failed after 3 cycles");
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    Log.Error("Live ping loop", exception);
                }

                var delay = TimeSpan.FromSeconds(Math.Max(1.0, _settings.LivePingIntervalSeconds)) - stopwatch.Elapsed;
                if (delay > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(delay, cancellation.Token);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }, cancellation.Token);
    }

    private void StartPopulationLoop()
    {
        _populationCancellation?.Cancel();
        _populationCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _populationCancellation = cancellation;

        _ = Task.Run(async () =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    var snapshot = await PopulationService.FetchAsync(cancellation.Token);
                    if (snapshot is not null)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => ApplyPopulation(snapshot));
                    }

                    if (Servers.Any(server => server.IsBlocked && string.IsNullOrEmpty(server.ProbeIp)))
                    {
                        _ = DiscoverProbeIpsAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    Log.Error("Population loop", exception);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(15.0, _settings.PopulationIntervalSeconds)), cancellation.Token);
                }
                catch
                {
                    break;
                }
            }
        }, cancellation.Token);
    }

    private async Task LoadGameWideStatsAsync()
    {
        try
        {
            var minutesTask = PopulationService.FetchAverageMatchMinutesAsync(CancellationToken.None);
            var daily = await PopulationService.FetchDailyMatchesAsync(CancellationToken.None);
            var minutes = await minutesTask;

            if (daily > 0) _dailyMatches = daily;
            if (minutes > 5) _averageMatchMinutes = minutes;
            _dailyMatchesFetchedAt = DateTime.UtcNow;
            Log.Info("Game wide stats: dailyMatches=" + _dailyMatches + ", avgMatchMinutes=" + _averageMatchMinutes.ToString("0.0"));

            var snapshot = _lastSnapshot;
            if (snapshot is not null && daily > 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() => ApplyPopulation(snapshot));
            }
        }
        catch (Exception exception)
        {
            Log.Error("LoadGameWideStats", exception);
        }
    }

    private void ApplyPopulation(PopulationSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        var merged = new Dictionary<string, RegionStat>(StringComparer.OrdinalIgnoreCase);
        _lastMerged = merged;
        foreach (var pair in snapshot.Regions)
        {
            var key = RegionMap.FromApiRegion(pair.Key);
            if (!merged.TryGetValue(key, out var stat))
            {
                stat = new RegionStat();
                merged[key] = stat;
            }

            stat.Matches += pair.Value.Matches;
            stat.Players += pair.Value.Players;
        }


        var playersPerMatch = snapshot.TotalMatches > 0
            ? (double)snapshot.TotalPlayers / snapshot.TotalMatches
            : 11.5;
        if (_dailyMatches > 0 && _averageMatchMinutes > 1)
        {
            var concurrentMatches = _dailyMatches * _averageMatchMinutes / (24 * 60);
            var estimated = (int)Math.Round(concurrentMatches * playersPerMatch);
            if (estimated < snapshot.TotalPlayers) estimated = snapshot.TotalPlayers;
            PlayerCountText = estimated >= 10000
                ? (L10n.IsEnglish ? (estimated / 1000.0).ToString("0") + "K" : (estimated / 10000.0).ToString("0.0") + " 万")
                : estimated.ToString("N0");
        }
        else
        {
            PlayerCountText = snapshot.TotalPlayers.ToString("N0");
        }

        UpdateRecommendation(merged);
    }

    private void UpdateRecommendation(Dictionary<string, RegionStat> merged)
    {
        var leader = merged
            .Where(pair => pair.Value.Matches > 0)
            .OrderByDescending(pair => pair.Value.Matches)
            .Select(pair => pair.Key)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(leader))
        {
            RecommendRegion = "—";
            RecommendHint = L10n.T("暂无活跃数据", "No activity data");
            return;
        }

        RecommendRegion = RegionMap.DisplayName(leader);
        var popsInLeader = Servers.Where(server => server.RegionKey == leader).ToList();
        var blockedOff = popsInLeader.Count > 0 && popsInLeader.All(server => server.IsBlocked);
        RecommendHint = blockedOff
            ? L10n.T("人最多，但已被你屏蔽", "Most players, but blocked by you")
            : L10n.T("人最多，匹配最快", "Most players — fastest queue");
    }

    private async Task ApplyFirewallAsync()
    {
        if (!IsAdmin)
        {
            ErrorText = L10n.T("需要管理员权限才能应用防火墙规则。", "Administrator rights are required to apply firewall rules.");
            return;
        }

        var blockedServers = Servers.Where(server => !server.IsEnabled).ToList();
        var result = await _firewallService.ApplyAsync(blockedServers, CancellationToken.None);

        foreach (var server in Servers) server.IsBlocked = !server.IsEnabled;
        _ = DiscoverProbeIpsAsync();

        if (!result.Ok)
        {
            ErrorText = L10n.T("部分规则创建失败：", "Some rules could not be created: ") + result.Error;
            Log.Error("Apply failed: " + result.Error);
        }
        else
        {
            ErrorText = string.Empty;
        }

        StatusText = L10n.Format("已应用：{0} 个节点屏蔽，{1} 个节点放行", "Applied: {0} blocked, {1} allowed",
            blockedServers.Count, Servers.Count - blockedServers.Count);
        Log.Info("Apply done, blocked=" + blockedServers.Count + ", failed=" + result.Failed.Count);
    }

    [RelayCommand]
    private async Task Apply()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await ApplyFirewallAsync();
            await PingOnceAsync();
        }
        catch (Exception exception)
        {
            Log.Error("Apply command", exception);
            ErrorText = L10n.T("应用失败：", "Apply failed: ") + exception.Message;
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    [RelayCommand]
    private async Task ResetAll()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (IsAdmin)
            {
                var ok = await _firewallService.ResetAsync(CancellationToken.None);
                if (!ok) ErrorText = L10n.T("删除防火墙规则失败，请检查权限。", "Could not remove firewall rules — check permissions.");
                else ErrorText = string.Empty;
            }

            foreach (var server in Servers)
            {
                server.IsBlocked = false;
                server.IsEnabled = true;
            }

            RefreshEnabledAllIndicator();
            StatusText = L10n.T("已清空所有屏蔽规则，全部节点放行", "All block rules removed; every node is allowed");
            Log.Info("ResetAll done");
            await PingOnceAsync();
        }
        catch (Exception exception)
        {
            Log.Error("ResetAll command", exception);
            ErrorText = L10n.T("重置失败：", "Reset failed: ") + exception.Message;
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    [RelayCommand]
    private async Task Rescan()
    {
        if (IsBusy) return;
        ErrorText = string.Empty;
        await LoadServersAsync(false);
    }

    [RelayCommand]
    private void OpenInfo()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/mschessler/cs2-server-picker-plus") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Log.Error("OpenInfo", exception);
        }
    }

    partial void OnLivePingEnabledChanged(bool value)
    {
        _settings.LivePingEnabled = value;
        _settingsService.Save(_settings);
        if (Servers.Count > 0) StartLivePing();
        UpdateStatus();
    }

    private void RefreshEnabledAllIndicator()
    {
        _suppressEnabledAllSync = true;
        IsEnabledAll = Servers.Count > 0 && Servers.All(server => server.IsEnabled);
        _suppressEnabledAllSync = false;
    }

    partial void OnIsEnabledAllChanged(bool value)
    {
        if (_suppressEnabledAllSync || Servers.Count == 0) return;
        foreach (var server in Servers) server.IsEnabled = value;
        if (!IsBusy && !_initializing) StatusText = L10n.T("勾选已改动，点「应用防火墙规则」后生效。", "Selection changed — press Apply rules to take effect.");
    }

    private string BuildSubtitle()
        => L10n.Format("Steam 中继节点 {0} 个 · 服务器版本 {1}", "Steam relay pops: {0} · server revision {1}", Servers.Count, _revision);

    private void OnLanguageChanged()
    {
        T.Refresh();
        foreach (var server in Servers) server.RefreshLocalization();

        _settings.Language = L10n.IsEnglish ? "en" : "zh";
        _settingsService.Save(_settings);

        if (!string.IsNullOrEmpty(_revision)) SubtitleText = BuildSubtitle();
        if (_lastMerged is not null) UpdateRecommendation(_lastMerged);
        UpdateStatus();
    }

    [RelayCommand]
    private void ToggleLanguage()
        => L10n.Set(L10n.IsEnglish ? AppLanguage.Chinese : AppLanguage.English);

    private void UpdateStatus()
    {
        var total = Servers.Count;
        var blocked = Servers.Count(server => !server.IsEnabled);
        var applied = Servers.Count(server => server.IsBlocked);
        var text = L10n.Format("节点 {0} 个 · 放行 {1} 个 · 屏蔽 {2} 个", "{0} nodes · {1} allowed · {2} blocked",
            total, total - blocked, applied);
        if (blocked != applied) text += L10n.Format("（{0} 个待应用）", " ({0} pending)", blocked - applied);
        text += IsAdmin ? L10n.T(" · 管理员权限", " · admin") : L10n.T(" · 非管理员运行", " · not elevated");
        if (!LivePingRunning) text += L10n.T(" · 实时延迟已暂停", " · live ping paused");
        StatusText = text;
    }

    public void Shutdown()
    {
        _livePingCancellation?.Cancel();
        _populationCancellation?.Cancel();
        Log.Info("Shutdown");
    }

    private sealed class PingTracker
    {
        public List<int> Samples { get; } = new();
        public int Misses { get; set; }
    }
}
