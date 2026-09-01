using System;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Result of a port test operation
    /// </summary>
    public class PortTestResult
    {
        public string TargetName { get; set; } = string.Empty;
        public string TargetAddress { get; set; } = string.Empty;
        public int Port { get; set; }
        public string PortName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public long? LatencyMs { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? ErrorMessage { get; set; }

        public string StatusIcon => Success ? "CheckCircle" : "CloseCircle";
        public string StatusColor => Success ? "Green" : "Red";
    }
}
