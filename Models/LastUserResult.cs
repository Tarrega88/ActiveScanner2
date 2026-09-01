using System;
using System.Collections.Generic;
using System.Linq;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents the result of a last user query for a target computer
    /// </summary>
    public class LastUserResult
    {
        /// <summary>
        /// The name of the target computer
        /// </summary>
        public string TargetName { get; set; } = string.Empty;

        /// <summary>
        /// The address used to connect
        /// </summary>
        public string TargetAddress { get; set; } = string.Empty;

        /// <summary>
        /// Whether the query was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The status of the query (Success, Offline, Failed, etc.)
        /// </summary>
        public string Status { get; set; } = "Pending";

        /// <summary>
        /// The most recent username (for quick display)
        /// </summary>
        public string LastUser { get; set; } = string.Empty;

        /// <summary>
        /// Whether the last user is currently logged in
        /// </summary>
        public bool IsLoggedIn { get; set; }

        /// <summary>
        /// When the user last logged in/used the profile
        /// </summary>
        public string LastLoginTime { get; set; } = string.Empty;

        /// <summary>
        /// All user profiles found on the computer
        /// </summary>
        public List<UserProfileInfo>? AllProfiles { get; set; }

        /// <summary>
        /// Number of profiles found
        /// </summary>
        public int ProfileCount => AllProfiles?.Count ?? 0;

        /// <summary>
        /// Error message if the query failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Icon for the status
        /// </summary>
        public string StatusIcon => Status switch
        {
            "Success" => "CheckCircle",
            "Offline" => "LanDisconnect",
            "Failed" => "CloseCircle",
            "Pending" => "CircleOutline",
            _ => "HelpCircle"
        };

        /// <summary>
        /// Color for the status icon
        /// </summary>
        public string StatusColor => Status switch
        {
            "Success" => "#4CAF50",
            "Offline" => "#9E9E9E",
            "Failed" => "#F44336",
            "Pending" => "#2196F3",
            _ => "#9E9E9E"
        };
    }
}
