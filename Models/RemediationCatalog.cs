using System.Collections.Generic;

namespace ActiveScanner.Models
{
    /// <summary>
    /// A single remediation rule: a matcher that identifies a family of vulnerable items plus the
    /// concrete fix to apply. One rule intentionally covers many TENs (e.g. every .NET runtime CVE
    /// maps to the same runtime upgrade), which is what lets the UI collapse many findings into one
    /// actionable fix.
    /// </summary>
    public class RemediationRule
    {
        /// <summary>Stable id for the rule (used as the grouping key).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>User-facing component name, e.g. ".NET Runtime", "Google Chrome".</summary>
        public string Component { get; set; } = string.Empty;

        /// <summary>Remediation mechanism: winget | msix-direct | dotnet-lts-policy | vendor-msi | manual.</summary>
        public string Method { get; set; } = "winget";

        /// <summary>winget/package id when Method is winget.</summary>
        public string PackageId { get; set; } = string.Empty;

        /// <summary>machine | user (winget --scope).</summary>
        public string Scope { get; set; } = "machine";

        public bool RequiresReboot { get; set; }

        public string Notes { get; set; } = string.Empty;

        // ---- Matchers (evaluated by priority: TenId > Cpe > Path > Product) ----

        /// <summary>Exact Tenable id match (most specific), e.g. "TEN-306445".</summary>
        public string? TenId { get; set; }

        /// <summary>Case-insensitive substring matched against the CPE (software_raw).</summary>
        public string? CpeContains { get; set; }

        /// <summary>Regex matched against the proof install path.</summary>
        public string? PathRegex { get; set; }

        /// <summary>Regex matched against the finding name/short description (for families the
        /// path/CPE don't cleanly capture, e.g. Office Click-to-Run "…Products C2R" titles).</summary>
        public string? NameRegex { get; set; }

        /// <summary>Case-insensitive substring matched against product/name/short description.</summary>
        public string? ProductContains { get; set; }
    }

    /// <summary>
    /// The remediation catalog: an ordered list of rules. Persisted as JSON and user-editable
    /// (import/export). Ships with a seeded default set.
    /// </summary>
    public class RemediationCatalog
    {
        /// <summary>
        /// Seed schema version. Bumped when the built-in seed rules change so an older on-disk
        /// catalog is regenerated instead of silently keeping stale defaults.
        /// </summary>
        public int SchemaVersion { get; set; }

        public List<RemediationRule> Rules { get; set; } = new();
    }
}
