using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ActiveScanner.Models;
using ActiveScanner.Services;

namespace ActiveScanner.Views
{
    public partial class EmailTemplateWindow : Window
    {
        private readonly EmailTemplateService _service;
        private readonly ObservableCollection<AdObjectInfo> _items;
        private readonly List<(string DisplayName, string PropertyName)> _visibleColumns;
        private readonly Func<IEnumerable<string>, Task<Dictionary<string, (string? Email, string? DisplayName)>>>? _emailLookup;
        private readonly Dictionary<AdObjectInfo, LastUserInfo> _lastUserInfo = new();
        private List<(string DisplayName, string Placeholder)> _syntheticVariables = new();
        private EmailTemplate? _currentTemplate;
        private IReadOnlyList<EmailTemplate> _allTemplates = Array.Empty<EmailTemplate>();
        private string _templateSearchText = string.Empty;
        private bool _suppressPreview;
        private bool _isResolvingEmails;

        public EmailTemplateWindow(IEnumerable<AdObjectInfo> selectedItems,
                                   List<(string DisplayName, string PropertyName)> visibleColumns,
                                   Func<IEnumerable<string>, Task<Dictionary<string, (string? Email, string? DisplayName)>>>? emailLookup = null)
        {
            InitializeComponent();

            _service = new EmailTemplateService();
            _items = new ObservableCollection<AdObjectInfo>(selectedItems);
            _visibleColumns = visibleColumns;
            _emailLookup = emailLookup;

            InitializeControls();

            Closed += (s, e) => _service.Dispose();
        }

        private async void InitializeControls()
        {
            await _service.LoadAsync();

            AffectedItemsList.ItemsSource = _items;
            UpdateAffectedItemsHeader();

            // Build the available-variables list: column variables + synthetic placeholders.
            BuildSyntheticVariables();
            RefreshVariablesList();

            ToTextBox.TextChanged += (s, e) => UpdatePreview();
            CcTextBox.TextChanged += (s, e) => UpdatePreview();
            BccTextBox.TextChanged += (s, e) => UpdatePreview();
            SubjectTextBox.TextChanged += (s, e) => UpdatePreview();
            BodyTextBox.TextChanged += (s, e) => UpdatePreview();

            LoadTemplates();
            UpdateButtonText();
            UpdatePreview();

            // Kick off the LastUser email/display-name resolution in the background.
            _ = ResolveLastUserEmailsAsync();
        }

        /// <summary>
        /// Build the list of synthetic placeholders that should always be available regardless
        /// of which DataGrid columns are visible. These are surfaced in both the Available
        /// Variables panel and the per-field Insert menu.
        /// </summary>
        private void BuildSyntheticVariables()
        {
            var list = new List<(string DisplayName, string Placeholder)>();

            // Computer name -> trailing digits (e.g. ANC-LT12345 -> 12345). Useful for any item type.
            list.Add(("Name Trailing Number", "{{NameTrailingNumber}}"));

            if (_items.Any(HasResolvableLastUser) || _items.Any(i => i.ObjectType == AdObjectType.User))
            {
                list.Add(("Last User Email",        "{{LastUserEmail}}"));
                list.Add(("Last User Display Name", "{{LastUserDisplayName}}"));
                list.Add(("Last User First Name",   "{{LastUserFirstName}}"));
                list.Add(("Last User Last Name",    "{{LastUserLastName}}"));
            }

            _syntheticVariables = list;
        }

        private void RefreshVariablesList()
        {
            var variables = _visibleColumns
                .Select(c => $"{{{{{c.PropertyName}}}}}")
                .ToList();
            foreach (var (_, placeholder) in _syntheticVariables)
            {
                if (!variables.Contains(placeholder, StringComparer.OrdinalIgnoreCase))
                    variables.Add(placeholder);
            }
            VariablesList.ItemsSource = variables;
        }

        private static bool HasResolvableLastUser(AdObjectInfo o)
        {
            var u = o.LastUser;
            return !string.IsNullOrWhiteSpace(u) && !u.StartsWith("(");
        }

        /// <summary>
        /// Resolves a LastUser SAM (with optional domain prefix) to email + display name by:
        /// 1. Using AdObjectInfo.Mail/DisplayName when the item is itself a User.
        /// 2. Calling the host-supplied lookup delegate (cache + AD Global Catalog fallback)
        ///    against each computer/printer's LastUser.
        /// </summary>
        private async Task ResolveLastUserEmailsAsync()
        {
            if (_isResolvingEmails) return;
            _isResolvingEmails = true;
            try
            {
                var samPerItem = new Dictionary<AdObjectInfo, string>();
                foreach (var item in _items)
                {
                    if (item.ObjectType == AdObjectType.User)
                    {
                        _lastUserInfo[item] = new LastUserInfo(item.Mail, item.DisplayName);
                        continue;
                    }

                    if (!HasResolvableLastUser(item)) continue;

                    var lookup = item.LastUser!;
                    var slash = lookup.IndexOf('\\');
                    if (slash >= 0) lookup = lookup.Substring(slash + 1);
                    samPerItem[item] = lookup;
                }

                if (samPerItem.Count > 0 && _emailLookup != null)
                {
                    Dictionary<string, (string? Email, string? DisplayName)> resolved;
                    try
                    {
                        resolved = await _emailLookup(samPerItem.Values.Distinct(StringComparer.OrdinalIgnoreCase));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"LastUser email resolution failed: {ex.Message}");
                        resolved = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
                    }

                    foreach (var kvp in samPerItem)
                    {
                        if (resolved.TryGetValue(kvp.Value, out var info))
                        {
                            _lastUserInfo[kvp.Key] = new LastUserInfo(info.Email, info.DisplayName);
                        }
                        else
                        {
                            _lastUserInfo[kvp.Key] = new LastUserInfo(null, null);
                        }
                    }
                }

                UpdatePreview();
            }
            finally
            {
                _isResolvingEmails = false;
            }
        }

