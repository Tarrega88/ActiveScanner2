using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Which shell interprets a custom remote command.
    /// </summary>
    public enum RemoteShell
    {
        /// <summary>Run the text as PowerShell on the target.</summary>
        PowerShell,

        /// <summary>Run the text through cmd.exe on the target.</summary>
        Cmd
    }

    /// <summary>
    /// Lifecycle stage of a single computer while a custom remote command runs.
    /// </summary>
    public enum RemoteCommandStage
    {
        Pending,
        Running,
        Success,
        Failed,
        Offline,
        Cancelled
    }

    /// <summary>
    /// One row in the Run Command grid: a target computer with live status and captured output.
    /// Bound directly by the modal, so status changes must raise change notifications.
    /// </summary>
    public partial class RemoteCommandTarget : ObservableObject
    {
        public string ComputerName { get; set; } = string.Empty;

        /// <summary>Address used for connections (may equal <see cref="ComputerName"/> or an IP).</summary>
        public string Address { get; set; } = string.Empty;

        [ObservableProperty]
        private RemoteCommandStage _stage = RemoteCommandStage.Pending;

        /// <summary>Short human-readable detail, e.g. "Done (1.2s)" or an error summary.</summary>
        [ObservableProperty]
        private string _statusDetail = string.Empty;

        /// <summary>Full captured stdout/stderr for the details pane.</summary>
        [ObservableProperty]
        private string _output = string.Empty;

        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        /// <summary>True once the target reached a terminal successful state.</summary>
        public bool Succeeded => Stage == RemoteCommandStage.Success;

        /// <summary>Short label for the current stage, shown in the grid.</summary>
        public string StatusText => Stage switch
        {
            RemoteCommandStage.Pending => "Pending",
            RemoteCommandStage.Running => "Running…",
            RemoteCommandStage.Success => string.IsNullOrEmpty(StatusDetail) ? "Success" : StatusDetail,
            RemoteCommandStage.Failed => string.IsNullOrEmpty(StatusDetail) ? "Failed" : StatusDetail,
            RemoteCommandStage.Offline => "Offline",
            RemoteCommandStage.Cancelled => "Cancelled",
            _ => Stage.ToString()
        };

        /// <summary>Status dot color for the grid (matches app conventions).</summary>
        public string StatusColor => Stage switch
        {
            RemoteCommandStage.Success => "#4CAF50",
            RemoteCommandStage.Failed => "#E53935",
            RemoteCommandStage.Offline => "#E53935",
            RemoteCommandStage.Cancelled => "#FF9800",
            RemoteCommandStage.Pending => "#9E9E9E",
            _ => "#2196F3"
        };

        // StatusText/StatusColor are derived; nudge them when their inputs change.
        partial void OnStageChanged(RemoteCommandStage value)
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(Succeeded));
        }

        partial void OnStatusDetailChanged(string value) => OnPropertyChanged(nameof(StatusText));
    }
}
