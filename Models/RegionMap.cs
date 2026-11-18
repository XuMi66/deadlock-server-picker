using System;
using System.Collections.Generic;
using DeadlockServerPicker.Localization;

namespace DeadlockServerPicker.Models;

public static class RegionMap
{
    public const string Asia = "as";
    public const string NorthAmerica = "na";
    public const string Europe = "eu";
    public const string SouthAmerica = "sa";

    public static readonly string[] DisplayOrder = { Asia, NorthAmerica, Europe, SouthAmerica };

    /// <summary>pop key → (activity bucket, area name in Chinese, area name in English).</summary>
    private static readonly Dictionary<string, (string Bucket, string Zh, string En)> Pops = new()
    {
        ["ams"] = (Europe, "西欧", "W. Europe"),
        ["fra"] = (Europe, "中欧", "C. Europe"),
        ["fsn"] = (Europe, "中欧", "C. Europe"),
        ["dfra"] = (Europe, "中欧", "C. Europe"),
        ["hel"] = (Europe, "北欧", "N. Europe"),
        ["lhr"] = (Europe, "西欧", "W. Europe"),
        ["dlhr"] = (Europe, "西欧", "W. Europe"),
        ["mad"] = (Europe, "南欧", "S. Europe"),
        ["par"] = (Europe, "西欧", "W. Europe"),
        ["sto"] = (Europe, "北欧", "N. Europe"),
        ["sto2"] = (Europe, "北欧", "N. Europe"),
        ["dsto"] = (Europe, "北欧", "N. Europe"),
        ["vie"] = (Europe, "中欧", "C. Europe"),
        ["dvie"] = (Europe, "中欧", "C. Europe"),
        ["waw"] = (Europe, "东欧", "E. Europe"),
        ["jnb"] = (Europe, "南非", "South Africa"),
        ["atl"] = (NorthAmerica, "北美东部", "US East"),
        ["iad"] = (NorthAmerica, "北美东部", "US East"),
        ["ord"] = (NorthAmerica, "北美中部", "US Central"),
        ["dfw"] = (NorthAmerica, "北美南部", "US South"),
        ["lax"] = (NorthAmerica, "北美西部", "US West"),
        ["sea"] = (NorthAmerica, "北美西北", "US Northwest"),
        ["eat"] = (NorthAmerica, "北美西北", "US Northwest"),
        ["seo"] = (Asia, "东亚", "East Asia"),
        ["tyo"] = (Asia, "东亚", "East Asia"),
        ["hkg"] = (Asia, "东亚", "East Asia"),
        ["sgp"] = (Asia, "东南亚", "SE Asia"),
        ["gum"] = (Asia, "西太平洋", "W. Pacific"),
        ["bom2"] = (Asia, "南亚", "South Asia"),
        ["maa2"] = (Asia, "南亚", "South Asia"),
        ["syd"] = (Asia, "大洋洲", "Oceania"),
        ["dxb"] = (Asia, "中东", "Middle East"),
        ["dfjr"] = (Asia, "中东", "Middle East"),
        ["gru"] = (SouthAmerica, "南美东部", "S. America E."),
        ["eze"] = (SouthAmerica, "南美南部", "S. America S."),
        ["scl"] = (SouthAmerica, "南美南部", "S. America S."),
        ["lim"] = (SouthAmerica, "南美西部", "S. America W."),
    };

    public static string BucketFor(string popKey) => Pops.TryGetValue(popKey, out var value) ? value.Bucket : NorthAmerica;

    public static (string Zh, string En) AreaFor(string popKey)
        => Pops.TryGetValue(popKey, out var value) ? (value.Zh, value.En) : ("未分类", "Other");

    public static string DisplayName(string bucket) => bucket switch
    {
        Asia => L10n.T("亚服", "Asia"),
        NorthAmerica => L10n.T("北美服", "N. America"),
        Europe => L10n.T("欧服", "Europe"),
        SouthAmerica => L10n.T("南美服", "S. America"),
        _ => L10n.T("其他", "Other"),
    };

    public static string FromApiRegion(string? apiRegion)
    {
        return (apiRegion ?? string.Empty).ToLowerInvariant() switch
        {
            "europe" => Europe,
            "russia" => Europe,
            "se_asia" => Asia,
            "s_america" => SouthAmerica,
            _ => NorthAmerica,
        };
    }
}
