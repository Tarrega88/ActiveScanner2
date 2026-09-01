using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ActiveScanner.Models;
using LiteDB;

namespace ActiveScanner.Services
{
    /// <summary>
    /// Service for managing incident ticket templates and URL generation
    /// </summary>
    public class IncidentTicketService : IDisposable
    {
        private readonly string _dbPath;
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<IncidentTicketTemplate> _templates;
        private readonly ILiteCollection<DropdownOption> _customUsers;
        private readonly ILiteCollection<DropdownOption> _customLocations;

        // Regex to match {{ColumnName}} placeholders
        private static readonly Regex PlaceholderRegex = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

        // Recommended max URL length (browsers may truncate longer URLs)
        public const int MaxRecommendedUrlLength = 2000;
        public const int WarningUrlLength = 1800;

        public IncidentTicketService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ActiveScanner");
            Directory.CreateDirectory(appDataPath);
            _dbPath = Path.Combine(appDataPath, "incidentTickets.db");

            // Open LiteDB connection
            _db = new LiteDatabase($"Filename={_dbPath};Connection=shared");
            _templates = _db.GetCollection<IncidentTicketTemplate>("templates");
            _customUsers = _db.GetCollection<DropdownOption>("customUsers");
            _customLocations = _db.GetCollection<DropdownOption>("customLocations");

            // Create indexes
            _templates.EnsureIndex(x => x.Id);
            _templates.EnsureIndex(x => x.Name);
        }

        public void Dispose()
        {
            _db?.Dispose();
        }

        /// <summary>
        /// All saved templates (sorted alphabetically by name)
        /// </summary>
        public IReadOnlyList<IncidentTicketTemplate> Templates => _templates.FindAll()
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

        /// <summary>
        /// Get all available users (built-in + custom)
        /// </summary>
        public List<DropdownOption> GetAllUsers()
        {
            var allUsers = new List<DropdownOption>(ServiceNowFields.SubmittedByOptions);
            foreach (var customUser in _customUsers.FindAll())
            {
                if (!allUsers.Any(u => u.UrlValue == customUser.UrlValue))
                {
                    allUsers.Add(customUser);
                }
            }
            return allUsers;
        }

        /// <summary>
        /// Get all available locations (built-in + custom)
        /// </summary>
        public List<DropdownOption> GetAllLocations()
        {
            var allLocations = new List<DropdownOption>(ServiceNowFields.LocationOptions);
            foreach (var customLocation in _customLocations.FindAll())
            {
                if (!allLocations.Any(l => l.UrlValue == customLocation.UrlValue))
                {
                    allLocations.Add(customLocation);
                }
            }
            return allLocations;
        }

