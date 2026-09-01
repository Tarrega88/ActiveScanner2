using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ClosedXML.Excel;
using ActiveScanner.Models;

namespace ActiveScanner.Services
{
    /// <summary>
    /// Service for exporting data to various formats
    /// </summary>
    public class ExportService
    {
        /// <summary>
        /// Gets the default export folder (OneDrive or Documents)
        /// </summary>
        public string GetDefaultExportFolder()
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Try OneDrive for Business
            var oneDrivePaths = new[]
            {
                Environment.GetEnvironmentVariable("OneDriveCommercial"),
                Path.Combine(profile, "OneDrive - Department of Veterans Affairs")
            };

            foreach (var path in oneDrivePaths)
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    var docs = Path.Combine(path, "Documents");
                    if (!Directory.Exists(docs))
                    {
                        try { Directory.CreateDirectory(docs); } catch { docs = path; }
                    }
                    return docs;
                }
            }

            // Fallback to My Documents
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        /// <summary>
        /// Generates a default filename with timestamp
        /// </summary>
        public string GenerateFileName(string prefix, ExportFormat format)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var extension = GetExtension(format);
            return $"{prefix}_{timestamp}{extension}";
        }

        /// <summary>
        /// Gets the file extension for a format
        /// </summary>
        public string GetExtension(ExportFormat format)
        {
            return format switch
            {
                ExportFormat.Excel => ".xlsx",
                ExportFormat.Csv => ".csv",
                ExportFormat.Json => ".json",
                ExportFormat.Text => ".txt",
                ExportFormat.Html => ".html",
                _ => ".txt"
            };
        }

        /// <summary>
        /// Generates the organized export folder path based on source and format.
        /// Structure: {baseFolder}/Active Scanner Exports/{ObjectType}/{SourceName}/{Format}/
        /// For Network exports: {baseFolder}/Active Scanner Exports/Network/{Format}/
        /// </summary>
        /// <param name="baseFolder">Base export folder (e.g., Documents)</param>
        /// <param name="objectType">The type of objects being exported (Computers, Users, Printers, Network)</param>
        /// <param name="sourcePath">The source DN, domain name, or group name</param>
        /// <param name="format">Export format (Excel, CSV, etc.)</param>
        /// <returns>Full organized export folder path</returns>
        public string GetOrganizedExportFolder(string baseFolder, string objectType, string? sourcePath, ExportFormat format)
        {
            var formatFolder = format.ToString();

            // Network and Vulnerability exports don't need a source subfolder
            if (objectType.Equals("Network", StringComparison.OrdinalIgnoreCase) ||
                objectType.Equals("Vulnerabilities", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(baseFolder, "Active Scanner Exports", objectType, formatFolder);
            }

            var sourceFolderName = ConvertSourcePathToFolderName(sourcePath);
            return Path.Combine(baseFolder, "Active Scanner Exports", objectType, sourceFolderName, formatFolder);
        }

        /// <summary>
        /// Converts a source path (DN, domain name, or group name) to a folder-safe name.
        /// Handles prefixed paths like "CustomGroup:MyGroup" or "DomainGroup:AD Group"
        /// For DNs: Extracts OUs and DCs, formats as "OU3.OU2.OU1.DC1.DC2"
        /// For custom/domain groups: Uses the group name directly (sanitized)
        /// </summary>
        public string ConvertSourcePathToFolderName(string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                return "Unknown";

            // Handle special prefixed sources
            if (sourcePath.StartsWith("CustomGroup:", StringComparison.OrdinalIgnoreCase))
            {
                var groupName = sourcePath.Substring("CustomGroup:".Length);
                return SanitizeFolderName(groupName);
            }

            if (sourcePath.StartsWith("DomainGroup:", StringComparison.OrdinalIgnoreCase))
            {
                var groupName = sourcePath.Substring("DomainGroup:".Length);
                return SanitizeFolderName(groupName);
            }

            // Handle simple "Network" source
            if (sourcePath.Equals("Network", StringComparison.OrdinalIgnoreCase))
            {
                return "Network";
            }

            // Check if it's a Distinguished Name (contains DC= or OU=)
            if (sourcePath.Contains("DC=", StringComparison.OrdinalIgnoreCase) ||
                sourcePath.Contains("OU=", StringComparison.OrdinalIgnoreCase) ||
                sourcePath.Contains("CN=", StringComparison.OrdinalIgnoreCase))
            {
                return ParseDistinguishedNameToFolderName(sourcePath);
            }

            // It's a simple name (custom group or domain name) - just sanitize it
            return SanitizeFolderName(sourcePath);
        }

        /// <summary>
        /// Parses a Distinguished Name and converts it to a folder name.
        /// Format: OU path (leaf to root) + last 2 DC components, dot-separated
        /// Example: "OU=Laptops,OU=Alaska,DC=vagov,DC=local" -> "Laptops.Alaska.vagov.local"
        /// </summary>
        private string ParseDistinguishedNameToFolderName(string dn)
        {
            var parts = dn.Split(',')
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            var ous = new List<string>();
            var dcs = new List<string>();

            foreach (var part in parts)
            {
                if (part.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                {
                    ous.Add(part.Substring(3));
                }
                else if (part.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                {
                    dcs.Add(part.Substring(3));
                }
                // Ignore CN= parts for folder naming
            }

            // Build folder name: OUs (already in leaf-to-root order) + last 2 DCs
            var nameParts = new List<string>();
            nameParts.AddRange(ous);
            
            // Add last 2 DC components (or all if less than 2)
            var dcCount = Math.Min(2, dcs.Count);
            if (dcCount > 0)
            {
                nameParts.AddRange(dcs.Take(dcCount));
            }

            if (nameParts.Count == 0)
            {
                // No OUs or DCs found, use sanitized original
                return SanitizeFolderName(dn);
            }

            // Join with dots and sanitize each part
            var result = string.Join(".", nameParts.Select(SanitizeFolderName));

            // Limit total length to avoid path issues (keep it reasonable)
            if (result.Length > 100)
            {
                result = result.Substring(0, 97) + "...";
            }

            return result;
        }

        /// <summary>
        /// Sanitizes a string to be safe for use as a folder name.
        /// Replaces invalid characters and trims.
        /// </summary>
        private string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Unknown";

            // Replace invalid path characters
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new StringBuilder(name.Length);

            foreach (var c in name)
            {
                if (invalidChars.Contains(c))
                {
                    sanitized.Append('_');
                }
                else
                {
                    sanitized.Append(c);
                }
            }

            var result = sanitized.ToString().Trim();

            // Remove trailing dots (Windows doesn't like them)
            result = result.TrimEnd('.');

            // Ensure not empty
            if (string.IsNullOrWhiteSpace(result))
                return "Unknown";

            return result;
        }

        /// <summary>
        /// Exports AD objects (computers, users, printers) to a file - uses filtered/sorted data as displayed
        /// </summary>
        public async Task ExportAdObjectsAsync(IEnumerable<AdObjectInfo> objects, string filePath, 
            ExportFormat format, string sheetName = "Results",
            IDictionary<string, string?>? lastUserEmails = null,
            CancellationToken cancellationToken = default)
        {
            switch (format)
            {
                case ExportFormat.Excel:
                    await ExportAdObjectsToExcelAsync(objects, filePath, sheetName, lastUserEmails, cancellationToken);
                    break;
                case ExportFormat.Csv:
                    await ExportAdObjectsToCsvAsync(objects, filePath, lastUserEmails, cancellationToken);
                    break;
                case ExportFormat.Json:
                    await ExportToJsonAsync(objects, filePath, cancellationToken);
                    break;
                case ExportFormat.Text:
                    await ExportAdObjectsToTextAsync(objects, filePath, sheetName, lastUserEmails, cancellationToken);
                    break;
                case ExportFormat.Html:
                    await ExportAdObjectsToHtmlAsync(objects, filePath, sheetName, lastUserEmails, cancellationToken);
                    break;
                case ExportFormat.Clipboard:
                    await ExportAdObjectsToClipboardAsync(objects, lastUserEmails, cancellationToken);
                    break;
            }
        }

        /// <summary>
        /// Exports vulnerability rows to a file.
        /// </summary>
        public async Task ExportVulnerabilitiesAsync(IEnumerable<VulnerabilityItem> items, string filePath,
            ExportFormat format, string title = "Vulnerabilities", CancellationToken cancellationToken = default)
        {
            var rows = BuildVulnerabilityRows(items).ToList();

            switch (format)
            {
                case ExportFormat.Excel:
                    await ExportVulnerabilitiesToExcelAsync(rows, filePath, cancellationToken);
                    break;
                case ExportFormat.Csv:
                    await ExportVulnerabilitiesToCsvAsync(rows, filePath, cancellationToken);
                    break;
                case ExportFormat.Json:
                    await ExportToJsonAsync(rows, filePath, cancellationToken);
                    break;
                case ExportFormat.Text:
                    await ExportVulnerabilitiesToTextAsync(rows, filePath, title, cancellationToken);
                    break;
                case ExportFormat.Html:
                    await ExportVulnerabilitiesToHtmlAsync(rows, filePath, title, cancellationToken);
                    break;
                case ExportFormat.Clipboard:
                    await ExportVulnerabilitiesToClipboardAsync(rows, cancellationToken);
                    break;
            }
        }

        /// <summary>
        /// Looks up the cached email for a computer's last user. Returns empty string when
        /// the lookup map is null/empty, the user is missing, or the LastUser value is a
        /// status placeholder (e.g. "(no user)").
        /// </summary>
        private static string GetLastUserEmail(AdObjectInfo obj, IDictionary<string, string?>? emails)
        {
            if (emails == null || emails.Count == 0) return string.Empty;
            if (obj.ObjectType != AdObjectType.Computer) return string.Empty;
            var user = obj.LastUser;
            if (string.IsNullOrWhiteSpace(user) || user.StartsWith("(")) return string.Empty;
            // Strip any DOMAIN\ prefix just in case
            var slash = user.IndexOf('\\');
            if (slash >= 0) user = user.Substring(slash + 1);
            return emails.TryGetValue(user, out var email) ? (email ?? string.Empty) : string.Empty;
        }

        #region Excel Export

        private async Task ExportAdObjectsToExcelAsync(IEnumerable<AdObjectInfo> objects, string filePath,
            string sheetName, IDictionary<string, string?>? lastUserEmails, CancellationToken cancellationToken)
        {
            var includeLastUserEmail = lastUserEmails != null;
            await Task.Run(() =>
            {
                using var workbook = new XLWorkbook();
                var objectsList = objects.ToList();
                
                // Determine what types we have
                var hasComputers = objectsList.Any(o => o.ObjectType == AdObjectType.Computer);
                var hasUsers = objectsList.Any(o => o.ObjectType == AdObjectType.User);
                var hasPrinters = objectsList.Any(o => o.ObjectType == AdObjectType.Printer);
                var mixedTypes = (hasComputers ? 1 : 0) + (hasUsers ? 1 : 0) + (hasPrinters ? 1 : 0) > 1;

                var worksheet = workbook.Worksheets.Add(sheetName);

                // Build dynamic headers based on object types present - matching UI column order
                var headers = new List<string> { "Name" };
                
                if (mixedTypes)
                    headers.Add("Type");
                
                // Printers: Share before IP
                if (hasPrinters)
                    headers.Add("Share Name");
                
                // IP Address for computers/printers
                if (hasComputers || hasPrinters)
                    headers.Add("IP Address");
                
                headers.Add("Description");
                headers.Add("Enabled");
                
                // Computer-specific columns in UI order: Source, Last User, [Last User Email], Last Activity
                if (hasComputers)
                {
                    headers.Add("Source Path");
                    headers.Add("Last User");
                    if (includeLastUserEmail) headers.Add("Last User Email");
                    headers.Add("Last Activity");
                }
                
                // User-specific columns in UI order: SAM, Email, Computer History, Title, Dept, Manager, Source, Last Logon
                if (hasUsers)
                {
                    headers.Add("SAM Account Name");
                    headers.Add("Email");
                    headers.Add("Computer History");
                    headers.Add("Title");
                    headers.Add("Department");
                    headers.Add("Manager");
                    headers.Add("Source Path");
                    headers.Add("AD Logon");
                }
                
                // Printer-specific columns in UI order: Driver/Model, UNC Path, Location
                if (hasPrinters)
                {
                    headers.Add("Driver/Model");
                    headers.Add("UNC Path");
                    headers.Add("Location");
                }

                // Write headers
                for (var i = 0; i < headers.Count; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                    worksheet.Cell(1, i + 1).Style.Font.Bold = true;
                }

                // Write data
                var row = 2;
                foreach (var obj in objectsList)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var col = 1;
                    worksheet.Cell(row, col++).Value = obj.DisplayName ?? obj.Name;
                    
                    if (mixedTypes)
                        worksheet.Cell(row, col++).Value = obj.ObjectTypeDisplay;
                    
                    // Printers: Share before IP
                    if (hasPrinters)
                        worksheet.Cell(row, col++).Value = obj.ObjectType == AdObjectType.Printer ? (obj.ShareName ?? "") : "";
                    
                    // IP Address for computers/printers
                    if (hasComputers || hasPrinters)
                        worksheet.Cell(row, col++).Value = (obj.ObjectType == AdObjectType.Computer || obj.ObjectType == AdObjectType.Printer) ? (obj.IpAddress ?? "") : "";
                    
                    worksheet.Cell(row, col++).Value = obj.Description ?? "";
                    worksheet.Cell(row, col++).Value = obj.IsEnabled ? "Yes" : "No";
                    
                    // Computer-specific data in UI order
                    if (hasComputers)
                    {
                        worksheet.Cell(row, col++).Value = obj.ObjectType == AdObjectType.Computer ? TargetPath.GetParentPath(obj.DistinguishedName) : "";
                        worksheet.Cell(row, col++).Value = obj.ObjectType == AdObjectType.Computer ? (obj.LastUserDisplay ?? "") : "";
                        if (includeLastUserEmail)
                            worksheet.Cell(row, col++).Value = GetLastUserEmail(obj, lastUserEmails);
                        worksheet.Cell(row, col++).Value = obj.ObjectType == AdObjectType.Computer ? (obj.LastActivity?.ToLocalTime().ToString("g") ?? "") : "";
                    }
                    
                    // User-specific data in UI order
                    if (hasUsers)
                    {
                        worksheet.Cell(row, col++).Value = obj.ObjectType == AdObjectType.User ? (obj.SamAccountName ?? "") : "";
                        worksheet.Cell(row, col++).Value = obj.ObjectType == AdObjectType.User ? (obj.Mail ?? "") : "";
                        worksheet.Cell(row, col++).Value = obj.ObjectType == AdObjectType.User ? (obj.ComputerHistoryDisplay ?? "") : "";
                        worksheet.Cell(row, col++).Value = obj.ObjectType == AdObjectType.User ? (obj.Title ?? "") : "";
                        worksheet.Cell(row, col++).Value = obj.ObjectType == AdObjectType.User ? (obj.Department ?? "") : "";
                        worksheet.Cell(row, col++).Value = obj.ObjectType == AdObjectType.User ? ExtractCn(obj.Manager) : "";
                        worksheet.Cell(row, col++).Value = obj.ObjectType == AdObjectType.User ? TargetPath.GetParentPath(obj.DistinguishedName) : "";
                        worksheet.Cell(row, col++).Value = obj.ObjectType == AdObjectType.User ? (obj.LastLogon?.ToLocalTime().ToString("g") ?? "") : "";
                    }
                    
                    // Printer-specific data in UI order
                    if (hasPrinters)
                    {
                        worksheet.Cell(row, col++).Value = obj.ObjectType == AdObjectType.Printer ? (obj.DriverName ?? "") : "";
                        worksheet.Cell(row, col++).Value = obj.ObjectType == AdObjectType.Printer ? (obj.UNCName ?? "") : "";
                        worksheet.Cell(row, col++).Value = obj.ObjectType == AdObjectType.Printer ? (obj.Location ?? "") : "";
                    }
                    
                    row++;
                }

                worksheet.Columns().AdjustToContents();
                worksheet.SheetView.FreezeRows(1);

                workbook.SaveAs(filePath);
            }, cancellationToken);
        }

        /// <summary>
        /// Extracts the CN (common name) from a Distinguished Name
        /// </summary>
        private static string ExtractCn(string? distinguishedName)
        {
            if (string.IsNullOrEmpty(distinguishedName)) return "";
            
            if (distinguishedName.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                var commaIndex = distinguishedName.IndexOf(',');
                return commaIndex > 3 
                    ? distinguishedName.Substring(3, commaIndex - 3) 
                    : distinguishedName.Substring(3);
            }
            return distinguishedName;
        }

        #endregion

        #region CSV Export

        private async Task ExportAdObjectsToCsvAsync(IEnumerable<AdObjectInfo> objects, string filePath,
            IDictionary<string, string?>? lastUserEmails, CancellationToken cancellationToken)
        {
            var includeLastUserEmail = lastUserEmails != null;
            var objectsList = objects.ToList();
            var hasComputers = objectsList.Any(o => o.ObjectType == AdObjectType.Computer);
            var hasUsers = objectsList.Any(o => o.ObjectType == AdObjectType.User);
            var hasPrinters = objectsList.Any(o => o.ObjectType == AdObjectType.Printer);
            var mixedTypes = (hasComputers ? 1 : 0) + (hasUsers ? 1 : 0) + (hasPrinters ? 1 : 0) > 1;

            var sb = new StringBuilder();
            
            // Build header - matching UI column order
            var headers = new List<string> { "Name" };
            if (mixedTypes) headers.Add("Type");
            if (hasPrinters) headers.Add("Share Name");
            if (hasComputers || hasPrinters) headers.Add("IP Address");
            headers.Add("Description");
            headers.Add("Enabled");
            // Computer columns in UI order
            if (hasComputers) { headers.Add("Source Path"); headers.Add("Last User"); if (includeLastUserEmail) headers.Add("Last User Email"); headers.Add("Last Activity"); }
            // User columns in UI order
            if (hasUsers) { headers.Add("SAM Account Name"); headers.Add("Email"); headers.Add("Computer History"); headers.Add("Title"); headers.Add("Department"); headers.Add("Manager"); headers.Add("Source Path"); headers.Add("AD Logon"); }
            // Printer columns in UI order
            if (hasPrinters) { headers.Add("Driver/Model"); headers.Add("UNC Path"); headers.Add("Location"); }
            
            sb.AppendLine(string.Join(",", headers));

            foreach (var obj in objectsList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = new List<string> { Escape(obj.DisplayName ?? obj.Name) };
                if (mixedTypes) values.Add(Escape(obj.ObjectTypeDisplay));
                if (hasPrinters) values.Add(obj.ObjectType == AdObjectType.Printer ? Escape(obj.ShareName) : "");
                if (hasComputers || hasPrinters) values.Add((obj.ObjectType == AdObjectType.Computer || obj.ObjectType == AdObjectType.Printer) ? Escape(obj.IpAddress) : "");
                values.Add(Escape(obj.Description));
                values.Add(obj.IsEnabled ? "Yes" : "No");
                // Computer data in UI order
                if (hasComputers) 
                { 
                    values.Add(obj.ObjectType == AdObjectType.Computer ? Escape(TargetPath.GetParentPath(obj.DistinguishedName)) : ""); 
                    values.Add(obj.ObjectType == AdObjectType.Computer ? Escape(obj.LastUserDisplay) : ""); 
                    if (includeLastUserEmail) values.Add(Escape(GetLastUserEmail(obj, lastUserEmails))); 
                    values.Add(obj.ObjectType == AdObjectType.Computer ? (obj.LastActivity?.ToLocalTime().ToString("g") ?? "") : ""); 
                }
                // User data in UI order
                if (hasUsers) 
                { 
                    values.Add(obj.ObjectType == AdObjectType.User ? Escape(obj.SamAccountName) : ""); 
                    values.Add(obj.ObjectType == AdObjectType.User ? Escape(obj.Mail) : ""); 
                    values.Add(obj.ObjectType == AdObjectType.User ? Escape(obj.ComputerHistoryDisplay) : ""); 
                    values.Add(obj.ObjectType == AdObjectType.User ? Escape(obj.Title) : ""); 
                    values.Add(obj.ObjectType == AdObjectType.User ? Escape(obj.Department) : ""); 
                    values.Add(obj.ObjectType == AdObjectType.User ? Escape(ExtractCn(obj.Manager)) : ""); 
                    values.Add(obj.ObjectType == AdObjectType.User ? Escape(TargetPath.GetParentPath(obj.DistinguishedName)) : ""); 
                    values.Add(obj.ObjectType == AdObjectType.User ? (obj.LastLogon?.ToLocalTime().ToString("g") ?? "") : ""); 
                }
                // Printer data in UI order
                if (hasPrinters) 
                { 
                    values.Add(obj.ObjectType == AdObjectType.Printer ? Escape(obj.DriverName) : ""); 
                    values.Add(obj.ObjectType == AdObjectType.Printer ? Escape(obj.UNCName) : ""); 
                    values.Add(obj.ObjectType == AdObjectType.Printer ? Escape(obj.Location) : ""); 
                }
                
                sb.AppendLine(string.Join(",", values));
            }

            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, cancellationToken);
        }

        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }

        #endregion

        #region JSON Export

        private async Task ExportToJsonAsync<T>(T data, string filePath, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8, cancellationToken);
        }

        #endregion

        #region Text Export

        private async Task ExportAdObjectsToTextAsync(IEnumerable<AdObjectInfo> objects, string filePath,
            string title, IDictionary<string, string?>? lastUserEmails, CancellationToken cancellationToken)
        {
            var includeLastUserEmail = lastUserEmails != null;
            var sb = new StringBuilder();
            sb.AppendLine($"Active Scanner - {title} Export");
            sb.AppendLine($"Generated: {DateTime.Now:g}");
            sb.AppendLine(new string('=', 80));
            sb.AppendLine();

            foreach (var obj in objects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine($"Name:             {obj.DisplayName ?? obj.Name}");
                sb.AppendLine($"Type:             {obj.ObjectTypeDisplay}");
                
                if (obj.ObjectType == AdObjectType.Computer)
                {
                    // Computer columns in UI order: Name, IP, Description, Enabled, Source, Last User, Last Activity
                    sb.AppendLine($"IP Address:       {obj.IpAddress}");
                    sb.AppendLine($"Description:      {obj.Description}");
                    sb.AppendLine($"Enabled:          {(obj.IsEnabled ? "Yes" : "No")}");
                    sb.AppendLine($"Source:           {TargetPath.GetParentPath(obj.DistinguishedName)}");
                    sb.AppendLine($"Last User:        {obj.LastUserDisplay}");
                    if (includeLastUserEmail)
                        sb.AppendLine($"Last User Email:  {GetLastUserEmail(obj, lastUserEmails)}");
                    sb.AppendLine($"Last Activity:    {obj.LastActivity?.ToLocalTime().ToString("g")}");
                }
                else if (obj.ObjectType == AdObjectType.User)
                {
                    // User columns in UI order: Name, Description, Enabled, SAM, Email, Computer History, Title, Dept, Manager, Source, Last Logon
                    sb.AppendLine($"Description:      {obj.Description}");
                    sb.AppendLine($"Enabled:          {(obj.IsEnabled ? "Yes" : "No")}");
                    sb.AppendLine($"SAM Account:      {obj.SamAccountName}");
                    sb.AppendLine($"Email:            {obj.Mail}");
                    sb.AppendLine($"Computer History: {obj.ComputerHistoryDisplay}");
                    sb.AppendLine($"Title:            {obj.Title}");
                    sb.AppendLine($"Department:       {obj.Department}");
                    sb.AppendLine($"Manager:          {ExtractCn(obj.Manager)}");
                    sb.AppendLine($"Source:           {TargetPath.GetParentPath(obj.DistinguishedName)}");
                    sb.AppendLine($"AD Logon:         {obj.LastLogon?.ToLocalTime().ToString("g")}");
                }
                else if (obj.ObjectType == AdObjectType.Printer)
                {
                    // Printer columns in UI order: Name, Share, IP, Description, Driver/Model, UNC Path, Location
                    sb.AppendLine($"Share Name:       {obj.ShareName}");
                    sb.AppendLine($"IP Address:       {obj.IpAddress}");
                    sb.AppendLine($"Description:      {obj.Description}");
                    sb.AppendLine($"Driver/Model:     {obj.DriverName}");
                    sb.AppendLine($"UNC Path:         {obj.UNCName}");
                    sb.AppendLine($"Location:         {obj.Location}");
                }
                
                sb.AppendLine(new string('-', 40));
            }

            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, cancellationToken);
        }

        #endregion

        #region HTML Export

        private async Task ExportAdObjectsToHtmlAsync(IEnumerable<AdObjectInfo> objects, string filePath,
            string title, IDictionary<string, string?>? lastUserEmails, CancellationToken cancellationToken)
        {
            var includeLastUserEmail = lastUserEmails != null;
            var objectsList = objects.ToList();
            var hasComputers = objectsList.Any(o => o.ObjectType == AdObjectType.Computer);
            var hasUsers = objectsList.Any(o => o.ObjectType == AdObjectType.User);
            var hasPrinters = objectsList.Any(o => o.ObjectType == AdObjectType.Printer);
            var mixedTypes = (hasComputers ? 1 : 0) + (hasUsers ? 1 : 0) + (hasPrinters ? 1 : 0) > 1;

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine($"<html><head><title>Active Scanner - {title}</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; margin: 20px; }");
            sb.AppendLine("h1 { color: #1976D2; }");
            sb.AppendLine("table { border-collapse: collapse; width: 100%; }");
            sb.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
            sb.AppendLine("th { background-color: #1976D2; color: white; }");
            sb.AppendLine("tr:nth-child(even) { background-color: #f2f2f2; }");
            sb.AppendLine("tr:hover { background-color: #e3f2fd; }");
            sb.AppendLine(".enabled { color: green; } .disabled { color: red; }");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine($"<h1>Active Scanner - {title}</h1>");
            sb.AppendLine($"<p>Generated: {DateTime.Now:g} | Count: {objectsList.Count}</p>");
            sb.AppendLine("<table>");
            
            // Build headers - matching UI column order
            sb.Append("<tr><th>Name</th>");
            if (mixedTypes) sb.Append("<th>Type</th>");
            if (hasPrinters) sb.Append("<th>Share</th>");
            if (hasComputers || hasPrinters) sb.Append("<th>IP Address</th>");
            sb.Append("<th>Description</th><th>Enabled</th>");
            // Computer headers in UI order
            if (hasComputers)
            {
                sb.Append("<th>Source</th><th>Last User</th>");
                if (includeLastUserEmail) sb.Append("<th>Last User Email</th>");
                sb.Append("<th>Last Activity</th>");
            }
            // User headers in UI order
            if (hasUsers) sb.Append("<th>SAM Account</th><th>Email</th><th>Computer History</th><th>Title</th><th>Department</th><th>Manager</th><th>Source</th><th>AD Logon</th>");
            // Printer headers in UI order
            if (hasPrinters) sb.Append("<th>Driver/Model</th><th>UNC Path</th><th>Location</th>");
            sb.AppendLine("</tr>");

            foreach (var obj in objectsList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var enabledClass = obj.IsEnabled ? "enabled" : "disabled";
                var enabledText = obj.IsEnabled ? "Yes" : "No";
                
                sb.Append($"<tr><td>{HtmlEncode(obj.DisplayName ?? obj.Name)}</td>");
                if (mixedTypes) sb.Append($"<td>{HtmlEncode(obj.ObjectTypeDisplay)}</td>");
                if (hasPrinters) sb.Append($"<td>{(obj.ObjectType == AdObjectType.Printer ? HtmlEncode(obj.ShareName) : "")}</td>");
                if (hasComputers || hasPrinters) sb.Append($"<td>{((obj.ObjectType == AdObjectType.Computer || obj.ObjectType == AdObjectType.Printer) ? HtmlEncode(obj.IpAddress) : "")}</td>");
                sb.Append($"<td>{HtmlEncode(obj.Description)}</td>");
                sb.Append($"<td class=\"{enabledClass}\">{enabledText}</td>");
                // Computer data in UI order
                if (hasComputers)
                {
                    sb.Append($"<td>{(obj.ObjectType == AdObjectType.Computer ? HtmlEncode(TargetPath.GetParentPath(obj.DistinguishedName)) : "")}</td><td>{(obj.ObjectType == AdObjectType.Computer ? HtmlEncode(obj.LastUserDisplay) : "")}</td>");
                    if (includeLastUserEmail) sb.Append($"<td>{HtmlEncode(GetLastUserEmail(obj, lastUserEmails))}</td>");
                    sb.Append($"<td>{(obj.ObjectType == AdObjectType.Computer ? obj.LastActivity?.ToLocalTime().ToString("g") : "")}</td>");
                }
                // User data in UI order
                if (hasUsers) sb.Append($"<td>{(obj.ObjectType == AdObjectType.User ? HtmlEncode(obj.SamAccountName) : "")}</td><td>{(obj.ObjectType == AdObjectType.User ? HtmlEncode(obj.Mail) : "")}</td><td>{(obj.ObjectType == AdObjectType.User ? HtmlEncode(obj.ComputerHistoryDisplay) : "")}</td><td>{(obj.ObjectType == AdObjectType.User ? HtmlEncode(obj.Title) : "")}</td><td>{(obj.ObjectType == AdObjectType.User ? HtmlEncode(obj.Department) : "")}</td><td>{(obj.ObjectType == AdObjectType.User ? HtmlEncode(ExtractCn(obj.Manager)) : "")}</td><td>{(obj.ObjectType == AdObjectType.User ? HtmlEncode(TargetPath.GetParentPath(obj.DistinguishedName)) : "")}</td><td>{(obj.ObjectType == AdObjectType.User ? obj.LastLogon?.ToLocalTime().ToString("g") : "")}</td>");
                // Printer data in UI order
                if (hasPrinters) sb.Append($"<td>{(obj.ObjectType == AdObjectType.Printer ? HtmlEncode(obj.DriverName) : "")}</td><td>{(obj.ObjectType == AdObjectType.Printer ? HtmlEncode(obj.UNCName) : "")}</td><td>{(obj.ObjectType == AdObjectType.Printer ? HtmlEncode(obj.Location) : "")}</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</table></body></html>");

            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, cancellationToken);
        }

        private static string HtmlEncode(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return System.Net.WebUtility.HtmlEncode(value);
        }

        #endregion

        #region Clipboard Export

        private async Task ExportAdObjectsToClipboardAsync(IEnumerable<AdObjectInfo> objects,
            IDictionary<string, string?>? lastUserEmails, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                var includeLastUserEmail = lastUserEmails != null;
                var objectsList = objects.ToList();
                var hasComputers = objectsList.Any(o => o.ObjectType == AdObjectType.Computer);
                var hasUsers = objectsList.Any(o => o.ObjectType == AdObjectType.User);
                var hasPrinters = objectsList.Any(o => o.ObjectType == AdObjectType.Printer);
                var mixedTypes = (hasComputers ? 1 : 0) + (hasUsers ? 1 : 0) + (hasPrinters ? 1 : 0) > 1;

                var sb = new StringBuilder();
                
                // Build header - matching UI column order
                var headers = new List<string> { "Name" };
                if (mixedTypes) headers.Add("Type");
                if (hasPrinters) headers.Add("Share");
                if (hasComputers || hasPrinters) headers.Add("IP Address");
                headers.Add("Description");
                headers.Add("Enabled");
                // Computer headers in UI order
                if (hasComputers) { headers.Add("Source"); headers.Add("Last User"); if (includeLastUserEmail) headers.Add("Last User Email"); headers.Add("Last Activity"); }
                // User headers in UI order
                if (hasUsers) { headers.Add("SAM Account"); headers.Add("Email"); headers.Add("Computer History"); headers.Add("Title"); headers.Add("Department"); headers.Add("Manager"); headers.Add("Source"); headers.Add("AD Logon"); }
                // Printer headers in UI order
                if (hasPrinters) { headers.Add("Driver/Model"); headers.Add("UNC Path"); headers.Add("Location"); }
                
                sb.AppendLine(string.Join("\t", headers));

                foreach (var obj in objectsList)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var values = new List<string> { obj.DisplayName ?? obj.Name };
                    if (mixedTypes) values.Add(obj.ObjectTypeDisplay);
                    if (hasPrinters) values.Add(obj.ObjectType == AdObjectType.Printer ? (obj.ShareName ?? "") : "");
                    if (hasComputers || hasPrinters) values.Add((obj.ObjectType == AdObjectType.Computer || obj.ObjectType == AdObjectType.Printer) ? (obj.IpAddress ?? "") : "");
                    values.Add(obj.Description ?? "");
                    values.Add(obj.IsEnabled ? "Yes" : "No");
                    // Computer data in UI order
                    if (hasComputers) { values.Add(obj.ObjectType == AdObjectType.Computer ? TargetPath.GetParentPath(obj.DistinguishedName) : ""); values.Add(obj.ObjectType == AdObjectType.Computer ? (obj.LastUserDisplay ?? "") : ""); if (includeLastUserEmail) values.Add(GetLastUserEmail(obj, lastUserEmails)); values.Add(obj.ObjectType == AdObjectType.Computer ? (obj.LastActivity?.ToLocalTime().ToString("g") ?? "") : ""); }
                    // User data in UI order
                    if (hasUsers) { values.Add(obj.ObjectType == AdObjectType.User ? (obj.SamAccountName ?? "") : ""); values.Add(obj.ObjectType == AdObjectType.User ? (obj.Mail ?? "") : ""); values.Add(obj.ObjectType == AdObjectType.User ? (obj.ComputerHistoryDisplay ?? "") : ""); values.Add(obj.ObjectType == AdObjectType.User ? (obj.Title ?? "") : ""); values.Add(obj.ObjectType == AdObjectType.User ? (obj.Department ?? "") : ""); values.Add(obj.ObjectType == AdObjectType.User ? ExtractCn(obj.Manager) : ""); values.Add(obj.ObjectType == AdObjectType.User ? TargetPath.GetParentPath(obj.DistinguishedName) : ""); values.Add(obj.ObjectType == AdObjectType.User ? (obj.LastLogon?.ToLocalTime().ToString("g") ?? "") : ""); }
                    // Printer data in UI order
                    if (hasPrinters) { values.Add(obj.ObjectType == AdObjectType.Printer ? (obj.DriverName ?? "") : ""); values.Add(obj.ObjectType == AdObjectType.Printer ? (obj.UNCName ?? "") : ""); values.Add(obj.ObjectType == AdObjectType.Printer ? (obj.Location ?? "") : ""); }
                    
                    sb.AppendLine(string.Join("\t", values));
                }

                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(sb.ToString()));
            }, cancellationToken);
        }

        #endregion

        #region Vulnerability Export

        private sealed class VulnerabilityExportRow
        {
            public string Device { get; set; } = string.Empty;
            public string Fqdn { get; set; } = string.Empty;
            public string Number { get; set; } = string.Empty;
            public string TenId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Component { get; set; } = string.Empty;
            public string Method { get; set; } = string.Empty;
            public string Fix { get; set; } = string.Empty;
            public string PackageId { get; set; } = string.Empty;
            public string InstallType { get; set; } = string.Empty;
            public string InstalledVersion { get; set; } = string.Empty;
            public string FixedVersion { get; set; } = string.Empty;
            public string VerifiedVersion { get; set; } = string.Empty;
            public string Eligibility { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string StatusText { get; set; } = string.Empty;
            public string RiskRating { get; set; } = string.Empty;
            public string RiskScore { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public bool HasMapping { get; set; }
            public bool CanPush { get; set; }
            public bool RebootAfter { get; set; }
            public string ProofPath { get; set; } = string.Empty;
        }

        private static readonly string[] VulnerabilityHeaders =
        {
            "Device", "FQDN", "VIT", "TEN", "Name", "Component", "Method", "Fix", "Package/Fix Id",
            "Install Type", "Installed", "Fixed", "Verified", "Eligibility", "Status", "Status Detail",
            "Risk", "Score", "State", "Mapped", "Can Push", "Reboot", "Proof Path"
        };

        private static IEnumerable<VulnerabilityExportRow> BuildVulnerabilityRows(IEnumerable<VulnerabilityItem> items)
        {
            return items.Select(i => new VulnerabilityExportRow
            {
                Device = i.DeviceName,
                Fqdn = i.Fqdn,
                Number = i.Number,
                TenId = i.TenId,
                Name = i.Name,
                Component = i.Component,
                Method = i.Method,
                Fix = i.FixLabel,
                PackageId = i.SuggestedPackageId,
                InstallType = i.InstallTypeLabel,
                InstalledVersion = i.InstalledVersion,
                FixedVersion = i.FixedVersion,
                VerifiedVersion = i.VerifiedVersion,
                Eligibility = i.EligibilityLabel,
                Status = i.Status.ToString(),
                StatusText = i.StatusText,
                RiskRating = i.RiskRating,
                RiskScore = i.RiskScore,
                State = i.State,
                HasMapping = i.HasMapping,
                CanPush = i.CanPush,
                RebootAfter = i.RebootAfter,
                ProofPath = i.ProofPath
            });
        }

        private async Task ExportVulnerabilitiesToExcelAsync(IReadOnlyList<VulnerabilityExportRow> rows, string filePath,
            CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Vulnerabilities");

                for (var i = 0; i < VulnerabilityHeaders.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = VulnerabilityHeaders[i];
                    worksheet.Cell(1, i + 1).Style.Font.Bold = true;
                }

                var row = 2;
                foreach (var it in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var col = 1;
                    worksheet.Cell(row, col++).Value = it.Device;
                    worksheet.Cell(row, col++).Value = it.Fqdn;
                    worksheet.Cell(row, col++).Value = it.Number;
                    worksheet.Cell(row, col++).Value = it.TenId;
                    worksheet.Cell(row, col++).Value = it.Name;
                    worksheet.Cell(row, col++).Value = it.Component;
                    worksheet.Cell(row, col++).Value = it.Method;
                    worksheet.Cell(row, col++).Value = it.Fix;
                    worksheet.Cell(row, col++).Value = it.PackageId;
                    worksheet.Cell(row, col++).Value = it.InstallType;
                    worksheet.Cell(row, col++).Value = it.InstalledVersion;
                    worksheet.Cell(row, col++).Value = it.FixedVersion;
                    worksheet.Cell(row, col++).Value = it.VerifiedVersion;
                    worksheet.Cell(row, col++).Value = it.Eligibility;
                    worksheet.Cell(row, col++).Value = it.Status;
                    worksheet.Cell(row, col++).Value = it.StatusText;
                    worksheet.Cell(row, col++).Value = it.RiskRating;
                    worksheet.Cell(row, col++).Value = it.RiskScore;
                    worksheet.Cell(row, col++).Value = it.State;
                    worksheet.Cell(row, col++).Value = it.HasMapping ? "Yes" : "No";
                    worksheet.Cell(row, col++).Value = it.CanPush ? "Yes" : "No";
                    worksheet.Cell(row, col++).Value = it.RebootAfter ? "Yes" : "No";
                    worksheet.Cell(row, col).Value = it.ProofPath;
                    row++;
                }

                worksheet.Columns().AdjustToContents();
                worksheet.SheetView.FreezeRows(1);
                workbook.SaveAs(filePath);
            }, cancellationToken);
        }

        private async Task ExportVulnerabilitiesToCsvAsync(IReadOnlyList<VulnerabilityExportRow> rows, string filePath,
            CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", VulnerabilityHeaders));

            foreach (var it in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = new[]
                {
                    Escape(it.Device), Escape(it.Fqdn), Escape(it.Number), Escape(it.TenId), Escape(it.Name),
                    Escape(it.Component), Escape(it.Method), Escape(it.Fix), Escape(it.PackageId), Escape(it.InstallType),
                    Escape(it.InstalledVersion), Escape(it.FixedVersion), Escape(it.VerifiedVersion),
                    Escape(it.Eligibility), Escape(it.Status), Escape(it.StatusText), Escape(it.RiskRating),
                    Escape(it.RiskScore), Escape(it.State), it.HasMapping ? "Yes" : "No",
                    it.CanPush ? "Yes" : "No", it.RebootAfter ? "Yes" : "No", Escape(it.ProofPath)
                };
                sb.AppendLine(string.Join(",", values));
            }

            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, cancellationToken);
        }

        private async Task ExportVulnerabilitiesToTextAsync(IReadOnlyList<VulnerabilityExportRow> rows, string filePath,
            string title, CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Active Scanner - {title} Export");
            sb.AppendLine($"Generated: {DateTime.Now:g}");
            sb.AppendLine($"Count: {rows.Count}");
            sb.AppendLine(new string('=', 80));
            sb.AppendLine();

            foreach (var it in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine($"Device:           {it.Device}");
                sb.AppendLine($"FQDN:             {it.Fqdn}");
                sb.AppendLine($"VIT/TEN:          {it.Number} / {it.TenId}");
                sb.AppendLine($"Name:             {it.Name}");
                sb.AppendLine($"Component:        {it.Component}");
                sb.AppendLine($"Method/Fix:       {it.Method} / {it.Fix}");
                sb.AppendLine($"Package Id:       {it.PackageId}");
                sb.AppendLine($"Install Type:     {it.InstallType}");
                sb.AppendLine($"Installed/Fixed:  {it.InstalledVersion} -> {it.FixedVersion}");
                sb.AppendLine($"Verified:         {it.VerifiedVersion}");
                sb.AppendLine($"Eligibility:      {it.Eligibility}");
                sb.AppendLine($"Status:           {it.Status}");
                sb.AppendLine($"Status Detail:    {it.StatusText}");
                sb.AppendLine($"Risk/Score/State: {it.RiskRating} / {it.RiskScore} / {it.State}");
                sb.AppendLine($"Mapped/CanPush:   {(it.HasMapping ? "Yes" : "No")} / {(it.CanPush ? "Yes" : "No")}");
                sb.AppendLine($"Reboot:           {(it.RebootAfter ? "Yes" : "No")}");
                sb.AppendLine($"Proof Path:       {it.ProofPath}");
                sb.AppendLine(new string('-', 60));
            }

            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, cancellationToken);
        }

        private async Task ExportVulnerabilitiesToHtmlAsync(IReadOnlyList<VulnerabilityExportRow> rows, string filePath,
            string title, CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine($"<html><head><title>Active Scanner - {HtmlEncode(title)}</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; margin: 20px; }");
            sb.AppendLine("h1 { color: #1976D2; }");
            sb.AppendLine("table { border-collapse: collapse; width: 100%; }");
            sb.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
            sb.AppendLine("th { background-color: #1976D2; color: white; }");
            sb.AppendLine("tr:nth-child(even) { background-color: #f2f2f2; }");
            sb.AppendLine("tr:hover { background-color: #e3f2fd; }");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine($"<h1>Active Scanner - {HtmlEncode(title)}</h1>");
            sb.AppendLine($"<p>Generated: {DateTime.Now:g} | Count: {rows.Count}</p>");
            sb.AppendLine("<table><tr>");
            foreach (var header in VulnerabilityHeaders)
                sb.Append($"<th>{HtmlEncode(header)}</th>");
            sb.AppendLine("</tr>");

            foreach (var it in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.Append("<tr>");
                sb.Append($"<td>{HtmlEncode(it.Device)}</td>");
                sb.Append($"<td>{HtmlEncode(it.Fqdn)}</td>");
                sb.Append($"<td>{HtmlEncode(it.Number)}</td>");
                sb.Append($"<td>{HtmlEncode(it.TenId)}</td>");
                sb.Append($"<td>{HtmlEncode(it.Name)}</td>");
                sb.Append($"<td>{HtmlEncode(it.Component)}</td>");
                sb.Append($"<td>{HtmlEncode(it.Method)}</td>");
                sb.Append($"<td>{HtmlEncode(it.Fix)}</td>");
                sb.Append($"<td>{HtmlEncode(it.PackageId)}</td>");
                sb.Append($"<td>{HtmlEncode(it.InstallType)}</td>");
                sb.Append($"<td>{HtmlEncode(it.InstalledVersion)}</td>");
                sb.Append($"<td>{HtmlEncode(it.FixedVersion)}</td>");
                sb.Append($"<td>{HtmlEncode(it.VerifiedVersion)}</td>");
                sb.Append($"<td>{HtmlEncode(it.Eligibility)}</td>");
                sb.Append($"<td>{HtmlEncode(it.Status)}</td>");
                sb.Append($"<td>{HtmlEncode(it.StatusText)}</td>");
                sb.Append($"<td>{HtmlEncode(it.RiskRating)}</td>");
                sb.Append($"<td>{HtmlEncode(it.RiskScore)}</td>");
                sb.Append($"<td>{HtmlEncode(it.State)}</td>");
                sb.Append($"<td>{(it.HasMapping ? "Yes" : "No")}</td>");
                sb.Append($"<td>{(it.CanPush ? "Yes" : "No")}</td>");
                sb.Append($"<td>{(it.RebootAfter ? "Yes" : "No")}</td>");
                sb.Append($"<td>{HtmlEncode(it.ProofPath)}</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</table></body></html>");
            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, cancellationToken);
        }

        private async Task ExportVulnerabilitiesToClipboardAsync(IReadOnlyList<VulnerabilityExportRow> rows,
            CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine(string.Join("\t", VulnerabilityHeaders));

                foreach (var it in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sb.AppendLine(string.Join("\t", new[]
                    {
                        it.Device, it.Fqdn, it.Number, it.TenId, it.Name, it.Component, it.Method, it.Fix,
                        it.PackageId, it.InstallType, it.InstalledVersion, it.FixedVersion, it.VerifiedVersion,
                        it.Eligibility, it.Status, it.StatusText, it.RiskRating, it.RiskScore, it.State,
                        it.HasMapping ? "Yes" : "No", it.CanPush ? "Yes" : "No", it.RebootAfter ? "Yes" : "No",
                        it.ProofPath
                    }));
                }

                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(sb.ToString()));
            }, cancellationToken);
        }

        #endregion

        #region Cache Export Methods

        /// <summary>
        /// Exports computer cache entries to various formats
        /// </summary>
        public async Task ExportCacheEntriesAsync(IEnumerable<ComputerCacheEntry> entries, string filePath,
            ExportFormat format, CancellationToken cancellationToken = default)
        {
            switch (format)
            {
                case ExportFormat.Excel:
                    await ExportCacheToExcelAsync(entries, filePath, cancellationToken);
                    break;
                case ExportFormat.Csv:
                    await ExportCacheToCsvAsync(entries, filePath, cancellationToken);
                    break;
                case ExportFormat.Json:
                    await ExportCacheToJsonAsync(entries, filePath, cancellationToken);
                    break;
                case ExportFormat.Text:
                    await ExportCacheToTextAsync(entries, filePath, cancellationToken);
                    break;
                case ExportFormat.Html:
                    await ExportCacheToHtmlAsync(entries, filePath, cancellationToken);
                    break;
                case ExportFormat.Clipboard:
                    ExportCacheToClipboard(entries);
                    break;
            }
        }

        private async Task ExportCacheToExcelAsync(IEnumerable<ComputerCacheEntry> entries, string filePath,
            CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("User History");

                // Headers - one row per user per computer
                var headers = new[] { "Computer Name", "Distinguished Name", "Username",
                                      "Date of Scan", "Last Seen in AD" };
                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                    worksheet.Cell(1, i + 1).Style.Font.Bold = true;
                    worksheet.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
                }

                // Data - one row per known user
                int row = 2;
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    if (entry.KnownUsers?.Count > 0)
                    {
                        foreach (var user in entry.KnownUsers.OrderByDescending(u => u.LastSeenLoggedIn))
                        {
                            worksheet.Cell(row, 1).Value = entry.ComputerName;
                            worksheet.Cell(row, 2).Value = entry.DistinguishedName ?? "";
                            worksheet.Cell(row, 3).Value = user.Username;
                            worksheet.Cell(row, 4).Value = user.LastSeenLoggedIn?.ToLocalTime().ToString("g") ?? "";
                            worksheet.Cell(row, 5).Value = entry.LastSeenInAd?.ToLocalTime().ToString("g") ?? "";
                            row++;
                        }
                    }
                    else if (!string.IsNullOrEmpty(entry.LastUser))
                    {
                        // Fallback for entries without KnownUsers (migrated data)
                        worksheet.Cell(row, 1).Value = entry.ComputerName;
                        worksheet.Cell(row, 2).Value = entry.DistinguishedName ?? "";
                        worksheet.Cell(row, 3).Value = entry.LastUser;
                        worksheet.Cell(row, 4).Value = entry.LastUserQueriedAt?.ToLocalTime().ToString("g") ?? "";
                        worksheet.Cell(row, 5).Value = entry.LastSeenInAd?.ToLocalTime().ToString("g") ?? "";
                        row++;
                    }
                }

                worksheet.Columns().AdjustToContents();
                workbook.SaveAs(filePath);
            }, cancellationToken);
        }

        private async Task ExportCacheToCsvAsync(IEnumerable<ComputerCacheEntry> entries, string filePath,
            CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("Computer Name,Distinguished Name,Username,Date of Scan,Last Seen in AD");

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    if (entry.KnownUsers?.Count > 0)
                    {
                        foreach (var user in entry.KnownUsers.OrderByDescending(u => u.LastSeenLoggedIn))
                        {
                            sb.AppendLine($"\"{entry.ComputerName}\",\"{entry.DistinguishedName ?? ""}\",\"{user.Username}\"," +
                                $"\"{user.LastSeenLoggedIn?.ToLocalTime().ToString("g") ?? ""}\",\"{entry.LastSeenInAd?.ToLocalTime().ToString("g") ?? ""}\"");
                        }
                    }
                    else if (!string.IsNullOrEmpty(entry.LastUser))
                    {
                        sb.AppendLine($"\"{entry.ComputerName}\",\"{entry.DistinguishedName ?? ""}\",\"{entry.LastUser}\"," +
                            $"\"{entry.LastUserQueriedAt?.ToLocalTime().ToString("g") ?? ""}\",\"{entry.LastSeenInAd?.ToLocalTime().ToString("g") ?? ""}\"");
                    }
                }

                File.WriteAllText(filePath, sb.ToString());
            }, cancellationToken);
        }

        private async Task ExportCacheToJsonAsync(IEnumerable<ComputerCacheEntry> entries, string filePath,
            CancellationToken cancellationToken)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(entries, options);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);
        }

        private async Task ExportCacheToTextAsync(IEnumerable<ComputerCacheEntry> entries, string filePath,
            CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("User History Export");
                sb.AppendLine($"Exported: {DateTime.Now:g}");
                sb.AppendLine(new string('=', 60));
                sb.AppendLine();

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sb.AppendLine($"Computer: {entry.ComputerName}");
                    if (!string.IsNullOrEmpty(entry.DistinguishedName))
                        sb.AppendLine($"  DN: {entry.DistinguishedName}");
                    if (entry.LastSeenInAd.HasValue)
                        sb.AppendLine($"  Last Seen in AD: {entry.LastSeenInAd.Value.ToLocalTime():g}");
                    
                    if (entry.KnownUsers?.Count > 0)
                    {
                        sb.AppendLine($"  Known Users ({entry.KnownUsers.Count}):");
                        foreach (var user in entry.KnownUsers.OrderByDescending(u => u.LastSeenLoggedIn))
                        {
                            var dateOfScan = user.LastSeenLoggedIn.HasValue ? $" - Date of Scan: {user.LastSeenLoggedIn.Value.ToLocalTime():g}" : "";
                            sb.AppendLine($"    - {user.Username}{dateOfScan}");
                        }
                    }
                    else if (!string.IsNullOrEmpty(entry.LastUser))
                    {
                        sb.AppendLine($"  Last User: {entry.LastUser}");
                    }
                    sb.AppendLine();
                }

                File.WriteAllText(filePath, sb.ToString());
            }, cancellationToken);
        }

        private async Task ExportCacheToHtmlAsync(IEnumerable<ComputerCacheEntry> entries, string filePath,
            CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("<!DOCTYPE html>");
                sb.AppendLine("<html><head><title>User History</title>");
                sb.AppendLine("<style>");
                sb.AppendLine("body { font-family: 'Segoe UI', sans-serif; margin: 20px; }");
                sb.AppendLine("table { border-collapse: collapse; width: 100%; }");
                sb.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
                sb.AppendLine("th { background-color: #4472C4; color: white; }");
                sb.AppendLine("tr:nth-child(even) { background-color: #f2f2f2; }");
                sb.AppendLine("</style></head><body>");
                sb.AppendLine($"<h1>User History</h1>");
                sb.AppendLine($"<p>Exported: {DateTime.Now:g}</p>");
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Computer Name</th><th>Distinguished Name</th><th>Username</th>" +
                              "<th>Date of Scan</th><th>Last Seen in AD</th></tr>");

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    if (entry.KnownUsers?.Count > 0)
                    {
                        foreach (var user in entry.KnownUsers.OrderByDescending(u => u.LastSeenLoggedIn))
                        {
                            sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(entry.ComputerName)}</td>" +
                                $"<td>{System.Net.WebUtility.HtmlEncode(entry.DistinguishedName ?? "")}</td>" +
                                $"<td>{System.Net.WebUtility.HtmlEncode(user.Username)}</td>" +
                                $"<td>{user.LastSeenLoggedIn?.ToLocalTime().ToString("g") ?? ""}</td>" +
                                $"<td>{entry.LastSeenInAd?.ToLocalTime().ToString("g") ?? ""}</td></tr>");
                        }
                    }
                    else if (!string.IsNullOrEmpty(entry.LastUser))
                    {
                        sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(entry.ComputerName)}</td>" +
                            $"<td>{System.Net.WebUtility.HtmlEncode(entry.DistinguishedName ?? "")}</td>" +
                            $"<td>{System.Net.WebUtility.HtmlEncode(entry.LastUser)}</td>" +
                            $"<td>{entry.LastUserQueriedAt?.ToLocalTime().ToString("g") ?? ""}</td>" +
                            $"<td>{entry.LastSeenInAd?.ToLocalTime().ToString("g") ?? ""}</td></tr>");
                    }
                }

                sb.AppendLine("</table></body></html>");
                File.WriteAllText(filePath, sb.ToString());
            }, cancellationToken);
        }

        private void ExportCacheToClipboard(IEnumerable<ComputerCacheEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Computer Name\tDistinguished Name\tUsername\tDate of Scan\tLast Seen in AD");

            foreach (var entry in entries)
            {
                if (entry.KnownUsers?.Count > 0)
                {
                    foreach (var user in entry.KnownUsers.OrderByDescending(u => u.LastSeenLoggedIn))
                    {
                        sb.AppendLine($"{entry.ComputerName}\t{entry.DistinguishedName ?? ""}\t{user.Username}\t" +
                            $"{user.LastSeenLoggedIn?.ToLocalTime().ToString("g") ?? ""}\t{entry.LastSeenInAd?.ToLocalTime().ToString("g") ?? ""}");
                    }
                }
                else if (!string.IsNullOrEmpty(entry.LastUser))
                {
                    sb.AppendLine($"{entry.ComputerName}\t{entry.DistinguishedName ?? ""}\t{entry.LastUser}\t" +
                        $"{entry.LastUserQueriedAt?.ToLocalTime().ToString("g") ?? ""}\t{entry.LastSeenInAd?.ToLocalTime().ToString("g") ?? ""}");
                }
            }

            Clipboard.SetText(sb.ToString());
        }

        #endregion
    }
}
