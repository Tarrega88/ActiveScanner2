using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ActiveScanner.Services;
using Microsoft.Win32;

namespace ActiveScanner.Views
{
    public partial class SettingsDialog : Window
    {
        public bool SettingsChanged { get; private set; }
        public bool GroupsImported { get; private set; }
        public bool SettingsReset { get; private set; }

        public SettingsDialog()
        {
            InitializeComponent();
            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            var settings = App.Settings.Current;

            ChkDarkMode.IsChecked = settings.DarkMode;
            TxtTimeout.Text = settings.DefaultTimeoutSeconds.ToString();
            TxtCacheTtl.Text = settings.CacheTtlMinutes.ToString();
        }

        private void ExportGroups_Click(object sender, RoutedEventArgs e)
        {
            if (App.TargetGroups.Groups.Count == 0)
            {
                MessageBox.Show(
                    "No groups to export. Create some groups first.",
                    "No Groups",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = "json",
                FileName = "ActiveScanner-Groups.json",
                Title = "Export Groups"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    App.TargetGroups.ExportToFile(saveDialog.FileName);
                    MessageBox.Show(
                        $"Exported {App.TargetGroups.Groups.Count} groups to:\n{saveDialog.FileName}",
                        "Export Successful",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to export groups:\n{ex.Message}",
                        "Export Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void ImportGroups_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = "json",
                Title = "Import Groups"
            };

            if (openDialog.ShowDialog() == true)
            {
                var result = MessageBox.Show(
                    "How would you like to import?\n\n" +
                    "• YES - Replace all existing groups with imported groups\n" +
                    "• NO - Merge (add new groups, skip duplicates by name)\n" +
                    "• CANCEL - Abort import",
                    "Import Mode",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;

                bool replaceExisting = result == MessageBoxResult.Yes;

                try
                {
                    int count = App.TargetGroups.ImportFromFile(openDialog.FileName, replaceExisting);
                    GroupsImported = true;

                    string mode = replaceExisting ? "Replaced with" : "Added";
                    MessageBox.Show(
                        $"{mode} {count} groups from:\n{openDialog.FileName}",
                        "Import Successful",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to import groups:\n{ex.Message}",
                        "Import Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will replace all your current groups with the built-in defaults.\n\nContinue?",
                "Restore Default Groups",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                App.TargetGroups.RestoreDefaults();
                GroupsImported = true;

                MessageBox.Show(
                    $"Restored {App.TargetGroups.Groups.Count} default groups.",
                    "Defaults Restored",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
        {
            var settingsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ActiveScanner");

            if (Directory.Exists(settingsFolder))
            {
                Process.Start("explorer.exe", settingsFolder);
            }
            else
            {
                MessageBox.Show("Settings folder does not exist yet.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ResetAllSettings_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will delete your settings file and reset ALL settings to defaults, including:\n\n" +
                "• Window position and size\n" +
                "• Theme preference\n" +
                "• All custom search groups\n" +
                "• Column settings\n\n" +
                "The application will reload with fresh default settings.\n\n" +
                "Are you sure you want to continue?",
                "Reset All Settings",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                if (App.Settings.ResetAllSettings())
                {
                    SettingsReset = true;
                    LoadCurrentSettings(); // Reload the dialog with fresh settings
                    
                    MessageBox.Show(
                        "All settings have been reset to defaults. The interface will now refresh.",
                        "Settings Reset",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show(
                        "Failed to reset settings. The settings file may be in use or protected.",
                        "Reset Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ExportDatabase_Click(object sender, RoutedEventArgs e)
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ActiveScanner",
                "computerCache.db");

            if (!File.Exists(dbPath))
            {
                MessageBox.Show(
                    "No database file exists yet. Run some Last User queries first to create data.",
                    "No Database",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "Database Files (*.db)|*.db|All Files (*.*)|*.*",
                DefaultExt = "db",
                FileName = "ActiveScanner-Cache.db",
                Title = "Export Cache Database"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    File.Copy(dbPath, saveDialog.FileName, overwrite: true);
                    MessageBox.Show(
                        $"Database exported to:\n{saveDialog.FileName}",
                        "Export Successful",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to export database:\n{ex.Message}",
                        "Export Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private async void ImportDatabase_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new OpenFileDialog
            {
                Filter = "Database Files (*.db)|*.db|All Files (*.*)|*.*",
                DefaultExt = "db",
                Title = "Import Cache Database"
            };

            if (openDialog.ShowDialog() == true)
            {
                var result = MessageBox.Show(
                    "How would you like to import?\n\n" +
                    "• YES - Replace your current database entirely\n" +
                    "• NO - Merge imported data with your existing data\n" +
                    "• CANCEL - Abort import\n\n" +
                    "Note: Replace mode will restart the application.",
                    "Import Mode",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;

                bool replaceExisting = result == MessageBoxResult.Yes;

                try
                {
                    // Show loading overlay
                    LoadingOverlay.Visibility = Visibility.Visible;
                    LoadingText.Text = replaceExisting ? "Replacing database..." : "Merging data...";
                    
                    var appDataPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "ActiveScanner");
                    Directory.CreateDirectory(appDataPath);
                    var dbPath = Path.Combine(appDataPath, "computerCache.db");

                    if (replaceExisting)
                    {
                        // Close the database connection first
                        App.ComputerCache.Dispose();
                        
                        // Small delay to ensure file handle is released
                        await Task.Delay(200);
                        
                        // Copy the new database file
                        File.Copy(openDialog.FileName, dbPath, overwrite: true);
                        
                        // Also handle the -journal and -wal files if they exist
                        var journalPath = dbPath + "-journal";
                        var walPath = dbPath + "-wal";
                        if (File.Exists(journalPath)) File.Delete(journalPath);
                        if (File.Exists(walPath)) File.Delete(walPath);
                        
                        LoadingText.Text = "Restarting application...";
                        await Task.Delay(500);
                        
                        // Restart the application
                        var exePath = Environment.ProcessPath;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            System.Diagnostics.Process.Start(exePath);
                        }
                        Application.Current.Shutdown();
                    }
                    else
                    {
                        // Merge - run on background thread
                        int imported = await Task.Run(() => App.ComputerCache.ImportAndMerge(openDialog.FileName));
                        
                        LoadingOverlay.Visibility = Visibility.Collapsed;
                        
                        MessageBox.Show(
                            $"Merged {imported} computer entries from the imported database.",
                            "Import Successful",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    
                    MessageBox.Show(
                        $"Failed to import database:\n{ex.Message}",
                        "Import Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void ClearMachineHistory_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will clear all cached 'Last Seen' computer history for users.\n\n" +
                "The data will rebuild when you run Get Last User scans.\n\n" +
                "Are you sure?",
                "Clear Machine History",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                using var userCache = new UserCacheService();
                int cleared = userCache.ClearAllMachineHistory();

                MessageBox.Show(
                    $"Cleared machine history for {cleared} user(s).",
                    "History Cleared",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to clear history:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ClearLastUserCache_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will clear all cached 'Last User' data for computers.\n\n" +
                "The data will rebuild when you run Get Last User scans.\n\n" +
                "Are you sure?",
                "Clear Last User Cache",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                int cleared = App.ComputerCache.ClearAllLastUserData();

                MessageBox.Show(
                    $"Cleared Last User data for {cleared} computer(s).",
                    "Cache Cleared",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to clear cache:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var settings = App.Settings.Current;

            // Check if dark mode changed
            bool darkModeChanged = settings.DarkMode != (ChkDarkMode.IsChecked ?? false);

            settings.DarkMode = ChkDarkMode.IsChecked ?? false;

            if (int.TryParse(TxtTimeout.Text, out int timeout) && timeout > 0)
            {
                settings.DefaultTimeoutSeconds = timeout;
            }

            if (int.TryParse(TxtCacheTtl.Text, out int cacheTtl) && cacheTtl >= 0)
            {
                settings.CacheTtlMinutes = cacheTtl;
            }

            App.Settings.Save();
            SettingsChanged = true;

            if (darkModeChanged)
            {
                // Apply theme change
                App.ApplyTheme(settings.DarkMode);
            }

            DialogResult = true;
            Close();
        }
    }
}