        /// <summary>
        /// Add a custom user
        /// </summary>
        public Task AddCustomUserAsync(string displayName, string sysId)
        {
            if (_customUsers.Exists(u => u.UrlValue == sysId)) return Task.CompletedTask;
            _customUsers.Insert(new DropdownOption(displayName, sysId));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Add a custom location
        /// </summary>
        public Task AddCustomLocationAsync(string displayName, string sysId)
        {
            if (_customLocations.Exists(l => l.UrlValue == sysId)) return Task.CompletedTask;
            _customLocations.Insert(new DropdownOption(displayName, sysId));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Load templates from disk (no-op for LiteDB)
        /// </summary>
        public Task LoadAsync()
        {
            // LiteDB handles this automatically
            return Task.CompletedTask;
        }

        /// <summary>
        /// Add or update a template
        /// </summary>
        public Task SaveTemplateAsync(IncidentTicketTemplate template)
        {
            template.ModifiedAt = DateTime.UtcNow;
            _templates.Upsert(template.Id, template);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Delete a template by ID
        /// </summary>
        public Task DeleteTemplateAsync(string templateId)
        {
            _templates.Delete(templateId);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Generate a ServiceNow incident URL for a single AD object
        /// </summary>
        public string GenerateUrl(IncidentTicketTemplate template, AdObjectInfo? adObject = null, string? resolvedAffectedEndUserSnowId = null)
        {
            var sb = new StringBuilder(ServiceNowFields.BaseUrl);
            var queryParts = new List<string>();

            // Add each non-empty field
            AddFieldIfNotEmpty(queryParts, ServiceNowFields.FieldCallerId, 
                ReplacePlaceholders(template.SubmittedBy, adObject));
            
            AddFieldIfNotEmpty(queryParts, ServiceNowFields.FieldBuildingNumber, 
                ReplacePlaceholders(template.AffectedUserBuildingNumber, adObject));
            
            AddFieldIfNotEmpty(queryParts, ServiceNowFields.FieldRoomNumber, 
                ReplacePlaceholders(template.AffectedUserRoomNumber, adObject));
            
            AddFieldIfNotEmpty(queryParts, ServiceNowFields.FieldContactMethod, 
                template.BestContactMethod); // Dropdown value, no placeholder replacement
            
            AddFieldIfNotEmpty(queryParts, ServiceNowFields.FieldPhoneNumber, 
                ReplacePlaceholders(template.AffectedUserPhoneNumber, adObject));
            
            AddFieldIfNotEmpty(queryParts, ServiceNowFields.FieldCategory, 
                template.Category); // Dropdown value
            
            AddFieldIfNotEmpty(queryParts, ServiceNowFields.FieldSubcategory, 
                ReplacePlaceholders(template.Subcategory, adObject));
            
            // Affected Service (only for enterprise_application category)
            if (template.Category == "enterprise_application" && !string.IsNullOrWhiteSpace(template.AffectedService))
            {
                AddFieldIfNotEmpty(queryParts, ServiceNowFields.FieldAffectedService, 
                    ReplacePlaceholders(template.AffectedService, adObject));
            }
            
            AddFieldIfNotEmpty(queryParts, ServiceNowFields.FieldShortDescription, 
                ReplacePlaceholders(template.ShortDescription, adObject));
            
            AddFieldIfNotEmpty(queryParts, ServiceNowFields.FieldContactType, 
                template.ContactType); // Dropdown value
            
            AddFieldIfNotEmpty(queryParts, ServiceNowFields.FieldImpact, 
                template.Impact); // Dropdown value
            
            AddFieldIfNotEmpty(queryParts, ServiceNowFields.FieldUrgency, 
                template.Urgency); // Dropdown value
            
            AddFieldIfNotEmpty(queryParts, ServiceNowFields.FieldDescription, 
                ReplacePlaceholders(template.Description, adObject));
            
            AddFieldIfNotEmpty(queryParts, ServiceNowFields.FieldAffectedSystemName, 
                ReplacePlaceholders(template.AffectedSystemName, adObject));
            
            // Affected End User - use resolved SNOW ID if provided
            // If it's a {{SnowId}} placeholder, resolve it per-item
            if (!string.IsNullOrEmpty(resolvedAffectedEndUserSnowId))
            {
                var affectedEndUserValue = resolvedAffectedEndUserSnowId.Contains("{{")
                    ? ReplacePlaceholders(resolvedAffectedEndUserSnowId, adObject)
                    : resolvedAffectedEndUserSnowId;
                AddFieldIfNotEmpty(queryParts, ServiceNowFields.FieldAffectedEndUser, affectedEndUserValue);
            }
            
            AddFieldIfNotEmpty(queryParts, ServiceNowFields.FieldWorkNotes, 
                ReplacePlaceholders(template.WorkNotes, adObject));

            // Join all parts with ^
            if (queryParts.Count > 0)
            {
                sb.Append(string.Join("^", queryParts));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Generate URLs for multiple AD objects
        /// </summary>
        public List<(AdObjectInfo AdObject, string Url)> GenerateUrls(
            IncidentTicketTemplate template, 
            IEnumerable<AdObjectInfo> adObjects,
            string? resolvedAffectedEndUserSnowId = null)
        {
            return adObjects
                .Select(obj => (obj, GenerateUrl(template, obj, resolvedAffectedEndUserSnowId)))
                .ToList();
        }

        /// <summary>
        /// Replace {{ColumnName}} placeholders with actual values from the AD object
        /// </summary>
        private string ReplacePlaceholders(string input, AdObjectInfo? adObject)
        {
            if (string.IsNullOrEmpty(input) || adObject == null)
                return input;

            return PlaceholderRegex.Replace(input, match =>
            {
                var columnName = match.Groups[1].Value;
                var value = GetColumnValue(adObject, columnName);
                return value ?? string.Empty;
            });
        }

        /// <summary>
        /// Get a column value from an AD object by column name
        /// </summary>
        private string? GetColumnValue(AdObjectInfo adObject, string columnName)
        {
            return columnName.ToLowerInvariant() switch
            {
                // Common properties
                "name" => adObject.Name,
                "description" => adObject.Description,
                "distinguishedname" => TargetPath.GetParentPath(adObject.DistinguishedName),
                "managedby" => adObject.ManagedBy,
                "isenabled" => adObject.IsEnabled ? "Enabled" : "Disabled",
                "lastactivity" => adObject.LastActivity?.ToLocalTime().ToString("g"),
                "lastlogon" => adObject.LastLogon?.ToLocalTime().ToString("g"),
                "whencreated" => adObject.WhenCreated?.ToLocalTime().ToString("g"),
                "whenchanged" => adObject.WhenChanged?.ToLocalTime().ToString("g"),
                
                // Computer properties
                "ipaddress" => adObject.IpAddress,
                "dnshostname" => adObject.DnsHostName,
                "operatingsystem" => adObject.OperatingSystem,
                "operatingsystemversion" => adObject.OperatingSystemVersion,
                "location" => adObject.Location,
                "lastuser" => adObject.LastUser,
                
                // User properties
                "samaccountname" => adObject.SamAccountName,
                "userprincipalname" => adObject.UserPrincipalName,
                "displayname" => adObject.DisplayName,
                "givenname" => adObject.GivenName,
                "surname" => adObject.Surname,
                "mail" => adObject.Mail,
                "title" => adObject.Title,
                "department" => adObject.Department,
                "company" => adObject.Company,
                "manager" => adObject.Manager,
                "telephonenumber" => adObject.TelephoneNumber,
                "mobile" => adObject.Mobile,
                "office" => adObject.Office,
                "passwordlastset" => adObject.PasswordLastSet?.ToLocalTime().ToString("g"),
                "accountexpires" => adObject.AccountExpires?.ToLocalTime().ToString("g"),
                "computerhistorydisplay" => adObject.ComputerHistoryDisplay,
                "snowid" => adObject.SnowId,
                
                // Printer properties
                "printername" => adObject.PrinterName,
                "servername" => adObject.ServerName,
                "sharename" => adObject.ShareName,
                "portname" => adObject.PortName,
                "drivername" => adObject.DriverName,
                "printermodel" => adObject.PrinterModel,
                "uncname" => adObject.UNCName,
                
                _ => null
            };
        }

        /// <summary>
        /// Add a field to the query parts if it has a non-empty value
        /// </summary>
        private void AddFieldIfNotEmpty(List<string> queryParts, string fieldName, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                var encodedValue = UrlEncode(value);
                queryParts.Add($"{fieldName}={encodedValue}");
            }
        }

        /// <summary>
        /// URL encode a string, replacing spaces with %20 and encoding other special chars
        /// </summary>
        public static string UrlEncode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            // Use Uri.EscapeDataString which handles most encoding
            // But ServiceNow seems to use %20 for spaces (already default in EscapeDataString)
            return Uri.EscapeDataString(value);
        }

        /// <summary>
        /// Open URLs in the specified browser
        /// </summary>
        public async Task OpenUrlsInBrowser(IEnumerable<string> urls, TicketBrowser browser, int delayBetweenMs = 500)
        {
            var browserPath = browser switch
            {
                TicketBrowser.Chrome => GetChromePath(),
                TicketBrowser.Edge => GetEdgePath(),
                _ => GetEdgePath()
            };

            foreach (var url in urls)
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = browserPath,
                        Arguments = $"\"{url}\"",
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);

                    // Small delay between opening tabs to avoid popup blocker
                    if (delayBetweenMs > 0)
                    {
                        await Task.Delay(delayBetweenMs);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open URL: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Get Chrome executable path
        /// </summary>
        private string GetChromePath()
        {
            var paths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 
                    "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), 
                    "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Google", "Chrome", "Application", "chrome.exe")
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                    return path;
            }

            // Fallback to just "chrome" and hope it's in PATH
            return "chrome";
        }

        /// <summary>
        /// Get Edge executable path
        /// </summary>
        private string GetEdgePath()
        {
            var paths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Microsoft", "Edge", "Application", "msedge.exe")
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                    return path;
            }

            // Fallback to just "msedge" and hope it's in PATH
            return "msedge";
        }

        /// <summary>
        /// Check if a URL exceeds the warning length
        /// </summary>
        public static bool IsUrlLengthWarning(string url)
        {
            return url.Length > WarningUrlLength;
        }

        /// <summary>
        /// Check if a URL exceeds the maximum recommended length
        /// </summary>
        public static bool IsUrlTooLong(string url)
        {
            return url.Length > MaxRecommendedUrlLength;
        }
    }
}
