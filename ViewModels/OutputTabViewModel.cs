using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ActiveScanner.Models;
//
namespace ActiveScanner.ViewModels
{
    public enum OutputTabType
    {
        Results,
        Computers,
        Users,
        Printers,
        Vulnerabilities,
    }

    public enum SortDirection
    {
        None,
        Ascending,
        Descending
    }

    public enum FilterMode
    {
        Contains,
        Starts,
        Ends,
        Equals
    }

    public partial class OutputTabViewModel : ObservableObject
    {
        /// <summary>
        /// Event raised when column visibility changes (ShowsUsers, ShowsComputers, ShowsPrinters)
        /// Used by MainWindow to trigger DataGrid layout refresh.
        /// </summary>
        public event EventHandler? ColumnVisibilityChanged;

        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private OutputTabType _type;

        [ObservableProperty]
        private bool _isRunning;

        /// <summary>
        /// Current operation name (e.g., "Traceroute", "Pathping") for progress display
        /// </summary>
        [ObservableProperty]
        private string _currentOperationName = string.Empty;

        /// <summary>
        /// Current operation progress (e.g., "3/7") for progress display
        /// </summary>
        [ObservableProperty]
        private string _currentOperationProgress = string.Empty;

        [ObservableProperty]
        private int _resultCount;

        [ObservableProperty]
        private int _filteredCount;

        [ObservableProperty]
        private bool _canClose = true;

        /// <summary>
        /// If this tab displays a custom group, this is the group ID
        /// </summary>
        [ObservableProperty]
        private string? _customGroupId;

        /// <summary>
        /// If this tab displays a domain target group, this is the group ID
        /// </summary>
        [ObservableProperty]
        private string? _targetGroupId;

        /// <summary>
        /// If this tab displays a custom group, this is the group type
        /// </summary>
        [ObservableProperty]
        private Models.CustomGroupType? _customGroupType;

        /// <summary>
        /// The source path for this tab's data (DN, domain name, group name, or "Network")
        /// Used for organizing export folder structure
        /// </summary>
        [ObservableProperty]
        private string? _sourcePath;

        /// <summary>
        /// For a Vulnerabilities tab, the scan view model that owns the vulnerability data and
        /// remediation actions. Null for all other tab types.
        /// </summary>
        [ObservableProperty]
        private VulnerabilityScanViewModel? _vulnerabilityScan;

        /// <summary>
        /// Gets the icon kind for this tab based on type and custom group type
        /// </summary>
        public string IconKind => Type switch
        {
            OutputTabType.Computers => "DesktopClassic",
            OutputTabType.Users => "AccountMultiple",
            OutputTabType.Printers => "Printer",
            OutputTabType.Vulnerabilities => "ShieldBug",
            OutputTabType.Results when CustomGroupType == Models.CustomGroupType.Computer => "DesktopClassic",
            OutputTabType.Results when CustomGroupType == Models.CustomGroupType.User => "AccountMultiple",
            OutputTabType.Results when CustomGroupType == Models.CustomGroupType.Printer => "Printer",
            OutputTabType.Results => "FolderMultiple",
            _ => "Folder"
        };

        partial void OnCustomGroupTypeChanged(Models.CustomGroupType? value)
        {
            OnPropertyChanged(nameof(IconKind));
        }

        partial void OnTypeChanged(OutputTabType value)
        {
            OnPropertyChanged(nameof(IconKind));
        }

        /// <summary>
        /// List of DNs for members that were not found in AD
        /// </summary>
        [ObservableProperty]
        private List<string>? _missingMemberDNs;

        /// <summary>
        /// Whether this tab has missing members (for showing Remove Missing button)
        /// </summary>
        public bool HasMissingMembers => MissingMemberDNs?.Count > 0;

        /// <summary>
        /// Number of missing members
        /// </summary>
        public int MissingMemberCount => MissingMemberDNs?.Count ?? 0;

        /// <summary>
        /// Whether the tab name is being edited (for rename functionality)
        /// </summary>
        [ObservableProperty]
        private bool _isEditingName;

        /// <summary>
        /// Temporary name while editing
        /// </summary>
        [ObservableProperty]
        private string _editingName = string.Empty;

        /// <summary>
        /// Whether the user has custom named this tab (disables count suffix in header)
        /// </summary>
        [ObservableProperty]
        private bool _isCustomNamed;

        /// <summary>
        /// Whether this tab has any results to display
        /// </summary>
        [ObservableProperty]
        private bool _hasResults;

