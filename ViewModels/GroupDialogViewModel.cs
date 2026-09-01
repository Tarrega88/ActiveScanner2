using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.DirectoryServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ActiveScanner.Models;
using ActiveScanner.Services;

namespace ActiveScanner.ViewModels
{
    /// <summary>
    /// ViewModel for Create/Edit Group dialogs with folder navigation
    /// </summary>
    public partial class GroupDialogViewModel : ViewModelBase
    {
        private readonly LdapService _ldapService;
        private readonly Stack<string> _navigationStack = new();
        private DirectoryEntry? _currentEntry;
        private CancellationTokenSource? _currentCts;
        private readonly List<string> _existingGroupNames;
        
        // HashSet for O(1) duplicate path checking
        private readonly HashSet<string> _addedPathDNs = new(StringComparer.OrdinalIgnoreCase);

        #region Observable Properties

        [ObservableProperty]
        private string _groupName = string.Empty;

        [ObservableProperty]
        private string _groupNameError = string.Empty;

        [ObservableProperty]
        private bool _isEditMode;

        [ObservableProperty]
        private string? _originalGroupId;

        [ObservableProperty]
        private GroupObjectType _groupObjectType = GroupObjectType.None;

        [ObservableProperty]
        private ObservableCollection<TargetPath> _groupPaths = new();

        [ObservableProperty]
        private TargetPath? _selectedGroupPath;

        // Domain selection
        [ObservableProperty]
        private ObservableCollection<DomainConfig> _presetDomains = new();

        [ObservableProperty]
        private DomainConfig? _selectedDomain;

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

        // Status
        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _busyMessage = string.Empty;

        [ObservableProperty]
        private string _lastAddedMessage = string.Empty;

        #endregion

        /// <summary>
        /// Event raised when the dialog should close with a result
        /// </summary>
        public event EventHandler<bool>? RequestClose;

        public GroupDialogViewModel(IEnumerable<string> existingGroupNames, TargetGroup? groupToEdit = null)
        {
            _ldapService = new LdapService();
            _existingGroupNames = existingGroupNames.ToList();

            InitializePresetDomains();
            _ = DiscoverDomainsOnStartupAsync(); // Auto-discover domains

            if (groupToEdit != null)
            {
                // Edit mode
                IsEditMode = true;
                OriginalGroupId = groupToEdit.Id;
                GroupName = groupToEdit.Name;
                GroupObjectType = groupToEdit.ObjectType;
                
                // Remove original name from validation list (allow keeping same name)
                _existingGroupNames.Remove(groupToEdit.Name);

                foreach (var path in groupToEdit.Paths)
                {
                    // Always regenerate DisplayName and DomainName from DN for consistent full path display
                    GroupPaths.Add(new TargetPath(path.DistinguishedName, null, null));
                    _addedPathDNs.Add(path.DistinguishedName);
                }
            }
        }

