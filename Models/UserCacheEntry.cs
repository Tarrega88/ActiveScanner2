using System;
using System.Collections.Generic;
using LiteDB;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents a computer that a user has logged into
    /// </summary>
    public class UserMachineEntry
    {
        /// <summary>
        /// The computer name
        /// </summary>
        public string ComputerName { get; set; } = string.Empty;

        /// <summary>
        /// When the user was last seen on this computer
        /// </summary>
        public DateTime LastSeen { get; set; }

        /// <summary>
        /// Whether the user was currently logged in when last checked
        /// </summary>
        public bool WasLoggedIn { get; set; }
    }

    /// <summary>
    /// Represents cached data for a domain user, keyed by sAMAccountName
    /// </summary>
    public class UserCacheEntry
    {
        /// <summary>
        /// The user's sAMAccountName (used as document ID in LiteDB)
        /// </summary>
        [BsonId]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// The user's email address from Active Directory
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// The user's display name from Active Directory
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// The user's SID (if known)
        /// </summary>
        public string? Sid { get; set; }

        /// <summary>
        /// The user's ServiceNow sys_id (for SNOW integration)
        /// </summary>
        public string? SnowId { get; set; }

        /// <summary>
        /// When this cache entry was last updated from AD
        /// </summary>
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// Whether the user was found in AD (false if lookup failed or user doesn't exist)
        /// </summary>
        public bool FoundInAd { get; set; }

        /// <summary>
        /// Computers this user has been seen on (populated from Get Last User scans)
        /// </summary>
        public List<UserMachineEntry> KnownMachines { get; set; } = new();
    }
}
