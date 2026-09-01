using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ActiveScanner.Models;
using ActiveScanner.ViewModels;

namespace ActiveScanner.Converters
{
    /// <summary>
    /// Converts a boolean to a Visibility value
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility visibility && visibility == Visibility.Visible;
        }
    }

    /// <summary>
    /// Converts a boolean to Visibility, using Hidden (not Collapsed) when false to preserve layout space
    /// </summary>
    public class BoolToHiddenVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Hidden;
            }
            return Visibility.Hidden;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility visibility && visibility == Visibility.Visible;
        }
    }

    /// <summary>
    /// Converts a boolean to one of two strings specified in the parameter (format: "TrueString|FalseString")
    /// Supports {0} placeholder in TrueString for additional binding data
    /// </summary>
    public class BoolToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string paramString)
            {
                var parts = paramString.Split('|');
                if (parts.Length == 2)
                {
                    return boolValue ? parts[0] : parts[1];
                }
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a count to Visibility (visible if > 0)
    /// </summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a count to bool (true if > 0)
    /// </summary>
    public class CountToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count > 0;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a count to Visibility (visible if == 0, for empty states)
    /// </summary>
    public class ZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts DataGridRowDetailsVisibility to boolean for ToggleButton IsChecked binding
    /// </summary>
    public class DataGridRowDetailsVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Check if the visibility enum value indicates "Visible"
            if (value != null && value.ToString() == "Visible")
            {
                return true;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isChecked)
            {
                // Return the appropriate enum value as object
                // The binding system will handle the conversion
                return isChecked ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Converts a boolean to a Visibility value (inverse - false = Visible)
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility visibility && visibility == Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Converts a boolean to an opacity value (1.0 for true, 0.4 for false)
    /// </summary>
    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? 1.0 : 0.4;
            }
            return 1.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a boolean to FontStyle (Italic for true, Normal for false)
    /// Used for staleness indicator on cached data
    /// </summary>
    public class BoolToFontStyleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue)
            {
                return FontStyles.Italic;
            }
            return FontStyles.Normal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Inverts a boolean value
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }
    }

    /// <summary>
    /// Converts a boolean to one of two icon names
    /// </summary>
    public class BoolToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string icons)
            {
                var parts = icons.Split('|');
                if (parts.Length == 2)
                {
                    return boolValue ? parts[0] : parts[1];
                }
            }
            return "Circle";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts connection status to a color
    /// </summary>
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                if (status.StartsWith("Connected", StringComparison.OrdinalIgnoreCase))
                {
                    return Colors.LimeGreen;
                }
                if (status.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                {
                    return Colors.Red;
                }
            }
            return Colors.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts OutputTabType enum to MaterialDesign icon kind string
    /// </summary>
    public class TabTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is OutputTabType tabType)
            {
                return tabType switch
                {
                    OutputTabType.Computers => "DesktopClassic",
                    OutputTabType.Users => "AccountMultiple",
                    OutputTabType.Printers => "Printer",
                    OutputTabType.Results => "FolderMultiple",  // Mixed results
                    _ => "Folder"
                };
            }
            return "Folder";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts OutputTabType enum to visibility based on parameter
    /// </summary>
    public class EnumToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is OutputTabType tabType && parameter is string allowedTypes)
            {
                var types = allowedTypes.Split('|');
                foreach (var type in types)
                {
                    if (Enum.TryParse<OutputTabType>(type.Trim(), out var parsed) && parsed == tabType)
                    {
                        return Visibility.Visible;
                    }
                }
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts success/failure to a color
    /// </summary>
    public class SuccessToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool success)
            {
                return success ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red);
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a string success/fail status color name to brush
    /// </summary>
    public class StringToColorBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string colorStr)
            {
                if (colorStr.StartsWith("#"))
                {
                    try
                    {
                        var color = (Color)ColorConverter.ConvertFromString(colorStr);
                        return new SolidColorBrush(color);
                    }
                    catch
                    {
                        return new SolidColorBrush(Colors.Gray);
                    }
                }

                return colorStr.ToLower() switch
                {
                    "green" => new SolidColorBrush(Colors.Green),
                    "red" => new SolidColorBrush(Colors.Red),
                    "yellow" => new SolidColorBrush(Colors.Orange),
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts SortDirection enum to an arrow icon name
    /// </summary>
    public class SortDirectionToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ViewModels.SortDirection direction)
            {
                return direction switch
                {
                    ViewModels.SortDirection.Ascending => "ArrowUp",
                    ViewModels.SortDirection.Descending => "ArrowDown",
                    _ => "Circle"  // Placeholder - icon is hidden when None anyway
                };
            }
            return "Circle";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Multi-value converter to show sort arrow only for the current sorted column
    /// (and only when direction is not None)
    /// </summary>
    public class SortColumnVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is string sortColumn && parameter is string columnName)
            {
                // Also check that direction is not None
                if (values[1] is ViewModels.SortDirection direction && direction == ViewModels.SortDirection.None)
                {
                    return Visibility.Collapsed;
                }
                return sortColumn == columnName ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts nullable int to string for TextBox binding
    /// </summary>
    public class NullableIntConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue.ToString();
            }
            return string.Empty;
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str && int.TryParse(str, out int result))
            {
                return result;
            }
            return null;
        }
    }

    /// <summary>
    /// Converts an LDAP distinguished name (e.g., "OU=Computers,OU=IT,DC=example,DC=com")
    /// to a user-friendly breadcrumb path (e.g., "example.com / IT / Computers")
    /// </summary>
    public class DistinguishedNameToPathConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string dn || string.IsNullOrWhiteSpace(dn))
                return string.Empty;

            try
            {
                var parts = dn.Split(',');
                var ouParts = new System.Collections.Generic.List<string>();
                var dcParts = new System.Collections.Generic.List<string>();

                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (trimmed.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                    {
                        ouParts.Add(trimmed.Substring(3));
                    }
                    else if (trimmed.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                    {
                        dcParts.Add(trimmed.Substring(3));
                    }
                    // Skip CN= parts as they're typically the object itself
                }

                // Build the domain name from DC parts
                var domain = string.Join(".", dcParts);

                // Reverse OU parts so they read from root to leaf (like a file path)
                ouParts.Reverse();

                // Combine domain and OU path
                if (!string.IsNullOrEmpty(domain) && ouParts.Count > 0)
                {
                    return domain + " / " + string.Join(" / ", ouParts);
                }
                else if (!string.IsNullOrEmpty(domain))
                {
                    return domain;
                }
                else if (ouParts.Count > 0)
                {
                    return string.Join(" / ", ouParts);
                }

                // Fallback to original if we couldn't parse it
                return dn;
            }
            catch
            {
                // If parsing fails, return the original value
                return dn;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts an enum value to boolean for toggle buttons
    /// </summary>
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;
            return value.ToString() == parameter.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue && parameter != null)
            {
                return Enum.Parse(targetType, parameter.ToString()!);
            }
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Converts a list/collection to Visibility - Visible if the list has items, Collapsed otherwise.
    /// Also shows Collapsed if the list is null.
    /// </summary>
    public class ListHasItemsToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return Visibility.Collapsed;
                
            if (value is System.Collections.IList list)
            {
                return list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (value is System.Collections.ICollection collection)
            {
                return collection.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a list/collection to inverse Visibility - Collapsed if the list has items, Visible otherwise.
    /// </summary>
    public class ListHasItemsToInverseVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return Visibility.Visible;
                
            if (value is System.Collections.IList list)
            {
                return list.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            if (value is System.Collections.ICollection collection)
            {
                return collection.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts IP address to color (muted for resolving/error states, normal otherwise)
    /// </summary>
    public class IpAddressToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var ip = value as string;
            if (string.IsNullOrEmpty(ip) || ip.StartsWith("("))
            {
                // Resolving, error, or placeholder states - use theme-aware muted color
                return System.Windows.Application.Current.TryFindResource("MaterialDesignBodyLight") 
                       ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
            }
            // Normal IP address - use theme-aware body color
            return System.Windows.Application.Current.TryFindResource("MaterialDesignBody") 
                   ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Multi-value converter: takes IpAddress and IsIpStale to determine text color.
    /// Stale IPs (from history when DNS fails) shown in grey, same as error states.
    /// </summary>
    public class IpAddressStaleColorConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var ip = values[0] as string;
            var isStale = values.Length > 1 && values[1] is true;

            if (isStale || string.IsNullOrEmpty(ip) || ip.StartsWith("("))
            {
                return System.Windows.Application.Current.TryFindResource("MaterialDesignBodyLight") 
                       ?? new SolidColorBrush(Colors.Gray);
            }
            return System.Windows.Application.Current.TryFindResource("MaterialDesignBody") 
                   ?? new SolidColorBrush(Colors.White);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts string to visibility (visible if not null/empty)
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts string to bool (true if not null/empty)
    /// </summary>
    public class StringToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !string.IsNullOrEmpty(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts any object to bool (true if not null)
    /// </summary>
    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts any object to Visibility (Visible if not null, Collapsed if null)
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a nullable boolean to a ComboBox index and back.
    /// Index 0 = null (All), Index 1 = true (Yes), Index 2 = false (No)
    /// </summary>
    public class NullableBoolToIndexConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null)
                return 0; // "All"
            if (value is bool boolValue)
                return boolValue ? 1 : 2; // true = "Yes" (1), false = "No" (2)
            return 0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                return index switch
                {
                    1 => true,   // "Yes"
                    2 => false,  // "No"
                    _ => null    // "All" or any other
                };
            }
            return null;
        }
    }

    /// <summary>
    /// A proxy element that allows DataGridColumns to bind to properties outside their visual tree.
    /// DataGridColumns are not part of the visual tree, so RelativeSource bindings don't work.
    /// Use this class as a StaticResource and bind to its Data property.
    /// </summary>
    public class BindingProxy : Freezable
    {
        protected override Freezable CreateInstanceCore()
        {
            return new BindingProxy();
        }

        public object Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register("Data", typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));
    }

    /// <summary>
    /// MultiValueConverter that returns Visible only if ObjectType is Computer AND IsMouseOver is true.
    /// Used for the Vista Copy button which should only appear on computer names.
    /// Values[0] = ObjectType (AdObjectType enum), Values[1] = IsMouseOver (bool)
    /// </summary>
    public class ComputerHoverVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && 
                values[0] is Models.AdObjectType objectType && 
                values[1] is bool isMouseOver)
            {
                if (objectType != Models.AdObjectType.Computer && objectType != Models.AdObjectType.Printer)
                    return Visibility.Collapsed;

                if (isMouseOver)
                    return Visibility.Visible;

                return Visibility.Hidden;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// MultiValueConverter for the Last User fetch button.
    /// Shows the button only when hovering AND no user profiles are loaded yet AND not currently querying.
    /// Values[0] = LastUserProfiles (List), Values[1] = IsMouseOver (bool), Values[2] = LastUser (string)
    /// </summary>
    public class LastUserFetchButtonVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[1] is bool isMouseOver)
            {
                // Check if currently querying
                var lastUser = values.Length >= 3 ? values[2] as string : null;
                if (lastUser == "Querying...")
                {
                    return Visibility.Hidden;
                }

                // Only show if hovering AND no profiles loaded yet
                var hasProfiles = values[0] is System.Collections.IList list && list.Count > 0;
                if (!hasProfiles && isMouseOver)
                {
                    return Visibility.Visible;
                }
            }
            return Visibility.Hidden;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a ButtonColorOption to a SolidColorBrush, adjusting shade based on light/dark mode.
    /// Pass "IsDarkMode" as a MultiBinding second value or use parameter "Dark" or "Light" to force.
    /// </summary>
    public class ButtonColorToBrushConverter : IMultiValueConverter
    {
        // Color definitions - Light mode (medium saturation), Dark mode (vibrant)
        private static readonly (Color Light, Color Dark) DefaultColor = (Color.FromRgb(103, 58, 183), Color.FromRgb(149, 117, 205));
        private static readonly (Color Light, Color Dark) BlueColor = (Color.FromRgb(30, 136, 229), Color.FromRgb(100, 181, 246));
        private static readonly (Color Light, Color Dark) GreenColor = (Color.FromRgb(67, 160, 71), Color.FromRgb(102, 187, 106));
        private static readonly (Color Light, Color Dark) OrangeColor = (Color.FromRgb(251, 140, 0), Color.FromRgb(255, 167, 38));
        private static readonly (Color Light, Color Dark) PurpleColor = (Color.FromRgb(142, 36, 170), Color.FromRgb(171, 104, 186));
        private static readonly (Color Light, Color Dark) TealColor = (Color.FromRgb(0, 150, 136), Color.FromRgb(77, 182, 172));
        private static readonly (Color Light, Color Dark) RedColor = (Color.FromRgb(229, 57, 53), Color.FromRgb(229, 115, 115));

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2 || 
                values[0] == DependencyProperty.UnsetValue || 
                values[1] == DependencyProperty.UnsetValue)
                return new SolidColorBrush(DefaultColor.Light);

            var colorOption = values[0] is ButtonColorOption opt ? opt : ButtonColorOption.Default;
            var isDarkMode = values[1] is bool dark && dark;

            var colorPair = colorOption switch
            {
                ButtonColorOption.Blue => BlueColor,
                ButtonColorOption.Green => GreenColor,
                ButtonColorOption.Orange => OrangeColor,
                ButtonColorOption.Purple => PurpleColor,
                ButtonColorOption.Teal => TealColor,
                ButtonColorOption.Red => RedColor,
                _ => DefaultColor
            };

            return new SolidColorBrush(isDarkMode ? colorPair.Dark : colorPair.Light);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Simple single-value converter for ButtonColor that auto-detects dark/light mode
    /// </summary>
    public class ButtonColorToSimpleBrushConverter : IValueConverter
    {
        // Light mode colors (medium saturation for visibility on light backgrounds)
        private static readonly Dictionary<ButtonColorOption, Color> LightModeColors = new()
        {
            { ButtonColorOption.Default, Color.FromRgb(103, 58, 183) },  // Purple
            { ButtonColorOption.Blue, Color.FromRgb(30, 136, 229) },     // Blue
            { ButtonColorOption.Green, Color.FromRgb(67, 160, 71) },     // Green
            { ButtonColorOption.Orange, Color.FromRgb(251, 140, 0) },    // Orange
            { ButtonColorOption.Purple, Color.FromRgb(142, 36, 170) },   // Purple
            { ButtonColorOption.Teal, Color.FromRgb(0, 150, 136) },      // Teal
            { ButtonColorOption.Red, Color.FromRgb(229, 57, 53) }        // Red
        };

        // Dark mode colors (vibrant but visible on dark backgrounds)
        private static readonly Dictionary<ButtonColorOption, Color> DarkModeColors = new()
        {
            { ButtonColorOption.Default, Color.FromRgb(149, 117, 205) }, // Vibrant purple
            { ButtonColorOption.Blue, Color.FromRgb(100, 181, 246) },    // Vibrant blue
            { ButtonColorOption.Green, Color.FromRgb(102, 187, 106) },   // Vibrant green
            { ButtonColorOption.Orange, Color.FromRgb(255, 167, 38) },   // Vibrant orange
            { ButtonColorOption.Purple, Color.FromRgb(171, 104, 186) },  // Vibrant purple
            { ButtonColorOption.Teal, Color.FromRgb(77, 182, 172) },     // Vibrant teal
            { ButtonColorOption.Red, Color.FromRgb(229, 115, 115) }      // Vibrant red
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var colorOption = value is ButtonColorOption opt ? opt : ButtonColorOption.Default;
            
            // Detect dark mode from app settings
            bool isDarkMode = false;
            try
            {
                isDarkMode = App.Settings?.Current?.DarkMode ?? false;
            }
            catch
            {
                // Fallback: check parameter
                isDarkMode = parameter is string s && s.Equals("Dark", StringComparison.OrdinalIgnoreCase);
            }

            var colorDict = isDarkMode ? DarkModeColors : LightModeColors;
            var color = colorDict.TryGetValue(colorOption, out var c) ? c : colorDict[ButtonColorOption.Default];

            return new SolidColorBrush(color);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts an enum value to a collection of all enum values of that type.
    /// Used for binding enums to ComboBox ItemsSource.
    /// </summary>
    public class EnumToCollectionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return Array.Empty<object>();
            return Enum.GetValues(value.GetType());
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Multi-value converter that returns Visible when first bool is true AND second bool is false.
    /// Used for showing blocking overlay only for non-network busy operations.
    /// </summary>
    public class BusyAndNotNetworkConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is bool isBusy && values[1] is bool isNetworkOp)
            {
                return (isBusy && !isNetworkOp) ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Returns Hidden (not Collapsed) when all bound bools are false, Visible when any is true.
    /// Preserves layout space when inactive.
    /// </summary>
    public class AnyBoolToHiddenVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            foreach (var v in values)
            {
                if (v is bool b && b) return Visibility.Visible;
            }
            return Visibility.Hidden;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a boolean to a GridLength (Star when true, Auto when false).
    /// Used for accordion-style layouts where expanded sections fill available space.
    /// </summary>
    public class BoolToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isExpanded && isExpanded)
            {
                return new GridLength(1, GridUnitType.Star);
            }
            return GridLength.Auto;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a UTC DateTime to local time for display
    /// </summary>
    public class UtcToLocalDateTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dt)
                return dt.ToLocalTime().ToString("g");
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Returns Visible only when ALL bound bools are true, Hidden otherwise.
    /// Useful for requiring multiple conditions to be met (e.g., admin logged in AND cell hovered AND not editing).
    /// </summary>
    public class AllBoolToHiddenVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            foreach (var v in values)
            {
                // Handle unset values (DataContext not ready)
                if (v == DependencyProperty.UnsetValue)
                    return Visibility.Hidden;
                    
                if (v is bool b && !b) 
                    return Visibility.Hidden;
            }
            return Visibility.Visible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
