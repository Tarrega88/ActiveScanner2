using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ActiveScanner.Models;

namespace ActiveScanner.Views
{
    /// <summary>
    /// View model wrapper for UserMachineEntry to add display formatting
    /// </summary>
    public class ComputerHistoryItem
    {
        public string ComputerName { get; set; } = string.Empty;
        public DateTime LastSeen { get; set; }
        public bool WasLoggedIn { get; set; }

        public string LastSeenFormatted
        {
            get
            {
                var localTime = LastSeen.Kind == DateTimeKind.Utc
                    ? LastSeen.ToLocalTime()
                    : LastSeen;
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

        public static ComputerHistoryItem FromEntry(UserMachineEntry entry)
        {
            return new ComputerHistoryItem
            {
                ComputerName = entry.ComputerName,
                LastSeen = entry.LastSeen,
                WasLoggedIn = entry.WasLoggedIn
            };
        }
    }

    /// <summary>
    /// Dialog for viewing computer history for a user
    /// </summary>
    public partial class ComputerHistoryDialog : Window
    {
        private List<ComputerHistoryItem> _computers = new();
        private string _userName = string.Empty;

        public ComputerHistoryDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Load computer history for display
        /// </summary>
        public void LoadHistory(string userName, string? displayName, List<UserMachineEntry>? machines)
        {
            _userName = userName;
            // Only show computers where we've actually seen the user logged in
            _computers = (machines ?? new List<UserMachineEntry>())
                .Where(m => m.LastSeen > DateTime.MinValue)
                .OrderByDescending(m => m.LastSeen)
                .Select(ComputerHistoryItem.FromEntry)
                .ToList();

            UserNameText.Text = !string.IsNullOrEmpty(displayName) ? displayName : userName;
            ComputerCountText.Text = $"{_computers.Count} computer(s)";

            ComputersGrid.ItemsSource = _computers;
        }

        /// <summary>
        /// Load computer history from an AdObjectInfo user
        /// </summary>
        public void LoadHistory(AdObjectInfo user)
        {
            LoadHistory(
                user.SamAccountName ?? user.Name,
                user.DisplayName,
                user.ComputerHistory);
        }

        private void CopyComputer_Click(object sender, RoutedEventArgs e)
        {
            if (ComputersGrid.SelectedItem is ComputerHistoryItem item)
            {
                Clipboard.SetText(item.ComputerName);
            }
        }

        private void CopyComputerButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string computerName)
            {
                Clipboard.SetText(computerName);
            }
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            if (_computers.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine($"Computer History for {_userName}");
            sb.AppendLine(new string('-', 60));
            sb.AppendLine();

            foreach (var computer in _computers)
            {
                sb.AppendLine($"{computer.ComputerName}");
                sb.AppendLine($"  Last Seen: {computer.LastSeenFormatted}");
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
