namespace PingTestTool
{
    public static class ResourceHelper
    {
        /// <summary>
        /// Finds a string resource by key. Returns the key in brackets if not found.
        /// </summary>
        /// <param name="resourceKey">The key of the resource to find.</param>
        /// <returns>The resource string or a placeholder if not found.</returns>
        public static string FindResourceString(string resourceKey) =>
            Application.Current?.FindResource(resourceKey) as string ?? $"[[{resourceKey}]]";

        /// <summary>
        /// Applies a resource dictionary (e.g., theme or language) to the application and optionally a window.
        /// </summary>
        /// <param name="resourcePath">The path to the resource dictionary (e.g., "Themes/DarkTheme.xaml").</param>
        /// <param name="baseDir">The base directory (e.g., "Themes" or "Resources").</param>
        /// <param name="window">The window to apply the dictionary to, if any.</param>
        public static void ApplyResourceDictionary(string resourcePath, string baseDir, Window? window = null)
        {
            try
            {
                var assemblyName = typeof(MainWindow).Assembly.GetName().Name;
                var uri = new Uri($"pack://application:,,,/{assemblyName};component/{resourcePath}", UriKind.Absolute);
                var newResourceDictionary = new ResourceDictionary { Source = uri };

                // Update application-level resources
                UpdateResourceDictionaries(Application.Current.Resources.MergedDictionaries, newResourceDictionary, baseDir);

                // Update window-level resources if a window is provided
                if (window != null)
                    UpdateResourceDictionaries(window.Resources.MergedDictionaries, newResourceDictionary, baseDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying resources: {ex.Message}", "Resource Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void UpdateResourceDictionaries(
            Collection<ResourceDictionary> dictionaries,
            ResourceDictionary newResourceDictionary,
            string baseDir)
        {
            for (int i = dictionaries.Count - 1; i >= 0; i--)
            {
                var source = dictionaries[i].Source?.ToString();
                if (source != null && source.Contains($"/{baseDir}/") &&
                    !(baseDir == "Themes" && source.EndsWith("CommonStyles.xaml", StringComparison.OrdinalIgnoreCase)))
                {
                    dictionaries.RemoveAt(i);
                }
            }
            dictionaries.Add(newResourceDictionary);
        }
    }
}