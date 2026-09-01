using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ActiveScanner.Models;
using LiteDB;

namespace ActiveScanner.Services
{
    /// <summary>
    /// Resolved metadata for the LastUser of a computer (or for a User AD object itself),
    /// used to expand the synthetic <c>{{LastUserEmail}}</c>/<c>{{LastUserFirstName}}</c>/
    /// <c>{{LastUserLastName}}</c>/<c>{{LastUserDisplayName}}</c> placeholders.
    /// </summary>
    public readonly struct LastUserInfo
    {
        public string? Email { get; }
        public string? DisplayName { get; }
        public LastUserInfo(string? email, string? displayName)
        {
            Email = email;
            DisplayName = displayName;
        }
    }

    /// <summary>
    /// Service for managing Outlook email templates and creating Outlook compose windows.
    /// Uses late-bound COM (no Office.Interop assembly reference) so the application
    /// can build/run without Office installed; Outlook is only required at send time.
    /// </summary>
    public class EmailTemplateService : IDisposable
    {
        private readonly string _dbPath;
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<EmailTemplate> _templates;

        // Matches {{ColumnName}} placeholders.
        private static readonly Regex PlaceholderRegex = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

        // Trailing digits at end of a name (e.g. "ANC-LT12345" -> "12345").
        private static readonly Regex TrailingDigitsRegex = new(@"(\d+)$", RegexOptions.Compiled);

        // Outlook OlBodyFormat constants.
        private const int olFormatPlain = 1;
        private const int olFormatHTML = 2;

        public EmailTemplateService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ActiveScanner");
            Directory.CreateDirectory(appDataPath);
            _dbPath = Path.Combine(appDataPath, "emailTemplates.db");

            _db = new LiteDatabase($"Filename={_dbPath};Connection=shared");
            _templates = _db.GetCollection<EmailTemplate>("templates");

            _templates.EnsureIndex(x => x.Id);
            _templates.EnsureIndex(x => x.Name);
        }

        public void Dispose() => _db?.Dispose();

        /// <summary>All saved templates sorted alphabetically.</summary>
        public IReadOnlyList<EmailTemplate> Templates => _templates.FindAll()
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

        public Task LoadAsync() => Task.CompletedTask;

        public Task SaveTemplateAsync(EmailTemplate template)
        {
            template.ModifiedAt = DateTime.UtcNow;
            _templates.Upsert(template.Id, template);
            return Task.CompletedTask;
        }

