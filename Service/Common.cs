namespace PingTestTool.Service;

public static class ResourceHelper
{
    public static string FindResourceString(string key) =>
        Application.Current?.FindResource(key) as string ?? $"[[{key}]]";

    public static void ApplyResourceDictionary(string path, string baseDir, Window? window = null)
    {
        try
        {
            string? asm = typeof(MainWindow).Assembly.GetName().Name;
            Uri uri = new($"pack://application:,,,/{asm};component/{path}", UriKind.Absolute);
            ResourceDictionary dict = new() { Source = uri };

            UpdateDicts(Application.Current.Resources.MergedDictionaries, dict, baseDir);
            if (window != null)
                UpdateDicts(window.Resources.MergedDictionaries, dict, baseDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Resource error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    static void UpdateDicts(Collection<ResourceDictionary> dicts, ResourceDictionary newDict, string baseDir)
    {
        for (int i = dicts.Count - 1; i >= 0; i--)
        {
            string? src = dicts[i].Source?.ToString();
            if (src != null && src.Contains($"/{baseDir}/") &&
                !(baseDir == "Themes" && src.EndsWith("CommonStyles.xaml", StringComparison.OrdinalIgnoreCase)))
                dicts.RemoveAt(i);
        }
        dicts.Add(newDict);
    }
}