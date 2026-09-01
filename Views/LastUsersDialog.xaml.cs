using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ActiveScanner.Models;

namespace ActiveScanner.Views
{
    /// <summary>
    /// Dialog for viewing all user profiles from a computer
    /// </summary>
    public partial class LastUsersDialog : Window
    {
        private List<UserProfileInfo> _activeUsers = new();
        private List<UserProfileInfo> _profileUsers = new();
        private string _computerName = string.Empty;
        private AdObjectInfo? _adObject;
        private Func<AdObjectInfo, Task>? _refreshCallback;
        private Func<IEnumerable<string>, Task<Dictionary<string, (string? Email, string? DisplayName)>>>? _emailLookupCallback;

        public LastUsersDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Load user profiles for display (simple overload without refresh)
        /// </summary>
        public void LoadUsers(string computerName, List<UserProfileInfo> users)
        {
            _computerName = computerName;
            _activeUsers = (users ?? new List<UserProfileInfo>())
                .Where(u => u.LastSeenLoggedIn.HasValue)
                .OrderByDescending(u => u.LastSeenLoggedIn)
                .ToList();
            _profileUsers = (users ?? new List<UserProfileInfo>())
                .Where(u => u.LastProfileScan.HasValue)
                .OrderByDescending(u => u.LastProfileScan)
                .ToList();

            ComputerNameText.Text = computerName;
            UpdateUserCountText();
            UpdateLastQueriedText();
            
            ActiveUsersGrid.ItemsSource = _activeUsers;
            ProfileUsersGrid.ItemsSource = _profileUsers;
            
            // Hide refresh button if no AD object is provided
            RefreshButton.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Load user profiles with refresh capability
        /// </summary>
        public void LoadUsers(AdObjectInfo adObject, Func<AdObjectInfo, Task> refreshCallback)
        {
            LoadUsers(adObject, refreshCallback, null);
        }

        /// <summary>
        /// Load user profiles with refresh and email lookup capabilities
        /// </summary>
        public void LoadUsers(
            AdObjectInfo adObject, 
            Func<AdObjectInfo, Task> refreshCallback,
            Func<IEnumerable<string>, Task<Dictionary<string, (string? Email, string? DisplayName)>>>? emailLookupCallback)
        {
            _adObject = adObject;
            _refreshCallback = refreshCallback;
            _emailLookupCallback = emailLookupCallback;
            _computerName = adObject.Name;
            
            // Load from database
            LoadUsersFromCache(adObject);
            
            ComputerNameText.Text = _computerName;
            UpdateUserCountText();
            UpdateLastQueriedText();
            
            ActiveUsersGrid.ItemsSource = _activeUsers;
            ProfileUsersGrid.ItemsSource = _profileUsers;
            
            // Show refresh button since we have the callback
            RefreshButton.Visibility = Visibility.Visible;

            // Trigger email lookup for users without emails
            if (_emailLookupCallback != null)
            {
                _ = LookupEmailsAsync();
            }
        }
        
        /// <summary>
        /// Load users from the database cache into both lists
        /// </summary>
        private void LoadUsersFromCache(AdObjectInfo adObject)
        {
            _activeUsers = new List<UserProfileInfo>();
            _profileUsers = new List<UserProfileInfo>();
            
            if (!adObject.ObjectGuid.HasValue)
                return;
            
            var cached = App.ComputerCache.GetEntry(adObject.ObjectGuid.Value);
            if (cached?.KnownUsers == null || cached.KnownUsers.Count == 0)
                return;
            
            // Active users: only those we've seen logged in
            _activeUsers = cached.KnownUsers
                .Where(u => u.LastSeenLoggedIn.HasValue)
                .OrderByDescending(u => u.LastSeenLoggedIn)
                .Select(u => new UserProfileInfo
                {
                    Username = u.Username,
                    LocalPath = u.LocalPath ?? string.Empty,
                    LastUseTime = u.LastUseTime,
                    Sid = u.Sid,
                    IsCurrentlyLoggedIn = u.IsCurrentlyLoggedIn,
                    LastSeenLoggedIn = u.LastSeenLoggedIn,
                    LastProfileScan = u.LastProfileScan
                })
                .ToList();
            
            // Profile users: all profiles that have been scanned
            _profileUsers = cached.KnownUsers
                .Where(u => u.LastProfileScan.HasValue)
                .OrderByDescending(u => u.LastProfileScan)
                .Select(u => new UserProfileInfo
                {
                    Username = u.Username,
                    LocalPath = u.LocalPath ?? string.Empty,
                    LastUseTime = u.LastUseTime,
                    Sid = u.Sid,
                    IsCurrentlyLoggedIn = u.IsCurrentlyLoggedIn,
                    LastSeenLoggedIn = u.LastSeenLoggedIn,
                    LastProfileScan = u.LastProfileScan
                })
                .ToList();
        }

        /// <summary>
        /// Lookup emails for users that don't have them populated
        /// </summary>
        private async Task LookupEmailsAsync()
        {
            if (_emailLookupCallback == null)
                return;

            try
            {
                // Get all unique usernames that need email lookup
                var allUsers = _activeUsers.Concat(_profileUsers).ToList();
                var usernamesNeedingLookup = allUsers
                    .Where(u => string.IsNullOrEmpty(u.Email))
                    .Select(u => u.Username)
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Distinct()
                    .ToList();

                if (usernamesNeedingLookup.Count == 0)
                    return;

                // Call the lookup callback
                var results = await _emailLookupCallback(usernamesNeedingLookup);

                // Update both user profile lists with the email results
                foreach (var user in allUsers)
                {
                    if (results.TryGetValue(user.Username, out var info))
                    {
                        user.Email = info.Email;
                        user.DisplayName = info.DisplayName;
                    }
                }

                // Refresh both grids to show the new emails
                ActiveUsersGrid.Items.Refresh();
                ProfileUsersGrid.Items.Refresh();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Email lookup failed: {ex.Message}");
            }
        }

        private void UpdateUserCountText()
        {
            var activeCount = _activeUsers.Count;
            var profileCount = _profileUsers.Count;
            UserCountText.Text = $"{activeCount} user(s) seen logged in, {profileCount} profile(s) scanned";
        }

        private void UpdateLastQueriedText()
        {
            if (_adObject?.LastUserQueriedAt.HasValue == true)
            {
                var age = _adObject.LastUserAgeText;
                var isStale = _adObject.LastUserIsStale;
                
                LastQueriedText.Text = $"Last queried: {age}";
                if (isStale)
                {
                    LastQueriedText.Text += " (stale - consider refreshing)";
                    LastQueriedText.Foreground = FindResource("MaterialDesignValidationErrorBrush") as System.Windows.Media.Brush 
                                                 ?? LastQueriedText.Foreground;
                }
            }
            else
            {
                LastQueriedText.Text = string.Empty;
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (_adObject == null || _refreshCallback == null)
                return;

            try
            {
                // Show loading state
                RefreshButton.Visibility = Visibility.Collapsed;
                LoadingPanel.Visibility = Visibility.Visible;
                ActiveUsersGrid.IsEnabled = false;
                ProfileUsersGrid.IsEnabled = false;

                // Call the refresh callback (which will query and update the cache)
                await _refreshCallback(_adObject);

                // Reload from database after the refresh
                LoadUsersFromCache(_adObject);
                ActiveUsersGrid.ItemsSource = _activeUsers;
                ProfileUsersGrid.ItemsSource = _profileUsers;
                UpdateUserCountText();
                UpdateLastQueriedText();

                // Re-run email lookup for the refreshed profiles
                if (_emailLookupCallback != null)
                {
                    _ = LookupEmailsAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to refresh: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                // Restore normal state
                RefreshButton.Visibility = Visibility.Visible;
                LoadingPanel.Visibility = Visibility.Collapsed;
                ActiveUsersGrid.IsEnabled = true;
                ProfileUsersGrid.IsEnabled = true;
            }
        }

        private void CopyUsername_Click(object sender, RoutedEventArgs e)
        {
            var currentGrid = GetCurrentGrid();
            if (currentGrid?.SelectedItem is UserProfileInfo user)
            {
                Clipboard.SetText(user.Username);
            }
        }

        private void CopyUsernameButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string username)
            {
                Clipboard.SetText(username);
            }
        }

        private void CopyEmailButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string email && !string.IsNullOrEmpty(email))
            {
                Clipboard.SetText(email);
            }
        }

