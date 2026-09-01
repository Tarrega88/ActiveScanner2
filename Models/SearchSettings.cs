using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents the search type options (Computer, User, Printer)
    /// </summary>
    public partial class SearchTypeOption : ObservableObject
    {
        [ObservableProperty]
        private bool _isSelected = true;

        public string Name { get; set; } = string.Empty;
        public SearchObjectType ObjectType { get; set; }

        public SearchTypeOption(string name, SearchObjectType objectType, bool isSelected = true)
        {
            Name = name;
            ObjectType = objectType;
            IsSelected = isSelected;
        }
    }

    /// <summary>
    /// Represents a searchable column option
    /// </summary>
    public partial class SearchColumnOption : ObservableObject
    {
        [ObservableProperty]
        private bool _isSelected = true;

        /// <summary>
        /// Display name for the column
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// The LDAP attribute name
        /// </summary>
        public string LdapAttribute { get; set; } = string.Empty;

        /// <summary>
        /// Which object type this column belongs to
        /// </summary>
        public SearchObjectType ObjectType { get; set; }

        public SearchColumnOption(string displayName, string ldapAttribute, SearchObjectType objectType, bool isSelected = true)
        {
            DisplayName = displayName;
            LdapAttribute = ldapAttribute;
            ObjectType = objectType;
            IsSelected = isSelected;
        }
    }

    /// <summary>
    /// Represents a header in the column selection dropdown
    /// </summary>
    public class SearchColumnHeader
    {
        public string HeaderText { get; set; } = string.Empty;
        public SearchObjectType ObjectType { get; set; }

        public SearchColumnHeader(string headerText, SearchObjectType objectType)
        {
            HeaderText = headerText;
            ObjectType = objectType;
        }
    }

    /// <summary>
    /// Union type for column dropdown items (either header or column option)
    /// </summary>
    public class SearchColumnItem
    {
        public bool IsHeader { get; set; }
        public SearchColumnHeader? Header { get; set; }
        public SearchColumnOption? Column { get; set; }

        public static SearchColumnItem CreateHeader(string text, SearchObjectType objectType)
            => new() { IsHeader = true, Header = new SearchColumnHeader(text, objectType) };

        public static SearchColumnItem CreateColumn(SearchColumnOption column)
            => new() { IsHeader = false, Column = column };
    }

    public enum SearchObjectType
    {
        Computer,
        User,
        Printer
    }

    /// <summary>
    /// Static helper to get default column options for each type
    /// </summary>
    public static class SearchColumnDefaults
    {
        public static List<SearchColumnOption> GetComputerColumns()
        {
            return new List<SearchColumnOption>
            {
                new("Name", "name", SearchObjectType.Computer),
                new("DNS Hostname", "dNSHostName", SearchObjectType.Computer),
                new("Description", "description", SearchObjectType.Computer),
                // Note: operatingSystem, location, managedBy are less commonly indexed
                // and may not work well with wildcard searches in all AD environments
                new("Operating System", "operatingSystem", SearchObjectType.Computer),
                new("Location", "location", SearchObjectType.Computer),
            };
        }

        public static List<SearchColumnOption> GetUserColumns()
        {
            return new List<SearchColumnOption>
            {
                new("Name", "name", SearchObjectType.User),
                new("Username", "sAMAccountName", SearchObjectType.User),
                new("Display Name", "displayName", SearchObjectType.User),
                new("Email", "mail", SearchObjectType.User),
                new("Title", "title", SearchObjectType.User),
                new("Department", "department", SearchObjectType.User),
                new("Description", "description", SearchObjectType.User),
                new("Company", "company", SearchObjectType.User),
                new("Office", "physicalDeliveryOfficeName", SearchObjectType.User),
                new("Phone", "telephoneNumber", SearchObjectType.User)
            };
        }

        public static List<SearchColumnOption> GetPrinterColumns()
        {
            return new List<SearchColumnOption>
            {
                new("Name", "name", SearchObjectType.Printer),
                new("Printer Name", "printerName", SearchObjectType.Printer),
                new("Description", "description", SearchObjectType.Printer),
                new("Server Name", "serverName", SearchObjectType.Printer),
                new("Share Name", "printShareName", SearchObjectType.Printer),
                new("Location", "location", SearchObjectType.Printer),
                new("Driver Name", "driverName", SearchObjectType.Printer),
                new("Port Name", "portName", SearchObjectType.Printer)
            };
        }

        public static List<SearchColumnOption> GetAllColumns()
        {
            var all = new List<SearchColumnOption>();
            all.AddRange(GetComputerColumns());
            all.AddRange(GetUserColumns());
            all.AddRange(GetPrinterColumns());
            return all;
        }
    }
}
