using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ActiveScanner.Models;

namespace ActiveScanner.Services
{
    /// <summary>
    /// Application settings that persist between sessions
    /// </summary>
    public class AppSettings
    {
        // Window state
        public double WindowLeft { get; set; } = 100;
        public double WindowTop { get; set; } = 100;
        public double WindowWidth { get; set; } = 1400;
        public double WindowHeight { get; set; } = 900;
        public bool IsMaximized { get; set; } = false;

        // Theme
        public bool DarkMode { get; set; } = true;

        // Network tool defaults
        public int DefaultTimeoutSeconds { get; set; } = 5;

        // Cache settings
        public int CacheTtlMinutes { get; set; } = 720;

        // Export settings
        public string LastExportFolder { get; set; } = string.Empty;
        public string LastExportFileName { get; set; } = string.Empty;
        public ExportFormat LastExportFormat { get; set; } = ExportFormat.Excel;
        public bool UseLastFileNameWithTimestamp { get; set; } = false;

        // Column settings
        public ColumnSettings ColumnSettings { get; set; } = new();

        // Saved custom paths
        public List<DomainConfig> SavedCustomPaths { get; set; } = new();

        // Saved target groups
        public List<TargetGroup> SavedTargetGroups { get; set; } = new();

        // Ticket creation settings
        public TicketBrowser TicketBrowser { get; set; } = TicketBrowser.Edge;

        // Vulnerability scan settings (assignment group, SNOW endpoints, excluded states)
        public VulnerabilityConfig Vulnerability { get; set; } = new();
    }

    /// <summary>
    /// Service to load and save application settings
    /// </summary>
    public class SettingsService
    {
        private readonly string _settingsPath;
        public AppSettings Current { get; private set; } = new();

        /// <summary>
        /// Gets the full path to the settings file
        /// </summary>
        public string SettingsFilePath => _settingsPath;

        public SettingsService()
        {
            var appDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ActiveScanner"
            );

            if (!Directory.Exists(appDataFolder))
            {
                Directory.CreateDirectory(appDataFolder);
            }

            _settingsPath = Path.Combine(appDataFolder, "settings.json");
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (settings != null)
                    {
                        Current = settings;
                    }
                }
                
                // Note: Groups are now handled by TargetGroupService (groups.json)
                // No default groups are created here - users can import or create their own
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
                Current = new AppSettings();
            }
        }

        // Groups are now managed separately via TargetGroupService and groups.json

        /// <summary>
        /// Deletes the settings file and reloads with fresh defaults.
        /// This is a safe way to reset all settings without restarting the application.
        /// </summary>
        /// <returns>True if reset was successful, false otherwise</returns>
        public bool ResetAllSettings()
        {
            try
            {
                // Delete the settings file if it exists
                if (File.Exists(_settingsPath))
                {
                    File.Delete(_settingsPath);
                }

                // Create fresh settings object
                Current = new AppSettings();

                // Save the fresh settings
                Save();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resetting settings: {ex.Message}");
                return false;
            }
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        public void AddCustomPath(string name, string dn)
        {
            if (Current.SavedCustomPaths.Any(p => p.DistinguishedName == dn))
                return;

            Current.SavedCustomPaths.Add(new DomainConfig(name, dn, false));
        }

        public void RemoveCustomPath(string dn)
        {
            Current.SavedCustomPaths.RemoveAll(p => p.DistinguishedName == dn);
        }
    }
}
