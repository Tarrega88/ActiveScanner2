using System;
using System.Collections.Generic;
using System.Linq;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents the type of AD objects a group is restricted to
    /// </summary>
    public enum GroupObjectType
    {
        None,       // Multi-Type: allows any object types (computers, users, printers)
        Computers,
        Users,
        Printers
    }

    /// <summary>
    /// Available button colors for quick bar buttons
    /// </summary>
    public enum ButtonColorOption
    {
        Default,
        Blue,
        Green,
        Orange,
        Purple,
        Teal,
        Red
    }

    /// <summary>
    /// Represents a saved group of target paths for multi-path searching
    /// </summary>
    public class TargetGroup
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public List<TargetPath> Paths { get; set; } = new();
        public GroupObjectType ObjectType { get; set; } = GroupObjectType.None;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime ModifiedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Whether to show this group as a quick button in the top bar
        /// </summary>
        public bool ShowInQuickBar { get; set; } = true;

        /// <summary>
        /// The color for the quick bar button
        /// </summary>
        public ButtonColorOption ButtonColor { get; set; } = ButtonColorOption.Default;

        /// <summary>
        /// Optional startup filter to apply automatically when this group is loaded
        /// </summary>
        public StartupFilter? StartupFilter { get; set; }

        public TargetGroup() { }

        public TargetGroup(string name)
        {
            Name = name;
        }

        public TargetGroup(string name, IEnumerable<TargetPath> paths, GroupObjectType objectType = GroupObjectType.None)
        {
            Name = name;
            Paths = paths.ToList();
            ObjectType = objectType;
        }

        /// <summary>
        /// Gets a display string for the object type
        /// </summary>
        public string ObjectTypeDisplay => ObjectType switch
        {
            GroupObjectType.Computers => "Computers",
            GroupObjectType.Users => "Users",
            GroupObjectType.Printers => "Printers",
            _ => "Any"
        };

        /// <summary>
        /// Button display text for group selection
        /// </summary>
        public string ButtonText => Name;

        /// <summary>
        /// Tooltip showing paths in the group
        /// </summary>
        public string ToolTipText => Paths.Count > 0 
            ? $"{ObjectTypeDisplay} group with {Paths.Count} path(s):\n" + string.Join("\n", Paths.Select(p => $"• {p.DisplayPath}"))
            : $"{ObjectTypeDisplay} group (empty)";

        public override string ToString() => $"{Name} ({Paths.Count} paths)";
    }
}
