# Active Scanner

A Windows WPF application for browsing Active Directory organizational units and querying computers, users, and printers, with integrated network diagnostic tools. Uses LDAP to connect to domain controllers without requiring RSAT tools.

<details open>
<summary><strong>Features</strong></summary>

### AD Object Support
- **Computers**: Name, DNS Hostname, Description, OS, Enabled status, AD Logon, Location, and more
- **Users**: Display Name, SAM Account Name, Email, Title, Department, Manager, Login history
- **Printers**: Server, Share Name, Port, Driver, UNC Path, Location

### Domain Navigation
- Browse multiple AD domains with Quick Bar preset buttons
- Navigate OUs with breadcrumb paths
- Search across multiple paths simultaneously with Domain Groups

### Network Tools
- **Ping**: ICMP connectivity test with batch support
- **Traceroute**: Network path tracing
- **Pathping**: Combined trace and latency statistics
- **DNS Lookup**: Forward and reverse resolution
- **Port Test**: TCP port connectivity (RDP, SMB, RPC, WinRM, SSH, HTTP/S presets)
- **RDP**: Launch Remote Desktop sessions
- **Last User**: Query last logged-on user via WMI
- **GPUpdate**: Remote Group Policy refresh
- **Remote Reboot**: Restart computers remotely

### Export Options
- Excel (.xlsx) with formatting and color-coded results
- CSV, JSON, Text, HTML
- Clipboard (tab-delimited)
- Last User Database export (persistent computer cache)

### Custom Groups
- Save collections of specific computers, users, or printers
- Quick access from sidebar or Quick Bar
- Drag-to-reorder, rename, manage members
- Custom button colors

### UI Features
- Dark/Light mode toggle
- Column filtering with per-column search
- Sortable columns
- Multi-tab results management
- Caching with configurable TTL
- Admin credential login for elevated operations

</details>

<details>
<summary><strong>Requirements</strong></summary>

- Windows 10/11
- .NET 8.0 Runtime
- Network access to Active Directory domain controllers

</details>

<details>
<summary><strong>Building</strong></summary>

```bash
cd ActiveScanner
dotnet restore
dotnet build
```

</details>

<details>
<summary><strong>Running</strong></summary>

```bash
dotnet run --project ActiveScanner/ActiveScanner.csproj
```

Or build a release:

```bash
dotnet publish -c Release -r win-x64 --self-contained
```
To build the versioned Windows installer from the project folder:

```powershell
.\build-installer.ps1
```

To run the installer build from any directory:

```powershell
& "C:\Dev\ActiveScanner\build-installer.ps1"
```

</details>

<details>
<summary><strong>Keyboard Shortcuts</strong></summary>

| Shortcut      | Action                          |
| ------------- | ------------------------------- |
| Ctrl+A        | Select all items                |
| Enter         | Apply filter (in filter field)  |

</details>

<details>
<summary><strong>Configuration</strong></summary>

Settings are stored in `%AppData%\ActiveScanner\settings.json`:

### Window & Appearance
- Window position, size, maximized state
- Dark/Light mode preference

### Network Tools
- Default timeout (seconds)

### Cache
- Cache TTL (minutes, default 720 = 12 hours)
- Set to 0 to disable caching

### Export Preferences
- Last folder, filename, format
- Timestamp append option

### Saved Data
- Custom paths and target groups
- Column visibility settings

### Additional Data Files
- `customGroups.json` - Computer/User/Printer group definitions
- `computerCache.json` - Persistent Last User data cache

</details>

<details>
<summary><strong>Project Structure</strong></summary>

```
ActiveScanner/
├── Models/           # Data models (AdObjectInfo, CustomGroup, etc.)
├── ViewModels/       # MVVM ViewModels
├── Views/            # Dialog windows
├── Services/
│   ├── LdapService.cs         # AD/LDAP operations
│   ├── NetworkService.cs      # Network diagnostics
│   ├── ExportService.cs       # File exports
│   ├── SettingsService.cs     # Settings persistence
│   ├── CacheService.cs        # In-memory caching
│   ├── ComputerCacheService.cs# Persistent computer data
│   └── CustomGroupService.cs  # Custom group management
├── Converters/       # XAML value converters
├── Resources/        # Icons and resources
├── App.xaml          # Application resources
├── MainWindow.xaml   # Main UI
└── HelpWindow.xaml   # Help documentation
```

</details>

<details>
<summary><strong>Dependencies</strong></summary>

- **MaterialDesignThemes** - Modern UI framework
- **ClosedXML** - Excel export
- **CommunityToolkit.Mvvm** - MVVM support
- **System.DirectoryServices** - LDAP/AD access
- **System.Management** - WMI queries

</details>

## License

Internal use only - Department of Veterans Affairs
