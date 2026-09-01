using System;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents an Active Directory target path for queries
    /// </summary>
    public class TargetPath
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string DisplayName { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public string DomainName { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;

        public TargetPath() { }

        public TargetPath(string dn, string? displayName = null, string? domainName = null)
        {
            DistinguishedName = dn;
            DisplayName = displayName ?? ExtractDisplayName(dn);
            DomainName = domainName ?? ExtractDomainName(dn);
        }

        /// <summary>
        /// Full display path including domain (formatted like "domain.com / OU1 / OU2")
        /// </summary>
        public string DisplayPath => !string.IsNullOrEmpty(DomainName) 
            ? $"{DomainName} / {DisplayName}" 
            : DisplayName;

        private static string ExtractDisplayName(string dn)
        {
            // Extract OU/CN components and build a friendly path like "Laptops/Anchorage/Alaska"
            if (string.IsNullOrEmpty(dn)) return string.Empty;
            
            var pathParts = new System.Collections.Generic.List<string>();
            foreach (var part in dn.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                {
                    pathParts.Add(trimmed.Substring(3));
                }
                else if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                {
                    pathParts.Add(trimmed.Substring(3));
                }
            }
            
            // Reverse to show from root to leaf (e.g., "Alaska / Anchorage / Laptops")
            pathParts.Reverse();
            return pathParts.Count > 0 ? string.Join(" / ", pathParts) : dn;
        }

        private static string ExtractDomainName(string dn)
        {
            // Extract domain from DN (e.g., DC=va,DC=gov -> va.gov)
            if (string.IsNullOrEmpty(dn)) return string.Empty;

            var dcParts = new System.Collections.Generic.List<string>();
            foreach (var part in dn.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                {
                    dcParts.Add(trimmed.Substring(3));
                }
            }

            return dcParts.Count > 0 ? string.Join(".", dcParts) : string.Empty;
        }

        /// <summary>
        /// Gets the parent container path from a DN (excludes the object's own name)
        /// </summary>
        public static string GetParentPath(string? dn)
        {
            if (string.IsNullOrEmpty(dn)) return string.Empty;
            
            // Remove the first RDN (CN=objectname or OU=first) to get parent container
            var firstComma = dn.IndexOf(',');
            if (firstComma < 0) return string.Empty;
            
            var parentDn = dn.Substring(firstComma + 1).TrimStart();
            return new TargetPath(parentDn).DisplayPath;
        }

        public override string ToString() => DisplayPath;
    }
}
