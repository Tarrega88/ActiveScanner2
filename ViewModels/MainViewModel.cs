using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.DirectoryServices;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ActiveScanner.Models;
using ActiveScanner.Services;

namespace ActiveScanner.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        private readonly LdapService _ldapService;
        private readonly NetworkService _networkService;
        private readonly PushRunService _pushRunService;
        private readonly ExportService _exportService;
        private readonly CacheService _cacheService;
        private readonly CustomGroupService _customGroupService;
        private readonly ComputerCacheService _computerCacheService;
        private readonly UserCacheService _userCacheService;
        private readonly SnowUserService2 _snowUserService;
        private readonly SnowVulnerabilityService _snowVulnService;
        private readonly RemediationCatalogService _catalogService;
        private readonly Stack<string> _navigationStack = new();
        
        /// <summary>
        /// Indicates whether the user is authenticated to ServiceNow
        /// </summary>
        public bool IsSnowAuthenticated => _snowUserService?.IsAuthenticated ?? false;
        private DirectoryEntry? _currentEntry;
        private CancellationTokenSource? _currentCts;
        private CancellationTokenSource? _statusClearCts;
        private CancellationTokenSource? _dnsCts;
        private const int StatusClearDelayMs = 5000; // Auto-clear status after 5 seconds

        /// <summary>
        /// Event raised when SelectAll is requested (Ctrl+A)
        /// </summary>
        public event Action? SelectAllRequested;

        #region Observable Properties

        // Domain selection
        [ObservableProperty]
        private ObservableCollection<DomainConfig> _presetDomains = new();

        [ObservableProperty]
        private DomainConfig? _selectedDomain;

        [ObservableProperty]
        private string _customPath = string.Empty;

        [ObservableProperty]
        private ObservableCollection<DomainConfig> _savedCustomPaths = new();

        // Multi-target management
        [ObservableProperty]
        private ObservableCollection<TargetPath> _activeTargets = new();

        [ObservableProperty]
        private ObservableCollection<TargetGroup> _savedTargetGroups = new();



        /// <summary>
        /// Domain groups filtered by type - Multi-Type (None)
        /// </summary>
        public IEnumerable<TargetGroup> DomainGroupsMultiType => SavedTargetGroups.Where(g => g.ObjectType == GroupObjectType.None);
        public bool HasDomainGroupsMultiType => DomainGroupsMultiType.Any();

        /// <summary>
        /// Domain groups filtered by type - Computers
        /// </summary>
        public IEnumerable<TargetGroup> DomainGroupsComputers => SavedTargetGroups.Where(g => g.ObjectType == GroupObjectType.Computers);
        public bool HasDomainGroupsComputers => DomainGroupsComputers.Any();

        /// <summary>
        /// Domain groups filtered by type - Users
        /// </summary>
        public IEnumerable<TargetGroup> DomainGroupsUsers => SavedTargetGroups.Where(g => g.ObjectType == GroupObjectType.Users);
        public bool HasDomainGroupsUsers => DomainGroupsUsers.Any();

        /// <summary>
        /// Domain groups filtered by type - Printers
        /// </summary>
        public IEnumerable<TargetGroup> DomainGroupsPrinters => SavedTargetGroups.Where(g => g.ObjectType == GroupObjectType.Printers);
        public bool HasDomainGroupsPrinters => DomainGroupsPrinters.Any();

        // Has properties for Saved Groups
        public bool HasComputerGroups => ComputerGroups.Any();
        public bool HasUserGroups => UserGroups.Any();
        public bool HasPrinterGroups => PrinterGroups.Any();

        // Custom Groups (Computer, User, Printer)
        [ObservableProperty]
        private ObservableCollection<CustomGroup> _computerGroups = new();

        [ObservableProperty]
        private ObservableCollection<CustomGroup> _userGroups = new();

        [ObservableProperty]
        private ObservableCollection<CustomGroup> _printerGroups = new();

        /// <summary>
        /// All custom groups (for menu/dropdown selection)
        /// </summary>
        public IEnumerable<CustomGroup> AllCustomGroups => 
            ComputerGroups.Concat(UserGroups).Concat(PrinterGroups);

        /// <summary>
        /// Move a domain group to a new position in the list
        /// </summary>
        public void MoveDomainGroup(TargetGroup group, int newIndex)
        {
            if (group == null) return;
            
            var currentIndex = SavedTargetGroups.IndexOf(group);
            if (currentIndex < 0 || currentIndex == newIndex) return;
            
            newIndex = Math.Max(0, Math.Min(newIndex, SavedTargetGroups.Count - 1));
            SavedTargetGroups.Move(currentIndex, newIndex);
            SaveDomainGroupOrder();
        }

        /// <summary>
        /// Save the current domain group order to settings
        /// </summary>
        public void SaveDomainGroupOrder()
        {
            // Rebuild the groups list with current order
            App.TargetGroups.Groups.Clear();
            foreach (var g in SavedTargetGroups)
                App.TargetGroups.Groups.Add(g);
            App.TargetGroups.Save();
        }

        /// <summary>
        /// Move a computer group to a new position in the list
        /// </summary>
        public void MoveComputerGroup(CustomGroup group, int newIndex)
        {
            if (group == null) return;
            
            var currentIndex = ComputerGroups.IndexOf(group);
            if (currentIndex < 0 || currentIndex == newIndex) return;
            
            newIndex = Math.Max(0, Math.Min(newIndex, ComputerGroups.Count - 1));
            ComputerGroups.Move(currentIndex, newIndex);
            SaveComputerGroupOrder();
        }

        /// <summary>
        /// Save the current computer group order
        /// </summary>
        public void SaveComputerGroupOrder()
        {
            _customGroupService.ReorderGroups(CustomGroupType.Computer, ComputerGroups.ToList());
            _ = _customGroupService.SaveAsync();
        }

        /// <summary>
        /// Move a user group to a new position in the list
        /// </summary>
        public void MoveUserGroup(CustomGroup group, int newIndex)
        {
            if (group == null) return;
            
            var currentIndex = UserGroups.IndexOf(group);
            if (currentIndex < 0 || currentIndex == newIndex) return;
            
            newIndex = Math.Max(0, Math.Min(newIndex, UserGroups.Count - 1));
            UserGroups.Move(currentIndex, newIndex);
            SaveUserGroupOrder();
        }

        /// <summary>
        /// Save the current user group order
        /// </summary>
        public void SaveUserGroupOrder()
        {
            _customGroupService.ReorderGroups(CustomGroupType.User, UserGroups.ToList());
            _ = _customGroupService.SaveAsync();
        }

        /// <summary>
        /// Move a printer group to a new position in the list
        /// </summary>
        public void MovePrinterGroup(CustomGroup group, int newIndex)
        {
            if (group == null) return;
            
            var currentIndex = PrinterGroups.IndexOf(group);
            if (currentIndex < 0 || currentIndex == newIndex) return;
            
            newIndex = Math.Max(0, Math.Min(newIndex, PrinterGroups.Count - 1));
            PrinterGroups.Move(currentIndex, newIndex);
            SavePrinterGroupOrder();
        }

        /// <summary>
        /// Save the current printer group order
        /// </summary>
        public void SavePrinterGroupOrder()
        {
            _customGroupService.ReorderGroups(CustomGroupType.Printer, PrinterGroups.ToList());
            _ = _customGroupService.SaveAsync();
        }

        // Navigation
        [ObservableProperty]
        private string _currentPath = string.Empty;

        [ObservableProperty]
        private ObservableCollection<BreadcrumbItem> _breadcrumbs = new();

        [ObservableProperty]
        private ObservableCollection<FolderItem> _subfolders = new();

        [ObservableProperty]
        private FolderItem? _selectedFolder;

        [ObservableProperty]
        private bool _canGoBack;

        // Results - using AdObjectInfo for both computers and users
        [ObservableProperty]
        private ObservableCollection<AdObjectInfo> _adObjects = new();

        // Computers collection (now using AdObjectInfo for unified model)
        [ObservableProperty]
        private ObservableCollection<AdObjectInfo> _computers = new();

        [ObservableProperty]
        private AdObjectInfo? _selectedComputer;

        [ObservableProperty]
        private ObservableCollection<AdObjectInfo> _selectedComputers = new();

        // Selected AD objects (for new unified model)
        [ObservableProperty]
        private ObservableCollection<AdObjectInfo> _selectedAdObjects = new();

        /// <summary>
        /// Flag to suppress CollectionChanged processing during batch selection operations
        /// </summary>
        private bool _isBatchUpdatingSelection;

        /// <summary>
        /// Cached selection object type - invalidated when selection changes
        /// </summary>
        private AdObjectType? _cachedSelectionType;
        private bool _selectionTypeCacheValid;

        // ===================================================================================
        // NOTE: These search-related properties are still used by SearchGroupAsync for 
        // loading domain groups. The Search Bar UI has been removed, but these properties
        // control column selection and filtering behavior when loading groups.
        // ===================================================================================

        // Search query (used when filtering group results - default to wildcard for "get all")
        [ObservableProperty]
        private string _searchQuery = "*";

        [ObservableProperty]
        private SearchScopeOption _selectedSearchScope = SearchScopeOption.CurrentFolder;

        [ObservableProperty]
        private bool _includeDisabledInSearch = true;

        // Search type and column selection (controls which LDAP attributes to retrieve)
        [ObservableProperty]
        private ObservableCollection<SearchTypeOption> _searchTypes = new();

        [ObservableProperty]
        private ObservableCollection<SearchColumnItem> _searchColumnItems = new();

        [ObservableProperty]
        private ObservableCollection<SearchColumnOption> _allSearchColumns = new();

        // Network tools
        [ObservableProperty]
        private int _selectedPort = 3389;

        [ObservableProperty]
        private int _timeoutSeconds = 30;

        [ObservableProperty]
        private ObservableCollection<PortInfo> _commonPorts = new();

        // Output tabs
        [ObservableProperty]
        private ObservableCollection<OutputTabViewModel> _outputTabs = new();

        [ObservableProperty]
        private OutputTabViewModel? _selectedOutputTab;

        // Export
        [ObservableProperty]
        private ExportFormat _selectedExportFormat = ExportFormat.Excel;

        [ObservableProperty]
        private ExportSource _selectedExportSource = ExportSource.CurrentTab;

        [ObservableProperty]
        private string _exportFolder = string.Empty;

        [ObservableProperty]
        private string _exportFileName = string.Empty;

        [ObservableProperty]
        private bool _useLastFileNameWithTimestamp;

        [ObservableProperty]
        private bool _useOrganizedExports = true;

        /// <summary>
        /// Gets the full export path, including organized subfolder when enabled.
        /// Structure: {baseFolder}/Active Scanner Exports/{ObjectType}/{SourceName}/{Format}/
        /// </summary>
        public string FullExportPath
        {
            get
            {
                if (string.IsNullOrEmpty(ExportFolder))
                    return string.Empty;

                if (UseOrganizedExports && SelectedExportFormat != ExportFormat.Clipboard)
                {
                    if (SelectedExportSource == ExportSource.LastUserDatabase)
                    {
                        // User history exports go to a special location
                        return Path.Combine(ExportFolder, "Active Scanner Exports", "User History", SelectedExportFormat.ToString());
                    }

                    // Get object type and source from the selected tab
                    var objectType = GetExportObjectType();
                    var sourcePath = SelectedOutputTab?.SourcePath;
                    
                    return _exportService.GetOrganizedExportFolder(ExportFolder, objectType, sourcePath, SelectedExportFormat);
                }

                return ExportFolder;
            }
        }

        /// <summary>
        /// Gets the object type string for export folder organization
        /// </summary>
        private string GetExportObjectType()
        {
            if (SelectedOutputTab == null)
                return "Mixed";

            return SelectedOutputTab.Type switch
            {
                OutputTabType.Computers => "Computers",
                OutputTabType.Users => "Users",
                OutputTabType.Printers => "Printers",
                OutputTabType.Vulnerabilities => "Vulnerabilities",
                OutputTabType.Results when SelectedOutputTab.ShowsComputers && !SelectedOutputTab.ShowsUsers && !SelectedOutputTab.ShowsPrinters => "Computers",
                OutputTabType.Results when SelectedOutputTab.ShowsUsers && !SelectedOutputTab.ShowsComputers && !SelectedOutputTab.ShowsPrinters => "Users",
                OutputTabType.Results when SelectedOutputTab.ShowsPrinters && !SelectedOutputTab.ShowsComputers && !SelectedOutputTab.ShowsUsers => "Printers",
                _ => "Mixed"
            };
        }

        // Notify FullExportPath when dependencies change
        partial void OnExportFolderChanged(string value) => OnPropertyChanged(nameof(FullExportPath));
        partial void OnUseOrganizedExportsChanged(bool value) => OnPropertyChanged(nameof(FullExportPath));
        partial void OnSelectedExportFormatChanged(ExportFormat value) => OnPropertyChanged(nameof(FullExportPath));
        partial void OnSelectedExportSourceChanged(ExportSource value) => OnPropertyChanged(nameof(FullExportPath));

        // UI State
        [ObservableProperty]
        private bool _isDarkMode;

        // Admin Credentials (session only - never persisted)
        [ObservableProperty]
        private bool _isAdminLoggedIn;

        [ObservableProperty]
        private string _adminUsername = string.Empty;

        [ObservableProperty]
        private bool _isGpUpdateForce = true;

        /// <summary>
        /// Whether a network operation is currently running (for cancel/skip buttons)
        /// </summary>
        [ObservableProperty]
        private bool _isNetworkOperationRunning;

        /// <summary>
        /// Status text showing current target in a loop (e.g., "2/5: ComputerName")
        /// </summary>
        [ObservableProperty]
        private string _currentOperationTarget = string.Empty;

        private NetworkCredential? _adminCredential;

        /// <summary>
        /// Gets the stored admin credential for remote operations.
        /// Returns null if not logged in.
        /// </summary>
        public NetworkCredential? AdminCredential => _adminCredential;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _isDnsResolving;

        [ObservableProperty]
        private int _computerCount;

        /// <summary>
        /// Number of currently selected items
        /// </summary>
        [ObservableProperty]
        private int _selectedCount;

        /// <summary>
        /// Progress text for network operations (e.g., "5/50")
        /// </summary>
        [ObservableProperty]
        private string _networkOperationProgress = string.Empty;

        /// <summary>
        /// Name of the current network operation (e.g., "Ping", "DNS")
        /// </summary>
        [ObservableProperty]
        private string _networkOperationName = string.Empty;

        // Network History Panel
        [ObservableProperty]
        private ObservableCollection<Models.NetworkHistoryEntry> _networkHistory = new();

        [ObservableProperty]
        private bool _isNetworkPanelExpanded;

        [ObservableProperty]
        private double _networkPanelHeight = 0;

        /// <summary>
        /// The last user-set height for the network panel (remembered across collapse/expand)
        /// </summary>
        internal double _lastNetworkPanelHeight = 180;

        [RelayCommand]
        private void ClearNetworkHistory()
        {
            NetworkHistory.Clear();
        }

        /// <summary>
        /// Adds a network history entry and ensures the panel is visible
        /// </summary>
        private Models.NetworkHistoryEntry AddNetworkHistoryEntry(string action, List<string> targets)
        {
            var entry = new Models.NetworkHistoryEntry
            {
                Timestamp = DateTime.Now,
                Action = action,
                Targets = targets
            };
            NetworkHistory.Add(entry);

            // Auto-expand panel if collapsed
            if (!IsNetworkPanelExpanded)
            {
                NetworkPanelHeight = _lastNetworkPanelHeight;
                IsNetworkPanelExpanded = true;
            }

            return entry;
        }

        [ObservableProperty]
        private string? _lastQueryTime;

        [ObservableProperty]
        private string _connectionStatus = "Disconnected";

        // Cache/View State
        [ObservableProperty]
        private string _currentViewName = string.Empty;

        [ObservableProperty]
        private string _lastUpdatedDisplay = string.Empty;

        [ObservableProperty]
        private bool _isViewingCachedResults;

        [ObservableProperty]
        private bool _hasCurrentView;

        #endregion

        public MainViewModel()
        {
            _ldapService = new LdapService();
            _networkService = new NetworkService();
            _pushRunService = new PushRunService(_networkService);
            _exportService = new ExportService();
            _cacheService = new CacheService();
            _customGroupService = new CustomGroupService();
            _computerCacheService = new ComputerCacheService();
            _userCacheService = new UserCacheService();
            _snowUserService = new SnowUserService2();
            _snowVulnService = new SnowVulnerabilityService(_snowUserService);
            _catalogService = new RemediationCatalogService();

            InitializePresetDomains();
            InitializeCommonPorts();
            InitializeSearchOptions(); // Still needed to set up column configurations for group loading
            LoadSettings();
            _ = LoadCustomGroupsAsync();
            _ = InitializeCachesAsync(); // Load caches and sync data at startup
            _ = DiscoverDomainsOnStartupAsync(); // Auto-discover domains in background

            // Subscribe to SelectedAdObjects collection changes for custom group button state
            _selectedAdObjects.CollectionChanged += SelectedAdObjects_CollectionChanged;

            // Create default Domain Results tab
            var resultsTab = new OutputTabViewModel("Domain Results", OutputTabType.Results) { CanClose = false };
            OutputTabs.Add(resultsTab);
            SelectedOutputTab = resultsTab;
        }

        /// <summary>
        /// Initializes caches on startup and syncs existing computer cache data to user cache
        /// for the Computer History reverse lookup feature.
        /// </summary>
        private async Task InitializeCachesAsync()
        {
            try
            {
                await _computerCacheService.LoadAsync();
                
                // Sync existing computer cache entries to user cache for reverse lookup
                var computerEntries = _computerCacheService.GetAllEntries();
                var synced = _userCacheService.SyncFromComputerCache(computerEntries);
                if (synced > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Synced {synced} user-machine mappings from computer cache");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cache initialization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Discovers domains on startup and adds them to the dropdown (not shortcuts)
        /// </summary>
        private async Task DiscoverDomainsOnStartupAsync()
        {
            try
            {
                var domains = await _ldapService.DiscoverDomainsAsync();
                
                if (domains.Count == 0) return;

                // Filter out domains we already have
                var existingDns = PresetDomains.Select(d => d.DistinguishedName.ToUpperInvariant()).ToHashSet();
                var newDomains = domains.Where(d => !existingDns.Contains(d.DistinguishedName.ToUpperInvariant())).ToList();

                if (newDomains.Count > 0)
                {
                    foreach (var domain in newDomains)
                    {
                        PresetDomains.Add(domain);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Startup domain discovery failed: {ex.Message}");
            }
        }

        private void SelectedAdObjects_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Skip notifications during batch operations - caller will notify at the end
            if (_isBatchUpdatingSelection) return;

            NotifySelectionPropertiesChanged();
        }

        /// <summary>
        /// Notifies all selection-dependent properties. Call after batch selection updates.
        /// </summary>
        public void NotifySelectionPropertiesChanged()
        {
            // Invalidate cached selection type
            _selectionTypeCacheValid = false;

            OnPropertyChanged(nameof(CanCreateCustomGroup));
            OnPropertyChanged(nameof(CreateCustomGroupButtonText));
            OnPropertyChanged(nameof(CanCreateStaleDescriptionGroup));
            OnPropertyChanged(nameof(HasGroupsForSelection));
            OnPropertyChanged(nameof(GroupsForSelectionType));
            OnPropertyChanged(nameof(AddToGroupMenuHeader));
        }

        /// <summary>
        /// Begins a batch selection update. Suppresses CollectionChanged notifications.
        /// </summary>
        public void BeginBatchSelection()
        {
            _isBatchUpdatingSelection = true;
        }

        /// <summary>
        /// Ends a batch selection update and raises all necessary notifications.
        /// </summary>
        public void EndBatchSelection()
        {
            _isBatchUpdatingSelection = false;
            NotifySelectionPropertiesChanged();
        }

        // Still needed to set up column configurations for group loading
        private void InitializeSearchOptions()
        {
            // Initialize search types
            SearchTypes.Add(new SearchTypeOption("Computers", SearchObjectType.Computer));
            SearchTypes.Add(new SearchTypeOption("Users", SearchObjectType.User));
            SearchTypes.Add(new SearchTypeOption("Printers", SearchObjectType.Printer));

            // Subscribe to changes on each type to update columns
            foreach (var searchType in SearchTypes)
            {
                searchType.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(SearchTypeOption.IsSelected))
                    {
                        UpdateSearchColumnItems();
                    }
                };
            }

            // Initialize all columns
            foreach (var col in SearchColumnDefaults.GetAllColumns())
            {
                AllSearchColumns.Add(col);
            }

            // Build initial column items
            UpdateSearchColumnItems();
        }

        /// <summary>
        /// Updates the SearchColumnItems collection based on selected types
        /// </summary>
        private void UpdateSearchColumnItems()
        {
            SearchColumnItems.Clear();

            var selectedTypes = SearchTypes.Where(t => t.IsSelected).Select(t => t.ObjectType).ToList();

            if (selectedTypes.Contains(SearchObjectType.Computer))
            {
                SearchColumnItems.Add(SearchColumnItem.CreateHeader("Computers", SearchObjectType.Computer));
                foreach (var col in AllSearchColumns.Where(c => c.ObjectType == SearchObjectType.Computer))
                {
                    SearchColumnItems.Add(SearchColumnItem.CreateColumn(col));
                }
            }

            if (selectedTypes.Contains(SearchObjectType.User))
            {
                SearchColumnItems.Add(SearchColumnItem.CreateHeader("Users", SearchObjectType.User));
                foreach (var col in AllSearchColumns.Where(c => c.ObjectType == SearchObjectType.User))
                {
                    SearchColumnItems.Add(SearchColumnItem.CreateColumn(col));
                }
            }

            if (selectedTypes.Contains(SearchObjectType.Printer))
            {
                SearchColumnItems.Add(SearchColumnItem.CreateHeader("Printers", SearchObjectType.Printer));
                foreach (var col in AllSearchColumns.Where(c => c.ObjectType == SearchObjectType.Printer))
                {
                    SearchColumnItems.Add(SearchColumnItem.CreateColumn(col));
                }
            }
        }

        // ===================================================================================
        // REMOVED SEARCH BAR - Legacy commands left for potential reimplementation later
        // The Search Bar UI was removed to simplify the UX. Users should select a Domain 
        // Group (Computers, Users, Printers) and use the per-column filters instead.
        // ===================================================================================
        /*
        [RelayCommand]
        private void SelectAllSearchTypes()
        {
            foreach (var type in SearchTypes)
            {
                type.IsSelected = true;
            }
        }

        [RelayCommand]
        private void DeselectAllSearchTypes()
        {
            foreach (var type in SearchTypes)
            {
                type.IsSelected = false;
            }
        }

        [RelayCommand]
        private void SelectAllSearchColumns()
        {
            foreach (var col in AllSearchColumns)
            {
                col.IsSelected = true;
            }
        }

        [RelayCommand]
        private void DeselectAllSearchColumns()
        {
            foreach (var col in AllSearchColumns)
            {
                col.IsSelected = false;
            }
        }
        */

        private void InitializePresetDomains()
        {
            // Root domain - will preload child domains without scanning
            PresetDomains.Add(new DomainConfig("va.gov", "DC=va,DC=gov", true));
            // med.va.gov with preloaded child domains (r01-r04, v01-v23, vha)
            PresetDomains.Add(new DomainConfig("med/*", "DC=med,DC=va,DC=gov", true) { Server = "PRELOAD_MED_CHILDREN" });
        }

        private void InitializeCommonPorts()
        {
            foreach (var port in NetworkService.GetCommonPorts())
            {
                CommonPorts.Add(port);
            }
        }

        private void LoadSettings()
        {
            var settings = App.Settings.Current;

            TimeoutSeconds = settings.DefaultTimeoutSeconds;
            ExportFolder = string.IsNullOrEmpty(settings.LastExportFolder)
                ? _exportService.GetDefaultExportFolder()
                : settings.LastExportFolder;
            UseLastFileNameWithTimestamp = settings.UseLastFileNameWithTimestamp;
            IsDarkMode = settings.DarkMode;

            // Point the vulnerability service at the persisted, user-editable config.
            _snowVulnService.Config = settings.Vulnerability;

            // Load saved paths
            foreach (var path in settings.SavedCustomPaths)
            {
                SavedCustomPaths.Add(path);
            }

            // Load saved groups
            foreach (var group in App.TargetGroups.Groups)
            {
                SavedTargetGroups.Add(group);
            }
        }

        /// <summary>
        /// Refreshes the SavedTargetGroups from TargetGroupService (used after importing groups)
        /// </summary>
        public void RefreshSavedGroups()
        {
            SavedTargetGroups.Clear();
            foreach (var group in App.TargetGroups.Groups)
            {
                SavedTargetGroups.Add(group);
            }
        }

        /// <summary>
        /// Refreshes all settings-bound properties after a complete settings reset.
        /// This reloads everything from the fresh settings without requiring an app restart.
        /// </summary>
        public void RefreshAfterSettingsReset()
        {
            var settings = App.Settings.Current;

            // Reload scan settings
            TimeoutSeconds = settings.DefaultTimeoutSeconds;
            ExportFolder = string.IsNullOrEmpty(settings.LastExportFolder)
                ? _exportService.GetDefaultExportFolder()
                : settings.LastExportFolder;
            UseLastFileNameWithTimestamp = settings.UseLastFileNameWithTimestamp;
            IsDarkMode = settings.DarkMode;

            // Clear and reload saved custom paths
            SavedCustomPaths.Clear();
            foreach (var path in settings.SavedCustomPaths)
            {
                SavedCustomPaths.Add(path);
            }

            // Saved groups are already refreshed by RefreshSavedGroups()
        }

        private void SaveSettings()
        {
            var settings = App.Settings.Current;

            settings.DefaultTimeoutSeconds = TimeoutSeconds;
            settings.LastExportFolder = ExportFolder;
            settings.UseLastFileNameWithTimestamp = UseLastFileNameWithTimestamp;
            settings.DarkMode = IsDarkMode;

            App.Settings.Save();
        }

        #region Domain & Target Commands

        [RelayCommand]
        private async Task SelectDomainAsync(DomainConfig domain)
        {
            SelectedDomain = domain;
            ConnectionStatus = $"Connecting to {domain.Name}...";
            
            try
            {
                await NavigateToPathAsync(domain.DistinguishedName);
                ConnectionStatus = $"Connected: {domain.Name}";

                // Check if this is the med/* shortcut (uses Server field as flag)
                if (domain.Server == "PRELOAD_MED_CHILDREN")
                {
                    PreloadMedChildDomains();
                }
                // Check if this path has preloaded child domains
                else
                {
                    TryPreloadChildDomains(domain.DistinguishedName);
                }
            }
            catch
            {
                ConnectionStatus = $"Failed: {domain.Name}";
            }
        }

        /// <summary>
        /// Preloads the med.va.gov child domains (r01-r04, v01-v23, vha)
        /// </summary>
        private void PreloadMedChildDomains()
        {
            Subfolders.Clear();
            
            // R domains (r01-r04)
            for (int i = 1; i <= 4; i++)
            {
                var name = $"r{i:D2}";
                Subfolders.Add(new FolderItem { Name = $"{name}.med.va.gov", DistinguishedName = $"DC={name},DC=med,DC=va,DC=gov", Type = "domain" });
            }
            
            // V domains (v01-v23)
            for (int i = 1; i <= 23; i++)
            {
                var name = $"v{i:D2}";
                Subfolders.Add(new FolderItem { Name = $"{name}.med.va.gov", DistinguishedName = $"DC={name},DC=med,DC=va,DC=gov", Type = "domain" });
            }
            
            // VHA domain
            Subfolders.Add(new FolderItem { Name = "vha.med.va.gov", DistinguishedName = "DC=vha,DC=med,DC=va,DC=gov", Type = "domain" });
        }

        /// <summary>
        /// Checks if the given DN has known child domains and preloads them without scanning
        /// </summary>
        private bool TryPreloadChildDomains(string dn)
        {
            var dnUpper = dn.ToUpperInvariant();
            
            // va.gov root domain
            if (dnUpper == "DC=VA,DC=GOV")
            {
                Subfolders.Clear();
                Subfolders.Add(new FolderItem { Name = "cem.va.gov", DistinguishedName = "DC=cem,DC=va,DC=gov", Type = "domain" });
                Subfolders.Add(new FolderItem { Name = "dva.va.gov", DistinguishedName = "DC=dva,DC=va,DC=gov", Type = "domain" });
                Subfolders.Add(new FolderItem { Name = "med.va.gov", DistinguishedName = "DC=med,DC=va,DC=gov", Type = "domain" });
                Subfolders.Add(new FolderItem { Name = "vapre51.va.gov", DistinguishedName = "DC=vapre51,DC=va,DC=gov", Type = "domain" });
                Subfolders.Add(new FolderItem { Name = "vba.va.gov", DistinguishedName = "DC=vba,DC=va,DC=gov", Type = "domain" });
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Adds the current domain/path as a shortcut button in the quick bar
        /// </summary>
        [RelayCommand]
        private async Task NavigateToCustomPathAsync()
        {
            if (string.IsNullOrWhiteSpace(CustomPath)) return;

            await NavigateToPathAsync(CustomPath);
        }

        [RelayCommand]
        private void SaveCustomPath()
        {
            if (string.IsNullOrWhiteSpace(CustomPath)) return;

            // Simple name extraction from DN
            var name = CustomPath;
            var parts = CustomPath.Split(',');
            if (parts.Length > 0)
            {
                var first = parts[0];
                var eqIndex = first.IndexOf('=');
                if (eqIndex > 0)
                {
                    name = first.Substring(eqIndex + 1);
                }
            }

            App.Settings.AddCustomPath(name, CustomPath);
            SavedCustomPaths.Add(new DomainConfig(name, CustomPath, false));
            App.Settings.Save();

            StatusMessage = $"Saved custom path: {name}";
        }

        [RelayCommand]
        private void RemoveCustomPath(DomainConfig path)
        {
            App.Settings.RemoveCustomPath(path.DistinguishedName);
            SavedCustomPaths.Remove(path);
            App.Settings.Save();
        }

        [RelayCommand]
        private void AddToActiveTargets()
        {
            if (string.IsNullOrWhiteSpace(CurrentPath)) return;

            if (ActiveTargets.Any(t => t.DistinguishedName == CurrentPath))
            {
                StatusMessage = "Path already in active targets";
                return;
            }

            ActiveTargets.Add(new TargetPath(CurrentPath));
            StatusMessage = $"Added target: {CurrentPath}";
        }

        [RelayCommand]
        private void AddCustomToActiveTargets()
        {
            if (string.IsNullOrWhiteSpace(CustomPath)) return;

            if (ActiveTargets.Any(t => t.DistinguishedName == CustomPath))
            {
                StatusMessage = "Path already in active targets";
                return;
            }

            ActiveTargets.Add(new TargetPath(CustomPath));
            StatusMessage = $"Added target: {CustomPath}";
        }

        [RelayCommand]
        private void RemoveActiveTarget(TargetPath target)
        {
            ActiveTargets.Remove(target);
        }

        #endregion

        #region Group Management Commands

        [ObservableProperty]
        private TargetGroup? _selectedGroup;

        [RelayCommand]
        private void CreateGroup()
        {
            var existingNames = SavedTargetGroups.Select(g => g.Name).ToList();
            var viewModel = new GroupDialogViewModel(existingNames);
            
            var dialog = new Views.GroupDialog(viewModel)
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true && dialog.ResultGroup != null)
            {
                var newGroup = dialog.ResultGroup;
                
                // Save to groups file
                App.TargetGroups.AddGroup(newGroup);
                
                // Add to observable collection
                SavedTargetGroups.Add(newGroup);
                
                StatusMessage = $"Created group: {newGroup.Name}";
            }
        }

        [RelayCommand]
        private void EditGroup(TargetGroup group)
        {
            if (group == null) return;

            var existingNames = SavedTargetGroups.Select(g => g.Name).ToList();
            var viewModel = new GroupDialogViewModel(existingNames, group);
            
            var dialog = new Views.GroupDialog(viewModel)
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true && dialog.ResultGroup != null)
            {
                var updatedGroup = dialog.ResultGroup;
                
                // Update in groups file
                App.TargetGroups.UpdateGroup(updatedGroup);
                
                // Update in observable collection
                var index = SavedTargetGroups.IndexOf(group);
                if (index >= 0)
                {
                    SavedTargetGroups[index] = updatedGroup;
                }
                
                StatusMessage = $"Updated group: {updatedGroup.Name}";
            }
        }

        [RelayCommand]
        private void DeleteGroup(TargetGroup group)
        {
            if (group == null) return;

            var result = MessageBox.Show(
                $"Delete group '{group.Name}'?\n\nThis action cannot be undone.",
                "Delete Group",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                App.TargetGroups.DeleteGroup(group.Id);
                SavedTargetGroups.Remove(group);
                
                // Clear selected group if it was deleted
                if (SelectedGroup?.Id == group.Id)
                {
                    SelectedGroup = null;
                }
                
                StatusMessage = $"Deleted group: {group.Name}";
            }
        }

        [RelayCommand]
        private async Task SelectGroupAsync(TargetGroup group)
        {
            if (group == null) return;

            // If there's already a tab open for this group, just switch to it
            var existingTab = OutputTabs.FirstOrDefault(t => t.TargetGroupId == group.Id);
            if (existingTab != null)
            {
                SelectedGroup = group;
                SelectedOutputTab = existingTab;
                CurrentViewName = group.Name;
                HasCurrentView = true;
                StatusMessage = $"Switched to group: {group.Name}";
                return;
            }

            SelectedGroup = group;
            
            // Clear current navigation since we're now in "group mode"
            CurrentPath = string.Empty;
            Breadcrumbs.Clear();
            Subfolders.Clear();
            
            // Automatically search the group
            await SearchGroupAsync();
        }

        [RelayCommand]
        private void ClearSelectedGroup()
        {
            SelectedGroup = null;
            StatusMessage = "Group selection cleared";
        }

        #endregion

        #region Custom Group Commands

        /// <summary>
        /// Load custom groups from disk
        /// </summary>
        private async Task LoadCustomGroupsAsync()
        {
            await _customGroupService.LoadAsync();
            RefreshCustomGroupCollections();
        }

        /// <summary>
        /// Refresh the observable collections from the service
        /// </summary>
        private void RefreshCustomGroupCollections()
        {
            ComputerGroups.Clear();
            UserGroups.Clear();
            PrinterGroups.Clear();

            foreach (var group in _customGroupService.ComputerGroups)
                ComputerGroups.Add(group);
            foreach (var group in _customGroupService.UserGroups)
                UserGroups.Add(group);
            foreach (var group in _customGroupService.PrinterGroups)
                PrinterGroups.Add(group);
            
            // Notify that group availability has changed (for context menu visibility and Navigate menu)
            OnPropertyChanged(nameof(HasGroupsForSelection));
            OnPropertyChanged(nameof(HasComputerGroups));
            OnPropertyChanged(nameof(HasUserGroups));
            OnPropertyChanged(nameof(HasPrinterGroups));
        }

        /// <summary>
        /// Determines if a custom group can be created based on current selection
        /// </summary>
        public bool CanCreateCustomGroup => SelectedAdObjects.Count > 0 && GetSelectionObjectType() != null;

        /// <summary>
        /// Gets the object type of current selection (null if mixed or empty).
        /// Result is cached until selection changes.
        /// </summary>
        private AdObjectType? GetSelectionObjectType()
        {
            if (_selectionTypeCacheValid)
            {
                return _cachedSelectionType;
            }

            if (SelectedAdObjects.Count == 0)
            {
                _cachedSelectionType = null;
            }
            else
            {
                AdObjectType? firstType = null;
                bool isMixed = false;

                foreach (var obj in SelectedAdObjects)
                {
                    if (firstType == null)
                    {
                        firstType = obj.ObjectType;
                    }
                    else if (obj.ObjectType != firstType)
                    {
                        isMixed = true;
                        break;
                    }
                }

                _cachedSelectionType = isMixed ? null : firstType;
            }

            _selectionTypeCacheValid = true;
            return _cachedSelectionType;
        }

        /// <summary>
        /// Gets the display text for the create custom group button
        /// </summary>
        public string CreateCustomGroupButtonText
        {
            get
            {
                var type = GetSelectionObjectType();
                return type switch
                {
                    AdObjectType.Computer => "Create Computer Group",
                    AdObjectType.User => "Create User Group",
                    AdObjectType.Printer => "Create Printer Group",
                    _ => "Create Group"
                };
            }
        }

        /// <summary>
        /// Notify custom group button properties when selection changes
        /// </summary>
        partial void OnSelectedAdObjectsChanged(ObservableCollection<AdObjectInfo> value)
        {
            OnPropertyChanged(nameof(CanCreateCustomGroup));
            OnPropertyChanged(nameof(CreateCustomGroupButtonText));
            OnPropertyChanged(nameof(CanCreateStaleDescriptionGroup));
        }

        /// <summary>
        /// Update status bar and refresh UI when tab changes
        /// </summary>
        partial void OnSelectedOutputTabChanged(OutputTabViewModel? value)
        {
            if (value != null)
            {
                // Update the status bar count based on the selected tab
                ComputerCount = value.FilteredCount > 0 ? value.FilteredCount : value.ResultCount;
                
                // Refresh the grid to ensure UI is in sync (helps when returning to a tab after background updates)
                value.RefreshAdObjectResults();
            }
            else
            {
                ComputerCount = 0;
            }

            // Update export path preview when tab changes (source path affects folder structure)
            OnPropertyChanged(nameof(FullExportPath));
            
            // Update startup filter button visibility
            OnPropertyChanged(nameof(HasStartupFilter));
            OnPropertyChanged(nameof(CanSetStartupFilter));
        }

        [RelayCommand]
        private async Task CreateCustomGroupFromSelectionAsync()
        {
            var objectType = GetSelectionObjectType();
            if (objectType == null || SelectedAdObjects.Count == 0)
            {
                SetError("Cannot create group: selection is empty or contains mixed types");
                return;
            }

            var groupType = objectType switch
            {
                AdObjectType.Computer => CustomGroupType.Computer,
                AdObjectType.User => CustomGroupType.User,
                AdObjectType.Printer => CustomGroupType.Printer,
                _ => CustomGroupType.Computer
            };

            // Existing names of the same type (uniqueness is scoped per group type).
            var existingNames = _customGroupService.Groups
                .Where(g => g.Type == groupType)
                .Select(g => g.Name)
                .ToList();

            // Suggested default name (user can keep or change).
            var suggested = $"{groupType} Group {DateTime.Now:M/d h:mmtt}".ToLower();
            // Ensure the suggestion itself doesn't collide.
            if (existingNames.Any(n => string.Equals(n, suggested, StringComparison.OrdinalIgnoreCase)))
            {
                suggested = string.Empty;
            }

            var dialog = new Views.SaveGroupDialog(
                title: $"Name {groupType} Group",
                promptText: $"Enter a name for this {groupType.ToString().ToLower()} group ({SelectedAdObjects.Count} item(s)):",
                existingNames: existingNames,
                initialName: suggested,
                okButtonText: "Create")
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() != true)
            {
                StatusMessage = "Group creation cancelled.";
                return;
            }

            var name = dialog.GroupName;

            var group = await _customGroupService.CreateGroupFromAdObjectsAsync(
                groupType, name, SelectedAdObjects);

            RefreshCustomGroupCollections();

            StatusMessage = $"Created {groupType.ToString().ToLower()} group: {name}";
        }

        /// <summary>
        /// True when the current selection contains at least one Computer object,
        /// enabling the "stale descriptions" smart-group command.
        /// </summary>
        public bool CanCreateStaleDescriptionGroup =>
            SelectedAdObjects.Any(o => o.ObjectType == AdObjectType.Computer);

        /// <summary>
        /// Creates a Computer custom group containing selected computers whose Description
        /// does NOT contain their LastUser value. Prompts the user with a preview and a
        /// checkbox controlling whether to include rows lacking a real LastUser value.
        /// </summary>
        [RelayCommand]
        private async Task CreateStaleDescriptionGroupFromSelectionAsync()
        {
            var selectedComputers = SelectedAdObjects
                .Where(o => o.ObjectType == AdObjectType.Computer)
                .ToList();

            if (selectedComputers.Count == 0)
            {
                SetError("Select one or more computer rows first.");
                return;
            }

            var existingNames = _customGroupService.Groups
                .Where(g => g.Type == CustomGroupType.Computer)
                .Select(g => g.Name)
                .ToList();

            var suggested = $"Stale Descriptions {DateTime.Now:M/d h:mmtt}".ToLower();

            var dialog = new Views.StaleDescriptionGroupDialog(selectedComputers, existingNames, suggested)
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() != true)
            {
                StatusMessage = "Stale-description group creation cancelled.";
                return;
            }

            var matches = dialog.ViewModel.MatchingComputers;
            if (matches.Count == 0)
            {
                StatusMessage = "No computers matched the stale-description criteria.";
                return;
            }

            await _customGroupService.CreateGroupFromAdObjectsAsync(
                CustomGroupType.Computer, dialog.ViewModel.GroupName, matches);

            RefreshCustomGroupCollections();

            StatusMessage = $"Created computer group \"{dialog.ViewModel.GroupName}\" with {matches.Count} stale-description computer(s).";
        }
        public ObservableCollection<CustomGroup> GroupsForSelectionType
        {
            get
            {
                var objectType = GetSelectionObjectType();
                return objectType switch
                {
                    AdObjectType.Computer => ComputerGroups,
                    AdObjectType.User => UserGroups,
                    AdObjectType.Printer => PrinterGroups,
                    _ => new ObservableCollection<CustomGroup>()
                };
            }
        }

        /// <summary>
        /// Gets the display text for the "Add to group" submenu based on selection type
        /// </summary>
        public string AddToGroupMenuHeader
        {
            get
            {
                var objectType = GetSelectionObjectType();
                return objectType switch
                {
                    AdObjectType.Computer => "Add to Computer Group",
                    AdObjectType.User => "Add to User Group",
                    AdObjectType.Printer => "Add to Printer Group",
                    _ => "Add to Group"
                };
            }
        }

        /// <summary>
        /// Returns true if there are groups available to add the current selection to
        /// </summary>
        public bool HasGroupsForSelection
        {
            get
            {
                if (SelectedAdObjects.Count == 0) return false;
                var objectType = GetSelectionObjectType();
                return objectType switch
                {
                    AdObjectType.Computer => ComputerGroups.Count > 0,
                    AdObjectType.User => UserGroups.Count > 0,
                    AdObjectType.Printer => PrinterGroups.Count > 0,
                    _ => false
                };
            }
        }

        [RelayCommand]
        private async Task AddSelectionToGroupAsync(CustomGroup group)
        {
            if (group == null || SelectedAdObjects.Count == 0) return;

            // Convert selected AD objects to group members
            var newMembers = SelectedAdObjects.Select(obj => new CustomGroupMember
            {
                Domain = ExtractDomainFromDN(obj.DistinguishedName),
                DistinguishedName = obj.DistinguishedName,
                Name = obj.Name ?? obj.DistinguishedName ?? "Unknown"
            }).ToList();

            // AddMembersAsync handles duplicate detection
            await _customGroupService.AddMembersAsync(group.Id, newMembers);
            
            RefreshCustomGroupCollections();
            
            // Invalidate cache for this group so it reloads fresh
            _cacheService.InvalidateGroupCache(group.Id);
            
            // If this group's tab is open, reload it to show the new members
            var existingTab = OutputTabs.FirstOrDefault(t => t.CustomGroupId == group.Id);
            if (existingTab != null)
            {
                // Re-fetch the group to get updated member list and reload the tab
                var updatedGroup = _customGroupService.GetGroup(group.Id);
                if (updatedGroup != null)
                {
                    await OpenCustomGroupAsync(updatedGroup);
                }
            }
            
            var addedCount = newMembers.Count;
            StatusMessage = $"Added {addedCount} item(s) to group: {group.Name}";
        }

        /// <summary>
        /// Extract domain from distinguished name (e.g., DC=example,DC=com -> example.com)
        /// </summary>
        private static string? ExtractDomainFromDN(string? dn)
        {
            if (string.IsNullOrEmpty(dn)) return null;

            try
            {
                var dcParts = dn.Split(',')
                    .Where(p => p.Trim().StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Trim().Substring(3));
                return string.Join(".", dcParts);
            }
            catch
            {
                return null;
            }
        }

        [RelayCommand]
        private async Task RenameCustomGroupAsync(CustomGroup group)
        {
            if (group == null) return;
            
            group.IsRenaming = false;
            await _customGroupService.RenameGroupAsync(group.Id, group.Name);
            
            // Update the tab name if this group's tab is open
            var existingTab = OutputTabs.FirstOrDefault(t => t.CustomGroupId == group.Id);
            if (existingTab != null)
            {
                existingTab.Name = group.Name;
            }
            
            StatusMessage = $"Renamed group to: {group.Name}";
        }

        [RelayCommand]
        private async Task EditCustomGroupAsync(CustomGroup group)
        {
            if (group == null) return;

            // Names of other groups of the same type (excluding this group itself).
            var otherNames = _customGroupService.Groups
                .Where(g => g.Type == group.Type && g.Id != group.Id)
                .Select(g => g.Name)
                .ToList();

            var dialog = new Views.CustomGroupDialog(group, otherNames)
            {
                Owner = Application.Current.MainWindow
            };

            dialog.ShowDialog();

            if (dialog.ViewModel.DeleteRequested)
            {
                // User requested deletion from the dialog - confirmation already shown
                await DeleteCustomGroupInternalAsync(group);
            }
            else if (dialog.ViewModel.DialogResult)
            {
                // Save changes through the service
                await _customGroupService.SaveAsync();
                
                // Refresh the group collections to update member counts in UI
                RefreshCustomGroupCollections();
                
                // Update the tab name if this group's tab is open
                var existingTab = OutputTabs.FirstOrDefault(t => t.CustomGroupId == group.Id);
                if (existingTab != null)
                {
                    existingTab.Name = group.Name;
                }
                
                StatusMessage = $"Updated group: {group.Name}";
            }
        }

        [RelayCommand]
        private async Task DeleteCustomGroupAsync(CustomGroup group)
        {
            if (group == null) return;

            var result = MessageBox.Show(
                $"Delete group '{group.Name}'?\n\nThis action cannot be undone.",
                "Delete Group",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                await DeleteCustomGroupInternalAsync(group);
            }
        }

        private async Task DeleteCustomGroupInternalAsync(CustomGroup group)
        {
            if (group == null) return;
            
            await _customGroupService.DeleteGroupAsync(group.Id);
            RefreshCustomGroupCollections();
            StatusMessage = $"Deleted group: {group.Name}";
        }

        [RelayCommand]
        private async Task OpenCustomGroupAsync(CustomGroup group)
        {
            if (group == null) return;

            try
            {
                // Check cache first (persists even if tab was closed)
                var cacheTtl = App.Settings.Current.CacheTtlMinutes;
                if (cacheTtl > 0)
                {
                    var cachedEntry = _cacheService.GetGroupCache(group.Id, cacheTtl);
                    if (cachedEntry != null)
                    {
                        // Load cached results with multi-type split handling
                        LoadCustomGroupFromCache(group, cachedEntry);
                        
                        StatusMessage = $"Loaded {group.Name} from cache ({cachedEntry.AgeDisplay}): {cachedEntry.Results.Count} items";
                        return;
                    }
                }

                SetBusy($"Loading {group.Name}...");

                // Query each member from AD
                var results = new List<AdObjectInfo>();
                var missingDNs = new List<string>();

                foreach (var member in group.Members)
                {
                    if (member.IsManual)
                    {
                        // Manual entry - create basic object
                        var manualObj = new AdObjectInfo
                        {
                            Name = member.Name,
                            DnsHostName = member.Address,
                            Type = "Computer",
                            Description = "(Manual entry)"
                        };
                        results.Add(manualObj);
                    }
                    else if (!string.IsNullOrEmpty(member.DistinguishedName))
                    {
                        // Try to query from AD
                        try
                        {
                            var obj = await Task.Run(() => 
                                _ldapService.GetObjectByDN(member.DistinguishedName, member.Domain));
                            
                            if (obj != null)
                            {
                                results.Add(obj);
                            }
                            else
                            {
                                // Object not found - add placeholder
                                var notFoundObj = new AdObjectInfo
                                {
                                    Name = member.Name,
                                    DistinguishedName = member.DistinguishedName,
                                    Type = group.Type.ToString(),
                                    Description = "(Not found in AD)"
                                };
                                results.Add(notFoundObj);
                                missingDNs.Add(member.DistinguishedName);
                            }
                        }
                        catch
                        {
                            // Query failed - add placeholder
                            var errorObj = new AdObjectInfo
                            {
                                Name = member.Name,
                                DistinguishedName = member.DistinguishedName,
                                Type = group.Type.ToString(),
                                Description = "(Not found in AD)"
                            };
                            results.Add(errorObj);
                            missingDNs.Add(member.DistinguishedName);
                        }
                    }
                }

                // Split results by type
                var computers = results.Where(r => r.ObjectType == AdObjectType.Computer).ToList();
                var users = results.Where(r => r.ObjectType == AdObjectType.User).ToList();
                var printers = results.Where(r => r.ObjectType == AdObjectType.Printer).ToList();
                
                var hasComputers = computers.Count > 0;
                var hasUsers = users.Count > 0;
                var hasPrinters = printers.Count > 0;
                var typeCount = (hasComputers ? 1 : 0) + (hasUsers ? 1 : 0) + (hasPrinters ? 1 : 0);

                if (typeCount > 1)
                {
                    // Multiple types - create split tabs
                    CreateSplitCustomGroupTabs(group, computers, users, printers, missingDNs);
                }
                else
                {
                    // Single type - use existing tab logic
                    var tabName = group.Name;
                    var existingTab = OutputTabs.FirstOrDefault(t => 
                        t.Type == OutputTabType.Results && t.CustomGroupId == group.Id);

                    if (existingTab == null)
                    {
                        existingTab = new OutputTabViewModel(tabName, OutputTabType.Results)
                        {
                            CanClose = true,
                            CustomGroupId = group.Id,
                            CustomGroupType = group.Type,
                            SourcePath = $"CustomGroup:{group.Name}"
                        };
                        OutputTabs.Add(existingTab);
                    }
                    else
                    {
                        existingTab.Name = tabName;
                        existingTab.SourcePath = $"CustomGroup:{group.Name}";
                        existingTab.ClearFilters();
                    }

                    existingTab.SetAdObjectResults(results, DateTime.Now);
                    PopulateCachedLastUserData(results);
                    existingTab.MissingMemberDNs = missingDNs;
                    existingTab.NotifyResultsChanged();
                    SelectedOutputTab = existingTab;
                    
                    // Resolve IP addresses in background for single-type groups
                    if (hasComputers)
                    {
                        _ = ResolveComputerIpAddressesAsync(computers);
                    }
                    if (hasPrinters)
                    {
                        _ = ResolvePrinterIpAddressesAsync(printers);
                    }
                }

                // Save to cache for persistence (combined results)
                _cacheService.SetGroupCache(group.Id, group.Name, results);

                var missingCount = missingDNs.Count;
                var splitNote = typeCount > 1 ? $" (split into {typeCount} tabs)" : "";
                StatusMessage = missingCount > 0 
                    ? $"Loaded {group.Name}: {results.Count} items ({missingCount} not found){splitNote}"
                    : $"Loaded {group.Name}: {results.Count} items{splitNote}";
            }
            catch (Exception ex)
            {
                SetError($"Failed to load group: {ex.Message}");
            }
            finally
            {
                ClearBusy();
            }
        }

        /// <summary>
        /// Creates separate tabs for each object type in a custom group when it contains mixed types.
        /// </summary>
        private void CreateSplitCustomGroupTabs(CustomGroup group, 
            List<AdObjectInfo> computers, List<AdObjectInfo> users, List<AdObjectInfo> printers,
            List<string>? missingDNs = null)
        {
            OutputTabViewModel? firstTab = null;
            var sourcePath = $"CustomGroup:{group.Name}";

            // Remove any existing single tab for this group
            var oldTab = OutputTabs.FirstOrDefault(t => t.CustomGroupId == group.Id && t.Name == group.Name);
            if (oldTab != null)
            {
                OutputTabs.Remove(oldTab);
            }

            // Distribute missing DNs to appropriate tabs (best effort)
            var computerMissing = new List<string>();
            var userMissing = new List<string>();
            var printerMissing = new List<string>();
            
            if (missingDNs != null)
            {
                // Missing objects default to the group's configured type
                switch (group.Type)
                {
                    case CustomGroupType.Computer:
                        computerMissing.AddRange(missingDNs);
                        break;
                    case CustomGroupType.User:
                        userMissing.AddRange(missingDNs);
                        break;
                    case CustomGroupType.Printer:
                        printerMissing.AddRange(missingDNs);
                        break;
                }
            }

            // Create Computers tab if there are computers
            if (computers.Count > 0)
            {
                var tabName = $"Computers • {group.Name}";
                var existingTab = OutputTabs.FirstOrDefault(t => t.Name == tabName);
                
                if (existingTab == null)
                {
                    existingTab = new OutputTabViewModel(tabName, OutputTabType.Computers)
                    {
                        CanClose = true,
                        CustomGroupId = group.Id,
                        CustomGroupType = CustomGroupType.Computer,
                        SourcePath = sourcePath
                    };
                    OutputTabs.Add(existingTab);
                }
                else
                {
                    existingTab.SourcePath = sourcePath;
                    existingTab.ClearFilters();
                }
                
                existingTab.SetAdObjectResults(computers, DateTime.Now);
                PopulateCachedLastUserData(computers);
                existingTab.MissingMemberDNs = computerMissing;
                existingTab.NotifyResultsChanged();
                firstTab ??= existingTab;
            }

            // Create Users tab if there are users
            if (users.Count > 0)
            {
                var tabName = $"Users • {group.Name}";
                var existingTab = OutputTabs.FirstOrDefault(t => t.Name == tabName);
                
                if (existingTab == null)
                {
                    existingTab = new OutputTabViewModel(tabName, OutputTabType.Users)
                    {
                        CanClose = true,
                        CustomGroupId = group.Id,
                        CustomGroupType = CustomGroupType.User,
                        SourcePath = sourcePath
                    };
                    OutputTabs.Add(existingTab);
                }
                else
                {
                    existingTab.SourcePath = sourcePath;
                    existingTab.ClearFilters();
                }
                
                existingTab.SetAdObjectResults(users, DateTime.Now);
                existingTab.MissingMemberDNs = userMissing;
                existingTab.NotifyResultsChanged();
                firstTab ??= existingTab;
            }

            // Create Printers tab if there are printers
            if (printers.Count > 0)
            {
                var tabName = $"Printers • {group.Name}";
                var existingTab = OutputTabs.FirstOrDefault(t => t.Name == tabName);
                
                if (existingTab == null)
                {
                    existingTab = new OutputTabViewModel(tabName, OutputTabType.Printers)
                    {
                        CanClose = true,
                        CustomGroupId = group.Id,
                        CustomGroupType = CustomGroupType.Printer,
                        SourcePath = sourcePath
                    };
                    OutputTabs.Add(existingTab);
                }
                else
                {
                    existingTab.SourcePath = sourcePath;
                    existingTab.ClearFilters();
                }
                
                existingTab.SetAdObjectResults(printers, DateTime.Now);
                PopulateCachedLastUserData(printers);  // Populate IP history from cache
                existingTab.MissingMemberDNs = printerMissing;
                existingTab.NotifyResultsChanged();
                firstTab ??= existingTab;
            }

            // Resolve IP addresses in background (non-blocking)
            if (computers.Count > 0)
            {
                _ = ResolveComputerIpAddressesAsync(computers);
            }
            if (printers.Count > 0)
            {
                _ = ResolvePrinterIpAddressesAsync(printers);
            }

            // Select the first tab with results
            if (firstTab != null)
            {
                SelectedOutputTab = firstTab;
            }
        }

        /// <summary>
        /// Loads custom group results from cache, handling multi-type splits.
        /// </summary>
        private void LoadCustomGroupFromCache(CustomGroup group, CacheEntry cachedEntry)
        {
            var allResults = cachedEntry.Results.ToList();
            
            // Split results by type
            var computers = allResults.Where(r => r.ObjectType == AdObjectType.Computer).ToList();
            var users = allResults.Where(r => r.ObjectType == AdObjectType.User).ToList();
            var printers = allResults.Where(r => r.ObjectType == AdObjectType.Printer).ToList();
            
            var hasComputers = computers.Count > 0;
            var hasUsers = users.Count > 0;
            var hasPrinters = printers.Count > 0;
            var typeCount = (hasComputers ? 1 : 0) + (hasUsers ? 1 : 0) + (hasPrinters ? 1 : 0);

            // Recalculate missing DNs from cached results
            var missingFromCache = allResults
                .Where(r => r.Description == "(Not found in AD)" && !string.IsNullOrEmpty(r.DistinguishedName))
                .Select(r => r.DistinguishedName!)
                .ToList();

            if (typeCount > 1)
            {
                // Multiple types - create split tabs
                CreateSplitCustomGroupTabs(group, computers, users, printers, missingFromCache);
            }
            else
            {
                // Single type - use existing tab logic
                var tabName = group.Name;
                var existingTab = OutputTabs.FirstOrDefault(t => 
                    t.Type == OutputTabType.Results && t.CustomGroupId == group.Id);

                if (existingTab == null)
                {
                    existingTab = new OutputTabViewModel(tabName, OutputTabType.Results)
                    {
                        CanClose = true,
                        CustomGroupId = group.Id,
                        CustomGroupType = group.Type,
                        SourcePath = $"CustomGroup:{group.Name}"
                    };
                    OutputTabs.Add(existingTab);
                }
                else
                {
                    existingTab.Name = tabName;
                    existingTab.SourcePath = $"CustomGroup:{group.Name}";
                    existingTab.ClearFilters();
                }

                existingTab.SetAdObjectResults(allResults, cachedEntry.LastUpdated);
                PopulateCachedLastUserData(allResults);
                existingTab.MissingMemberDNs = missingFromCache;
                existingTab.NotifyResultsChanged();
                SelectedOutputTab = existingTab;
                
                // Resolve IP addresses in background for single-type groups
                if (hasComputers)
                {
                    _ = ResolveComputerIpAddressesAsync(computers);
                }
                if (hasPrinters)
                {
                    _ = ResolvePrinterIpAddressesAsync(printers);
                }
            }
        }

        [RelayCommand]
        private async Task RemoveMissingMembersAsync()
        {
            var tab = SelectedOutputTab;
            if (tab == null || string.IsNullOrEmpty(tab.CustomGroupId)) return;

            var missingDNs = tab.MissingMemberDNs;
            if (missingDNs == null || missingDNs.Count == 0) return;

            var result = MessageBox.Show(
                $"Remove {missingDNs.Count} missing object(s) from this group?",
                "Remove Missing Objects",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _customGroupService.RemoveMissingMembersAsync(tab.CustomGroupId, missingDNs);
                RefreshCustomGroupCollections();
                
                // Invalidate cache so refresh queries fresh data
                _cacheService.InvalidateGroupCache(tab.CustomGroupId);
                
                // Refresh the tab
                var group = _customGroupService.GetGroup(tab.CustomGroupId);
                if (group != null)
                {
                    await OpenCustomGroupAsync(group);
                }
                
                StatusMessage = $"Removed {missingDNs.Count} missing objects from group";
            }
        }

        #endregion

        #region Navigation Commands

        private async Task NavigateToPathAsync(string dn, bool forceRefresh = false)
        {
            try
            {
                SetBusy("Connecting...");

                _currentEntry?.Dispose();
                _currentEntry = _ldapService.CreateEntry(dn);
                
                // Force connection test by accessing a property
                await Task.Run(() => { var _ = _currentEntry.NativeGuid; });

                CurrentPath = dn;
                
                // Build navigation stack from parent DNs so back button works
                _navigationStack.Clear();
                BuildNavigationStackFromDn(dn);
                CanGoBack = _navigationStack.Count > 0;

                // Update current view info
                CurrentViewName = GetDisplayNameFromDn(dn);
                HasCurrentView = true;
                SelectedGroup = null; // Clear group selection when navigating to domain

                // Check cache first (unless force refresh)
                var cacheTtl = App.Settings.Current.CacheTtlMinutes;
                if (!forceRefresh && cacheTtl > 0)
                {
                    var cachedEntry = _cacheService.GetDomainCache(dn, cacheTtl);
                    if (cachedEntry != null)
                    {
                        // Use cached results
                        LoadFromCache(cachedEntry);
                        IsViewingCachedResults = true;
                        LastUpdatedDisplay = cachedEntry.AgeDisplay;
                        StatusMessage = $"Loaded from cache ({cachedEntry.AgeDisplay})";
                        ClearBusy();
                        return;
                    }
                }

                // No valid cache, do fresh scan
                await ScanCurrentFolderAsync(saveToCache: true);
            }
            catch (Exception ex)
            {
                ConnectionStatus = "Disconnected";
                HasCurrentView = false;
                SetError($"Navigation failed: {ex.Message}");
                throw; // Re-throw so caller knows it failed
            }
            finally
            {
                ClearBusy();
            }
        }

        /// <summary>
        /// Extracts a display name from a distinguished name
        /// </summary>
        private string GetDisplayNameFromDn(string dn)
        {
            if (string.IsNullOrEmpty(dn)) return string.Empty;
            
            var parts = dn.Split(',');
            if (parts.Length == 0) return dn;
            
            var first = parts[0];
            if (first.Contains('='))
            {
                return first.Split('=')[1];
            }
            return first;
        }

        /// <summary>
        /// Loads results from a cache entry
        /// </summary>
        private void LoadFromCache(CacheEntry entry)
        {
            Subfolders.Clear();
            foreach (var folder in entry.Subfolders)
            {
                Subfolders.Add(folder);
            }

            AdObjects.Clear();
            Computers.Clear();
            
            foreach (var obj in entry.Results)
            {
                AdObjects.Add(obj);
                if (obj.ObjectType == AdObjectType.Computer)
                {
                    Computers.Add(obj);
                }
            }

            ComputerCount = AdObjects.Count;

            // Update result tabs with cache info - pass source path for export organization
            var computerObjects = entry.Results.Where(o => o.ObjectType == AdObjectType.Computer).ToList();
            var users = entry.Results.Where(o => o.ObjectType == AdObjectType.User).ToList();
            var printers = entry.Results.Where(o => o.ObjectType == AdObjectType.Printer).ToList();
            UpdateResultTabs(computerObjects, users, printers, entry.LastUpdated, isFromCache: true, sourcePath: entry.Key);
        }

        /// <summary>
        /// Builds the navigation stack from parent DNs so the back button works when navigating to deep paths
        /// </summary>
        private void BuildNavigationStackFromDn(string dn)
        {
            if (string.IsNullOrWhiteSpace(dn)) return;

            // Parse DN into components (e.g., "OU=Anchorage,OU=VISN20,DC=v20,DC=med,DC=va,DC=gov")
            var parts = dn.Split(',');
            if (parts.Length <= 1) return; // No parent paths

            // Build parent paths from bottom up (most recent = domain root, oldest = direct parent)
            // Stack is LIFO, so we push in order from domain root to direct parent
            var parentPaths = new List<string>();
            
            for (int i = 1; i < parts.Length; i++)
            {
                // Join remaining parts to form parent DN
                var parentDn = string.Join(",", parts.Skip(i));
                
                // Stop at domain root (DC=...,DC=...,DC=...,DC=...)
                // We don't want to go above the domain level
                if (parentDn.StartsWith("DC=", StringComparison.OrdinalIgnoreCase) && 
                    !parentDn.Contains("OU=", StringComparison.OrdinalIgnoreCase) &&
                    !parentDn.Contains("CN=", StringComparison.OrdinalIgnoreCase))
                {
                    // This is the domain root - add it and stop
                    parentPaths.Add(parentDn);
                    break;
                }
                
                parentPaths.Add(parentDn);
            }

            // Push in reverse order (domain root first, direct parent last)
            // So when we pop, we get direct parent first
            parentPaths.Reverse();
            foreach (var path in parentPaths)
            {
                _navigationStack.Push(path);
            }
        }

        [RelayCommand]
        private async Task OpenFolderAsync(FolderItem folder)
        {
            if (folder == null) return;

            try
            {
                SetBusy("Opening folder...");

                _navigationStack.Push(CurrentPath);
                CanGoBack = true;

                _currentEntry?.Dispose();
                _currentEntry = _ldapService.CreateEntry(folder.DistinguishedName);
                CurrentPath = folder.DistinguishedName;

                // Update view state
                CurrentViewName = folder.Name;
                HasCurrentView = true;
                SelectedGroup = null;

                // Check cache first
                var cacheTtl = App.Settings.Current.CacheTtlMinutes;
                if (cacheTtl > 0)
                {
                    var cachedEntry = _cacheService.GetDomainCache(folder.DistinguishedName, cacheTtl);
                    if (cachedEntry != null)
                    {
                        LoadFromCache(cachedEntry);
                        IsViewingCachedResults = true;
                        LastUpdatedDisplay = cachedEntry.AgeDisplay;
                        StatusMessage = $"Loaded from cache ({cachedEntry.AgeDisplay})";
                        return;
                    }
                }

                // Check if this path has preloaded child domains
                if (!TryPreloadChildDomains(folder.DistinguishedName))
                {
                    // No preloaded domains, scan with caching
                    await ScanCurrentFolderAsync(saveToCache: true);
                }
            }
            catch (Exception ex)
            {
                SetError($"Failed to open folder: {ex.Message}");
            }
            finally
            {
                ClearBusy();
            }
        }

        [RelayCommand]
        private async Task GoBackAsync()
        {
            if (_navigationStack.Count == 0) return;

            try
            {
                SetBusy("Going back...");

                var previousDn = _navigationStack.Pop();
                CanGoBack = _navigationStack.Count > 0;

                _currentEntry?.Dispose();
                _currentEntry = _ldapService.CreateEntry(previousDn);
                CurrentPath = previousDn;

                // Update view state
                CurrentViewName = GetDisplayNameFromDn(previousDn);
                HasCurrentView = true;
                SelectedGroup = null;

                // Check cache first
                var cacheTtl = App.Settings.Current.CacheTtlMinutes;
                if (cacheTtl > 0)
                {
                    var cachedEntry = _cacheService.GetDomainCache(previousDn, cacheTtl);
                    if (cachedEntry != null)
                    {
                        LoadFromCache(cachedEntry);
                        IsViewingCachedResults = true;
                        LastUpdatedDisplay = cachedEntry.AgeDisplay;
                        StatusMessage = $"Loaded from cache ({cachedEntry.AgeDisplay})";
                        return;
                    }
                }

                // No cache, do fresh scan
                await ScanCurrentFolderAsync(saveToCache: true);
            }
            catch (Exception ex)
            {
                SetError($"Navigation failed: {ex.Message}");
            }
            finally
            {
                ClearBusy();
            }
        }

        [RelayCommand]
        private async Task GoToRootAsync()
        {
            if (SelectedDomain == null) return;

            _navigationStack.Clear();
            CanGoBack = false;

            await NavigateToPathAsync(SelectedDomain.DistinguishedName);
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            // Check if current tab is a custom group (Computer/User/Printer group)
            if (SelectedOutputTab != null && !string.IsNullOrEmpty(SelectedOutputTab.CustomGroupId))
            {
                var group = _customGroupService.GetGroup(SelectedOutputTab.CustomGroupId);
                if (group != null)
                {
                    // Invalidate cache and refresh just this custom group
                    _cacheService.InvalidateGroupCache(group.Id);
                    await OpenCustomGroupAsync(group);
                    return;
                }
            }

            // Check if current tab is a domain target group
            if (SelectedOutputTab != null && !string.IsNullOrEmpty(SelectedOutputTab.TargetGroupId))
            {
                var tabGroup = App.TargetGroups.Groups.FirstOrDefault(g => g.Id == SelectedOutputTab.TargetGroupId);
                if (tabGroup != null)
                {
                    // Temporarily set SelectedGroup to refresh the correct group
                    var previousGroup = SelectedGroup;
                    SelectedGroup = tabGroup;
                    await SearchGroupAsync(forceRefresh: true);
                    // Restore previous selection only if it was different
                    if (previousGroup != tabGroup)
                    {
                        SelectedGroup = previousGroup;
                    }
                    return;
                }
            }
            
            if (SelectedGroup != null)
            {
                // Force refresh the current domain group
                await SearchGroupAsync(forceRefresh: true);
            }
            else if (_currentEntry != null)
            {
                // Force refresh the current domain path
                await ScanCurrentFolderAsync(saveToCache: true, forceRefresh: true);
            }
        }

        /// <summary>
        /// Forces a refresh of the current view, bypassing the cache
        /// </summary>
        [RelayCommand]
        private async Task ForceRefreshAsync()
        {
            await RefreshAsync();
        }

        private async Task ScanCurrentFolderAsync(bool saveToCache = false, bool forceRefresh = false)
        {
            if (_currentEntry == null) return;

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                SetBusy("Scanning...");
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // Scan subfolders
                var folders = await _ldapService.GetChildFoldersAsync(_currentEntry, _currentCts.Token);
                Subfolders.Clear();
                foreach (var folder in folders)
                {
                    Subfolders.Add(folder);
                }

                // Scan AD objects - query computers, users, and printers in parallel
                AdObjects.Clear();
                Computers.Clear();
                
                    // Convert current DN to friendly source path
                    var currentSourcePath = new TargetPath(CurrentPath).DisplayPath;
                    
                    // Run all queries in parallel
                    var computersTask = _ldapService.QueryComputersAsync(
                        _currentEntry, SearchScope.OneLevel, App.Settings.Current.ColumnSettings,
                        currentSourcePath, new Progress<string>(s => BusyMessage = s), _currentCts.Token);
                    
                    var usersTask = _ldapService.QueryUsersAsync(
                        _currentEntry, SearchScope.OneLevel, App.Settings.Current.ColumnSettings,
                        currentSourcePath, new Progress<string>(s => BusyMessage = s), _currentCts.Token);

                    var printersTask = _ldapService.QueryPrintersAsync(
                        _currentEntry, SearchScope.OneLevel, App.Settings.Current.ColumnSettings,
                        currentSourcePath, new Progress<string>(s => BusyMessage = s), _currentCts.Token);

                    await Task.WhenAll(computersTask, usersTask, printersTask);

                    var computers = await computersTask;
                    var users = await usersTask;
                    var printers = await printersTask;

                    // Convert computers to AdObjectInfo and add to collections
                    var computerObjects = new List<AdObjectInfo>();
                    foreach (var computer in computers)
                    {
                        Computers.Add(computer);
                        var adObject = new AdObjectInfo
                        {
                            Type = "Computer",  // Computers have objectCategory=computer
                            Name = computer.Name,
                            DnsHostName = computer.DnsHostName,
                            Description = computer.Description,
                            OperatingSystem = computer.OperatingSystem,
                            IsEnabled = computer.IsEnabled,
                            LastLogon = computer.LastLogon,
                            DistinguishedName = computer.DistinguishedName,
                            SourcePath = computer.SourcePath,
                            ObjectGuid = computer.ObjectGuid
                        };
                        AdObjects.Add(adObject);
                        computerObjects.Add(adObject);
                    }

                    // Add users to AdObjects collection
                    foreach (var user in users)
                    {
                        AdObjects.Add(user);
                    }

                    // Cache user emails and populate computer history for lookup in modals
                    if (users.Count > 0)
                    {
                        CacheUserEmails(users);
                        PopulateUserComputerHistory(users);
                        _ = ResolveSnowIdsAsync(users, _currentCts?.Token ?? CancellationToken.None);
                    }

                    // Add printers to AdObjects collection
                    foreach (var printer in printers)
                    {
                        AdObjects.Add(printer);
                    }
                    
                    ComputerCount = AdObjects.Count;

                    // Manage result tabs based on what was found - pass source path for export organization
                    UpdateResultTabs(computerObjects, users.ToList(), printers, sourcePath: CurrentPath);
                    
                    // Update LastSeenInAd for computers in the cache (background, non-blocking)
                    if (computers.Count > 0)
                    {
                        var computersToUpdate = computers
                            .Where(c => c.ObjectGuid.HasValue)
                            .Select(c => (c.ObjectGuid!.Value, c.Name, c.DistinguishedName))
                            .ToList();
                        
                        _ = Task.Run(() => _computerCacheService.TouchLastSeenBatch(computersToUpdate));
                    }

                stopwatch.Stop();
                LastQueryTime = $"{stopwatch.ElapsedMilliseconds}ms";
                
                // Save to cache if requested
                if (saveToCache && !string.IsNullOrEmpty(CurrentPath))
                {
                    _cacheService.SetDomainCache(
                        CurrentPath, 
                        CurrentViewName,
                        AdObjects.ToList(), 
                        Subfolders.ToList());
                    
                    IsViewingCachedResults = false;
                    LastUpdatedDisplay = "just now";
                }
                
                // Generate status message based on what was found
                var computerCount = AdObjects.Count(o => o.ObjectType == AdObjectType.Computer);
                var userCount = AdObjects.Count(o => o.ObjectType == AdObjectType.User);
                var objectTypeText = (computerCount > 0, userCount > 0) switch
                {
                    (true, true) => $"{computerCount} computers, {userCount} users",
                    (true, false) => $"{computerCount} computers",
                    (false, true) => $"{userCount} users",
                    _ => "0 objects"
                };
                var refreshNote = forceRefresh ? " (refreshed)" : "";
                StatusMessage = $"Scan complete: {Subfolders.Count} folders, {objectTypeText}{refreshNote}";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Scan cancelled";
            }
            catch (Exception ex)
            {
                SetError($"Scan failed: {ex.Message}");
            }
            finally
            {
                ClearBusy();
            }
        }

        [RelayCommand]
        private async Task QueryComputersAsync()
        {
            if (_currentEntry == null) return;

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                SetBusy("Querying computers...");
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // Always use OneLevel scope for manual query (use search for deep searches)
                var currentSourcePath = new TargetPath(CurrentPath).DisplayPath;
                var computers = await _ldapService.QueryComputersAsync(
                    _currentEntry, SearchScope.OneLevel, App.Settings.Current.ColumnSettings,
                    currentSourcePath, new Progress<string>(s => BusyMessage = s), _currentCts.Token);

                Computers.Clear();
                foreach (var computer in computers)
                {
                    Computers.Add(computer);
                }
                ComputerCount = Computers.Count;

                // Update results tab
                var resultsTab = OutputTabs.FirstOrDefault(t => t.Type == OutputTabType.Results);
                if (resultsTab != null)
                {
                    resultsTab.SetAdObjectResults(Computers.ToList());
                }

                // Update LastSeenInAd for computers in the cache (background, non-blocking)
                if (computers.Count > 0)
                {
                    var computersToUpdate = computers
                        .Where(c => c.ObjectGuid.HasValue)
                        .Select(c => (c.ObjectGuid!.Value, c.Name, c.DistinguishedName))
                        .ToList();
                    
                    _ = Task.Run(() => _computerCacheService.TouchLastSeenBatch(computersToUpdate));
                    
                    // Resolve IP addresses in background (non-blocking)
                    _ = ResolveComputerIpAddressesAsync(computers, _currentCts?.Token ?? CancellationToken.None);
                }

                stopwatch.Stop();
                LastQueryTime = $"{stopwatch.ElapsedMilliseconds}ms";
                StatusMessage = $"Found {ComputerCount} computers";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Query cancelled";
            }
            catch (Exception ex)
            {
                SetError($"Query failed: {ex.Message}");
            }
            finally
            {
                ClearBusy();
            }
        }

        #endregion

        // ===================================================================================
        // REMOVED SEARCH BAR - Legacy code left for potential reimplementation later
        // The Search Bar was removed to simplify the UX. Users should select a Domain Group
        // (Computers, Users, Printers) and use the per-column filters instead.
        // ===================================================================================
        /*
        #region Search Commands

        [RelayCommand]
        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) return;

            // Check if at least one type is selected
            var selectedTypes = SearchTypes.Where(t => t.IsSelected).ToList();
            if (!selectedTypes.Any())
            {
                SetError("Please select at least one search type (Computers, Users, or Printers)");
                return;
            }

            // Handle "Entire Domain" option - currently deactivated
            if (SelectedSearchScope == SearchScopeOption.EntireDomain)
            {
                SetError("Entire Domain search is not yet enabled. Please use Current Folder options.");
                return;
            }

            // Handle "Selected Group" option
            if (SelectedSearchScope == SearchScopeOption.SelectedGroup)
            {
                if (SelectedGroup == null)
                {
                    SetError("Please select a group first (click a group button or select one from the left panel)");
                    return;
                }

                await SearchGroupAsync();
                return;
            }

            // Determine search base - use current entry
            DirectoryEntry? searchBase = null;
            bool disposeSearchBase = false;

            if (_currentEntry != null)
            {
                searchBase = _currentEntry;
            }
            else if (SelectedDomain != null)
            {
                // Fall back to domain root if no folder selected
                searchBase = _ldapService.CreateEntry(SelectedDomain.DistinguishedName);
                disposeSearchBase = true;
            }
            else
            {
                SetError("Please select a folder or domain first");
                return;
            }

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                SetBusy($"Searching for '{SearchQuery}'...");

                // Determine LDAP search scope based on selected option
                var scope = SelectedSearchScope == SearchScopeOption.CurrentFolder 
                    ? SearchScope.OneLevel 
                    : SearchScope.Subtree;

                // Get selected columns for each type
                var computerColumns = AllSearchColumns
                    .Where(c => c.ObjectType == SearchObjectType.Computer && c.IsSelected)
                    .Select(c => c.LdapAttribute)
                    .ToList();

                var userColumns = AllSearchColumns
                    .Where(c => c.ObjectType == SearchObjectType.User && c.IsSelected)
                    .Select(c => c.LdapAttribute)
                    .ToList();

                var printerColumns = AllSearchColumns
                    .Where(c => c.ObjectType == SearchObjectType.Printer && c.IsSelected)
                    .Select(c => c.LdapAttribute)
                    .ToList();

                // Build parallel search tasks
                var searchComputers = selectedTypes.Any(t => t.ObjectType == SearchObjectType.Computer) && computerColumns.Any();
                var searchUsers = selectedTypes.Any(t => t.ObjectType == SearchObjectType.User) && userColumns.Any();
                var searchPrinters = selectedTypes.Any(t => t.ObjectType == SearchObjectType.Printer) && printerColumns.Any();

                List<AdObjectInfo>? computerResults = null;
                List<AdObjectInfo>? userResults = null;
                List<AdObjectInfo>? printerResults = null;
                var errors = new List<string>();

                // Create tasks
                Task<List<AdObjectInfo>>? computersTask = null;
                Task<List<AdObjectInfo>>? usersTask = null;
                Task<List<AdObjectInfo>>? printersTask = null;

                if (searchComputers)
                {
                    computersTask = _ldapService.SearchComputersAsync(
                        searchBase!, SearchQuery, scope, App.Settings.Current.ColumnSettings,
                        IncludeDisabledInSearch, computerColumns,
                        new Progress<string>(s => BusyMessage = s), _currentCts.Token);
                }

                if (searchUsers)
                {
                    usersTask = _ldapService.SearchUsersAsync(
                        searchBase!, SearchQuery, scope, App.Settings.Current.ColumnSettings,
                        IncludeDisabledInSearch, userColumns,
                        new Progress<string>(s => BusyMessage = s), _currentCts.Token);
                }

                if (searchPrinters)
                {
                    printersTask = _ldapService.SearchPrintersAsync(
                        searchBase!, SearchQuery, scope, App.Settings.Current.ColumnSettings,
                        printerColumns,
                        new Progress<string>(s => BusyMessage = s), _currentCts.Token);
                }

                // Await each task individually to capture errors
                if (computersTask != null)
                {
                    try
                    {
                        computerResults = await computersTask;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Computer search failed: {ex.Message}");
                    }
                }

                if (usersTask != null)
                {
                    try
                    {
                        userResults = await usersTask;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"User search failed: {ex.Message}");
                    }
                }

                if (printersTask != null)
                {
                    try
                    {
                        printerResults = await printersTask;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Printer search failed: {ex.Message}");
                    }
                }

                // Report any errors
                if (errors.Any())
                {
                    SetError(string.Join("; ", errors));
                }

                // Update results and create/update tabs
                var totalCount = 0;

                // Handle computer results
                if (computerResults != null && computerResults.Any())
                {
                    Computers.Clear();
                    foreach (var computer in computerResults)
                    {
                        Computers.Add(computer);
                    }

                    // Find or create Computers search tab
                    var computersTab = OutputTabs.FirstOrDefault(t => t.Name == "Search: Computers");
                    if (computersTab == null)
                    {
                        computersTab = new OutputTabViewModel("Search: Computers", OutputTabType.Computers)
                        {
                            SourcePath = CurrentPath  // Use current AD path for export organization
                        };
                        OutputTabs.Add(computersTab);
                    }
                    else
                    {
                        computersTab.SourcePath = CurrentPath;
                    }
                    computersTab.SetAdObjectResults(computerResults);
                    PopulateCachedLastUserData(computerResults);
                    totalCount += computerResults.Count;
                    
                    // Update LastSeenInAd for computers in the cache (background, non-blocking)
                    var computersToUpdate = computerResults
                        .Where(c => c.ObjectGuid.HasValue)
                        .Select(c => (c.ObjectGuid!.Value, c.Name, c.DistinguishedName))
                        .ToList();
                    
                    if (computersToUpdate.Count > 0)
                    {
                        _ = Task.Run(() => _computerCacheService.TouchLastSeenBatch(computersToUpdate));
                    }
                }

                // Handle user results
                if (userResults != null && userResults.Any())
                {
                    // Find or create Users search tab
                    var usersTab = OutputTabs.FirstOrDefault(t => t.Name == "Search: Users");
                    if (usersTab == null)
                    {
                        usersTab = new OutputTabViewModel("Search: Users", OutputTabType.Users)
                        {
                            SourcePath = CurrentPath  // Use current AD path for export organization
                        };
                        OutputTabs.Add(usersTab);
                    }
                    else
                    {
                        usersTab.SourcePath = CurrentPath;
                    }
                    usersTab.SetAdObjectResults(userResults);
                    totalCount += userResults.Count;

                    // Cache user emails and populate computer history for lookup in modals
                    CacheUserEmails(userResults);
                    PopulateUserComputerHistory(userResults);
                    _ = ResolveSnowIdsAsync(userResults, _currentCts?.Token ?? CancellationToken.None);
                }

                // Handle printer results
                if (printerResults != null && printerResults.Any())
                {
                    // Find or create Printers search tab
                    var printersTab = OutputTabs.FirstOrDefault(t => t.Name == "Search: Printers");
                    if (printersTab == null)
                    {
                        printersTab = new OutputTabViewModel("Search: Printers", OutputTabType.Printers)
                        {
                            SourcePath = CurrentPath  // Use current AD path for export organization
                        };
                        OutputTabs.Add(printersTab);
                    }
                    else
                    {
                        printersTab.SourcePath = CurrentPath;
                    }
                    printersTab.SetAdObjectResults(printerResults);
                    PopulateCachedLastUserData(printerResults);  // Populate IP history from cache
                    totalCount += printerResults.Count;
                }

                ComputerCount = Computers.Count;

                // Select first tab with results
                if (computerResults != null && computerResults.Any())
                    SelectedOutputTab = OutputTabs.FirstOrDefault(t => t.Name == "Search: Computers");
                else if (userResults != null && userResults.Any())
                    SelectedOutputTab = OutputTabs.FirstOrDefault(t => t.Name == "Search: Users");
                else if (printerResults != null && printerResults.Any())
                    SelectedOutputTab = OutputTabs.FirstOrDefault(t => t.Name == "Search: Printers");

                // Resolve IP addresses in background (non-blocking)
                if (computerResults != null && computerResults.Any())
                {
                    _ = ResolveComputerIpAddressesAsync(computerResults, _currentCts?.Token ?? CancellationToken.None);
                }
                if (printerResults != null && printerResults.Any())
                {
                    _ = ResolvePrinterIpAddressesAsync(printerResults, _currentCts?.Token ?? CancellationToken.None);
                }

                // Build status message
                var statusParts = new List<string>();
                if (computerResults != null)
                    statusParts.Add($"{computerResults.Count} computers");
                if (userResults != null)
                    statusParts.Add($"{userResults.Count} users");
                if (printerResults != null)
                    statusParts.Add($"{printerResults.Count} printers");

                StatusMessage = $"Search complete: {string.Join(", ", statusParts)}";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Search cancelled";
            }
            catch (Exception ex)
            {
                SetError($"Search failed: {ex.Message}");
            }
            finally
            {
                if (disposeSearchBase)
                {
                    searchBase?.Dispose();
                }
                ClearBusy();
            }
        }

        [RelayCommand]
        private void ClearSearch()
        {
            SearchQuery = string.Empty;
        }
        */
        // END REMOVED SEARCH BAR LEGACY CODE

        /// <summary>
        /// Searches across all paths in the selected group sequentially.
        /// Queries all object types (computers, users, printers) regardless of group configuration,
        /// and splits results into separate tabs if multiple types are found.
        /// </summary>
        private async Task SearchGroupAsync(bool forceRefresh = false)
        {
            if (SelectedGroup == null || !SelectedGroup.Paths.Any())
            {
                SetError("Selected group has no paths");
                return;
            }

            // Update current view info
            CurrentViewName = SelectedGroup.Name;
            HasCurrentView = true;

            // Check cache first (unless force refresh)
            var cacheTtl = App.Settings.Current.CacheTtlMinutes;
            if (!forceRefresh && cacheTtl > 0)
            {
                var cachedEntry = _cacheService.GetGroupCache(SelectedGroup.Id, cacheTtl);
                if (cachedEntry != null)
                {
                    // Use cached results
                    LoadGroupFromCache(cachedEntry);
                    IsViewingCachedResults = true;
                    LastUpdatedDisplay = cachedEntry.AgeDisplay;
                    StatusMessage = $"Group '{SelectedGroup.Name}' loaded from cache ({cachedEntry.AgeDisplay})";
                    return;
                }
            }

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                // Collect results by type
                var allComputers = new List<AdObjectInfo>();
                var allUsers = new List<AdObjectInfo>();
                var allPrinters = new List<AdObjectInfo>();
                var errors = new List<string>();
                var pathCount = SelectedGroup.Paths.Count;

                for (int i = 0; i < pathCount; i++)
                {
                    var path = SelectedGroup.Paths[i];
                    
                    _currentCts.Token.ThrowIfCancellationRequested();
                    
                    // Build path prefix for progress messages
                    var pathDisplayName = !string.IsNullOrWhiteSpace(path.DisplayName) 
                        ? path.DisplayName 
                        : $"Path {i + 1}";
                    var pathPrefix = pathCount > 1 ? $"[{pathDisplayName}] " : "";
                    
                    SetBusy(pathCount > 1 
                        ? $"Searching path {i + 1}/{pathCount}: {pathDisplayName}..."
                        : $"Searching {pathDisplayName}...");

                    DirectoryEntry? searchBase = null;
                    try
                    {
                        searchBase = _ldapService.CreateEntry(path.DistinguishedName);

                        // Use Subtree scope for group searches to get all results under each path
                        var scope = SearchScope.Subtree;
                        
                        // Determine which object types to query based on group setting
                        var queryComputers = SelectedGroup.ObjectType == GroupObjectType.None || SelectedGroup.ObjectType == GroupObjectType.Computers;
                        var queryUsers = SelectedGroup.ObjectType == GroupObjectType.None || SelectedGroup.ObjectType == GroupObjectType.Users;
                        var queryPrinters = SelectedGroup.ObjectType == GroupObjectType.None || SelectedGroup.ObjectType == GroupObjectType.Printers;

                        // Prepare tasks list for parallel execution
                        var tasks = new List<Task>();
                        Task<List<AdObjectInfo>>? computerTask = null;
                        Task<List<AdObjectInfo>>? userTask = null;
                        Task<List<AdObjectInfo>>? printerTask = null;

                        if (queryComputers)
                        {
                            var computerColumns = AllSearchColumns
                                .Where(c => c.ObjectType == SearchObjectType.Computer && c.IsSelected)
                                .Select(c => c.LdapAttribute)
                                .ToList();

                            computerTask = _ldapService.SearchComputersAsync(
                                searchBase, SearchQuery, scope, App.Settings.Current.ColumnSettings,
                                IncludeDisabledInSearch, computerColumns,
                                new Progress<string>(s => BusyMessage = $"{pathPrefix}Computers: {s}"), 
                                _currentCts.Token);
                            tasks.Add(computerTask);
                        }

                        if (queryUsers)
                        {
                            var userColumns = AllSearchColumns
                                .Where(c => c.ObjectType == SearchObjectType.User && c.IsSelected)
                                .Select(c => c.LdapAttribute)
                                .ToList();

                            userTask = _ldapService.SearchUsersAsync(
                                searchBase, SearchQuery, scope, App.Settings.Current.ColumnSettings,
                                IncludeDisabledInSearch, userColumns,
                                new Progress<string>(s => BusyMessage = $"{pathPrefix}Users: {s}"), 
                                _currentCts.Token);
                            tasks.Add(userTask);
                        }

                        if (queryPrinters)
                        {
                            var printerColumns = AllSearchColumns
                                .Where(c => c.ObjectType == SearchObjectType.Printer && c.IsSelected)
                                .Select(c => c.LdapAttribute)
                                .ToList();

                            printerTask = _ldapService.SearchPrintersAsync(
                                searchBase, SearchQuery, scope, App.Settings.Current.ColumnSettings,
                                printerColumns,
                                new Progress<string>(s => BusyMessage = $"{pathPrefix}Printers: {s}"), 
                                _currentCts.Token);
                            tasks.Add(printerTask);
                        }

                        // Wait for all relevant queries to complete
                        await Task.WhenAll(tasks);

                        // Process computer results
                        if (computerTask != null)
                        {
                            var computerResults = await computerTask;
                            foreach (var c in computerResults)
                            {
                                c.SourcePath = path.DisplayPath;
                                allComputers.Add(c);
                            }
                            
                            // Update LastSeenInAd for computers in the cache (background, non-blocking)
                            if (computerResults.Count > 0)
                            {
                                var computersToUpdate = computerResults
                                    .Where(c => c.ObjectGuid.HasValue)
                                    .Select(c => (c.ObjectGuid!.Value, c.Name, c.DistinguishedName))
                                    .ToList();
                                
                                _ = Task.Run(() => _computerCacheService.TouchLastSeenBatch(computersToUpdate));
                            }
                        }

                        // Process user results
                        if (userTask != null)
                        {
                            var userResults = await userTask;
                            foreach (var user in userResults)
                            {
                                user.SourcePath = path.DisplayPath;
                                allUsers.Add(user);
                            }

                            // Cache user emails and populate computer history for lookup in modals
                            if (userResults.Count > 0)
                            {
                                CacheUserEmails(userResults);
                                PopulateUserComputerHistory(userResults);
                                _ = ResolveSnowIdsAsync(userResults, _currentCts?.Token ?? CancellationToken.None);
                            }
                        }

                        // Process printer results
                        if (printerTask != null)
                        {
                            var printerResults = await printerTask;
                            foreach (var printer in printerResults)
                            {
                                printer.SourcePath = path.DisplayPath;
                                allPrinters.Add(printer);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{path.DisplayName}: {ex.Message}");
                    }
                    finally
                    {
                        searchBase?.Dispose();
                    }
                }

                // Deduplicate by DistinguishedName (can happen when paths overlap, e.g., parent OU and child OU)
                allComputers = allComputers
                    .GroupBy(c => c.DistinguishedName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
                allUsers = allUsers
                    .GroupBy(u => u.DistinguishedName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
                allPrinters = allPrinters
                    .GroupBy(p => p.DistinguishedName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                // Combine all results for cache
                var allResults = new List<AdObjectInfo>();
                allResults.AddRange(allComputers);
                allResults.AddRange(allUsers);
                allResults.AddRange(allPrinters);

                // Determine if we have multiple object types
                var hasComputers = allComputers.Count > 0;
                var hasUsers = allUsers.Count > 0;
                var hasPrinters = allPrinters.Count > 0;
                var typeCount = (hasComputers ? 1 : 0) + (hasUsers ? 1 : 0) + (hasPrinters ? 1 : 0);

                // Create tabs based on results
                if (typeCount > 1)
                {
                    // Multiple types found - create split tabs
                    CreateSplitGroupTabs(SelectedGroup.Name, SelectedGroup.Id, allComputers, allUsers, allPrinters);
                    
                    // Resolve IP addresses in background (use filtered results from tabs to respect startup filters)
                    if (hasComputers)
                    {
                        var computersTab = OutputTabs.FirstOrDefault(t => t.Name == $"Computers \u2022 {SelectedGroup.Name}");
                        var dnsTargets = computersTab?.AdObjectResults ?? allComputers;
                        _ = ResolveComputerIpAddressesAsync(dnsTargets, _currentCts?.Token ?? CancellationToken.None);
                    }
                    if (hasPrinters)
                    {
                        var printersTab = OutputTabs.FirstOrDefault(t => t.Name == $"Printers \u2022 {SelectedGroup.Name}");
                        var dnsTargets = printersTab?.AdObjectResults ?? allPrinters;
                        _ = ResolvePrinterIpAddressesAsync(dnsTargets, _currentCts?.Token ?? CancellationToken.None);
                    }
                }
                else
                {
                    // Single type or no results - create one tab
                    var tabName = $"Group: {SelectedGroup.Name}";
                    var tabType = hasComputers ? OutputTabType.Computers :
                                  hasUsers ? OutputTabType.Users :
                                  hasPrinters ? OutputTabType.Printers :
                                  OutputTabType.Results;

                    var resultTab = OutputTabs.FirstOrDefault(t => t.Name == tabName);
                    if (resultTab == null)
                    {
                        resultTab = new OutputTabViewModel(tabName, tabType)
                        {
                            SourcePath = $"DomainGroup:{SelectedGroup.Name}",
                            TargetGroupId = SelectedGroup.Id
                        };
                        OutputTabs.Add(resultTab);
                    }
                    else
                    {
                        resultTab.SourcePath = $"DomainGroup:{SelectedGroup.Name}";
                        resultTab.TargetGroupId = SelectedGroup.Id;
                        resultTab.ClearFilters();
                    }
                    
                    resultTab.SetAdObjectResults(allResults, DateTime.Now, isFromCache: false);
                    PopulateCachedLastUserData(allResults);
                    ApplyStartupFilterIfExists(resultTab);
                    SelectedOutputTab = resultTab;
                    
                    // Resolve IP addresses in background (use filtered results to respect startup filters)
                    if (hasComputers)
                    {
                        var dnsTargets = resultTab.AdObjectResults?.Where(o => o.ObjectType == AdObjectType.Computer).ToList() ?? allComputers;
                        _ = ResolveComputerIpAddressesAsync(dnsTargets, _currentCts?.Token ?? CancellationToken.None);
                    }
                    if (hasPrinters)
                    {
                        var dnsTargets = resultTab.AdObjectResults?.Where(o => o.ObjectType == AdObjectType.Printer).ToList() ?? allPrinters;
                        _ = ResolvePrinterIpAddressesAsync(dnsTargets, _currentCts?.Token ?? CancellationToken.None);
                    }
                }

                // Save all results to cache (combined)
                _cacheService.SetGroupCache(SelectedGroup.Id, SelectedGroup.Name, allResults);

                // Report results
                var refreshNote = forceRefresh ? " (refreshed)" : "";
                var splitNote = typeCount > 1 ? $" (split into {typeCount} tabs)" : "";
                if (errors.Any())
                {
                    SetError($"Found {allResults.Count} results{splitNote}. Errors: {string.Join("; ", errors)}");
                }
                else
                {
                    StatusMessage = $"Group search complete: {allResults.Count} results from {pathCount} paths{refreshNote}{splitNote}";
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Group search cancelled";
            }
            catch (Exception ex)
            {
                SetError($"Group search failed: {ex.Message}");
            }
            finally
            {
                ClearBusy();
            }
        }

        /// <summary>
        /// Creates separate tabs for each object type when a group contains mixed types.
        /// Uses naming convention: "Computers • GroupName", "Users • GroupName", etc.
        /// </summary>
        private void CreateSplitGroupTabs(string groupName, string groupId, 
            List<AdObjectInfo> computers, List<AdObjectInfo> users, List<AdObjectInfo> printers,
            DateTime? lastUpdated = null, bool isFromCache = false)
        {
            OutputTabViewModel? firstTab = null;
            var sourcePath = $"DomainGroup:{groupName}";

            // Remove any existing tabs for this group (they might be the old single-type tab)
            var oldGroupTab = OutputTabs.FirstOrDefault(t => t.Name == $"Group: {groupName}");
            if (oldGroupTab != null)
            {
                OutputTabs.Remove(oldGroupTab);
            }

            // Create Computers tab if there are computers
            if (computers.Count > 0)
            {
                var tabName = $"Computers • {groupName}";
                var existingTab = OutputTabs.FirstOrDefault(t => t.Name == tabName);
                
                if (existingTab == null)
                {
                    existingTab = new OutputTabViewModel(tabName, OutputTabType.Computers)
                    {
                        CanClose = true,
                        SourcePath = sourcePath,
                        TargetGroupId = groupId
                    };
                    OutputTabs.Add(existingTab);
                }
                else
                {
                    existingTab.SourcePath = sourcePath;
                    existingTab.TargetGroupId = groupId;
                    existingTab.ClearFilters();
                }
                
                existingTab.SetAdObjectResults(computers, lastUpdated ?? DateTime.Now, isFromCache);
                PopulateCachedLastUserData(computers);
                ApplyStartupFilterIfExists(existingTab);
                firstTab ??= existingTab;
            }

            // Create Users tab if there are users
            if (users.Count > 0)
            {
                var tabName = $"Users • {groupName}";
                var existingTab = OutputTabs.FirstOrDefault(t => t.Name == tabName);
                
                if (existingTab == null)
                {
                    existingTab = new OutputTabViewModel(tabName, OutputTabType.Users)
                    {
                        CanClose = true,
                        SourcePath = sourcePath,
                        TargetGroupId = groupId
                    };
                    OutputTabs.Add(existingTab);
                }
                else
                {
                    existingTab.SourcePath = sourcePath;
                    existingTab.TargetGroupId = groupId;
                    existingTab.ClearFilters();
                }
                
                existingTab.SetAdObjectResults(users, lastUpdated ?? DateTime.Now, isFromCache);
                ApplyStartupFilterIfExists(existingTab);
                firstTab ??= existingTab;
            }

            // Create Printers tab if there are printers
            if (printers.Count > 0)
            {
                var tabName = $"Printers • {groupName}";
                var existingTab = OutputTabs.FirstOrDefault(t => t.Name == tabName);
                
                if (existingTab == null)
                {
                    existingTab = new OutputTabViewModel(tabName, OutputTabType.Printers)
                    {
                        CanClose = true,
                        SourcePath = sourcePath,
                        TargetGroupId = groupId
                    };
                    OutputTabs.Add(existingTab);
                }
                else
                {
                    existingTab.SourcePath = sourcePath;
                    existingTab.TargetGroupId = groupId;
                    existingTab.ClearFilters();
                }
                
                existingTab.SetAdObjectResults(printers, lastUpdated ?? DateTime.Now, isFromCache);
                PopulateCachedLastUserData(printers);  // Populate IP history from cache
                ApplyStartupFilterIfExists(existingTab);
                firstTab ??= existingTab;
            }

            // Select the first tab with results
            if (firstTab != null)
            {
                SelectedOutputTab = firstTab;
            }
        }

        /// <summary>
        /// Loads group results from a cache entry.
        /// Handles multi-type cached results by splitting into separate tabs.
        /// </summary>
        private void LoadGroupFromCache(CacheEntry entry)
        {
            if (SelectedGroup == null) return;

            var allResults = entry.Results.ToList();
            
            // Split results by type
            var computers = allResults.Where(r => r.ObjectType == AdObjectType.Computer).ToList();
            var users = allResults.Where(r => r.ObjectType == AdObjectType.User).ToList();
            var printers = allResults.Where(r => r.ObjectType == AdObjectType.Printer).ToList();
            
            var hasComputers = computers.Count > 0;
            var hasUsers = users.Count > 0;
            var hasPrinters = printers.Count > 0;
            var typeCount = (hasComputers ? 1 : 0) + (hasUsers ? 1 : 0) + (hasPrinters ? 1 : 0);

            if (typeCount > 1)
            {
                // Multiple types in cache - create split tabs
                CreateSplitGroupTabs(SelectedGroup.Name, SelectedGroup.Id, computers, users, printers, 
                    entry.LastUpdated, isFromCache: true);
                
                // Resolve IP addresses in background (use filtered results from tabs to respect startup filters)
                if (hasComputers)
                {
                    var computersTab = OutputTabs.FirstOrDefault(t => t.Name == $"Computers \u2022 {SelectedGroup.Name}");
                    var dnsTargets = computersTab?.AdObjectResults ?? computers;
                    _ = ResolveComputerIpAddressesAsync(dnsTargets);
                }
                if (hasPrinters)
                {
                    var printersTab = OutputTabs.FirstOrDefault(t => t.Name == $"Printers \u2022 {SelectedGroup.Name}");
                    var dnsTargets = printersTab?.AdObjectResults ?? printers;
                    _ = ResolvePrinterIpAddressesAsync(dnsTargets);
                }
            }
            else
            {
                // Single type or no results - create one tab
                var tabName = $"Group: {SelectedGroup.Name}";
                var tabType = hasComputers ? OutputTabType.Computers :
                              hasUsers ? OutputTabType.Users :
                              hasPrinters ? OutputTabType.Printers :
                              OutputTabType.Results;

                var resultTab = OutputTabs.FirstOrDefault(t => t.Name == tabName);
                if (resultTab == null)
                {
                    resultTab = new OutputTabViewModel(tabName, tabType)
                    {
                        SourcePath = $"DomainGroup:{SelectedGroup.Name}",
                        TargetGroupId = SelectedGroup.Id
                    };
                    OutputTabs.Add(resultTab);
                }
                else
                {
                    resultTab.SourcePath = $"DomainGroup:{SelectedGroup.Name}";
                    resultTab.TargetGroupId = SelectedGroup.Id;
                    resultTab.ClearFilters();
                }
                
                resultTab.SetAdObjectResults(allResults, entry.LastUpdated, isFromCache: true);
                PopulateCachedLastUserData(allResults);
                ApplyStartupFilterIfExists(resultTab);
                SelectedOutputTab = resultTab;
                
                // Resolve IP addresses in background (use filtered results to respect startup filters)
                if (hasComputers)
                {
                    var dnsTargets = resultTab.AdObjectResults?.Where(o => o.ObjectType == AdObjectType.Computer).ToList() ?? computers;
                    _ = ResolveComputerIpAddressesAsync(dnsTargets);
                }
                if (hasPrinters)
                {
                    var dnsTargets = resultTab.AdObjectResults?.Where(o => o.ObjectType == AdObjectType.Printer).ToList() ?? printers;
                    _ = ResolvePrinterIpAddressesAsync(dnsTargets);
                }
            }
        }

        #region Network Tool Commands

        private IEnumerable<AdObjectInfo> GetNetworkTargets()
        {
            if (SelectedComputers.Any())
            {
                return SelectedComputers;
            }
            if (SelectedComputer != null)
            {
                return new[] { SelectedComputer };
            }
            return Enumerable.Empty<AdObjectInfo>();
        }

        /// <summary>
        /// Gets network targets as AdObjectInfo for use with network actions.
        /// Uses selected AdObjects from the current grid view.
        /// </summary>
        private IEnumerable<AdObjectInfo> GetNetworkTargetsAsAdObjects()
        {
            // Check for selected AdObjects first
            if (SelectedAdObjects.Any(o => o.ObjectType == AdObjectType.Computer))
            {
                return SelectedAdObjects.Where(o => o.ObjectType == AdObjectType.Computer);
            }

            // Also include printers for applicable operations
            if (SelectedAdObjects.Any(o => o.ObjectType == AdObjectType.Printer))
            {
                return SelectedAdObjects.Where(o => o.ObjectType == AdObjectType.Printer);
            }

            // Fall back to Computers collection targets (already AdObjectInfo)
            return GetNetworkTargets();
        }

        /// <summary>
        /// Gets a valid IP address from an AdObjectInfo for use in network operations.
        /// Returns null if no valid IP is available (still resolving, error, etc.)
        /// </summary>
        private static string? GetValidIpAddress(AdObjectInfo target)
        {
            var ip = target.IpAddress;
            
            // Check if the IP address is valid (not a status message)
            if (!string.IsNullOrEmpty(ip))
            {
                // Filter out status messages
                if (!ip.StartsWith("(") && !ip.Contains("resolving") && !ip.Contains("error") && !ip.Contains("not found"))
                {
                    // Validate it's actually an IP address
                    if (System.Net.IPAddress.TryParse(ip, out _))
                        return ip;
                }
            }
            
            // Fallback: check if Name or DnsHostName is itself an IP address (e.g., manual target entered as IP)
            if (System.Net.IPAddress.TryParse(target.Name, out _))
                return target.Name;
            
            if (!string.IsNullOrEmpty(target.DnsHostName) && System.Net.IPAddress.TryParse(target.DnsHostName, out _))
                return target.DnsHostName;
            
            return null;
        }

        /// <summary>
        /// Opens ServiceNow login window to authenticate for SNOW ID lookups
        /// </summary>
        [RelayCommand]
        private async Task SnowLogin()
        {
            await _snowUserService.AuthenticateAsync();
            OnPropertyChanged(nameof(IsSnowAuthenticated));
        }

        /// <summary>
        /// Opens the Push &amp; Run modal for the selected computers: copy a payload and run it
        /// silently over WinRM, then summarize per-machine outcomes in the network history panel.
        /// </summary>
        [RelayCommand]
        private void ShowPushRun()
        {
            var targets = GetNetworkTargetsAsAdObjects().ToList();
            if (targets.Count == 0)
            {
                SetError("No target selected for Push & Run");
                return;
            }

            if (_adminCredential == null)
            {
                System.Windows.MessageBox.Show(
                    "Admin login is required to push and run software on remote machines.",
                    "Push & Run", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            // Use the AD DNS hostname for WinRM/Kerberos; keep the short name for display.
            var windowTargets = targets
                .Select(t => (t.Name, t.TargetAddress))
                .ToList();

            var window = new Views.PushRunWindow(_pushRunService, _adminCredential, windowTargets)
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            window.ShowDialog();

            if (!window.DidRun || window.Results.Count == 0)
                return;

            var entry = AddNetworkHistoryEntry($"Push & Run: {window.PayloadName}",
                targets.Select(t => t.Name).ToList());

            var cmdResults = window.Results.Select(r => new Models.RemoteCommandResult
            {
                TargetName = r.ComputerName,
                Command = window.PayloadName,
                Success = r.Succeeded,
                Status = r.Detail,
                Output = r.Succeeded && r.RunMethod != null ? $"via {r.RunMethod}" : null,
                ErrorMessage = r.Succeeded ? null : r.Detail,
                EndTime = DateTime.Now
            }).ToList();

            entry.ResultText = Models.NetworkHistoryEntry.FormatRemoteCommandResults(cmdResults);
            entry.IsComplete = true;
            var successCount = window.Results.Count(r => r.Succeeded);
            var total = window.Results.Count;
            entry.StatusColor = successCount == total ? "#4CAF50" : successCount == 0 ? "#E53935" : "#FF9800";
            StatusMessage = $"Push & Run complete: {successCount}/{total} succeeded";
        }

        /// <summary>
        /// Opens the Terminal modal for the selected computers: run one PowerShell/cmd command on
        /// each machine over WinRM, then summarize per-machine outcomes in the network history panel.
        /// A single-machine selection is just a broadcast of one.
        /// </summary>
        [RelayCommand]
        private void ShowRemoteCommand()
        {
            var targets = GetNetworkTargetsAsAdObjects().ToList();
            if (targets.Count == 0)
            {
                SetError("No target selected for Terminal");
                return;
            }

            if (_adminCredential == null)
            {
                System.Windows.MessageBox.Show(
                    "Admin login is required to run commands on remote machines.",
                    "Terminal", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            // Use the AD DNS hostname for WinRM/Kerberos; keep the short name for display.
            var windowTargets = targets
                .Select(t => (t.Name, t.TargetAddress))
                .ToList();

            var window = new Views.RemoteCommandWindow(_networkService, _adminCredential, windowTargets)
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            window.ShowDialog();

            if (!window.DidRun || window.Results.Count == 0)
                return;

            var entry = AddNetworkHistoryEntry($"Terminal: {window.CommandText}",
                targets.Select(t => t.Name).ToList());
            entry.ResultText = Models.NetworkHistoryEntry.FormatRemoteCommandResults(window.Results.ToList());
            entry.IsComplete = true;
            var cmdSuccess = window.Results.Count(r => r.Success);
            var cmdTotal = window.Results.Count;
            entry.StatusColor = cmdSuccess == cmdTotal ? "#4CAF50" : cmdSuccess == 0 ? "#E53935" : "#FF9800";
            StatusMessage = $"Terminal complete: {cmdSuccess}/{cmdTotal} succeeded";
        }

        /// <summary>
        /// Opens (or reuses) the Vulnerabilities tab for the selected computers and starts a
        /// ServiceNow vulnerability scan for them.
        /// </summary>
        [RelayCommand]
        private async Task ShowVulnerabilities()
        {
            var computers = SelectedAdObjects
                .Where(o => o.ObjectType == AdObjectType.Computer)
                .Select(o => o.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (computers.Count == 0)
            {
                StatusMessage = "Select one or more computers to scan for vulnerabilities.";
                return;
            }

            var tab = OutputTabs.FirstOrDefault(t => t.Type == OutputTabType.Vulnerabilities);
            if (tab == null)
            {
                tab = new OutputTabViewModel("Vulnerabilities", OutputTabType.Vulnerabilities)
                {
                    VulnerabilityScan = CreateVulnerabilityScanViewModel()
                };
                OutputTabs.Add(tab);
            }
            else if (tab.VulnerabilityScan == null)
            {
                tab.VulnerabilityScan = CreateVulnerabilityScanViewModel();
            }

            SelectedOutputTab = tab;

            if (tab.VulnerabilityScan != null)
                await tab.VulnerabilityScan.InitializeAsync(computers);
        }

        /// <summary>
        /// Builds a vulnerability scan view model wired with SNOW fetch, the catalog, settings
        /// persistence, and read-only WinRM verification (using the current admin credential).
        /// </summary>
        private VulnerabilityScanViewModel CreateVulnerabilityScanViewModel()
        {
            return new VulnerabilityScanViewModel(
                _snowVulnService,
                _snowVulnService.Config,
                _catalogService,
                () => App.Settings.Save(),
                executeRemote: (host, cmd, ct) =>
                    _networkService.ExecuteRemoteCommandAsync(host, cmd, _adminCredential, 60000, ct),
                pingHost: async (host, ct) =>
                    (await _networkService.PingAsync(host, 5000, ct)).Success,
                resolveDnsRoundTrip: async (host, ct) =>
                {
                    var fwd = await _networkService.ResolveDnsForwardAsync(host, ct);
                    var ip = fwd.Success
                        ? (fwd.IpAddresses.FirstOrDefault(a => !a.Contains(':')) ?? fwd.IpAddresses.FirstOrDefault())
                        : null;
                    if (string.IsNullOrEmpty(ip))
                        return ((string?)null, (string?)null);
                    var rev = await _networkService.ResolveDnsReverseAsync(ip, ct);
                    return (ip, rev.Success ? rev.HostName : null);
                },
                // Installs are far heavier than a version query — give the push a 20-minute window.
                executeRemoteLong: (host, cmd, ct) =>
                    _networkService.ExecuteRemoteCommandAsync(host, cmd, _adminCredential, 1200000, ct),
                // The push installs software over WinRM — always confirm with the operator first.
                confirmRemediation: (title, message) =>
                    System.Windows.MessageBox.Show(message, title,
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes);
        }

        /// <summary>
        /// Runs GPUpdate without /force flag
        /// </summary>
        [RelayCommand]
        private async Task GpUpdateNormal()
        {
            IsGpUpdateForce = false;
            await GpUpdateAsync();
        }

        /// <summary>
        /// Runs GPUpdate with /force flag
        /// </summary>
        [RelayCommand]
        private async Task GpUpdateForce()
        {
            IsGpUpdateForce = true;
            await GpUpdateAsync();
        }

        /// <summary>
        /// Reboots selected computers remotely
        /// </summary>
        [RelayCommand]
        private async Task RebootComputers()
        {
            var targets = GetNetworkTargetsAsAdObjects().ToList();
            if (!targets.Any())
            {
                SetError("No target selected for reboot");
                return;
            }

            // Confirmation dialog
            var targetNames = string.Join(", ", targets.Take(5).Select(t => t.Name));
            if (targets.Count > 5) targetNames += $" and {targets.Count - 5} more";
            
            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to reboot the following computer(s)?\n\n{targetNames}\n\nThis will immediately restart the machines.",
                "Confirm Reboot",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            var entry = AddNetworkHistoryEntry("Reboot", targets.Select(t => t.Name).ToList());

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                IsNetworkOperationRunning = true;
                NetworkOperationName = "Reboot";

                var total = targets.Count;

                var resultDict = new System.Collections.Concurrent.ConcurrentDictionary<string, Models.RemoteCommandResult>();
                foreach (var target in targets)
                {
                    resultDict[target.Name] = new Models.RemoteCommandResult
                    {
                        TargetName = target.Name,
                        Command = "shutdown /r /t 0 /f",
                        Status = "Pending",
                        StartTime = DateTime.Now
                    };
                }

                int current = 0;
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 5,
                    CancellationToken = _currentCts.Token
                };

                await Parallel.ForEachAsync(targets, parallelOptions, async (target, ct) =>
                {
                    var cmdResult = resultDict[target.Name];
                    cmdResult.Status = "Running";
                    cmdResult.StartTime = DateTime.Now;

                    var currentCount = Interlocked.Increment(ref current);
                    NetworkOperationProgress = $"{currentCount}/{total}";

                    try
                    {
                        var pingResult = await _networkService.PingAsync(target.TargetAddress, 5000, ct);
                        if (!pingResult.Success)
                        {
                            cmdResult.EndTime = DateTime.Now;
                            cmdResult.Success = false;
                            cmdResult.Status = "Offline";
                            cmdResult.ErrorMessage = "Ping failed - machine appears offline";
                            return;
                        }

                        var cmdResultTask = await _networkService.ExecuteRemoteCommandAsync(
                            target.TargetAddress,
                            "shutdown /r /t 0 /f",
                            _adminCredential,
                            30000,
                            ct);

                        cmdResult.EndTime = DateTime.Now;
                        cmdResult.Success = cmdResultTask.Success;
                        cmdResult.Status = cmdResultTask.Success ? "Rebooting" : "Failed";
                        cmdResult.Output = cmdResultTask.Output;
                        cmdResult.ErrorMessage = cmdResultTask.Error;
                    }
                    catch (OperationCanceledException)
                    {
                        cmdResult.EndTime = DateTime.Now;
                        cmdResult.Success = false;
                        cmdResult.Status = "Cancelled";
                    }
                });

                var resultList = resultDict.Values.ToList();
                entry.ResultText = Models.NetworkHistoryEntry.FormatRemoteCommandResults(resultList);
                entry.IsComplete = true;
                var successCount = resultList.Count(r => r.Success);
                entry.StatusColor = successCount == total ? "#4CAF50" : successCount == 0 ? "#E53935" : "#FF9800";

                StatusMessage = $"Reboot complete: {successCount}/{total} initiated";
            }
            catch (OperationCanceledException)
            {
                entry.ResultText = "  Cancelled";
                entry.IsComplete = true;
                entry.StatusColor = "#FF9800";
                StatusMessage = "Reboot cancelled";
            }
            catch (Exception ex)
            {
                entry.ResultText = $"  Error: {ex.Message}";
                entry.IsComplete = true;
                entry.StatusColor = "#E53935";
                SetError($"Reboot failed: {ex.Message}");
            }
            finally
            {
                IsNetworkOperationRunning = false;
                NetworkOperationProgress = string.Empty;
                NetworkOperationName = string.Empty;
                CurrentOperationTarget = string.Empty;
            }
        }

        /// <summary>
        /// Cancels the current activity: network operation takes priority, then DNS resolution
        /// </summary>
        [RelayCommand]
        private void CancelActivity()
        {
            if (IsNetworkOperationRunning)
            {
                _currentCts?.Cancel();
            }
            else if (IsDnsResolving)
            {
                _dnsCts?.Cancel();
            }
        }

        /// <summary>
        /// Quick ping from context menu - runs ping and logs to history panel
        /// </summary>
        public async Task QuickPingAsync(IEnumerable<AdObjectInfo> targets)
        {
            var targetList = targets.ToList();
            if (!targetList.Any()) return;

            var entry = AddNetworkHistoryEntry("Ping", targetList.Select(t => t.Name).ToList());
            IsNetworkOperationRunning = true;
            NetworkOperationName = "Ping";

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                var results = new System.Collections.Concurrent.ConcurrentBag<PingResult>();
                int current = 0;
                int total = targetList.Count;

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 10,
                    CancellationToken = _currentCts.Token
                };

                await Parallel.ForEachAsync(targetList, parallelOptions, async (target, ct) =>
                {
                    var currentCount = Interlocked.Increment(ref current);
                    NetworkOperationProgress = $"{currentCount}/{total}";

                    try
                    {
                        var result = await _networkService.PingAsync(target.TargetAddress, TimeoutSeconds * 1000, ct);
                        result.TargetName = target.Name;
                        if (result.Success)
                            target.LastNetworkActivity = DateTime.UtcNow;
                        results.Add(result);
                    }
                    catch (OperationCanceledException)
                    {
                        results.Add(new PingResult
                        {
                            TargetName = target.Name,
                            TargetAddress = target.TargetAddress,
                            Status = "Cancelled",
                            Success = false
                        });
                    }
                });

                var resultList = results.ToList();
                entry.ResultText = Models.NetworkHistoryEntry.FormatPingResults(resultList);
                entry.IsComplete = true;
                var successCount = resultList.Count(r => r.Success);
                entry.StatusColor = successCount == resultList.Count ? "#4CAF50" : successCount == 0 ? "#E53935" : "#FF9800";

                StatusMessage = targetList.Count == 1
                    ? (resultList.First().Success 
                        ? $"Ping successful: {resultList.First().LatencyMs}ms"
                        : $"Ping failed: {resultList.First().Status}")
                    : $"Ping complete: {successCount}/{resultList.Count} responded";
            }
            catch (OperationCanceledException)
            {
                entry.ResultText = "  Cancelled";
                entry.IsComplete = true;
                entry.StatusColor = "#FF9800";
                StatusMessage = "Ping cancelled";
            }
            catch (Exception ex)
            {
                entry.ResultText = $"  Error: {ex.Message}";
                entry.IsComplete = true;
                entry.StatusColor = "#E53935";
                SetError($"Ping failed: {ex.Message}");
            }
            finally
            {
                IsNetworkOperationRunning = false;
                NetworkOperationProgress = string.Empty;
                NetworkOperationName = string.Empty;
                CurrentOperationTarget = string.Empty;
            }
        }

        [RelayCommand]
        private async Task PingAsync()
        {
            var targets = GetNetworkTargetsAsAdObjects().ToList();
            if (!targets.Any())
            {
                SetError("No target selected for ping");
                return;
            }
            await QuickPingAsync(targets);
        }

        [RelayCommand]
        private async Task TracerouteAsync()
        {
            var targets = GetNetworkTargetsAsAdObjects().ToList();
            if (!targets.Any())
            {
                SetError("No target specified for traceroute");
                return;
            }

            var entry = AddNetworkHistoryEntry("Traceroute", targets.Select(t => t.Name).ToList());

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                IsNetworkOperationRunning = true;
                NetworkOperationName = "Traceroute";

                var results = new System.Collections.Concurrent.ConcurrentBag<TraceResult>();
                int current = 0;
                int total = targets.Count;

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 3,
                    CancellationToken = _currentCts.Token
                };

                await Parallel.ForEachAsync(targets, parallelOptions, async (target, ct) =>
                {
                    var currentCount = Interlocked.Increment(ref current);
                    NetworkOperationProgress = $"{currentCount}/{total}";

                    try
                    {
                        var result = await _networkService.TracerouteAsync(
                            target.TargetAddress, 
                            TimeoutSeconds * 1000,
                            20,
                            null,
                            ct);

                        result.Target = target.Name;
                        results.Add(result);
                    }
                    catch (OperationCanceledException)
                    {
                        results.Add(new TraceResult { Target = target.Name, Cancelled = true });
                    }
                });

                var resultList = results.OrderBy(r => targets.FindIndex(t => t.Name == r.Target)).ToList();
                entry.ResultText = Models.NetworkHistoryEntry.FormatTracerouteResults(resultList);
                entry.IsComplete = true;
                var completedCount = resultList.Count(r => r.Completed);
                entry.StatusColor = completedCount == resultList.Count ? "#4CAF50" : completedCount == 0 ? "#E53935" : "#FF9800";

                StatusMessage = targets.Count == 1
                    ? (resultList.FirstOrDefault()?.Completed == true 
                        ? $"Traceroute complete: {resultList.First().Hops.Count} hops"
                        : resultList.FirstOrDefault()?.Cancelled == true ? "Traceroute cancelled/timed out" : "Traceroute incomplete")
                    : $"Traceroute complete: {completedCount}/{resultList.Count} succeeded";
            }
            catch (OperationCanceledException)
            {
                entry.ResultText = "  Cancelled";
                entry.IsComplete = true;
                entry.StatusColor = "#FF9800";
                StatusMessage = "Traceroute cancelled";
            }
            catch (Exception ex)
            {
                entry.ResultText = $"  Error: {ex.Message}";
                entry.IsComplete = true;
                entry.StatusColor = "#E53935";
                SetError($"Traceroute failed: {ex.Message}");
            }
            finally
            {
                IsNetworkOperationRunning = false;
                NetworkOperationProgress = string.Empty;
                NetworkOperationName = string.Empty;
                CurrentOperationTarget = string.Empty;
            }
        }

        [RelayCommand]
        private async Task PathpingAsync()
        {
            var targets = GetNetworkTargetsAsAdObjects().ToList();
            if (!targets.Any())
            {
                SetError("No target specified for pathping");
                return;
            }

            var entry = AddNetworkHistoryEntry("Pathping", targets.Select(t => t.Name).ToList());

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                IsNetworkOperationRunning = true;
                NetworkOperationName = "Pathping";

                var results = new List<TraceResult>();
                int current = 0;
                int total = targets.Count;

                foreach (var target in targets)
                {
                    _currentCts.Token.ThrowIfCancellationRequested();
                    
                    current++;
                    NetworkOperationProgress = $"{current}/{total}";
                    CurrentOperationTarget = $"{current}/{total}: {target.Name}";

                    var result = await _networkService.PathpingAsync(
                        target.TargetAddress, 
                        TimeoutSeconds * 1000,
                        30,
                        null,
                        _currentCts.Token);

                    results.Add(result);

                    // Update entry text progressively
                    entry.ResultText = Models.NetworkHistoryEntry.FormatPathpingResults(results);
                }

                entry.IsComplete = true;
                var completedCount = results.Count(r => r.Completed);
                entry.StatusColor = completedCount == results.Count ? "#4CAF50" : completedCount == 0 ? "#E53935" : "#FF9800";

                StatusMessage = targets.Count == 1
                    ? (results.First().Completed 
                        ? $"Pathping complete ({results.First().Duration.TotalSeconds:F0}s)"
                        : results.First().Cancelled ? "Pathping cancelled/timed out" : "Pathping incomplete")
                    : $"Pathping complete: {completedCount}/{results.Count} succeeded";
            }
            catch (OperationCanceledException)
            {
                entry.ResultText += "\n  Cancelled";
                entry.IsComplete = true;
                entry.StatusColor = "#FF9800";
                StatusMessage = "Pathping cancelled";
            }
            catch (Exception ex)
            {
                entry.ResultText = $"  Error: {ex.Message}";
                entry.IsComplete = true;
                entry.StatusColor = "#E53935";
                SetError($"Pathping failed: {ex.Message}");
            }
            finally
            {
                IsNetworkOperationRunning = false;
                NetworkOperationProgress = string.Empty;
                NetworkOperationName = string.Empty;
                CurrentOperationTarget = string.Empty;
            }
        }

        [RelayCommand]
        private async Task GetLastUserAsync()
        {
            // Get selected AdObjectInfo items (computers only)
            var selectedAdObjects = SelectedAdObjects
                .Where(o => o.ObjectType == AdObjectType.Computer)
                .ToList();

            // Fall back to Computers collection if no AdObjects selected
            var computerTargets = GetNetworkTargets().ToList();
            
            if (!selectedAdObjects.Any() && !computerTargets.Any())
            {
                SetError("No target selected for last user query");
                return;
            }

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                // Prefer AdObjects, fall back to computer targets
                var targets = selectedAdObjects.Any() ? selectedAdObjects : computerTargets;
                var total = targets.Count;

                var entry = AddNetworkHistoryEntry("Last User", targets.Select(t => t.Name).ToList());

                IsNetworkOperationRunning = true;
                NetworkOperationName = "Last User";
                NetworkOperationProgress = $"0/{total}";

                var successCount = 0;
                var current = 0;
                var lastUserResults = new System.Collections.Concurrent.ConcurrentBag<LastUserResult>();

                // Always use parallel processing with cancel support
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 10,
                    CancellationToken = _currentCts.Token
                };

                // Parallel processing with cancel support
                await Parallel.ForEachAsync(targets, parallelOptions, async (adObj, ct) =>
                    {
                        var currentCount = Interlocked.Increment(ref current);
                        NetworkOperationProgress = $"{currentCount}/{total}";
                        adObj.LastUserQueriedAt = DateTime.UtcNow;

                        try
                        {
                            // Quick ping pre-check to skip obviously offline machines
                            var pingResult = await _networkService.PingAsync(adObj.TargetAddress, 5000, ct);
                            if (pingResult.Success)
                                adObj.LastNetworkActivity = DateTime.UtcNow;
                            if (!pingResult.Success)
                            {
                                // Preserve cached last user if available - don't overwrite
                                if (adObj.ObjectGuid.HasValue)
                                {
                                    var cached = _computerCacheService.GetEntry(adObj.ObjectGuid.Value);
                                    if (!string.IsNullOrEmpty(cached?.LastUser))
                                    {
                                        if (string.IsNullOrEmpty(adObj.LastUser) || adObj.LastUser.StartsWith("("))
                                        {
                                            adObj.LastUser = cached.LastUser;
                                            adObj.LastUserActiveAt = cached.LastUserActiveAt;
                                            adObj.LastUserProfiles = Services.ComputerCacheService.ConvertToUserProfileInfos(cached.KnownUsers);
                                        }
                                        lastUserResults.Add(new LastUserResult
                                        {
                                            TargetName = adObj.Name,
                                            TargetAddress = adObj.TargetAddress,
                                            Success = true,
                                            Status = "Cached",
                                            LastUser = adObj.LastUser,
                                            AllProfiles = adObj.LastUserProfiles?.ToList()
                                        });
                                        return;
                                    }
                                }
                                adObj.LastUser = "(Offline)";
                                adObj.LastUserProfiles = null;
                                lastUserResults.Add(new LastUserResult
                                {
                                    TargetName = adObj.Name,
                                    TargetAddress = adObj.TargetAddress,
                                    Success = false,
                                    Status = "Offline"
                                });
                                return;
                            }

                                var result = await _networkService.GetCurrentUserAsync(
                                    adObj.TargetAddress, ct, _adminCredential);

                                if (result.Success)
                                {
                                    if (!string.IsNullOrEmpty(result.Username))
                                    {
                                        // Someone is actively logged in
                                        adObj.LastUser = result.Username;
                                        adObj.LastUserActiveAt = DateTime.UtcNow;
                                        
                                        if (adObj.ObjectGuid.HasValue)
                                        {
                                            _computerCacheService.UpdateActiveUser(
                                                adObj.ObjectGuid.Value,
                                                adObj.Name,
                                                adObj.DistinguishedName,
                                                result.Username);
                                        }
                                        
                                        // Update user's computer history
                                        CacheActiveUserMachineHistory(adObj.Name, result.Username);
                                        
                                        Interlocked.Increment(ref successCount);
                                        lastUserResults.Add(new LastUserResult
                                        {
                                            TargetName = adObj.Name,
                                            TargetAddress = adObj.TargetAddress,
                                            Success = true,
                                            Status = "Success",
                                            LastUser = result.Username,
                                            IsLoggedIn = true
                                        });
                                    }
                                    else
                                    {
                                        // No one logged in - use cached data if available
                                        if (adObj.ObjectGuid.HasValue)
                                        {
                                            var cached = _computerCacheService.GetEntry(adObj.ObjectGuid.Value);
                                            if (!string.IsNullOrEmpty(cached?.LastUser) && cached.IsLoggedIn)
                                            {
                                                adObj.LastUser = cached.LastUser;
                                                adObj.LastUserActiveAt = cached.LastUserActiveAt;
                                                Interlocked.Increment(ref successCount);
                                                lastUserResults.Add(new LastUserResult
                                                {
                                                    TargetName = adObj.Name,
                                                    TargetAddress = adObj.TargetAddress,
                                                    Success = true,
                                                    Status = "Cached",
                                                    LastUser = cached.LastUser,
                                                    IsLoggedIn = false
                                                });
                                            }
                                            else
                                            {
                                                adObj.LastUser = "(No active session)";
                                                adObj.LastUserActiveAt = null;
                                                lastUserResults.Add(new LastUserResult
                                                {
                                                    TargetName = adObj.Name,
                                                    TargetAddress = adObj.TargetAddress,
                                                    Success = true,
                                                    Status = "No active session"
                                                });
                                            }
                                        }
                                        else
                                        {
                                            adObj.LastUser = "(No active session)";
                                            adObj.LastUserActiveAt = null;
                                            lastUserResults.Add(new LastUserResult
                                            {
                                                TargetName = adObj.Name,
                                                TargetAddress = adObj.TargetAddress,
                                                Success = true,
                                                Status = "No active session"
                                            });
                                        }
                                    }
                                }
                                else
                                {
                                    // For any failure, preserve cached last user if available - don't overwrite with error
                                    if (adObj.ObjectGuid.HasValue)
                                    {
                                        var cached = _computerCacheService.GetEntry(adObj.ObjectGuid.Value);
                                        if (!string.IsNullOrEmpty(cached?.LastUser))
                                        {
                                            // Keep existing cached data, don't overwrite
                                            if (string.IsNullOrEmpty(adObj.LastUser) || adObj.LastUser.StartsWith("("))
                                            {
                                                adObj.LastUser = cached.LastUser;
                                                adObj.LastUserActiveAt = cached.LastUserActiveAt;
                                                adObj.LastUserProfiles = Services.ComputerCacheService.ConvertToUserProfileInfos(cached.KnownUsers);
                                            }
                                            lastUserResults.Add(new LastUserResult
                                            {
                                                TargetName = adObj.Name,
                                                TargetAddress = adObj.TargetAddress,
                                                Success = true,
                                                Status = "Cached",
                                                LastUser = adObj.LastUser,
                                                AllProfiles = adObj.LastUserProfiles?.ToList()
                                            });
                                            return;
                                        }
                                    }
                                    // Only show error if we have no cached data
                                    adObj.LastUser = $"({result.Error ?? "Not found"})";
                                    adObj.LastUserProfiles = null;
                                    lastUserResults.Add(new LastUserResult
                                    {
                                        TargetName = adObj.Name,
                                        TargetAddress = adObj.TargetAddress,
                                        Success = false,
                                        Status = result.Error ?? "Not found"
                                    });
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // On cancel, preserve cached last user if available
                                if (adObj.ObjectGuid.HasValue)
                                {
                                    var cached = _computerCacheService.GetEntry(adObj.ObjectGuid.Value);
                                    if (!string.IsNullOrEmpty(cached?.LastUser))
                                    {
                                        if (string.IsNullOrEmpty(adObj.LastUser) || adObj.LastUser.StartsWith("("))
                                        {
                                            adObj.LastUser = cached.LastUser;
                                            adObj.LastUserActiveAt = cached.LastUserActiveAt;
                                            adObj.LastUserProfiles = Services.ComputerCacheService.ConvertToUserProfileInfos(cached.KnownUsers);
                                        }
                                        return;
                                    }
                                }
                                adObj.LastUser = "(Cancelled)";
                                adObj.LastUserProfiles = null;
                                lastUserResults.Add(new LastUserResult
                                {
                                    TargetName = adObj.Name,
                                    TargetAddress = adObj.TargetAddress,
                                    Success = false,
                                    Status = "Cancelled"
                                });
                            }
                        });

                // Refresh the AdObject grid to show updates
                SelectedOutputTab?.RefreshAdObjectResults();
                
                // Save cache (debounced)
                _ = _computerCacheService.SaveAsync();

                entry.ResultText = Models.NetworkHistoryEntry.FormatLastUserResults(lastUserResults.ToList());
                entry.IsComplete = true;
                entry.StatusColor = successCount == total ? "#4CAF50" : successCount == 0 ? "#E53935" : "#FF9800";

                StatusMessage = $"Last user query complete: {successCount}/{total} successful";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Last user query cancelled";
            }
            catch (Exception ex)
            {
                SetError($"Last user query failed: {ex.Message}");
            }
            finally
            {
                IsNetworkOperationRunning = false;
                NetworkOperationProgress = string.Empty;
                NetworkOperationName = string.Empty;
                CurrentOperationTarget = string.Empty;
            }
        }

        [RelayCommand]
        private async Task UserProfilesScanAsync()
        {
            // Get selected AdObjectInfo items (computers only)
            var selectedAdObjects = SelectedAdObjects
                .Where(o => o.ObjectType == AdObjectType.Computer)
                .ToList();

            // Fall back to Computers collection if no AdObjects selected
            var computerTargets = GetNetworkTargets().ToList();
            
            if (!selectedAdObjects.Any() && !computerTargets.Any())
            {
                SetError("No target selected for user profiles scan");
                return;
            }

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                // Prefer AdObjects, fall back to computer targets
                var targets = selectedAdObjects.Any() ? selectedAdObjects : computerTargets;
                var total = targets.Count;

                var entry = AddNetworkHistoryEntry("User Profiles Scan", targets.Select(t => t.Name).ToList());

                IsNetworkOperationRunning = true;
                NetworkOperationName = "User Profiles";
                NetworkOperationProgress = $"0/{total}";

                var successCount = 0;
                var current = 0;
                var lastUserResults = new System.Collections.Concurrent.ConcurrentBag<LastUserResult>();

                // Parallel processing
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 10,
                    CancellationToken = _currentCts.Token
                };

                await Parallel.ForEachAsync(targets, parallelOptions, async (adObj, ct) =>
                {
                    var currentCount = Interlocked.Increment(ref current);
                    NetworkOperationProgress = $"{currentCount}/{total}";
                    adObj.LastUserQueriedAt = DateTime.UtcNow;

                    try
                    {
                        // Quick ping pre-check to skip obviously offline machines
                        var pingResult = await _networkService.PingAsync(adObj.TargetAddress, 5000, ct);
                        if (pingResult.Success)
                            adObj.LastNetworkActivity = DateTime.UtcNow;
                        if (!pingResult.Success)
                        {
                            lastUserResults.Add(new LastUserResult
                            {
                                TargetName = adObj.Name,
                                TargetAddress = adObj.TargetAddress,
                                Success = false,
                                Status = "Offline"
                            });
                            return;
                        }

                        // Full profile scan - slower but gets all user profiles
                        var result = await _networkService.GetAllUserProfilesAsync(
                            adObj.TargetAddress, ct, _adminCredential);

                        if (result.Success && result.Users.Count > 0)
                        {
                            adObj.LastUserProfiles = result.Users;
                            
                            // Check if someone is currently logged in
                            var loggedInUser = result.Users.FirstOrDefault(u => u.IsCurrentlyLoggedIn);
                            
                            if (loggedInUser != null)
                            {
                                // Someone is actively logged in
                                adObj.LastUser = loggedInUser.Username;
                                adObj.LastUserActiveAt = DateTime.UtcNow;
                            }
                            
                            // Cache the profiles
                            if (adObj.ObjectGuid.HasValue)
                            {
                                _computerCacheService.UpdateUserProfiles(
                                    adObj.ObjectGuid.Value,
                                    adObj.Name,
                                    adObj.DistinguishedName,
                                    result.Users.ToList(),
                                    loggedInUser?.Username);
                            }
                            
                            Interlocked.Increment(ref successCount);
                            lastUserResults.Add(new LastUserResult
                            {
                                TargetName = adObj.Name,
                                TargetAddress = adObj.TargetAddress,
                                Success = true,
                                Status = "Success",
                                LastUser = loggedInUser?.Username ?? string.Empty,
                                IsLoggedIn = loggedInUser != null,
                                AllProfiles = result.Users.ToList()
                            });
                        }
                        else
                        {
                            lastUserResults.Add(new LastUserResult
                            {
                                TargetName = adObj.Name,
                                TargetAddress = adObj.TargetAddress,
                                Success = false,
                                Status = result.Error ?? "No profiles found"
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        lastUserResults.Add(new LastUserResult
                        {
                            TargetName = adObj.Name,
                            TargetAddress = adObj.TargetAddress,
                            Success = false,
                            Status = "Cancelled"
                        });
                    }
                });

                // Refresh the AdObject grid to show updates
                SelectedOutputTab?.RefreshAdObjectResults();
                
                // Save cache
                _ = _computerCacheService.SaveAsync();

                entry.ResultText = Models.NetworkHistoryEntry.FormatLastUserResults(lastUserResults.ToList());
                entry.IsComplete = true;
                entry.StatusColor = successCount == total ? "#4CAF50" : successCount == 0 ? "#E53935" : "#FF9800";

                StatusMessage = $"User profiles scan complete: {successCount}/{total} successful";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "User profiles scan cancelled";
            }
            catch (Exception ex)
            {
                SetError($"User profiles scan failed: {ex.Message}");
            }
            finally
            {
                IsNetworkOperationRunning = false;
                NetworkOperationProgress = string.Empty;
                NetworkOperationName = string.Empty;
                CurrentOperationTarget = string.Empty;
            }
        }

        /// <summary>
        /// Gets the last user for a single AdObjectInfo item (called from cell hover button).
        /// </summary>
        public async Task GetLastUserForItemAsync(Models.AdObjectInfo adObj)
        {
            if (adObj == null || adObj.ObjectType != AdObjectType.Computer)
                return;

            try
            {
                adObj.LastUser = "Querying...";
                adObj.LastUserQueriedAt = DateTime.UtcNow;

                // Quick ping pre-check to skip obviously offline machines
                var pingResult = await _networkService.PingAsync(adObj.TargetAddress, 5000);
                if (pingResult.Success)
                    adObj.LastNetworkActivity = DateTime.UtcNow;
                if (!pingResult.Success)
                {
                    // Preserve cached last user if available
                    if (adObj.ObjectGuid.HasValue)
                    {
                        var cached = _computerCacheService.GetEntry(adObj.ObjectGuid.Value);
                        if (!string.IsNullOrEmpty(cached?.LastUser))
                        {
                            adObj.LastUser = cached.LastUser;
                            adObj.LastUserActiveAt = cached.LastUserActiveAt;
                            return;
                        }
                    }
                    adObj.LastUser = "(Offline)";
                    return;
                }

                // Fast query - only get currently logged in user
                var result = await _networkService.GetCurrentUserAsync(
                    adObj.TargetAddress, CancellationToken.None, _adminCredential);

                if (result.Success)
                {
                    if (!string.IsNullOrEmpty(result.Username))
                    {
                        // Someone is actively logged in
                        adObj.LastUser = result.Username;
                        adObj.LastUserActiveAt = DateTime.UtcNow;
                        
                        if (adObj.ObjectGuid.HasValue)
                        {
                            _computerCacheService.UpdateActiveUser(
                                adObj.ObjectGuid.Value,
                                adObj.Name,
                                adObj.DistinguishedName,
                                result.Username);
                            _ = _computerCacheService.SaveAsync();
                        }
                        
                        // Update user's computer history
                        CacheActiveUserMachineHistory(adObj.Name, result.Username);
                    }
                    else
                    {
                        // No one logged in - use cached data if available
                        if (adObj.ObjectGuid.HasValue)
                        {
                            var cached = _computerCacheService.GetEntry(adObj.ObjectGuid.Value);
                            if (!string.IsNullOrEmpty(cached?.LastUser) && cached.IsLoggedIn)
                            {
                                adObj.LastUser = cached.LastUser;
                                adObj.LastUserActiveAt = cached.LastUserActiveAt;
                            }
                            else
                            {
                                adObj.LastUser = "(No active session)";
                                adObj.LastUserActiveAt = null;
                            }
                        }
                        else
                        {
                            adObj.LastUser = "(No active session)";
                            adObj.LastUserActiveAt = null;
                        }
                    }
                }
                else
                {
                    // Preserve cached last user if available
                    if (adObj.ObjectGuid.HasValue)
                    {
                        var cached = _computerCacheService.GetEntry(adObj.ObjectGuid.Value);
                        if (!string.IsNullOrEmpty(cached?.LastUser))
                        {
                            adObj.LastUser = cached.LastUser;
                            adObj.LastUserActiveAt = cached.LastUserActiveAt;
                            return;
                        }
                    }
                    adObj.LastUser = $"({result.Error ?? "Not found"})";
                }
            }
            catch (Exception ex)
            {
                // Preserve cached last user if available
                if (adObj.ObjectGuid.HasValue)
                {
                    var cached = _computerCacheService.GetEntry(adObj.ObjectGuid.Value);
                    if (!string.IsNullOrEmpty(cached?.LastUser))
                    {
                        adObj.LastUser = cached.LastUser;
                        adObj.LastUserActiveAt = cached.LastUserActiveAt;
                        return;
                    }
                }
                adObj.LastUser = $"(Error: {ex.Message})";
            }
        }

        /// <summary>
        /// Full user profiles scan for a single AdObjectInfo item (called from dialog refresh).
        /// Uses Win32_UserProfile to get all profiles on the machine.
        /// </summary>
        public async Task UserProfilesScanForItemAsync(Models.AdObjectInfo adObj)
        {
            if (adObj == null || adObj.ObjectType != AdObjectType.Computer)
                return;

            try
            {
                adObj.LastUser = "Scanning profiles...";
                adObj.LastUserQueriedAt = DateTime.UtcNow;

                // Quick ping pre-check to skip obviously offline machines
                var pingResult = await _networkService.PingAsync(adObj.TargetAddress, 5000);
                if (pingResult.Success)
                    adObj.LastNetworkActivity = DateTime.UtcNow;
                if (!pingResult.Success)
                {
                    // Preserve cached last user if available
                    if (adObj.ObjectGuid.HasValue)
                    {
                        var cached = _computerCacheService.GetEntry(adObj.ObjectGuid.Value);
                        if (!string.IsNullOrEmpty(cached?.LastUser))
                        {
                            adObj.LastUser = cached.LastUser;
                            adObj.LastUserActiveAt = cached.LastUserActiveAt;
                            return;
                        }
                    }
                    adObj.LastUser = "(Offline)";
                    return;
                }

                // Full profile scan - uses Win32_UserProfile
                var result = await _networkService.GetAllUserProfilesAsync(
                    adObj.TargetAddress, CancellationToken.None, _adminCredential);

                if (result.Success && result.Users.Count > 0)
                {
                    adObj.LastUserProfiles = result.Users;
                    
                    // Check if someone is currently logged in
                    var loggedInUser = result.Users.FirstOrDefault(u => u.IsCurrentlyLoggedIn);
                    
                    if (loggedInUser != null)
                    {
                        // Someone is actively logged in
                        adObj.LastUser = loggedInUser.Username;
                        adObj.LastUserActiveAt = DateTime.UtcNow;
                    }
                    
                    // Cache the profiles
                    if (adObj.ObjectGuid.HasValue)
                    {
                        _computerCacheService.UpdateUserProfiles(
                            adObj.ObjectGuid.Value,
                            adObj.Name,
                            adObj.DistinguishedName,
                            result.Users.ToList(),
                            loggedInUser?.Username);
                        _ = _computerCacheService.SaveAsync();
                    }
                }
                else
                {
                    // Preserve cached last user if available
                    if (adObj.ObjectGuid.HasValue)
                    {
                        var cached = _computerCacheService.GetEntry(adObj.ObjectGuid.Value);
                        if (!string.IsNullOrEmpty(cached?.LastUser))
                        {
                            adObj.LastUser = cached.LastUser;
                            adObj.LastUserActiveAt = cached.LastUserActiveAt;
                            return;
                        }
                    }
                    adObj.LastUser = $"({result.Error ?? "No profiles found"})";
                }
            }
            catch (Exception ex)
            {
                // Preserve cached last user if available
                if (adObj.ObjectGuid.HasValue)
                {
                    var cached = _computerCacheService.GetEntry(adObj.ObjectGuid.Value);
                    if (!string.IsNullOrEmpty(cached?.LastUser))
                    {
                        adObj.LastUser = cached.LastUser;
                        adObj.LastUserActiveAt = cached.LastUserActiveAt;
                        return;
                    }
                }
                adObj.LastUser = $"(Error: {ex.Message})";
            }
        }

        /// <summary>
        /// Synchronous cache-only batch lookup of emails by sAMAccountName.
        /// Returns a case-insensitive dictionary of username -> email (null when no email is on file).
        /// Strips any DOMAIN\ prefix from inputs. Used by clipboard helpers that need immediate values.
        /// </summary>
        public Dictionary<string, string?> GetCachedUserEmails(IEnumerable<string> usernames)
        {
            var normalized = usernames
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u =>
                {
                    var name = u;
                    var slash = name.IndexOf('\\');
                    return slash >= 0 ? name.Substring(slash + 1) : name;
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (normalized.Count == 0) return result;

            var cached = _userCacheService.GetUsers(normalized);
            foreach (var name in normalized)
            {
                result[name] = cached.TryGetValue(name, out var entry) ? entry.Email : null;
            }
            return result;
        }

        /// <summary>
        /// Session-level negative cache: SAMs we've already tried to resolve via AD fallback
        /// and that weren't returned. Prevents repeated LDAP traffic for genuinely missing users.
        /// </summary>
        private readonly HashSet<string> _emailLookupMisses = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Looks up user emails from cache, with an Active Directory batch fallback for cache
        /// misses (users that have only been seen via Last User scans and were never enriched
        /// from a user-tab scan). AD results are written back to the cache so subsequent lookups
        /// stay fast. SAMs not found in AD are remembered for the session to avoid re-querying.
        /// All blocking work (LiteDB reads/writes, LDAP) runs on the thread pool so the UI thread
        /// is never held while this method runs.
        /// </summary>
        public async Task<Dictionary<string, (string? Email, string? DisplayName)>> LookupUserEmailsAsync(
            IEnumerable<string> usernames)
        {
            var result = new Dictionary<string, (string? Email, string? DisplayName)>(StringComparer.OrdinalIgnoreCase);
            var usernameList = usernames
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u =>
                {
                    var slash = u.IndexOf('\\');
                    return slash >= 0 ? u.Substring(slash + 1) : u;
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (usernameList.Count == 0)
                return result;

            // Push all blocking work off the UI thread: cache reads, the LDAP fallback,
            // and the cache writeback. Snapshot _emailLookupMisses up front so the
            // background thread doesn't touch it concurrently.
            var sessionMisses = new HashSet<string>(_emailLookupMisses, StringComparer.OrdinalIgnoreCase);

            var (resolved, newMisses) = await Task.Run(async () =>
            {
                var resolvedLocal = new Dictionary<string, (string? Email, string? DisplayName)>(StringComparer.OrdinalIgnoreCase);
                var newMissesLocal = new List<string>();

                // 1. Cache pass.
                var cachedUsers = _userCacheService.GetUsers(usernameList);
                foreach (var cached in cachedUsers)
                {
                    resolvedLocal[cached.Key] = (cached.Value.Email, cached.Value.DisplayName);
                }

                // 2. Identify SAMs that need an AD fallback.
                var needsLookup = new List<string>();
                foreach (var sam in usernameList)
                {
                    if (sessionMisses.Contains(sam))
                        continue;

                    if (!cachedUsers.TryGetValue(sam, out var entry))
                    {
                        needsLookup.Add(sam);
                        continue;
                    }

                    if (!entry.FoundInAd && string.IsNullOrEmpty(entry.Email))
                    {
                        needsLookup.Add(sam);
                    }
                }

                if (needsLookup.Count == 0)
                    return (resolvedLocal, newMissesLocal);

                // 3. Single batched LDAP query against the forest Global Catalog. The GC
                //    partial attribute set includes sAMAccountName, mail, and displayName,
                //    so we don't need to know which domain each user lives in. This
                //    matters for multi-domain target groups where SelectedDomain may be
                //    null or wrong for a given LastUser.
                try
                {
                    var adResults = await _ldapService.GetUsersBySamBatchViaGlobalCatalogAsync(needsLookup);

                    foreach (var sam in needsLookup)
                    {
                        if (adResults.TryGetValue(sam, out var info))
                        {
                            _userCacheService.UpdateUser(sam, info.Mail, info.DisplayName, sid: null, foundInAd: true);
                            resolvedLocal[sam] = (info.Mail, info.DisplayName);
                        }
                        else
                        {
                            newMissesLocal.Add(sam);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AD email fallback failed: {ex.Message}");
                }

                return (resolvedLocal, newMissesLocal);
            }).ConfigureAwait(true);

            // Merge results back on the calling context. Updating the session miss set
            // here (rather than inside the Task.Run) keeps that field touched only from
            // the UI thread.
            foreach (var kvp in resolved)
                result[kvp.Key] = kvp.Value;
            foreach (var miss in newMisses)
                _emailLookupMisses.Add(miss);

            return result;
        }

        /// <summary>
        /// Caches user emails from AD query results. Call this after querying users.
        /// </summary>
        private void CacheUserEmails(IEnumerable<AdObjectInfo> users)
        {
            foreach (var user in users)
            {
                if (!string.IsNullOrEmpty(user.SamAccountName))
                {
                    _userCacheService.UpdateUser(
                        user.SamAccountName,
                        user.Mail,
                        user.DisplayName,
                        sid: null,
                        foundInAd: true);
                }
            }
        }

        /// <summary>
        /// Caches user machine history from Get Last User results.
        /// Updates user cache with which computers they've logged into.
        /// </summary>
        private void CacheUserMachineHistory(string computerName, List<UserProfileInfo> profiles)
        {
            if (string.IsNullOrEmpty(computerName) || profiles == null || profiles.Count == 0)
                return;

            var userEntries = profiles
                .Where(p => !string.IsNullOrWhiteSpace(p.Username))
                .Select(p => (
                    Username: p.Username,
                    LastSeen: p.LastUseTime ?? DateTime.UtcNow,
                    WasLoggedIn: p.IsCurrentlyLoggedIn
                ));

            _userCacheService.UpdateUserMachinesBatch(computerName, userEntries);
        }

        /// <summary>
        /// Caches a single active user's machine history from the fast scan.
        /// Called when we detect someone is currently logged in.
        /// </summary>
        private void CacheActiveUserMachineHistory(string computerName, string username)
        {
            if (string.IsNullOrEmpty(computerName) || string.IsNullOrEmpty(username))
                return;

            var userEntry = new[] { (Username: username, LastSeen: DateTime.UtcNow, WasLoggedIn: true) };
            _userCacheService.UpdateUserMachinesBatch(computerName, userEntry);
        }

        /// <summary>
        /// Populates ComputerHistory and SnowId from cache for users being displayed.
        /// Call this after users are loaded to show which computers they've logged into.
        /// </summary>
        private void PopulateUserComputerHistory(IEnumerable<AdObjectInfo> users)
        {
            foreach (var user in users)
            {
                if (!string.IsNullOrEmpty(user.SamAccountName))
                {
                    var machines = _userCacheService.GetUserMachines(user.SamAccountName);
                    if (machines.Count > 0)
                    {
                        user.ComputerHistory = machines;
                    }

                    // Also populate SNOW ID from cache
                    var snowId = _userCacheService.GetSnowId(user.SamAccountName);
                    if (!string.IsNullOrEmpty(snowId))
                    {
                        user.SnowId = snowId;
                    }
                }
            }
        }

        /// <summary>
        /// Resolves SNOW IDs (sys_ids) for users that don't have one cached.
        /// Runs asynchronously in background - SNOW IDs populate as they resolve.
        /// </summary>
        private async Task ResolveSnowIdsAsync(IEnumerable<AdObjectInfo> users, CancellationToken cancellationToken = default)
        {
            var userList = users
                .Where(u => u.ObjectType == AdObjectType.User && 
                           !string.IsNullOrEmpty(u.SamAccountName) && 
                           string.IsNullOrEmpty(u.SnowId))
                .ToList();

            if (userList.Count == 0) return;

            try
            {
                // Skip if not authenticated - user can use "Connect to SNOW" button
                if (!_snowUserService.IsAuthenticated)
                {
                    System.Diagnostics.Debug.WriteLine($"SNOW: Skipping {userList.Count} users - not authenticated");
                    return;
                }

                // Batch lookup for efficiency (up to 50 at a time to stay under URL length limits)
                const int batchSize = 50;
                
                for (int i = 0; i < userList.Count; i += batchSize)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var batch = userList.Skip(i).Take(batchSize).ToList();
                    var samNames = batch.Select(u => u.SamAccountName!).ToList();

                    var snowIds = await _snowUserService.GetSysIdsBatchAsync(samNames, cancellationToken);

                    // Update users and cache with results
                    foreach (var user in batch)
                    {
                        if (snowIds.TryGetValue(user.SamAccountName!, out var snowId))
                        {
                            user.SnowId = snowId;
                            _userCacheService.UpdateSnowId(user.SamAccountName!, snowId);
                        }
                    }
                }

                var resolved = userList.Count(u => !string.IsNullOrEmpty(u.SnowId));
                if (resolved > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Resolved {resolved} SNOW IDs from ServiceNow API");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SNOW ID resolution failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves IP addresses for computers using DNS lookup.
        /// Runs asynchronously in background - IPs populate as they resolve.
        /// </summary>
        private async Task ResolveComputerIpAddressesAsync(IEnumerable<AdObjectInfo> computers, CancellationToken cancellationToken = default)
        {
            var computerList = computers.Where(c => c.ObjectType == AdObjectType.Computer).ToList();
            if (computerList.Count == 0) return;

            // Cancel any previous DNS resolution and create a new linked CTS
            _dnsCts?.Cancel();
            _dnsCts?.Dispose();
            var dnsCts = cancellationToken == default
                ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _dnsCts = dnsCts;
            var dnsToken = dnsCts.Token;

            IsDnsResolving = true;
            try
            {
            // Clear IP initially (blank while resolving)
            foreach (var computer in computerList)
            {
                computer.IpAddress = null;
                computer.IsIpStale = false;
            }

            int dnsTotal = computerList.Count;
            int dnsCurrent = 0;
            NetworkOperationProgress = $"0/{dnsTotal}";

            // Resolve in parallel with throttling
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 50, // Limit concurrent DNS lookups
                CancellationToken = dnsToken
            };

            await Parallel.ForEachAsync(computerList, parallelOptions, async (computer, ct) =>
            {
                var currentCount = Interlocked.Increment(ref dnsCurrent);
                NetworkOperationProgress = $"{currentCount}/{dnsTotal}";
                try
                {
                    // Use DnsHostName if available, otherwise Name
                    var hostName = !string.IsNullOrEmpty(computer.DnsHostName) 
                        ? computer.DnsHostName 
                        : computer.Name;

                    // Add timeout to DNS lookup (5 seconds) - DNS can hang for 30+ seconds on non-existent hosts
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                    
                    DnsResult result;
                    try
                    {
                        result = await _networkService.ResolveDnsForwardAsync(hostName, timeoutCts.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // Timeout occurred (not user cancellation)
                        computer.IpAddress = "(timeout)";
                        return;
                    }
                    
                    if (result.Success && result.IpAddresses?.Count > 0)
                    {
                        // Take first IPv4 address, or first address if no IPv4
                        var ipv4 = result.IpAddresses.FirstOrDefault(ip => !ip.Contains(':'));
                        var resolvedIp = ipv4 ?? result.IpAddresses.First();
                        computer.IpAddress = resolvedIp;
                        
                        // Save to cache if we have a valid objectGuid
                        if (computer.ObjectGuid.HasValue)
                        {
                            _computerCacheService.UpdateIpAddress(
                                computer.ObjectGuid.Value,
                                computer.Name,
                                computer.DistinguishedName,
                                resolvedIp);
                            
                            // Update local KnownIpAddresses for immediate UI access
                            computer.KnownIpAddresses ??= new Dictionary<string, DateTime>();
                            computer.KnownIpAddresses[resolvedIp] = DateTime.UtcNow;
                        }
                    }
                    else
                    {
                        // DNS failed - show last known IP from history if available
                        if (computer.KnownIpAddresses?.Count > 0)
                        {
                            var mostRecent = computer.KnownIpAddresses.OrderByDescending(kv => kv.Value).First();
                            computer.IpAddress = mostRecent.Key;
                            computer.IsIpStale = true;
                        }
                        else
                        {
                            computer.IpAddress = "(not found)";
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    computer.IpAddress = null;
                }
                catch
                {
                    computer.IpAddress = "(error)";
                }
            });
            }
            catch (OperationCanceledException)
            {
                // Parallel.ForEachAsync throws when token is cancelled - apply stale fallback to unresolved
                foreach (var computer in computerList.Where(c => c.IpAddress == null))
                {
                    if (computer.KnownIpAddresses?.Count > 0)
                    {
                        var mostRecent = computer.KnownIpAddresses.OrderByDescending(kv => kv.Value).First();
                        computer.IpAddress = mostRecent.Key;
                        computer.IsIpStale = true;
                    }
                }
            }
            finally
            {
                IsDnsResolving = false;
                NetworkOperationProgress = string.Empty;
                if (_dnsCts == dnsCts)
                {
                    _dnsCts = null;
                }
                dnsCts.Dispose();
            }
        }

        /// <summary>
        /// Resolves IP addresses for printers using DNS lookup on their ServerName.
        /// Runs asynchronously in background - IPs populate as they resolve.
        /// </summary>
        private async Task ResolvePrinterIpAddressesAsync(IEnumerable<AdObjectInfo> printers, CancellationToken cancellationToken = default)
        {
            var printerList = printers.Where(p => p.ObjectType == AdObjectType.Printer).ToList();
            if (printerList.Count == 0) return;

            // Clear IP initially (blank while resolving)
            foreach (var printer in printerList)
            {
                printer.IpAddress = null;
            }

            // Resolve in parallel with throttling
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 50, // Limit concurrent DNS lookups
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(printerList, parallelOptions, async (printer, ct) =>
            {
                try
                {
                    // Try to extract printer IP/hostname from PortName first
                    // PortName often contains IP like "IP_192.168.1.100", "TCPIP_printer1", or direct IP/hostname
                    var hostName = ExtractPrinterHostFromPortName(printer.PortName);
                    
                    // Fall back to ServerName (print server) if no host found in PortName
                    if (string.IsNullOrEmpty(hostName))
                    {
                        hostName = printer.ServerName;
                    }
                    
                    if (string.IsNullOrEmpty(hostName))
                    {
                        printer.IpAddress = "(no server)";
                        return;
                    }

                    // Check if hostName is already an IP address
                    if (System.Net.IPAddress.TryParse(hostName, out _))
                    {
                        printer.IpAddress = hostName;
                        
                        // Save to cache if we have a valid objectGuid
                        if (printer.ObjectGuid.HasValue)
                        {
                            _computerCacheService.UpdateIpAddress(
                                printer.ObjectGuid.Value,
                                printer.Name,
                                printer.DistinguishedName,
                                hostName);
                            
                            // Update local KnownIpAddresses for immediate UI access
                            printer.KnownIpAddresses ??= new Dictionary<string, DateTime>();
                            printer.KnownIpAddresses[hostName] = DateTime.UtcNow;
                        }
                        return;
                    }

                    // Add timeout to DNS lookup (5 seconds) - DNS can hang for 30+ seconds on non-existent hosts
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                    
                    DnsResult result;
                    try
                    {
                        result = await _networkService.ResolveDnsForwardAsync(hostName, timeoutCts.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // Timeout occurred (not user cancellation)
                        printer.IpAddress = "(timeout)";
                        return;
                    }
                    
                    if (result.Success && result.IpAddresses?.Count > 0)
                    {
                        // Take first IPv4 address, or first address if no IPv4
                        var ipv4 = result.IpAddresses.FirstOrDefault(ip => !ip.Contains(':'));
                        var resolvedIp = ipv4 ?? result.IpAddresses.First();
                        printer.IpAddress = resolvedIp;
                        
                        // Save to cache if we have a valid objectGuid
                        if (printer.ObjectGuid.HasValue)
                        {
                            _computerCacheService.UpdateIpAddress(
                                printer.ObjectGuid.Value,
                                printer.Name,
                                printer.DistinguishedName,
                                resolvedIp);
                            
                            // Update local KnownIpAddresses for immediate UI access
                            printer.KnownIpAddresses ??= new Dictionary<string, DateTime>();
                            printer.KnownIpAddresses[resolvedIp] = DateTime.UtcNow;
                        }
                    }
                    else
                    {
                        printer.IpAddress = "(not found)";
                    }
                }
                catch (OperationCanceledException)
                {
                    printer.IpAddress = null;
                }
                catch
                {
                    printer.IpAddress = "(error)";
                }
            });
        }

        /// <summary>
        /// Extracts printer IP address or hostname from the portName attribute.
        /// Common formats: "IP_192.168.1.100", "TCPIP_hostname", "WSD-uuid", direct IP, or hostname.
        /// </summary>
        private static string? ExtractPrinterHostFromPortName(string? portName)
        {
            if (string.IsNullOrWhiteSpace(portName))
                return null;

            // Common prefixes to strip: IP_, TCPIP_, TCP/IP:
            var prefixes = new[] { "IP_", "TCPIP_", "TCP/IP:", "TCP/IP_" };
            foreach (var prefix in prefixes)
            {
                if (portName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return portName.Substring(prefix.Length);
                }
            }

            // Skip WSD (Web Services for Devices) and other non-resolvable port types
            if (portName.StartsWith("WSD-", StringComparison.OrdinalIgnoreCase) ||
                portName.StartsWith("USB", StringComparison.OrdinalIgnoreCase) ||
                portName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase) ||
                portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                portName.StartsWith("NUL", StringComparison.OrdinalIgnoreCase) ||
                portName.StartsWith("FILE:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // If it looks like an IP address or hostname, use it directly
            if (System.Net.IPAddress.TryParse(portName, out _))
            {
                return portName;
            }

            // If it contains dots and no spaces, treat as hostname
            if (portName.Contains('.') && !portName.Contains(' '))
            {
                return portName;
            }

            // Otherwise, don't use it (could be an arbitrary port name)
            return null;
        }

        [RelayCommand]
        private async Task GpUpdateAsync()
        {
            var targets = GetNetworkTargetsAsAdObjects().ToList();
            if (!targets.Any())
            {
                SetError("No target selected for GPUpdate");
                return;
            }

            var gpCommand = IsGpUpdateForce ? "gpupdate /force" : "gpupdate";
            var entry = AddNetworkHistoryEntry(IsGpUpdateForce ? "GPUpdate /force" : "GPUpdate", targets.Select(t => t.Name).ToList());

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                IsNetworkOperationRunning = true;

                var total = targets.Count;

                var resultDict = new System.Collections.Concurrent.ConcurrentDictionary<string, Models.RemoteCommandResult>();
                foreach (var target in targets)
                {
                    resultDict[target.Name] = new Models.RemoteCommandResult
                    {
                        TargetName = target.Name,
                        Command = gpCommand,
                        Status = "Pending",
                        StartTime = DateTime.Now
                    };
                }

                int current = 0;
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 5,
                    CancellationToken = _currentCts.Token
                };

                await Parallel.ForEachAsync(targets, parallelOptions, async (target, ct) =>
                {
                    var cmdResult = resultDict[target.Name];
                    cmdResult.Status = "Running";
                    cmdResult.StartTime = DateTime.Now;

                    var currentCount = Interlocked.Increment(ref current);
                    NetworkOperationProgress = $"{currentCount}/{total}";

                    try
                    {
                        var pingResult = await _networkService.PingAsync(target.TargetAddress, 5000, ct);
                        if (!pingResult.Success)
                        {
                            cmdResult.EndTime = DateTime.Now;
                            cmdResult.Success = false;
                            cmdResult.Status = "Offline";
                            cmdResult.ErrorMessage = "Ping failed - machine appears offline";
                            return;
                        }

                        var result = await _networkService.ExecuteRemoteCommandAsync(
                            target.TargetAddress,
                            gpCommand,
                            _adminCredential,
                            300000,
                            ct);

                        cmdResult.EndTime = DateTime.Now;
                        cmdResult.Success = result.Success;
                        cmdResult.Status = result.Success ? "Success" : "Failed";
                        cmdResult.Output = result.Output;
                        cmdResult.ErrorMessage = result.Error;
                    }
                    catch (OperationCanceledException)
                    {
                        cmdResult.EndTime = DateTime.Now;
                        cmdResult.Success = false;
                        cmdResult.Status = "Cancelled";
                    }
                });

                var resultList = resultDict.Values.ToList();
                entry.ResultText = Models.NetworkHistoryEntry.FormatRemoteCommandResults(resultList);
                entry.IsComplete = true;
                var successCount = resultList.Count(r => r.Success);
                entry.StatusColor = successCount == total ? "#4CAF50" : successCount == 0 ? "#E53935" : "#FF9800";

                StatusMessage = $"GPUpdate complete: {successCount}/{total} successful";
            }
            catch (OperationCanceledException)
            {
                entry.ResultText = "  Cancelled";
                entry.IsComplete = true;
                entry.StatusColor = "#FF9800";
                StatusMessage = "GPUpdate cancelled";
            }
            catch (Exception ex)
            {
                entry.ResultText = $"  Error: {ex.Message}";
                entry.IsComplete = true;
                entry.StatusColor = "#E53935";
                SetError($"GPUpdate failed: {ex.Message}");
            }
            finally
            {
                IsNetworkOperationRunning = false;
                NetworkOperationProgress = string.Empty;
                NetworkOperationName = string.Empty;
                CurrentOperationTarget = string.Empty;
                ClearBusy();
            }
        }

        [RelayCommand]
        private async Task DnsResolveForwardAsync()
        {
            var targets = GetNetworkTargetsAsAdObjects().ToList();
            if (!targets.Any())
            {
                SetError("No target specified for DNS resolution");
                return;
            }

            var entry = AddNetworkHistoryEntry("DNS Forward", targets.Select(t => t.Name).ToList());

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                IsNetworkOperationRunning = true;
                NetworkOperationName = "DNS";

                var results = new System.Collections.Concurrent.ConcurrentBag<DnsResult>();
                int current = 0;
                int total = targets.Count;

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 10,
                    CancellationToken = _currentCts.Token
                };

                await Parallel.ForEachAsync(targets, parallelOptions, async (target, ct) =>
                {
                    var currentCount = Interlocked.Increment(ref current);
                    NetworkOperationProgress = $"{currentCount}/{total}";

                    try
                    {
                        var result = await _networkService.ResolveDnsForwardAsync(target.TargetAddress);
                        result.TargetName = target.Name;
                        results.Add(result);
                    }
                    catch (OperationCanceledException)
                    {
                        results.Add(new DnsResult
                        {
                            TargetName = target.Name,
                            Query = target.TargetAddress,
                            Success = false,
                            ErrorMessage = "Cancelled"
                        });
                    }
                });

                var resultList = results.ToList();
                entry.ResultText = Models.NetworkHistoryEntry.FormatDnsResults(resultList);
                entry.IsComplete = true;
                var successCount = resultList.Count(r => r.Success);
                entry.StatusColor = successCount == resultList.Count ? "#4CAF50" : successCount == 0 ? "#E53935" : "#FF9800";

                StatusMessage = targets.Count == 1
                    ? (resultList.First().Success
                        ? $"Resolved: {resultList.First().HostName} ({resultList.First().IpAddressesDisplay})"
                        : $"DNS resolution failed: {resultList.First().ErrorMessage}")
                    : $"DNS resolved: {successCount}/{resultList.Count} successful";
            }
            catch (OperationCanceledException)
            {
                entry.ResultText = "  Cancelled";
                entry.IsComplete = true;
                entry.StatusColor = "#FF9800";
                StatusMessage = "DNS resolution cancelled";
            }
            catch (Exception ex)
            {
                entry.ResultText = $"  Error: {ex.Message}";
                entry.IsComplete = true;
                entry.StatusColor = "#E53935";
                SetError($"DNS resolution failed: {ex.Message}");
            }
            finally
            {
                IsNetworkOperationRunning = false;
                NetworkOperationProgress = string.Empty;
                NetworkOperationName = string.Empty;
            }
        }

        [RelayCommand]
        private async Task DnsResolveReverseAsync()
        {
            var targets = GetNetworkTargetsAsAdObjects().ToList();
            if (!targets.Any())
            {
                SetError("No target specified for reverse DNS");
                return;
            }

            var entry = AddNetworkHistoryEntry("DNS Reverse", targets.Select(t => t.Name).ToList());

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                IsNetworkOperationRunning = true;
                NetworkOperationName = "rDNS";

                var results = new System.Collections.Concurrent.ConcurrentBag<DnsResult>();
                int current = 0;
                int total = targets.Count;

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 10,
                    CancellationToken = _currentCts.Token
                };

                await Parallel.ForEachAsync(targets, parallelOptions, async (target, ct) =>
                {
                    var currentCount = Interlocked.Increment(ref current);
                    NetworkOperationProgress = $"{currentCount}/{total}";

                    try
                    {
                        var ipToResolve = GetValidIpAddress(target);
                        
                        if (string.IsNullOrEmpty(ipToResolve))
                        {
                            results.Add(new DnsResult
                            {
                                TargetName = target.Name,
                                Query = target.IpAddress ?? "(no IP)",
                                QueryType = "Reverse",
                                Success = false,
                                Status = "No IP",
                                ErrorMessage = "No valid IP address available.",
                                Timestamp = DateTime.Now
                            });
                            return;
                        }

                        var result = await _networkService.ResolveDnsReverseAsync(ipToResolve, ct);
                        result.TargetName = target.Name;
                        results.Add(result);
                    }
                    catch (OperationCanceledException)
                    {
                        results.Add(new DnsResult
                        {
                            TargetName = target.Name,
                            Query = target.IpAddress ?? target.TargetAddress,
                            Success = false,
                            ErrorMessage = "Cancelled"
                        });
                    }
                });

                var resultList = results.ToList();
                entry.ResultText = Models.NetworkHistoryEntry.FormatDnsResults(resultList);
                entry.IsComplete = true;
                var successCount = resultList.Count(r => r.Success);
                entry.StatusColor = successCount == resultList.Count ? "#4CAF50" : successCount == 0 ? "#E53935" : "#FF9800";

                StatusMessage = targets.Count == 1
                    ? (resultList.First().Success
                        ? $"Resolved: {resultList.First().HostName}"
                        : $"Reverse DNS failed: {resultList.First().ErrorMessage}")
                    : $"Reverse DNS resolved: {successCount}/{resultList.Count} successful";
            }
            catch (OperationCanceledException)
            {
                entry.ResultText = "  Cancelled";
                entry.IsComplete = true;
                entry.StatusColor = "#FF9800";
                StatusMessage = "Reverse DNS cancelled";
            }
            catch (Exception ex)
            {
                entry.ResultText = $"  Error: {ex.Message}";
                entry.IsComplete = true;
                entry.StatusColor = "#E53935";
                SetError($"Reverse DNS failed: {ex.Message}");
            }
            finally
            {
                IsNetworkOperationRunning = false;
                NetworkOperationProgress = string.Empty;
                NetworkOperationName = string.Empty;
            }
        }

        [RelayCommand]
        private async Task TestPortAsync()
        {
            var targets = GetNetworkTargetsAsAdObjects().ToList();
            if (!targets.Any())
            {
                SetError("No target specified for port test");
                return;
            }

            var portToTest = SelectedPort;
            var entry = AddNetworkHistoryEntry($"Port {portToTest}", targets.Select(t => t.Name).ToList());

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                IsNetworkOperationRunning = true;
                NetworkOperationName = $"Port {portToTest}";

                var results = new System.Collections.Concurrent.ConcurrentBag<PortTestResult>();
                int current = 0;
                int total = targets.Count;

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 10,
                    CancellationToken = _currentCts.Token
                };

                await Parallel.ForEachAsync(targets, parallelOptions, async (target, ct) =>
                {
                    var currentCount = Interlocked.Increment(ref current);
                    NetworkOperationProgress = $"{currentCount}/{total}";

                    try
                    {
                        var result = await _networkService.TestPortAsync(target.TargetAddress, portToTest, TimeoutSeconds * 1000, ct);
                        result.TargetName = target.Name;
                        results.Add(result);
                    }
                    catch (OperationCanceledException)
                    {
                        results.Add(new PortTestResult
                        {
                            TargetName = target.Name,
                            TargetAddress = target.TargetAddress,
                            Port = portToTest,
                            Status = "Cancelled",
                            Success = false
                        });
                    }
                });

                var resultList = results.ToList();
                entry.ResultText = Models.NetworkHistoryEntry.FormatPortTestResults(resultList);
                entry.IsComplete = true;
                var successCount = resultList.Count(r => r.Success);
                entry.StatusColor = successCount == resultList.Count ? "#4CAF50" : successCount == 0 ? "#E53935" : "#FF9800";

                StatusMessage = targets.Count == 1
                    ? (resultList.First().Success
                        ? $"Port {portToTest} is open ({resultList.First().LatencyMs}ms)"
                        : $"Port {portToTest} test failed: {resultList.First().Status}")
                    : $"Port {portToTest} test: {successCount}/{resultList.Count} open";
            }
            catch (OperationCanceledException)
            {
                entry.ResultText = "  Cancelled";
                entry.IsComplete = true;
                entry.StatusColor = "#FF9800";
                StatusMessage = "Port test cancelled";
            }
            catch (Exception ex)
            {
                entry.ResultText = $"  Error: {ex.Message}";
                entry.IsComplete = true;
                entry.StatusColor = "#E53935";
                SetError($"Port test failed: {ex.Message}");
            }
            finally
            {
                IsNetworkOperationRunning = false;
                NetworkOperationProgress = string.Empty;
                NetworkOperationName = string.Empty;
            }
        }

        [RelayCommand]
        private void LaunchRdp()
        {
            var targets = GetNetworkTargets().ToList();
            if (!targets.Any())
            {
                SetError("No target specified for RDP");
                return;
            }

            // RDP should only launch for single target - don't loop through multi-select
            var target = targets.First();
            _networkService.LaunchRdp(target.TargetAddress);
            StatusMessage = $"Launching RDP to {target.Name}...";
        }

        [RelayCommand]
        private void OpenHttp()
        {
            var targets = GetNetworkTargets().ToList();
            if (!targets.Any())
            {
                SetError("No target specified for HTTP");
                return;
            }

            foreach (var target in targets)
            {
                _networkService.OpenInBrowser($"http://{target.TargetAddress}");
            }
            StatusMessage = targets.Count == 1
                ? $"Opening HTTP for {targets.First().Name}..."
                : $"Opening HTTP for {targets.Count} targets...";
        }

        [RelayCommand]
        private void OpenHttps()
        {
            var targets = GetNetworkTargets().ToList();
            if (!targets.Any())
            {
                SetError("No target specified for HTTPS");
                return;
            }

            foreach (var target in targets)
            {
                _networkService.OpenInBrowser($"https://{target.TargetAddress}");
            }
            StatusMessage = targets.Count == 1
                ? $"Opening HTTPS for {targets.First().Name}..."
                : $"Opening HTTPS for {targets.Count} targets...";
        }

        [RelayCommand]
        private void CancelCurrentOperation()
        {
            _currentCts?.Cancel();
            StatusMessage = "Operation cancelled";
        }

        private OutputTabViewModel GetOrCreateOutputTab(string name, OutputTabType type)
        {
            var tab = new OutputTabViewModel(name, type);
            OutputTabs.Add(tab);
            return tab;
        }

        /// <summary>
        /// Updates result tabs based on what object types were found.
        /// Creates separate tabs for each type found (Computers, Users, Printers).
        /// </summary>
        private void UpdateResultTabs(List<AdObjectInfo> computers, List<AdObjectInfo> users, List<AdObjectInfo>? printers = null, DateTime? lastUpdated = null, bool isFromCache = false, string? sourcePath = null)
        {
            var hasComputers = computers.Count > 0;
            var hasUsers = users.Count > 0;
            var hasPrinters = printers?.Count > 0;
            var typeCount = (hasComputers ? 1 : 0) + (hasUsers ? 1 : 0) + (hasPrinters ? 1 : 0);

            if (typeCount > 1)
            {
                // Multiple types - create separate tabs for each type
                // First, remove any existing Results/Computers/Users/Printers tabs
                var tabsToRemove = OutputTabs.Where(t => 
                    t.Type == OutputTabType.Results || 
                    t.Type == OutputTabType.Computers || 
                    t.Type == OutputTabType.Users ||
                    t.Type == OutputTabType.Printers).ToList();
                
                foreach (var tab in tabsToRemove)
                {
                    OutputTabs.Remove(tab);
                }

                int insertIndex = 0;
                OutputTabViewModel? firstTab = null;

                // Create Computers tab if there are computers
                if (hasComputers)
                {
                    var computersTab = new OutputTabViewModel($"Computers ({computers.Count})", OutputTabType.Computers) { CanClose = false, SourcePath = sourcePath };
                    computersTab.SetAdObjectResults(computers, lastUpdated, isFromCache);
                    PopulateCachedLastUserData(computers);
                    OutputTabs.Insert(insertIndex++, computersTab);
                    firstTab ??= computersTab;
                }

                // Create Users tab if there are users
                if (hasUsers)
                {
                    var usersTab = new OutputTabViewModel($"Users ({users.Count})", OutputTabType.Users) { CanClose = false, SourcePath = sourcePath };
                    usersTab.SetAdObjectResults(users, lastUpdated, isFromCache);
                    OutputTabs.Insert(insertIndex++, usersTab);
                    firstTab ??= usersTab;
                }

                // Create Printers tab if there are printers
                if (hasPrinters && printers != null)
                {
                    var printersTab = new OutputTabViewModel($"Printers ({printers.Count})", OutputTabType.Printers) { CanClose = false, SourcePath = sourcePath };
                    printersTab.SetAdObjectResults(printers, lastUpdated, isFromCache);
                    PopulateCachedLastUserData(printers);  // Populate IP history from cache
                    OutputTabs.Insert(insertIndex++, printersTab);
                    firstTab ??= printersTab;
                }

                // Select the first tab with results
                if (firstTab != null)
                    SelectedOutputTab = firstTab;
                
                // Resolve IP addresses in background
                if (hasComputers)
                {
                    _ = ResolveComputerIpAddressesAsync(computers);
                }
                if (hasPrinters && printers != null)
                {
                    _ = ResolvePrinterIpAddressesAsync(printers);
                }
            }
            else
            {
                // Single type or no results - use single Results tab
                ClearToSingleResultsTab();

                var resultsTab = OutputTabs.FirstOrDefault(t => t.Type == OutputTabType.Results);
                if (resultsTab != null)
                {
                    var allResults = hasComputers ? computers : hasUsers ? users : (printers ?? new List<AdObjectInfo>());
                    resultsTab.SetAdObjectResults(allResults, lastUpdated, isFromCache);
                    resultsTab.SourcePath = sourcePath;
                    if (hasComputers) PopulateCachedLastUserData(allResults);
                    resultsTab.Name = hasComputers ? $"Computers ({allResults.Count})" : 
                                      hasUsers ? $"Users ({allResults.Count})" : 
                                      hasPrinters ? $"Printers ({allResults.Count})" :
                                      "Results";
                    SelectedOutputTab = resultsTab;
                    
                    // Resolve IP addresses in background
                    if (hasComputers)
                    {
                        _ = ResolveComputerIpAddressesAsync(computers);
                    }
                    if (hasPrinters && printers != null)
                    {
                        _ = ResolvePrinterIpAddressesAsync(printers);
                    }
                }
            }
        }

        /// <summary>
        /// Removes Computers/Users/Printers tabs and ensures a single Results tab exists
        /// </summary>
        private void ClearToSingleResultsTab()
        {
            // Remove any Computers, Users, or Printers tabs
            var specialTabs = OutputTabs.Where(t => 
                t.Type == OutputTabType.Computers || 
                t.Type == OutputTabType.Users ||
                t.Type == OutputTabType.Printers).ToList();
            
            foreach (var tab in specialTabs)
            {
                OutputTabs.Remove(tab);
            }

            // Ensure Results tab exists
            var resultsTab = OutputTabs.FirstOrDefault(t => t.Type == OutputTabType.Results);
            if (resultsTab == null)
            {
                resultsTab = new OutputTabViewModel("Domain Results", OutputTabType.Results) { CanClose = false };
                OutputTabs.Insert(0, resultsTab);
            }
        }

        [RelayCommand]
        private void CloseOutputTab(OutputTabViewModel tab)
        {
            if (!tab.CanClose) return; // Respect the CanClose property

            OutputTabs.Remove(tab);
            if (SelectedOutputTab == tab)
            {
                SelectedOutputTab = OutputTabs.FirstOrDefault();
            }
        }

        /// <summary>
        /// Gets whether the current tab has a startup filter set for its group
        /// </summary>
        public bool HasStartupFilter
        {
            get
            {
                var groupId = SelectedOutputTab?.TargetGroupId;
                if (string.IsNullOrEmpty(groupId)) return false;
                var group = App.TargetGroups.Groups.FirstOrDefault(g => g.Id == groupId);
                return group?.StartupFilter != null;
            }
        }

        /// <summary>
        /// Gets whether the current tab belongs to a group (and thus can have a startup filter)
        /// </summary>
        public bool CanSetStartupFilter => SelectedOutputTab?.TargetGroupId != null;

        [RelayCommand]
        private void SetStartupFilter()
        {
            if (SelectedOutputTab?.TargetGroupId == null) return;

            var groupId = SelectedOutputTab.TargetGroupId;
            var group = App.TargetGroups.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return;

            group.StartupFilter = Models.StartupFilter.FromTab(SelectedOutputTab);
            group.ModifiedAt = DateTime.Now;
            App.TargetGroups.Save();

            OnPropertyChanged(nameof(HasStartupFilter));
            StatusMessage = "Startup filter saved for this group";
        }

        [RelayCommand]
        private void LoadStartupFilter()
        {
            if (SelectedOutputTab?.TargetGroupId == null) return;

            var groupId = SelectedOutputTab.TargetGroupId;
            var group = App.TargetGroups.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group?.StartupFilter == null) return;

            group.StartupFilter.ApplyTo(SelectedOutputTab);
            StatusMessage = "Startup filter applied";
        }

        [RelayCommand]
        private void ClearStartupFilter()
        {
            if (SelectedOutputTab?.TargetGroupId == null) return;

            var groupId = SelectedOutputTab.TargetGroupId;
            var group = App.TargetGroups.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return;

            if (group.StartupFilter != null)
            {
                group.StartupFilter = null;
                group.ModifiedAt = DateTime.Now;
                App.TargetGroups.Save();
                OnPropertyChanged(nameof(HasStartupFilter));
                StatusMessage = "Startup filter cleared for this group";
            }
        }

        /// <summary>
        /// Applies the startup filter to a tab if one exists for its group
        /// </summary>
        private void ApplyStartupFilterIfExists(OutputTabViewModel tab)
        {
            if (string.IsNullOrEmpty(tab.TargetGroupId)) return;
            
            var group = App.TargetGroups.Groups.FirstOrDefault(g => g.Id == tab.TargetGroupId);
            if (group?.StartupFilter != null)
            {
                group.StartupFilter.ApplyTo(tab);
            }
        }

        [RelayCommand]
        private void SelectAll()
        {
            SelectAllRequested?.Invoke();
        }

        #endregion

        #region Export Commands

        [RelayCommand]
        private async Task ExportAsync()
        {
            // Handle Last User Database export separately
            if (SelectedExportSource == ExportSource.LastUserDatabase)
            {
                await ExportLastUserDatabaseAsync();
                return;
            }

            if (SelectedOutputTab == null)
            {
                SetError("No data to export");
                return;
            }

            // Vulnerability exports are JSON-only.
            if (SelectedOutputTab.Type == OutputTabType.Vulnerabilities && SelectedExportFormat != ExportFormat.Json)
            {
                SelectedExportFormat = ExportFormat.Json;
            }

            try
            {
                SetBusy("Exporting...");

                // Determine sheet/title name based on tab content
                var sheetName = GetExportSheetName(SelectedOutputTab);

                // Generate filename if needed - include content type in filename
                if (string.IsNullOrWhiteSpace(ExportFileName) || !UseLastFileNameWithTimestamp)
                {
                    var prefix = sheetName.Replace(" ", "");
                    ExportFileName = _exportService.GenerateFileName(prefix, SelectedExportFormat);
                }
                else if (UseLastFileNameWithTimestamp)
                {
                    var baseName = Path.GetFileNameWithoutExtension(ExportFileName);
                    var ext = _exportService.GetExtension(SelectedExportFormat);
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    ExportFileName = $"{baseName}_{timestamp}{ext}";
                }

                // Determine export folder using organized structure
                var exportFolder = ExportFolder;
                if (UseOrganizedExports && SelectedExportFormat != ExportFormat.Clipboard)
                {
                    var objectType = GetExportObjectType();
                    var sourcePath = SelectedOutputTab.SourcePath;
                    exportFolder = _exportService.GetOrganizedExportFolder(ExportFolder, objectType, sourcePath, SelectedExportFormat);
                }
                
                // Create folder if it doesn't exist
                if (!Directory.Exists(exportFolder) && SelectedExportFormat != ExportFormat.Clipboard)
                {
                    Directory.CreateDirectory(exportFolder);
                }

                var filePath = Path.Combine(exportFolder, ExportFileName);

                // Export based on tab type - use filtered/sorted data from tab
                switch (SelectedOutputTab.Type)
                {
                    case OutputTabType.Results:
                    case OutputTabType.Computers:
                    case OutputTabType.Users:
                    case OutputTabType.Printers:
                        // Export the filtered/sorted AdObjectResults (what user sees in DataGrid)
                        var dataToExport = SelectedOutputTab.AdObjectResults;
                        if (dataToExport != null && dataToExport.Count > 0)
                        {
                            // Build a Last User -> email map from the user cache (computer exports only)
                            IDictionary<string, string?>? lastUserEmails = null;
                            if (dataToExport.Any(o => o.ObjectType == AdObjectType.Computer))
                            {
                                var usernames = dataToExport
                                    .Where(o => o.ObjectType == AdObjectType.Computer && !string.IsNullOrWhiteSpace(o.LastUser) && !o.LastUser!.StartsWith("("))
                                    .Select(o =>
                                    {
                                        var u = o.LastUser!;
                                        var slash = u.IndexOf('\\');
                                        return slash >= 0 ? u.Substring(slash + 1) : u;
                                    })
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToList();

                                var cached = _userCacheService.GetUsers(usernames);
                                lastUserEmails = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                                foreach (var u in usernames)
                                {
                                    lastUserEmails[u] = cached.TryGetValue(u, out var entry) ? entry.Email : null;
                                }
                            }

                            await _exportService.ExportAdObjectsAsync(dataToExport, filePath, SelectedExportFormat, sheetName, lastUserEmails);
                        }
                        else
                        {
                            SetError("No data to export (filters may have removed all results)");
                            return;
                        }
                        break;

                    case OutputTabType.Vulnerabilities:
                        var vulnVm = SelectedOutputTab.VulnerabilityScan;
                        if (vulnVm?.Items == null || vulnVm.Items.Count == 0)
                        {
                            SetError("No vulnerabilities to export");
                            return;
                        }

                        await _exportService.ExportVulnerabilitiesAsync(vulnVm.Items, filePath, ExportFormat.Json, sheetName);
                        break;
                }

                // Save export settings
                App.Settings.Current.LastExportFolder = ExportFolder;
                App.Settings.Current.LastExportFileName = ExportFileName;
                App.Settings.Current.LastExportFormat = SelectedExportFormat;
                App.Settings.Save();

                StatusMessage = SelectedExportFormat == ExportFormat.Clipboard
                    ? "Copied to clipboard"
                    : $"Exported to {filePath}";
            }
            catch (Exception ex)
            {
                SetError($"Export failed: {ex.Message}");
            }
            finally
            {
                ClearBusy();
            }
        }

        /// <summary>
        /// Exports the Last User database cache
        /// </summary>
        private async Task ExportLastUserDatabaseAsync()
        {
            try
            {
                SetBusy("Exporting User Query History...");

                // Use fixed filename (no timestamp) since this is cumulative data
                var ext = _exportService.GetExtension(SelectedExportFormat);
                ExportFileName = $"UserQueryHistory{ext}";

                // Determine export folder
                var exportFolder = ExportFolder;
                if (UseOrganizedExports && SelectedExportFormat != ExportFormat.Clipboard)
                {
                    var formatFolder = SelectedExportFormat.ToString();
                    exportFolder = Path.Combine(ExportFolder, "Active Scanner Exports", "User History", formatFolder);
                }
                
                // Create folder if it doesn't exist
                if (!Directory.Exists(exportFolder) && SelectedExportFormat != ExportFormat.Clipboard)
                {
                    Directory.CreateDirectory(exportFolder);
                }

                var filePath = Path.Combine(exportFolder, ExportFileName);

                // Get all cache entries
                var cacheEntries = _computerCacheService.GetAllEntries().ToList();

                if (cacheEntries.Count == 0)
                {
                    SetError("No cached user query data to export");
                    return;
                }

                await _exportService.ExportCacheEntriesAsync(cacheEntries, filePath, SelectedExportFormat);

                // Save export settings
                App.Settings.Current.LastExportFolder = ExportFolder;
                App.Settings.Current.LastExportFileName = ExportFileName;
                App.Settings.Current.LastExportFormat = SelectedExportFormat;
                App.Settings.Save();

                StatusMessage = SelectedExportFormat == ExportFormat.Clipboard
                    ? "Copied to clipboard"
                    : $"Exported {cacheEntries.Count} entries to {filePath}";
            }
            catch (Exception ex)
            {
                SetError($"Export failed: {ex.Message}");
            }
            finally
            {
                ClearBusy();
            }
        }

        /// <summary>
        /// Determines the appropriate sheet/title name based on tab content
        /// </summary>
        private static string GetExportSheetName(OutputTabViewModel tab)
        {
            // For typed tabs, use the type directly
            return tab.Type switch
            {
                OutputTabType.Computers => "Computers",
                OutputTabType.Users => "Users",
                OutputTabType.Printers => "Printers",
                OutputTabType.Vulnerabilities => "Vulnerabilities",
                OutputTabType.Results => GetResultsSheetName(tab),
                _ => "Results"
            };
        }

        /// <summary>
        /// Determines sheet name for Results tab based on actual content
        /// </summary>
        private static string GetResultsSheetName(OutputTabViewModel tab)
        {
            var hasComputers = tab.ShowsComputers;
            var hasUsers = tab.ShowsUsers;
            var hasPrinters = tab.ShowsPrinters;
            var typeCount = (hasComputers ? 1 : 0) + (hasUsers ? 1 : 0) + (hasPrinters ? 1 : 0);

            if (typeCount > 1)
                return "AD Objects";
            if (hasComputers)
                return "Computers";
            if (hasUsers)
                return "Users";
            if (hasPrinters)
                return "Printers";
            
            return "Results";
        }

        [RelayCommand]
        private void BrowseExportFolder()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                InitialDirectory = ExportFolder,
                FileName = ExportFileName,
                Filter = GetExportFilter()
            };

            if (dialog.ShowDialog() == true)
            {
                ExportFolder = Path.GetDirectoryName(dialog.FileName) ?? ExportFolder;
                ExportFileName = Path.GetFileName(dialog.FileName);
            }
        }

        private string GetExportFilter()
        {
            return "Excel Files (*.xlsx)|*.xlsx|CSV Files (*.csv)|*.csv|JSON Files (*.json)|*.json|" +
                   "Text Files (*.txt)|*.txt|HTML Files (*.html)|*.html|All Files (*.*)|*.*";
        }

        #endregion

        #region Settings & Theme

        [RelayCommand]
        private void ToggleDarkMode()
        {
            IsDarkMode = !IsDarkMode;
            App.ApplyTheme(IsDarkMode);
            App.Settings.Current.DarkMode = IsDarkMode;
            App.Settings.Save();
        }

        #endregion

        #region Admin Credential Management

        // P/Invoke for Windows Credential UI
        [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int CredUIPromptForCredentialsW(
            ref CREDUI_INFO pUiInfo,
            string pszTargetName,
            IntPtr pContext,
            int dwAuthError,
            System.Text.StringBuilder pszUserName,
            int ulUserNameBufferSize,
            System.Text.StringBuilder pszPassword,
            int ulPasswordBufferSize,
            [MarshalAs(UnmanagedType.Bool)] ref bool pfSave,
            int dwFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDUI_INFO
        {
            public int cbSize;
            public IntPtr hwndParent;
            public string pszMessageText;
            public string pszCaptionText;
            public IntPtr hbmBanner;
        }

        private const int CREDUI_FLAGS_GENERIC_CREDENTIALS = 0x40000;
        private const int CREDUI_FLAGS_DO_NOT_PERSIST = 0x2;
        private const int CREDUI_FLAGS_ALWAYS_SHOW_UI = 0x80;
        private const int ERROR_CANCELLED = 1223;
        private const int NO_ERROR = 0;

        [RelayCommand]
        private async Task AdminLoginAsync()
        {
            if (IsAdminLoggedIn)
            {
                // Logout
                AdminLogout();
                return;
            }

            // Show Windows credential dialog
            var credInfo = new CREDUI_INFO
            {
                cbSize = Marshal.SizeOf(typeof(CREDUI_INFO)),
                hwndParent = IntPtr.Zero,
                pszCaptionText = "Admin Login - ActiveScanner",
                pszMessageText = "Enter admin credentials for remote computer management.\nThese credentials will be used for operations like gpupdate, restart, etc.",
                hbmBanner = IntPtr.Zero
            };

            var username = new System.Text.StringBuilder(256);
            var password = new System.Text.StringBuilder(256);
            bool save = false;

            int flags = CREDUI_FLAGS_GENERIC_CREDENTIALS | CREDUI_FLAGS_DO_NOT_PERSIST | CREDUI_FLAGS_ALWAYS_SHOW_UI;

            int result = CredUIPromptForCredentialsW(
                ref credInfo,
                "ActiveScanner Remote Admin",
                IntPtr.Zero,
                0,
                username,
                username.Capacity,
                password,
                password.Capacity,
                ref save,
                flags);

            if (result == NO_ERROR)
            {
                string user = username.ToString();
                string pass = password.ToString();

                // Clear the password StringBuilder for security
                password.Clear();

                // Parse domain\user or user@domain format
                string? domain = null;
                string actualUser = user;

                if (user.Contains('\\'))
                {
                    var parts = user.Split('\\', 2);
                    domain = parts[0];
                    actualUser = parts[1];
                }
                else if (user.Contains('@'))
                {
                    var parts = user.Split('@', 2);
                    actualUser = parts[0];
                    domain = parts[1];
                }

                // If the user didn't specify a domain (just typed "mike"), default to the
                // current logon domain so downstream calls (LDAP writes, WinRM) get a
                // valid identity instead of an empty domain that some APIs reject.
                if (string.IsNullOrEmpty(domain))
                {
                    domain = Environment.UserDomainName;
                }

                _adminCredential = new NetworkCredential(actualUser, pass, domain ?? string.Empty);
                
                // Validate credentials by attempting to bind to LDAP
                var validationResult = await ValidateCredentialsAsync(_adminCredential);
                if (validationResult.Success)
                {
                    AdminUsername = user;
                    IsAdminLoggedIn = true;
                    StatusMessage = $"Logged in as admin: {user}";
                }
                else
                {
                    _adminCredential = null;
                    MessageBox.Show($"Login failed: {validationResult.Error}\n\nPlease check your username and password.", 
                        "Authentication Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else if (result == ERROR_CANCELLED)
            {
                // User cancelled - do nothing
            }
            else
            {
                MessageBox.Show($"Failed to get credentials. Error code: {result}", "Credential Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void AdminLogout()
        {
            _adminCredential = null;
            AdminUsername = string.Empty;
            IsAdminLoggedIn = false;
            StatusMessage = "Admin logged out";
        }

        /// <summary>
        /// Validates credentials by attempting to bind to the domain using LDAP.
        /// </summary>
        private async Task<(bool Success, string? Error)> ValidateCredentialsAsync(NetworkCredential credential)
        {
            return await Task.Run<(bool, string?)>(() =>
            {
                try
                {
                    // Try to get the current domain
                    string? domainPath = null;
                    
                    if (!string.IsNullOrEmpty(credential.Domain))
                    {
                        // Use the provided domain
                        domainPath = $"LDAP://{credential.Domain}";
                    }
                    else
                    {
                        // Try to get from current domain
                        try
                        {
                            using var rootDse = new DirectoryEntry("LDAP://RootDSE");
                            var defaultNamingContext = rootDse.Properties["defaultNamingContext"]?.Value?.ToString();
                            if (!string.IsNullOrEmpty(defaultNamingContext))
                            {
                                domainPath = $"LDAP://{defaultNamingContext}";
                            }
                        }
                        catch
                        {
                            return (false, "Could not determine domain. Please use domain\\username format.");
                        }
                    }

                    if (string.IsNullOrEmpty(domainPath))
                    {
                        return (false, "Could not determine domain path");
                    }

                    // Build the username in domain\user format for LDAP binding
                    string ldapUsername = string.IsNullOrEmpty(credential.Domain) 
                        ? credential.UserName 
                        : $"{credential.Domain}\\{credential.UserName}";

                    // Attempt to bind to LDAP with the credentials
                    using var entry = new DirectoryEntry(domainPath, ldapUsername, credential.Password);
                    
                    // Force authentication by accessing a property
                    var nativeObject = entry.NativeObject;
                    
                    return (true, null);
                }
                catch (DirectoryServicesCOMException ex)
                {
                    // Common error codes:
                    // 0x8007052E = Logon failure: unknown user name or bad password
                    // 0x80070775 = Account locked out
                    // 0x80070532 = Password expired
                    // 0x80070533 = Account disabled
                    var errorCode = ex.ErrorCode;
                    
                    if (errorCode == unchecked((int)0x8007052E))
                        return (false, "Invalid username or password");
                    if (errorCode == unchecked((int)0x80070775))
                        return (false, "Account is locked out");
                    if (errorCode == unchecked((int)0x80070532))
                        return (false, "Password has expired");
                    if (errorCode == unchecked((int)0x80070533))
                        return (false, "Account is disabled");
                    
                    return (false, ex.Message);
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });
        }

        partial void OnCurrentPathChanged(string value)
        {
            UpdateBreadcrumbs(value);
        }

        partial void OnSelectedComputerChanged(AdObjectInfo? value)
        {
        }

        partial void OnStatusMessageChanged(string value)
        {
            // Cancel any pending clear
            _statusClearCts?.Cancel();
            _statusClearCts?.Dispose();
            _statusClearCts = null;

            // Don't auto-clear "Ready" or empty messages
            if (string.IsNullOrEmpty(value) || value == "Ready")
                return;

            // Don't auto-clear in-progress messages (contain "...")
            if (value.Contains("..."))
                return;

            // Schedule auto-clear
            _statusClearCts = new CancellationTokenSource();
            var token = _statusClearCts.Token;
            _ = Task.Delay(StatusClearDelayMs, token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        if (StatusMessage == value) // Only clear if message hasn't changed
                            StatusMessage = string.Empty;
                    });
                }
            }, token);
        }

        #endregion

        #region Breadcrumb Navigation

        private void UpdateBreadcrumbs(string dn)
        {
            Breadcrumbs.Clear();
            if (string.IsNullOrWhiteSpace(dn)) return;

            // Parse the DN into components
            // e.g., "OU=Districts,OU=resources,DC=va,DC=gov" -> ["va.gov", "resources", "Districts"]
            var parts = dn.Split(',');
            var items = new List<BreadcrumbItem>();

            // Build the domain part first (DC components)
            var dcParts = parts.Where(p => p.Trim().StartsWith("DC=", StringComparison.OrdinalIgnoreCase)).ToList();
            if (dcParts.Any())
            {
                var domainName = string.Join(".", dcParts.Select(p => p.Split('=')[1]));
                var domainDn = string.Join(",", dcParts);
                items.Add(new BreadcrumbItem { DisplayName = domainName, DistinguishedName = domainDn });
            }

            // Build the OU/CN path (in reverse order since DN is leaf-first)
            var ouParts = parts.Where(p => 
                p.Trim().StartsWith("OU=", StringComparison.OrdinalIgnoreCase) ||
                p.Trim().StartsWith("CN=", StringComparison.OrdinalIgnoreCase)).Reverse().ToList();

            var accumulatedPath = string.Join(",", dcParts);
            foreach (var part in ouParts)
            {
                accumulatedPath = part + "," + accumulatedPath;
                var name = part.Split('=')[1];
                items.Add(new BreadcrumbItem { DisplayName = name, DistinguishedName = accumulatedPath });
            }

            // Mark the last item
            if (items.Any())
            {
                items.Last().IsLast = true;
            }

            foreach (var item in items)
            {
                Breadcrumbs.Add(item);
            }
        }

        [RelayCommand]
        private async Task NavigateToBreadcrumbAsync(BreadcrumbItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.DistinguishedName)) return;
            if (item.DistinguishedName == CurrentPath) return; // Already there

            try
            {
                SetBusy("Navigating...");

                _navigationStack.Push(CurrentPath);
                CanGoBack = true;

                _currentEntry?.Dispose();
                _currentEntry = _ldapService.CreateEntry(item.DistinguishedName);
                CurrentPath = item.DistinguishedName;

                // Update view state
                CurrentViewName = item.DisplayName;
                HasCurrentView = true;
                SelectedGroup = null;

                // Check cache first
                var cacheTtl = App.Settings.Current.CacheTtlMinutes;
                if (cacheTtl > 0)
                {
                    var cachedEntry = _cacheService.GetDomainCache(item.DistinguishedName, cacheTtl);
                    if (cachedEntry != null)
                    {
                        LoadFromCache(cachedEntry);
                        IsViewingCachedResults = true;
                        LastUpdatedDisplay = cachedEntry.AgeDisplay;
                        StatusMessage = $"Loaded from cache ({cachedEntry.AgeDisplay})";
                        return;
                    }
                }

                // No cache, do fresh scan
                await ScanCurrentFolderAsync(saveToCache: true);
            }
            catch (Exception ex)
            {
                SetError($"Navigation failed: {ex.Message}");
            }
            finally
            {
                ClearBusy();
            }
        }

        #endregion

        #region Cache Population

        /// <summary>
        /// Populates cached last user data and IP history on AdObjectInfo items
        /// </summary>
        public void PopulateCachedLastUserData(IEnumerable<Models.AdObjectInfo> items)
        {
            foreach (var item in items)
            {
                if (!item.ObjectGuid.HasValue) continue;

                var cached = _computerCacheService.GetEntry(item.ObjectGuid.Value);
                if (cached == null) continue;

                // Populate IP history for computers and printers
                if (cached.KnownIpAddresses?.Count > 0)
                {
                    item.KnownIpAddresses = new Dictionary<string, DateTime>(cached.KnownIpAddresses);
                }

                // Restore last network activity
                if (cached.LastNetworkActivity.HasValue && (!item.LastNetworkActivity.HasValue || cached.LastNetworkActivity.Value > item.LastNetworkActivity.Value))
                {
                    item.LastNetworkActivity = cached.LastNetworkActivity.Value;
                }

                // Only computers have last user data
                if (item.ObjectType != Models.AdObjectType.Computer) continue;

                // Only populate if we don't already have data (fresh query takes precedence)
                if (item.LastUserProfiles != null || !string.IsNullOrEmpty(item.LastUser)) continue;

                // Populate from cache - use KnownUsers
                if (!string.IsNullOrEmpty(cached.LastUser))
                {
                    item.LastUser = cached.LastUser;
                    item.LastUserQueriedAt = cached.LastUserQueriedAt;
                    item.LastUserActiveAt = cached.LastUserActiveAt;
                    item.LastUserProfiles = Services.ComputerCacheService.ConvertToUserProfileInfos(cached.KnownUsers);
                }
            }
        }

        /// <summary>
        /// Gets the computer cache service (for external access if needed)
        /// </summary>
        public Services.ComputerCacheService ComputerCacheService => _computerCacheService;

        #endregion

        public void Cleanup()
        {
            // Cancel any running operations FIRST to prevent blocking
            _currentCts?.Cancel();
            _statusClearCts?.Cancel();
            
            SaveSettings();
            _computerCacheService.SaveAsync(force: true).Wait(); // Checkpoint LiteDB
            _computerCacheService.Dispose(); // Dispose LiteDB connection
            _userCacheService.Dispose(); // Dispose user cache LiteDB connection
            _currentEntry?.Dispose();
            _currentCts?.Dispose();
            _statusClearCts?.Dispose();
            
            // Clear admin credentials on exit
            _adminCredential = null;
            IsAdminLoggedIn = false;
        }
    }
}
