namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents a preset or custom domain configuration
    /// </summary>
    public class DomainConfig
    {
        public string Name { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public string? Server { get; set; }
        public bool UseLdaps { get; set; }
        public bool IsPreset { get; set; }

        public DomainConfig() { }

        public DomainConfig(string name, string dn, bool isPreset = false)
        {
            Name = name;
            DistinguishedName = dn;
            IsPreset = isPreset;
        }

        public override string ToString() => Name;
    }
}
