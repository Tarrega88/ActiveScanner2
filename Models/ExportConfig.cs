namespace ActiveScanner.Models
{
    /// <summary>
    /// Search scope options for the search bar
    /// </summary>
    public enum SearchScopeOption
    {
        CurrentFolder,
        CurrentFolderAndSubfolders,
        EntireDomain,
        SelectedGroup
    }

    /// <summary>
    /// Export format options
    /// </summary>
    public enum ExportFormat
    {
        Excel,
        Csv,
        Json,
        Text,
        Html,
        Clipboard
    }

    /// <summary>
    /// What source to export from
    /// </summary>
    public enum ExportSource
    {
        CurrentTab,
        LastUserDatabase
    }
}
