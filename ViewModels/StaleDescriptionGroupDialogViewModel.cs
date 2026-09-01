using ActiveScanner.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ActiveScanner.ViewModels
{
    /// <summary>
    /// ViewModel for the "Create group: stale descriptions" dialog.
    /// Identifies selected computers whose Description does not contain their LastUser value.
    /// </summary>
    public partial class StaleDescriptionGroupDialogViewModel : ObservableObject
    {
        private readonly List<AdObjectInfo> _candidates;
        private readonly HashSet<string> _existingNames;

        [ObservableProperty]
        private string _groupName = string.Empty;

        [ObservableProperty]
        private string? _groupNameError;

        /// <summary>
        /// When true, computers without a real LastUser value (null/empty/"Querying..."/"(...)")
        /// are excluded. When false, those rows are also added to the group (treated as "unknown").
        /// </summary>
        [ObservableProperty]
        private bool _skipPlaceholderLastUsers = true;

        /// <summary>
        /// When true, computers whose Description is blank/whitespace are excluded from the group.
        /// </summary>
        [ObservableProperty]
        private bool _skipBlankDescriptions;

        [ObservableProperty]
        private int _selectedCount;

        [ObservableProperty]
        private int _matchCount;

        [ObservableProperty]
        private int _skippedNoLastUserCount;

        [ObservableProperty]
        private int _skippedDescriptionMatchesCount;

        [ObservableProperty]
        private int _skippedBlankDescriptionCount;

        /// <summary>
        /// Set after Save() to the computers that should be included in the new group.
        /// </summary>
        public List<AdObjectInfo> MatchingComputers { get; private set; } = new();

        public bool DialogResult { get; private set; }

        public StaleDescriptionGroupDialogViewModel(
            IEnumerable<AdObjectInfo> selectedComputers,
            IEnumerable<string> existingComputerGroupNames,
            string suggestedName)
        {
            _candidates = selectedComputers?.ToList() ?? new List<AdObjectInfo>();
            _existingNames = new HashSet<string>(
                (existingComputerGroupNames ?? Enumerable.Empty<string>()).Select(n => n?.Trim() ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            SelectedCount = _candidates.Count;
            GroupName = _existingNames.Contains(suggestedName) ? string.Empty : suggestedName;

            Recompute();
            ValidateGroupName();
        }

        partial void OnSkipPlaceholderLastUsersChanged(bool value)
        {
            Recompute();
        }

        partial void OnSkipBlankDescriptionsChanged(bool value)
        {
            Recompute();
        }

        partial void OnGroupNameChanged(string value)
        {
            ValidateGroupName();
        }

        /// <summary>
        /// Returns true when the LastUser string represents a real username
        /// (not null/empty, not "Querying...", not a parenthesized status like "(Offline)").
        /// </summary>
        public static bool IsRealLastUser(string? lastUser)
        {
            if (string.IsNullOrWhiteSpace(lastUser)) return false;
            var trimmed = lastUser.Trim();
            if (trimmed.Equals("Querying...", StringComparison.OrdinalIgnoreCase)) return false;
            if (trimmed.StartsWith("(", StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>
        /// Strip a leading "DOMAIN\" prefix from a username, returning the bare SAM portion.
        /// </summary>
        private static string NormalizeUser(string user)
        {
            var idx = user.IndexOf('\\');
            return idx >= 0 ? user.Substring(idx + 1).Trim() : user.Trim();
        }

        /// <summary>
        /// A computer is "stale" when:
        /// - Its LastUser is a real username AND its Description does NOT contain that username, OR
        /// - SkipPlaceholderLastUsers is false AND no real LastUser is available (treated as unknown).
        /// Rows with a blank Description are excluded when SkipBlankDescriptions is true.
        /// </summary>
        private void Recompute()
        {
            var matches = new List<AdObjectInfo>();
            int skippedNoUser = 0;
            int skippedMatched = 0;
            int skippedBlankDesc = 0;

            foreach (var comp in _candidates)
            {
                var lastUser = comp.LastUser;
                var description = comp.Description ?? string.Empty;
                var hasDescription = !string.IsNullOrWhiteSpace(description);

                if (IsRealLastUser(lastUser))
                {
                    var bare = NormalizeUser(lastUser!);
                    if (description.IndexOf(bare, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        skippedMatched++;
                    }
                    else if (!hasDescription && SkipBlankDescriptions)
                    {
                        skippedBlankDesc++;
                    }
                    else
                    {
                        matches.Add(comp);
                    }
                }
                else
                {
                    if (SkipPlaceholderLastUsers)
                    {
                        skippedNoUser++;
                    }
                    else if (!hasDescription && SkipBlankDescriptions)
                    {
                        skippedBlankDesc++;
                    }
                    else
                    {
                        matches.Add(comp);
                    }
                }
            }

            MatchingComputers = matches;
            MatchCount = matches.Count;
            SkippedNoLastUserCount = skippedNoUser;
            SkippedDescriptionMatchesCount = skippedMatched;
            SkippedBlankDescriptionCount = skippedBlankDesc;
        }

        private bool ValidateGroupName()
        {
            var trimmed = GroupName?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                GroupNameError = "Group name is required.";
                return false;
            }

            if (trimmed.Length > 100)
            {
                GroupNameError = "Group name must be 100 characters or less.";
                return false;
            }

            if (_existingNames.Contains(trimmed))
            {
                GroupNameError = $"A computer group named \"{trimmed}\" already exists.";
                return false;
            }

            GroupNameError = null;
            return true;
        }

        public bool CanSave => GroupNameError == null && MatchCount > 0;

        partial void OnGroupNameErrorChanged(string? value) => OnPropertyChanged(nameof(CanSave));
        partial void OnMatchCountChanged(int value) => OnPropertyChanged(nameof(CanSave));

        [RelayCommand]
        private void Save(Window window)
        {
            if (!ValidateGroupName()) return;
            if (MatchCount == 0)
            {
                GroupNameError = "No computers match the criteria \u2014 nothing to add.";
                return;
            }

            GroupName = GroupName.Trim();
            DialogResult = true;
            window.DialogResult = true;
            window.Close();
        }

        [RelayCommand]
        private void Cancel(Window window)
        {
            DialogResult = false;
            window.DialogResult = false;
            window.Close();
        }
    }
}