        /// <summary>
        /// Discovers domains on startup and adds them to the dropdown
        /// </summary>
        private async Task DiscoverDomainsOnStartupAsync()
        {
            try
            {
                var domains = await _ldapService.DiscoverDomainsAsync();
                
                if (domains.Count == 0) return;

                var existingDns = PresetDomains.Select(d => d.DistinguishedName.ToUpperInvariant()).ToHashSet();
                var newDomains = domains.Where(d => !existingDns.Contains(d.DistinguishedName.ToUpperInvariant())).ToList();

                foreach (var domain in newDomains)
                {
                    PresetDomains.Add(domain);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Startup domain discovery failed: {ex.Message}");
            }
        }

        partial void OnSelectedDomainChanged(DomainConfig? value)
        {
            if (value != null)
            {
                _ = NavigateToPathAsync(value.DistinguishedName);
            }
        }

        private void InitializePresetDomains()
        {
            // Same domains as main window - MUST match MainViewModel.InitializePresetDomains()
            PresetDomains.Add(new DomainConfig("va.gov", "DC=va,DC=gov", true));
            PresetDomains.Add(new DomainConfig("med/*", "DC=med,DC=va,DC=gov", true));
        }

        #region Validation

        partial void OnGroupNameChanged(string value)
        {
            ValidateGroupName();
        }

        private bool ValidateGroupName()
        {
            if (string.IsNullOrWhiteSpace(GroupName))
            {
                GroupNameError = "Group name is required";
                return false;
            }

            if (_existingGroupNames.Contains(GroupName, StringComparer.OrdinalIgnoreCase))
            {
                GroupNameError = "A group with this name already exists";
                return false;
            }

            GroupNameError = string.Empty;
            return true;
        }

        #endregion

        #region Navigation Commands

        private async Task NavigateToPathAsync(string dn)
        {
            try
            {
                SetBusy("Connecting...");

                _currentEntry?.Dispose();
                _currentEntry = _ldapService.CreateEntry(dn);

                CurrentPath = dn;
                
                // Build navigation stack from parent DNs so back button works
                BuildNavigationStackFromDn(dn);
                CanGoBack = _navigationStack.Count > 0;
                
                UpdateBreadcrumbs();

                SetBusy("Loading folders...");
                await LoadSubfoldersAsync();

                ClearBusy();
            }
            catch (Exception ex)
            {
                SetError($"Navigation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the navigation stack from parent DNs so the back button works when navigating to deep paths
        /// </summary>
        private void BuildNavigationStackFromDn(string dn)
        {
            _navigationStack.Clear();
            
            if (string.IsNullOrWhiteSpace(dn)) return;

            var parts = dn.Split(',');
            if (parts.Length <= 1) return;

            var parentPaths = new List<string>();
            
            for (int i = 1; i < parts.Length; i++)
            {
                var parentDn = string.Join(",", parts.Skip(i));
                
                // Stop at domain root (DC=...,DC=...,DC=...,DC=...)
                if (parentDn.StartsWith("DC=", StringComparison.OrdinalIgnoreCase) && 
                    !parentDn.Contains("OU=", StringComparison.OrdinalIgnoreCase) &&
                    !parentDn.Contains("CN=", StringComparison.OrdinalIgnoreCase))
                {
                    parentPaths.Add(parentDn);
                    break;
                }
                
                parentPaths.Add(parentDn);
            }

            parentPaths.Reverse();
            foreach (var path in parentPaths)
            {
                _navigationStack.Push(path);
            }
        }

        private async Task LoadSubfoldersAsync()
        {
            if (_currentEntry == null) return;

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                var folders = await _ldapService.GetChildFoldersAsync(_currentEntry, _currentCts.Token);

                Subfolders.Clear();
                foreach (var folder in folders)
                {
                    Subfolders.Add(folder);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled, ignore
            }
            catch (Exception ex)
            {
                SetError($"Failed to load folders: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task GoBackAsync()
        {
            if (_navigationStack.Count > 0)
            {
                var previousPath = _navigationStack.Pop();
                await NavigateToPathAsync(previousPath);
            }
        }

        [RelayCommand]
        private async Task NavigateToFolderAsync(FolderItem folder)
        {
            if (!string.IsNullOrEmpty(CurrentPath))
            {
                _navigationStack.Push(CurrentPath);
            }
            await NavigateToPathAsync(folder.DistinguishedName);
        }

        [RelayCommand]
        private async Task NavigateToBreadcrumbAsync(BreadcrumbItem breadcrumb)
        {
            // Clear navigation stack and navigate to the selected breadcrumb
            _navigationStack.Clear();
            await NavigateToPathAsync(breadcrumb.DistinguishedName);
        }

        private void UpdateBreadcrumbs()
        {
            Breadcrumbs.Clear();

            if (string.IsNullOrEmpty(CurrentPath)) return;

            var parts = CurrentPath.Split(',');
            var accumulated = new List<string>();

            // Build breadcrumbs from right to left (domain first)
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                accumulated.Insert(0, parts[i]);
                var dn = string.Join(",", accumulated);

                var part = parts[i];
                var eqIndex = part.IndexOf('=');
                var displayName = eqIndex > 0 ? part.Substring(eqIndex + 1) : part;

                Breadcrumbs.Add(new BreadcrumbItem
                {
                    DisplayName = displayName,
                    DistinguishedName = dn,
                    IsLast = i == 0
                });
            }
        }

        public void HandleFolderDoubleClick(FolderItem folder)
        {
            if (folder != null)
            {
                _ = NavigateToFolderAsync(folder);
            }
        }

        #endregion

        #region Group Path Commands

        [RelayCommand]
        private async Task AddCurrentToGroupAsync()
        {
            if (string.IsNullOrEmpty(CurrentPath))
            {
                SetError("Please navigate to a folder first");
                return;
            }

            // Check if already added - O(1) HashSet lookup instead of O(n) .Any()
            if (_addedPathDNs.Contains(CurrentPath))
            {
                SetError("This path is already in the group");
                return;
            }

            try
            {
                SetBusy("Detecting object type...");

                // Detect what type of objects are in this folder
                if (_currentEntry == null)
                {
                    SetError("No folder selected");
                    return;
                }

                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                var detectedType = await _ldapService.DetectFolderObjectTypeAsync(_currentEntry, _currentCts.Token);

                // Validation based on group type setting
                if (GroupObjectType == GroupObjectType.None)
                {
                    // Multi-Type: Accept any folder (including empty ones)
                    // No validation needed
                }
                else
                {
                    // Specific type: Validate folder contents
                    if (detectedType != GroupObjectType.None && detectedType != GroupObjectType)
                    {
                        SetError($"This folder contains {detectedType} but the group is set to {GroupObjectType}. Change the group type or select a different folder.");
                        return;
                    }
                    // Empty folders (detectedType == None) are allowed
                }

                // Build full display path from breadcrumbs (e.g., "Resources/Districts/Pacific (PA)/Alaska/Anchorage (VHAANC)/Users")
                var pathParts = Breadcrumbs.Select(b => b.DisplayName).ToList();
                var displayName = pathParts.Count > 0 ? string.Join("/", pathParts) : "Unknown";

                // Use null for domainName so TargetPath extracts it from the DN (avoids using shortcut names)
                var newPath = new TargetPath(CurrentPath, displayName, null);
                GroupPaths.Add(newPath);
                _addedPathDNs.Add(CurrentPath);

                var shortName = Breadcrumbs.LastOrDefault()?.DisplayName ?? "Unknown";
                LastAddedMessage = $"Added \"{shortName}\" to {(string.IsNullOrEmpty(GroupName) ? "group" : $"\"{GroupName}\"")}";
                ClearBusy();
                StatusMessage = LastAddedMessage;
            }
            catch (OperationCanceledException)
            {
                ClearBusy();
            }
            catch (Exception ex)
            {
                SetError($"Failed to add path: {ex.Message}");
            }
        }

        [RelayCommand]
        private void RemovePathFromGroup(TargetPath path)
        {
            GroupPaths.Remove(path);
            _addedPathDNs.Remove(path.DistinguishedName);

            // If no paths left, reset the object type
            if (GroupPaths.Count == 0)
            {
                GroupObjectType = GroupObjectType.None;
            }

            StatusMessage = $"Removed \"{path.DisplayName}\" from group";
        }

        #endregion

        #region Dialog Commands

        [RelayCommand]
        private void Save()
        {
            if (!ValidateGroupName())
            {
                return;
            }

            if (GroupPaths.Count == 0)
            {
                SetError("Please add at least one path to the group");
                return;
            }

            RequestClose?.Invoke(this, true);
        }

        [RelayCommand]
        private void Cancel()
        {
            RequestClose?.Invoke(this, false);
        }

        #endregion

        #region Status Helpers

        private new void SetBusy(string message)
        {
            IsBusy = true;
            BusyMessage = message;
            StatusMessage = message;
        }

        private new void ClearBusy()
        {
            IsBusy = false;
            BusyMessage = string.Empty;
            StatusMessage = string.Empty;
        }

        private new void SetError(string message)
        {
            IsBusy = false;
            BusyMessage = string.Empty;
            StatusMessage = message;
        }

        #endregion

        /// <summary>
        /// Creates the TargetGroup from the current state
        /// </summary>
        public TargetGroup CreateGroup()
        {
            var group = new TargetGroup(GroupName)
            {
                ObjectType = GroupObjectType,
                Paths = GroupPaths.ToList(),
                ModifiedAt = DateTime.Now
            };

            if (IsEditMode && !string.IsNullOrEmpty(OriginalGroupId))
            {
                group.Id = OriginalGroupId;
            }

            return group;
        }

        public void Dispose()
        {
            _currentCts?.Cancel();
            _currentEntry?.Dispose();
        }
    }
}
