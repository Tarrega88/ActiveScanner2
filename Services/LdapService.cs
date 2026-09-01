using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActiveScanner.Models;

namespace ActiveScanner.Services
{
    /// <summary>
    /// Service for LDAP/Active Directory operations
    /// </summary>
    public class LdapService
    {
        private const int ADS_UF_ACCOUNTDISABLE = 0x0002;

        /// <summary>
        /// Creates an LDAP DirectoryEntry connection
        /// </summary>
        public DirectoryEntry CreateEntry(string dn, string? server = null, string? username = null, 
            string? password = null, bool useLdaps = false)
        {
            var prefix = server != null
                ? (useLdaps ? $"LDAP://{server}:636/" : $"LDAP://{server}/")
                : "LDAP://";

            var path = $"{prefix}{dn}";

            return (username != null && password != null)
                ? new DirectoryEntry(path, username, password)
                : new DirectoryEntry(path);
        }

        /// <summary>
        /// Gets child folders (OUs and Containers) under the given entry
        /// </summary>
        public async Task<List<FolderItem>> GetChildFoldersAsync(DirectoryEntry entry, 
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var list = new List<FolderItem>();

                foreach (DirectoryEntry child in entry.Children)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var schemaClass = child.SchemaClassName?.ToLower();
                    if (schemaClass == "organizationalunit" || schemaClass == "container")
                    {
                        string? displayName = null;
                        string? dn = null;

                        try
                        {
                            displayName = child.Properties["name"]?.Value?.ToString();
                            dn = child.Properties["distinguishedName"]?.Value?.ToString();
                        }
                        catch { }

                        if (string.IsNullOrEmpty(displayName))
                        {
                            displayName = child.Name;
                        }

                        if (!string.IsNullOrEmpty(dn))
                        {
                            list.Add(new FolderItem
                            {
                                Name = displayName ?? "Unknown",
                                DistinguishedName = dn,
                                Type = child.SchemaClassName ?? "unknown"
                            });
                        }
                    }
                }

