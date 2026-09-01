using System.Windows;
using ActiveScanner.Models;

namespace ActiveScanner.Views
{
    public partial class ColumnSettingsDialog : Window
    {
        public ColumnSettings Result { get; private set; }

        public ColumnSettingsDialog(ColumnSettings current)
        {
            InitializeComponent();
            Result = current;
            LoadSettings(current);
        }

        private void LoadSettings(ColumnSettings settings)
        {
            ChkDnsHostName.IsChecked = settings.DnsHostName;
            ChkDescription.IsChecked = settings.Description;
            ChkIsEnabled.IsChecked = settings.IsEnabled;
            ChkLastLogon.IsChecked = settings.LastLogon;
            ChkWhenCreated.IsChecked = settings.WhenCreated;
            ChkWhenChanged.IsChecked = settings.WhenChanged;
            ChkLocation.IsChecked = settings.Location;
            ChkManagedBy.IsChecked = settings.ManagedBy;
            ChkDistinguishedName.IsChecked = settings.DistinguishedName;
            ChkSourcePath.IsChecked = settings.SourcePath;
        }

        private ColumnSettings GetSettings()
        {
            return new ColumnSettings
            {
                Name = true, // Always true
                DnsHostName = ChkDnsHostName.IsChecked ?? false,
                Description = ChkDescription.IsChecked ?? false,
                IsEnabled = ChkIsEnabled.IsChecked ?? false,
                LastLogon = ChkLastLogon.IsChecked ?? false,
                WhenCreated = ChkWhenCreated.IsChecked ?? false,
                WhenChanged = ChkWhenChanged.IsChecked ?? false,
                Location = ChkLocation.IsChecked ?? false,
                ManagedBy = ChkManagedBy.IsChecked ?? false,
                DistinguishedName = ChkDistinguishedName.IsChecked ?? false,
                SourcePath = ChkSourcePath.IsChecked ?? false
            };
        }

        private void ResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            LoadSettings(new ColumnSettings());
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            Result = GetSettings();
            DialogResult = true;
            Close();
        }
    }
}
