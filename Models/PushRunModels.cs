using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Lifecycle stage of a single computer during a Push &amp; Run operation.
    /// </summary>
    public enum PushRunStage
    {
        Pending,
        Preflight,
        Copying,
        Verifying,
        Running,
        Success,
        Failed,
        Offline,
        Cancelled
    }

    /// <summary>
    /// How the entry point should be launched. <see cref="Auto"/> resolves by file extension.
    /// </summary>
    public enum PushRunLaunchKind
    {
        Auto,
        Executable,
        Msi,
        PowerShell,
        Batch,
        DriverInf,      // pnputil on the chosen .inf
        DriverFolder    // pnputil on all *.inf in the destination folder
    }

    /// <summary>
    /// Describes the payload to push: a single file or a folder tree, plus the
    /// entry-point that gets executed once the copy completes.
    /// </summary>
    public class PushRunPayload
    {
        /// <summary>Local source path (a file or a folder).</summary>
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>True when <see cref="SourcePath"/> is a folder whose contents are copied.</summary>
        public bool IsFolder { get; set; }

        /// <summary>
        /// Path of the file to run, relative to the destination folder root. For a single-file
        /// payload this is just the file name; for a folder payload it is the chosen entry point
        /// relative to the folder root (e.g. "bin\setup.exe").
        /// </summary>
        public string EntryPointRelativePath { get; set; } = string.Empty;

        /// <summary>Silent/CLI arguments passed to the entry point.</summary>
        public string Arguments { get; set; } = string.Empty;

        /// <summary>How to launch the entry point (Auto = resolve by extension).</summary>
        public PushRunLaunchKind LaunchKind { get; set; } = PushRunLaunchKind.Auto;

        /// <summary>SHA256 of the entry-point file, used for optional remote verification.</summary>
        public string? EntryPointSha256 { get; set; }
    }

    /// <summary>
    /// Options that apply to the whole Push &amp; Run batch.
    /// </summary>
    public class PushRunOptions
    {
        /// <summary>Remote destination folder (created if missing), e.g. "C:\temp\it".</summary>
        public string DestinationFolder { get; set; } = @"C:\temp\it";

        /// <summary>Verify the entry-point hash on the remote machine after copy.</summary>
        public bool VerifyHash { get; set; }

        /// <summary>Remove the copied payload from the remote machine after the run completes.</summary>
        public bool DeleteAfterRun { get; set; }

        /// <summary>Copy the payload only — skip the verify, run and cleanup stages entirely.</summary>
        public bool CopyOnly { get; set; }

        /// <summary>Master default for allowing the scheduled-task fallback (per-target can override).</summary>
        public bool AllowTaskFallbackByDefault { get; set; } = true;

        /// <summary>Concurrent machines processed at once.</summary>
        public int MaxParallelism { get; set; } = 5;

        /// <summary>Overall per-machine timeout in milliseconds (copy + run).</summary>
        public int TimeoutMs { get; set; } = 1200000;
    }

    /// <summary>
    /// One row in the Push &amp; Run grid: a target computer with live status. Bound directly by
    /// the modal, so status changes must raise change notifications.
    /// </summary>
    public partial class PushRunTarget : ObservableObject
    {
        public string ComputerName { get; set; } = string.Empty;

        /// <summary>Address used for connections (may equal <see cref="ComputerName"/> or an IP).</summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>Per-target override allowing the scheduled-task fallback for the run step.</summary>
        [ObservableProperty]
        private bool _allowTaskFallback = true;

        [ObservableProperty]
        private PushRunStage _stage = PushRunStage.Pending;

        /// <summary>Short human-readable detail, e.g. "Exit 0", "1603", "copied via WinRM".</summary>
        [ObservableProperty]
        private string _statusDetail = string.Empty;

        /// <summary>Exit code of the entry point when the run completed.</summary>
        [ObservableProperty]
        private int? _exitCode;

        /// <summary>Copy transport actually used ("SMB" or "WinRM").</summary>
        [ObservableProperty]
        private string? _copyMethod;

        /// <summary>Run transport actually used ("WinRM" or "ScheduledTask").</summary>
        [ObservableProperty]
        private string? _runMethod;

        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        /// <summary>True once the target reached a terminal successful state.</summary>
        public bool Succeeded => Stage == PushRunStage.Success;

        /// <summary>Short label for the current stage, shown in the grid.</summary>
        public string StatusText => Stage switch
        {
            PushRunStage.Pending => "Pending",
            PushRunStage.Preflight => "Preflight…",
            PushRunStage.Copying => "Copying…",
            PushRunStage.Verifying => "Verifying…",
            PushRunStage.Running => "Running…",
            PushRunStage.Success => string.IsNullOrEmpty(StatusDetail) ? "Success" : StatusDetail,
            PushRunStage.Failed => string.IsNullOrEmpty(StatusDetail) ? "Failed" : StatusDetail,
            PushRunStage.Offline => "Offline",
            PushRunStage.Cancelled => "Cancelled",
            _ => Stage.ToString()
        };

        /// <summary>Status dot color for the grid (matches app conventions).</summary>
        public string StatusColor => Stage switch
        {
            PushRunStage.Success => "#4CAF50",
            PushRunStage.Failed => "#E53935",
            PushRunStage.Offline => "#E53935",
            PushRunStage.Cancelled => "#FF9800",
            PushRunStage.Pending => "#9E9E9E",
            _ => "#2196F3"
        };

        // StatusText/StatusColor are derived; nudge them when their inputs change.
        partial void OnStageChanged(PushRunStage value)
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(Succeeded));
        }

        partial void OnStatusDetailChanged(string value) => OnPropertyChanged(nameof(StatusText));
    }
}
