using System.Collections.Generic;
using ActiveScanner.ViewModels;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents saved filter settings for a group that should be applied on startup/load
    /// </summary>
    public class StartupFilter
    {
        // Text filter values
        public string FilterName { get; set; } = string.Empty;
        public string FilterDescription { get; set; } = string.Empty;
        public string FilterSamAccountName { get; set; } = string.Empty;
        public string FilterEmail { get; set; } = string.Empty;
        public string FilterTitle { get; set; } = string.Empty;
        public string FilterDepartment { get; set; } = string.Empty;
        public string FilterManager { get; set; } = string.Empty;
        public string FilterSource { get; set; } = string.Empty;
        public string FilterLastUser { get; set; } = string.Empty;
        public string FilterComputerHistory { get; set; } = string.Empty;
        public string FilterIpAddress { get; set; } = string.Empty;
        public string FilterShareName { get; set; } = string.Empty;
        public string FilterDriverName { get; set; } = string.Empty;
        public string FilterUncName { get; set; } = string.Empty;
        public string FilterLocation { get; set; } = string.Empty;

        // Numeric filter values
        public int? FilterLastActivityDays { get; set; }
        public bool FilterLastActivityOlderThan { get; set; } = true;

        // Boolean/tri-state filter values
        public bool? FilterEnabled { get; set; }

        // Filter modes
        public FilterMode FilterNameMode { get; set; } = FilterMode.Contains;
        public FilterMode FilterDescriptionMode { get; set; } = FilterMode.Contains;
        public FilterMode FilterSamAccountNameMode { get; set; } = FilterMode.Contains;
        public FilterMode FilterEmailMode { get; set; } = FilterMode.Contains;
        public FilterMode FilterTitleMode { get; set; } = FilterMode.Contains;
        public FilterMode FilterDepartmentMode { get; set; } = FilterMode.Contains;
        public FilterMode FilterManagerMode { get; set; } = FilterMode.Contains;
        public FilterMode FilterSourceMode { get; set; } = FilterMode.Contains;
        public FilterMode FilterLastUserMode { get; set; } = FilterMode.Contains;
        public FilterMode FilterComputerHistoryMode { get; set; } = FilterMode.Contains;
        public FilterMode FilterIpAddressMode { get; set; } = FilterMode.Contains;
        public FilterMode FilterShareNameMode { get; set; } = FilterMode.Contains;
        public FilterMode FilterDriverNameMode { get; set; } = FilterMode.Contains;
        public FilterMode FilterUncNameMode { get; set; } = FilterMode.Contains;
        public FilterMode FilterLocationMode { get; set; } = FilterMode.Contains;

        /// <summary>
        /// Creates a StartupFilter from the current filter state of an OutputTabViewModel
        /// </summary>
        public static StartupFilter FromTab(OutputTabViewModel tab)
        {
            return new StartupFilter
            {
                FilterName = tab.FilterName,
                FilterDescription = tab.FilterDescription,
                FilterSamAccountName = tab.FilterSamAccountName,
                FilterEmail = tab.FilterEmail,
                FilterTitle = tab.FilterTitle,
                FilterDepartment = tab.FilterDepartment,
                FilterManager = tab.FilterManager,
                FilterSource = tab.FilterSource,
                FilterLastUser = tab.FilterLastUser,
                FilterComputerHistory = tab.FilterComputerHistory,
                FilterIpAddress = tab.FilterIpAddress,
                FilterShareName = tab.FilterShareName,
                FilterDriverName = tab.FilterDriverName,
                FilterUncName = tab.FilterUncName,
                FilterLocation = tab.FilterLocation,
                FilterLastActivityDays = tab.FilterLastActivityDays,
                FilterLastActivityOlderThan = tab.FilterLastActivityOlderThan,
                FilterEnabled = tab.FilterEnabled,
                FilterNameMode = tab.FilterNameMode,
                FilterDescriptionMode = tab.FilterDescriptionMode,
                FilterSamAccountNameMode = tab.FilterSamAccountNameMode,
                FilterEmailMode = tab.FilterEmailMode,
                FilterTitleMode = tab.FilterTitleMode,
                FilterDepartmentMode = tab.FilterDepartmentMode,
                FilterManagerMode = tab.FilterManagerMode,
                FilterSourceMode = tab.FilterSourceMode,
                FilterLastUserMode = tab.FilterLastUserMode,
                FilterComputerHistoryMode = tab.FilterComputerHistoryMode,
                FilterIpAddressMode = tab.FilterIpAddressMode,
                FilterShareNameMode = tab.FilterShareNameMode,
                FilterDriverNameMode = tab.FilterDriverNameMode,
                FilterUncNameMode = tab.FilterUncNameMode,
                FilterLocationMode = tab.FilterLocationMode
            };
        }

        /// <summary>
        /// Applies this startup filter's settings to an OutputTabViewModel
        /// </summary>
        public void ApplyTo(OutputTabViewModel tab)
        {
            // Apply text filters (only if not empty - ignore obsolete filter properties)
            if (!string.IsNullOrEmpty(FilterName)) tab.FilterName = FilterName;
            if (!string.IsNullOrEmpty(FilterDescription)) tab.FilterDescription = FilterDescription;
            if (!string.IsNullOrEmpty(FilterSamAccountName)) tab.FilterSamAccountName = FilterSamAccountName;
            if (!string.IsNullOrEmpty(FilterEmail)) tab.FilterEmail = FilterEmail;
            if (!string.IsNullOrEmpty(FilterTitle)) tab.FilterTitle = FilterTitle;
            if (!string.IsNullOrEmpty(FilterDepartment)) tab.FilterDepartment = FilterDepartment;
            if (!string.IsNullOrEmpty(FilterManager)) tab.FilterManager = FilterManager;
            if (!string.IsNullOrEmpty(FilterSource)) tab.FilterSource = FilterSource;
            if (!string.IsNullOrEmpty(FilterLastUser)) tab.FilterLastUser = FilterLastUser;
            if (!string.IsNullOrEmpty(FilterComputerHistory)) tab.FilterComputerHistory = FilterComputerHistory;
            if (!string.IsNullOrEmpty(FilterIpAddress)) tab.FilterIpAddress = FilterIpAddress;
            if (!string.IsNullOrEmpty(FilterShareName)) tab.FilterShareName = FilterShareName;
            if (!string.IsNullOrEmpty(FilterDriverName)) tab.FilterDriverName = FilterDriverName;
            if (!string.IsNullOrEmpty(FilterUncName)) tab.FilterUncName = FilterUncName;
            if (!string.IsNullOrEmpty(FilterLocation)) tab.FilterLocation = FilterLocation;

            // Apply numeric filters
            if (FilterLastActivityDays.HasValue) tab.FilterLastActivityDays = FilterLastActivityDays;
            tab.FilterLastActivityOlderThan = FilterLastActivityOlderThan;

            // Apply boolean filters
            tab.FilterEnabled = FilterEnabled;

            // Apply filter modes
            tab.FilterNameMode = FilterNameMode;
            tab.FilterDescriptionMode = FilterDescriptionMode;
            tab.FilterSamAccountNameMode = FilterSamAccountNameMode;
            tab.FilterEmailMode = FilterEmailMode;
            tab.FilterTitleMode = FilterTitleMode;
            tab.FilterDepartmentMode = FilterDepartmentMode;
            tab.FilterManagerMode = FilterManagerMode;
            tab.FilterSourceMode = FilterSourceMode;
            tab.FilterLastUserMode = FilterLastUserMode;
            tab.FilterComputerHistoryMode = FilterComputerHistoryMode;
            tab.FilterIpAddressMode = FilterIpAddressMode;
            tab.FilterShareNameMode = FilterShareNameMode;
            tab.FilterDriverNameMode = FilterDriverNameMode;
            tab.FilterUncNameMode = FilterUncNameMode;
            tab.FilterLocationMode = FilterLocationMode;

            // Apply filter to refresh the view
            tab.ApplyFilterAndSort();
        }

        /// <summary>
        /// Checks if this filter has any active filter conditions
        /// </summary>
        public bool HasAnyFilters()
        {
            return !string.IsNullOrEmpty(FilterName) ||
                   !string.IsNullOrEmpty(FilterDescription) ||
                   !string.IsNullOrEmpty(FilterSamAccountName) ||
                   !string.IsNullOrEmpty(FilterEmail) ||
                   !string.IsNullOrEmpty(FilterTitle) ||
                   !string.IsNullOrEmpty(FilterDepartment) ||
                   !string.IsNullOrEmpty(FilterManager) ||
                   !string.IsNullOrEmpty(FilterSource) ||
                   !string.IsNullOrEmpty(FilterLastUser) ||
                   !string.IsNullOrEmpty(FilterComputerHistory) ||
                   !string.IsNullOrEmpty(FilterIpAddress) ||
                   !string.IsNullOrEmpty(FilterShareName) ||
                   !string.IsNullOrEmpty(FilterDriverName) ||
                   !string.IsNullOrEmpty(FilterUncName) ||
                   !string.IsNullOrEmpty(FilterLocation) ||
                   FilterLastActivityDays.HasValue ||
                   FilterEnabled.HasValue;
        }
    }
}