        private void CopyEmail_Click(object sender, RoutedEventArgs e)
        {
            var currentGrid = GetCurrentGrid();
            if (currentGrid?.SelectedItem is UserProfileInfo user && !string.IsNullOrEmpty(user.Email))
            {
                Clipboard.SetText(user.Email);
            }
        }

        private DataGrid? GetCurrentGrid()
        {
            return UserTabs.SelectedIndex == 0 ? ActiveUsersGrid : ProfileUsersGrid;
        }

        private List<UserProfileInfo> GetCurrentUsers()
        {
            return UserTabs.SelectedIndex == 0 ? _activeUsers : _profileUsers;
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            var users = GetCurrentUsers();
            if (users.Count == 0) return;

            var isActiveTab = UserTabs.SelectedIndex == 0;
            var tabName = isActiveTab ? "Active User History" : "User Profile History";
            
            var sb = new StringBuilder();
            sb.AppendLine($"{tabName} for {_computerName}");
            sb.AppendLine(new string('-', 60));
            sb.AppendLine();

            foreach (var user in users)
            {
                sb.AppendLine($"{user.Username}");
                if (isActiveTab)
                    sb.AppendLine($"  Last Seen Logged In: {user.LastSeenLoggedInFormatted}");
                else
                    sb.AppendLine($"  Profile Last Scanned: {user.LastProfileScanFormatted}");
                if (!string.IsNullOrEmpty(user.Email))
                    sb.AppendLine($"  Email: {user.Email}");
                sb.AppendLine();
            }

            Clipboard.SetText(sb.ToString());
            
            // Show temporary feedback on button
            if (sender is Button button)
            {
                var originalContent = button.Content;
                button.Content = "Copied \u2713";
                button.IsEnabled = false;
                
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    button.Content = originalContent;
                    button.IsEnabled = true;
                };
                timer.Start();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