        public Task DeleteTemplateAsync(string templateId)
        {
            _templates.Delete(templateId);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Replace {{ColumnName}} placeholders against an AD object.
        /// The optional <paramref name="lastUserInfo"/> map resolves the synthetic
        /// <c>{{LastUserEmail}}</c>, <c>{{LastUserFirstName}}</c>, <c>{{LastUserLastName}}</c>,
        /// and <c>{{LastUserDisplayName}}</c> placeholders per AD object.
        /// Also supports <c>{{NameTrailingNumber}}</c> (trailing digits of the object name).
        /// </summary>
        public string ReplacePlaceholders(string input, AdObjectInfo? adObject,
            IReadOnlyDictionary<AdObjectInfo, LastUserInfo>? lastUserInfo = null)
        {
            if (string.IsNullOrEmpty(input) || adObject == null)
                return input ?? string.Empty;

            return PlaceholderRegex.Replace(input, match =>
            {
                var columnName = match.Groups[1].Value;
                var synthetic = ResolveSyntheticPlaceholder(columnName, adObject, lastUserInfo);
                if (synthetic != null) return synthetic;
                return GetColumnValue(adObject, columnName) ?? string.Empty;
            });
        }

        /// <summary>
        /// Resolve a synthetic placeholder name (returns null if it isn't synthetic, so the
        /// caller falls through to column-based resolution). An empty string is a valid value
        /// (e.g. when the placeholder is recognised but the data is unavailable).
        /// </summary>
        private static string? ResolveSyntheticPlaceholder(string columnName, AdObjectInfo o,
            IReadOnlyDictionary<AdObjectInfo, LastUserInfo>? lastUserInfo)
        {
            switch (columnName.ToLowerInvariant())
            {
                case "nametrailingnumber":
                case "nametrailingdigits":
                {
                    var name = o.Name;
                    if (string.IsNullOrEmpty(name)) return string.Empty;
                    var m = TrailingDigitsRegex.Match(name);
                    return m.Success ? m.Groups[1].Value : string.Empty;
                }

                case "lastuseremail":
                {
                    if (lastUserInfo != null && lastUserInfo.TryGetValue(o, out var info)
                        && !string.IsNullOrWhiteSpace(info.Email))
                    {
                        return info.Email;
                    }
                    return string.Empty;
                }

                case "lastuserdisplayname":
                {
                    if (lastUserInfo != null && lastUserInfo.TryGetValue(o, out var info)
                        && !string.IsNullOrWhiteSpace(info.DisplayName))
                    {
                        return info.DisplayName;
                    }
                    return string.Empty;
                }

                case "lastuserfirstname":
                    return ResolveLastUserFirstName(o, lastUserInfo);

                case "lastuserlastname":
                    return ResolveLastUserLastName(o, lastUserInfo);

                default:
                    return null;
            }
        }

        private static string ResolveLastUserFirstName(AdObjectInfo o,
            IReadOnlyDictionary<AdObjectInfo, LastUserInfo>? lastUserInfo)
        {
            // Prefer DisplayName "First Last" / "Last, First" -> first name token.
            if (lastUserInfo != null && lastUserInfo.TryGetValue(o, out var info))
            {
                var fromDisplay = ParseFirstNameFromDisplayName(info.DisplayName);
                if (!string.IsNullOrEmpty(fromDisplay)) return fromDisplay;

                var fromEmail = ParseFirstNameFromEmail(info.Email);
                if (!string.IsNullOrEmpty(fromEmail)) return fromEmail;
            }

            // Fallback: User-typed AD object's own attributes.
            if (!string.IsNullOrWhiteSpace(o.GivenName)) return o.GivenName!;
            var fromOwnDisplay = ParseFirstNameFromDisplayName(o.DisplayName);
            if (!string.IsNullOrEmpty(fromOwnDisplay)) return fromOwnDisplay;
            return ParseFirstNameFromEmail(o.Mail) ?? string.Empty;
        }

        private static string ResolveLastUserLastName(AdObjectInfo o,
            IReadOnlyDictionary<AdObjectInfo, LastUserInfo>? lastUserInfo)
        {
            if (lastUserInfo != null && lastUserInfo.TryGetValue(o, out var info))
            {
                var fromDisplay = ParseLastNameFromDisplayName(info.DisplayName);
                if (!string.IsNullOrEmpty(fromDisplay)) return fromDisplay;

                var fromEmail = ParseLastNameFromEmail(info.Email);
                if (!string.IsNullOrEmpty(fromEmail)) return fromEmail;
            }

            if (!string.IsNullOrWhiteSpace(o.Surname)) return o.Surname!;
            var fromOwnDisplay = ParseLastNameFromDisplayName(o.DisplayName);
            if (!string.IsNullOrEmpty(fromOwnDisplay)) return fromOwnDisplay;
            return ParseLastNameFromEmail(o.Mail) ?? string.Empty;
        }

        // Parse "Last, First Middle" or "First Middle Last" -> First.
        private static string? ParseFirstNameFromDisplayName(string? displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return null;
            var dn = displayName.Trim();
            var commaIdx = dn.IndexOf(',');
            if (commaIdx >= 0 && commaIdx < dn.Length - 1)
            {
                var afterComma = dn.Substring(commaIdx + 1).Trim();
                var firstToken = afterComma.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return StripDigits(firstToken);
            }
            var first = dn.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return StripDigits(first);
        }

        // Parse "Last, First" or "First Middle Last" -> Last.
        private static string? ParseLastNameFromDisplayName(string? displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return null;
            var dn = displayName.Trim();
            var commaIdx = dn.IndexOf(',');
            if (commaIdx > 0)
            {
                return StripDigits(dn.Substring(0, commaIdx).Trim());
            }
            var tokens = dn.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length >= 2 ? StripDigits(tokens[tokens.Length - 1]) : null;
        }

        // Parse "first.last@domain" -> "first" (digits stripped).
        private static string? ParseFirstNameFromEmail(string? email)
        {
            var local = GetEmailLocalPart(email);
            if (local == null) return null;
            var dot = local.IndexOf('.');
            var firstPart = dot >= 0 ? local.Substring(0, dot) : local;
            return Capitalize(StripDigits(firstPart));
        }

        // Parse "first.last@domain" -> "last" (digits stripped). Returns null if no '.' is present.
        private static string? ParseLastNameFromEmail(string? email)
        {
            var local = GetEmailLocalPart(email);
            if (local == null) return null;
            var dot = local.IndexOf('.');
            if (dot < 0 || dot == local.Length - 1) return null;
            // Use last segment so middle initials "first.m.last" still resolve to "last".
            var segments = local.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2) return null;
            return Capitalize(StripDigits(segments[segments.Length - 1]));
        }

