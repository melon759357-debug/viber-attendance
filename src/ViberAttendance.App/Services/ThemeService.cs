using System.Windows;
using Microsoft.Win32;

namespace ViberAttendance.App.Services;

public static class ThemeService
{
    public static void Apply(string theme)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        var resolvedTheme = ResolveTheme(theme);
        var dictionaries = resources.MergedDictionaries;
        var themeIndex = -1;
        for (var index = 0; index < dictionaries.Count; index++)
        {
            var source = dictionaries[index].Source?.OriginalString;
            if (source?.EndsWith("Theme.xaml", StringComparison.OrdinalIgnoreCase) == true)
            {
                themeIndex = index;
                break;
            }
        }

        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"Styles/{resolvedTheme}Theme.xaml", UriKind.Relative)
        };
        if (themeIndex >= 0)
        {
            dictionaries[themeIndex] = dictionary;
        }
        else
        {
            dictionaries.Insert(0, dictionary);
        }
    }

    private static string ResolveTheme(string theme)
    {
        if (theme.Equals("Dark", StringComparison.OrdinalIgnoreCase))
        {
            return "Dark";
        }

        if (!theme.Equals("System", StringComparison.OrdinalIgnoreCase))
        {
            return "Light";
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0
                ? "Dark"
                : "Light";
        }
        catch
        {
            return "Light";
        }
    }
}
