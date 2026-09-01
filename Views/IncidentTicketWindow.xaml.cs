using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ActiveScanner.Models;
using ActiveScanner.Services;

namespace ActiveScanner.Views
{
    public partial class IncidentTicketWindow : Window
    {
        private readonly IncidentTicketService _ticketService;
        private readonly UserCacheService _userCacheService;
        private readonly SnowUserService2 _snowUserService;
        private readonly ObservableCollection<AdObjectInfo> _affectedItems;
        private readonly List<(string DisplayName, string PropertyName)> _visibleColumns;
        private IncidentTicketTemplate? _currentTemplate;
        private string? _resolvedAffectedEndUserSnowId;
        private string? _resolvedAffectedEndUserDisplay;
        private IReadOnlyList<IncidentTicketTemplate> _allTemplates = Array.Empty<IncidentTicketTemplate>();
        private string _templateSearchText = string.Empty;

        public IncidentTicketWindow(IEnumerable<AdObjectInfo> selectedItems, List<(string DisplayName, string PropertyName)> visibleColumns)
        {
            InitializeComponent();

            _ticketService = new IncidentTicketService();
            _userCacheService = new UserCacheService();
            _snowUserService = new SnowUserService2();
            _affectedItems = new ObservableCollection<AdObjectInfo>(selectedItems);
            _visibleColumns = visibleColumns;

            InitializeControls();
            UpdatePreview();

            // Dispose service when window closes
            Closed += (s, e) =>
            {
                _ticketService.Dispose();
                _userCacheService.Dispose();
                _snowUserService.Dispose();
            };
        }

        private async void InitializeControls()
        {
            // Load templates
            await _ticketService.LoadAsync();

            // Populate dropdown options FIRST (use combined built-in + custom lists)
            SubmittedByComboBox.ItemsSource = _ticketService.GetAllUsers();
            SubmittedByComboBox.SelectedIndex = 0;

            ContactMethodComboBox.ItemsSource = ServiceNowFields.ContactMethodOptions;
            ContactMethodComboBox.SelectedIndex = 0;

            CategoryComboBox.ItemsSource = ServiceNowFields.CategoryOptions;
            CategoryComboBox.SelectedIndex = 0;

            SubcategoryComboBox.ItemsSource = new List<DropdownOption> { new DropdownOption("", "") };
            SubcategoryComboBox.SelectedIndex = 0;

            ContactTypeComboBox.ItemsSource = ServiceNowFields.ContactTypeOptions;
            ContactTypeComboBox.SelectedIndex = 0;

            ImpactComboBox.ItemsSource = ServiceNowFields.ImpactOptions;
            ImpactComboBox.SelectedIndex = 0;

            UrgencyComboBox.ItemsSource = ServiceNowFields.UrgencyOptions;
            UrgencyComboBox.SelectedIndex = 0;

            // Set browser preference from settings
            var browserPref = App.Settings.Current.TicketBrowser;
            BrowserComboBox.SelectedIndex = browserPref == TicketBrowser.Chrome ? 1 : 0;

            // Populate affected items
            AffectedItemsList.ItemsSource = _affectedItems;
            UpdateAffectedItemsHeader();

            // Populate available variables from visible columns
            VariablesList.ItemsSource = _visibleColumns
                .Select(c => $"{{{{{c.PropertyName}}}}}");

            // Wire up text changed events for preview
            SubmittedByTextBox.TextChanged += (s, e) => UpdatePreview();
            BuildingNumberTextBox.TextChanged += (s, e) => UpdatePreview();
            RoomNumberTextBox.TextChanged += (s, e) => UpdatePreview();
            PhoneNumberTextBox.TextChanged += (s, e) => UpdatePreview();
            ShortDescriptionTextBox.TextChanged += (s, e) => UpdatePreview();
            DescriptionTextBox.TextChanged += (s, e) => UpdatePreview();
            AffectedSystemNameTextBox.TextChanged += (s, e) => UpdatePreview();
            AffectedServiceTextBox.TextChanged += (s, e) => UpdatePreview();
            WorkNotesTextBox.TextChanged += (s, e) => UpdatePreview();

            ContactMethodComboBox.SelectionChanged += (s, e) => UpdatePreview();
            SubcategoryComboBox.SelectionChanged += (s, e) => UpdatePreview();
            ContactTypeComboBox.SelectionChanged += (s, e) => UpdatePreview();
            ImpactComboBox.SelectionChanged += (s, e) => UpdatePreview();
            UrgencyComboBox.SelectionChanged += (s, e) => UpdatePreview();

            // Load templates AFTER all comboboxes are initialized
            LoadTemplates();

            // Update button text
            UpdateCreateButtonText();
        }

