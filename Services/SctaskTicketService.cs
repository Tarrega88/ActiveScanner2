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
    /// Service for managing SCTASK ticket templates and URL generation
    /// </summary>
    public class SctaskTicketService : IDisposable
    {
        private readonly string _dbPath;
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<SctaskTicketTemplate> _templates;
        private readonly ILiteCollection<DropdownOption> _customGroups;

        // Regex to match {{ColumnName}} placeholders
        private static readonly Regex PlaceholderRegex = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

        // Recommended max URL length (browsers may truncate longer URLs)
        public const int MaxRecommendedUrlLength = 2000;
        public const int WarningUrlLength = 1800;

        public SctaskTicketService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ActiveScanner");
            Directory.CreateDirectory(appDataPath);
            _dbPath = Path.Combine(appDataPath, "sctaskTickets.db");

            // Open LiteDB connection
            _db = new LiteDatabase($"Filename={_dbPath};Connection=shared");
            _templates = _db.GetCollection<SctaskTicketTemplate>("templates");
            _customGroups = _db.GetCollection<DropdownOption>("customGroups");

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
        public IReadOnlyList<SctaskTicketTemplate> Templates => _templates.FindAll()
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

        /// <summary>
        /// Get all available assignment groups (built-in + custom)
        /// </summary>
        public List<DropdownOption> GetAllGroups()
        {
            var allGroups = new List<DropdownOption>(SctaskFields.AssignmentGroupOptions);
            foreach (var customGroup in _customGroups.FindAll())
            {
                if (!allGroups.Any(g => g.UrlValue == customGroup.UrlValue))
                {
                    allGroups.Add(customGroup);
                }
            }
            return allGroups;
        }

        /// <summary>
        /// Add a custom assignment group
        /// </summary>
        public Task AddCustomGroupAsync(string displayName, string sysId)
        {
            if (_customGroups.Exists(g => g.UrlValue == sysId)) return Task.CompletedTask;
            _customGroups.Insert(new DropdownOption(displayName, sysId));
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
        public Task SaveTemplateAsync(SctaskTicketTemplate template)
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
        /// Generate a ServiceNow SCTASK URL for a single AD object
        /// </summary>
        public string GenerateUrl(
            SctaskTicketTemplate template, 
            AdObjectInfo? adObject = null, 
            string? resolvedRitmSysId = null,
            string? resolvedRequestedForSnowId = null,
            string? resolvedAssignmentGroupSysId = null,
            string? resolvedAssignedToSnowId = null)
        {
            var sb = new StringBuilder(SctaskFields.BaseUrl);
            var queryParts = new List<string>();

            // Request Item (RITM) - required
            if (!string.IsNullOrEmpty(resolvedRitmSysId))
            {
                AddFieldIfNotEmpty(queryParts, SctaskFields.FieldRequestItem, resolvedRitmSysId);
            }

            // Requested For
            if (!string.IsNullOrEmpty(resolvedRequestedForSnowId))
            {
                var value = resolvedRequestedForSnowId.Contains("{{")
                    ? ReplacePlaceholders(resolvedRequestedForSnowId, adObject)
                    : resolvedRequestedForSnowId;
                AddFieldIfNotEmpty(queryParts, SctaskFields.FieldRequestedFor, value);
            }

            // Assignment Group
            if (!string.IsNullOrEmpty(resolvedAssignmentGroupSysId))
            {
                AddFieldIfNotEmpty(queryParts, SctaskFields.FieldAssignmentGroup, resolvedAssignmentGroupSysId);
            }

            // Assigned To
            if (!string.IsNullOrEmpty(resolvedAssignedToSnowId))
            {
                var value = resolvedAssignedToSnowId.Contains("{{")
                    ? ReplacePlaceholders(resolvedAssignedToSnowId, adObject)
                    : resolvedAssignedToSnowId;
                AddFieldIfNotEmpty(queryParts, SctaskFields.FieldAssignedTo, value);
            }

            // Short Description
            AddFieldIfNotEmpty(queryParts, SctaskFields.FieldShortDescription, 
                ReplacePlaceholders(template.ShortDescription, adObject));

            // Description
            AddFieldIfNotEmpty(queryParts, SctaskFields.FieldDescription, 
                ReplacePlaceholders(template.Description, adObject));

            // Work Notes
            AddFieldIfNotEmpty(queryParts, SctaskFields.FieldWorkNotes, 
                ReplacePlaceholders(template.WorkNotes, adObject));

            // Priority (dropdown value)
            AddFieldIfNotEmpty(queryParts, SctaskFields.FieldPriority, template.Priority);

            // State (dropdown value)
            AddFieldIfNotEmpty(queryParts, SctaskFields.FieldState, template.State);

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
            SctaskTicketTemplate template, 
            IEnumerable<AdObjectInfo> adObjects,
            string? resolvedRitmSysId = null,
            string? resolvedRequestedForSnowId = null,
            string? resolvedAssignmentGroupSysId = null,
            string? resolvedAssignedToSnowId = null)
        {
            return adObjects
                .Select(obj => (obj, GenerateUrl(
                    template, obj, 
                    resolvedRitmSysId,
                    resolvedRequestedForSnowId,
                    resolvedAssignmentGroupSysId,
                    resolvedAssignedToSnowId)))
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
        /// Add a field to the query if it has a non-empty value
        /// </summary>
        private void AddFieldIfNotEmpty(List<string> queryParts, string fieldName, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                queryParts.Add($"{fieldName}={UrlEncode(value)}");
            }
        }

        /// <summary>
        /// URL encode a value for ServiceNow
        /// </summary>
        private string UrlEncode(string value)
        {
            // Standard URL encoding
            return Uri.EscapeDataString(value);
        }

        /// <summary>
        /// Check if a URL length is within recommended limits
        /// </summary>
        public (bool IsWarning, bool IsError, string Message) CheckUrlLength(string url)
        {
            if (url.Length > MaxRecommendedUrlLength)
            {
                return (true, true, $"URL is {url.Length} chars (max recommended: {MaxRecommendedUrlLength}). Consider shorter descriptions.");
            }
            if (url.Length > WarningUrlLength)
            {
                return (true, false, $"URL is {url.Length} chars (approaching limit of {MaxRecommendedUrlLength}).");
            }
            return (false, false, string.Empty);
        }

        /// <summary>
        /// Open URLs in the specified browser
        /// </summary>
        public async Task OpenUrlsInBrowser(IEnumerable<string> urls, TicketBrowser browser, int delayBetweenMs = 500)
        {
            var browserPath = browser == TicketBrowser.Chrome
                ? "chrome.exe"
                : "msedge.exe";

            foreach (var url in urls)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = browserPath,
                        Arguments = $"\"{url}\"",
                        UseShellExecute = true
                    });

                    if (delayBetweenMs > 0)
                    {
                        await Task.Delay(delayBetweenMs);
                    }
                }
                catch (Exception)
                {
                    // Fall back to default browser
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
            }
        }
    }
}
