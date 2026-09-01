using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ActiveScanner.Models;

namespace ActiveScanner.Services
{
    /// <summary>
    /// Service for managing target groups (domain path groups for multi-path searching).
    /// Groups are stored in a separate groups.json file.
    /// </summary>
    public class TargetGroupService
    {
        private readonly string _groupsPath;
        private List<TargetGroup> _groups = new();

        /// <summary>
        /// Gets all saved target groups
        /// </summary>
        public List<TargetGroup> Groups => _groups;

        public TargetGroupService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ActiveScanner");

            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            _groupsPath = Path.Combine(appDataPath, "groups.json");
            
            // If groups.json doesn't exist, copy from embedded default resource
            if (!File.Exists(_groupsPath))
            {
                CopyDefaultGroups();
            }
            
            Load();
        }

        /// <summary>
        /// Copies the embedded default-groups.json resource to the user's AppData folder
        /// </summary>
        private void CopyDefaultGroups()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "ActiveScanner.Resources.default-groups.json";
                
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var defaultJson = reader.ReadToEnd();
                    File.WriteAllText(_groupsPath, defaultJson);
                    System.Diagnostics.Debug.WriteLine("Copied default groups to AppData");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Default groups resource not found: {resourceName}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error copying default groups: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores groups from the embedded default resource (overwrites current groups)
        /// </summary>
        public void RestoreDefaults()
        {
            CopyDefaultGroups();
            Load();
        }

        /// <summary>
        /// Loads groups from disk
        /// </summary>
        public void Load()
        {
            try
            {
                if (File.Exists(_groupsPath))
                {
                    var json = File.ReadAllText(_groupsPath);
                    var options = new JsonSerializerOptions();
                    options.Converters.Add(new JsonStringEnumConverter());
                    _groups = JsonSerializer.Deserialize<List<TargetGroup>>(json, options) ?? new();
                }
                else
                {
                    _groups = new List<TargetGroup>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading groups: {ex.Message}");
                _groups = new List<TargetGroup>();
            }
        }

        /// <summary>
        /// Saves groups to disk
        /// </summary>
        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                options.Converters.Add(new JsonStringEnumConverter());
                var json = JsonSerializer.Serialize(_groups, options);
                File.WriteAllText(_groupsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving groups: {ex.Message}");
            }
        }

        /// <summary>
        /// Adds a new group
        /// </summary>
        public void AddGroup(TargetGroup group)
        {
            _groups.Add(group);
            Save();
        }

        /// <summary>
        /// Updates an existing group
        /// </summary>
        public void UpdateGroup(TargetGroup group)
        {
            var index = _groups.FindIndex(g => g.Id == group.Id);
            if (index >= 0)
            {
                group.ModifiedAt = DateTime.Now;
                _groups[index] = group;
                Save();
            }
        }

        /// <summary>
        /// Deletes a group by ID
        /// </summary>
        public void DeleteGroup(string id)
        {
            _groups.RemoveAll(g => g.Id == id);
            Save();
        }

        /// <summary>
        /// Gets a group by ID
        /// </summary>
        public TargetGroup? GetGroup(string id)
        {
            return _groups.FirstOrDefault(g => g.Id == id);
        }

        /// <summary>
        /// Gets a group by name
        /// </summary>
        public TargetGroup? GetGroupByName(string name)
        {
            return _groups.FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Exports all groups to a JSON string
        /// </summary>
        public string ExportToJson()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(_groups, options);
        }

        /// <summary>
        /// Exports all groups to a file
        /// </summary>
        public void ExportToFile(string filePath)
        {
            var json = ExportToJson();
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Imports groups from a JSON string. Returns count of imported groups.
        /// </summary>
        /// <param name="json">JSON string containing groups</param>
        /// <param name="replaceExisting">If true, replaces all existing groups. If false, merges (adds new, skips duplicates by name).</param>
        public int ImportFromJson(string json, bool replaceExisting = false)
        {
            var importedGroups = JsonSerializer.Deserialize<List<TargetGroup>>(json);
            if (importedGroups == null || importedGroups.Count == 0)
                return 0;

            if (replaceExisting)
            {
                _groups = importedGroups;
                // Regenerate IDs to avoid conflicts
                foreach (var group in _groups)
                {
                    group.Id = Guid.NewGuid().ToString();
                }
                Save();
                return importedGroups.Count;
            }
            else
            {
                int addedCount = 0;
                foreach (var group in importedGroups)
                {
                    // Skip if a group with this name already exists
                    if (!_groups.Any(g => g.Name.Equals(group.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        group.Id = Guid.NewGuid().ToString(); // New ID to avoid conflicts
                        _groups.Add(group);
                        addedCount++;
                    }
                }
                if (addedCount > 0)
                {
                    Save();
                }
                return addedCount;
            }
        }

        /// <summary>
        /// Imports groups from a file. Returns count of imported groups.
        /// </summary>
        public int ImportFromFile(string filePath, bool replaceExisting = false)
        {
            var json = File.ReadAllText(filePath);
            return ImportFromJson(json, replaceExisting);
        }

        /// <summary>
        /// Gets the path to the groups file
        /// </summary>
        public string GroupsFilePath => _groupsPath;

        /// <summary>
        /// Checks if the groups file exists
        /// </summary>
        public bool GroupsFileExists => File.Exists(_groupsPath);

        /// <summary>
        /// Deletes the groups file and clears all groups
        /// </summary>
        public void Reset()
        {
            _groups.Clear();
            if (File.Exists(_groupsPath))
            {
                File.Delete(_groupsPath);
            }
        }
    }
}