        private void LoadTemplates()
        {
            _allTemplates = _ticketService.Templates;
            FilterTemplates();
        }

        private void FilterTemplates()
        {
            if (string.IsNullOrWhiteSpace(_templateSearchText))
            {
                TemplateComboBox.ItemsSource = _allTemplates;
                ClearTemplateSearchButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                var filtered = _allTemplates
                    .Where(t => t.Name.Contains(_templateSearchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                TemplateComboBox.ItemsSource = filtered;
                ClearTemplateSearchButton.Visibility = Visibility.Visible;
            }
        }

        private void TemplateComboBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // Update search text with the new character
            var textBox = TemplateComboBox.Template.FindName("PART_EditableTextBox", TemplateComboBox) as TextBox;
            if (textBox != null)
            {
                _templateSearchText = textBox.Text + e.Text;
                FilterTemplates();
                TemplateComboBox.IsDropDownOpen = true;
            }
        }

        private void TemplateComboBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Back || e.Key == System.Windows.Input.Key.Delete)
            {
                var textBox = TemplateComboBox.Template.FindName("PART_EditableTextBox", TemplateComboBox) as TextBox;
                if (textBox != null && textBox.Text.Length > 0)
                {
                    // Schedule update after the key is processed
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _templateSearchText = textBox.Text;
                        FilterTemplates();
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
            }
        }

        private void ClearTemplateSearch_Click(object sender, RoutedEventArgs e)
        {
            _templateSearchText = string.Empty;
            var textBox = TemplateComboBox.Template.FindName("PART_EditableTextBox", TemplateComboBox) as TextBox;
            if (textBox != null)
            {
                textBox.Text = string.Empty;
            }
            TemplateComboBox.SelectedItem = null;
            FilterTemplates();
        }

        private void UpdateAffectedItemsHeader()
        {
            var count = _affectedItems.Count;
            AffectedItemsHeader.Text = count == 1 ? "1 item selected" : $"{count} items selected";
            UpdateCreateButtonText();
        }

        private void UpdateCreateButtonText()
        {
            var count = _affectedItems.Count;
            CreateTicketsButtonText.Text = count == 1 ? "Create Ticket" : $"Create {count} Tickets";
            CreateTicketsButton.IsEnabled = count > 0;
        }

        private IncidentTicketTemplate BuildTemplateFromForm()
        {
            // Always create a new object to avoid modifying the selected template
            var template = new IncidentTicketTemplate();
            
            // Preserve ID and metadata from current template if editing
            if (_currentTemplate != null)
            {
                template.Id = _currentTemplate.Id;
                template.Name = _currentTemplate.Name;
                template.CreatedAt = _currentTemplate.CreatedAt;
            }

            template.SubmittedBy = SubmittedByTextBox.Text;
            template.AffectedUserBuildingNumber = BuildingNumberTextBox.Text;
            template.AffectedUserRoomNumber = RoomNumberTextBox.Text;
            template.BestContactMethod = (ContactMethodComboBox.SelectedItem as DropdownOption)?.UrlValue ?? "";
            template.AffectedUserPhoneNumber = PhoneNumberTextBox.Text;
            template.Category = (CategoryComboBox.SelectedItem as DropdownOption)?.UrlValue ?? "";
            template.Subcategory = (SubcategoryComboBox.SelectedItem as DropdownOption)?.UrlValue ?? "";
            template.AffectedService = AffectedServiceTextBox.Text;
            template.ShortDescription = ShortDescriptionTextBox.Text;
            template.ContactType = (ContactTypeComboBox.SelectedItem as DropdownOption)?.UrlValue ?? "";
            template.Impact = (ImpactComboBox.SelectedItem as DropdownOption)?.UrlValue ?? "";
            template.Urgency = (UrgencyComboBox.SelectedItem as DropdownOption)?.UrlValue ?? "";
            template.Description = DescriptionTextBox.Text;
            template.AffectedSystemName = AffectedSystemNameTextBox.Text;
            template.AffectedEndUser = AffectedEndUserTextBox.Text;
            template.WorkNotes = WorkNotesTextBox.Text;

            // Persist resolved SNOW value so template loads skip lookups
            template.ResolvedAffectedEndUserSysId = _resolvedAffectedEndUserSnowId;
            template.ResolvedAffectedEndUserDisplay = _resolvedAffectedEndUserDisplay;

            return template;
        }