        /// <summary>
        /// Whether this tab displays computer results (for column visibility)
        /// </summary>
        [ObservableProperty]
        private bool _showsComputers;

        /// <summary>
        /// Whether this tab displays user results (for column visibility)
        /// </summary>
        [ObservableProperty]
        private bool _showsUsers;

        /// <summary>
        /// Whether this tab displays printer results (for column visibility)
        /// </summary>
        [ObservableProperty]
        private bool _showsPrinters;

        /// <summary>
        /// Whether this tab displays mixed results (multiple object types)
        /// </summary>
        public bool ShowsMixed => (ShowsComputers ? 1 : 0) + (ShowsUsers ? 1 : 0) + (ShowsPrinters ? 1 : 0) > 1;

        /// <summary>
        /// When this tab's results were last updated
        /// </summary>
        [ObservableProperty]
        private DateTime? _lastUpdated;

        /// <summary>
        /// Gets a formatted time string for when results were last updated
        /// </summary>
        public string LastUpdatedDisplay
        {
            get
            {
                if (!LastUpdated.HasValue) return string.Empty;
                
                // Show time only if today, otherwise show date and time
                if (LastUpdated.Value.Date == DateTime.Today)
                    return LastUpdated.Value.ToString("h:mm tt");
                else
                    return LastUpdated.Value.ToString("M/d h:mm tt");
            }
        }

        /// <summary>
        /// Whether this tab was loaded from cache
        /// </summary>
        [ObservableProperty]
        private bool _isFromCache;

        // Per-column filter properties
        [ObservableProperty]
        private string _filterName = string.Empty;

        [ObservableProperty]
        private string _filterDescription = string.Empty;

        [ObservableProperty]
        private bool? _filterEnabled;  // null = no filter, true = enabled only, false = disabled only

        [ObservableProperty]
        private string _filterSamAccountName = string.Empty;

        [ObservableProperty]
        private string _filterEmail = string.Empty;

        [ObservableProperty]
        private string _filterTitle = string.Empty;

        [ObservableProperty]
        private string _filterDepartment = string.Empty;

        [ObservableProperty]
        private string _filterManager = string.Empty;

        [ObservableProperty]
        private string _filterSource = string.Empty;

        [ObservableProperty]
        private string _filterLastUser = string.Empty;

        [ObservableProperty]
        private string _filterComputerHistory = string.Empty;

        [ObservableProperty]
        private string _filterSnowId = string.Empty;

        [ObservableProperty]
        private string _filterIpAddress = string.Empty;

        [ObservableProperty]
        private int? _filterLastActivityDays;  // Within X days

        [ObservableProperty]
        private bool _filterLastActivityOlderThan = true;  // true = older than, false = within

        public string FilterLastActivityDirectionText => FilterLastActivityOlderThan ? ">" : "<";

        [ObservableProperty]
        private int? _filterLastLogonDays;

        [ObservableProperty]
        private bool _filterLastLogonOlderThan = true;

        public string FilterLastLogonDirectionText => FilterLastLogonOlderThan ? ">" : "<";

        // Printer-specific filter properties
        [ObservableProperty]
        private string _filterShareName = string.Empty;

        [ObservableProperty]
        private string _filterDriverName = string.Empty;

        [ObservableProperty]
        private string _filterUncName = string.Empty;

        [ObservableProperty]
        private string _filterLocation = string.Empty;

        // Filter mode properties (Contains, Starts, Ends, Equals)
        [ObservableProperty]
        private FilterMode _filterNameMode = FilterMode.Contains;

        [ObservableProperty]
        private FilterMode _filterDescriptionMode = FilterMode.Contains;

        [ObservableProperty]
        private FilterMode _filterSamAccountNameMode = FilterMode.Contains;

        [ObservableProperty]
        private FilterMode _filterEmailMode = FilterMode.Contains;

        [ObservableProperty]
        private FilterMode _filterTitleMode = FilterMode.Contains;

        [ObservableProperty]
        private FilterMode _filterDepartmentMode = FilterMode.Contains;

        [ObservableProperty]
        private FilterMode _filterManagerMode = FilterMode.Contains;

        [ObservableProperty]
        private FilterMode _filterSourceMode = FilterMode.Contains;

        [ObservableProperty]
        private FilterMode _filterLastUserMode = FilterMode.Contains;

        [ObservableProperty]
        private FilterMode _filterComputerHistoryMode = FilterMode.Contains;

        [ObservableProperty]
        private FilterMode _filterSnowIdMode = FilterMode.Contains;

        [ObservableProperty]
        private FilterMode _filterIpAddressMode = FilterMode.Contains;

