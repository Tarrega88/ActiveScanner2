namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents an OU or Container in Active Directory
    /// </summary>
    public class FolderItem
    {
        public string Name { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // organizationalUnit or container
        public bool HasChildren { get; set; }

        public string TypeIcon => Type.ToLower() switch
        {
            "organizationalunit" => "FolderAccount",
            "container" => "Folder",
            "domain" => "Domain",
            _ => "Folder"
        };

        public override string ToString() => Name;
    }
}
