using System.Windows;

namespace Fursoy.Browser.App.Services;

public static class ThemeManager
{
    public static void Apply(string theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains("Colors.xaml", StringComparison.OrdinalIgnoreCase) == true);
        var index = current == null ? 0 : dictionaries.IndexOf(current);
        var source = theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? new Uri("Themes/LightColors.xaml", UriKind.Relative)
            : new Uri("Themes/Colors.xaml", UriKind.Relative);

        var replacement = new ResourceDictionary { Source = source };
        if (current == null) dictionaries.Insert(index, replacement);
        else dictionaries[index] = replacement;
    }
}
