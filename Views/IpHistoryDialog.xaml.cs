using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ActiveScanner.Models;

namespace ActiveScanner.Views
{
    /// <summary>
    /// View model for IP history entries
    /// </summary>
    public class IpHistoryItem
    {
        public string IpAddress { get; set; } = string.Empty;
        public DateTime LastSeen { get; set; }

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
                if (age.TotalDays < 7)
                    return $"{(int)age.TotalDays} day{((int)age.TotalDays == 1 ? "" : "s")} ago";
                if (age.TotalDays < 30)
                    return $"{(int)(age.TotalDays / 7)} week{((int)(age.TotalDays / 7) == 1 ? "" : "s")} ago";
                if (age.TotalDays < 365)
                    return $"{(int)(age.TotalDays / 30)} month{((int)(age.TotalDays / 30) == 1 ? "" : "s")} ago";

                return localTime.ToString("MMM d, yyyy");
            }
        }
    }

    /// <summary>
    /// Dialog for viewing IP address history for a computer
    /// </summary>
    public partial class IpHistoryDialog : Window
    {
        private List<IpHistoryItem> _ipHistory = new();
        private string _computerName = string.Empty;

        public IpHistoryDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Load IP history for display
        /// </summary>
        public void LoadHistory(string computerName, Dictionary<string, DateTime>? knownIpAddresses)
        {
            _computerName = computerName;
            _ipHistory = (knownIpAddresses ?? new Dictionary<string, DateTime>())
                .Select(kvp => new IpHistoryItem
                {
                    IpAddress = kvp.Key,
                    LastSeen = kvp.Value
                })
                .OrderByDescending(ip => ip.LastSeen)
                .ToList();

            ComputerNameText.Text = computerName;
            IpCountText.Text = $"{_ipHistory.Count} known IP address{(_ipHistory.Count == 1 ? "" : "es")}";

            IpHistoryGrid.ItemsSource = _ipHistory;
        }

        /// <summary>
        /// Load IP history from an AdObjectInfo
        /// </summary>
        public void LoadHistory(AdObjectInfo adObject)
        {
            LoadHistory(adObject.Name, adObject.KnownIpAddresses);
        }

        private void CopyIp_Click(object sender, RoutedEventArgs e)
        {
            if (IpHistoryGrid.SelectedItem is IpHistoryItem item)
            {
                Clipboard.SetText(item.IpAddress);
            }
        }

        private void CopyIpButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string ipAddress)
            {
                Clipboard.SetText(ipAddress);
            }
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            if (_ipHistory.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine($"IP History for {_computerName}");
            sb.AppendLine(new string('-', 50));
            sb.AppendLine();

            foreach (var ip in _ipHistory)
            {
                sb.AppendLine($"{ip.IpAddress}");
                sb.AppendLine($"  Last Seen: {ip.LastSeenFormatted}");
                sb.AppendLine();
            }

            Clipboard.SetText(sb.ToString());
            MessageBox.Show("IP history copied to clipboard.", "Copied",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
