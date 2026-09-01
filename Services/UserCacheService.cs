using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ActiveScanner.Models;
using LiteDB;

namespace ActiveScanner.Services
{
    /// <summary>
    /// Service for caching user data (email, display name) looked up from Active Directory.
    /// Uses LiteDB with shared connection to the same database as ComputerCacheService.
    /// </summary>
    public class UserCacheService : IDisposable
    {
        private readonly string _dbPath;
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<UserCacheEntry> _users;

        public UserCacheService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ActiveScanner");
            Directory.CreateDirectory(appDataPath);
            _dbPath = Path.Combine(appDataPath, "computerCache.db"); // Same DB as ComputerCacheService
            
            // Open LiteDB connection with shared mode (supports multiple connections)
            _db = new LiteDatabase($"Filename={_dbPath};Connection=shared");
            _users = _db.GetCollection<UserCacheEntry>("users");
            
            // Create index on Username for faster lookups
            _users.EnsureIndex(x => x.Username);
            // Create index on Email for email-based lookups
            _users.EnsureIndex(x => x.Email);
        }

        /// <summary>
        /// Get a cached user by username (sAMAccountName)
        /// </summary>
        public UserCacheEntry? GetUser(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return null;
                
            return _users.FindById(username.ToLowerInvariant());
        }

        /// <summary>
        /// Get cached users by a list of usernames
        /// </summary>
        public Dictionary<string, UserCacheEntry> GetUsers(IEnumerable<string> usernames)
        {
            var result = new Dictionary<string, UserCacheEntry>(StringComparer.OrdinalIgnoreCase);
            
            // Get valid usernames and their lowercase keys
            var usernameList = usernames.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
            if (usernameList.Count == 0)
                return result;
            
            // Use batch query instead of sequential lookups
            var keys = usernameList.Select(u => u.ToLowerInvariant()).ToList();
            var entries = _users.Find(Query.In("_id", keys.Select(k => new BsonValue(k))));
            
            foreach (var entry in entries)
            {
                result[entry.Username] = entry;
            }
            
            return result;
        }

        /// <summary>
        /// Update or create a user cache entry
        /// </summary>
        public void UpdateUser(string username, string? email, string? displayName, string? sid, bool foundInAd = true)
        {
            if (string.IsNullOrWhiteSpace(username))
                return;

            var key = username.ToLowerInvariant();
            var entry = _users.FindById(key);
            
            if (entry == null)
            {
                entry = new UserCacheEntry
                {
                    Username = key,
                    Email = email,
                    DisplayName = displayName,
                    Sid = sid,
                    LastUpdated = DateTime.UtcNow,
                    FoundInAd = foundInAd
                };
                _users.Insert(entry);
            }
            else
            {
                entry.Email = email;
                entry.DisplayName = displayName;
                if (!string.IsNullOrEmpty(sid))
                    entry.Sid = sid;
                entry.LastUpdated = DateTime.UtcNow;
                entry.FoundInAd = foundInAd;
                _users.Update(entry);
            }
        }

        /// <summary>
        /// Bulk update users from AD lookup results
        /// </summary>
        public void UpdateUsers(IEnumerable<(string Username, string? Email, string? DisplayName)> users)
        {
            foreach (var (username, email, displayName) in users)
            {
                UpdateUser(username, email, displayName, null, foundInAd: true);
            }
        }

        /// <summary>
        /// Mark users as not found in AD (so we don't keep querying for them)
        /// </summary>
        public void MarkUsersNotFound(IEnumerable<string> usernames)
        {
            foreach (var username in usernames)
            {
                UpdateUser(username, null, null, null, foundInAd: false);
            }
        }

        /// <summary>
        /// Get usernames that need to be looked up (not in cache or cache is stale)
        /// </summary>
        public List<string> GetUsernamesNeedingLookup(IEnumerable<string> usernames, TimeSpan? maxAge = null)
        {
            var maxAgeActual = maxAge ?? TimeSpan.FromDays(30); // Default: refresh after 30 days
            var cutoff = DateTime.UtcNow - maxAgeActual;
            
            // Get valid usernames
            var usernameList = usernames.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
            if (usernameList.Count == 0)
                return new List<string>();
            
            // Batch query for existing entries
            var keys = usernameList.Select(u => u.ToLowerInvariant()).ToList();
            var existingEntries = _users.Find(Query.In("_id", keys.Select(k => new BsonValue(k))))
                .ToDictionary(e => e.Username, StringComparer.OrdinalIgnoreCase);
            
            // Find usernames that need lookup (not cached or stale)
            var needsLookup = new List<string>();
            foreach (var username in usernameList)
            {
                if (!existingEntries.TryGetValue(username.ToLowerInvariant(), out var entry) || 
                    entry.LastUpdated < cutoff)
                {
                    needsLookup.Add(username);
                }
            }
            
            return needsLookup;
        }