        private static string? GetEmailLocalPart(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            var at = email.IndexOf('@');
            var local = at >= 0 ? email.Substring(0, at) : email;
            return string.IsNullOrWhiteSpace(local) ? null : local;
        }

        private static string? StripDigits(string? input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var sb = new System.Text.StringBuilder(input.Length);
            foreach (var ch in input)
            {
                if (!char.IsDigit(ch)) sb.Append(ch);
            }
            return sb.ToString();
        }

        private static string? Capitalize(string? input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return char.ToUpperInvariant(input[0]) + input.Substring(1);
        }

        /// <summary>
        /// Aggregate semicolon-separated address strings across multiple items
        /// (with placeholders evaluated per item) into a single deduped list.
        /// </summary>
        public string AggregateAddresses(string input, IEnumerable<AdObjectInfo> adObjects,
            IReadOnlyDictionary<AdObjectInfo, LastUserInfo>? lastUserInfo = null)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();

            foreach (var obj in adObjects)
            {
                var resolved = ReplacePlaceholders(input, obj, lastUserInfo);
                foreach (var part in resolved.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length > 0 && seen.Add(trimmed))
                    {
                        result.Add(trimmed);
                    }
                }
            }
            return string.Join("; ", result);
        }

        /// <summary>
        /// Open one or more Outlook compose windows based on the template and selected items.
        /// Returns the number of compose windows opened.
        /// </summary>
        public int OpenInOutlook(EmailTemplate template, IReadOnlyList<AdObjectInfo> adObjects,
            IReadOnlyDictionary<AdObjectInfo, LastUserInfo>? lastUserInfo = null)
        {
            if (adObjects == null || adObjects.Count == 0)
                return 0;

            var outlook = GetOrCreateOutlookApplication();

            int opened = 0;
            try
            {
                if (template.RecipientMode == EmailRecipientMode.Combined)
                {
                    var firstItem = adObjects[0];
                    CreateMailItem(
                        outlook,
                        to: AggregateAddresses(template.To, adObjects, lastUserInfo),
                        cc: AggregateAddresses(template.Cc, adObjects, lastUserInfo),
                        bcc: AggregateAddresses(template.Bcc, adObjects, lastUserInfo),
                        subject: ReplacePlaceholders(template.Subject, firstItem, lastUserInfo),
                        body: ReplacePlaceholders(template.Body, firstItem, lastUserInfo),
                        isHtml: template.IsHtml);
                    opened = 1;
                }
                else
                {
                    foreach (var obj in adObjects)
                    {
                        CreateMailItem(
                            outlook,
                            to: ReplacePlaceholders(template.To, obj, lastUserInfo),
                            cc: ReplacePlaceholders(template.Cc, obj, lastUserInfo),
                            bcc: ReplacePlaceholders(template.Bcc, obj, lastUserInfo),
                            subject: ReplacePlaceholders(template.Subject, obj, lastUserInfo),
                            body: ReplacePlaceholders(template.Body, obj, lastUserInfo),
                            isHtml: template.IsHtml);
                        opened++;
                    }
                }
            }
            finally
            {
                // Release the Application COM ref; Outlook stays running for the user.
                ReleaseCom(outlook);
            }

            return opened;
        }

        /// <summary>
        /// Generate a preview of the resolved email for the given AD object (for the UI preview).
        /// </summary>
        public (string To, string Cc, string Bcc, string Subject, string Body) GeneratePreview(
            EmailTemplate template, AdObjectInfo? sample, IReadOnlyList<AdObjectInfo> allItems,
            IReadOnlyDictionary<AdObjectInfo, LastUserInfo>? lastUserInfo = null)
        {
            if (template.RecipientMode == EmailRecipientMode.Combined && allItems.Count > 0)
            {
                return (
                    AggregateAddresses(template.To, allItems, lastUserInfo),
                    AggregateAddresses(template.Cc, allItems, lastUserInfo),
                    AggregateAddresses(template.Bcc, allItems, lastUserInfo),
                    ReplacePlaceholders(template.Subject, allItems[0], lastUserInfo),
                    ReplacePlaceholders(template.Body, allItems[0], lastUserInfo));
            }

            return (
                ReplacePlaceholders(template.To, sample, lastUserInfo),
                ReplacePlaceholders(template.Cc, sample, lastUserInfo),
                ReplacePlaceholders(template.Bcc, sample, lastUserInfo),
                ReplacePlaceholders(template.Subject, sample, lastUserInfo),
                ReplacePlaceholders(template.Body, sample, lastUserInfo));
        }

        // ---------------- Outlook COM (late bound) ----------------

        private static dynamic GetOrCreateOutlookApplication()
        {
            // Outlook.Application is registered as a single-instance COM server, so creating
            // a new instance attaches to the user's already-running Outlook session if present.
            var type = Type.GetTypeFromProgID("Outlook.Application");
            if (type == null)
                throw new InvalidOperationException(
                    "Microsoft Outlook does not appear to be installed (Outlook.Application ProgID not found).");

            var instance = Activator.CreateInstance(type);
            if (instance == null)
                throw new InvalidOperationException("Unable to create an Outlook.Application instance.");

            return instance;
        }

        private static void CreateMailItem(dynamic outlook, string to, string cc, string bcc,
            string subject, string body, bool isHtml)
        {
            // 0 = olMailItem
            dynamic mail = outlook.CreateItem(0);
            try
            {
                if (!string.IsNullOrWhiteSpace(to)) mail.To = to;
                if (!string.IsNullOrWhiteSpace(cc)) mail.CC = cc;
                if (!string.IsNullOrWhiteSpace(bcc)) mail.BCC = bcc;
                if (!string.IsNullOrEmpty(subject)) mail.Subject = subject;

                if (isHtml)
                {
                    mail.BodyFormat = olFormatHTML;
                    // Use HTMLBody so the user's default signature still works when displayed.
                    // Outlook will append the signature on Display() if HTMLBody is set before display
                    // and the body doesn't already contain a signature marker.
                    mail.HTMLBody = body ?? string.Empty;
                }
                else
                {
                    mail.BodyFormat = olFormatPlain;
                    mail.Body = body ?? string.Empty;
                }

                // Display compose window (non-modal) so the user can review and Send.
                mail.Display(false);
            }
            finally
            {
                ReleaseCom(mail);
            }
        }

        private static void ReleaseCom(object? comObject)
        {
            if (comObject == null) return;
            try
            {
                if (Marshal.IsComObject(comObject))
                    Marshal.ReleaseComObject(comObject);
            }
            catch { /* best-effort */ }
        }

        // ---------------- Column resolution ----------------

        private static string? GetColumnValue(AdObjectInfo o, string columnName)
        {
            return columnName.ToLowerInvariant() switch
            {
                // Common
                "name" => o.Name,
                "description" => o.Description,
                "distinguishedname" => o.DistinguishedName,
                "managedby" => o.ManagedBy,
                "isenabled" => o.IsEnabled ? "Enabled" : "Disabled",
                "lastactivity" => o.LastActivity?.ToLocalTime().ToString("g"),
                "lastlogon" => o.LastLogon?.ToLocalTime().ToString("g"),
                "whencreated" => o.WhenCreated?.ToLocalTime().ToString("g"),
                "whenchanged" => o.WhenChanged?.ToLocalTime().ToString("g"),

                // Computer
                "ipaddress" => o.IpAddress,
                "dnshostname" => o.DnsHostName,
                "operatingsystem" => o.OperatingSystem,
                "operatingsystemversion" => o.OperatingSystemVersion,
                "location" => o.Location,
                "lastuser" => o.LastUser,

                // User
                "samaccountname" => o.SamAccountName,
                "userprincipalname" => o.UserPrincipalName,
                "displayname" => o.DisplayName,
                "givenname" => o.GivenName,
                "surname" => o.Surname,
                "mail" => o.Mail,
                "email" => o.Mail,
                "title" => o.Title,
                "department" => o.Department,
                "company" => o.Company,
                "manager" => o.Manager,
                "telephonenumber" => o.TelephoneNumber,
                "mobile" => o.Mobile,
                "office" => o.Office,
                "passwordlastset" => o.PasswordLastSet?.ToLocalTime().ToString("g"),
                "accountexpires" => o.AccountExpires?.ToLocalTime().ToString("g"),
                "computerhistorydisplay" => o.ComputerHistoryDisplay,
                "snowid" => o.SnowId,

                // Printer
                "printername" => o.PrinterName,
                "servername" => o.ServerName,
                "sharename" => o.ShareName,
                "portname" => o.PortName,
                "drivername" => o.DriverName,
                "printermodel" => o.PrinterModel,
                "uncname" => o.UNCName,

                _ => null
            };
        }
    }
}
