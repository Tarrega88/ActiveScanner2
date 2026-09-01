using System;
using System.Collections.Generic;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Line item from traceroute or pathping output
    /// </summary>
    public class TraceHop
    {
        public int HopNumber { get; set; }
        public string? IpAddress { get; set; }
        public string? HostName { get; set; }
        public long? Latency1 { get; set; }
        public long? Latency2 { get; set; }
        public long? Latency3 { get; set; }
        public bool TimedOut { get; set; }
        public string RawLine { get; set; } = string.Empty;
        
        /// <summary>
        /// Target name for grouping in flattened views
        /// </summary>
        public string Target { get; set; } = string.Empty;
        
        /// <summary>
        /// Total hops for this target (for group header display)
        /// </summary>
        public int TotalHops { get; set; }
        
        // Pathping-specific fields
        public int? NodeLostPercent { get; set; }  // Packet loss at this node
        public int? LinkLostPercent { get; set; }  // Packet loss on link to this node
        public int? SentCount { get; set; }        // Number of packets sent
        
        /// <summary>
        /// Duration for this target (for pathping group header)
        /// </summary>
        public TimeSpan? TargetDuration { get; set; }

        /// <summary>
        /// RTT for display - uses Latency1 (the primary latency value)
        /// </summary>
        public string RttDisplay => Latency1.HasValue ? $"{Latency1}ms" : "-";

        public string LatencyDisplay
        {
            get
            {
                if (TimedOut) return "* * *";
                var parts = new List<string>();
                if (Latency1.HasValue) parts.Add($"{Latency1}ms");
                if (Latency2.HasValue) parts.Add($"{Latency2}ms");
                if (Latency3.HasValue) parts.Add($"{Latency3}ms");
                return parts.Count > 0 ? string.Join(" / ", parts) : "*";
            }
        }
        
        /// <summary>
        /// Packet loss display for pathping
        /// </summary>
        public string PacketLossDisplay
        {
            get
            {
                if (!NodeLostPercent.HasValue && !LinkLostPercent.HasValue)
                    return "-";
                    
                var nodeLoss = NodeLostPercent.HasValue ? $"{NodeLostPercent}%" : "-";
                var linkLoss = LinkLostPercent.HasValue ? $"{LinkLostPercent}%" : "-";
                
                // If both are 0, just show 0%
                if (NodeLostPercent == 0 && LinkLostPercent == 0)
                    return "0%";
                    
                return linkLoss; // Link loss is typically more useful
            }
        }
    }

    /// <summary>
    /// Full traceroute/pathping result
    /// </summary>
    public class TraceResult
    {
        public string Target { get; set; } = string.Empty;
        public string ToolType { get; set; } = string.Empty; // Traceroute or Pathping
        public bool Completed { get; set; }
        public bool Cancelled { get; set; }
        public List<TraceHop> Hops { get; set; } = new();
        public List<string> RawOutput { get; set; } = new();
        public TimeSpan Duration { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? ErrorMessage { get; set; }
    }
}
