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
    /// Service for managing the computer cache (last user data, online status, etc.) using LiteDB
    /// </summary>
    public class ComputerCacheService : IDisposable
    {
        private readonly string _dbPath;
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<ComputerCacheEntry> _computers;

        public ComputerCacheService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ActiveScanner");
            Directory.CreateDirectory(appDataPath);
            _dbPath = Path.Combine(appDataPath, "computerCache.db");
            
            // Open LiteDB connection (auto-creates file if doesn't exist)
            _db = new LiteDatabase($"Filename={_dbPath};Connection=shared");
            _computers = _db.GetCollection<ComputerCacheEntry>("computers");
            
            // Create index on ComputerName for faster lookups
            _computers.EnsureIndex(x => x.ComputerName);
        }

        /// <summary>
        /// Load cache from disk (no-op for LiteDB - it's always loaded)
        /// </summary>
        public Task LoadAsync()
        {
            // LiteDB handles this automatically
            return Task.CompletedTask;
        }

        /// <summary>
        /// Save cache to disk (no-op for LiteDB - writes are immediate)
        /// </summary>
        public Task SaveAsync(bool force = false)
        {
            // LiteDB handles this automatically
            // Checkpoint to ensure data is flushed to disk
            if (force)
            {
                _db.Checkpoint();
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Get a cache entry by objectGUID
        /// </summary>
        public ComputerCacheEntry? GetEntry(Guid objectGuid)
        {
            var key = objectGuid.ToString();
            return _computers.FindById(key);
        }

        /// <summary>
        /// Get or create a cache entry for a computer
        /// </summary>
        public ComputerCacheEntry GetOrCreateEntry(Guid objectGuid, string computerName, string? distinguishedName = null)
        {
            var key = objectGuid.ToString();
            var entry = _computers.FindById(key);
            
            if (entry == null)
            {
                entry = new ComputerCacheEntry
                {
                    Id = key,
                    ComputerName = computerName,
                    DistinguishedName = distinguishedName,
                    LastSeenInAd = DateTime.UtcNow
                };
                _computers.Insert(entry);
            }
            else
            {
                // Update basic info if changed
                bool needsUpdate = false;
                
                if (entry.ComputerName != computerName)
                {
                    entry.ComputerName = computerName;
                    needsUpdate = true;
                }
                if (entry.DistinguishedName != distinguishedName)
                {
                    entry.DistinguishedName = distinguishedName;
                    needsUpdate = true;
                }
                
                entry.LastSeenInAd = DateTime.UtcNow;
                needsUpdate = true;
                
                if (needsUpdate)
                {
                    _computers.Update(entry);
                }
            }
            
            return entry;
        }

        /// <summary>
        /// Update the last online time for a computer (called when any network tool succeeds)
        /// </summary>
        public void UpdateLastOnline(Guid objectGuid, string computerName, string? distinguishedName = null)
        {
            var entry = GetOrCreateEntry(objectGuid, computerName, distinguishedName);
            entry.LastOnline = DateTime.UtcNow;
            _computers.Update(entry);
        }

        /// <summary>
        /// Update the last user data for a computer
        /// </summary>
        public void UpdateLastUser(Guid objectGuid, string computerName, string? distinguishedName,
            string? lastUser, bool isLoggedIn, List<UserProfileInfo>? profiles, DateTime? lastUserActiveAt = null)
        {
            var entry = GetOrCreateEntry(objectGuid, computerName, distinguishedName);
            var now = DateTime.UtcNow;
            
            entry.LastOnline = now; // If we got user data, computer was online
            entry.LastUserQueriedAt = now;
            entry.LastUser = lastUser;
            entry.LastUserActiveAt = lastUserActiveAt;
            entry.IsLoggedIn = isLoggedIn;
            
            // Merge profiles - add new users, update existing ones if LastUseTime is newer
            if (profiles != null)
            {
                entry.KnownUsers ??= new List<CachedUserProfile>();
                
                foreach (var profile in profiles)
                {
                    var existing = entry.KnownUsers.FirstOrDefault(u => 
                        string.Equals(u.Username, profile.Username, StringComparison.OrdinalIgnoreCase));
                    
                    if (existing != null)
                    {
                        // Update if this profile has a newer LastUseTime
                        if (profile.LastUseTime.HasValue && 
                            (!existing.LastUseTime.HasValue || profile.LastUseTime > existing.LastUseTime))
                        {
                            existing.LastUseTime = profile.LastUseTime;
                            existing.LocalPath = profile.LocalPath;
                            existing.Sid = profile.Sid;
                        }
                        // Always update IsCurrentlyLoggedIn to current state
                        existing.IsCurrentlyLoggedIn = profile.IsCurrentlyLoggedIn;
                        // Track when we actually saw them logged in
                        if (profile.IsCurrentlyLoggedIn)
                        {
                            existing.LastSeenLoggedIn = now;
                        }
                    }
                    else
                    {
                        // Add new user
                        entry.KnownUsers.Add(new CachedUserProfile
                        {
                            Username = profile.Username,
                            LocalPath = profile.LocalPath,
                            LastUseTime = profile.LastUseTime,
                            Sid = profile.Sid,
                            IsCurrentlyLoggedIn = profile.IsCurrentlyLoggedIn,
                            LastSeenLoggedIn = profile.IsCurrentlyLoggedIn ? now : null
                        });
                    }
                }
            }
            
            _computers.Update(entry);
        }

        /// <summary>
        /// Update only the active/logged-in user for a computer (fast method, no profiles)
        /// </summary>
        public void UpdateActiveUser(Guid objectGuid, string computerName, string? distinguishedName, string username)
        {
            var entry = GetOrCreateEntry(objectGuid, computerName, distinguishedName);
            var now = DateTime.UtcNow;
            
            entry.LastOnline = now;
            entry.LastUserQueriedAt = now;
            entry.LastUser = username;
            entry.LastUserActiveAt = now;
            entry.IsLoggedIn = true;
            
            // Update or add this user to KnownUsers with LastSeenLoggedIn
            entry.KnownUsers ??= new List<CachedUserProfile>();
            var existing = entry.KnownUsers.FirstOrDefault(u => 
                string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
            
            if (existing != null)
            {
                existing.IsCurrentlyLoggedIn = true;
                existing.LastSeenLoggedIn = now;
            }
            else
            {
                entry.KnownUsers.Add(new CachedUserProfile
                {
                    Username = username,
                    IsCurrentlyLoggedIn = true,
                    LastSeenLoggedIn = now
                });
            }
            
            _computers.Update(entry);
        }

        /// <summary>
        /// Update all user profiles from a User Profiles Scan (slow/full method)
        /// </summary>
        public void UpdateUserProfiles(Guid objectGuid, string computerName, string? distinguishedName, 
            List<UserProfileInfo> profiles, string? currentlyLoggedInUser)
        {
            var entry = GetOrCreateEntry(objectGuid, computerName, distinguishedName);
            var now = DateTime.UtcNow;
            
            entry.LastOnline = now;
            entry.LastUserQueriedAt = now;
            
            if (!string.IsNullOrEmpty(currentlyLoggedInUser))
            {
                entry.LastUser = currentlyLoggedInUser;
                entry.LastUserActiveAt = now;
                entry.IsLoggedIn = true;
            }
            
            // Update all profiles
            entry.KnownUsers ??= new List<CachedUserProfile>();
            
            foreach (var profile in profiles)
            {
                var existing = entry.KnownUsers.FirstOrDefault(u => 
                    string.Equals(u.Username, profile.Username, StringComparison.OrdinalIgnoreCase));
                
                if (existing != null)
                {
                    existing.LocalPath = profile.LocalPath;
                    existing.Sid = profile.Sid;
                    existing.LastProfileScan = now;
                    if (profile.LastUseTime.HasValue)
                        existing.LastUseTime = profile.LastUseTime;
                    if (profile.IsCurrentlyLoggedIn)
                    {
                        existing.IsCurrentlyLoggedIn = true;
                        existing.LastSeenLoggedIn = now;
                    }
                }
                else
                {
                    entry.KnownUsers.Add(new CachedUserProfile
                    {
                        Username = profile.Username,
                        LocalPath = profile.LocalPath,
                        LastUseTime = profile.LastUseTime,
                        Sid = profile.Sid,
                        IsCurrentlyLoggedIn = profile.IsCurrentlyLoggedIn,
                        LastSeenLoggedIn = profile.IsCurrentlyLoggedIn ? now : null,
                        LastProfileScan = now
                    });
                }
            }
            
            _computers.Update(entry);
        }

        /// <summary>
        /// Update the IP address history for a computer
        /// </summary>
        public void UpdateIpAddress(Guid objectGuid, string computerName, string? distinguishedName, string? ipAddress)
        {
            // Don't store non-IP values (resolving, not found, error, etc.)
            if (string.IsNullOrEmpty(ipAddress) || ipAddress.StartsWith("("))
                return;

            var entry = GetOrCreateEntry(objectGuid, computerName, distinguishedName);
            entry.KnownIpAddresses ??= new Dictionary<string, DateTime>();
            
            // Upsert - add or update the timestamp
            entry.KnownIpAddresses[ipAddress] = DateTime.UtcNow;
            
            _computers.Update(entry);
        }

        /// <summary>
        /// Get the IP history for a computer
        /// </summary>
        public Dictionary<string, DateTime>? GetIpHistory(Guid objectGuid)
        {
            var entry = GetEntry(objectGuid);
            return entry?.KnownIpAddresses;
        }

        /// <summary>
        /// Lightweight update of LastSeenInAd for computers returned from AD queries.
        /// Only updates existing entries - does not create new ones.
        /// </summary>
        public void TouchLastSeen(Guid objectGuid, string computerName, string? distinguishedName)
        {
            var key = objectGuid.ToString();
            var entry = _computers.FindById(key);
            
            if (entry != null)
            {
                // Only update if already tracked
                entry.LastSeenInAd = DateTime.UtcNow;
                entry.ComputerName = computerName;  // Keep name current
                entry.DistinguishedName = distinguishedName;
                _computers.Update(entry);
            }
            // If not in cache, do nothing - we only track computers 
            // that have had explicit user queries
        }

        /// <summary>
        /// Batch update LastSeenInAd for multiple computers (runs in background)
        /// </summary>
        public void TouchLastSeenBatch(IEnumerable<(Guid ObjectGuid, string Name, string? DistinguishedName)> computers)
        {
            _db.BeginTrans();
            try
            {
                foreach (var (objectGuid, name, dn) in computers)
                {
                    var key = objectGuid.ToString();
                    var entry = _computers.FindById(key);
                    if (entry != null)
                    {
                        entry.LastSeenInAd = DateTime.UtcNow;
                        entry.ComputerName = name;
                        entry.DistinguishedName = dn;
                        _computers.Update(entry);
                    }
                }
                _db.Commit();
            }
            catch
            {
                _db.Rollback();
                throw;
            }
        }

        /// <summary>
        /// Get all GUIDs currently in cache
        /// </summary>
        public IEnumerable<Guid> GetAllCachedGuids()
        {
            return _computers.FindAll()
                .Select(e => Guid.TryParse(e.Id, out var guid) ? guid : (Guid?)null)
                .Where(g => g.HasValue)
                .Select(g => g!.Value);
        }

        /// <summary>
        /// Get all cache entries
        /// </summary>
        public IEnumerable<ComputerCacheEntry> GetAllEntries()
        {
            return _computers.FindAll();
        }

        /// <summary>
        /// Get statistics about the cache
        /// </summary>
        public (int total, int seenRecently, int withLastUser) GetStats()
        {
            var all = _computers.FindAll().ToList();
            var total = all.Count;
            var cutoff = DateTime.UtcNow.AddDays(-30);
            var seenRecently = all.Count(e => e.LastSeenInAd.HasValue && e.LastSeenInAd > cutoff);
            var withLastUser = all.Count(e => !string.IsNullOrEmpty(e.LastUser));
            return (total, seenRecently, withLastUser);
        }

        /// <summary>
        /// Find computers by name (partial match)
        /// </summary>
        public IEnumerable<ComputerCacheEntry> FindByName(string namePattern)
        {
            return _computers.Find(x => x.ComputerName.Contains(namePattern));
        }

        /// <summary>
        /// Get computers with stale last user data (older than specified days)
        /// </summary>
        public IEnumerable<ComputerCacheEntry> GetStaleEntries(int olderThanDays = 30)
        {
            var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
            return _computers.Find(x => x.LastUserQueriedAt != null && x.LastUserQueriedAt < cutoff);
        }

        /// <summary>
        /// Get computers that were online recently
        /// </summary>
        public IEnumerable<ComputerCacheEntry> GetRecentlyOnline(int withinDays = 7)
        {
            var cutoff = DateTime.UtcNow.AddDays(-withinDays);
            return _computers.Find(x => x.LastOnline != null && x.LastOnline > cutoff);
        }

        /// <summary>
        /// Convert cached profiles back to UserProfileInfo for display
        /// </summary>
        public static List<UserProfileInfo>? ConvertToUserProfileInfos(List<CachedUserProfile>? cached)
        {
            if (cached == null || cached.Count == 0) return null;
            
            return cached.Select(p => new UserProfileInfo
            {
                Username = p.Username,
                LocalPath = p.LocalPath ?? string.Empty,
                LastUseTime = p.LastUseTime,
                Sid = p.Sid,
                IsCurrentlyLoggedIn = p.IsCurrentlyLoggedIn
            }).ToList();
        }

        /// <summary>
        /// Import and merge entries from another database file.
        /// Merges entries by preferring newer data.
        /// </summary>
        public int ImportAndMerge(string sourceDbPath)
        {
            if (!File.Exists(sourceDbPath))
                throw new FileNotFoundException("Source database not found", sourceDbPath);

            int imported = 0;

            using var sourceDb = new LiteDatabase($"Filename={sourceDbPath};Connection=shared;ReadOnly=true");
            var sourceComputers = sourceDb.GetCollection<ComputerCacheEntry>("computers");

            foreach (var sourceEntry in sourceComputers.FindAll())
            {
                if (string.IsNullOrWhiteSpace(sourceEntry.Id))
                    continue;

                var existing = _computers.FindById(sourceEntry.Id);

                if (existing == null)
                {
                    // New entry - insert directly
                    _computers.Insert(sourceEntry);
                    imported++;
                }
                else
                {
                    // Existing entry - merge by preferring newer data
                    bool updated = false;

                    // Update LastSeenInAd if source is newer
                    if (sourceEntry.LastSeenInAd.HasValue && 
                        (!existing.LastSeenInAd.HasValue || sourceEntry.LastSeenInAd > existing.LastSeenInAd))
                    {
                        existing.LastSeenInAd = sourceEntry.LastSeenInAd;
                        updated = true;
                    }

                    // Update LastOnline if source is newer
                    if (sourceEntry.LastOnline.HasValue && 
                        (!existing.LastOnline.HasValue || sourceEntry.LastOnline > existing.LastOnline))
                    {
                        existing.LastOnline = sourceEntry.LastOnline;
                        updated = true;
                    }

                    // Update LastUser info if source is newer
                    if (sourceEntry.LastUserQueriedAt.HasValue && 
                        (!existing.LastUserQueriedAt.HasValue || sourceEntry.LastUserQueriedAt > existing.LastUserQueriedAt))
                    {
                        existing.LastUserQueriedAt = sourceEntry.LastUserQueriedAt;
                        existing.LastUser = sourceEntry.LastUser;
                        existing.IsLoggedIn = sourceEntry.IsLoggedIn;
                        updated = true;
                    }

                    // Merge KnownUsers lists
                    if (sourceEntry.KnownUsers?.Count > 0)
                    {
                        existing.KnownUsers ??= new List<CachedUserProfile>();
                        
                        foreach (var sourceUser in sourceEntry.KnownUsers)
                        {
                            var existingUser = existing.KnownUsers.FirstOrDefault(u => 
                                string.Equals(u.Username, sourceUser.Username, StringComparison.OrdinalIgnoreCase));

                            if (existingUser != null)
                            {
                                // Update if source has newer data
                                if (sourceUser.LastUseTime.HasValue && 
                                    (!existingUser.LastUseTime.HasValue || sourceUser.LastUseTime > existingUser.LastUseTime))
                                {
                                    existingUser.LastUseTime = sourceUser.LastUseTime;
                                    existingUser.LocalPath = sourceUser.LocalPath;
                                    existingUser.Sid = sourceUser.Sid;
                                    updated = true;
                                }
                            }
                            else
                            {
                                // Add new user
                                existing.KnownUsers.Add(sourceUser);
                                updated = true;
                            }
                        }
                    }

                    // Merge KnownIpAddresses dictionaries
                    if (sourceEntry.KnownIpAddresses?.Count > 0)
                    {
                        existing.KnownIpAddresses ??= new Dictionary<string, DateTime>();
                        
                        foreach (var (ip, lastSeen) in sourceEntry.KnownIpAddresses)
                        {
                            // Update if source has newer timestamp for this IP
                            if (!existing.KnownIpAddresses.TryGetValue(ip, out var existingTime) || lastSeen > existingTime)
                            {
                                existing.KnownIpAddresses[ip] = lastSeen;
                                updated = true;
                            }
                        }
                    }

                    // Merge LastNetworkActivity (keep newest)
                    if (sourceEntry.LastNetworkActivity.HasValue &&
                        (!existing.LastNetworkActivity.HasValue || sourceEntry.LastNetworkActivity > existing.LastNetworkActivity))
                    {
                        existing.LastNetworkActivity = sourceEntry.LastNetworkActivity;
                        updated = true;
                    }

                    if (updated)
                    {
                        _computers.Update(existing);
                        imported++;
                    }
                }
            }

            return imported;
        }

        /// <summary>
        /// Clear all Last User data from all computer entries.
        /// Preserves other data like IP addresses and network activity.
        /// </summary>
        /// <returns>Number of computers whose Last User data was cleared</returns>
        public int ClearAllLastUserData()
        {
            var computers = _computers.FindAll().ToList();
            int cleared = 0;

            foreach (var entry in computers)
            {
                if (!string.IsNullOrEmpty(entry.LastUser) || entry.KnownUsers?.Count > 0)
                {
                    entry.LastUser = null;
                    entry.LastUserActiveAt = null;
                    entry.LastUserQueriedAt = null;
                    entry.IsLoggedIn = false;
                    entry.KnownUsers = new List<CachedUserProfile>();
                    _computers.Update(entry);
                    cleared++;
                }
            }

            return cleared;
        }

        public void Dispose()
        {
            _db?.Dispose();
        }
    }
}
