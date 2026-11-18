using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DeadlockServerPicker.Services;

public static class FlagService
{
    private static readonly (string Hint, string File)[] Map =
    {
        ("Argentina", "Flag_Argentina.png"), ("Australia", "Flag_Australia.png"), ("Austria", "Flag_Austria.png"),
        ("Brazil", "Flag_Brazil.png"), ("Chile", "Flag_Chile.png"), ("China", "Flag_China.png"),
        ("Hong Kong", "Flag_HongKong.png"), ("England", "Flag_England.png"), ("France", "Flag_France.png"),
        ("Germany", "Flag_Germany.png"), ("Finland", "Flag_Finland.png"), ("India", "Flag_India.png"),
        ("Japan", "Flag_Japan.png"), ("Netherlands", "Flag_Netherlands.png"), ("Peru", "Flag_Peru.png"),
        ("Poland", "Flag_Poland.png"), ("Singapore", "Flag_Singapore.png"), ("South Africa", "Flag_SouthAfrica.png"),
        ("South Korea", "Flag_SouthKorea.png"), ("Spain", "Flag_Spain.png"), ("Sweden", "Flag_Sweden.png"),
        ("United Arab Emirates", "Flag_UAE.png"),
    };

    public static Bitmap? GetFlag(string description)
    {
        foreach (var (hint, file) in Map)
        {
            if (description.Contains(hint, StringComparison.OrdinalIgnoreCase)) return Load(file);
        }

        return Load("Flag_USA.png");
    }

    private static Bitmap Load(string file)
        => new(AssetLoader.Open(new Uri("avares://DeadlockServerPicker/Assets/" + file)));
}
