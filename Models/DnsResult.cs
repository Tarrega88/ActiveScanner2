using System;
using System.Collections.Generic;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Result of a DNS resolution operation
    /// </summary>
    public class DnsResult
    {
        public string TargetName { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
        public string QueryType { get; set; } = string.Empty; // Forward or Reverse
        public bool Success { get; set; }
        public string? HostName { get; set; }
        public List<string> IpAddresses { get; set; } = new();
        public List<string> Aliases { get; set; } = new();
        public string Status { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? ErrorMessage { get; set; }

        public string IpAddressesDisplay => string.Join(", ", IpAddresses);
        public string AliasesDisplay => string.Join(", ", Aliases);
    }
}
