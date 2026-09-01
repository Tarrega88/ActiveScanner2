using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ActiveScanner.Models;

namespace ActiveScanner.Services
{
    /// <summary>
    /// Service for managing custom groups (Computer, User, Printer groups)
    /// </summary>
    public class CustomGroupService
    {
        private readonly string _filePath;
        private List<CustomGroup> _groups = new();
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public CustomGroupService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ActiveScanner");
            Directory.CreateDirectory(appDataPath);
            _filePath = Path.Combine(appDataPath, "customGroups.json");
        }

        /// <summary>
        /// All custom groups
        /// </summary>
        public IReadOnlyList<CustomGroup> Groups => _groups.AsReadOnly();

        /// <summary>
        /// Computer groups
        /// </summary>
        public IEnumerable<CustomGroup> ComputerGroups => _groups.Where(g => g.Type == CustomGroupType.Computer);

        /// <summary>
        /// User groups
        /// </summary>
        public IEnumerable<CustomGroup> UserGroups => _groups.Where(g => g.Type == CustomGroupType.User);

        /// <summary>
        /// Printer groups
        /// </summary>
        public IEnumerable<CustomGroup> PrinterGroups => _groups.Where(g => g.Type == CustomGroupType.Printer);

        /// <summary>
        /// Load groups from disk
        /// </summary>
        public async Task LoadAsync()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = await File.ReadAllTextAsync(_filePath);
                    var data = JsonSerializer.Deserialize<CustomGroupsFile>(json, _jsonOptions);
                    _groups = data?.Groups ?? new List<CustomGroup>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load custom groups: {ex.Message}");
                _groups = new List<CustomGroup>();
            }
        }

        /// <summary>
        /// Save groups to disk
        /// </summary>
        public async Task SaveAsync()
        {
            try
            {
                var data = new CustomGroupsFile { Groups = _groups };
                var json = JsonSerializer.Serialize(data, _jsonOptions);
                await File.WriteAllTextAsync(_filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save custom groups: {ex.Message}");
            }
        }

        /// <summary>
        /// Create a new custom group
        /// </summary>
        public async Task<CustomGroup> CreateGroupAsync(CustomGroupType type, string name, IEnumerable<CustomGroupMember> members)
        {
            var group = new CustomGroup
            {
                Id = Guid.NewGuid().ToString(),
                Type = type,
                Name = name,
                Created = DateTime.Now,
                LastModified = DateTime.Now,
                Members = members.ToList()
            };

            _groups.Add(group);
            await SaveAsync();
            return group;
        }

        /// <summary>
        /// Create a group from AD objects
        /// </summary>
        public async Task<CustomGroup> CreateGroupFromAdObjectsAsync(CustomGroupType type, string name, IEnumerable<AdObjectInfo> adObjects)
        {
            var members = adObjects.Select(obj => new CustomGroupMember
            {
                Domain = ExtractDomainFromDN(obj.DistinguishedName),
                DistinguishedName = obj.DistinguishedName,
                Name = obj.Name,
                IsManual = false,
                AddedAt = DateTime.Now
            }).ToList();

            return await CreateGroupAsync(type, name, members);
        }

        /// <summary>
        /// Create a group from selected targets
        /// </summary>
        public async Task<CustomGroup> CreateGroupFromTargetsAsync(string name, IEnumerable<AdObjectInfo> targets)
        {
            var members = targets.Select(t => new CustomGroupMember
            {
                Domain = ExtractDomainFromDN(t.DistinguishedName),
                DistinguishedName = t.DistinguishedName,
                Name = t.Name,
                Address = t.TargetAddress,
                IsManual = string.IsNullOrEmpty(t.DistinguishedName),
                AddedAt = DateTime.Now
            }).ToList();

            return await CreateGroupAsync(CustomGroupType.Computer, name, members);
        }

        /// <summary>
        /// Rename a group
        /// </summary>
        public async Task RenameGroupAsync(string groupId, string newName)
        {
            var group = _groups.FirstOrDefault(g => g.Id == groupId);
            if (group != null)
            {
                group.Name = newName;
                group.LastModified = DateTime.Now;
                await SaveAsync();
            }
        }

        /// <summary>
        /// Delete a group
        /// </summary>
        public async Task DeleteGroupAsync(string groupId)
        {
            var group = _groups.FirstOrDefault(g => g.Id == groupId);
            if (group != null)
            {
                _groups.Remove(group);
                await SaveAsync();
            }
        }

        /// <summary>
        /// Reorder groups of a specific type to match the provided order
        /// </summary>
        public void ReorderGroups(CustomGroupType type, List<CustomGroup> orderedGroups)
        {
            // Remove all groups of this type from the internal list
            _groups.RemoveAll(g => g.Type == type);
            
            // Add them back in the new order
            _groups.AddRange(orderedGroups);
        }

        /// <summary>
        /// Remove missing members from a group
        /// </summary>
        public async Task RemoveMissingMembersAsync(string groupId, IEnumerable<string> missingDNs)
        {
            var group = _groups.FirstOrDefault(g => g.Id == groupId);
            if (group != null)
            {
                // Use HashSet for O(1) lookups instead of O(n) Contains on IEnumerable
                var missingSet = missingDNs.ToHashSet(StringComparer.OrdinalIgnoreCase);
                group.Members.RemoveAll(m => m.DistinguishedName != null && missingSet.Contains(m.DistinguishedName));
                group.LastModified = DateTime.Now;
                await SaveAsync();
            }
        }

        /// <summary>
        /// Add members to an existing group
        /// </summary>
        public async Task AddMembersAsync(string groupId, IEnumerable<CustomGroupMember> newMembers)
        {
            var group = _groups.FirstOrDefault(g => g.Id == groupId);
            if (group != null)
            {
                // Avoid duplicates by DN
                var existingDNs = group.Members
                    .Where(m => !string.IsNullOrEmpty(m.DistinguishedName))
                    .Select(m => m.DistinguishedName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var member in newMembers)
                {
                    if (string.IsNullOrEmpty(member.DistinguishedName) || 
                        !existingDNs.Contains(member.DistinguishedName))
                    {
                        group.Members.Add(member);
                    }
                }

                group.LastModified = DateTime.Now;
                await SaveAsync();
            }
        }

        /// <summary>
        /// Get a group by ID
        /// </summary>
        public CustomGroup? GetGroup(string groupId)
        {
            return _groups.FirstOrDefault(g => g.Id == groupId);
        }

        /// <summary>
        /// Extract domain from distinguished name
        /// </summary>
        private static string? ExtractDomainFromDN(string? dn)
        {
            if (string.IsNullOrEmpty(dn)) return null;

            try
            {
                var dcParts = dn.Split(',')
                    .Where(p => p.Trim().StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Trim().Substring(3));
                return string.Join(".", dcParts);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// File structure for JSON serialization
        /// </summary>
        private class CustomGroupsFile
        {
            public List<CustomGroup> Groups { get; set; } = new();
        }
    }
}
