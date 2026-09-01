using System;
using System.Collections.Generic;
using LiteDB;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents cached data for a computer, keyed by its AD objectGUID
    /// </summary>
    public class ComputerCacheEntry
    {
        /// <summary>
        /// The AD objectGUID as string (used as document ID in LiteDB)
        /// </summary>
        [BsonId]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The computer's name at the time of caching
        /// </summary>
        public string ComputerName { get; set; } = string.Empty;

        /// <summary>
        /// The computer's distinguished name at the time of caching
        /// </summary>
        public string? DistinguishedName { get; set; }

        /// <summary>
        /// When this computer was last seen in an AD query
        /// </summary>
        public DateTime? LastSeenInAd { get; set; }

        /// <summary>
        /// When any network tool was last successfully run on this computer (indicates online)
        /// </summary>
        public DateTime? LastOnline { get; set; }

        /// <summary>
        /// When the last user query was performed
        /// </summary>
        public DateTime? LastUserQueriedAt { get; set; }

        /// <summary>
        /// The most recent user (for quick display)
        /// </summary>
        public string? LastUser { get; set; }

        /// <summary>
        /// When the most recent user was last active (from WMI profile LastUseTime)
        /// </summary>
        public DateTime? LastUserActiveAt { get; set; }

        /// <summary>
        /// Whether the last user is currently logged in
        /// </summary>
        public bool IsLoggedIn { get; set; }

        /// <summary>
        /// All known users on this computer (merged from all queries)
        /// </summary>
        public List<CachedUserProfile> KnownUsers { get; set; } = new();

        /// <summary>
        /// Known IP addresses for this computer (IP -> last seen timestamp)
        /// </summary>
        public Dictionary<string, DateTime> KnownIpAddresses { get; set; } = new();

        /// <summary>
        /// When the computer was last confirmed reachable by a successful network operation
        /// </summary>
        public DateTime? LastNetworkActivity { get; set; }
    }

    /// <summary>
    /// A user profile found on a computer
    /// </summary>
    public class CachedUserProfile
    {
        public string Username { get; set; } = string.Empty;
        public string? LocalPath { get; set; }
        public DateTime? LastUseTime { get; set; }
        public string? Sid { get; set; }
        public bool IsCurrentlyLoggedIn { get; set; }
        
        /// <summary>
        /// When we actually saw this user logged in (from our scans)
        /// </summary>
        public DateTime? LastSeenLoggedIn { get; set; }
        
        /// <summary>
        /// When we found this user's profile via User Profiles Scan
        /// </summary>
        public DateTime? LastProfileScan { get; set; }
    }

    /// <summary>
    /// Root object for the computer cache JSON file (legacy, kept for reference)
    /// </summary>
    public class ComputerCacheFile
    {
        public int Version { get; set; } = 1;
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Dictionary keyed by objectGUID string
        /// </summary>
        public Dictionary<string, ComputerCacheEntry> Entries { get; set; } = new();
    }
}