        [ObservableProperty]
        private FilterMode _filterShareNameMode = FilterMode.Contains;

        [ObservableProperty]
        private FilterMode _filterDriverNameMode = FilterMode.Contains;

        [ObservableProperty]
        private FilterMode _filterUncNameMode = FilterMode.Contains;

        [ObservableProperty]
        private FilterMode _filterLocationMode = FilterMode.Contains;

        /// <summary>
        /// Available filter modes for ComboBox binding
        /// </summary>
        public static IReadOnlyList<FilterMode> FilterModes { get; } = Enum.GetValues<FilterMode>();

        // Legacy filter properties (keeping for backwards compatibility)
        [ObservableProperty]
        private string _filterNameStartsWith = string.Empty;

        [ObservableProperty]
        private string _filterNameEndsWith = string.Empty;

        [ObservableProperty]
        private string _filterNameContains = string.Empty;

        // Tri-state filter checkboxes:
        // false (empty) = no filter, true (check) = filter for true, null (dash) = filter for false
        [ObservableProperty]
        private bool? _filterHasDescription = false;

        [ObservableProperty]
        private bool? _filterIsEnabled = false;

        [ObservableProperty]
        private int? _filterOlderThanDays;

        [ObservableProperty]
        private bool _isFilterExpanded;

        // Sorting
        [ObservableProperty]
        private string _sortColumn = string.Empty;

        [ObservableProperty]
        private SortDirection _sortDirection = SortDirection.None;

        // Results data - unified AdObjectInfo for all object types
        private List<AdObjectInfo>? _allAdObjectResults;
        
        [ObservableProperty]
        private List<AdObjectInfo>? _adObjectResults;

        /// <summary>
        /// Start editing the tab name
        /// </summary>
        [RelayCommand]
        private void StartEditingName()
        {
            EditingName = Name;
            IsEditingName = true;
        }

        /// <summary>
        /// Confirm the name edit
        /// </summary>
        [RelayCommand]
        private void ConfirmNameEdit()
        {
            if (!string.IsNullOrWhiteSpace(EditingName))
            {
                Name = EditingName.Trim();
                IsCustomNamed = true;
                OnPropertyChanged(nameof(Header));
            }
            IsEditingName = false;
        }

        /// <summary>
        /// Cancel the name edit
        /// </summary>
        [RelayCommand]
        private void CancelNameEdit()
        {
            EditingName = Name;
            IsEditingName = false;
        }

        /// <summary>
        /// Notifies property changes for result-dependent computed properties
        /// </summary>
        public void NotifyResultsChanged()
        {
            OnPropertyChanged(nameof(Header));
        }

        public string Header => IsRunning ? $"{Name} ⏳" : Name;

        /// <summary>
        /// When Name changes, notify that Header also changed
        /// </summary>
        partial void OnNameChanged(string value)
        {
            OnPropertyChanged(nameof(Header));
        }

        public bool IsFilterActive =>
            !string.IsNullOrEmpty(FilterName) ||
            !string.IsNullOrEmpty(FilterDescription) ||
            FilterEnabled.HasValue ||
            !string.IsNullOrEmpty(FilterSamAccountName) ||
            !string.IsNullOrEmpty(FilterEmail) ||
            !string.IsNullOrEmpty(FilterTitle) ||
            !string.IsNullOrEmpty(FilterDepartment) ||
            !string.IsNullOrEmpty(FilterManager) ||
            !string.IsNullOrEmpty(FilterSource) ||
            !string.IsNullOrEmpty(FilterLastUser) ||
            !string.IsNullOrEmpty(FilterComputerHistory) ||
            !string.IsNullOrEmpty(FilterSnowId) ||
            !string.IsNullOrEmpty(FilterIpAddress) ||
            FilterLastActivityDays.HasValue ||
            FilterLastLogonDays.HasValue ||
            // Printer filters
            !string.IsNullOrEmpty(FilterShareName) ||
            !string.IsNullOrEmpty(FilterDriverName) ||
            !string.IsNullOrEmpty(FilterUncName) ||
            !string.IsNullOrEmpty(FilterLocation) ||
            // Legacy properties
            !string.IsNullOrEmpty(FilterNameStartsWith) ||
            !string.IsNullOrEmpty(FilterNameEndsWith) ||
            !string.IsNullOrEmpty(FilterNameContains) ||
            FilterHasDescription != false ||
            FilterIsEnabled != false ||
            FilterOlderThanDays.HasValue;