        private void LoadTemplateToForm(IncidentTicketTemplate template)
        {
            SubmittedByTextBox.Text = template.SubmittedBy;
            // Try to select matching team member in dropdown (won't fire SelectionChanged because text is already set)
            SelectComboBoxByUrlValue(SubmittedByComboBox, template.SubmittedBy);
            
            BuildingNumberTextBox.Text = template.AffectedUserBuildingNumber;
            RoomNumberTextBox.Text = template.AffectedUserRoomNumber;
            
            SelectComboBoxByUrlValue(ContactMethodComboBox, template.BestContactMethod);
            
            PhoneNumberTextBox.Text = template.AffectedUserPhoneNumber;
            
            SelectComboBoxByUrlValue(CategoryComboBox, template.Category);
            // Category change will update subcategory options
            SelectComboBoxByUrlValue(SubcategoryComboBox, template.Subcategory);
            
            AffectedServiceTextBox.Text = template.AffectedService;
            ShortDescriptionTextBox.Text = template.ShortDescription;
            
            SelectComboBoxByUrlValue(ContactTypeComboBox, template.ContactType);
            SelectComboBoxByUrlValue(ImpactComboBox, template.Impact);
            SelectComboBoxByUrlValue(UrgencyComboBox, template.Urgency);
            
            DescriptionTextBox.Text = template.Description;
            AffectedSystemNameTextBox.Text = template.AffectedSystemName;
            AffectedEndUserTextBox.Text = template.AffectedEndUser;
            WorkNotesTextBox.Text = template.WorkNotes;
            
            // Restore cached resolved value or trigger lookup
            if (!string.IsNullOrWhiteSpace(template.ResolvedAffectedEndUserSysId) && !string.IsNullOrWhiteSpace(template.AffectedEndUser))
            {
                _resolvedAffectedEndUserSnowId = template.ResolvedAffectedEndUserSysId;
                _resolvedAffectedEndUserDisplay = template.ResolvedAffectedEndUserDisplay;
                AffectedEndUserLoadingIcon.Visibility = Visibility.Collapsed;
                AffectedEndUserErrorIcon.Visibility = Visibility.Collapsed;
                AffectedEndUserCheckIcon.Visibility = Visibility.Visible;
                AffectedEndUserStatusText.Text = $"✓ {template.ResolvedAffectedEndUserDisplay ?? "Found"}";
                AffectedEndUserStatusText.Foreground = new SolidColorBrush(Colors.Green);
            }
            else if (!string.IsNullOrWhiteSpace(template.AffectedEndUser))
            {
                _ = ResolveAffectedEndUserAsync(template.AffectedEndUser);
            }
        }

        private void SelectComboBoxByUrlValue(ComboBox comboBox, string urlValue)
        {
            if (comboBox.ItemsSource is IEnumerable<DropdownOption> options)
            {
                var match = options.FirstOrDefault(o => o.UrlValue == urlValue);
                if (match != null)
                {
                    comboBox.SelectedItem = match;
                }
            }
        }

