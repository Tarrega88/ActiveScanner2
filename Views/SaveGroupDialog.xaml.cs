using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace ActiveScanner.Views
{
    public partial class SaveGroupDialog : Window
    {
        private readonly HashSet<string> _existingNames;

        public string GroupName => GroupNameTextBox.Text.Trim();

        /// <summary>
        /// Creates a name-prompt dialog with optional duplicate-name validation.
        /// </summary>
        /// <param name="title">Window title.</param>
        /// <param name="promptText">Prompt label shown above the input.</param>
        /// <param name="existingNames">Names already taken (case-insensitive). The new name must not match any.</param>
        /// <param name="initialName">Initial value to pre-populate the input with.</param>
        /// <param name="okButtonText">Text for the OK button.</param>
        public SaveGroupDialog(
            string title = "Name Group",
            string promptText = "Enter a name for this group:",
            IEnumerable<string>? existingNames = null,
            string? initialName = null,
            string okButtonText = "Save")
        {
            InitializeComponent();

            Title = title;
            PromptText.Text = promptText;
            SaveButton.Content = okButtonText;

            _existingNames = new HashSet<string>(
                (existingNames ?? Enumerable.Empty<string>()).Select(n => n?.Trim() ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(initialName))
            {
                GroupNameTextBox.Text = initialName;
                GroupNameTextBox.SelectAll();
            }

            Loaded += (_, _) => GroupNameTextBox.Focus();
            Validate();
        }

        // Parameterless ctor for XAML/designer compatibility.
        public SaveGroupDialog() : this("Name Group", "Enter a name for this group:", null, null, "Save") { }

        private bool Validate()
        {
            var name = GroupName;

            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError("Group name is required.");
                return false;
            }

            if (name.Length > 100)
            {
                ShowError("Group name must be 100 characters or less.");
                return false;
            }

            if (_existingNames.Contains(name))
            {
                ShowError($"A group named \"{name}\" already exists. Please choose a different name.");
                return false;
            }

            ClearError();
            return true;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
            SaveButton.IsEnabled = false;
        }

        private void ClearError()
        {
            ErrorText.Text = string.Empty;
            ErrorText.Visibility = Visibility.Collapsed;
            SaveButton.IsEnabled = true;
        }

        private void GroupNameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            Validate();
        }

        private void GroupNameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Save_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!Validate()) return;

            DialogResult = true;
            Close();
        }
    }
}
