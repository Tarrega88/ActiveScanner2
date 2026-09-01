using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using ActiveScanner.ViewModels;

namespace ActiveScanner.Views
{
    /// <summary>
    /// A larger, modal editor for a single column filter. Edits are buffered: nothing is
    /// applied to the tab/grid until Apply is clicked. Closing without applying discards
    /// any unapplied changes.
    /// </summary>
    public partial class FilterExpandWindow : Window
    {
        private readonly OutputTabViewModel _tab;
        private readonly PropertyInfo? _filterProp;
        private readonly PropertyInfo? _modeProp;

        public FilterExpandWindow(OutputTabViewModel tab, string filterProperty)
        {
            InitializeComponent();

            _tab = tab;
            DataContext = tab;

            var label = PrettifyLabel(filterProperty);
            Title = $"Filter: {label}";
            HeaderText.Text = label;

            // Seed the editor from the current filter value (a copy - not live-bound).
            _filterProp = tab.GetType().GetProperty(filterProperty);
            EditorBox.Text = _filterProp?.GetValue(tab) as string ?? string.Empty;
            EditorBox.TextChanged += (s, e) => UpdateValueCount();

            // Seed the match-mode dropdown from the current mode, if this column has one.
            _modeProp = tab.GetType().GetProperty(filterProperty + "Mode");
            if (_modeProp != null)
            {
                ModeCombo.ItemsSource = OutputTabViewModel.FilterModes;
                ModeCombo.SelectedItem = _modeProp.GetValue(tab);
            }
            else
            {
                ModePanel.Visibility = Visibility.Collapsed;
            }

            Loaded += (s, e) =>
            {
                EditorBox.Focus();
                EditorBox.CaretIndex = EditorBox.Text?.Length ?? 0;
                UpdateValueCount();
                UpdateRowCount();
            };
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            // Collapse blank/whitespace-only lines and trim each remaining line.
            var normalized = NormalizeFilterText(EditorBox.Text);
            EditorBox.Text = normalized;
            EditorBox.CaretIndex = normalized.Length;

            _filterProp?.SetValue(_tab, normalized);
            if (_modeProp != null && ModeCombo.SelectedItem is FilterMode mode)
                _modeProp.SetValue(_tab, mode);

            _tab.ApplyFilterAndSort();

            UpdateValueCount();
            UpdateRowCount();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void UpdateValueCount()
        {
            var values = (EditorBox.Text ?? string.Empty)
                .Split(new[] { "\r\n", "\n", "\r", "||" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Count(v => v.Length > 0);

            ValueCountText.Text = values == 1 ? "1 value" : $"{values} values";
        }

        private void UpdateRowCount() => RowCountText.Text = $"{_tab.FilteredCount} rows shown";

        private static string NormalizeFilterText(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var lines = text
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0);

            return string.Join(Environment.NewLine, lines);
        }

        private static string PrettifyLabel(string filterProperty)
        {
            var name = filterProperty.StartsWith("Filter", StringComparison.Ordinal)
                ? filterProperty.Substring("Filter".Length)
                : filterProperty;

            var sb = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                    sb.Append(' ');
                sb.Append(name[i]);
            }
            return sb.ToString();
        }
    }
}
