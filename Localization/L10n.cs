using System;
using System.Reflection;

namespace DeadlockServerPicker.Localization;

public enum AppLanguage
{
    Chinese,
    English,
}

public static class L10n
{
    public static AppLanguage Current { get; private set; } = AppLanguage.Chinese;

    public static event Action? Changed;

    public static bool IsEnglish => Current == AppLanguage.English;

    public static string T(string chinese, string english) => IsEnglish ? english : chinese;

    public static string Format(string chineseFormat, string englishFormat, params object?[] args)
        => string.Format(IsEnglish ? englishFormat : chineseFormat, args);

    public static void Set(AppLanguage language)
    {
        if (Current == language) return;
        Current = language;
        Changed?.Invoke();
    }

    /// <summary>Default language: --lang argument &gt; settings &gt; build-time default &gt; OS UI culture.</summary>
    public static AppLanguage ResolveDefault(string?[] args, string? stored)
    {
        if (args is not null)
        {
            foreach (var arg in args)
            {
                if (string.IsNullOrWhiteSpace(arg)) continue;
                if (arg.Equals("--lang=en", StringComparison.OrdinalIgnoreCase) || arg.Equals("-lang=en", StringComparison.OrdinalIgnoreCase))
                    return AppLanguage.English;
                if (arg.Equals("--lang=zh", StringComparison.OrdinalIgnoreCase) || arg.Equals("-lang=zh", StringComparison.OrdinalIgnoreCase))
                    return AppLanguage.Chinese;
            }
        }

        if (!string.IsNullOrWhiteSpace(stored))
            return stored.Equals("en", StringComparison.OrdinalIgnoreCase) ? AppLanguage.English : AppLanguage.Chinese;

        var buildDefault = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefaultAttribute("DefaultLanguage");
        if (!string.IsNullOrWhiteSpace(buildDefault))
            return buildDefault!.Equals("en", StringComparison.OrdinalIgnoreCase) ? AppLanguage.English : AppLanguage.Chinese;

        return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Chinese
            : AppLanguage.English;
    }

    private static string? FirstOrDefaultAttribute(this System.Collections.Generic.IEnumerable<AssemblyMetadataAttribute> attributes, string key)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.Key == key) return attribute.Value;
        }

        return null;
    }
}
