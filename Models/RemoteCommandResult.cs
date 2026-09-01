using System;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents the result of a remote command execution on a target computer
    /// </summary>
    public class RemoteCommandResult
    {
        /// <summary>
        /// The name of the target computer
        /// </summary>
        public string TargetName { get; set; } = string.Empty;

        /// <summary>
        /// The command that was executed
        /// </summary>
        public string Command { get; set; } = string.Empty;

        /// <summary>
        /// Whether the command executed successfully
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The status of the command (Running, Success, Failed, Timeout, etc.)
        /// </summary>
        public string Status { get; set; } = "Pending";

        /// <summary>
        /// The output from the command
        /// </summary>
        public string? Output { get; set; }

        /// <summary>
        /// Error message if the command failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// When the command started
        /// </summary>
        public DateTime StartTime { get; set; } = DateTime.Now;

        /// <summary>
        /// When the command completed
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// Duration of the command execution in milliseconds
        /// </summary>
        public long? DurationMs => EndTime.HasValue ? (long)(EndTime.Value - StartTime).TotalMilliseconds : null;

        /// <summary>
        /// Human-readable duration
        /// </summary>
        public string DurationText => DurationMs.HasValue 
            ? DurationMs.Value < 1000 ? $"{DurationMs}ms" : $"{DurationMs.Value / 1000.0:F1}s"
            : "—";

        /// <summary>
        /// Combined display of output and error for UI
        /// </summary>
        public string DisplayOutput => !string.IsNullOrEmpty(ErrorMessage) 
            ? ErrorMessage 
            : Output ?? string.Empty;

        /// <summary>
        /// Status color for display
        /// </summary>
        public string StatusColor => Status switch
        {
            "Success" => "#4CAF50",
            "Running" => "#2196F3",
            "Failed" => "#F44336",
            "Timeout" => "#FF9800",
            _ => "#9E9E9E"
        };
    }
}