        /// <summary>
        /// Get number of cached users
        /// </summary>
        public int Count => _users.Count();

        /// <summary>
        /// Update the machine history for a user (called when Get Last User finds them on a computer)
        /// </summary>
        public void UpdateUserMachine(string username, string computerName, DateTime lastSeen, bool wasLoggedIn)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(computerName))
                return;

            var key = username.ToLowerInvariant();
            var entry = _users.FindById(key);
            
            if (entry == null)
            {
                // Create minimal entry - will be enriched when user is queried from AD
                entry = new UserCacheEntry
                {
                    Username = key,
                    LastUpdated = DateTime.UtcNow,
                    FoundInAd = false, // Not confirmed from AD yet
                    KnownMachines = new List<UserMachineEntry>()
                };
                _users.Insert(entry);
            }

            entry.KnownMachines ??= new List<UserMachineEntry>();

            // Find existing machine entry or create new one
            var machineEntry = entry.KnownMachines.FirstOrDefault(m => 
                string.Equals(m.ComputerName, computerName, StringComparison.OrdinalIgnoreCase));

            if (machineEntry != null)
            {
                // Only update LastSeen if user was actually logged in (not just profile exists)
                if (wasLoggedIn && lastSeen > machineEntry.LastSeen)
                {
                    machineEntry.LastSeen = lastSeen;
                }
                machineEntry.WasLoggedIn = wasLoggedIn;
            }
            else
            {
                // Add new machine - only set LastSeen if user is logged in
                entry.KnownMachines.Add(new UserMachineEntry
                {
                    ComputerName = computerName,
                    LastSeen = wasLoggedIn ? lastSeen : DateTime.MinValue,
                    WasLoggedIn = wasLoggedIn
                });
            }

            _users.Update(entry);
        }

        /// <summary>
        /// Batch update machine history for multiple users on the same computer
        /// </summary>
        public void UpdateUserMachinesBatch(string computerName, IEnumerable<(string Username, DateTime LastSeen, bool WasLoggedIn)> users)
        {
            foreach (var (username, lastSeen, wasLoggedIn) in users)
            {
                UpdateUserMachine(username, computerName, lastSeen, wasLoggedIn);
            }
        }

        /// <summary>
        /// Get known machines for a user
        /// </summary>
        public List<UserMachineEntry> GetUserMachines(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return new List<UserMachineEntry>();

            var entry = _users.FindById(username.ToLowerInvariant());
            return entry?.KnownMachines?.OrderByDescending(m => m.LastSeen).ToList() 
                   ?? new List<UserMachineEntry>();
        }

        /// <summary>
        /// Syncs KnownMachines data from existing ComputerCacheService entries.
        /// Call this on startup to ensure old "Get Last User" data is available for reverse lookup.
        /// </summary>
        public int SyncFromComputerCache(IEnumerable<Models.ComputerCacheEntry> computerEntries)
        {
            int synced = 0;
            
            foreach (var computer in computerEntries)
            {
                if (string.IsNullOrWhiteSpace(computer.ComputerName))
                    continue;

                // Sync from KnownUsers list (newer entries)
                if (computer.KnownUsers != null && computer.KnownUsers.Count > 0)
                {
                    foreach (var user in computer.KnownUsers)
                    {
                        if (string.IsNullOrWhiteSpace(user.Username))
                            continue;

                        var lastSeen = user.LastUseTime ?? computer.LastUserQueriedAt ?? DateTime.UtcNow;
                        UpdateUserMachine(user.Username, computer.ComputerName, lastSeen, user.IsCurrentlyLoggedIn);
                        synced++;
                    }
                }
                // Fallback: sync from LastUser string (older entries without KnownUsers)
                else if (!string.IsNullOrWhiteSpace(computer.LastUser))
                {
                    var lastSeen = computer.LastUserQueriedAt ?? DateTime.UtcNow;
                    UpdateUserMachine(computer.LastUser, computer.ComputerName, lastSeen, computer.IsLoggedIn);
                    synced++;
                }
            }

            return synced;
        }

        /// <summary>
        /// Import ServiceNow sys_ids from a JSON file mapping username to sys_id.
        /// This is a one-time import operation for bulk populating SNOW IDs.
        /// </summary>
        /// <param name="jsonPath">Path to JSON file with format { "username": "sys_id", ... }</param>
        /// <returns>Tuple of (updated count, created count)</returns>
        public (int Updated, int Created) ImportSnowIds(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                return (0, 0);

            var jsonContent = File.ReadAllText(jsonPath);
            var snowUsers = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent);
            
            if (snowUsers == null || snowUsers.Count == 0)
                return (0, 0);

            int updated = 0;
            int created = 0;

            foreach (var kvp in snowUsers)
            {
                var username = kvp.Key;
                var snowId = kvp.Value;
                
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(snowId))
                    continue;

                var key = username.ToLowerInvariant();
                var entry = _users.FindById(key);

                if (entry != null)
                {
                    // Update existing user with SNOW ID
                    entry.SnowId = snowId;
                    _users.Update(entry);
                    updated++;
                }
                else
                {
                    // Create minimal entry with SNOW ID
                    entry = new UserCacheEntry
                    {
                        Username = key,
                        SnowId = snowId,
                        LastUpdated = DateTime.UtcNow,
                        FoundInAd = false
                    };
                    _users.Insert(entry);
                    created++;
                }
            }

            return (updated, created);
        }

        /// <summary>
        /// Get the SNOW ID (sys_id) for a user by username
        /// </summary>
        public string? GetSnowId(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return null;

            var entry = _users.FindById(username.ToLowerInvariant());
            return entry?.SnowId;
        }

        /// <summary>
        /// Get the SNOW ID by email or username. Detects email by presence of @.
        /// Returns (snowId, displayName) tuple.
        /// </summary>
        public (string? SnowId, string? DisplayName) GetSnowIdByEmailOrUsername(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return (null, null);

            UserCacheEntry? entry;

            if (input.Contains('@'))
            {
                // Email lookup
                var emailLower = input.ToLowerInvariant().Trim();
                entry = _users.FindOne(u => u.Email != null && u.Email.ToLower() == emailLower);
            }
            else
            {
                // Username lookup
                entry = _users.FindById(input.ToLowerInvariant().Trim());
            }

            return (entry?.SnowId, entry?.DisplayName);
        }

        /// <summary>
        /// Update or set the SNOW ID for a user. Creates entry if it doesn't exist.
        /// </summary>
        public void UpdateSnowId(string username, string snowId)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(snowId))
                return;

            var key = username.ToLowerInvariant();
            var entry = _users.FindById(key);

            if (entry != null)
            {
                entry.SnowId = snowId;
                _users.Update(entry);
            }
            else
            {
                entry = new UserCacheEntry
                {
                    Username = key,
                    SnowId = snowId,
                    LastUpdated = DateTime.UtcNow,
                    FoundInAd = false
                };
                _users.Insert(entry);
            }
        }

        /// <summary>
        /// Get count of users that have a SNOW ID populated
        /// </summary>
        public int GetSnowIdCount()
        {
            return _users.Count(u => !string.IsNullOrEmpty(u.SnowId));
        }

        /// <summary>
        /// Get count of users missing SNOW IDs
        /// </summary>
        public int GetMissingSnowIdCount()
        {
            return _users.Count(u => string.IsNullOrEmpty(u.SnowId));
        }

        /// <summary>
        /// Clear all machine history (KnownMachines) for all users.
        /// Preserves other user data like email, display name, and SNOW IDs.
        /// </summary>
        /// <returns>Number of users whose machine history was cleared</returns>
        public int ClearAllMachineHistory()
        {
            var usersWithMachines = _users.Find(u => u.KnownMachines != null && u.KnownMachines.Count > 0).ToList();
            int cleared = 0;

            foreach (var user in usersWithMachines)
            {
                user.KnownMachines = new List<UserMachineEntry>();
                _users.Update(user);
                cleared++;
            }

            return cleared;
        }

        public void Dispose()
        {
            _db?.Dispose();
        }
    }
}
