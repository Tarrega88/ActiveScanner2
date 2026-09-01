using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ActiveScanner.Models;
using ActiveScanner.Services;

namespace ActiveScanner.Views
{
    public partial class SctaskTicketWindow : Window
    {
        private readonly SctaskTicketService _ticketService;
        private readonly UserCacheService _userCacheService;
        private readonly SnowUserService2 _snowUserService;
        private readonly ObservableCollection<AdObjectInfo> _affectedItems;
        private readonly List<(string DisplayName, string PropertyName)> _visibleColumns;
        private SctaskTicketTemplate? _currentTemplate;
        private IReadOnlyList<SctaskTicketTemplate> _allTemplates = Array.Empty<SctaskTicketTemplate>();
        private string _templateSearchText = string.Empty;

        // Resolved values for SNOW lookups
        private string? _resolvedRitmSysId;
        private string? _resolvedRitmDisplay;
        private string? _resolvedRequestedForSnowId;
        private string? _resolvedRequestedForDisplay;
        private string? _resolvedAssignmentGroupSysId;
        private string? _resolvedAssignmentGroupDisplay;
        private string? _resolvedAssignedToSnowId;
        private string? _resolvedAssignedToDisplay;

        // Validation state
        private bool _isRitmValid;
        private bool _isAssignmentGroupValid;

        public SctaskTicketWindow(IEnumerable<AdObjectInfo> selectedItems, List<(string DisplayName, string PropertyName)> visibleColumns)
        {
            InitializeComponent();

            _ticketService = new SctaskTicketService();
            _userCacheService = new UserCacheService();
            _snowUserService = new SnowUserService2();
            _affectedItems = new ObservableCollection<AdObjectInfo>(selectedItems);
            _visibleColumns = visibleColumns;

            InitializeControls();
            UpdatePreview();
            UpdateValidationState();

            // Dispose services when window closes
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

            // Populate dropdown options
            AssignmentGroupComboBox.ItemsSource = _ticketService.GetAllGroups();
            AssignmentGroupComboBox.SelectedIndex = 0;

            PriorityComboBox.ItemsSource = SctaskFields.PriorityOptions;
            PriorityComboBox.SelectedIndex = 0;

            StateComboBox.ItemsSource = SctaskFields.StateOptions;
            StateComboBox.SelectedIndex = 0;

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
            RitmNumberTextBox.TextChanged += (s, e) => UpdatePreview();
            RequestedForTextBox.TextChanged += (s, e) => UpdatePreview();
            AssignmentGroupTextBox.TextChanged += (s, e) => UpdatePreview();
            AssignedToTextBox.TextChanged += (s, e) => UpdatePreview();
            ShortDescriptionTextBox.TextChanged += (s, e) => UpdatePreview();
            DescriptionTextBox.TextChanged += (s, e) => UpdatePreview();
            WorkNotesTextBox.TextChanged += (s, e) => UpdatePreview();

            PriorityComboBox.SelectionChanged += (s, e) => UpdatePreview();
            StateComboBox.SelectionChanged += (s, e) => UpdatePreview();

            // Load templates AFTER all comboboxes are initialized
            LoadTemplates();

            // Update button text
            UpdateCreateButtonText();
        }

        private void LoadTemplates()
        {
            _allTemplates = _ticketService.Templates;
            TemplateComboBox.ItemsSource = _allTemplates;
        }

        private void UpdateAffectedItemsHeader()
        {
            AffectedItemsHeader.Text = $"{_affectedItems.Count} item{(_affectedItems.Count == 1 ? "" : "s")} selected";
        }

        private void UpdateCreateButtonText()
        {
            var count = _affectedItems.Count;
            CreateTicketsButtonText.Text = count <= 1 ? "Create Ticket" : $"Create {count} Tickets";
        }

        #region Template Management

