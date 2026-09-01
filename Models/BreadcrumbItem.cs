namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents a segment in the breadcrumb path navigation
    /// </summary>
    public class BreadcrumbItem
    {
        public string DisplayName { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public bool IsLast { get; set; }
    }
}