        public OutputTabViewModel(string name, OutputTabType type)
        {
            _name = name;
            _type = type;
            _canClose = true; // Closable by default; domain scanning tabs will explicitly set to false
            
            // Set initial visibility based on tab type
            UpdateColumnVisibility();
        }

        /// <summary>
        /// Updates ShowsComputers, ShowsUsers, ShowsPrinters based on tab type and data content
        /// </summary>
        private void UpdateColumnVisibility()
        {
            if (Type == OutputTabType.Computers)
            {
                ShowsComputers = true;
                ShowsUsers = false;
                ShowsPrinters = false;
            }
            else if (Type == OutputTabType.Users)
            {
                ShowsComputers = false;
                ShowsUsers = true;
                ShowsPrinters = false;
            }
            else if (Type == OutputTabType.Printers)
            {
                ShowsComputers = false;
                ShowsUsers = false;
                ShowsPrinters = true;
            }
            else if (Type == OutputTabType.Results)
            {
                // Check actual data content - single pass instead of 3 separate .Any() calls
                var hasComputers = false;
                var hasUsers = false;
                var hasPrinters = false;
                
                if (_allAdObjectResults != null)
                {
                    foreach (var obj in _allAdObjectResults)
                    {
                        switch (obj.ObjectType)
                        {
                            case AdObjectType.Computer: hasComputers = true; break;
                            case AdObjectType.User: hasUsers = true; break;
                            case AdObjectType.Printer: hasPrinters = true; break;
                        }
                        // Early exit if we've found all types
                        if (hasComputers && hasUsers && hasPrinters) break;
                    }
                }
                
                ShowsComputers = hasComputers;
                ShowsUsers = hasUsers;
                ShowsPrinters = hasPrinters;
            }
            else
            {
                // For other tab types (Ping, DNS, etc.), hide all
                ShowsComputers = false;
                ShowsUsers = false;
                ShowsPrinters = false;
            }
            
            HasResults = (_allAdObjectResults?.Count ?? 0) > 0;
            OnPropertyChanged(nameof(ShowsMixed));
            
            // Notify listeners that column visibility may have changed
            ColumnVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Sets the AD object results (unified for users, computers, and printers)
        /// </summary>
        public void SetAdObjectResults(List<AdObjectInfo> results, DateTime? lastUpdated = null, bool isFromCache = false)
        {
            _allAdObjectResults = results?.ToList();
            ResultCount = _allAdObjectResults?.Count ?? 0;
            LastUpdated = lastUpdated ?? DateTime.Now;
            IsFromCache = isFromCache;
            OnPropertyChanged(nameof(LastUpdatedDisplay));
            ApplyAdObjectFilterAndSort();
            UpdateColumnVisibility();
        }

        /// <summary>
        /// Forces a refresh of the AdObject grid by re-applying filter and sort.
        /// Use this when objects in the list have been modified but the list itself hasn't changed.
        /// </summary>
        public void RefreshAdObjectResults()
        {
            ApplyAdObjectFilterAndSort();
        }

        /// <summary>
        /// Removes an AD object from this tab's result set, updating BOTH the full backing
        /// list and the displayed/filtered list, then rebuilds the grid so the DataGrid
        /// stays in sync. Use this after an object is deleted from Active Directory to avoid
        /// index mismatches between the displayed list and the backing list.
        /// </summary>
        /// <returns>True if the object was found and removed; otherwise false.</returns>
        public bool RemoveAdObject(AdObjectInfo adObject)
        {
            if (adObject == null || _allAdObjectResults == null)
                return false;

            bool removed = _allAdObjectResults.Remove(adObject);
            if (removed)
            {
                ResultCount = _allAdObjectResults.Count;
                HasResults = _allAdObjectResults.Count > 0;
                // Rebuild the displayed list (creates a new list reference so the bound
                // DataGrid refreshes) and keeps the filtered view consistent.
                ApplyAdObjectFilterAndSort();
            }
            return removed;
        }

        /// <summary>
        /// Checks if a value matches a filter string where each line is an OR term.
        /// Example: a filter of "HR\nIT\nSales" matches if the value contains "HR" OR "IT" OR "Sales".
        /// The legacy "||" separator is still accepted for backwards compatibility.
        /// </summary>
        private static bool MatchesFilter(string? value, string filter)
        {
            return MatchesFilterWithMode(value, filter, FilterMode.Contains);
        }

        /// <summary>
        /// Checks if a value matches a filter string using the specified mode. Each line is an
        /// OR term; the legacy "||" separator is also accepted for backwards compatibility.
        /// </summary>
        private static bool MatchesFilterWithMode(string? value, string filter, FilterMode mode)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(filter))
                return false;

