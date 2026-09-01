using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ActiveScanner.Models;

namespace ActiveScanner.Services
{
    /// <summary>
    /// Loads, persists and applies the remediation catalog (vulnerability -> concrete fix mappings).
    /// The catalog is keyed by reusable component rules rather than per-TEN, so one rule collapses
    /// many findings into a single fix. Ships a seeded default; user copy lives in %AppData%.
    /// </summary>
    public class RemediationCatalogService
    {
        private readonly string _catalogPath;
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        /// <summary>Bump when the seed rules change so older on-disk catalogs get regenerated.</summary>
        private const int SeedVersion = 3;

        public RemediationCatalog Catalog { get; private set; } = new();

        public string CatalogFilePath => _catalogPath;

        public RemediationCatalogService()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ActiveScanner");
            Directory.CreateDirectory(dir);
            _catalogPath = Path.Combine(dir, "remediation_catalog.json");
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_catalogPath))
                {
                    var json = File.ReadAllText(_catalogPath);
                    var loaded = JsonSerializer.Deserialize<RemediationCatalog>(json, JsonOpts);
                    if (loaded?.Rules is { Count: > 0 } && loaded.SchemaVersion >= SeedVersion)
                    {
                        Catalog = loaded;
                        return;
                    }
                }
            }
            catch { /* fall through to seed */ }

            Catalog = BuildSeedCatalog();
            Save();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(_catalogPath, JsonSerializer.Serialize(Catalog, JsonOpts));
            }
            catch { /* best effort */ }
        }

        public void ImportFrom(string path)
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<RemediationCatalog>(json, JsonOpts);
            if (loaded?.Rules != null)
            {
                Catalog = loaded;
                Save();
            }
        }

        public void ExportTo(string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(Catalog, JsonOpts));
        }

        /// <summary>Adds or replaces a rule (by Id) and persists.</summary>
        public void UpsertRule(RemediationRule rule)
        {
            var existing = Catalog.Rules.FirstOrDefault(r =>
                string.Equals(r.Id, rule.Id, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                Catalog.Rules.Remove(existing);
            Catalog.Rules.Add(rule);
            Save();
        }

        /// <summary>
        /// Finds the best-matching rule for an item. Priority: exact TEN id, then CPE, then path
        /// regex, then product/name substring. Returns null when nothing matches.
        /// </summary>
        public RemediationRule? Match(VulnerabilityItem item)
        {
            // 1. Exact TEN id
            var byTen = Catalog.Rules.FirstOrDefault(r =>
                !string.IsNullOrWhiteSpace(r.TenId) &&
                string.Equals(r.TenId, item.TenId, StringComparison.OrdinalIgnoreCase));
            if (byTen != null) return byTen;

            // 2. CPE substring
            var byCpe = Catalog.Rules.FirstOrDefault(r =>
                !string.IsNullOrWhiteSpace(r.CpeContains) &&
                !string.IsNullOrEmpty(item.SoftwareRaw) &&
                item.SoftwareRaw.Contains(r.CpeContains!, StringComparison.OrdinalIgnoreCase));
            if (byCpe != null) return byCpe;

            // 3. Path regex
            foreach (var r in Catalog.Rules.Where(r => !string.IsNullOrWhiteSpace(r.PathRegex)))
            {
                try
                {
                    if (Regex.IsMatch(item.ProofPath ?? string.Empty, r.PathRegex!, RegexOptions.IgnoreCase))
                        return r;
                }
                catch { /* ignore bad regex */ }
            }

            // 4. Name regex (finding title / short description)
            var nameHaystack = $"{item.Name}\n{item.ShortDescription}";
            foreach (var r in Catalog.Rules.Where(r => !string.IsNullOrWhiteSpace(r.NameRegex)))
            {
                try
                {
                    if (Regex.IsMatch(nameHaystack, r.NameRegex!, RegexOptions.IgnoreCase))
                        return r;
                }
                catch { /* ignore bad regex */ }
            }

            // 5. Product / name / short description substring
            var haystack = $"{item.Product}\n{item.Name}\n{item.ShortDescription}";
            var byProduct = Catalog.Rules.FirstOrDefault(r =>
                !string.IsNullOrWhiteSpace(r.ProductContains) &&
                haystack.Contains(r.ProductContains!, StringComparison.OrdinalIgnoreCase));
            return byProduct;
        }

        /// <summary>
        /// Applies the catalog to an item, setting Component/Method/mapping fields. When unmatched,
        /// derives a best-guess component + suggested winget id so an operator can confirm it into
        /// the catalog.
        /// </summary>
        public void ApplyTo(VulnerabilityItem item)
        {
            // Findings that aren't installable-package fixes (AV signature checks, registry/config
            // compliance checks) are marked non-remediable so they aren't
            // presented as a remediation action, don't inflate the fix count, and are skipped by
            // the version verify.
            if (IsNonRemediable(item))
            {
                var (naComponent, _) = Suggest(item);
                item.HasMapping = false;
                item.Component = naComponent;
                item.Method = "not-applicable";
                item.SuggestedPackageId = string.Empty;
                item.SetClassification(true);
                return;
            }

            if (IsWindowsUpdate(item))
            {
                item.HasMapping = true;
                item.Component = BuildWindowsUpdateComponent(item);
                item.Method = "windows-update";
                item.SuggestedPackageId = string.Empty;
                item.RequiresReboot = true;
                item.SetClassification(false);
                return;
            }

            var rule = Match(item);
            if (rule != null)
            {
                item.HasMapping = true;
                item.Component = rule.Component;
                item.Method = rule.Method;
                item.SuggestedPackageId = rule.PackageId;
                item.RequiresReboot = rule.RequiresReboot;
                item.SetClassification(false);
                return;
            }

            item.HasMapping = false;
            var (component, suggestedId) = Suggest(item);
            item.Component = component;
            item.Method = item.InstallType == VulnerabilityInstallType.MsixStore ? "msix-direct" : "winget";
            item.SuggestedPackageId = suggestedId;
            item.SetClassification(false);
        }

        /// <summary>
        /// True when a finding is not fixable by pushing a package — antivirus signature/definition
        /// checks and registry/configuration compliance checks. Kept deliberately specific so real
        /// application "Security Updates … C2R" titles are NOT caught.
        /// </summary>
        public static bool IsNonRemediable(VulnerabilityItem item)
        {
            var name = $"{item.Name}\n{item.ShortDescription}";

            // AV signature / definition checks and config/compliance checks.
            if (Regex.IsMatch(name, @"Signature (Definition )?Check|Definition Check|Configuration Check",
                    RegexOptions.IgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// Detects findings remediated through Windows Update/KB flow instead of winget/MSIX.
        /// </summary>
        public static bool IsWindowsUpdate(VulnerabilityItem item)
        {
            if (item.KbArticleIds.Count > 0)
                return true;

            var name = $"{item.Name}\n{item.ShortDescription}";

            if (Regex.IsMatch(name, @"\bKB\s*[:#-]?\s*\d{6,8}\b", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(name, @"Windows\s+(10|11|Server)\b.*(Security Update|Cumulative Update)",
                    RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(name, @"(SQL Server|\.NET Framework).*(Security Update|Cumulative Update)",
                    RegexOptions.IgnoreCase))
                return true;

            return false;
        }

        private static string BuildWindowsUpdateComponent(VulnerabilityItem item)
        {
            var kb = item.KbArticleIds.FirstOrDefault();
            var suffix = string.IsNullOrWhiteSpace(kb) ? string.Empty : $" (KB{kb})";
            var name = $"{item.Name}\n{item.ShortDescription}";

            if (Regex.IsMatch(name, @"SQL Server", RegexOptions.IgnoreCase))
                return $"SQL Server Update{suffix}";
            if (Regex.IsMatch(name, @"\.NET Framework", RegexOptions.IgnoreCase))
                return $".NET Framework Update{suffix}";
            return $"Windows Update{suffix}";
        }

        /// <summary>
        /// Heuristic component name + suggested winget id for an unmatched item, derived from the
        /// install path leaf, product, or CPE.
        /// </summary>
        public static (string Component, string SuggestedId) Suggest(VulnerabilityItem item)
        {
            // Prefer the product name parsed from the finding title — most specific, and avoids
            // collapsing distinct products that share a vendor folder (e.g. Adobe Media Encoder
            // vs Adobe Illustrator both under \Adobe\, which would make one version query
            // wrongly clear both).
            var component = ExtractProductFromName(item.Name);

            // Prefer a meaningful folder from the install path (skip generic roots).
            var path = item.ProofPath ?? string.Empty;

            if (string.IsNullOrWhiteSpace(component))
            {
                var segments = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(s => !Regex.IsMatch(s, @"^(C:|Program Files( \(x86\))?|WindowsApps|Application|root|Office16|lib|bin|shared)$", RegexOptions.IgnoreCase))
                    .ToList();
                if (segments.Count > 0)
                    component = segments[0];
            }

            if (string.IsNullOrWhiteSpace(component))
                component = !string.IsNullOrWhiteSpace(item.Product) ? item.Product
                          : !string.IsNullOrWhiteSpace(item.Vendor) ? item.Vendor
                          : "Unknown";

            // Suggested winget id from CPE (cpe:/a:vendor:product) or vendor.product.
            var suggestedId = string.Empty;
            var cpe = item.SoftwareRaw ?? string.Empty;
            var cpeMatch = Regex.Match(cpe, @"cpe:/[aoh]:(?<vendor>[^:]+):(?<product>[^:]+)");
            if (cpeMatch.Success)
            {
                string Clean(string s) => Regex.Replace(s, @"[^a-zA-Z0-9]", "");
                var vendor = Clean(cpeMatch.Groups["vendor"].Value);
                var product = Clean(cpeMatch.Groups["product"].Value);
                if (vendor.Length > 0 && product.Length > 0)
                    suggestedId = $"{vendor}.{product}";
            }

            return (component, suggestedId);
        }

        /// <summary>
        /// Parses the product name from a Tenable finding title — the text before the first version
        /// comparator or parenthetical, e.g. "Adobe Media Encoder &lt; 25.6.5 / 26.0.0 ..." -> "Adobe
        /// Media Encoder". Returns empty for titles that don't start with a clean product token
        /// (e.g. long OS-update descriptions), so the caller falls back to the path/product.
        /// </summary>
        private static string ExtractProductFromName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var n = name.Trim();
            var cut = Regex.Match(n, @"\s+(<|\()");
            var product = (cut.Success ? n.Substring(0, cut.Index) : n).Trim();

            // Skip OS-update / config-check style titles; those aren't clean product names.
            if (Regex.IsMatch(product, @"^KB\d+", RegexOptions.IgnoreCase) ||
                product.Contains("Security Update", StringComparison.OrdinalIgnoreCase) ||
                product.Contains("Cumulative Update", StringComparison.OrdinalIgnoreCase) ||
                product.Contains("Configuration Check", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return product.Length is >= 2 and <= 60 ? product : string.Empty;
        }

        /// <summary>
        /// Built-in default catalog seeded from the components commonly seen in the environment.
        /// Operators refine/extend it; ids reflect the winget community repo where known.
        /// </summary>
        private static RemediationCatalog BuildSeedCatalog()
        {
            return new RemediationCatalog
            {
                SchemaVersion = SeedVersion,
                Rules = new List<RemediationRule>
                {
                    new()
                    {
                        Id = "dotnet-runtime",
                        Component = ".NET Runtime",
                        Method = "dotnet-lts-policy",
                        PathRegex = @"dotnet\\shared|Microsoft\.(Net|AspNet)Core\.App|Microsoft\.NETCore",
                        ProductContains = ".NET",
                        RequiresReboot = false,
                        Notes = "One runtime upgrade clears every .NET CVE. Uses the .NET LTS enforcement script."
                    },
                    new()
                    {
                        Id = "google-chrome",
                        Component = "Google Chrome",
                        Method = "winget",
                        PackageId = "Google.Chrome",
                        PathRegex = @"Google\\Chrome",
                        ProductContains = "Chrome"
                    },
                    new()
                    {
                        Id = "microsoft-edge",
                        Component = "Microsoft Edge",
                        Method = "winget",
                        PackageId = "Microsoft.Edge",
                        PathRegex = @"Microsoft\\Edge",
                        ProductContains = "Edge"
                    },
                    new()
                    {
                        Id = "adobe-reader",
                        Component = "Adobe Acrobat Reader",
                        Method = "winget",
                        PackageId = "Adobe.Acrobat.Reader.64-bit",
                        PathRegex = @"Acrobat Reader",
                        ProductContains = "Acrobat Reader"
                    },
                    new()
                    {
                        Id = "adobe-acrobat",
                        Component = "Adobe Acrobat",
                        Method = "vendor-msi",
                        PathRegex = @"Adobe\\Acrobat DC",
                        ProductContains = "Acrobat DC",
                        Notes = "Acrobat (Pro/Standard) updates via Adobe; confirm winget id before enabling."
                    },
                    new()
                    {
                        Id = "citrix-workspace",
                        Component = "Citrix Workspace",
                        Method = "winget",
                        PackageId = "Citrix.Workspace",
                        PathRegex = @"Citrix\\Citrix Workspace",
                        ProductContains = "Citrix Workspace"
                    },
                    new()
                    {
                        Id = "office-c2r",
                        Component = "Microsoft 365 Apps (C2R)",
                        Method = "office-c2r",
                        PathRegex = @"Microsoft Office\\root\\Office16",
                        NameRegex = @"Products C2R|Office (Click-to-Run|C2R)|Security Updates for (Microsoft )?(Word|Excel|PowerPoint|Outlook|Access|Publisher|OneNote|Office)",
                        Notes = "Microsoft 365 Apps (Click-to-Run): one OfficeC2RClient /update clears Word/Excel/PowerPoint/Outlook/Office CVEs together."
                    },
                    new()
                    {
                        Id = "windows-app",
                        Component = "Windows App (Store)",
                        Method = "msix-direct",
                        PathRegex = @"WindowsApps\\.*Windows365|MicrosoftCorporationII\.Windows365",
                        ProductContains = "windows app",
                        CpeContains = "windows_app",
                        Notes = "MSIX/Store app; install via direct package (msstore cannot install over remoting)."
                    },
                    new()
                    {
                        Id = "outlook-new",
                        Component = "Outlook for Windows (Store)",
                        Method = "store-update",
                        PathRegex = @"WindowsApps\\.*OutlookForWindows",
                        CpeContains = "outlook_for_windows",
                        Notes = "New Outlook is a Store-managed (msstore) app; no winget-pkgs manifest exists. Remediated by triggering a Store update scan over WinRM. Classic C2R Outlook is matched by the office-c2r rule instead."
                    },
                    new()
                    {
                        Id = "nessus-agent",
                        Component = "Tenable Nessus Agent",
                        Method = "vendor-msi",
                        PathRegex = @"Tenable\\Nessus Agent",
                        ProductContains = "Nessus",
                        Notes = "Managed by the scanning team; confirm before self-remediating."
                    }
                }
            };
        }
    }
}
