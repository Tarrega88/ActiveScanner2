using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ActiveScanner.Models
{
    public partial class NetworkHistoryEntry : ObservableObject
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Action { get; set; } = string.Empty;
        public List<string> Targets { get; set; } = new();

        [ObservableProperty]
        private string _resultText = string.Empty;

        [ObservableProperty]
        private bool _isComplete;

        /// <summary>
        /// Color for the status indicator: Green=success, Red=failure, default=mixed/in-progress
        /// </summary>
        [ObservableProperty]
        private string _statusColor = "Gray";

        public string HeaderText
        {
            get
            {
                var time = Timestamp.ToString("HH:mm:ss");
                var targetDisplay = Targets.Count switch
                {
                    0 => "",
                    1 => Targets[0],
                    <= 3 => string.Join(", ", Targets),
                    _ => $"{Targets[0]}, {Targets[1]} +{Targets.Count - 2} more"
                };
                return $"[{time}] {Action} → {targetDisplay}";
            }
        }

        /// <summary>
        /// Full display text (header + results) for the log
        /// </summary>
        public string DisplayText => string.IsNullOrEmpty(ResultText)
            ? HeaderText
            : $"{HeaderText}\n{ResultText}";

        /// <summary>
        /// Formats ping results into terminal-style text output
        /// </summary>
        public static string FormatPingResults(IEnumerable<PingResult> results)
        {
            var sb = new StringBuilder();
            foreach (var r in results)
            {
                if (r.Success)
                    sb.AppendLine($"  Reply from {r.TargetAddress} ({r.TargetName}): time={r.LatencyMs}ms");
                else
                    sb.AppendLine($"  {r.TargetName} ({r.TargetAddress}): {r.Status ?? "Request timed out"}");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Formats traceroute results into terminal-style text output
        /// </summary>
        public static string FormatTracerouteResults(IEnumerable<TraceResult> results)
        {
            var sb = new StringBuilder();
            foreach (var r in results)
            {
                sb.AppendLine($"  Tracing route to {r.Target}:");
                if (r.Cancelled)
                {
                    sb.AppendLine("  Trace cancelled.");
                    continue;
                }
                foreach (var hop in r.Hops)
                {
                    var lat1 = hop.Latency1.HasValue && hop.Latency1 > 0 ? $"{hop.Latency1,4} ms" : "    *";
                    var lat2 = hop.Latency2.HasValue && hop.Latency2 > 0 ? $"{hop.Latency2,4} ms" : "    *";
                    var lat3 = hop.Latency3.HasValue && hop.Latency3 > 0 ? $"{hop.Latency3,4} ms" : "    *";
                    var addr = string.IsNullOrEmpty(hop.IpAddress) ? "Request timed out." : hop.IpAddress;
                    var host = !string.IsNullOrEmpty(hop.HostName) && hop.HostName != hop.IpAddress
                        ? $"{hop.HostName} [{hop.IpAddress}]" : addr;
                    sb.AppendLine($"  {hop.HopNumber,3}  {lat1}  {lat2}  {lat3}  {host}");
                }
                sb.AppendLine(r.Completed ? "  Trace complete." : "  Trace incomplete.");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Formats pathping results into terminal-style text output
        /// </summary>
        public static string FormatPathpingResults(IEnumerable<TraceResult> results)
        {
            var sb = new StringBuilder();
            foreach (var r in results)
            {
                var duration = r.Duration.TotalSeconds > 0 ? $" in {r.Duration.TotalSeconds:F0}s" : "";
                sb.AppendLine($"  Pathping to {r.Target}{(r.Completed ? $" ({r.Hops.Count} hops{duration})" : r.Cancelled ? " (cancelled)" : " (incomplete)")}:");
                if (r.Cancelled) continue;
                sb.AppendLine($"  {"Hop",-4} {"RTT",-8} {"Lost/Sent",-12} {"Address"}");
                sb.AppendLine($"  {"---",-4} {"---",-8} {"---",-12} {"---"}");
                foreach (var hop in r.Hops)
                {
                    var addr = string.IsNullOrEmpty(hop.IpAddress) ? "*" : hop.IpAddress;
                    var host = !string.IsNullOrEmpty(hop.HostName) && hop.HostName != hop.IpAddress
                        ? $"{hop.HostName} [{hop.IpAddress}]" : addr;
                    var lat = hop.Latency1.HasValue && hop.Latency1 > 0 ? $"{hop.Latency1}ms" : "---";
                    var loss = hop.NodeLostPercent.HasValue
                        ? $"{hop.NodeLostPercent}%" + (hop.SentCount.HasValue ? $" ({hop.SentCount - (hop.SentCount * hop.NodeLostPercent / 100)}/{hop.SentCount})" : "")
                        : "---";
                    sb.AppendLine($"  {hop.HopNumber,-4} {lat,-8} {loss,-12} {host}");
                }
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Formats DNS results into terminal-style text output
        /// </summary>
        public static string FormatDnsResults(IEnumerable<DnsResult> results)
        {
            var sb = new StringBuilder();
            foreach (var r in results)
            {
                if (r.Success)
                {
                    if (r.QueryType == "Forward")
                    {
                        var name = r.HostName ?? r.TargetName;
                        sb.AppendLine($"  Name:    {name}");
                        foreach (var ip in r.IpAddresses)
                            sb.AppendLine($"  Address: {ip}");
                        if (r.Aliases?.Count > 0)
                            sb.AppendLine($"  Aliases: {r.AliasesDisplay}");
                    }
                    else
                    {
                        sb.AppendLine($"  {r.Query} → {r.HostName}");
                    }
                }
                else
                {
                    sb.AppendLine($"  *** {r.TargetName}: {r.ErrorMessage ?? "DNS request failed"}");
                }
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Formats port test results into terminal-style text output
        /// </summary>
        public static string FormatPortTestResults(IEnumerable<PortTestResult> results)
        {
            var sb = new StringBuilder();
            foreach (var r in results)
            {
                var portLabel = !string.IsNullOrEmpty(r.PortName) ? $"{r.PortName}/{r.Port}" : $"{r.Port}";
                if (r.Success)
                    sb.AppendLine($"  {r.TargetName}:{r.Port} ({portLabel}) — Open, {r.LatencyMs}ms");
                else
                    sb.AppendLine($"  {r.TargetName}:{r.Port} ({portLabel}) — {r.Status ?? "Connection failed"}");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Formats remote command results (GPUpdate, Reboot) into terminal-style text output
        /// </summary>
        public static string FormatRemoteCommandResults(IEnumerable<RemoteCommandResult> results)
        {
            var sb = new StringBuilder();
            foreach (var r in results)
            {
                var duration = r.EndTime.HasValue
                    ? $" ({(r.EndTime.Value - r.StartTime).TotalSeconds:F1}s)"
                    : "";
                if (r.Success)
                {
                    sb.AppendLine($"  {r.TargetName}: {r.Status}{duration}");
                    if (!string.IsNullOrEmpty(r.Output))
                        sb.AppendLine($"    {r.Output.Replace("\n", "\n    ").TrimEnd()}");
                }
                else
                {
                    sb.AppendLine($"  {r.TargetName}: {r.Status}{duration}");
                    if (!string.IsNullOrEmpty(r.ErrorMessage))
                        sb.AppendLine($"    {r.ErrorMessage}");
                }
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Formats last user results into terminal-style text output
        /// </summary>
        public static string FormatLastUserResults(IEnumerable<LastUserResult> results)
        {
            var sb = new StringBuilder();
            foreach (var r in results)
            {
                if (r.Success)
                {
                    var login = r.IsLoggedIn ? " (active session)" : "";
                    sb.AppendLine($"  {r.TargetName}: {r.LastUser}{login}");
                    if (r.AllProfiles?.Count > 1)
                    {
                        foreach (var p in r.AllProfiles.Skip(1).Take(3))
                        {
                            var lastUsed = p.LastUseTime.HasValue ? $" — last seen {p.LastUseTimeFormatted}" : "";
                            sb.AppendLine($"    {p.Username}{lastUsed}");
                        }
                        if (r.AllProfiles.Count > 4)
                            sb.AppendLine($"    ... and {r.AllProfiles.Count - 4} more profiles");
                    }
                }
                else
                {
                    sb.AppendLine($"  {r.TargetName}: {r.Status ?? "Failed"}");
                }
            }
            return sb.ToString().TrimEnd();
        }
    }
}