            // Split on newlines (primary) and the legacy "||" operator, then check if any part matches
            var parts = filter.Split(new[] { "\r\n", "\n", "\r", "||" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                bool matches = mode switch
                {
                    FilterMode.Contains => value.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0,
                    FilterMode.Starts => value.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase),
                    FilterMode.Ends => value.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase),
                    FilterMode.Equals => value.Equals(trimmed, StringComparison.OrdinalIgnoreCase),
                    _ => value.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0
                };

                if (matches)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if a value matches a filter string with OR support, or the filter is empty.
        /// Returns true if filter is empty (no filtering), otherwise checks for match.
        /// </summary>
        private static bool MatchesFilterOrEmpty(string? value, string filter)
        {
            if (string.IsNullOrEmpty(filter))
                return true;
            return MatchesFilter(value, filter);
        }

        /// <summary>
        /// Checks if a value starts with any of the filter parts (supports || OR operator).
        /// </summary>
        private static bool MatchesStartsWith(string? value, string filter)
        {
            return MatchesFilterWithMode(value, filter, FilterMode.Starts);
        }

        /// <summary>
        /// Checks if a value ends with any of the filter parts (supports || OR operator).
        /// </summary>
        private static bool MatchesEndsWith(string? value, string filter)
        {
            return MatchesFilterWithMode(value, filter, FilterMode.Ends);
        }

        /// <summary>
        /// Applies current filter and sort settings to AdObject results
        /// </summary>
        private void ApplyAdObjectFilterAndSort()
        {
            if (_allAdObjectResults == null)
            {
                AdObjectResults = null;
                FilteredCount = 0;
                return;
            }

            var filtered = _allAdObjectResults.AsEnumerable();

            // New per-column filters (each line is an OR term; legacy || also supported) and filter modes
            if (!string.IsNullOrEmpty(FilterName))
            {
                filtered = filtered.Where(c => 
                    MatchesFilterWithMode(c.Name, FilterName, FilterNameMode) ||
                    MatchesFilterWithMode(c.DisplayName, FilterName, FilterNameMode));
            }

            if (!string.IsNullOrEmpty(FilterDescription))
            {
                filtered = filtered.Where(c => MatchesFilterWithMode(c.Description, FilterDescription, FilterDescriptionMode));
            }

            if (FilterEnabled.HasValue)
            {
                filtered = filtered.Where(c => c.IsEnabled == FilterEnabled.Value);
            }

            if (!string.IsNullOrEmpty(FilterSamAccountName))
            {
                filtered = filtered.Where(c => MatchesFilterWithMode(c.SamAccountName, FilterSamAccountName, FilterSamAccountNameMode));
            }

            if (!string.IsNullOrEmpty(FilterEmail))
            {
                filtered = filtered.Where(c => MatchesFilterWithMode(c.Mail, FilterEmail, FilterEmailMode));
            }

            if (!string.IsNullOrEmpty(FilterTitle))
            {
                filtered = filtered.Where(c => MatchesFilterWithMode(c.Title, FilterTitle, FilterTitleMode));
            }

            if (!string.IsNullOrEmpty(FilterDepartment))
            {
                filtered = filtered.Where(c => MatchesFilterWithMode(c.Department, FilterDepartment, FilterDepartmentMode));
            }

            if (!string.IsNullOrEmpty(FilterManager))
            {
                filtered = filtered.Where(c => MatchesFilterWithMode(c.Manager, FilterManager, FilterManagerMode));
            }

            // Source/DistinguishedName filter (for Users)
            if (!string.IsNullOrEmpty(FilterSource))
            {
                filtered = filtered.Where(c => 
                    MatchesFilterWithMode(c.DistinguishedName, FilterSource, FilterSourceMode) ||
                    MatchesFilterWithMode(c.SourcePath, FilterSource, FilterSourceMode));
            }

            // Last User filter (for Computers)
            if (!string.IsNullOrEmpty(FilterLastUser))
            {
                filtered = filtered.Where(c => MatchesFilterWithMode(c.LastUser, FilterLastUser, FilterLastUserMode));
            }

            // IP Address filter (for Computers)
            if (!string.IsNullOrEmpty(FilterIpAddress))
            {
                filtered = filtered.Where(c => MatchesFilterWithMode(c.IpAddress, FilterIpAddress, FilterIpAddressMode));
            }

            // Computer History filter (for Users)
            if (!string.IsNullOrEmpty(FilterComputerHistory))
            {
                filtered = filtered.Where(c => MatchesFilterWithMode(c.ComputerHistoryDisplay, FilterComputerHistory, FilterComputerHistoryMode));
            }

            // SNOW ID filter (for Users)
            if (!string.IsNullOrEmpty(FilterSnowId))
            {
                filtered = filtered.Where(c => MatchesFilterWithMode(c.SnowId, FilterSnowId, FilterSnowIdMode));
            }

            // Printer-specific filters
            if (!string.IsNullOrEmpty(FilterShareName))
            {
                filtered = filtered.Where(c => MatchesFilterWithMode(c.ShareName, FilterShareName, FilterShareNameMode));
            }

            if (!string.IsNullOrEmpty(FilterDriverName))
            {
                filtered = filtered.Where(c => MatchesFilterWithMode(c.DriverName, FilterDriverName, FilterDriverNameMode));
            }

            if (!string.IsNullOrEmpty(FilterUncName))
            {
                filtered = filtered.Where(c => MatchesFilterWithMode(c.UNCName, FilterUncName, FilterUncNameMode));
            }

            if (!string.IsNullOrEmpty(FilterLocation))
            {
                filtered = filtered.Where(c => MatchesFilterWithMode(c.Location, FilterLocation, FilterLocationMode));
            }

            if (FilterLastActivityDays.HasValue && FilterLastActivityDays.Value > 0)
            {
                var cutoffDate = DateTime.Now.AddDays(-FilterLastActivityDays.Value);
                if (FilterLastActivityOlderThan)
                {
                    filtered = filtered.Where(c => !c.LastActivity.HasValue || c.LastActivity.Value < cutoffDate);
                }
                else
                {
                    filtered = filtered.Where(c => c.LastActivity.HasValue && c.LastActivity.Value > cutoffDate);
                }
            }

            if (FilterLastLogonDays.HasValue && FilterLastLogonDays.Value > 0)
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-FilterLastLogonDays.Value);
                if (FilterLastLogonOlderThan)
                {
                    filtered = filtered.Where(c => !c.LastLogon.HasValue || c.LastLogon.Value < cutoffDate);
                }
                else
                {
                    filtered = filtered.Where(c => c.LastLogon.HasValue && c.LastLogon.Value > cutoffDate);
                }
            }

