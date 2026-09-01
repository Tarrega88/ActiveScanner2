using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents an Active Directory object (computer, user, or printer)
    /// </summary>
    public class AdObjectInfo : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Common properties
        public string Name { get; set; } = string.Empty;
        
        private string? _description;
        public string? Description 
        { 
            get => _description; 
            set 
            { 
                if (_description != value) 
                { 
                    _description = value; 
                    OnPropertyChanged(); 
                } 
            } 
        }
        
        private bool _isEditingDescription;
        /// <summary>
        /// Indicates if the description field is currently being edited in the UI
        /// </summary>
        public bool IsEditingDescription
        {
            get => _isEditingDescription;
            set
            {
                if (_isEditingDescription != value)
                {
                    _isEditingDescription = value;
                    OnPropertyChanged();
                }
            }
        }
        
        private bool _isEnabled = true;
        public bool IsEnabled 
        { 
            get => _isEnabled; 
            set 
            { 
                if (_isEnabled != value) 
                { 
                    _isEnabled = value; 
                    OnPropertyChanged(); 
                } 
            } 
        }
        
        private DateTime? _lastLogon;
        public DateTime? LastLogon 
        { 
            get => _lastLogon; 
            set 
            { 
                if (_lastLogon != value) 
                { 
                    _lastLogon = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(LastActivity));
                } 
            } 
        }
        
        public DateTime? WhenCreated { get; set; }
        public DateTime? WhenChanged { get; set; }
        public string? ManagedBy { get; set; }
        public string? DistinguishedName { get; set; }
        public string? SourcePath { get; set; }
        
        /// <summary>
        /// The AD objectGUID - unique identifier for this object in Active Directory
        /// </summary>
        public Guid? ObjectGuid { get; set; }
        
        /// <summary>
        /// The type of AD object from objectCategory (e.g., "Computer", "Person", "Printer")
        /// </summary>
        public string? Type { get; set; }
        
        /// <summary>
        /// The type of AD object (Computer, User, Printer) - derived from Type property
        /// </summary>
        public AdObjectType ObjectType => Type?.ToLowerInvariant() switch
        {
            "computer" => AdObjectType.Computer,
            "person" => AdObjectType.User,
            "printer" or "printqueue" => AdObjectType.Printer,
            _ => AdObjectType.Computer  // Default fallback
        };

        // Computer-specific properties
        public string? DnsHostName { get; set; }
        public string? OperatingSystem { get; set; }
        public string? OperatingSystemVersion { get; set; }
        public string? Location { get; set; }

        private string? _ipAddress;
        /// <summary>
        /// Resolved IP address from DNS lookup (populated asynchronously after query)
        /// </summary>
        public string? IpAddress 
        { 
            get => _ipAddress; 
            set 
            { 
                if (_ipAddress != value) 
                { 
                    _ipAddress = value; 
                    OnPropertyChanged(); 
                } 
            } 
        }

        private bool _isIpStale;
        /// <summary>
        /// Whether the displayed IP address is stale (from history, DNS failed to resolve)
        /// </summary>
        public bool IsIpStale
        {
            get => _isIpStale;
            set
            {
                if (_isIpStale != value)
                {
                    _isIpStale = value;
                    OnPropertyChanged();
                }
            }
        }

        private Dictionary<string, DateTime>? _knownIpAddresses;
        /// <summary>
        /// Known IP addresses for this computer (IP -> last seen timestamp)
        /// </summary>
        public Dictionary<string, DateTime>? KnownIpAddresses 
        { 
            get => _knownIpAddresses; 
            set 
            { 
                if (_knownIpAddresses != value) 
                { 
                    _knownIpAddresses = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(HasIpHistory));
                } 
            } 
        }

        /// <summary>
        /// Whether this object has IP history
        /// </summary>
        public bool HasIpHistory => KnownIpAddresses?.Count > 0;

        private string? _lastUser;
        /// <summary>
        /// Last/current logged-on user (from WMI)
        /// </summary>
        public string? LastUser 
        { 
            get => _lastUser; 
            set 
            { 
                if (_lastUser != value) 
                { 
                    _lastUser = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(LastUserDisplay));
                    OnPropertyChanged(nameof(HasLastUserData));
                } 
            } 
        }

        private DateTime? _lastUserActiveAt;
        /// <summary>
        /// When the most recent user was last active on this computer (from WMI profile LastUseTime)
        /// </summary>
        public DateTime? LastUserActiveAt 
        { 
            get => _lastUserActiveAt; 
            set 
            { 
                if (_lastUserActiveAt != value) 
                { 
                    _lastUserActiveAt = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(LastUserDate));
                    OnPropertyChanged(nameof(LastUserDisplay));
                    OnPropertyChanged(nameof(LastActivity));
                } 
            } 
        }
        
        private DateTime? _lastUserQueriedAt;
        /// <summary>
        /// When the last user data was queried (used for staleness indicator)
        /// </summary>
        public DateTime? LastUserQueriedAt 
        { 
            get => _lastUserQueriedAt; 
            set 
            { 
                if (_lastUserQueriedAt != value) 
                { 
                    _lastUserQueriedAt = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(LastUserIsStale));
                    OnPropertyChanged(nameof(LastUserAgeText));
                    OnPropertyChanged(nameof(LastUserDate));
                    OnPropertyChanged(nameof(LastUserDisplay));
                    OnPropertyChanged(nameof(LastActivity));
                } 
            } 
        }

        private DateTime? _lastNetworkActivity;
        /// <summary>
        /// When the computer was last confirmed reachable by a successful network operation
        /// </summary>
        public DateTime? LastNetworkActivity 
        { 
            get => _lastNetworkActivity; 
            set 
            { 
                if (_lastNetworkActivity != value) 
                { 
                    _lastNetworkActivity = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(LastActivity));
                } 
            } 
        }
        
        /// <summary>
        /// Returns the most recent timestamp when this machine was known to be alive:
        /// max(LastLogon, LastUserActiveAt, LastNetworkActivity)
        /// </summary>
        public DateTime? LastActivity
        {
            get
            {
                DateTime? max = null;
                if (LastLogon.HasValue && (!max.HasValue || LastLogon.Value > max.Value))
                    max = LastLogon;
                if (LastUserActiveAt.HasValue && (!max.HasValue || LastUserActiveAt.Value > max.Value))
                    max = LastUserActiveAt;
                if (LastNetworkActivity.HasValue && (!max.HasValue || LastNetworkActivity.Value > max.Value))
                    max = LastNetworkActivity;
                return max;
            }
        }
        
        /// <summary>
        /// Whether the cached last user data is stale (older than 30 days)
        /// </summary>
        public bool LastUserIsStale => LastUserQueriedAt.HasValue && 
            (DateTime.UtcNow - LastUserQueriedAt.Value).TotalDays > 30;
        
        /// <summary>
        /// Human-readable text for when the last user was queried
        /// </summary>
        public string? LastUserAgeText
        {
            get
            {
                if (!LastUserQueriedAt.HasValue) return null;
                var elapsed = DateTime.UtcNow - LastUserQueriedAt.Value;
                if (elapsed.TotalMinutes < 5) return "just now";
                if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes}m ago";
                if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours}h ago";
                if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays}d ago";
                if (elapsed.TotalDays < 30) return $"{(int)(elapsed.TotalDays / 7)}w ago";
                return $"{(int)(elapsed.TotalDays / 30)}mo ago";
            }
        }
        
        /// <summary>
        /// The effective date for the Last User column (activity time, falling back to query time).
        /// Used for sorting.
        /// </summary>
        public DateTime? LastUserDate => LastUserActiveAt ?? LastUserQueriedAt;

        /// <summary>
        /// Formatted display text for the Last User column: "{date} - {username}"
        /// </summary>
        public string? LastUserDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(LastUser)) return null;
                // If it's an error/status message (starts with parenthesis), show as-is
                if (LastUser.StartsWith("(")) return LastUser;
                // Format as "date - username" using actual user activity time, fall back to query time
                if (LastUserDate.HasValue)
                {
                    var localTime = LastUserDate.Value.Kind == DateTimeKind.Utc 
                        ? LastUserDate.Value.ToLocalTime() 
                        : LastUserDate.Value;
                    return $"{localTime:M/d/yy} - {LastUser}";
                }
                return LastUser;
            }
        }

        /// <summary>
        /// Returns true if there's valid last user data (a real username, not a status message)
        /// </summary>
        public bool HasLastUserData => !string.IsNullOrEmpty(LastUser) && !LastUser.StartsWith("(");
        
        private List<UserProfileInfo>? _lastUserProfiles;
        /// <summary>
        /// All user profiles from the computer (populated when LastUser query succeeds)
        /// </summary>
        public List<UserProfileInfo>? LastUserProfiles 
        { 
            get => _lastUserProfiles; 
            set 
            { 
                if (_lastUserProfiles != value) 
                { 
                    _lastUserProfiles = value; 
                    OnPropertyChanged(); 
                } 
            } 
        }

        // User-specific properties
        public string? SamAccountName { get; set; }
        public string? UserPrincipalName { get; set; }
        public string? DisplayName { get; set; }
        public string? GivenName { get; set; }  // First name
        public string? Surname { get; set; }     // Last name
        public string? Mail { get; set; }
        public string? Title { get; set; }
        public string? Department { get; set; }
        public string? Company { get; set; }
        public string? Manager { get; set; }
        public string? TelephoneNumber { get; set; }
        public string? Mobile { get; set; }
        public string? Office { get; set; }      // physicalDeliveryOfficeName
        public DateTime? PasswordLastSet { get; set; }
        public DateTime? AccountExpires { get; set; }
        public bool PasswordNeverExpires { get; set; }
        public bool PasswordExpired { get; set; }
        public bool LockedOut { get; set; }

        private string? _snowId;
        /// <summary>
        /// ServiceNow sys_id for this user (populated from cache)
        /// </summary>
        public string? SnowId
        {
            get => _snowId;
            set
            {
                if (_snowId != value)
                {
                    _snowId = value;
                    OnPropertyChanged();
                }
            }
        }

        private List<UserMachineEntry>? _computerHistory;
        private string? _computerHistoryDisplay;
        private bool _hasComputerHistory;

        /// <summary>
        /// Computers this user has logged into (populated from cache)
        /// </summary>
        public List<UserMachineEntry>? ComputerHistory
        {
            get => _computerHistory;
            set
            {
                if (_computerHistory != value)
                {
                    _computerHistory = value;
                    // Pre-compute cached values to avoid repeated LINQ on every property access
                    ComputeComputerHistoryCache();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ComputerHistoryDisplay));
                    OnPropertyChanged(nameof(HasComputerHistory));
                }
            }
        }

        /// <summary>
        /// Pre-computes and caches ComputerHistoryDisplay and HasComputerHistory values.
        /// Uses single-pass O(n) algorithm with zero allocations.
        /// </summary>
        private void ComputeComputerHistoryCache()
        {
            if (_computerHistory == null || _computerHistory.Count == 0)
            {
                _computerHistoryDisplay = null;
                _hasComputerHistory = false;
                return;
            }

            // Single pass: find most recent and count valid entries
            UserMachineEntry? mostRecent = null;
            int validCount = 0;
            foreach (var entry in _computerHistory)
            {
                if (entry.LastSeen > DateTime.MinValue)
                {
                    validCount++;
                    if (mostRecent == null || entry.LastSeen > mostRecent.LastSeen)
                        mostRecent = entry;
                }
            }

            if (validCount == 0 || mostRecent == null)
            {
                _computerHistoryDisplay = null;
                _hasComputerHistory = false;
                return;
            }

            _hasComputerHistory = true;
            _computerHistoryDisplay = validCount == 1
                ? mostRecent.ComputerName
                : $"{mostRecent.ComputerName} (+{validCount - 1})";
        }

        /// <summary>
        /// Whether this user has any computer history (where they were actually seen logged in)
        /// </summary>
        public bool HasComputerHistory => _hasComputerHistory;

        /// <summary>
        /// Display string for Computer History column showing most recent computer (where user was seen logged in)
        /// </summary>
        public string? ComputerHistoryDisplay => _computerHistoryDisplay;

        // Printer-specific properties
        public string? PrinterName { get; set; }      // printerName
        public string? ServerName { get; set; }       // serverName
        public string? ShareName { get; set; }        // printShareName
        public string? PortName { get; set; }         // portName
        public string? DriverName { get; set; }       // driverName
        public string? PrinterModel { get; set; }     // driverName often contains model info
        public string? UNCName { get; set; }          // uNCName (\\server\share)
        public int? PrinterPriority { get; set; }     // priority
        public string? PrinterStatus { get; set; }    // Derived from attributes

        /// <summary>
        /// Gets the best target address for network operations
        /// </summary>
        public string TargetAddress => DnsHostName ?? ServerName ?? Name;

        /// <summary>
        /// Gets display string for object type (uses Type property directly)
        /// </summary>
        public string ObjectTypeDisplay => Type ?? "Unknown";

        public override string ToString() => Name;
    }

    public enum AdObjectType
    {
        Computer,
        User,
        Printer
    }
}
