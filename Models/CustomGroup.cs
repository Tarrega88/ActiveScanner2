using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents a user-defined custom group of AD objects
    /// </summary>
    public class CustomGroup : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Unique identifier for the group
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Type of objects in this group (Computer, User, Printer)
        /// </summary>
        public CustomGroupType Type { get; set; } = CustomGroupType.Computer;

        private string _name = "New Group";
        /// <summary>
        /// Display name of the group
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// When the group was created
        /// </summary>
        public DateTime Created { get; set; } = DateTime.Now;

        /// <summary>
        /// When the group was last modified
        /// </summary>
        public DateTime LastModified { get; set; } = DateTime.Now;

        /// <summary>
        /// Members of this group
        /// </summary>
        public List<CustomGroupMember> Members { get; set; } = new();

        /// <summary>
        /// Number of members in the group
        /// </summary>
        public int MemberCount => Members?.Count ?? 0;

        private bool _isRenaming;
        /// <summary>
        /// Whether the group is currently being renamed (for UI binding)
        /// </summary>
        public bool IsRenaming
        {
            get => _isRenaming;
            set
            {
                if (_isRenaming != value)
                {
                    _isRenaming = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _showInQuickBar;
        /// <summary>
        /// Whether this group should appear in the Quick Bar
        /// </summary>
        public bool ShowInQuickBar
        {
            get => _showInQuickBar;
            set
            {
                if (_showInQuickBar != value)
                {
                    _showInQuickBar = value;
                    OnPropertyChanged();
                }
            }
        }

        private ButtonColorOption _buttonColor = ButtonColorOption.Default;
        /// <summary>
        /// Color for the Quick Bar button
        /// </summary>
        public ButtonColorOption ButtonColor
        {
            get => _buttonColor;
            set
            {
                if (_buttonColor != value)
                {
                    _buttonColor = value;
                    OnPropertyChanged();
                }
            }
        }

        public override string ToString() => $"{Name} ({MemberCount} {Type}s)";
    }

    /// <summary>
    /// Type of custom group
    /// </summary>
    public enum CustomGroupType
    {
        Computer,
        User,
        Printer
    }

    /// <summary>
    /// Represents a member of a custom group
    /// </summary>
    public class CustomGroupMember
    {
        /// <summary>
        /// Domain the object belongs to
        /// </summary>
        public string? Domain { get; set; }

        /// <summary>
        /// Distinguished name for AD lookup
        /// </summary>
        public string? DistinguishedName { get; set; }

        /// <summary>
        /// Display name (cached for quick display before requery)
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Target address for manual entries
        /// </summary>
        public string? Address { get; set; }

        /// <summary>
        /// Whether this is a manually-added entry (not from AD)
        /// </summary>
        public bool IsManual { get; set; }

        /// <summary>
        /// When this member was added to the group
        /// </summary>
        public DateTime AddedAt { get; set; } = DateTime.Now;
    }
}