            // Legacy filters (for backwards compatibility - support newline and || OR operators)
            if (!string.IsNullOrEmpty(FilterNameStartsWith))
            {
                filtered = filtered.Where(c => 
                    MatchesStartsWith(c.Name, FilterNameStartsWith) ||
                    MatchesStartsWith(c.DisplayName, FilterNameStartsWith) ||
                    MatchesStartsWith(c.SamAccountName, FilterNameStartsWith));
            }

            if (!string.IsNullOrEmpty(FilterNameEndsWith))
            {
                filtered = filtered.Where(c => 
                    MatchesEndsWith(c.Name, FilterNameEndsWith) ||
                    MatchesEndsWith(c.DisplayName, FilterNameEndsWith) ||
                    MatchesEndsWith(c.SamAccountName, FilterNameEndsWith));
            }

            if (!string.IsNullOrEmpty(FilterNameContains))
            {
                filtered = filtered.Where(c => 
                    MatchesFilter(c.Name, FilterNameContains) ||
                    MatchesFilter(c.DisplayName, FilterNameContains) ||
                    MatchesFilter(c.SamAccountName, FilterNameContains) ||
                    MatchesFilter(c.Mail, FilterNameContains));
            }

            if (FilterHasDescription == true)
            {
                filtered = filtered.Where(c => !string.IsNullOrWhiteSpace(c.Description));
            }
            else if (FilterHasDescription == null)
            {
                filtered = filtered.Where(c => string.IsNullOrWhiteSpace(c.Description));
            }

            if (FilterIsEnabled == true)
            {
                filtered = filtered.Where(c => c.IsEnabled);
            }
            else if (FilterIsEnabled == null)
            {
                filtered = filtered.Where(c => !c.IsEnabled);
            }

            if (FilterOlderThanDays.HasValue && FilterOlderThanDays.Value > 0)
            {
                var cutoffDate = DateTime.Now.AddDays(-FilterOlderThanDays.Value);
                if (FilterLastActivityOlderThan)
                {
                    filtered = filtered.Where(c => !c.LastActivity.HasValue || c.LastActivity.Value < cutoffDate);
                }
                else
                {
                    filtered = filtered.Where(c => c.LastActivity.HasValue && c.LastActivity.Value > cutoffDate);
                }
            }