        private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TemplateComboBox.SelectedItem is SctaskTicketTemplate template)
            {
                _currentTemplate = template;
                LoadTemplateToForm(template);
                DeleteTemplateButton.IsEnabled = true;
            }
        }

        private void TemplateComboBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Show clear button when typing
            ClearTemplateSearchButton.Visibility = Visibility.Visible;
        }

        private void TemplateComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                ClearTemplateSearch_Click(sender, e);
            }
            else if (e.Key == Key.Back || e.Key == Key.Delete)
            {
                // Update search after a small delay
                Dispatcher.InvokeAsync(() =>
                {
                    var text = TemplateComboBox.Text;
                    FilterTemplates(text);
                });
            }
        }

        private void ClearTemplateSearch_Click(object sender, RoutedEventArgs e)
        {
            TemplateComboBox.Text = string.Empty;
            TemplateComboBox.ItemsSource = _allTemplates;
            ClearTemplateSearchButton.Visibility = Visibility.Collapsed;
            _templateSearchText = string.Empty;
        }

        private void FilterTemplates(string searchText)
        {
            _templateSearchText = searchText;
            if (string.IsNullOrWhiteSpace(searchText))
            {
                TemplateComboBox.ItemsSource = _allTemplates;
                ClearTemplateSearchButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                var filtered = _allTemplates
                    .Where(t => t.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                TemplateComboBox.ItemsSource = filtered;
                ClearTemplateSearchButton.Visibility = Visibility.Visible;
            }
        }

        private async void SaveTemplate_Click(object sender, RoutedEventArgs e)
        {
            // Check that resolver fields are either blank, placeholders, or validated
            var unresolved = new List<string>();
            if (!string.IsNullOrWhiteSpace(RitmNumberTextBox.Text) && !RitmNumberTextBox.Text.Contains("{{") && !_isRitmValid)
                unresolved.Add("RITM Number");
            if (!string.IsNullOrWhiteSpace(AssignmentGroupTextBox.Text) && !AssignmentGroupTextBox.Text.Contains("{{") && !_isAssignmentGroupValid)
                unresolved.Add("Assignment Group");
            if (!string.IsNullOrWhiteSpace(RequestedForTextBox.Text) && !RequestedForTextBox.Text.Contains("{{") && _resolvedRequestedForSnowId == null)
                unresolved.Add("Requested For");
            if (!string.IsNullOrWhiteSpace(AssignedToTextBox.Text) && !AssignedToTextBox.Text.Contains("{{") && _resolvedAssignedToSnowId == null)
                unresolved.Add("Assigned To");

            if (unresolved.Count > 0)
            {
                MessageBox.Show($"Please validate these fields before saving (click away from the field to trigger lookup):\n\n• {string.Join("\n• ", unresolved)}",
                    "Unresolved Fields", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Prompt for template name
            var dialog = new InputDialog("Save Template", "Enter a name for this template:", 
                _currentTemplate?.Name ?? "");
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            {
                var template = GetTemplateFromForm();
                template.Name = dialog.InputText;

                // If updating existing template, preserve the ID
                if (_currentTemplate != null && _currentTemplate.Name == dialog.InputText)
                {
                    template.Id = _currentTemplate.Id;
                    template.CreatedAt = _currentTemplate.CreatedAt;
                }

                await _ticketService.SaveTemplateAsync(template);
                _currentTemplate = template;

                // Refresh templates list
                LoadTemplates();
                TemplateComboBox.SelectedItem = _allTemplates.FirstOrDefault(t => t.Id == template.Id);

                MessageBox.Show("Template saved successfully.", "Success", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void DeleteTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTemplate == null)
                return;

            var result = MessageBox.Show(
                $"Delete template '{_currentTemplate.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            await _ticketService.DeleteTemplateAsync(_currentTemplate.Id);
            _currentTemplate = null;
            DeleteTemplateButton.IsEnabled = false;

            // Refresh templates list
            LoadTemplates();
            TemplateComboBox.SelectedIndex = -1;
        }

        private SctaskTicketTemplate GetTemplateFromForm()
        {
            var template = new SctaskTicketTemplate();

            if (_currentTemplate != null)
            {
                template.Id = _currentTemplate.Id;
                template.Name = _currentTemplate.Name;
                template.CreatedAt = _currentTemplate.CreatedAt;
            }

            template.RequestItem = RitmNumberTextBox.Text;
            template.RequestedFor = RequestedForTextBox.Text;
            template.AssignmentGroup = AssignmentGroupTextBox.Text;
            template.AssignmentGroupDropdownValue = (AssignmentGroupComboBox.SelectedItem as DropdownOption)?.UrlValue ?? "";
            template.AssignedTo = AssignedToTextBox.Text;
            template.ShortDescription = ShortDescriptionTextBox.Text;
            template.Description = DescriptionTextBox.Text;
            template.WorkNotes = WorkNotesTextBox.Text;
            template.Priority = (PriorityComboBox.SelectedItem as DropdownOption)?.UrlValue ?? "";
            template.State = (StateComboBox.SelectedItem as DropdownOption)?.UrlValue ?? "";

            // Persist resolved SNOW values so template loads skip lookups
            template.ResolvedRequestedForSysId = _resolvedRequestedForSnowId;
            template.ResolvedRequestedForDisplay = _resolvedRequestedForDisplay;
            template.ResolvedAssignmentGroupSysId = _resolvedAssignmentGroupSysId;
            template.ResolvedAssignmentGroupDisplay = _resolvedAssignmentGroupDisplay;
            template.ResolvedAssignedToSysId = _resolvedAssignedToSnowId;
            template.ResolvedAssignedToDisplay = _resolvedAssignedToDisplay;

            return template;
        }

        private void LoadTemplateToForm(SctaskTicketTemplate template)
        {
            // Clear existing resolved state
            _resolvedRitmSysId = null;
            _resolvedRitmDisplay = null;
            _resolvedRequestedForSnowId = null;
            _resolvedRequestedForDisplay = null;
            _resolvedAssignmentGroupSysId = null;
            _resolvedAssignmentGroupDisplay = null;
            _resolvedAssignedToSnowId = null;
            _resolvedAssignedToDisplay = null;
            _isRitmValid = false;
            _isAssignmentGroupValid = false;

            RitmNumberTextBox.Text = template.RequestItem;
            RequestedForTextBox.Text = template.RequestedFor;
            AssignmentGroupTextBox.Text = template.AssignmentGroup;
            SelectComboBoxByUrlValue(AssignmentGroupComboBox, template.AssignmentGroupDropdownValue);
            AssignedToTextBox.Text = template.AssignedTo;
            ShortDescriptionTextBox.Text = template.ShortDescription;
            DescriptionTextBox.Text = template.Description;
            WorkNotesTextBox.Text = template.WorkNotes;

            SelectComboBoxByUrlValue(PriorityComboBox, template.Priority);
            SelectComboBoxByUrlValue(StateComboBox, template.State);

            // Restore cached resolved values or trigger lookups
            if (!string.IsNullOrWhiteSpace(template.RequestItem))
            {
                _ = ResolveRitmAsync(template.RequestItem);
            }

            if (!string.IsNullOrWhiteSpace(template.ResolvedRequestedForSysId) && !string.IsNullOrWhiteSpace(template.RequestedFor))
            {
                _resolvedRequestedForSnowId = template.ResolvedRequestedForSysId;
                _resolvedRequestedForDisplay = template.ResolvedRequestedForDisplay;
                SetUserStatus("RequestedFor", true, template.ResolvedRequestedForDisplay ?? "Found");
            }
            else if (!string.IsNullOrWhiteSpace(template.RequestedFor))
            {
                _ = ResolveUserAsync(template.RequestedFor, "RequestedFor");
            }

            if (!string.IsNullOrWhiteSpace(template.ResolvedAssignmentGroupSysId) && !string.IsNullOrWhiteSpace(template.AssignmentGroup))
            {
                _resolvedAssignmentGroupSysId = template.ResolvedAssignmentGroupSysId;
                _resolvedAssignmentGroupDisplay = template.ResolvedAssignmentGroupDisplay;
                _isAssignmentGroupValid = true;
                ShowAssignmentGroupSuccess($"Found: {template.ResolvedAssignmentGroupDisplay ?? template.AssignmentGroup}");
            }
            else if (!string.IsNullOrWhiteSpace(template.AssignmentGroup))
            {
                _ = ResolveAssignmentGroupAsync(template.AssignmentGroup);
            }

            if (!string.IsNullOrWhiteSpace(template.ResolvedAssignedToSysId) && !string.IsNullOrWhiteSpace(template.AssignedTo))
            {
                _resolvedAssignedToSnowId = template.ResolvedAssignedToSysId;
                _resolvedAssignedToDisplay = template.ResolvedAssignedToDisplay;
                SetUserStatus("AssignedTo", true, template.ResolvedAssignedToDisplay ?? "Found");
            }
            else if (!string.IsNullOrWhiteSpace(template.AssignedTo))
            {
                _ = ResolveUserAsync(template.AssignedTo, "AssignedTo");
            }

            UpdateValidationState();
            UpdatePreview();
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

        #endregion

        #region RITM Resolution

        private async void RitmNumberTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var ritmNumber = RitmNumberTextBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(ritmNumber))
            {
                await ResolveRitmAsync(ritmNumber);
            }
            else
            {
                ClearRitmStatus();
            }
        }

        private async Task ResolveRitmAsync(string ritmNumber)
        {
            // Normalize - ensure RITM prefix
            var normalizedRitm = ritmNumber.Trim();
            if (!normalizedRitm.StartsWith("RITM", StringComparison.OrdinalIgnoreCase))
            {
                normalizedRitm = "RITM" + normalizedRitm;
                RitmNumberTextBox.Text = normalizedRitm;
            }

            ShowRitmLoading();

            try
            {
                // Check if authenticated
                if (!_snowUserService.IsAuthenticated)
                {
                    var authenticated = await _snowUserService.AuthenticateAsync();
                    if (!authenticated)
                    {
                        ShowRitmError("Not authenticated to ServiceNow");
                        return;
                    }
                }

                var sysId = await _snowUserService.GetRitmSysIdAsync(normalizedRitm);

                if (!string.IsNullOrEmpty(sysId))
                {
                    _resolvedRitmSysId = sysId;
                    _resolvedRitmDisplay = normalizedRitm;
                    _isRitmValid = true;

                    ShowRitmSuccess($"Found: {normalizedRitm}");
                }
                else
                {
                    _resolvedRitmDisplay = null;
                    ShowRitmError("RITM not found");
                }
            }
            catch (Exception ex)
            {
                ShowRitmError($"Error: {ex.Message}");
            }

            UpdateValidationState();
            UpdatePreview();
        }

        private void ShowRitmLoading()
        {
            RitmCheckIcon.Visibility = Visibility.Collapsed;
            RitmErrorIcon.Visibility = Visibility.Collapsed;
            RitmLoadingIcon.Visibility = Visibility.Visible;
            RitmStatusText.Text = "Validating...";
            _isRitmValid = false;
        }

        private void ShowRitmSuccess(string message)
        {
            RitmLoadingIcon.Visibility = Visibility.Collapsed;
            RitmErrorIcon.Visibility = Visibility.Collapsed;
            RitmCheckIcon.Visibility = Visibility.Visible;
            RitmStatusText.Text = message;
        }

        private void ShowRitmError(string message)
        {
            RitmLoadingIcon.Visibility = Visibility.Collapsed;
            RitmCheckIcon.Visibility = Visibility.Collapsed;
            RitmErrorIcon.Visibility = Visibility.Visible;
            RitmStatusText.Text = message;
            _isRitmValid = false;
            _resolvedRitmSysId = null;
        }

        private void ClearRitmStatus()
        {
            RitmCheckIcon.Visibility = Visibility.Collapsed;
            RitmErrorIcon.Visibility = Visibility.Collapsed;
            RitmLoadingIcon.Visibility = Visibility.Collapsed;
            RitmStatusText.Text = "";
            _isRitmValid = false;
            _resolvedRitmSysId = null;
            UpdateValidationState();
        }

        #endregion

        #region User Resolution (Requested For, Assigned To)

        private async void RequestedForTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var input = RequestedForTextBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(input))
            {
                await ResolveUserAsync(input, "RequestedFor");
            }
            else
            {
                ClearUserStatus("RequestedFor");
            }
        }

        private async void AssignedToTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var input = AssignedToTextBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(input))
            {
                await ResolveUserAsync(input, "AssignedTo");
            }
            else
            {
                ClearUserStatus("AssignedTo");
            }
        }

        private async Task ResolveUserAsync(string input, string fieldType)
        {
            // Check if it's a placeholder
            if (input.Contains("{{"))
            {
                SetUserStatus(fieldType, true, "Template variable");
                if (fieldType == "RequestedFor")
                {
                    _resolvedRequestedForSnowId = input;
                    _resolvedRequestedForDisplay = "Template variable";
                }
                else
                {
                    _resolvedAssignedToSnowId = input;
                    _resolvedAssignedToDisplay = "Template variable";
                }
                return;
            }

            // Check if it looks like a sys_id already (32-char hex, no @)
            if (Regex.IsMatch(input, @"^[a-f0-9]{32}$", RegexOptions.IgnoreCase) && !input.Contains("@"))
            {
                SetUserStatus(fieldType, true, "sys_id format");
                if (fieldType == "RequestedFor")
                {
                    _resolvedRequestedForSnowId = input;
                    _resolvedRequestedForDisplay = "sys_id format";
                }
                else
                {
                    _resolvedAssignedToSnowId = input;
                    _resolvedAssignedToDisplay = "sys_id format";
                }
                return;
            }

            ShowUserLoading(fieldType);

            try
            {
                // Check if authenticated
                if (!_snowUserService.IsAuthenticated)
                {
                    var authenticated = await _snowUserService.AuthenticateAsync();
                    if (!authenticated)
                    {
                        SetUserStatus(fieldType, false, "Not authenticated");
                        return;
                    }
                }

                var (sysId, displayName) = await _snowUserService.GetSysIdByEmailOrUsernameAsync(input);

                if (!string.IsNullOrEmpty(sysId))
                {
                    if (fieldType == "RequestedFor")
                    {
                        _resolvedRequestedForSnowId = sysId;
                        _resolvedRequestedForDisplay = displayName ?? "Found";
                    }
                    else
                    {
                        _resolvedAssignedToSnowId = sysId;
                        _resolvedAssignedToDisplay = displayName ?? "Found";
                    }

                    SetUserStatus(fieldType, true, displayName ?? "Found");
                }
                else
                {
                    SetUserStatus(fieldType, false, "User not found");
                }
            }
            catch (Exception ex)
            {
                SetUserStatus(fieldType, false, $"Error: {ex.Message}");
            }

            UpdatePreview();
        }

        private void ShowUserLoading(string fieldType)
        {
            if (fieldType == "RequestedFor")
            {
                RequestedForCheckIcon.Visibility = Visibility.Collapsed;
                RequestedForErrorIcon.Visibility = Visibility.Collapsed;
                RequestedForLoadingIcon.Visibility = Visibility.Visible;
                RequestedForStatusText.Text = "Looking up...";
            }
            else
            {
                AssignedToCheckIcon.Visibility = Visibility.Collapsed;
                AssignedToErrorIcon.Visibility = Visibility.Collapsed;
                AssignedToLoadingIcon.Visibility = Visibility.Visible;
                AssignedToStatusText.Text = "Looking up...";
            }
        }

        private void SetUserStatus(string fieldType, bool success, string message)
        {
            if (fieldType == "RequestedFor")
            {
                RequestedForLoadingIcon.Visibility = Visibility.Collapsed;
                RequestedForCheckIcon.Visibility = success ? Visibility.Visible : Visibility.Collapsed;
                RequestedForErrorIcon.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
                RequestedForStatusText.Text = message;
            }
            else
            {
                AssignedToLoadingIcon.Visibility = Visibility.Collapsed;
                AssignedToCheckIcon.Visibility = success ? Visibility.Visible : Visibility.Collapsed;
                AssignedToErrorIcon.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
                AssignedToStatusText.Text = message;
            }
        }

        private void ClearUserStatus(string fieldType)
        {
            if (fieldType == "RequestedFor")
            {
                RequestedForCheckIcon.Visibility = Visibility.Collapsed;
                RequestedForErrorIcon.Visibility = Visibility.Collapsed;
                RequestedForLoadingIcon.Visibility = Visibility.Collapsed;
                RequestedForStatusText.Text = "";
                _resolvedRequestedForSnowId = null;
            }
            else
            {
                AssignedToCheckIcon.Visibility = Visibility.Collapsed;
                AssignedToErrorIcon.Visibility = Visibility.Collapsed;
                AssignedToLoadingIcon.Visibility = Visibility.Collapsed;
                AssignedToStatusText.Text = "";
                _resolvedAssignedToSnowId = null;
            }
        }

        #endregion

        #region Assignment Group Resolution

        private void AssignmentGroupComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AssignmentGroupComboBox.SelectedItem is DropdownOption option && !string.IsNullOrEmpty(option.UrlValue))
            {
                AssignmentGroupTextBox.Text = option.UrlValue;
                _resolvedAssignmentGroupSysId = option.UrlValue;
                _resolvedAssignmentGroupDisplay = option.DisplayText;
                _isAssignmentGroupValid = true;
                ShowAssignmentGroupSuccess($"Selected: {option.DisplayText}");
                UpdateValidationState();
                UpdatePreview();
            }
        }

        private async void AssignmentGroupTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var input = AssignmentGroupTextBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(input))
            {
                await ResolveAssignmentGroupAsync(input);
            }
            else
            {
                ClearAssignmentGroupStatus();
            }
        }

        private async Task ResolveAssignmentGroupAsync(string input)
        {
            // Check if it looks like a sys_id already (32-char hex)
            if (Regex.IsMatch(input, @"^[a-f0-9]{32}$", RegexOptions.IgnoreCase))
            {
                _resolvedAssignmentGroupSysId = input;
                _resolvedAssignmentGroupDisplay = "sys_id format";
                _isAssignmentGroupValid = true;
                ShowAssignmentGroupSuccess("sys_id format");
                UpdateValidationState();
                return;
            }

            // Check if it matches a known group
            var groups = _ticketService.GetAllGroups();
            var match = groups.FirstOrDefault(g => 
                g.DisplayText.Equals(input, StringComparison.OrdinalIgnoreCase) ||
                g.UrlValue.Equals(input, StringComparison.OrdinalIgnoreCase));
            
            if (match != null)
            {
                _resolvedAssignmentGroupSysId = match.UrlValue;
                _resolvedAssignmentGroupDisplay = match.DisplayText;
                _isAssignmentGroupValid = true;
                ShowAssignmentGroupSuccess($"Matched: {match.DisplayText}");
                UpdateValidationState();
                return;
            }

            // Look up by name
            ShowAssignmentGroupLoading();

            try
            {
                // Check if authenticated
                if (!_snowUserService.IsAuthenticated)
                {
                    var authenticated = await _snowUserService.AuthenticateAsync();
                    if (!authenticated)
                    {
                        ShowAssignmentGroupError("Not authenticated");
                        return;
                    }
                }

                var (sysId, name) = await _snowUserService.GetGroupSysIdAsync(input);

                if (!string.IsNullOrEmpty(sysId))
                {
                    _resolvedAssignmentGroupSysId = sysId;
                    _resolvedAssignmentGroupDisplay = name ?? input;
                    _isAssignmentGroupValid = true;
                    ShowAssignmentGroupSuccess($"Found: {name ?? input}");
                }
                else
                {
                    ShowAssignmentGroupError("Group not found");
                }
            }
            catch (Exception ex)
            {
                ShowAssignmentGroupError($"Error: {ex.Message}");
            }

            UpdateValidationState();
            UpdatePreview();
        }

        private void ShowAssignmentGroupLoading()
        {
            AssignmentGroupCheckIcon.Visibility = Visibility.Collapsed;
            AssignmentGroupErrorIcon.Visibility = Visibility.Collapsed;
            AssignmentGroupLoadingIcon.Visibility = Visibility.Visible;
            AssignmentGroupStatusText.Text = "Looking up...";
            _isAssignmentGroupValid = false;
        }

        private void ShowAssignmentGroupSuccess(string message)
        {
            AssignmentGroupLoadingIcon.Visibility = Visibility.Collapsed;
            AssignmentGroupErrorIcon.Visibility = Visibility.Collapsed;
            AssignmentGroupCheckIcon.Visibility = Visibility.Visible;
            AssignmentGroupStatusText.Text = message;
        }

        private void ShowAssignmentGroupError(string message)
        {
            AssignmentGroupLoadingIcon.Visibility = Visibility.Collapsed;
            AssignmentGroupCheckIcon.Visibility = Visibility.Collapsed;
            AssignmentGroupErrorIcon.Visibility = Visibility.Visible;
            AssignmentGroupStatusText.Text = message;
            _isAssignmentGroupValid = false;
            _resolvedAssignmentGroupSysId = null;
        }

        private void ClearAssignmentGroupStatus()
        {
            AssignmentGroupCheckIcon.Visibility = Visibility.Collapsed;
            AssignmentGroupErrorIcon.Visibility = Visibility.Collapsed;
            AssignmentGroupLoadingIcon.Visibility = Visibility.Collapsed;
            AssignmentGroupStatusText.Text = "";
            _isAssignmentGroupValid = false;
            _resolvedAssignmentGroupSysId = null;
            UpdateValidationState();
        }

        private async void SaveAssignmentGroup_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAssignmentGroupValid || string.IsNullOrEmpty(_resolvedAssignmentGroupSysId))
            {
                MessageBox.Show("Please enter and validate a group first.", "Cannot Save", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var displayName = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter display name for this group:", 
                "Save Assignment Group", 
                AssignmentGroupTextBox.Text);

            if (string.IsNullOrWhiteSpace(displayName))
                return;

            await _ticketService.AddCustomGroupAsync(displayName, _resolvedAssignmentGroupSysId);

            // Refresh dropdown
            AssignmentGroupComboBox.ItemsSource = _ticketService.GetAllGroups();

            MessageBox.Show($"Assignment group '{displayName}' saved to quick-select.", "Saved", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region Validation

        private void UpdateValidationState()
        {
            var messages = new List<string>();

            if (string.IsNullOrWhiteSpace(RitmNumberTextBox.Text))
            {
                messages.Add("RITM number is required");
            }
            else if (!_isRitmValid)
            {
                messages.Add("RITM must be validated");
            }

            if (string.IsNullOrWhiteSpace(AssignmentGroupTextBox.Text))
            {
                messages.Add("Assignment group is required");
            }
            else if (!_isAssignmentGroupValid)
            {
                messages.Add("Assignment group must be validated");
            }

            var isValid = messages.Count == 0;
            CreateTicketsButton.IsEnabled = isValid;

            if (messages.Count > 0)
            {
                ValidationMessageText.Text = string.Join(" | ", messages);
                ValidationMessageBorder.Visibility = Visibility.Visible;
            }
            else
            {
                ValidationMessageBorder.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region Preview

        private void UpdatePreview()
        {
            var template = GetTemplateFromForm();
            var firstItem = _affectedItems.FirstOrDefault();

            var url = _ticketService.GenerateUrl(
                template,
                firstItem,
                _resolvedRitmSysId,
                _resolvedRequestedForSnowId,
                _resolvedAssignmentGroupSysId,
                _resolvedAssignedToSnowId);

            UrlPreviewTextBox.Text = url;

            // Check URL length
            var (isWarning, isError, message) = _ticketService.CheckUrlLength(url);
            if (isWarning)
            {
                UrlLengthWarning.Text = message;
                UrlLengthWarning.Foreground = isError 
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36))
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x98, 0x00));
                UrlLengthWarning.Visibility = Visibility.Visible;
            }
            else
            {
                UrlLengthWarning.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region Actions

        private void InsertVariable_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string textBoxName)
            {
                var targetTextBox = FindName(textBoxName) as TextBox;
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
                UpdateCreateButtonText();
                UpdatePreview();
            }
        }

        private void CopyPreviewUrl_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(UrlPreviewTextBox.Text);
        }

        private void CopyAllUrls_Click(object sender, RoutedEventArgs e)
        {
            var template = GetTemplateFromForm();
            var urls = _ticketService.GenerateUrls(
                template,
                _affectedItems,
                _resolvedRitmSysId,
                _resolvedRequestedForSnowId,
                _resolvedAssignmentGroupSysId,
                _resolvedAssignedToSnowId);

            var allUrls = string.Join(Environment.NewLine, urls.Select(u => u.Url));
            Clipboard.SetText(allUrls);

            MessageBox.Show($"Copied {urls.Count} URL(s) to clipboard.", "URLs Copied", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void CreateTickets_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRitmValid || !_isAssignmentGroupValid)
            {
                MessageBox.Show("Please validate RITM and Assignment Group before creating tickets.", 
                    "Validation Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Save browser preference
            var browserTag = (BrowserComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var browser = browserTag == "Chrome" ? TicketBrowser.Chrome : TicketBrowser.Edge;
            App.Settings.Current.TicketBrowser = browser;
            App.Settings.Save();

            var template = GetTemplateFromForm();
            var urls = _ticketService.GenerateUrls(
                template,
                _affectedItems,
                _resolvedRitmSysId,
                _resolvedRequestedForSnowId,
                _resolvedAssignmentGroupSysId,
                _resolvedAssignedToSnowId);

            // Confirm if many URLs
            if (urls.Count > 5)
            {
                var result = MessageBox.Show(
                    $"This will open {urls.Count} browser tabs. Continue?",
                    "Confirm",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            await _ticketService.OpenUrlsInBrowser(urls.Select(u => u.Url), browser);

            DialogResult = true;
            Close();
        }

        #endregion
    }
}
