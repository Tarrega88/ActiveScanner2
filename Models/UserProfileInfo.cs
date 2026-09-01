using System;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents a user profile from a remote computer via WMI
    /// </summary>
    public class UserProfileInfo
    {
        /// <summary>
        /// The username extracted from the local path
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// The full local path (e.g., C:\Users\username)
        /// </summary>
        public string LocalPath { get; set; } = string.Empty;

        /// <summary>
        /// When this profile was last used
        /// </summary>
        public DateTime? LastUseTime { get; set; }

        /// <summary>
        /// Human-readable string for last use time (e.g., "2 hours ago") - computed dynamically
        /// </summary>
        public string LastUseTimeFormatted
        {
            get
            {                    
                if (!LastUseTime.HasValue)
                    return "Unknown";
                    
                var localTime = LastUseTime.Value.Kind == DateTimeKind.Utc 
                    ? LastUseTime.Value.ToLocalTime() 
                    : LastUseTime.Value;
                var age = DateTime.Now - localTime;
                
                if (age.TotalMinutes < 1)
                    return "Just now";
                if (age.TotalMinutes < 60)
                    return $"{(int)age.TotalMinutes} minute{((int)age.TotalMinutes == 1 ? "" : "s")} ago";
                if (age.TotalHours < 24)
                    return $"{(int)age.TotalHours} hour{((int)age.TotalHours == 1 ? "" : "s")} ago";
                if (age.TotalDays < 365)
                    return $"{(int)age.TotalDays} day{((int)age.TotalDays == 1 ? "" : "s")} ago";
                    
                return localTime.ToString("MMM d, yyyy");
            }
            set { /* Ignore set - computed property, but setter needed for serialization compatibility */ }
        }

        /// <summary>
        /// The user's SID
        /// </summary>
        public string? Sid { get; set; }

        /// <summary>
        /// Whether this user is currently logged in
        /// </summary>
        public bool IsCurrentlyLoggedIn { get; set; }

        /// <summary>
        /// When we actually saw this user logged in (from our scans)
        /// </summary>
        public DateTime? LastSeenLoggedIn { get; set; }

        /// <summary>
        /// Human-readable string for when we last saw them logged in
        /// </summary>
        public string LastSeenLoggedInFormatted
        {
            get
            {                    
                if (!LastSeenLoggedIn.HasValue)
                    return "Never";
                    
                var localTime = LastSeenLoggedIn.Value.Kind == DateTimeKind.Utc 
                    ? LastSeenLoggedIn.Value.ToLocalTime() 
                    : LastSeenLoggedIn.Value;
                var age = DateTime.Now - localTime;
                
                if (age.TotalMinutes < 1)
                    return "Just now";
                if (age.TotalMinutes < 60)
                    return $"{(int)age.TotalMinutes} minute{((int)age.TotalMinutes == 1 ? "" : "s")} ago";
                if (age.TotalHours < 24)
                    return $"{(int)age.TotalHours} hour{((int)age.TotalHours == 1 ? "" : "s")} ago";
                if (age.TotalDays < 365)
                    return $"{(int)age.TotalDays} day{((int)age.TotalDays == 1 ? "" : "s")} ago";
                    
                return localTime.ToString("MMM d, yyyy");
            }
        }

        /// <summary>
        /// When we found this user's profile via User Profiles Scan
        /// </summary>
        public DateTime? LastProfileScan { get; set; }

        /// <summary>
        /// Human-readable string for when the profile was scanned
        /// </summary>
        public string LastProfileScanFormatted
        {
            get
            {                    
                if (!LastProfileScan.HasValue)
                    return "Never";
                    
                var localTime = LastProfileScan.Value.Kind == DateTimeKind.Utc 
                    ? LastProfileScan.Value.ToLocalTime() 
                    : LastProfileScan.Value;
                var age = DateTime.Now - localTime;
                
                if (age.TotalMinutes < 1)
                    return "Just now";
                if (age.TotalMinutes < 60)
                    return $"{(int)age.TotalMinutes} minute{((int)age.TotalMinutes == 1 ? "" : "s")} ago";
                if (age.TotalHours < 24)
                    return $"{(int)age.TotalHours} hour{((int)age.TotalHours == 1 ? "" : "s")} ago";
                if (age.TotalDays < 365)
                    return $"{(int)age.TotalDays} day{((int)age.TotalDays == 1 ? "" : "s")} ago";
                    
                return localTime.ToString("MMM d, yyyy");
            }
        }

        /// <summary>
        /// The user's email address (from AD lookup cache)
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// The user's display name (from AD lookup cache)
        /// </summary>
        public string? DisplayName { get; set; }

        public override string ToString() => $"{Username} ({LastUseTimeFormatted})";
    }
}
