using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using DeadlockServerPicker.Localization;

namespace DeadlockServerPicker.Models;

public partial class ServerInfo : ObservableObject
{
    public string PopKey { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string RegionKey { get; init; } = RegionMap.NorthAmerica;
    public string AreaZh { get; init; } = "未分类";
    public string AreaEn { get; init; } = "Other";
    public IReadOnlyList<string> Relays { get; init; } = new List<string>();
    public Bitmap? Flag { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PingText))]
    private string probeIp = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PingText), nameof(PingBrush), nameof(PingTooltip))]
    private int ping = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PingTooltip))]
    private int pingAvg = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(StateBrush), nameof(PingText), nameof(PingBrush), nameof(PingTooltip))]
    private bool isEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(StateBrush), nameof(PingText), nameof(PingBrush), nameof(PingTooltip))]
    private bool isBlocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PingText), nameof(PingTooltip))]
    private bool isEstimatedLatency;

    public string RegionName => L10n.T(AreaZh, AreaEn);

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    public string PingText
    {
        get
        {
            if (Ping < 0) return IsBlocked ? L10n.T("屏蔽", "Blocked") : "…";
            if (Ping > 900) return L10n.T("超时", "Timeout");
            return IsEstimatedLatency ? "≈" + Ping + " ms" : Ping + " ms";
        }
    }

    public string PingTooltip
    {
        get
        {
            if (Ping < 0)
            {
                return IsBlocked
                    ? L10n.T("该节点已被屏蔽，正在找同机房的相邻 IP 来测延迟", "Node is blocked; looking for a neighbour IP in the same datacenter to measure latency")
                    : L10n.T("正在测量中继节点", "Measuring relays");
            }

            if (Ping > 900) return L10n.T("所有中继都没有响应", "No relay responded");

            var relays = L10n.Format("最佳 {0} ms · 平均 {1} ms · {2} 个中继", "best {0} ms · avg {1} ms · {2} relays", Ping, PingAvg, Relays.Count);
            return IsEstimatedLatency
                ? relays + L10n.T("。该节点已被屏蔽，数字来自同机房相邻 IP " + ProbeIp + "，用作参考",
                    ". Node is blocked; measured through neighbour IP " + ProbeIp + " in the same datacenter, as a reference")
                : relays;
        }
    }

    public string StateText => IsEnabled
        ? L10n.T("放行", "Allowed")
        : IsBlocked ? L10n.T("已屏蔽", "Blocked") : L10n.T("待屏蔽", "Pending");

    public IBrush PingBrush
    {
        get
        {
            if (Ping < 0) return Brush.Parse("#7A8699");
            if (Ping > 900) return Brush.Parse("#6B7280");
            if (Ping <= 80) return Brush.Parse("#4ADE80");
            if (Ping <= 160) return Brush.Parse("#FACC15");
            if (Ping <= 260) return Brush.Parse("#FB923C");
            return Brush.Parse("#F87171");
        }
    }

    public IBrush StateBrush => !IsEnabled
        ? (IsBlocked ? Brush.Parse("#F87171") : Brush.Parse("#FACC15"))
        : Brush.Parse("#4ADE80");
}
