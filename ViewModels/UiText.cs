using CommunityToolkit.Mvvm.ComponentModel;
using DeadlockServerPicker.Localization;

namespace DeadlockServerPicker.ViewModels;

/// <summary>Every static label in the window, so a language switch refreshes in place.</summary>
public sealed class UiText : ObservableObject
{
    public void Refresh() => OnPropertyChanged(string.Empty);

    public string AppTitle => L10n.T("Deadlock 服务器选取器", "Deadlock Server Picker");
    public string LanguageButton => L10n.T("EN", "中文");

    public string BtnApply => L10n.T("应用防火墙规则", "Apply rules");
    public string BtnReset => L10n.T("全部放行", "Allow all");
    public string BtnRescan => L10n.T("重新获取节点", "Reload nodes");
    public string LivePing => L10n.T("实时延迟", "Live ping");

    public string CardRecommend => L10n.T("匹配推荐", "Matchmaking");
    public string CardOnline => L10n.T("当前在线玩家（估算）", "Players online (est.)");

    public string RecommendTip => L10n.T(
        "按 deadlock-api 各区域当前活跃对局占比判断：对局越多的区，排队通常越快。只做相对比较",
        "Based on the share of live matches per region reported by deadlock-api. More matches usually means a faster queue. Relative comparison only.");

    public string OnlineTip => L10n.T(
        "按 deadlock-api 最近 24 小时的对局量 × 平均对局时长 ÷ 一天，再乘每局人数推算，是估算值",
        "Estimated from deadlock-api: matches in the last 24 h × average match length ÷ 24 h × players per match.");

    public string ColNode => L10n.T("中继节点", "Relay node");
    public string ColRegion => L10n.T("区域", "Region");
    public string ColPing => L10n.T("延迟", "Ping");
    public string ColState => L10n.T("状态", "State");
}
