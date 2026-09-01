# Using Active Scanner

A comprehensive guide to browsing Active Directory and running network diagnostics.

---

## Table of Contents
1. [Getting Started](#getting-started)
2. [Filtering Results](#filtering-results)
3. [Sorting & Copying](#sorting--copying)
4. [Selection](#selection)
5. [Working with Groups](#working-with-groups)
6. [Exporting Data](#exporting-data)
7. [Network Actions](#network-actions)
8. [Admin Actions](#admin-actions)
9. [Network Output Panel](#network-output-panel)
10. [Settings & Customization](#settings--customization)
11. [Keyboard Shortcuts](#keyboard-shortcuts)
12. [Tips & Tricks](#tips--tricks)

---

## Getting Started

### Launching the Application

When you first open Active Scanner, you'll see the main window with a sidebar on the left containing **Domain Groups** and sections for **Computer Groups**, **User Groups**, and **Printer Groups**.

### Loading Data with Domain Groups

The quickest way to get started is to click one of the pre-configured **Domain Groups** in the sidebar:
- **AK Computers** – Queries computers from the main organizational units
- **AK Users** – Queries user accounts
- **AK Printers** – Queries network printers

Clicking a Domain Group queries Active Directory and returns results in the main data grid. The status bar at the bottom shows the total count (e.g., "1183 computers").

### Understanding the Interface

| Area | Description |
|------|-------------|
| **Blue Header Bar** | Export, admin login, dark mode toggle, settings, and help buttons |
| **Left Sidebar** | Domain Groups, Computer/User/Printer Groups, quick access buttons |
| **Quick Bar** | Favorite groups pinned for one-click access (below the header) |
| **Main Data Grid** | Results with sortable/filterable columns |
| **Tabs** | Manage multiple result sets; close tabs with the X button |
| **Status Bar** | Shows item count, selection count, and current status |

---

## Filtering Results

Each column in the data grid has a **filter box** directly below the column header.

### Basic Text Filtering
1. Type your search text in any column's filter box
2. Press **Enter** to apply the filter
3. Results collapse to show only matching items

*Example: Type "LT" in the Name filter to show only laptops.*

### Special Filters

#### Enabled Status Filter
The **Enabled** column has a dropdown filter with three options:
- **All** – Shows all items regardless of status
- **Enabled** – Shows only enabled accounts/computers
- **Disabled** – Shows only disabled accounts/computers

#### AD Logon Date Filter
The **AD Logon** column has a special numeric filter with a toggle button:
- Click the **>** symbol to switch to **<** (less than)
- Enter a number of days
- Press Enter to filter

*Example: Set "<" and "30" to show only items logged into within the last 30 days.*

### Clearing Filters
- Click the **X** button that appears inside a filter box to clear that specific filter
- Individual filters can be cleared without affecting others

---

## Sorting & Copying

### Sorting Columns

Click any **column header** to sort results by that column:
- First click: Ascending order (A→Z, oldest→newest)
- Second click: Descending order (Z→A, newest→oldest)
- A small arrow icon indicates the current sort direction

### Copying Cell Values

When you hover over any cell, copy buttons appear:

| Button | Description |
|--------|-------------|
| **Copy icon** | Copies the full cell text to clipboard |
| **VC** (Vista Copy) | *Computers only* – Copies only the trailing numbers from the name (e.g., "ANC-LT35011" → "35011") |

### Right-Click Copy Menu

Right-click on selected items to access the **Copy** submenu:
- **Copy Row / Copy Rows** – Copies all visible column values as formatted table (HTML) and tab-separated text. Pastes as a visual table in Teams/Outlook.
- **Individual columns** – Copy just that column's value for all selected items

*The Copy menu only shows columns that are currently enabled in your Column Settings.*

---

## Selection

### Single Selection
Click on any row to select it. The row highlights and the status bar updates to show "1 item selected."

### Multi-Selection

| Action | Result |
|--------|--------|
| **Ctrl+Click** | Toggle individual items in/out of selection |
| **Shift+Click** | Select all items between the last selection and clicked item |
| **Click and Drag** | Draw a selection across multiple consecutive rows |
| **Ctrl+A** | Select all items in the current results |

The status bar shows the count by type (e.g., "3 computers, 2 users selected").

---

## Working with Groups

Active Scanner has two types of groups:

| Type | Description |
|------|-------------|
| **Domain Groups** | Collections of AD organizational units (like "AK Computers") – these query AD directly |
| **Item Groups** | Saved collections of specific computers, users, or printers that you create |

### Creating Item Groups

1. Select one or more items using multi-select
2. Right-click on your selection
3. Click **Create Computer Group** (or User Group, or Printer Group)
4. The new group appears in the sidebar under the appropriate section

*Groups are named with "Computer Group" plus a timestamp by default.*

### Managing Groups

Click the **pencil icon** next to any group to open the edit dialog:
- **Rename** the group
- **Change button color** for Quick Bar visibility
- **Show/Hide in Quick Bar** – Toggle whether it appears in the top quick access bar
- **Remove members** – Delete individual items from the group
- **Delete group** – Remove the entire group

### Adding Items to Existing Groups

1. Select items in the main data grid
2. Right-click and choose **Add to Computer Group** (or User/Printer Group)
3. Select the target group from the submenu

### Quick Bar

Groups with "Show in Quick Bar" enabled appear as colored buttons below the header bar for one-click access. You can:
- Drag groups to reorder them in the sidebar
- Customize button colors in the group editor

---

## Exporting Data

### How to Export

1. Make sure the tab with your desired data is selected
2. Click the **Export button** (leftmost icon in the blue header bar)
3. An export dropdown appears with options

### Export Options

| Setting | Description |
|---------|-------------|
| **Base Folder** | Where exports are saved (defaults to Documents) |
| **Filename** | Custom filename for the export |
| **Add timestamp** | Appends date/time to filename to prevent overwriting |
| **Organize into folders** | Creates subfolders by format type (recommended) |

### Export Formats

- **Excel (.xlsx)** – Formatted spreadsheet with color-coded results
- **CSV** – Comma-separated values for import into other tools
- **JSON** – Structured data format
- **Text** – Plain text file
- **HTML** – Viewable in web browsers
- **Clipboard** – Tab-delimited, paste directly into Excel

### Export Location

With "Organize into folders" enabled, exports go to:
```
[Base Folder]\Active Scanner\Exports\[Format]\
```

*Example: `C:\Users\YourName\Documents\Active Scanner\Exports\Excel\`*

---

## Network Actions

Right-click on selected computers or printers to access **Network Actions**:

| Action | Description |
|--------|-------------|
| **Ping** | ICMP connectivity test |
| **Traceroute** | Shows the network path to the target |
| **Pathping** | Combined trace with latency statistics |
| **DNS Forward Lookup** | Resolves hostname to IP address |
| **DNS Reverse Lookup** | Resolves IP address to hostname |
| **Test Port** | Check if a specific TCP port is open (RDP, SMB, HTTP, etc.) |

### Remote Desktop (RDP)

For computers, you can also click **Remote Desktop** to launch an RDP session directly.

### Network Tools

Right-click computers to access network tools (Ping, Traceroute, DNS, Port Test, etc.) from the context menu. Results appear in the **Network Output** panel at the bottom of the window.

---

## Admin Actions

Some actions require elevated credentials:

| Action | Description |
|--------|-------------|
| **Query Last User** | Retrieves the last logged-on user via WMI |
| **GPUpdate** | Refreshes Group Policy on the remote computer |
| **GPUpdate /force** | Forces a full Group Policy refresh |
| **Reboot** | Remotely restarts the computer |

### Logging In as Admin

1. Click the **user/profile icon** in the blue header bar
2. A Windows credential dialog appears
3. Enter your admin credentials
4. The icon changes to indicate you're logged in

Once logged in, admin actions become available in the context menu.

### Logging Out

Click the profile icon again to log out of the admin session.

---

## Network Output Panel

When you run network actions, results appear in the **Network Output** panel at the bottom of the window:

### Features
- **Terminal-style output** – Results formatted like native Windows commands
- **Action history** – All network actions logged chronologically
- **Auto-expand** – Panel opens automatically when new results arrive
- **Resizable** – Drag the grip bar to resize the panel
- **Skip button** – Skip to the next target if one is taking too long
- **Cancel button** – Stop the entire operation

### Example Use Case

Running a ping on 6 computers:
1. Select the 6 computers
2. Right-click → Ping
3. The Network Output panel expands
4. Watch as each ping completes with success/failure status
5. Export results if needed

---

## Settings & Customization

### Accessing Settings

Click the **gear icon** in the blue header bar to open Settings.

### Available Settings

| Setting | Description |
|---------|-------------|
| **Dark Mode** | Toggle between light and dark themes |
| **Default Timeout** | How long to wait for network operations (seconds) |
| **Cache TTL** | How long to cache AD query results (minutes, 0 = disabled) |
| **Export Groups** | Save your Domain Groups to a JSON file for backup or sharing |
| **Import Groups** | Load Domain Groups from a JSON file (replace or merge) |
| **Restore Defaults** | Reset Domain Groups to the built-in defaults |
| **Reset All Settings** | Clear all settings and start fresh |

### Column Settings

Column visibility is configured per-query type and affects which attributes are fetched from Active Directory. Access via the help or settings areas.

### Dark Mode Toggle

For quick theme switching, use the **sun/moon icon** in the header bar.

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| **Ctrl+A** | Select all items in current results |
| **Enter** | Apply filter (when typing in a filter box) |
| **Ctrl+Click** | Add/remove item from selection |
| **Shift+Click** | Extend selection to clicked item |
| **Delete** | Works in group editor to remove selected members |

---

## Tips & Tricks

### Efficiency Tips

1. **Use the Quick Bar** – Pin frequently-used groups for one-click access
2. **Combine filters** – Filter multiple columns simultaneously for precise results
3. **Use tabs** – Keep multiple queries open in separate tabs
4. **Copy Row for Teams/Excel** – Right-click → Copy → Copy Rows, then paste directly into Teams or Excel as a formatted table

### Performance Tips

1. **Enable caching** – Reduces AD query time for repeated searches
2. **Close unused tabs** – Reduces memory usage
3. **Use specific filters** – Reduces result set size for faster operations

### Troubleshooting

| Issue | Solution |
|-------|----------|
| No results | Check if you have network access to the domain controller |
| Admin actions grayed out | Click the profile icon to log in with admin credentials |
| Slow queries | Try filtering to reduce result size, or increase cache TTL |
| Export fails | Check write permissions on the export folder |

### Common Workflows

**Finding stale computers:**
1. Load AK Computers
2. Set AD Logon filter to ">" and "90"
3. Set Enabled filter to "Enabled"
4. Export results for review

**Creating a workstation group:**
1. Load AK Computers
2. Filter Name by "WS"
3. Ctrl+A to select all
4. Right-click → Create Computer Group
5. Edit group to rename and pin to Quick Bar

**Batch ping check:**
1. Load or create a computer group
2. Ctrl+A to select all
3. Right-click → Network Actions → Ping
4. Review results in the Network Output panel
5. Export failures for follow-up

---

*Active Scanner – Department of Veterans Affairs*