        private void LoadTemplates()
        {
            _allTemplates = _service.Templates;
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
                TemplateComboBox.ItemsSource = _allTemplates
                    .Where(t => t.Name.Contains(_templateSearchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                ClearTemplateSearchButton.Visibility = Visibility.Visible;
            }
        }

        private void TemplateComboBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            if (TemplateComboBox.Template.FindName("PART_EditableTextBox", TemplateComboBox) is TextBox tb)
            {
                _templateSearchText = tb.Text + e.Text;
                FilterTemplates();
                TemplateComboBox.IsDropDownOpen = true;
            }
        }

        private void TemplateComboBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Back || e.Key == System.Windows.Input.Key.Delete)
            {
                if (TemplateComboBox.Template.FindName("PART_EditableTextBox", TemplateComboBox) is TextBox tb && tb.Text.Length > 0)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _templateSearchText = tb.Text;
                        FilterTemplates();
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
            }
        }

        private void ClearTemplateSearch_Click(object sender, RoutedEventArgs e)
        {
            _templateSearchText = string.Empty;
            if (TemplateComboBox.Template.FindName("PART_EditableTextBox", TemplateComboBox) is TextBox tb)
                tb.Text = string.Empty;
            TemplateComboBox.SelectedItem = null;
            FilterTemplates();
        }

        private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TemplateComboBox.SelectedItem is EmailTemplate selected)
            {
                _currentTemplate = selected;
                LoadTemplateToForm(selected);
                DeleteTemplateButton.IsEnabled = true;
            }
            else
            {
                DeleteTemplateButton.IsEnabled = false;
            }
        }

        private void LoadTemplateToForm(EmailTemplate t)
        {
            _suppressPreview = true;
            try
            {
                ToTextBox.Text = t.To;
                CcTextBox.Text = t.Cc;
                BccTextBox.Text = t.Bcc;
                SubjectTextBox.Text = t.Subject;
                BodyTextBox.Text = t.Body;
                HtmlBodyCheckBox.IsChecked = t.IsHtml;
                if (t.RecipientMode == EmailRecipientMode.Combined)
                    CombinedRadio.IsChecked = true;
                else
                    PerItemRadio.IsChecked = true;
            }
            finally
            {
                _suppressPreview = false;
            }
            UpdatePreview();
        }

        private EmailTemplate BuildTemplateFromForm()
        {
            var t = new EmailTemplate();
            if (_currentTemplate != null)
            {
                t.Id = _currentTemplate.Id;
                t.Name = _currentTemplate.Name;
                t.CreatedAt = _currentTemplate.CreatedAt;
            }
            t.To = ToTextBox.Text;
            t.Cc = CcTextBox.Text;
            t.Bcc = BccTextBox.Text;
            t.Subject = SubjectTextBox.Text;
            t.Body = BodyTextBox.Text;
            t.IsHtml = HtmlBodyCheckBox.IsChecked == true;
            t.RecipientMode = CombinedRadio.IsChecked == true
                ? EmailRecipientMode.Combined
                : EmailRecipientMode.PerItem;
            return t;
        }

        private void UpdatePreview()
        {
            if (_suppressPreview) return;
            var template = BuildTemplateFromForm();
            var sample = _items.FirstOrDefault();
            var preview = _service.GeneratePreview(template, sample, _items, _lastUserInfo);

            PreviewToText.Text = preview.To;
            PreviewCcText.Text = preview.Cc;
            PreviewSubjectText.Text = preview.Subject;
            PreviewBodyText.Text = preview.Body;
        }

        private void HtmlBodyCheckBox_Click(object sender, RoutedEventArgs e) => UpdatePreview();
        private void RecipientMode_Changed(object sender, RoutedEventArgs e) => UpdatePreview();

        private void UpdateAffectedItemsHeader()
        {
            var count = _items.Count;
            AffectedItemsHeader.Text = count == 1 ? "1 item selected" : $"{count} items selected";
            UpdateButtonText();
        }

        private void UpdateButtonText()
        {
            var count = _items.Count;
            var combined = CombinedRadio?.IsChecked == true;
            if (count == 0)
            {
                OpenInOutlookButtonText.Text = "Open in Outlook";
                OpenInOutlookButton.IsEnabled = false;
                return;
            }
            OpenInOutlookButton.IsEnabled = true;
            if (combined || count == 1)
                OpenInOutlookButtonText.Text = "Open in Outlook";
            else
                OpenInOutlookButtonText.Text = $"Open {count} Emails";
        }

        private void RemoveAffectedItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is AdObjectInfo item)
            {
                _items.Remove(item);
                _lastUserInfo.Remove(item);
                UpdateAffectedItemsHeader();
                UpdatePreview();
            }
        }

        private void InsertVariable_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string targetName) return;
            if (FindName(targetName) is not TextBox target) return;

            var menu = new ContextMenu();
            foreach (var col in _visibleColumns)
            {
                menu.Items.Add(BuildInsertMenuItem(target, col.DisplayName, $"{{{{{col.PropertyName}}}}}"));
            }
            if (_syntheticVariables.Count > 0)
            {
                menu.Items.Add(new Separator());
                foreach (var (display, placeholder) in _syntheticVariables)
                {
                    menu.Items.Add(BuildInsertMenuItem(target, display, placeholder));
                }
            }
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }

        private static MenuItem BuildInsertMenuItem(TextBox target, string header, string placeholder)
        {
            var mi = new MenuItem { Header = header, Tag = placeholder };
            mi.Click += (s, args) =>
            {
                if (s is MenuItem m && m.Tag is string variable)
                {
                    var caret = target.CaretIndex;
                    target.Text = target.Text.Insert(caret, variable);
                    target.CaretIndex = caret + variable.Length;
                    target.Focus();
                }
            };
            return mi;
        }

        private async void SaveTemplate_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("Save Template", "Enter a name for this template:",
                _currentTemplate?.Name ?? "");
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            {
                var template = BuildTemplateFromForm();
                template.Name = dialog.InputText;

                if (_currentTemplate != null && _currentTemplate.Name == dialog.InputText)
                {
                    template.Id = _currentTemplate.Id;
                    template.CreatedAt = _currentTemplate.CreatedAt;
                }

                await _service.SaveTemplateAsync(template);
                _currentTemplate = template;

                LoadTemplates();
                TemplateComboBox.SelectedItem = _service.Templates.FirstOrDefault(t => t.Id == template.Id);

                MessageBox.Show("Template saved.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void DeleteTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTemplate == null) return;

            var result = MessageBox.Show(
                $"Delete the template '{_currentTemplate.Name}'?",
                "Delete Template",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                await _service.DeleteTemplateAsync(_currentTemplate.Id);
                _currentTemplate = null;
                LoadTemplates();
                TemplateComboBox.SelectedIndex = -1;
                DeleteTemplateButton.IsEnabled = false;
            }
        }

        private async void OpenInOutlook_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0) return;

            var template = BuildTemplateFromForm();
            var items = _items.ToList();

            // If the template references {{LastUserEmail}} but resolution hasn't completed yet,
            // wait for it so we don't open Outlook with empty addresses.
            if (TemplateReferencesResolvedLastUser(template))
            {
                if (_isResolvingEmails || _lastUserInfo.Count == 0)
                {
                    try
                    {
                        Mouse.OverrideCursor = Cursors.Wait;
                        await ResolveLastUserEmailsAsync();
                    }
                    finally
                    {
                        Mouse.OverrideCursor = null;
                    }
                }
            }

            // Confirm bulk open
            var willOpen = template.RecipientMode == EmailRecipientMode.Combined ? 1 : items.Count;
            if (willOpen > 5)
            {
                var confirm = MessageBox.Show(
                    $"This will open {willOpen} Outlook compose windows. Continue?",
                    "Confirm",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var opened = _service.OpenInOutlook(template, items, _lastUserInfo);
                Mouse.OverrideCursor = null;

                if (opened == 0)
                {
                    MessageBox.Show("No emails were opened.", "Outlook",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                MessageBox.Show(
                    "Failed to open Outlook compose window.\n\n" + ex.Message +
                    "\n\nMake sure Microsoft Outlook is installed and running, and that this application " +
                    "is allowed to interact with it.",
                    "Outlook Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool TemplateReferencesResolvedLastUser(EmailTemplate t)
        {
            return ContainsResolvedLastUser(t.To)
                || ContainsResolvedLastUser(t.Cc)
                || ContainsResolvedLastUser(t.Bcc)
                || ContainsResolvedLastUser(t.Subject)
                || ContainsResolvedLastUser(t.Body);
        }

        // The four placeholders that depend on the async LastUser lookup completing first.
        private static readonly string[] ResolvedLastUserPlaceholders = new[]
        {
            "{{LastUserEmail}}",
            "{{LastUserDisplayName}}",
            "{{LastUserFirstName}}",
            "{{LastUserLastName}}"
        };

        private static bool ContainsResolvedLastUser(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var p in ResolvedLastUserPlaceholders)
            {
                if (s.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