            // Apply sort
            if (!string.IsNullOrEmpty(SortColumn) && SortDirection != SortDirection.None)
            {
                filtered = SortByProperty(filtered, SortColumn, SortDirection == SortDirection.Ascending);
            }

            AdObjectResults = filtered.ToList();
            FilteredCount = AdObjectResults.Count;
            OnPropertyChanged(nameof(FilteredCount));
            OnPropertyChanged(nameof(ResultCount));
            OnPropertyChanged(nameof(IsFilterActive));
        }

        /// <summary>
        /// Universal property-based sorter using reflection. Works with any type and property.
        /// For the Last User column, status rows (e.g. "(Offline)", "(Timeout)") are treated as
        /// dateless so they don't interleave with real user entries, and all rows use an
        /// alphabetical tiebreaker on LastUser / Name.
        /// </summary>
        private static IEnumerable<T> SortByProperty<T>(IEnumerable<T> source, string propertyName, bool ascending)
        {
            bool isLastUserColumn = string.Equals(propertyName, nameof(AdObjectInfo.LastUserDate), StringComparison.OrdinalIgnoreCase)
                && typeof(AdObjectInfo).IsAssignableFrom(typeof(T));

            if (isLastUserColumn)
            {
                // Only count a date when there's real user data; status messages sort as dateless.
                static DateTime? EffectiveDate(AdObjectInfo? ad) =>
                    ad != null && ad.HasLastUserData ? ad.LastUserDate : null;

                var primary = ascending
                    ? source.OrderBy(x => EffectiveDate(x as AdObjectInfo))
                    : source.OrderByDescending(x => EffectiveDate(x as AdObjectInfo));

                return primary
                    .ThenBy(x => (x as AdObjectInfo)?.LastUser ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => (x as AdObjectInfo)?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            }

            var property = typeof(T).GetProperty(propertyName, 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            
            if (property == null)
                return source;

            return ascending
                ? source.OrderBy(x => property.GetValue(x))
                : source.OrderByDescending(x => property.GetValue(x));
        }

        /// <summary>
        /// Applies current filter and sort settings to the results
        /// </summary>
        [RelayCommand]
        public void ApplyFilterAndSort()
        {
            // All results now use AdObjectInfo
            ApplyAdObjectFilterAndSort();
        }

        /// <summary>
        /// Sort by a column, toggling direction if same column clicked
        /// </summary>
        [RelayCommand]
        public void SortByColumn(string columnName)
        {
            if (SortColumn == columnName)
            {
                // Toggle direction
                SortDirection = SortDirection switch
                {
                    SortDirection.None => SortDirection.Ascending,
                    SortDirection.Ascending => SortDirection.Descending,
                    SortDirection.Descending => SortDirection.None,
                    _ => SortDirection.None
                };
            }
            else
            {
                SortColumn = columnName;
                SortDirection = SortDirection.Ascending;
            }

            ApplyAdObjectFilterAndSort();
        }

        /// <summary>
        /// Clears all filters
        /// </summary>
        [RelayCommand]
        public void ClearFilters()
        {
            // Clear new per-column filters
            FilterName = string.Empty;
            FilterDescription = string.Empty;
            FilterEnabled = null;
            FilterSamAccountName = string.Empty;
            FilterEmail = string.Empty;
            FilterTitle = string.Empty;
            FilterDepartment = string.Empty;
            FilterManager = string.Empty;
            FilterSource = string.Empty;
            FilterLastUser = string.Empty;
            FilterComputerHistory = string.Empty;
            FilterSnowId = string.Empty;
            FilterIpAddress = string.Empty;
            FilterLastActivityDays = null;
            FilterLastActivityOlderThan = true;
            FilterLastLogonDays = null;
            FilterLastLogonOlderThan = true;

            // Clear printer filters
            FilterShareName = string.Empty;
            FilterDriverName = string.Empty;
            FilterUncName = string.Empty;
            FilterLocation = string.Empty;

            // Reset filter modes to default (Contains)
            FilterNameMode = FilterMode.Contains;
            FilterDescriptionMode = FilterMode.Contains;
            FilterSamAccountNameMode = FilterMode.Contains;
            FilterEmailMode = FilterMode.Contains;
            FilterTitleMode = FilterMode.Contains;
            FilterDepartmentMode = FilterMode.Contains;
            FilterManagerMode = FilterMode.Contains;
            FilterSourceMode = FilterMode.Contains;
            FilterLastUserMode = FilterMode.Contains;
            FilterComputerHistoryMode = FilterMode.Contains;
            FilterSnowIdMode = FilterMode.Contains;
            FilterIpAddressMode = FilterMode.Contains;
            FilterShareNameMode = FilterMode.Contains;
            FilterDriverNameMode = FilterMode.Contains;
            FilterUncNameMode = FilterMode.Contains;
            FilterLocationMode = FilterMode.Contains;

            // Clear legacy filters
            FilterNameStartsWith = string.Empty;
            FilterNameEndsWith = string.Empty;
            FilterNameContains = string.Empty;
            FilterHasDescription = false;
            FilterIsEnabled = false;
            FilterOlderThanDays = null;
            
            ApplyFilterAndSort();
        }

        /// <summary>
        /// Toggles the last activity filter direction between older than and newer than
        /// </summary>
        [RelayCommand]
        public void ToggleLastActivityDirection()
        {
            FilterLastActivityOlderThan = !FilterLastActivityOlderThan;
            OnPropertyChanged(nameof(FilterLastActivityDirectionText));
            ApplyFilterAndSort();
        }

        [RelayCommand]
        public void ToggleLastLogonDirection()
        {
            FilterLastLogonOlderThan = !FilterLastLogonOlderThan;
            OnPropertyChanged(nameof(FilterLastLogonDirectionText));
            ApplyFilterAndSort();
        }

        // ComboBox filter changes apply immediately (no typing involved)
        partial void OnFilterEnabledChanged(bool? value) => ApplyFilterAndSort();

        // Filter mode changes only apply if the corresponding filter has content
        partial void OnFilterNameModeChanged(FilterMode value) { if (!string.IsNullOrEmpty(FilterName)) ApplyFilterAndSort(); }
        partial void OnFilterDescriptionModeChanged(FilterMode value) { if (!string.IsNullOrEmpty(FilterDescription)) ApplyFilterAndSort(); }
        partial void OnFilterSamAccountNameModeChanged(FilterMode value) { if (!string.IsNullOrEmpty(FilterSamAccountName)) ApplyFilterAndSort(); }
        partial void OnFilterEmailModeChanged(FilterMode value) { if (!string.IsNullOrEmpty(FilterEmail)) ApplyFilterAndSort(); }
        partial void OnFilterTitleModeChanged(FilterMode value) { if (!string.IsNullOrEmpty(FilterTitle)) ApplyFilterAndSort(); }
        partial void OnFilterDepartmentModeChanged(FilterMode value) { if (!string.IsNullOrEmpty(FilterDepartment)) ApplyFilterAndSort(); }
        partial void OnFilterManagerModeChanged(FilterMode value) { if (!string.IsNullOrEmpty(FilterManager)) ApplyFilterAndSort(); }
        partial void OnFilterSourceModeChanged(FilterMode value) { if (!string.IsNullOrEmpty(FilterSource)) ApplyFilterAndSort(); }
        partial void OnFilterLastUserModeChanged(FilterMode value) { if (!string.IsNullOrEmpty(FilterLastUser)) ApplyFilterAndSort(); }
        partial void OnFilterComputerHistoryModeChanged(FilterMode value) { if (!string.IsNullOrEmpty(FilterComputerHistory)) ApplyFilterAndSort(); }
        partial void OnFilterSnowIdModeChanged(FilterMode value) { if (!string.IsNullOrEmpty(FilterSnowId)) ApplyFilterAndSort(); }
        partial void OnFilterIpAddressModeChanged(FilterMode value) { if (!string.IsNullOrEmpty(FilterIpAddress)) ApplyFilterAndSort(); }
        partial void OnFilterShareNameModeChanged(FilterMode value) { if (!string.IsNullOrEmpty(FilterShareName)) ApplyFilterAndSort(); }
        partial void OnFilterDriverNameModeChanged(FilterMode value) { if (!string.IsNullOrEmpty(FilterDriverName)) ApplyFilterAndSort(); }
        partial void OnFilterUncNameModeChanged(FilterMode value) { if (!string.IsNullOrEmpty(FilterUncName)) ApplyFilterAndSort(); }
        partial void OnFilterLocationModeChanged(FilterMode value) { if (!string.IsNullOrEmpty(FilterLocation)) ApplyFilterAndSort(); }

        partial void OnIsRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(Header));
        }

        partial void OnResultCountChanged(int value)
        {
            OnPropertyChanged(nameof(Header));
        }

        partial void OnIsCustomNamedChanged(bool value)
        {
            OnPropertyChanged(nameof(Header));
        }
    }
}
