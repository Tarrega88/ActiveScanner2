using System;
using System.Collections.Generic;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents a cached query result with timestamp
    /// </summary>
    public class CacheEntry
    {
        /// <summary>
        /// The cache key (OU path for domains, group ID for groups)
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Display name for the cached location (domain path or group name)
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// When this cache entry was created/updated
        /// </summary>
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// The cached AD objects
        /// </summary>
        public List<AdObjectInfo> Results { get; set; } = new();

        /// <summary>
        /// The cached subfolders (for domain navigation)
        /// </summary>
        public List<FolderItem> Subfolders { get; set; } = new();

        /// <summary>
        /// Whether this is a group cache entry (vs domain/OU cache)
        /// </summary>
        public bool IsGroupCache { get; set; }

        /// <summary>
        /// Gets the age of this cache entry
        /// </summary>
        public TimeSpan Age => DateTime.Now - LastUpdated;

        /// <summary>
        /// Gets a human-readable string for how long ago the cache was updated
        /// </summary>
        public string AgeDisplay
        {
            get
            {
                var age = Age;
                if (age.TotalSeconds < 60)
                    return "just now";
                if (age.TotalMinutes < 2)
                    return "1 minute ago";
                if (age.TotalMinutes < 60)
                    return $"{(int)age.TotalMinutes} minutes ago";
                if (age.TotalHours < 2)
                    return "1 hour ago";
                return $"{(int)age.TotalHours} hours ago";
            }
        }

        /// <summary>
        /// Checks if this cache entry is still valid based on the TTL
        /// </summary>
        public bool IsValid(int cacheTtlMinutes)
        {
            return Age.TotalMinutes < cacheTtlMinutes;
        }
    }
}