                return list.OrderBy(f => f.Type).ThenBy(f => f.Name).ToList();
            }, cancellationToken);
        }

        /// <summary>
        /// Gets all properties for a computer by its distinguished name
        /// </summary>
        public async Task<Dictionary<string, object?>> GetAllPropertiesAsync(string distinguishedName,
            string? server = null, string? username = null, string? password = null, bool useLdaps = false,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var properties = new Dictionary<string, object?>();

                using var entry = CreateEntry(distinguishedName, server, username, password, useLdaps);

                // Force refresh to get all properties
                entry.RefreshCache();

                // Check if PropertyNames is available
                if (entry.Properties?.PropertyNames == null)
                {
                    return properties;
                }

                foreach (string propName in entry.Properties.PropertyNames)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var propCollection = entry.Properties[propName];
                    if (propCollection == null || propCollection.Count == 0)
                    {
                        properties[propName] = null;
                    }
                    else if (propCollection.Count == 1)
                    {
                        properties[propName] = propCollection[0];
                    }
                    else
                    {
                        // Multi-valued property - collect all values
                        var values = new List<object?>();
                        foreach (var val in propCollection)
                        {
                            values.Add(val);
                        }
                        properties[propName] = values;
                    }
                }

                return properties;
            }, cancellationToken);
        }

        /// <summary>
        /// Queries computer objects under the given entry
        /// </summary>
        public async Task<List<AdObjectInfo>> QueryComputersAsync(DirectoryEntry baseEntry, 
            SearchScope scope, ColumnSettings columns, string? sourcePath = null,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var results = new List<AdObjectInfo>();

                using var searcher = new DirectorySearcher(baseEntry)
                {
                    Filter = "(objectClass=computer)",
                    PageSize = 1000,
                    SearchScope = scope
                };

                // Load only requested properties
                foreach (var prop in columns.GetLdapProperties())
                {
                    searcher.PropertiesToLoad.Add(prop);
                }

                progress?.Report("Searching for computers...");

                SearchResultCollection? searchResults = null;
                try
                {
                    searchResults = searcher.FindAll();

                    var count = 0;
                    foreach (SearchResult result in searchResults)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var computer = new AdObjectInfo
                        {
                            Name = GetPropertyValue(result, "name") ?? "Unknown",
                            Type = "Computer",
                            SourcePath = sourcePath,
                            ObjectGuid = GetPropertyValueGuid(result, "objectGUID")
                        };

                        if (columns.DnsHostName)
                            computer.DnsHostName = GetPropertyValue(result, "dNSHostName");

                        if (columns.Description)
                            computer.Description = GetPropertyValue(result, "description");

                        if (columns.OperatingSystem)
                            computer.OperatingSystem = GetPropertyValue(result, "operatingSystem");

                        if (columns.OperatingSystemVersion)
                            computer.OperatingSystemVersion = GetPropertyValue(result, "operatingSystemVersion");

                        if (columns.IsEnabled)
                        {
                            var uac = GetPropertyValueInt(result, "userAccountControl");
                            computer.IsEnabled = uac.HasValue && (uac.Value & ADS_UF_ACCOUNTDISABLE) == 0;
                        }

                        if (columns.LastLogon)
                            computer.LastLogon = GetPropertyValueDateTime(result, "lastLogonTimestamp");

                        if (columns.WhenCreated)
                            computer.WhenCreated = GetPropertyValueDateTime(result, "whenCreated");

                        if (columns.WhenChanged)
                            computer.WhenChanged = GetPropertyValueDateTime(result, "whenChanged");

                        if (columns.Location)
                            computer.Location = GetPropertyValue(result, "location");

                        if (columns.ManagedBy)
                            computer.ManagedBy = GetPropertyValue(result, "managedBy");

                        if (columns.DistinguishedName || columns.SourcePath)
                            computer.DistinguishedName = GetPropertyValue(result, "distinguishedName");

                        results.Add(computer);
                        count++;

                        if (count % 100 == 0)
                        {
                            progress?.Report($"Found {count} computers...");
                        }
                    }

                    progress?.Report($"Query complete: {results.Count} computers found");
                }
                finally
                {
                    searchResults?.Dispose();
                }

                return results;
            }, cancellationToken);
        }

        /// <summary>
        /// Searches for computers by name pattern across the given entry
        /// </summary>
        public async Task<List<AdObjectInfo>> SearchComputersAsync(DirectoryEntry baseEntry,
            string searchPattern, SearchScope scope, ColumnSettings columns, bool includeDisabled = true,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            // Backwards compatible overload - uses default columns
            return await SearchComputersAsync(baseEntry, searchPattern, scope, columns, includeDisabled, 
                null, progress, cancellationToken);
        }

        public async Task<List<AdObjectInfo>> SearchComputersAsync(DirectoryEntry baseEntry,
            string searchPattern, SearchScope scope, ColumnSettings columns, bool includeDisabled = true,
            IEnumerable<string>? searchColumns = null,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var results = new List<AdObjectInfo>();

                // Prepare pattern for "contains" matching (auto-wraps with wildcards)
                var ldapPattern = PrepareSearchPattern(searchPattern);

                // Build filter based on selected columns or default columns
                var columnsToSearch = searchColumns?.ToList() ?? new List<string> 
                { 
                    "name", "dNSHostName", "description" 
                };

                var columnFilters = columnsToSearch.Select(col => $"({col}={ldapPattern})");
                var columnFilterStr = $"(|{string.Join("", columnFilters)})";

                var filter = includeDisabled
                    ? $"(&(objectClass=computer){columnFilterStr})"
                    : $"(&(objectClass=computer)(!(userAccountControl:1.2.840.113556.1.4.803:=2)){columnFilterStr})";

                using var searcher = new DirectorySearcher(baseEntry)
                {
                    Filter = filter,
                    PageSize = 1000,
                    SearchScope = scope
                };

                foreach (var prop in columns.GetLdapProperties())
                {
                    searcher.PropertiesToLoad.Add(prop);
                }

                progress?.Report($"Searching for '{searchPattern}'...");

                SearchResultCollection? searchResults = null;
                try
                {
                    searchResults = searcher.FindAll();

                    foreach (SearchResult result in searchResults)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var computer = new AdObjectInfo
                        {
                            Name = GetPropertyValue(result, "name") ?? "Unknown",
                            Type = "Computer",
                            ObjectGuid = GetPropertyValueGuid(result, "objectGUID")
                        };

                        if (columns.DnsHostName)
                            computer.DnsHostName = GetPropertyValue(result, "dNSHostName");

                        if (columns.Description)
                            computer.Description = GetPropertyValue(result, "description");

                        if (columns.OperatingSystem)
                            computer.OperatingSystem = GetPropertyValue(result, "operatingSystem");

                        if (columns.OperatingSystemVersion)
                            computer.OperatingSystemVersion = GetPropertyValue(result, "operatingSystemVersion");

                        if (columns.IsEnabled)
                        {
                            var uac = GetPropertyValueInt(result, "userAccountControl");
                            computer.IsEnabled = uac.HasValue && (uac.Value & ADS_UF_ACCOUNTDISABLE) == 0;
                        }

                        if (columns.LastLogon)
                            computer.LastLogon = GetPropertyValueDateTime(result, "lastLogonTimestamp");

                        if (columns.WhenCreated)
                            computer.WhenCreated = GetPropertyValueDateTime(result, "whenCreated");

                        if (columns.WhenChanged)
                            computer.WhenChanged = GetPropertyValueDateTime(result, "whenChanged");

                        if (columns.Location)
                            computer.Location = GetPropertyValue(result, "location");

                        if (columns.ManagedBy)
                            computer.ManagedBy = GetPropertyValue(result, "managedBy");

                        if (columns.DistinguishedName || columns.SourcePath)
                            computer.DistinguishedName = GetPropertyValue(result, "distinguishedName");

                        results.Add(computer);
                    }

                    progress?.Report($"Search complete: {results.Count} computers found");
                }
                finally
                {
                    searchResults?.Dispose();
                }

                return results;
            }, cancellationToken);
        }

        /// <summary>
        /// Queries user objects under the given entry
        /// </summary>
        public async Task<List<AdObjectInfo>> QueryUsersAsync(DirectoryEntry baseEntry,
            SearchScope scope, ColumnSettings columns, string? sourcePath = null,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var results = new List<AdObjectInfo>();

                using var searcher = new DirectorySearcher(baseEntry)
                {
                    // Filter for user objects, excluding computer accounts
                    Filter = "(&(objectCategory=person)(objectClass=user))",
                    PageSize = 1000,
                    SearchScope = scope
                };

                foreach (var prop in columns.GetUserLdapProperties())
                {
                    searcher.PropertiesToLoad.Add(prop);
                }

                progress?.Report("Searching for users...");

                SearchResultCollection? searchResults = null;
                try
                {
                    searchResults = searcher.FindAll();

                    var count = 0;
                    foreach (SearchResult result in searchResults)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var user = new AdObjectInfo
                        {
                            Type = "Person",  // Users have objectCategory=person
                            Name = GetPropertyValue(result, "name") ?? "Unknown",
                            SourcePath = sourcePath,
                            ObjectGuid = GetPropertyValueGuid(result, "objectGUID")
                        };

                        if (columns.SamAccountName)
                            user.SamAccountName = GetPropertyValue(result, "sAMAccountName");

                        if (columns.UserPrincipalName)
                            user.UserPrincipalName = GetPropertyValue(result, "userPrincipalName");

                        if (columns.DisplayName)
                            user.DisplayName = GetPropertyValue(result, "displayName");

                        if (columns.GivenName)
                            user.GivenName = GetPropertyValue(result, "givenName");

                        if (columns.Surname)
                            user.Surname = GetPropertyValue(result, "sn");

                        if (columns.Description)
                            user.Description = GetPropertyValue(result, "description");

                        if (columns.Mail)
                            user.Mail = GetPropertyValue(result, "mail");

                        if (columns.Title)
                            user.Title = GetPropertyValue(result, "title");

                        if (columns.Department)
                            user.Department = GetPropertyValue(result, "department");

                        if (columns.Company)
                            user.Company = GetPropertyValue(result, "company");

                        if (columns.Manager)
                        {
                            var managerDn = GetPropertyValue(result, "manager");
                            user.Manager = ExtractCnFromDn(managerDn);
                        }

                        if (columns.TelephoneNumber)
                            user.TelephoneNumber = GetPropertyValue(result, "telephoneNumber");

                        if (columns.Mobile)
                            user.Mobile = GetPropertyValue(result, "mobile");

                        if (columns.Office)
                            user.Office = GetPropertyValue(result, "physicalDeliveryOfficeName");

                        if (columns.IsEnabled)
                        {
                            var uac = GetPropertyValueInt(result, "userAccountControl");
                            user.IsEnabled = uac.HasValue && (uac.Value & ADS_UF_ACCOUNTDISABLE) == 0;
                            user.PasswordNeverExpires = uac.HasValue && (uac.Value & ADS_UF_DONT_EXPIRE_PASSWD) != 0;
                            user.PasswordExpired = uac.HasValue && (uac.Value & ADS_UF_PASSWORD_EXPIRED) != 0;
                            user.LockedOut = uac.HasValue && (uac.Value & ADS_UF_LOCKOUT) != 0;
                        }

                        if (columns.LastLogon)
                            user.LastLogon = GetPropertyValueDateTime(result, "lastLogonTimestamp");

                        if (columns.PasswordLastSet)
                            user.PasswordLastSet = GetPropertyValueDateTime(result, "pwdLastSet");

                        if (columns.AccountExpires)
                            user.AccountExpires = GetPropertyValueDateTime(result, "accountExpires");

                        if (columns.WhenCreated)
                            user.WhenCreated = GetPropertyValueDateTime(result, "whenCreated");

                        if (columns.WhenChanged)
                            user.WhenChanged = GetPropertyValueDateTime(result, "whenChanged");

                        if (columns.ManagedBy)
                        {
                            var managedByDn = GetPropertyValue(result, "managedBy");
                            user.ManagedBy = ExtractCnFromDn(managedByDn);
                        }

                        if (columns.DistinguishedName || columns.SourcePath)
                            user.DistinguishedName = GetPropertyValue(result, "distinguishedName");

                        results.Add(user);
                        count++;

                        if (count % 100 == 0)
                        {
                            progress?.Report($"Found {count} users...");
                        }
                    }

                    progress?.Report($"Query complete: {results.Count} users found");
                }
                finally
                {
                    searchResults?.Dispose();
                }

                return results;
            }, cancellationToken);
        }

        /// <summary>
        /// Batch lookup of mail/displayName for a set of sAMAccountNames using a single
        /// indexed LDAP filter (chunked to keep filter size sane). Subtree scope from
        /// <paramref name="baseEntry"/>. Returns only the SAMs that were found in AD;
        /// callers can treat absent keys as "not found".
        /// </summary>
        public async Task<Dictionary<string, (string? Mail, string? DisplayName)>> GetUsersBySamBatchAsync(
            DirectoryEntry baseEntry,
            IEnumerable<string> samAccountNames,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, (string? Mail, string? DisplayName)>(StringComparer.OrdinalIgnoreCase);
            var sams = samAccountNames
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sams.Count == 0)
                return result;

            return await Task.Run(() =>
            {
                const int chunkSize = 100;
                for (int i = 0; i < sams.Count; i += chunkSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var chunk = sams.GetRange(i, Math.Min(chunkSize, sams.Count - i));

                    var orClauses = string.Concat(chunk.Select(s =>
                        $"(sAMAccountName={EscapeLdapValueLiteral(s)})"));
                    var filter = $"(&(objectCategory=person)(objectClass=user)(|{orClauses}))";

                    using var searcher = new DirectorySearcher(baseEntry)
                    {
                        Filter = filter,
                        PageSize = 1000,
                        SearchScope = SearchScope.Subtree
                    };
                    searcher.PropertiesToLoad.Add("sAMAccountName");
                    searcher.PropertiesToLoad.Add("mail");
                    searcher.PropertiesToLoad.Add("displayName");

                    SearchResultCollection? found = null;
                    try
                    {
                        found = searcher.FindAll();
                        foreach (SearchResult r in found)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var sam = GetPropertyValue(r, "sAMAccountName");
                            if (string.IsNullOrEmpty(sam))
                                continue;
                            var mail = GetPropertyValue(r, "mail");
                            var dn = GetPropertyValue(r, "displayName");
                            result[sam] = (mail, dn);
                        }
                    }
                    finally
                    {
                        found?.Dispose();
                    }
                }

                return result;
            }, cancellationToken);
        }

        /// <summary>
        /// Forest-wide batch lookup of mail/displayName by sAMAccountName via the Global
        /// Catalog. Used by the Last User email fallback so we don't have to know which
        /// domain each user lives in. The GC partial attribute set includes
        /// sAMAccountName, mail, and displayName, so a single subtree search covers the
        /// entire forest in one indexed query.
        /// </summary>
        public async Task<Dictionary<string, (string? Mail, string? DisplayName)>> GetUsersBySamBatchViaGlobalCatalogAsync(
            IEnumerable<string> samAccountNames,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, (string? Mail, string? DisplayName)>(StringComparer.OrdinalIgnoreCase);

            var sams = samAccountNames
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sams.Count == 0)
                return result;

            return await Task.Run(() =>
            {
                // Locate a GC root for the current forest. "GC:" enumerates available GC
                // bindings; we take the first child as our search root.
                DirectoryEntry? gcRoot = null;
                try
                {
                    using var gcContainer = new DirectoryEntry("GC:");
                    foreach (DirectoryEntry child in gcContainer.Children)
                    {
                        gcRoot = child;
                        break;
                    }

                    if (gcRoot == null)
                        return result;

                    const int chunkSize = 100;
                    for (int i = 0; i < sams.Count; i += chunkSize)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var chunk = sams.GetRange(i, Math.Min(chunkSize, sams.Count - i));

                        var orClauses = string.Concat(chunk.Select(s =>
                            $"(sAMAccountName={EscapeLdapValueLiteral(s)})"));
                        var filter = $"(&(objectCategory=person)(objectClass=user)(|{orClauses}))";

                        using var searcher = new DirectorySearcher(gcRoot)
                        {
                            Filter = filter,
                            PageSize = 1000,
                            SearchScope = SearchScope.Subtree
                        };
                        searcher.PropertiesToLoad.Add("sAMAccountName");
                        searcher.PropertiesToLoad.Add("mail");
                        searcher.PropertiesToLoad.Add("displayName");

                        SearchResultCollection? found = null;
                        try
                        {
                            found = searcher.FindAll();
                            foreach (SearchResult r in found)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var sam = GetPropertyValue(r, "sAMAccountName");
                                if (string.IsNullOrEmpty(sam))
                                    continue;
                                var mail = GetPropertyValue(r, "mail");
                                var dn = GetPropertyValue(r, "displayName");
                                // First hit wins; the GC may return one row per domain replica.
                                if (!result.ContainsKey(sam))
                                    result[sam] = (mail, dn);
                            }
                        }
                        finally
                        {
                            found?.Dispose();
                        }
                    }
                }
                finally
                {
                    gcRoot?.Dispose();
                }

                return result;
            }, cancellationToken);
        }

        /// <summary>
        /// Escapes special characters for use as a literal LDAP filter value (RFC 4515).
        /// Unlike <see cref="EscapeLdapSearchFilter"/>, this also escapes <c>*</c> so the
        /// value is treated as a literal rather than a wildcard.
        /// </summary>
        private static string EscapeLdapValueLiteral(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            return value
                .Replace("\\", "\\5c")
                .Replace("*", "\\2a")
                .Replace("(", "\\28")
                .Replace(")", "\\29")
                .Replace("\0", "\\00");
        }

        /// <summary>
        /// Queries printer objects under the given entry
        /// </summary>
        public async Task<List<AdObjectInfo>> QueryPrintersAsync(DirectoryEntry baseEntry,
            SearchScope scope, ColumnSettings columns, string? sourcePath = null,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var results = new List<AdObjectInfo>();

                using var searcher = new DirectorySearcher(baseEntry)
                {
                    // Filter for printer objects (printQueue class)
                    Filter = "(objectClass=printQueue)",
                    PageSize = 1000,
                    SearchScope = scope
                };

                foreach (var prop in columns.GetPrinterLdapProperties())
                {
                    searcher.PropertiesToLoad.Add(prop);
                }

                progress?.Report("Searching for printers...");

                SearchResultCollection? searchResults = null;
                try
                {
                    searchResults = searcher.FindAll();

                    var count = 0;
                    foreach (SearchResult result in searchResults)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var printer = new AdObjectInfo
                        {
                            Type = "Printer",
                            Name = GetPropertyValue(result, "name") ?? "Unknown",
                            SourcePath = sourcePath,
                            ObjectGuid = GetPropertyValueGuid(result, "objectGUID")
                        };

                        if (columns.PrinterName)
                            printer.PrinterName = GetPropertyValue(result, "printerName");

                        if (columns.Description)
                            printer.Description = GetPropertyValue(result, "description");

                        if (columns.ServerName)
                            printer.ServerName = GetPropertyValue(result, "serverName");

                        if (columns.ShareName)
                            printer.ShareName = GetPropertyValue(result, "printShareName");

                        // Always load portName for IP resolution
                        printer.PortName = GetPropertyValue(result, "portName");

                        if (columns.DriverName)
                        {
                            var driverName = GetPropertyValue(result, "driverName");
                            printer.DriverName = driverName;
                            printer.PrinterModel = driverName; // Driver name often contains model info
                        }

                        if (columns.UNCName)
                            printer.UNCName = GetPropertyValue(result, "uNCName");

                        if (columns.Location)
                            printer.Location = GetPropertyValue(result, "location");

                        if (columns.PrinterPriority)
                            printer.PrinterPriority = GetPropertyValueInt(result, "priority");

                        if (columns.WhenCreated)
                            printer.WhenCreated = GetPropertyValueDateTime(result, "whenCreated");

                        if (columns.WhenChanged)
                            printer.WhenChanged = GetPropertyValueDateTime(result, "whenChanged");

                        if (columns.DistinguishedName || columns.SourcePath)
                            printer.DistinguishedName = GetPropertyValue(result, "distinguishedName");

                        // Printers don't have userAccountControl, so they're always "enabled"
                        printer.IsEnabled = true;

                        results.Add(printer);
                        count++;

                        if (count % 100 == 0)
                        {
                            progress?.Report($"Found {count} printers...");
                        }
                    }

                    progress?.Report($"Query complete: {results.Count} printers found");
                }
                finally
                {
                    searchResults?.Dispose();
                }

                return results;
            }, cancellationToken);
        }

        /// <summary>
        /// Searches for printers by pattern across the given entry
        /// </summary>
        public async Task<List<AdObjectInfo>> SearchPrintersAsync(DirectoryEntry baseEntry,
            string searchPattern, SearchScope scope, ColumnSettings columns, 
            IEnumerable<string>? searchColumns = null,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var results = new List<AdObjectInfo>();

                // Prepare pattern for "contains" matching (auto-wraps with wildcards)
                var ldapPattern = PrepareSearchPattern(searchPattern);

                // Build filter based on selected columns or default columns
                var columnsToSearch = searchColumns?.ToList() ?? new List<string> 
                { 
                    "name", "printerName", "description", "serverName", "location" 
                };

                var columnFilters = columnsToSearch.Select(col => $"({col}={ldapPattern})");
                var filter = $"(&(objectClass=printQueue)(|{string.Join("", columnFilters)}))";

                using var searcher = new DirectorySearcher(baseEntry)
                {
                    Filter = filter,
                    PageSize = 1000,
                    SearchScope = scope
                };

                foreach (var prop in columns.GetPrinterLdapProperties())
                {
                    searcher.PropertiesToLoad.Add(prop);
                }

                progress?.Report($"Searching for printers matching '{searchPattern}'...");

                SearchResultCollection? searchResults = null;
                try
                {
                    searchResults = searcher.FindAll();

                    foreach (SearchResult result in searchResults)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var printer = new AdObjectInfo
                        {
                            Type = "Printer",
                            Name = GetPropertyValue(result, "name") ?? "Unknown",
                            ObjectGuid = GetPropertyValueGuid(result, "objectGUID"),
                            PrinterName = GetPropertyValue(result, "printerName"),
                            Description = GetPropertyValue(result, "description"),
                            ServerName = GetPropertyValue(result, "serverName"),
                            ShareName = GetPropertyValue(result, "printShareName"),
                            Location = GetPropertyValue(result, "location"),
                            DriverName = GetPropertyValue(result, "driverName"),
                            PortName = GetPropertyValue(result, "portName"),
                            UNCName = GetPropertyValue(result, "uNCName"),
                            DistinguishedName = GetPropertyValue(result, "distinguishedName")
                        };

                        printer.PrinterModel = printer.DriverName;
                        printer.IsEnabled = true;

                        results.Add(printer);
                    }

                    progress?.Report($"Search complete: {results.Count} printers found");
                }
                finally
                {
                    searchResults?.Dispose();
                }

                return results;
            }, cancellationToken);
        }

        /// <summary>
        /// Searches for users by name pattern across the given entry
        /// </summary>
        public async Task<List<AdObjectInfo>> SearchUsersAsync(DirectoryEntry baseEntry,
            string searchPattern, SearchScope scope, ColumnSettings columns, bool includeDisabled = true,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            // Backwards compatible overload - uses default columns
            return await SearchUsersAsync(baseEntry, searchPattern, scope, columns, includeDisabled,
                null, progress, cancellationToken);
        }

        public async Task<List<AdObjectInfo>> SearchUsersAsync(DirectoryEntry baseEntry,
            string searchPattern, SearchScope scope, ColumnSettings columns, bool includeDisabled = true,
            IEnumerable<string>? searchColumns = null,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var results = new List<AdObjectInfo>();

                // Prepare pattern for "contains" matching (auto-wraps with wildcards)
                var ldapPattern = PrepareSearchPattern(searchPattern);

                // Build filter based on selected columns or default columns
                var columnsToSearch = searchColumns?.ToList() ?? new List<string> 
                { 
                    "name", "sAMAccountName", "displayName", "mail" 
                };

                var columnFilters = columnsToSearch.Select(col => $"({col}={ldapPattern})");
                var columnFilterStr = $"(|{string.Join("", columnFilters)})";

                var filter = includeDisabled
                    ? $"(&(objectCategory=person)(objectClass=user){columnFilterStr})"
                    : $"(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2)){columnFilterStr})";

                using var searcher = new DirectorySearcher(baseEntry)
                {
                    Filter = filter,
                    PageSize = 1000,
                    SearchScope = scope
                };

                foreach (var prop in columns.GetUserLdapProperties())
                {
                    searcher.PropertiesToLoad.Add(prop);
                }

                progress?.Report($"Searching for users matching '{searchPattern}'...");

                SearchResultCollection? searchResults = null;
                try
                {
                    searchResults = searcher.FindAll();

                    foreach (SearchResult result in searchResults)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var user = new AdObjectInfo
                        {
                            Type = "Person",  // Users have objectCategory=person
                            Name = GetPropertyValue(result, "name") ?? "Unknown",
                            ObjectGuid = GetPropertyValueGuid(result, "objectGUID")
                        };

                        if (columns.SamAccountName)
                            user.SamAccountName = GetPropertyValue(result, "sAMAccountName");

                        if (columns.UserPrincipalName)
                            user.UserPrincipalName = GetPropertyValue(result, "userPrincipalName");

                        if (columns.DisplayName)
                            user.DisplayName = GetPropertyValue(result, "displayName");

                        if (columns.GivenName)
                            user.GivenName = GetPropertyValue(result, "givenName");

                        if (columns.Surname)
                            user.Surname = GetPropertyValue(result, "sn");

                        if (columns.Description)
                            user.Description = GetPropertyValue(result, "description");

                        if (columns.Mail)
                            user.Mail = GetPropertyValue(result, "mail");

                        if (columns.Title)
                            user.Title = GetPropertyValue(result, "title");

                        if (columns.Department)
                            user.Department = GetPropertyValue(result, "department");

                        if (columns.Company)
                            user.Company = GetPropertyValue(result, "company");

                        if (columns.Manager)
                        {
                            var managerDn = GetPropertyValue(result, "manager");
                            user.Manager = ExtractCnFromDn(managerDn);
                        }

                        if (columns.TelephoneNumber)
                            user.TelephoneNumber = GetPropertyValue(result, "telephoneNumber");

                        if (columns.Mobile)
                            user.Mobile = GetPropertyValue(result, "mobile");

                        if (columns.Office)
                            user.Office = GetPropertyValue(result, "physicalDeliveryOfficeName");

                        if (columns.IsEnabled)
                        {
                            var uac = GetPropertyValueInt(result, "userAccountControl");
                            user.IsEnabled = uac.HasValue && (uac.Value & ADS_UF_ACCOUNTDISABLE) == 0;
                            user.PasswordNeverExpires = uac.HasValue && (uac.Value & ADS_UF_DONT_EXPIRE_PASSWD) != 0;
                            user.PasswordExpired = uac.HasValue && (uac.Value & ADS_UF_PASSWORD_EXPIRED) != 0;
                            user.LockedOut = uac.HasValue && (uac.Value & ADS_UF_LOCKOUT) != 0;
                        }

                        if (columns.LastLogon)
                            user.LastLogon = GetPropertyValueDateTime(result, "lastLogonTimestamp");

                        if (columns.PasswordLastSet)
                            user.PasswordLastSet = GetPropertyValueDateTime(result, "pwdLastSet");

                        if (columns.AccountExpires)
                            user.AccountExpires = GetPropertyValueDateTime(result, "accountExpires");

                        if (columns.WhenCreated)
                            user.WhenCreated = GetPropertyValueDateTime(result, "whenCreated");

                        if (columns.WhenChanged)
                            user.WhenChanged = GetPropertyValueDateTime(result, "whenChanged");

                        if (columns.ManagedBy)
                        {
                            var managedByDn = GetPropertyValue(result, "managedBy");
                            user.ManagedBy = ExtractCnFromDn(managedByDn);
                        }

                        if (columns.DistinguishedName || columns.SourcePath)
                            user.DistinguishedName = GetPropertyValue(result, "distinguishedName");

                        results.Add(user);
                    }

                    progress?.Report($"Search complete: {results.Count} users found");
                }
                finally
                {
                    searchResults?.Dispose();
                }

                return results;
            }, cancellationToken);
        }

        /// <summary>
        /// Extracts the CN (Common Name) from a Distinguished Name
        /// </summary>
        private static string? ExtractCnFromDn(string? dn)
        {
            if (string.IsNullOrEmpty(dn)) return null;
            
            // DN format: CN=LastName\, FirstName,OU=Users,DC=domain,DC=com
            // Need to handle escaped commas (\,) in the CN value
            if (!dn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                return dn;
            
            var cnValue = dn.Substring(3);
            
            // Find the first unescaped comma (not preceded by backslash)
            int endIndex = -1;
            for (int i = 0; i < cnValue.Length; i++)
            {
                if (cnValue[i] == ',' && (i == 0 || cnValue[i - 1] != '\\'))
                {
                    endIndex = i;
                    break;
                }
            }
            
            if (endIndex > 0)
            {
                cnValue = cnValue.Substring(0, endIndex);
            }
            
            // Unescape backslash-comma to just comma
            return cnValue.Replace("\\,", ",");
        }

        // Additional UAC flags for user properties
        private const int ADS_UF_DONT_EXPIRE_PASSWD = 0x10000;
        private const int ADS_UF_PASSWORD_EXPIRED = 0x800000;
        private const int ADS_UF_LOCKOUT = 0x0010;

        private static string? GetPropertyValue(SearchResult result, string propertyName)
        {
            // LDAP property names are case-insensitive, but ResultPropertyCollection may be case-sensitive
            // Try multiple case variations
            string[] variations = { propertyName, propertyName.ToLowerInvariant(), propertyName.ToUpperInvariant() };
            
            foreach (var name in variations)
            {
                if (result.Properties.Contains(name) && result.Properties[name].Count > 0)
                {
                    return result.Properties[name][0]?.ToString();
                }
            }
            
            // Last resort: iterate through all properties to find case-insensitive match
            foreach (string propName in result.Properties.PropertyNames)
            {
                if (string.Equals(propName, propertyName, StringComparison.OrdinalIgnoreCase) && 
                    result.Properties[propName].Count > 0)
                {
                    return result.Properties[propName][0]?.ToString();
                }
            }
            
            return null;
        }

        private static int? GetPropertyValueInt(SearchResult result, string propertyName)
        {
            var value = GetPropertyValue(result, propertyName);
            if (int.TryParse(value, out var intValue))
            {
                return intValue;
            }
            return null;
        }

        private static DateTime? GetPropertyValueDateTime(SearchResult result, string propertyName)
        {
            if (result.Properties.Contains(propertyName) && result.Properties[propertyName].Count > 0)
            {
                var value = result.Properties[propertyName][0];

                // Handle COM object for lastLogonTimestamp (returns as Int64/long)
                if (value is long longValue && longValue > 0)
                {
                    try
                    {
                        return DateTime.FromFileTimeUtc(longValue);
                    }
                    catch { }
                }

                // Handle DateTime directly
                if (value is DateTime dtValue)
                {
                    return dtValue.Kind == DateTimeKind.Local ? dtValue.ToUniversalTime() : dtValue;
                }
            }
            return null;
        }

        private static Guid? GetPropertyValueGuid(SearchResult result, string propertyName)
        {
            // objectGUID is stored as a byte array in AD
            string[] variations = { propertyName, propertyName.ToLowerInvariant(), propertyName.ToUpperInvariant() };
            
            foreach (var name in variations)
            {
                if (result.Properties.Contains(name) && result.Properties[name].Count > 0)
                {
                    var value = result.Properties[name][0];
                    if (value is byte[] guidBytes && guidBytes.Length == 16)
                    {
                        return new Guid(guidBytes);
                    }
                }
            }
            
            return null;
        }

        /// <summary>
        /// Escapes special characters in LDAP search filters while preserving wildcards (*)
        /// </summary>
        private static string EscapeLdapSearchFilter(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Escape special LDAP characters (RFC 4515)
            // Note: * is intentionally NOT escaped to allow wildcard searches
            return input
                .Replace("\\", "\\5c")  // Backslash must be escaped first
                .Replace("(", "\\28")
                .Replace(")", "\\29")
                .Replace("\0", "\\00");
        }

        /// <summary>
        /// Prepares a search pattern for LDAP "contains" matching.
        /// Automatically wraps with wildcards if not already present.
        /// </summary>
        private static string PrepareSearchPattern(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "*";

            // First escape special characters
            var escaped = EscapeLdapSearchFilter(input);

            // If user already included wildcards, use their pattern as-is
            if (escaped.Contains("*"))
                return escaped;

            // Otherwise, wrap with wildcards for "contains" matching
            return $"*{escaped}*";
        }

        /// <summary>
        /// Detects what type of AD objects are contained in a folder (OU/Container).
        /// Returns the primary object type found (Computers, Users, Printers, or None).
        /// </summary>
        public async Task<GroupObjectType> DetectFolderObjectTypeAsync(DirectoryEntry entry,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                // Check for computers
                using (var searcher = new DirectorySearcher(entry)
                {
                    Filter = "(objectClass=computer)",
                    SearchScope = SearchScope.OneLevel,
                    SizeLimit = 1
                })
                {
                    searcher.PropertiesToLoad.Add("name");
                    try
                    {
                        var result = searcher.FindOne();
                        if (result != null)
                            return GroupObjectType.Computers;
                    }
                    catch { }
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Check for users
                using (var searcher = new DirectorySearcher(entry)
                {
                    Filter = "(&(objectCategory=person)(objectClass=user))",
                    SearchScope = SearchScope.OneLevel,
                    SizeLimit = 1
                })
                {
                    searcher.PropertiesToLoad.Add("name");
                    try
                    {
                        var result = searcher.FindOne();
                        if (result != null)
                            return GroupObjectType.Users;
                    }
                    catch { }
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Check for printers
                using (var searcher = new DirectorySearcher(entry)
                {
                    Filter = "(objectClass=printQueue)",
                    SearchScope = SearchScope.OneLevel,
                    SizeLimit = 1
                })
                {
                    searcher.PropertiesToLoad.Add("name");
                    try
                    {
                        var result = searcher.FindOne();
                        if (result != null)
                            return GroupObjectType.Printers;
                    }
                    catch { }
                }

                return GroupObjectType.None;
            }, cancellationToken);
        }

        /// <summary>
        /// Gets an AD object by its distinguished name
        /// </summary>
        public AdObjectInfo? GetObjectByDN(string distinguishedName, string? domain = null)
        {
            try
            {
                // Use the domain as the server for faster targeted queries
                using var entry = CreateEntry(distinguishedName, server: domain);
                
                // Try to access the entry to verify it exists
                var nativeGuid = entry.NativeGuid;
                
                var objectClass = entry.SchemaClassName?.ToLower();
                var type = objectClass switch
                {
                    "computer" => "Computer",
                    "user" => "Person",
                    "printqueue" => "Printer",
                    _ => objectClass ?? "Unknown"
                };

                var obj = new AdObjectInfo
                {
                    Type = type,
                    Name = entry.Properties["name"]?.Value?.ToString() ?? "Unknown",
                    DistinguishedName = distinguishedName,
                    Description = entry.Properties["description"]?.Value?.ToString(),
                    ObjectGuid = entry.Properties["objectGUID"]?.Value is byte[] guidBytes && guidBytes.Length == 16 
                        ? new Guid(guidBytes) : null
                };

                // Load type-specific properties
                if (type == "Computer")
                {
                    obj.DnsHostName = entry.Properties["dNSHostName"]?.Value?.ToString();
                    obj.OperatingSystem = entry.Properties["operatingSystem"]?.Value?.ToString();
                    obj.OperatingSystemVersion = entry.Properties["operatingSystemVersion"]?.Value?.ToString();
                    obj.Location = entry.Properties["location"]?.Value?.ToString();
                    obj.ManagedBy = ExtractCnFromDn(entry.Properties["managedBy"]?.Value?.ToString());
                    
                    var uac = entry.Properties["userAccountControl"]?.Value;
                    if (uac != null && int.TryParse(uac.ToString(), out var uacValue))
                    {
                        obj.IsEnabled = (uacValue & ADS_UF_ACCOUNTDISABLE) == 0;
                    }
                    
                    obj.LastLogon = GetDateTimeFromLargeInteger(entry.Properties["lastLogonTimestamp"]?.Value);
                    obj.WhenCreated = entry.Properties["whenCreated"]?.Value as DateTime?;
                    obj.WhenChanged = entry.Properties["whenChanged"]?.Value as DateTime?;
                }
                else if (type == "Person")
                {
                    obj.SamAccountName = entry.Properties["sAMAccountName"]?.Value?.ToString();
                    obj.UserPrincipalName = entry.Properties["userPrincipalName"]?.Value?.ToString();
                    obj.DisplayName = entry.Properties["displayName"]?.Value?.ToString();
                    obj.GivenName = entry.Properties["givenName"]?.Value?.ToString();
                    obj.Surname = entry.Properties["sn"]?.Value?.ToString();
                    obj.Mail = entry.Properties["mail"]?.Value?.ToString();
                    obj.Title = entry.Properties["title"]?.Value?.ToString();
                    obj.Department = entry.Properties["department"]?.Value?.ToString();
                    obj.TelephoneNumber = entry.Properties["telephoneNumber"]?.Value?.ToString();
                    
                    var uac = entry.Properties["userAccountControl"]?.Value;
                    if (uac != null && int.TryParse(uac.ToString(), out var uacValue))
                    {
                        obj.IsEnabled = (uacValue & ADS_UF_ACCOUNTDISABLE) == 0;
                    }
                    
                    obj.LastLogon = GetDateTimeFromLargeInteger(entry.Properties["lastLogonTimestamp"]?.Value);
                    obj.WhenCreated = entry.Properties["whenCreated"]?.Value as DateTime?;
                    obj.WhenChanged = entry.Properties["whenChanged"]?.Value as DateTime?;
                }
                else if (type == "Printer")
                {
                    obj.PrinterName = entry.Properties["printerName"]?.Value?.ToString();
                    obj.ServerName = entry.Properties["serverName"]?.Value?.ToString();
                    obj.ShareName = entry.Properties["printShareName"]?.Value?.ToString();
                    obj.PortName = entry.Properties["portName"]?.Value?.ToString();
                    obj.DriverName = entry.Properties["driverName"]?.Value?.ToString();
                    obj.UNCName = entry.Properties["uNCName"]?.Value?.ToString();
                    obj.Location = entry.Properties["location"]?.Value?.ToString();
                }

                return obj;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Helper to convert IADsLargeInteger to DateTime
        /// </summary>
        private DateTime? GetDateTimeFromLargeInteger(object? value)
        {
            if (value == null) return null;
            
            try
            {
                if (value is long longValue)
                {
                    if (longValue <= 0 || longValue == 0x7FFFFFFFFFFFFFFF)
                        return null;
                    return DateTime.FromFileTimeUtc(longValue);
                }
                
                // Handle IADsLargeInteger COM object
                var type = value.GetType();
                var highPart = (int)type.InvokeMember("HighPart", 
                    System.Reflection.BindingFlags.GetProperty, null, value, null)!;
                var lowPart = (int)type.InvokeMember("LowPart", 
                    System.Reflection.BindingFlags.GetProperty, null, value, null)!;
                
                var fileTime = ((long)highPart << 32) + (uint)lowPart;
                if (fileTime <= 0 || fileTime == 0x7FFFFFFFFFFFFFFF)
                    return null;
                    
                return DateTime.FromFileTimeUtc(fileTime);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Discovers all available domain naming contexts from RootDSE
        /// </summary>
        /// <returns>List of discovered domains with their distinguished names</returns>
        public async Task<List<DomainConfig>> DiscoverDomainsAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var domains = new List<DomainConfig>();
                var seenDns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Method 1: Try forest domain enumeration (most comprehensive)
                try
                {
                    var forest = System.DirectoryServices.ActiveDirectory.Forest.GetCurrentForest();
                    foreach (System.DirectoryServices.ActiveDirectory.Domain domain in forest.Domains)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        var dn = DomainNameToDn(domain.Name);
                        if (!string.IsNullOrEmpty(dn) && seenDns.Add(dn))
                        {
                            domains.Add(new DomainConfig(domain.Name, dn, false));
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Forest enumeration failed: {ex.Message}");
                }

                // Method 2: RootDSE naming contexts (fallback / additional)
                try
                {
                    using var rootDse = new DirectoryEntry("LDAP://RootDSE");
                    
                    var namingContexts = rootDse.Properties["namingContexts"];
                    if (namingContexts != null)
                    {
                        foreach (var context in namingContexts)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            
                            var dn = context?.ToString();
                            if (string.IsNullOrEmpty(dn))
                                continue;

                            // Skip configuration, schema, and DNS partitions
                            if (dn.StartsWith("CN=Configuration", StringComparison.OrdinalIgnoreCase) ||
                                dn.StartsWith("CN=Schema", StringComparison.OrdinalIgnoreCase) ||
                                dn.StartsWith("DC=DomainDnsZones", StringComparison.OrdinalIgnoreCase) ||
                                dn.StartsWith("DC=ForestDnsZones", StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (seenDns.Add(dn))
                            {
                                var friendlyName = ConvertDnToFriendlyName(dn);
                                domains.Add(new DomainConfig(friendlyName, dn, false));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"RootDSE enumeration failed: {ex.Message}");
                }

                return domains.OrderBy(d => d.Name).ToList();
            }, cancellationToken);
        }

        /// <summary>
        /// Converts a domain name (e.g., v20.med.va.gov) to distinguished name (DC=v20,DC=med,DC=va,DC=gov)
        /// </summary>
        private static string DomainNameToDn(string domainName)
        {
            if (string.IsNullOrEmpty(domainName))
                return string.Empty;
                
            var parts = domainName.Split('.');
            return string.Join(",", parts.Select(p => $"DC={p}"));
        }

        /// <summary>
        /// Converts a distinguished name to a friendly domain name
        /// </summary>
        private static string ConvertDnToFriendlyName(string dn)
        {
            // Extract DC components and join with dots
            // e.g., DC=v20,DC=med,DC=va,DC=gov -> v20.med.va.gov
            var dcParts = new List<string>();
            var parts = dn.Split(',');
            
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                {
                    dcParts.Add(trimmed.Substring(3));
                }
            }

            return dcParts.Count > 0 ? string.Join(".", dcParts) : dn;
        }

        /// <summary>
        /// Updates the description attribute of an AD object.
        /// Requires write permissions (admin credentials).
        /// </summary>
        /// <param name="distinguishedName">The DN of the object to update</param>
        /// <param name="newDescription">The new description value (null or empty to clear)</param>
        /// <param name="server">Optional domain controller to target</param>
        /// <param name="username">Admin username (domain\user format)</param>
        /// <param name="password">Admin password</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result indicating success or failure with error message</returns>
        public async Task<(bool Success, string? Error)> UpdateDescriptionAsync(
            string distinguishedName,
            string? newDescription,
            string? server = null,
            string? username = null,
            string? password = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run<(bool, string?)>(() =>
            {
                try
                {
                    using var entry = CreateEntry(distinguishedName, server, username, password);
                    
                    // Force authentication by accessing NativeObject
                    var nativeObject = entry.NativeObject;
                    
                    if (string.IsNullOrEmpty(newDescription))
                    {
                        // Clear the description
                        if (entry.Properties.Contains("description"))
                        {
                            entry.Properties["description"].Clear();
                        }
                    }
                    else
                    {
                        // Set the new description
                        entry.Properties["description"].Value = newDescription;
                    }
                    
                    entry.CommitChanges();
                    return (true, null);
                }
                catch (UnauthorizedAccessException)
                {
                    return (false, "Access denied. You may not have permission to modify this object.");
                }
                catch (DirectoryServicesCOMException ex)
                {
                    // Parse common error codes
                    var errorCode = ex.ErrorCode;
                    
                    if (errorCode == unchecked((int)0x80072020))
                        return (false, "An operations error occurred. The object may not exist.");
                    if (errorCode == unchecked((int)0x8007202F))
                        return (false, "A constraint violation occurred. Check the description length or characters.");
                    if (errorCode == unchecked((int)0x80070005))
                        return (false, "Access denied. You may not have permission to modify this object.");
                    
                    return (false, $"Directory error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    return (false, $"Failed to update description: {ex.Message}");
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Enables or disables an AD account (computer or user) by modifying userAccountControl.
        /// </summary>
        /// <param name="distinguishedName">The DN of the object to modify</param>
        /// <param name="enabled">True to enable, false to disable</param>
        /// <param name="server">Optional domain controller to target</param>
        /// <param name="username">Admin username (domain\user format)</param>
        /// <param name="password">Admin password</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result indicating success or failure with error message</returns>
        public async Task<(bool Success, string? Error)> SetAccountEnabledAsync(
            string distinguishedName,
            bool enabled,
            string? server = null,
            string? username = null,
            string? password = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run<(bool, string?)>(() =>
            {
                try
                {
                    using var entry = CreateEntry(distinguishedName, server, username, password);
                    
                    // Force authentication by accessing NativeObject
                    var nativeObject = entry.NativeObject;
                    
                    // Get current userAccountControl value
                    var uacValue = entry.Properties["userAccountControl"]?.Value;
                    if (uacValue == null)
                    {
                        return (false, "Could not read userAccountControl attribute.");
                    }
                    
                    int currentUac = Convert.ToInt32(uacValue);
                    int newUac;
                    
                    if (enabled)
                    {
                        // Remove the ACCOUNTDISABLE flag (bit 1)
                        newUac = currentUac & ~ADS_UF_ACCOUNTDISABLE;
                    }
                    else
                    {
                        // Add the ACCOUNTDISABLE flag (bit 1)
                        newUac = currentUac | ADS_UF_ACCOUNTDISABLE;
                    }
                    
                    // Only update if the value actually changed
                    if (newUac != currentUac)
                    {
                        entry.Properties["userAccountControl"].Value = newUac;
                        entry.CommitChanges();
                    }
                    
                    return (true, null);
                }
                catch (UnauthorizedAccessException)
                {
                    return (false, "Access denied. You may not have permission to modify this object.");
                }
                catch (DirectoryServicesCOMException ex)
                {
                    var errorCode = ex.ErrorCode;
                    
                    if (errorCode == unchecked((int)0x80072020))
                        return (false, "An operations error occurred. The object may not exist.");
                    if (errorCode == unchecked((int)0x80070005))
                        return (false, "Access denied. You may not have permission to modify this object.");
                    if (errorCode == unchecked((int)0x80070056))
                        return (false, "Cannot enable account: password does not meet requirements or must be changed.");
                    
                    return (false, $"Directory error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    return (false, $"Failed to modify account: {ex.Message}");
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Checks if an AD object is protected from accidental deletion.
        /// This is determined by checking for a Deny Delete ACE on the object.
        /// </summary>
        public async Task<(bool IsProtected, string? Error)> IsProtectedFromDeletionAsync(
            string distinguishedName,
            string? server = null,
            string? username = null,
            string? password = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run<(bool, string?)>(() =>
            {
                try
                {
                    using var entry = CreateEntry(distinguishedName, server, username, password);
                    var nativeObject = entry.NativeObject;

                    // Check the nTSecurityDescriptor for deny delete ACEs
                    // A simpler approach: try to check if the object has the specific ACE
                    // The protection is typically a deny ACE for "Delete" and "Delete Subtree"
                    
                    var security = entry.ObjectSecurity;
                    var accessRules = security.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier));
                    
                    foreach (System.DirectoryServices.ActiveDirectoryAccessRule rule in accessRules)
                    {
                        // Check for Deny rules on Delete
                        if (rule.AccessControlType == System.Security.AccessControl.AccessControlType.Deny &&
                            (rule.ActiveDirectoryRights.HasFlag(System.DirectoryServices.ActiveDirectoryRights.Delete) ||
                             rule.ActiveDirectoryRights.HasFlag(System.DirectoryServices.ActiveDirectoryRights.DeleteTree)))
                        {
                            // Check if this applies to Everyone (S-1-1-0)
                            if (rule.IdentityReference.Value == "S-1-1-0")
                            {
                                return (true, null);
                            }
                        }
                    }
                    
                    return (false, null);
                }
                catch (Exception ex)
                {
                    return (false, $"Failed to check protection status: {ex.Message}");
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Sets or removes the "Protect object from accidental deletion" flag.
        /// </summary>
        public async Task<(bool Success, string? Error)> SetProtectedFromDeletionAsync(
            string distinguishedName,
            bool protect,
            string? server = null,
            string? username = null,
            string? password = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run<(bool, string?)>(() =>
            {
                try
                {
                    using var entry = CreateEntry(distinguishedName, server, username, password);
                    var nativeObject = entry.NativeObject;

                    var security = entry.ObjectSecurity;
                    var everyoneSid = new System.Security.Principal.SecurityIdentifier("S-1-1-0");
                    
                    if (protect)
                    {
                        // Add deny ACE for Delete and DeleteTree
                        var denyRule = new System.DirectoryServices.ActiveDirectoryAccessRule(
                            everyoneSid,
                            System.DirectoryServices.ActiveDirectoryRights.Delete | System.DirectoryServices.ActiveDirectoryRights.DeleteTree,
                            System.Security.AccessControl.AccessControlType.Deny);
                        security.AddAccessRule(denyRule);
                    }
                    else
                    {
                        // Remove deny ACEs for Delete from Everyone
                        var accessRules = security.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier));
                        foreach (System.DirectoryServices.ActiveDirectoryAccessRule rule in accessRules)
                        {
                            if (rule.AccessControlType == System.Security.AccessControl.AccessControlType.Deny &&
                                rule.IdentityReference.Value == "S-1-1-0" &&
                                (rule.ActiveDirectoryRights.HasFlag(System.DirectoryServices.ActiveDirectoryRights.Delete) ||
                                 rule.ActiveDirectoryRights.HasFlag(System.DirectoryServices.ActiveDirectoryRights.DeleteTree)))
                            {
                                security.RemoveAccessRule(rule);
                            }
                        }
                    }
                    
                    entry.CommitChanges();
                    return (true, null);
                }
                catch (UnauthorizedAccessException)
                {
                    return (false, "Access denied. You may not have permission to modify security settings.");
                }
                catch (Exception ex)
                {
                    return (false, $"Failed to modify protection: {ex.Message}");
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Checks if an AD object has child objects.
        /// </summary>
        public async Task<(bool HasChildren, int ChildCount, string? Error)> HasChildObjectsAsync(
            string distinguishedName,
            string? server = null,
            string? username = null,
            string? password = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run<(bool, int, string?)>(() =>
            {
                try
                {
                    using var entry = CreateEntry(distinguishedName, server, username, password);
                    var nativeObject = entry.NativeObject;

                    using var searcher = new DirectorySearcher(entry)
                    {
                        Filter = "(objectClass=*)",
                        SearchScope = SearchScope.OneLevel,
                        SizeLimit = 100  // Don't need to count all, just know if there are any
                    };
                    searcher.PropertiesToLoad.Add("name");

                    using var results = searcher.FindAll();
                    var count = results.Count;
                    
                    return (count > 0, count, null);
                }
                catch (Exception ex)
                {
                    return (false, 0, $"Failed to check for child objects: {ex.Message}");
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Deletes an AD object from the directory.
        /// </summary>
        /// <param name="distinguishedName">The DN of the object to delete</param>
        /// <param name="deleteSubtree">If true, deletes all child objects as well</param>
        /// <param name="server">Optional domain controller to target</param>
        /// <param name="username">Admin username</param>
        /// <param name="password">Admin password</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task<(bool Success, string? Error)> DeleteObjectAsync(
            string distinguishedName,
            bool deleteSubtree = false,
            string? server = null,
            string? username = null,
            string? password = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run<(bool, string?)>(() =>
            {
                try
                {
                    using var entry = CreateEntry(distinguishedName, server, username, password);
                    var nativeObject = entry.NativeObject;  // Force authentication

                    if (deleteSubtree)
                    {
                        // DeleteTree removes the object and all its children
                        entry.DeleteTree();
                    }
                    else
                    {
                        // Get parent and remove this entry
                        using var parent = entry.Parent;
                        parent.Children.Remove(entry);
                    }
                    
                    return (true, null);
                }
                catch (UnauthorizedAccessException)
                {
                    return (false, "Access denied. You may not have permission to delete this object.");
                }
                catch (DirectoryServicesCOMException ex)
                {
                    var errorCode = ex.ErrorCode;
                    
                    if (errorCode == unchecked((int)0x80072020))
                        return (false, "An operations error occurred. The object may not exist.");
                    if (errorCode == unchecked((int)0x80070005))
                        return (false, "Access denied. You may not have permission to delete this object.");
                    if (errorCode == unchecked((int)0x8007208C))
                        return (false, "Cannot delete object: it has child objects. Use 'Delete Subtree' option.");
                    if (errorCode == unchecked((int)0x80072030))
                        return (false, "Object not found. It may have already been deleted.");
                    
                    return (false, $"Directory error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    return (false, $"Failed to delete object: {ex.Message}");
                }
            }, cancellationToken);
        }
    }
}