        private void UpdatePreview()
        {
            var template = BuildTemplateFromForm();
            var sampleObject = _affectedItems.FirstOrDefault();
            var url = _ticketService.GenerateUrl(template, sampleObject, _resolvedAffectedEndUserSnowId);

            UrlPreviewTextBox.Text = url;

            // Check URL length and show warning
            if (IncidentTicketService.IsUrlTooLong(url))
            {
                UrlLengthWarning.Text = $"⚠ URL is {url.Length} chars (max recommended: {IncidentTicketService.MaxRecommendedUrlLength}). May be truncated.";
                UrlLengthWarning.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54)); // Red
                UrlLengthWarning.Visibility = Visibility.Visible;
            }
            else if (IncidentTicketService.IsUrlLengthWarning(url))
            {
                UrlLengthWarning.Text = $"⚠ URL is {url.Length} chars (approaching limit)";
                UrlLengthWarning.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0)); // Orange
                UrlLengthWarning.Visibility = Visibility.Visible;
            }
            else
            {
                UrlLengthWarning.Visibility = Visibility.Collapsed;
            }
        }

        private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedCategory = CategoryComboBox.SelectedItem as DropdownOption;
            if (selectedCategory == null) return;

            // Update subcategory options based on category
            var subcategoryOptions = ServiceNowFields.GetSubcategoryOptions(selectedCategory.UrlValue);
            SubcategoryComboBox.ItemsSource = subcategoryOptions;
            SubcategoryComboBox.SelectedIndex = subcategoryOptions.Count > 0 ? 0 : -1;

            // Show/hide Affected Service text input
            AffectedServicePanel.Visibility = 
                ServiceNowFields.CategoryUsesTextSubcategory(selectedCategory.UrlValue) 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;

            UpdatePreview();
        }

        private void SubmittedByComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = SubmittedByComboBox.SelectedItem as DropdownOption;
            if (selected != null && !string.IsNullOrEmpty(selected.UrlValue))
            {
                SubmittedByTextBox.Text = selected.UrlValue;
            }
            UpdatePreview();
        }

        private void SubmittedByHelp_Click(object sender, RoutedEventArgs e)
        {
            var helpMessage = @"How to find a user's sys_id in ServiceNow:

1. In ServiceNow, type the user's email in the 'Submitted by' field
2. Click the search/magnifying glass button
3. When the user appears in the popup, right-click their name
4. Select 'Inspect' or 'Inspect Element'
5. In the developer tools, look for the element's data attributes
6. Find the 'sys_id' value - it's a 32-character string like:
   459cbbbcdbe9d700aec6740d0f9619c7

This sys_id is required because ServiceNow's URL parameter
needs the internal ID, not the email address.

Use the dropdown above to quickly select a team member,
or paste a sys_id directly into the text field.";

            MessageBox.Show(helpMessage, "Finding User sys_id", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void SaveSubmittedBy_Click(object sender, RoutedEventArgs e)
        {
            var sysId = SubmittedByTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(sysId))
            {
                MessageBox.Show("Enter a sys_id first.", "Save User", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check if it looks like a sys_id (32 chars hex)
            if (sysId.Length != 32 || !System.Text.RegularExpressions.Regex.IsMatch(sysId, "^[a-fA-F0-9]+$"))
            {
                var result = MessageBox.Show(
                    "The value doesn't look like a valid sys_id (32-character hex string). Save anyway?",
                    "Save User", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
            }

            var dialog = new InputDialog("Enter Display Name", "Enter a name for this team member:", "");
            dialog.Owner = this;
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.InputText)) return;
            var displayName = dialog.InputText;

            await _ticketService.AddCustomUserAsync(displayName, sysId);
            SubmittedByComboBox.ItemsSource = _ticketService.GetAllUsers();
            
            // Select the newly added user
            var newItem = _ticketService.GetAllUsers().FirstOrDefault(u => u.UrlValue == sysId);
            if (newItem != null)
            {
                SubmittedByComboBox.SelectedItem = newItem;
            }

            MessageBox.Show($"Saved '{displayName}' to the team member list.", "User Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedTemplate = TemplateComboBox.SelectedItem as IncidentTicketTemplate;
            if (selectedTemplate != null)
            {
                _currentTemplate = selectedTemplate;
                LoadTemplateToForm(selectedTemplate);
                DeleteTemplateButton.IsEnabled = true;
            }
            else
            {
                DeleteTemplateButton.IsEnabled = false;
            }
        }

        private async void SaveTemplate_Click(object sender, RoutedEventArgs e)
        {
            // Check that resolver fields are either blank, placeholders, or validated
            if (!string.IsNullOrWhiteSpace(AffectedEndUserTextBox.Text) && !AffectedEndUserTextBox.Text.Contains("{{") && _resolvedAffectedEndUserSnowId == null)
            {
                MessageBox.Show("Please validate the Affected End User field before saving (click away from the field to trigger lookup).",
                    "Unresolved Field", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Prompt for template name
            var dialog = new InputDialog("Save Template", "Enter a name for this template:", 
                _currentTemplate?.Name ?? "");
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            {
                var template = BuildTemplateFromForm();
                template.Name = dialog.InputText;

                // If saving over existing template, keep the ID
                if (_currentTemplate != null && _currentTemplate.Name == dialog.InputText)
                {
                    template.Id = _currentTemplate.Id;
                    template.CreatedAt = _currentTemplate.CreatedAt;
                }

                await _ticketService.SaveTemplateAsync(template);
                _currentTemplate = template;

                // Refresh template list
                LoadTemplates();
                TemplateComboBox.SelectedItem = _ticketService.Templates.FirstOrDefault(t => t.Id == template.Id);

                MessageBox.Show("Template saved successfully.", "Success", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void DeleteTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTemplate == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete the template '{_currentTemplate.Name}'?",
                "Delete Template",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                await _ticketService.DeleteTemplateAsync(_currentTemplate.Id);
                _currentTemplate = null;


                // Refresh template list
                LoadTemplates();
                TemplateComboBox.SelectedIndex = -1;
                DeleteTemplateButton.IsEnabled = false;
            }
        }

        private void InsertVariable_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string targetTextBoxName)
            {
                var targetTextBox = FindName(targetTextBoxName) as TextBox;
                if (targetTextBox == null) return;

                // Show a context menu with available columns
                var contextMenu = new ContextMenu();
                foreach (var column in _visibleColumns)
                {
                    var menuItem = new MenuItem
                    {
                        Header = column.DisplayName,
                        Tag = $"{{{{{column.PropertyName}}}}}"
                    };
                    menuItem.Click += (s, args) =>
                    {
                        if (s is MenuItem mi && mi.Tag is string variable)
                        {
                            var caretIndex = targetTextBox.CaretIndex;
                            targetTextBox.Text = targetTextBox.Text.Insert(caretIndex, variable);
                            targetTextBox.CaretIndex = caretIndex + variable.Length;
                            targetTextBox.Focus();
                        }
                    };
                    contextMenu.Items.Add(menuItem);
                }

                contextMenu.PlacementTarget = button;
                contextMenu.IsOpen = true;
            }
        }

        private void RemoveAffectedItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is AdObjectInfo item)
            {
                _affectedItems.Remove(item);
                UpdateAffectedItemsHeader();
                UpdatePreview();
            }
        }

        private void CopyPreviewUrl_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(UrlPreviewTextBox.Text))
            {
                Clipboard.SetText(UrlPreviewTextBox.Text);
            }
        }

        private void CopyAllUrls_Click(object sender, RoutedEventArgs e)
        {
            var template = BuildTemplateFromForm();
            var urls = _ticketService.GenerateUrls(template, _affectedItems, _resolvedAffectedEndUserSnowId);
            var allUrls = string.Join(Environment.NewLine, urls.Select(u => u.Url));
            Clipboard.SetText(allUrls);

            MessageBox.Show($"Copied {urls.Count} URL(s) to clipboard.", "URLs Copied", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void CreateTickets_Click(object sender, RoutedEventArgs e)
        {
            if (_affectedItems.Count == 0) return;

            // Save browser preference
            var browserSelection = BrowserComboBox.SelectedItem as ComboBoxItem;
            var browser = browserSelection?.Tag?.ToString() == "Chrome" ? TicketBrowser.Chrome : TicketBrowser.Edge;
            App.Settings.Current.TicketBrowser = browser;
            App.Settings.Save();

            var template = BuildTemplateFromForm();
            var urls = _ticketService.GenerateUrls(template, _affectedItems, _resolvedAffectedEndUserSnowId);

            // Check for long URLs
            var longUrls = urls.Where(u => IncidentTicketService.IsUrlTooLong(u.Url)).ToList();
            if (longUrls.Count > 0)
            {
                var result = MessageBox.Show(
                    $"{longUrls.Count} URL(s) exceed the recommended length and may be truncated by the browser. Continue anyway?",
                    "URL Length Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;
            }

            // Confirm opening multiple tabs
            if (urls.Count > 5)
            {
                var result = MessageBox.Show(
                    $"This will open {urls.Count} browser tabs. Continue?",
                    "Confirm",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;
            }

            // Standard URL-only mode
            await _ticketService.OpenUrlsInBrowser(urls.Select(u => u.Url), browser);

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Get column value from AD object for placeholder replacement
        /// </summary>
        private string? GetColumnValue(AdObjectInfo adObject, string columnName)
        {
            return columnName.ToLowerInvariant() switch
            {
                "name" => adObject.Name,
                "description" => adObject.Description,
                "ipaddress" => adObject.IpAddress,
                "operatingsystem" => adObject.OperatingSystem,
                "operatingsystemversion" => adObject.OperatingSystemVersion,
                "location" => adObject.Location,
                "dnshostname" => adObject.DnsHostName,
                "lastuser" => adObject.LastUser,
                "samaccountname" => adObject.SamAccountName,
                "userprincipalname" => adObject.UserPrincipalName,
                "displayname" => adObject.DisplayName,
                "distinguishedname" => adObject.DistinguishedName,
                "managedby" => adObject.ManagedBy,
                _ => null
            };
        }

        private void AffectedEndUserTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var input = AffectedEndUserTextBox.Text;
            if (!string.IsNullOrWhiteSpace(input))
            {
                _ = ResolveAffectedEndUserAsync(input);
            }
            else
            {
                // Clear feedback when empty
                AffectedEndUserCheckIcon.Visibility = Visibility.Collapsed;
                AffectedEndUserErrorIcon.Visibility = Visibility.Collapsed;
                AffectedEndUserLoadingIcon.Visibility = Visibility.Collapsed;
                AffectedEndUserStatusText.Text = string.Empty;
                _resolvedAffectedEndUserSnowId = null;
                _resolvedAffectedEndUserDisplay = null;
            }
        }

        private async Task ResolveAffectedEndUserAsync(string input)
        {
            // Show loading state
            AffectedEndUserCheckIcon.Visibility = Visibility.Collapsed;
            AffectedEndUserErrorIcon.Visibility = Visibility.Collapsed;
            AffectedEndUserLoadingIcon.Visibility = Visibility.Visible;
            AffectedEndUserStatusText.Text = "Resolving...";
            _resolvedAffectedEndUserSnowId = null;
            _resolvedAffectedEndUserDisplay = null;

            try
            {
                // Check if it's a {{SnowId}} variable placeholder
                if (input.Equals("{{SnowId}}", StringComparison.OrdinalIgnoreCase))
                {
                    // Variable - resolved at URL generation time per-item
                    _resolvedAffectedEndUserSnowId = "{{SnowId}}"; // Keep as placeholder
                    _resolvedAffectedEndUserDisplay = "Variable (from selected item)";
                    AffectedEndUserLoadingIcon.Visibility = Visibility.Collapsed;
                    AffectedEndUserCheckIcon.Visibility = Visibility.Visible;
                    AffectedEndUserStatusText.Text = "✓ Variable (from selected item)";
                    AffectedEndUserStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    return;
                }

                // Check if it's a direct SNOW sys_id (32 hex chars, no @)
                if (!input.Contains('@') && input.Length == 32 && 
                    System.Text.RegularExpressions.Regex.IsMatch(input, "^[0-9a-fA-F]{32}$"))
                {
                    // Direct SNOW ID - use as-is
                    _resolvedAffectedEndUserSnowId = input;
                    _resolvedAffectedEndUserDisplay = "Direct SNOW ID";
                    AffectedEndUserLoadingIcon.Visibility = Visibility.Collapsed;
                    AffectedEndUserCheckIcon.Visibility = Visibility.Visible;
                    AffectedEndUserStatusText.Text = "✓ Direct SNOW ID";
                    AffectedEndUserStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    return;
                }

                // Try local cache first
                var (cachedSnowId, cachedDisplayName) = _userCacheService.GetSnowIdByEmailOrUsername(input);
                if (!string.IsNullOrEmpty(cachedSnowId))
                {
                    _resolvedAffectedEndUserSnowId = cachedSnowId;
                    _resolvedAffectedEndUserDisplay = cachedDisplayName;
                    AffectedEndUserLoadingIcon.Visibility = Visibility.Collapsed;
                    AffectedEndUserCheckIcon.Visibility = Visibility.Visible;
                    AffectedEndUserStatusText.Text = $"✓ {cachedDisplayName}";
                    AffectedEndUserStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    return;
                }

                // Fallback to SNOW API if authenticated
                if (_snowUserService.IsAuthenticated)
                {
                    var (apiSnowId, apiDisplayName) = await _snowUserService.GetSysIdByEmailOrUsernameAsync(input);
                    if (!string.IsNullOrEmpty(apiSnowId))
                    {
                        _resolvedAffectedEndUserSnowId = apiSnowId;
                        _resolvedAffectedEndUserDisplay = apiDisplayName;
                        AffectedEndUserLoadingIcon.Visibility = Visibility.Collapsed;
                        AffectedEndUserCheckIcon.Visibility = Visibility.Visible;
                        AffectedEndUserStatusText.Text = $"✓ {apiDisplayName}";
                        AffectedEndUserStatusText.Foreground = new SolidColorBrush(Colors.Green);
                        return;
                    }
                }

                // Not found
                AffectedEndUserLoadingIcon.Visibility = Visibility.Collapsed;
                AffectedEndUserErrorIcon.Visibility = Visibility.Visible;
                AffectedEndUserStatusText.Text = "User not found";
                AffectedEndUserStatusText.Foreground = new SolidColorBrush(Colors.Red);
            }
            catch (Exception ex)
            {
                AffectedEndUserLoadingIcon.Visibility = Visibility.Collapsed;
                AffectedEndUserErrorIcon.Visibility = Visibility.Visible;
                AffectedEndUserStatusText.Text = $"Error: {ex.Message}";
                AffectedEndUserStatusText.Foreground = new SolidColorBrush(Colors.Red);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    /// <summary>
    /// Simple input dialog for getting text input from user
    /// </summary>
    public partial class InputDialog : Window
    {
        public string InputText { get; private set; } = string.Empty;

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            Title = title;
            Width = 400;
            Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = (System.Windows.Media.Brush)Application.Current.Resources["MaterialDesignPaper"];

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var promptLabel = new TextBlock
            {
                Text = prompt,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["MaterialDesignBody"]
            };
            Grid.SetRow(promptLabel, 0);
            grid.Children.Add(promptLabel);

            var textBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, 16),
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["MaterialDesignBody"],
                Background = (System.Windows.Media.Brush)Application.Current.Resources["MaterialDesignCardBackground"],
                CaretBrush = (System.Windows.Media.Brush)Application.Current.Resources["MaterialDesignBody"]
            };
            Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(buttonPanel, 2);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(16, 8, 16, 8)
            };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };
            buttonPanel.Children.Add(cancelButton);

            var okButton = new Button
            {
                Content = "OK",
                Padding = new Thickness(16, 8, 16, 8)
            };
            okButton.Click += (s, e) =>
            {
                InputText = textBox.Text;
                DialogResult = true;
                Close();
            };
            buttonPanel.Children.Add(okButton);

            grid.Children.Add(buttonPanel);
            Content = grid;

            Loaded += (s, e) =>
            {
                textBox.Focus();
                textBox.SelectAll();
            };
        }
    }
}
