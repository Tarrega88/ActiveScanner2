using System;
using System.Windows;
using MaterialDesignThemes.Wpf;
using ActiveScanner.Services;

namespace ActiveScanner
{
    public partial class App : Application
    {
        public static SettingsService Settings { get; private set; } = null!;
        public static TargetGroupService TargetGroups { get; private set; } = null!;
        public static ComputerCacheService ComputerCache { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Add global exception handler
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                System.IO.File.WriteAllText("crash.log", $"Unhandled: {ex?.ToString()}");
            };
            DispatcherUnhandledException += (s, args) =>
            {
                System.IO.File.WriteAllText("crash.log", $"Dispatcher: {args.Exception}");
                args.Handled = true;
            };

            base.OnStartup(e);

            // Initialize settings
            Settings = new SettingsService();
            Settings.Load();

            // Initialize target groups service
            TargetGroups = new TargetGroupService();

            // Initialize computer cache service (used by Settings dialog for import/export)
            ComputerCache = new ComputerCacheService();

            // Migrate groups from old settings.json location if needed
            MigrateGroupsFromSettings();

            // One-time SNOW ID import (auto-runs if JSON file exists, then you can delete it)
            ImportSnowIdsIfAvailable();

            // Apply theme
            ApplyTheme(Settings.Current.DarkMode);
        }

        /// <summary>
        /// One-time migration: Move groups from settings.json (AppSettings.SavedTargetGroups) to groups.json
        /// </summary>
        private static void MigrateGroupsFromSettings()
        {
            // If groups.json already has groups, don't migrate
            if (TargetGroups.Groups.Count > 0)
                return;

            // If old settings has groups, migrate them
            if (Settings.Current.SavedTargetGroups?.Count > 0)
            {
                foreach (var group in Settings.Current.SavedTargetGroups)
                {
                    TargetGroups.AddGroup(group);
                }

                // Clear the old location and save settings
                Settings.Current.SavedTargetGroups.Clear();
                Settings.Save();

                System.Diagnostics.Debug.WriteLine($"Migrated {TargetGroups.Groups.Count} groups to groups.json");
            }
        }

        /// <summary>
        /// One-time import: Import SNOW sys_ids from Resources/snow_users.json if it exists.
        /// After import, you can safely delete the JSON file.
        /// </summary>
        private static void ImportSnowIdsIfAvailable()
        {
            // Check multiple possible locations
            var possiblePaths = new[]
            {
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "snow_users.json"),
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "snow_users.json"),
                System.IO.Path.Combine(Environment.CurrentDirectory, "Resources", "snow_users.json"),
                System.IO.Path.Combine(Environment.CurrentDirectory, "snow_users.json")
            };

            string? jsonPath = null;
            foreach (var path in possiblePaths)
            {
                if (System.IO.File.Exists(path))
                {
                    jsonPath = path;
                    break;
                }
            }

            if (jsonPath == null)
                return;

            try
            {
                using var userCache = new UserCacheService();
                var (updated, created) = userCache.ImportSnowIds(jsonPath);
                
                if (updated > 0 || created > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"SNOW ID Import: Updated {updated}, Created {created} user entries from {jsonPath}");
                    
                    // Show a message so user knows it worked
                    MessageBox.Show(
                        $"Imported SNOW IDs:\n• Updated: {updated} existing users\n• Created: {created} new entries\n\nYou can now delete {System.IO.Path.GetFileName(jsonPath)}",
                        "SNOW ID Import Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SNOW ID Import failed: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Dispose cache service to release file lock
            ComputerCache?.Dispose();
            
            // Save settings on exit
            Settings.Save();
            base.OnExit(e);
        }

        public static void ApplyTheme(bool isDarkMode)
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(isDarkMode 
                ? BaseTheme.Dark 
                : BaseTheme.Light);
            paletteHelper.SetTheme(theme);
        }
    }
}
