using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ActiveScanner.Views
{
    /// <summary>
    /// Represents a single AD property with its name and value
    /// </summary>
    public class PropertyItem
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string ValueType { get; set; } = string.Empty;
    }

    /// <summary>
    /// Dialog for viewing all AD properties of an object (computer, user, or printer)
    /// </summary>
    public partial class ComputerPropertiesDialog : Window
    {
        private List<PropertyItem> _allProperties = new();
        private string _objectName = string.Empty;

        public ComputerPropertiesDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Load properties for display
        /// </summary>
        public void LoadProperties(string objectName, string? distinguishedName, Dictionary<string, object?> properties, string objectTypeName = "Object")
        {
            _objectName = objectName;
            Title = $"{objectTypeName} Properties";
            ComputerNameText.Text = objectName;
            DistinguishedNameText.Text = distinguishedName ?? "Unknown location";

            _allProperties = properties
                .OrderBy(p => p.Key)
                .Select(p => new PropertyItem
                {
                    Name = p.Key,
                    Value = FormatValue(p.Value),
                    ValueType = GetValueTypeName(p.Value)
                })
                .ToList();

            ApplyFilter();
        }

        private string FormatValue(object? value)
        {
            if (value == null)
                return string.Empty;

            // Handle COM objects (IADsLargeInteger for timestamps, security descriptors, etc.)
            if (value.GetType().IsCOMObject)
            {
                return FormatComObject(value);
            }

            // Handle byte arrays (like objectGUID, objectSid)
            if (value is byte[] bytes)
            {
                // Try to convert to GUID if it's 16 bytes
                if (bytes.Length == 16)
                {
                    try
                    {
                        return new Guid(bytes).ToString();
                    }
                    catch { }
                }
                
                // For SID or other binary data, show as hex
                if (bytes.Length <= 64)
                {
                    return BitConverter.ToString(bytes).Replace("-", " ");
                }
                return $"[Binary data: {bytes.Length} bytes]";
            }

            // Handle arrays/collections
            if (value is System.Collections.ICollection collection)
            {
                var items = new List<string>();
                foreach (var item in collection)
                {
                    items.Add(FormatValue(item)); // Recursively format items
                }
                return string.Join("\n", items);
            }

            // Handle COM objects for large integers (like lastLogonTimestamp)
            if (value is long longValue)
            {
                // Check if it looks like a Windows FILETIME
                if (longValue > 100000000000000000L && longValue < 200000000000000000L)
                {
                    try
                    {
                        var dateTime = DateTime.FromFileTime(longValue);
                        return $"{dateTime:g} (Raw: {longValue})";
                    }
                    catch { }
                }
            }

            // Handle DateTime
            if (value is DateTime dtValue)
            {
                return dtValue.ToString("g");
            }

            return value.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Handles COM objects like IADsLargeInteger (for timestamps) and IADsSecurityDescriptor
        /// </summary>
        private string FormatComObject(object comObject)
        {
            try
            {
                // Try to get IADsLargeInteger (used for timestamps like lastLogonTimestamp, pwdLastSet)
                var type = comObject.GetType();
                
                // Check for LargeInteger (has HighPart and LowPart properties)
                var highPart = type.InvokeMember("HighPart", 
                    System.Reflection.BindingFlags.GetProperty, null, comObject, null);
                var lowPart = type.InvokeMember("LowPart", 
                    System.Reflection.BindingFlags.GetProperty, null, comObject, null);
                
                if (highPart is int high && lowPart is int low)
                {
                    // Combine to get 64-bit value
                    long fileTime = ((long)high << 32) | (uint)low;
                    
                    // Handle "never" values (0 or max value)
                    if (fileTime == 0)
                        return "(Never / Not Set)";
                    if (fileTime == long.MaxValue || fileTime < 0)
                        return "(Never Expires)";
                    
                    try
                    {
                        var dateTime = DateTime.FromFileTime(fileTime);
                        return $"{dateTime:g}";
                    }
                    catch
                    {
                        return $"(Invalid date: {fileTime})";
                    }
                }
            }
            catch
            {
                // Not a LargeInteger, try other COM types
            }

            try
            {
                // Try to get security descriptor info
                var type = comObject.GetType();
                var owner = type.InvokeMember("Owner",
                    System.Reflection.BindingFlags.GetProperty, null, comObject, null);
                if (owner != null)
                {
                    return $"[Security Descriptor: Owner={owner}]";
                }
            }
            catch
            {
                // Not a security descriptor
            }

            return "[COM Object]";
        }

        private string GetValueTypeName(object? value)
        {
            if (value == null)
                return "null";

            var type = value.GetType();

            if (type.IsCOMObject)
                return "LargeInteger";
            if (type == typeof(byte[]))
                return "Binary";
            if (type == typeof(string))
                return "String";
            if (type == typeof(int) || type == typeof(long))
                return "Integer";
            if (type == typeof(DateTime))
                return "DateTime";
            if (type == typeof(bool))
                return "Boolean";
            if (typeof(System.Collections.ICollection).IsAssignableFrom(type))
                return "Multi-Value";

            return type.Name;
        }

        private void ApplyFilter()
        {
            // Guard against being called before InitializeComponent completes
            if (PropertiesGrid == null || SearchBox == null || HideEmptyCheckBox == null || CountText == null)
                return;

            var searchText = SearchBox.Text ?? "";
            var hideEmpty = HideEmptyCheckBox.IsChecked == true;

            var filtered = _allProperties.AsEnumerable();

            if (hideEmpty)
            {
                filtered = filtered.Where(p => !string.IsNullOrWhiteSpace(p.Value));
            }

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                // Each line is an OR term; legacy || separator also supported
                var searchParts = searchText.Split(new[] { "\r\n", "\n", "\r", "||" }, StringSplitOptions.RemoveEmptyEntries);
                filtered = filtered.Where(p =>
                {
                    foreach (var part in searchParts)
                    {
                        var trimmed = part.Trim();
                        if (!string.IsNullOrEmpty(trimmed) &&
                            (p.Name.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0 ||
                             p.Value.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            return true;
                        }
                    }
                    return false;
                });
            }

            var list = filtered.ToList();
            PropertiesGrid.ItemsSource = list;
            CountText.Text = $"Showing {list.Count} of {_allProperties.Count} properties";
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void FilterChanged(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        private void CopyName_Click(object sender, RoutedEventArgs e)
        {
            if (PropertiesGrid.SelectedItem is PropertyItem item)
            {
                Clipboard.SetText(item.Name);
            }
        }

        private void CopyValue_Click(object sender, RoutedEventArgs e)
        {
            if (PropertiesGrid.SelectedItem is PropertyItem item)
            {
                Clipboard.SetText(item.Value);
            }
        }

        private void CopyBoth_Click(object sender, RoutedEventArgs e)
        {
            if (PropertiesGrid.SelectedItem is PropertyItem item)
            {
                Clipboard.SetText($"{item.Name}: {item.Value}");
            }
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Title.Replace(" Properties", "")}: {_objectName}");
            sb.AppendLine($"Distinguished Name: {DistinguishedNameText.Text}");
            sb.AppendLine(new string('-', 80));

            foreach (var prop in _allProperties.Where(p => !string.IsNullOrWhiteSpace(p.Value)))
            {
                sb.AppendLine($"{prop.Name}: {prop.Value}");
            }

            Clipboard.SetText(sb.ToString());
            MessageBox.Show("All properties copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                FileName = $"{_objectName}_properties.csv",
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                DefaultExt = ".csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Property Name,Value,Type");

                    foreach (var prop in _allProperties)
                    {
                        // Escape values for CSV
                        var escapedValue = prop.Value.Replace("\"", "\"\"");
                        if (escapedValue.Contains(',') || escapedValue.Contains('"') || escapedValue.Contains('\n'))
                        {
                            escapedValue = $"\"{escapedValue}\"";
                        }

                        sb.AppendLine($"{prop.Name},{escapedValue},{prop.ValueType}");
                    }

                    File.WriteAllText(dialog.FileName, sb.ToString());
                    MessageBox.Show($"Properties exported to:\n{dialog.FileName}", "Export Complete", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting: {ex.Message}", "Export Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
