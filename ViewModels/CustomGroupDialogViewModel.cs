using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ActiveScanner.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace ActiveScanner.ViewModels
{
    /// <summary>
    /// ViewModel for editing custom groups (Computer, User, Printer groups)
    /// </summary>
    public partial class CustomGroupDialogViewModel : ObservableObject
    {
        private readonly CustomGroup _originalGroup;
        private readonly HashSet<string> _otherGroupNames;

        [ObservableProperty]
        private string _groupName = string.Empty;

        [ObservableProperty]
        private string? _groupNameError;

        [ObservableProperty]
        private CustomGroupType _groupType;

        [ObservableProperty]
        private int _memberCount;

        /// <summary>
        /// Observable collection of members for display with delete buttons
        /// </summary>
        public ObservableCollection<CustomGroupMember> Members { get; } = new();

        public bool DialogResult { get; private set; }
        
        /// <summary>
        /// Set to true if user requested deletion
        /// </summary>
        public bool DeleteRequested { get; private set; }

        public CustomGroupDialogViewModel(CustomGroup group, IEnumerable<string>? otherGroupNames = null)
        {
            _originalGroup = group;
            _otherGroupNames = new HashSet<string>(
                (otherGroupNames ?? Enumerable.Empty<string>()).Select(n => n?.Trim() ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            GroupName = group.Name;
            GroupType = group.Type;
            MemberCount = group.MemberCount;

            // Populate members collection
            foreach (var member in group.Members)
            {
                Members.Add(member);
            }
        }

        /// <summary>
        /// Gets the group type display name
        /// </summary>
        public string GroupTypeDisplay => GroupType switch
        {
            CustomGroupType.Computer => "Computer Group",
            CustomGroupType.User => "User Group",
            CustomGroupType.Printer => "Printer Group",
            _ => "Group"
        };

        /// <summary>
        /// Gets the icon kind for the group type
        /// </summary>
        public string GroupTypeIcon => GroupType switch
        {
            CustomGroupType.Computer => "DesktopClassic",
            CustomGroupType.User => "AccountMultiple",
            CustomGroupType.Printer => "Printer",
            _ => "FolderMultiple"
        };

        partial void OnGroupNameChanged(string value)
        {
            ValidateGroupName();
        }

        private bool ValidateGroupName()
        {
            var trimmed = GroupName?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                GroupNameError = "Group name is required";
                return false;
            }

            if (trimmed.Length > 100)
            {
                GroupNameError = "Group name must be 100 characters or less";
                return false;
            }

            if (_otherGroupNames.Contains(trimmed))
            {
                GroupNameError = $"A group named \"{trimmed}\" already exists.";
                return false;
            }

            GroupNameError = null;
            return true;
        }

        [RelayCommand]
        private void Save(System.Windows.Window window)
        {
            if (!ValidateGroupName())
                return;

            // Apply changes to the original group
            _originalGroup.Name = GroupName.Trim();
            
            // Sync member removals back to original group
            _originalGroup.Members.Clear();
            foreach (var member in Members)
            {
                _originalGroup.Members.Add(member);
            }

            DialogResult = true;
            window.Close();
        }

        [RelayCommand]
        private void RemoveMember(CustomGroupMember member)
        {
            if (member != null)
            {
                Members.Remove(member);
                MemberCount = Members.Count;
            }
        }

        [RelayCommand]
        private void Cancel(System.Windows.Window window)
        {
            DialogResult = false;
            window.Close();
        }

        [RelayCommand]
        private void Delete(System.Windows.Window window)
        {
            var result = MessageBox.Show(
                $"Delete group '{GroupName}'?\n\nThis action cannot be undone.",
                "Delete Group",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                DeleteRequested = true;
                DialogResult = false;
                window.Close();
            }
        }
    }
}
