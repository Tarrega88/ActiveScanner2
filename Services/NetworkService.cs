using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ActiveScanner.Models;

namespace ActiveScanner.Services
{
    /// <summary>
    /// Service for network diagnostic operations
    /// </summary>
    public class NetworkService
    {
        /// <summary>
        /// Pings a single target
        /// </summary>
        public async Task<PingResult> PingAsync(string target, int timeoutMs = 5000, 
            CancellationToken cancellationToken = default)
        {
            var result = new PingResult
            {
                TargetName = target,
                TargetAddress = target,
                Timestamp = DateTime.Now
            };

            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(target, timeoutMs);

                result.Success = reply.Status == IPStatus.Success;
                result.LatencyMs = result.Success ? reply.RoundtripTime : null;
                result.Status = reply.Status.ToString();

                if (reply.Status != IPStatus.Success)
                {
                    result.ErrorMessage = GetPingStatusMessage(reply.Status);
                }
            }
            catch (PingException ex)
            {
                result.Success = false;
                result.Status = "Error";
                result.ErrorMessage = ex.InnerException?.Message ?? ex.Message;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Status = "Error";
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Pings multiple targets in batch
        /// </summary>
        public async Task<List<PingResult>> PingBatchAsync(IEnumerable<AdObjectInfo> computers,
            int timeoutMs = 5000, IProgress<(int current, int total, PingResult result)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var results = new List<PingResult>();
            var computerList = computers.ToList();
            var total = computerList.Count;
            var current = 0;

            foreach (var computer in computerList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await PingAsync(computer.TargetAddress, timeoutMs, cancellationToken);
                result.TargetName = computer.Name;

                results.Add(result);
                current++;

                progress?.Report((current, total, result));
            }

            return results;
        }

        /// <summary>
        /// Tests a TCP port connection
        /// </summary>
        public async Task<PortTestResult> TestPortAsync(string target, int port, int timeoutMs = 5000,
            CancellationToken cancellationToken = default)
        {
            var result = new PortTestResult
            {
                TargetName = target,
                TargetAddress = target,
                Port = port,
                PortName = GetPortName(port),
                Timestamp = DateTime.Now
            };

            var sw = Stopwatch.StartNew();

            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(target, port);
                var timeoutTask = Task.Delay(timeoutMs, cancellationToken);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    result.Success = false;
                    result.Status = "Timeout";
                    result.ErrorMessage = $"Connection timed out after {timeoutMs}ms";
                }
                else if (connectTask.IsFaulted)
                {
                    result.Success = false;
                    result.Status = "Failed";
                    result.ErrorMessage = connectTask.Exception?.InnerException?.Message ?? "Connection failed";
                }
                else
                {
                    result.Success = true;
                    result.Status = "Open";
                    result.LatencyMs = sw.ElapsedMilliseconds;
                }
            }
            catch (SocketException ex)
            {
                result.Success = false;
                result.Status = "Failed";
                result.ErrorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Status = "Error";
                result.ErrorMessage = ex.Message;
            }

            sw.Stop();
            return result;
        }

        /// <summary>
        /// Resolves a DNS name (forward lookup)
        /// </summary>
        public async Task<DnsResult> ResolveDnsForwardAsync(string hostName,
            CancellationToken cancellationToken = default)
        {
            var result = new DnsResult
            {
                Query = hostName,
                QueryType = "Forward",
                Timestamp = DateTime.Now
            };

            try
            {
                var entry = await Dns.GetHostEntryAsync(hostName, cancellationToken);

                result.Success = true;
                result.Status = "Resolved";
                result.HostName = entry.HostName;
                result.IpAddresses = entry.AddressList.Select(a => a.ToString()).ToList();
                result.Aliases = entry.Aliases.ToList();
            }
            catch (SocketException ex)
            {
                result.Success = false;
                result.Status = "Failed";
                result.ErrorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Status = "Error";
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Resolves an IP address (reverse lookup)
        /// </summary>
        public async Task<DnsResult> ResolveDnsReverseAsync(string ipAddress,
            CancellationToken cancellationToken = default)
        {
            var result = new DnsResult
            {
                Query = ipAddress,
                QueryType = "Reverse",
                Timestamp = DateTime.Now
            };

            try
            {
                if (!IPAddress.TryParse(ipAddress, out var ip))
                {
                    result.Success = false;
                    result.Status = "Invalid";
                    result.ErrorMessage = "Not a valid IP address";
                    return result;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var entry = await Dns.GetHostEntryAsync(ip.ToString());

                result.Success = true;
                result.Status = "Resolved";
                result.HostName = entry.HostName;
                result.IpAddresses = entry.AddressList.Select(a => a.ToString()).ToList();
                result.Aliases = entry.Aliases.ToList();
            }
            catch (SocketException ex)
            {
                result.Success = false;
                result.Status = "Failed";
                result.ErrorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Status = "Error";
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Runs traceroute to a target
        /// </summary>
        public async Task<TraceResult> TracerouteAsync(string target, int timeoutMs = 5000, int maxHops = 30,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var result = new TraceResult
            {
                Target = target,
                ToolType = "Traceroute",
                Timestamp = DateTime.Now
            };

            var sw = Stopwatch.StartNew();

            // Run on thread pool to ensure we don't block UI
            await Task.Run(async () =>
            {
                Process? process = null;
                try
                {
                    // Use 1 second per-hop timeout (standard default)
                    // The timeoutMs parameter is the OVERALL timeout for the entire traceroute
                    var perHopTimeout = 1000;
                    
                    // -d = no DNS resolution, -w = timeout per hop in ms, -h = max hops
                    var psi = new ProcessStartInfo
                    {
                        FileName = "tracert",
                        Arguments = $"-d -w {perHopTimeout} -h {maxHops} {target}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    
                    var outputLines = new List<string>();
                    var processExited = new TaskCompletionSource<bool>();
                    
                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            outputLines.Add(e.Data);
                            progress?.Report(e.Data);
                            
                            var hop = ParseTracertLine(e.Data);
                            if (hop != null)
                            {
                                result.Hops.Add(hop);
                            }
                        }
                    };
                    
                    process.Exited += (s, e) => processExited.TrySetResult(true);
                    
                    // Use the passed timeout as overall timeout (from UI settings)
                    using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    overallCts.CancelAfter(timeoutMs);
                    
                    // Register cancellation
                    overallCts.Token.Register(() => processExited.TrySetCanceled());
                    
                    process.Start();
                    process.BeginOutputReadLine();

                    // Wait for process to exit or cancellation
                    try
                    {
                        await processExited.Task;
                        result.Completed = true;
                    }
                    catch (TaskCanceledException)
                    {
                        result.Cancelled = true;
                    }
                    
                    result.RawOutput.AddRange(outputLines);
                }
                catch (OperationCanceledException)
                {
                    result.Cancelled = true;
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = ex.Message;
                }
                finally
                {
                    // Always kill the process if still running
                    if (process != null && !process.HasExited)
                    {
                        try { process.Kill(true); } catch { }
                    }
                    process?.Dispose();
                }
            }, cancellationToken);

            sw.Stop();
            result.Duration = sw.Elapsed;

            return result;
        }

        /// <summary>
        /// Runs pathping to a target
        /// </summary>
        public async Task<TraceResult> PathpingAsync(string target, int timeoutMs = 5000, int maxHops = 30,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var result = new TraceResult
            {
                Target = target,
                ToolType = "Pathping",
                Timestamp = DateTime.Now
            };

            var sw = Stopwatch.StartNew();

            // Run on thread pool to ensure we don't block UI
            await Task.Run(async () =>
            {
                Process? process = null;
                try
                {
                    // -n = no DNS resolution, -p = period between pings in ms, -h = max hops, -q = queries per hop
                    var psi = new ProcessStartInfo
                    {
                        FileName = "pathping",
                        Arguments = $"-n -p {Math.Max(timeoutMs / 10, 100)} -h {maxHops} -q 10 {target}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    
                    var outputLines = new List<string>();
                    var processExited = new TaskCompletionSource<bool>();
                    
                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            outputLines.Add(e.Data);
                            progress?.Report(e.Data);
                        }
                    };
                    
                    process.Exited += (s, e) => processExited.TrySetResult(true);
                    
                    // Pathping can take a very long time - set overall timeout based on settings
                    var overallTimeoutMs = (maxHops * 25000) + 30000;
                    using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    overallCts.CancelAfter(overallTimeoutMs);
                    
                    // Register cancellation
                    overallCts.Token.Register(() => processExited.TrySetCanceled());
                    
                    process.Start();
                    process.BeginOutputReadLine();

                    // Wait for process to exit or cancellation
                    try
                    {
                        await processExited.Task;
                        result.Completed = true;
                    }
                    catch (TaskCanceledException)
                    {
                        result.Cancelled = true;
                    }
                    
                    result.RawOutput.AddRange(outputLines);
                    
                    // Parse pathping output into hops
                    ParsePathpingOutput(outputLines, result);
                }
                catch (OperationCanceledException)
                {
                    result.Cancelled = true;
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = ex.Message;
                }
                finally
                {
                    // Always kill the process if still running
                    if (process != null && !process.HasExited)
                    {
                        try { process.Kill(true); } catch { }
                    }
                    process?.Dispose();
                }
            }, cancellationToken);

            sw.Stop();
            result.Duration = sw.Elapsed;

            return result;
        }

        /// <summary>
        /// Parses pathping output into hop entries
        /// </summary>
        private static void ParsePathpingOutput(List<string> outputLines, TraceResult result)
        {
            // Pathping statistics section format (after "Computing statistics for X seconds..."):
            //             Source to Here   This Node/Link
            // Hop  RTT    Lost/Sent = Pct  Lost/Sent = Pct  Address
            //   0                                           10.162.154.122
            //                                 0/ 100 =  0%   |
            //   1    1ms     0/ 100 =  0%     0/ 100 =  0%  10.162.154.102
            //                                 0/ 100 =  0%   |
            //   2    5ms     1/ 100 =  1%     1/ 100 =  1%  192.168.1.1
            
            var statisticsStarted = false;
            
            foreach (var line in outputLines)
            {
                // Check if we've reached the statistics section
                if (line.Contains("Computing statistics"))
                {
                    statisticsStarted = true;
                    continue;
                }
                
                if (statisticsStarted)
                {
                    // Skip header lines and link-only lines (containing just |)
                    if (line.Contains("Source to Here") || line.Contains("Lost/Sent") || 
                        line.Trim().EndsWith("|") || string.IsNullOrWhiteSpace(line))
                        continue;
                    
                    // Full stats line with RTT: "  1    5ms     0/ 100 =  0%     0/ 100 =  0%  192.168.1.1"
                    // Capture: hop, rtt, node_lost, node_sent, node_pct, link_lost, link_sent, link_pct, ip
                    var fullStatsMatch = Regex.Match(line, 
                        @"^\s*(\d+)\s+(\d+)\s*ms\s+(\d+)/\s*(\d+)\s*=\s*(\d+)%\s+(\d+)/\s*(\d+)\s*=\s*(\d+)%\s+(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})");
                    
                    if (fullStatsMatch.Success)
                    {
                        var hop = new TraceHop
                        {
                            HopNumber = int.Parse(fullStatsMatch.Groups[1].Value),
                            Latency1 = long.Parse(fullStatsMatch.Groups[2].Value),
                            NodeLostPercent = int.Parse(fullStatsMatch.Groups[5].Value),
                            SentCount = int.Parse(fullStatsMatch.Groups[4].Value),
                            LinkLostPercent = int.Parse(fullStatsMatch.Groups[8].Value),
                            IpAddress = fullStatsMatch.Groups[9].Value.Trim(),
                            RawLine = line
                        };
                        result.Hops.Add(hop);
                        continue;
                    }
                    
                    // Hop 0 or timed out line without RTT: "  0                                           10.162.154.122"
                    var noRttMatch = Regex.Match(line, @"^\s*(\d+)\s+(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\s*$");
                    if (noRttMatch.Success)
                    {
                        var hop = new TraceHop
                        {
                            HopNumber = int.Parse(noRttMatch.Groups[1].Value),
                            IpAddress = noRttMatch.Groups[2].Value.Trim(),
                            RawLine = line
                        };
                        result.Hops.Add(hop);
                        continue;
                    }
                    
                    // Timed out hop: "  3     *        ---       ---       ---"
                    var timedOutMatch = Regex.Match(line, @"^\s*(\d+)\s+\*\s+");
                    if (timedOutMatch.Success)
                    {
                        var hop = new TraceHop
                        {
                            HopNumber = int.Parse(timedOutMatch.Groups[1].Value),
                            TimedOut = true,
                            RawLine = line
                        };
                        result.Hops.Add(hop);
                    }
                }
            }
            
            // If no statistics section was found (cancelled early), parse from initial trace
            if (result.Hops.Count == 0)
            {
                foreach (var line in outputLines)
                {
                    // Initial trace format: "  1  192.168.1.1" or with hostname
                    if (line.Contains("maximum of") || line.Contains("Tracing route"))
                        continue;
                        
                    var traceMatch = Regex.Match(line, @"^\s*(\d+)\s+(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})");
                    if (traceMatch.Success)
                    {
                        var hop = new TraceHop
                        {
                            HopNumber = int.Parse(traceMatch.Groups[1].Value),
                            IpAddress = traceMatch.Groups[2].Value.Trim(),
                            RawLine = line
                        };
                        result.Hops.Add(hop);
                    }
                }
            }
        }

        /// <summary>
        /// Launches Remote Desktop Connection to a target
        /// </summary>
        public void LaunchRdp(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "mstsc",
                    Arguments = $"/v:{target}",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to launch RDP: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Opens a URL in the default browser
        /// </summary>
        public void OpenInBrowser(string target, bool useHttps = false)
        {
            try
            {
                var protocol = useHttps ? "https" : "http";
                var url = target.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? target
                    : $"{protocol}://{target}";

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to open browser: {ex.Message}", ex);
            }
        }

        private static TraceHop? ParseTracertLine(string line)
        {
            // Typical format: "  1    <1 ms    <1 ms    <1 ms  192.168.1.1"
            var regex = new Regex(@"^\s*(\d+)\s+(<?\d+\s*ms|\*)\s+(<?\d+\s*ms|\*)\s+(<?\d+\s*ms|\*)\s+(.+)$");
            var match = regex.Match(line);

            if (match.Success)
            {
                var hop = new TraceHop
                {
                    HopNumber = int.Parse(match.Groups[1].Value),
                    RawLine = line
                };

                hop.Latency1 = ParseLatency(match.Groups[2].Value);
                hop.Latency2 = ParseLatency(match.Groups[3].Value);
                hop.Latency3 = ParseLatency(match.Groups[4].Value);
                hop.TimedOut = !hop.Latency1.HasValue && !hop.Latency2.HasValue && !hop.Latency3.HasValue;

                var address = match.Groups[5].Value.Trim();
                if (address != "Request timed out.")
                {
                    hop.IpAddress = address;
                }

                return hop;
            }

            return null;
        }

        private static long? ParseLatency(string value)
        {
            if (value == "*") return null;

            var numMatch = Regex.Match(value, @"(\d+)");
            if (numMatch.Success && long.TryParse(numMatch.Groups[1].Value, out var ms))
            {
                return ms;
            }

            // Handle "<1 ms" as 0
            if (value.Contains("<1"))
            {
                return 0;
            }

            return null;
        }

        private static string GetPingStatusMessage(IPStatus status)
        {
            return status switch
            {
                IPStatus.Success => "Success",
                IPStatus.TimedOut => "Request timed out",
                IPStatus.DestinationHostUnreachable => "Destination host unreachable",
                IPStatus.DestinationNetworkUnreachable => "Destination network unreachable",
                IPStatus.DestinationUnreachable => "Destination unreachable",
                IPStatus.BadRoute => "Bad route",
                IPStatus.TtlExpired => "TTL expired",
                _ => status.ToString()
            };
        }

        /// <summary>
        /// Gets the common name for a well-known port
        /// </summary>
        public static string GetPortName(int port)
        {
            return port switch
            {
                22 => "SSH",
                80 => "HTTP",
                135 => "RPC",
                443 => "HTTPS",
                445 => "SMB",
                3389 => "RDP",
                5985 => "WinRM HTTP",
                5986 => "WinRM HTTPS",
                _ => $"Port {port}"
            };
        }

        /// <summary>
        /// Gets the list of common ports for quick selection
        /// </summary>
        public static List<PortInfo> GetCommonPorts()
        {
            return new List<PortInfo>
            {
                new PortInfo(3389, "RDP"),
                new PortInfo(445, "SMB"),
                new PortInfo(135, "RPC"),
                new PortInfo(5985, "WinRM HTTP"),
                new PortInfo(5986, "WinRM HTTPS"),
                new PortInfo(22, "SSH"),
                new PortInfo(80, "HTTP"),
                new PortInfo(443, "HTTPS")
            };
        }

        /// <summary>
        /// Gets the currently logged-in user from a remote computer via WMI (Win32_ComputerSystem only).
        /// This is a fast query that only checks who is actively logged in right now.
        /// </summary>
        public async Task<(bool Success, string? Username, string? Error)> GetCurrentUserAsync(
            string target, CancellationToken cancellationToken = default, NetworkCredential? credential = null, int timeoutMs = 15000)
        {
            try
            {
                System.Management.ConnectionOptions CreateConnectionOptions(bool useCredentials)
                {
                    var options = new System.Management.ConnectionOptions();
                    options.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
                    options.EnablePrivileges = true;
                    
                    if (useCredentials && credential != null && !string.IsNullOrEmpty(credential.UserName))
                    {
                        var domain = credential.Domain;
                        var username = credential.UserName;
                        
                        if (!string.IsNullOrEmpty(domain))
                        {
                            options.Username = $"{domain}\\{username}";
                        }
                        else
                        {
                            options.Username = username;
                        }
                        options.Password = credential.Password;
                        options.Impersonation = System.Management.ImpersonationLevel.Impersonate;
                        options.Authentication = System.Management.AuthenticationLevel.PacketPrivacy;
                    }
                    return options;
                }

                var wmiTask = Task.Run(() =>
                {
                    var connectionOptions = CreateConnectionOptions(useCredentials: true);
                    
                    try
                    {
                        var scope = new System.Management.ManagementScope($@"\\{target}\root\cimv2", connectionOptions);
                        scope.Connect();

                        var query = new System.Management.ObjectQuery("SELECT UserName FROM Win32_ComputerSystem");
                        using var searcher = new System.Management.ManagementObjectSearcher(scope, query);
                        using var results = searcher.Get();
                        
                        foreach (var obj in results)
                        {
                            using (obj)
                            {
                                var userName = obj["UserName"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(userName))
                                {
                                    // Extract just the username part (remove domain\\)
                                    var parts = userName.Split('\\');
                                    var user = parts.Length > 1 ? parts[^1] : userName;
                                    return (true, (string?)user, (string?)null);
                                }
                            }
                        }
                        
                        // No one logged in
                        return (true, (string?)null, (string?)null);
                    }
                    catch (Exception ex)
                    {
                        // Check for local credentials error - retry without credentials
                        if (ex.Message.Contains("local connections") || ex.HResult == unchecked((int)0x80041064))
                        {
                            try
                            {
                                var retryOptions = CreateConnectionOptions(useCredentials: false);
                                var scope = new System.Management.ManagementScope($@"\\{target}\root\cimv2", retryOptions);
                                scope.Connect();

                                var query = new System.Management.ObjectQuery("SELECT UserName FROM Win32_ComputerSystem");
                                using var searcher = new System.Management.ManagementObjectSearcher(scope, query);
                                using var results = searcher.Get();
                                
                                foreach (var obj in results)
                                {
                                    using (obj)
                                    {
                                        var userName = obj["UserName"]?.ToString();
                                        if (!string.IsNullOrWhiteSpace(userName))
                                        {
                                            var parts = userName.Split('\\');
                                            var user = parts.Length > 1 ? parts[^1] : userName;
                                            return (true, (string?)user, (string?)null);
                                        }
                                    }
                                }
                                return (true, (string?)null, (string?)null);
                            }
                            catch (Exception retryEx)
                            {
                                return (false, (string?)null, retryEx.Message);
                            }
                        }
                        return (false, (string?)null, ex.Message);
                    }
                });

                var delayTask = Task.Delay(timeoutMs, cancellationToken);
                var completedTask = await Task.WhenAny(wmiTask, delayTask);

                if (completedTask == delayTask)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);
                    return (false, null, "Timeout");
                }

                return await wmiTask;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return (false, null, "Timeout");
            }
            catch (UnauthorizedAccessException)
            {
                return (false, null, "Access denied");
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                if (msg.Contains("RPC") || msg.Contains("network path"))
                    return (false, null, "Offline/Unreachable");
                return (false, null, msg);
            }
        }

        /// <summary>
        /// Gets all user profiles from a remote computer via WMI.
        /// Returns a list of all user profiles with their last use times.
        /// Uses fire-and-forget pattern to ensure cancellation/timeout is always responsive,
        /// since WMI operations can hang indefinitely on certain machines.
        /// </summary>
        public async Task<(bool Success, List<Models.UserProfileInfo> Users, string? Error)> GetAllUserProfilesAsync(
            string target, CancellationToken cancellationToken = default, NetworkCredential? credential = null, int timeoutMs = 45000)
        {
            var users = new List<Models.UserProfileInfo>();
            
            try
            {
                // Helper to create connection options
                System.Management.ConnectionOptions CreateConnectionOptions(bool useCredentials)
                {
                    var options = new System.Management.ConnectionOptions();
                    options.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
                    options.EnablePrivileges = true;
                    
                    if (useCredentials && credential != null && !string.IsNullOrEmpty(credential.UserName))
                    {
                        var domain = credential.Domain;
                        var username = credential.UserName;
                        
                        // Format username correctly for WMI
                        if (!string.IsNullOrEmpty(domain))
                        {
                            options.Username = $"{domain}\\{username}";
                        }
                        else
                        {
                            // For local or non-domain accounts, just use the username
                            options.Username = username;
                        }
                        options.Password = credential.Password;
                        options.Impersonation = System.Management.ImpersonationLevel.Impersonate;
                        options.Authentication = System.Management.AuthenticationLevel.PacketPrivacy;
                        
                        // Note: Don't set Authority - it can cause "Invalid parameter" errors
                        // The domain is already included in the Username when needed
                    }
                    return options;
                }

                // Fire-and-forget pattern: WMI calls can hang indefinitely and don't respect
                // cancellation tokens, so we race against a delay that does respect cancellation.
                // If timeout/cancel wins, we abandon the WMI task (it continues in background).
                var wmiTask = Task.Run(() =>
                {
                    // Try with credentials first, retry without if local machine error
                    var connectionOptions = CreateConnectionOptions(useCredentials: true);
                    bool retryWithoutCredentials = false;
                    
                    string? currentlyLoggedInUser = null;

                    // Try Win32_ComputerSystem first for currently logged-in user
                    try
                    {
                        var scope = new System.Management.ManagementScope($@"\\{target}\root\cimv2", connectionOptions);
                        scope.Connect();

                        var query = new System.Management.ObjectQuery("SELECT UserName FROM Win32_ComputerSystem");
                        using var searcher = new System.Management.ManagementObjectSearcher(scope, query);
                        using var computerResults = searcher.Get();
                        
                        foreach (var obj in computerResults)
                        {
                            using (obj)
                            {
                                var userName = obj["UserName"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(userName))
                                {
                                    // Extract just the username part (remove domain\\)
                                    var parts = userName.Split('\\');
                                    currentlyLoggedInUser = parts.Length > 1 ? parts[^1] : userName;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Check for local credentials error (WBEM_E_LOCAL_CREDENTIALS = 0x80041064)
                        if (ex.Message.Contains("local connections") || ex.HResult == unchecked((int)0x80041064))
                        {
                            retryWithoutCredentials = true;
                            connectionOptions = CreateConnectionOptions(useCredentials: false);
                        }
                        // Otherwise continue to get profiles
                    }

                    // Get all user profiles
                    try
                    {
                        // If we need to retry, create fresh connection options
                        if (retryWithoutCredentials)
                        {
                            retryWithoutCredentials = false; // Reset flag
                        }
                        
                        var scope = new System.Management.ManagementScope($@"\\{target}\root\cimv2", connectionOptions);
                        scope.Connect();

                        var query = new System.Management.ObjectQuery(
                            "SELECT LocalPath, LastUseTime, SID FROM Win32_UserProfile WHERE Special = FALSE AND LocalPath LIKE '%Users%'");
                        using var searcher = new System.Management.ManagementObjectSearcher(scope, query);
                        using var profileResults = searcher.Get();

                        var resultUsers = new List<Models.UserProfileInfo>();

                        foreach (var obj in profileResults)
                        {
                            using (obj)
                            {
                                var localPath = obj["LocalPath"]?.ToString();
                                var lastUseStr = obj["LastUseTime"]?.ToString();
                                var sid = obj["SID"]?.ToString();

                                if (string.IsNullOrWhiteSpace(localPath)) continue;

                                // Extract username from path like C:\Users\username
                                var pathParts = localPath.Split('\\');
                                var username = pathParts.Length > 0 ? pathParts[^1] : localPath;

                                DateTime? lastUseTime = null;

                                if (!string.IsNullOrWhiteSpace(lastUseStr) && lastUseStr.Length >= 14)
                                {
                                    try
                                    {
                                        lastUseTime = System.Management.ManagementDateTimeConverter.ToDateTime(lastUseStr);
                                    }
                                    catch { /* Skip invalid dates */ }
                                }

                                var isCurrentlyLoggedIn = !string.IsNullOrEmpty(currentlyLoggedInUser) && 
                                    username.Equals(currentlyLoggedInUser, StringComparison.OrdinalIgnoreCase);

                                resultUsers.Add(new Models.UserProfileInfo
                                {
                                    Username = username,
                                    LocalPath = localPath,
                                    LastUseTime = lastUseTime,
                                    Sid = sid,
                                    IsCurrentlyLoggedIn = isCurrentlyLoggedIn
                                });
                            }
                        }

                        // Sort by currently logged in first, then by last use time descending
                        resultUsers = resultUsers
                            .OrderByDescending(u => u.IsCurrentlyLoggedIn)
                            .ThenByDescending(u => u.LastUseTime ?? DateTime.MinValue)
                            .ToList();

                        if (resultUsers.Count > 0)
                        {
                            return (true, resultUsers, (string?)null);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Check for local credentials error - retry without credentials
                        if (ex.Message.Contains("local connections") || ex.HResult == unchecked((int)0x80041064))
                        {
                            try
                            {
                                var retryOptions = CreateConnectionOptions(useCredentials: false);
                                var scope = new System.Management.ManagementScope($@"\\{target}\root\cimv2", retryOptions);
                                scope.Connect();

                                var query = new System.Management.ObjectQuery(
                                    "SELECT LocalPath, LastUseTime, SID FROM Win32_UserProfile WHERE Special = FALSE AND LocalPath LIKE '%Users%'");
                                using var searcher = new System.Management.ManagementObjectSearcher(scope, query);
                                using var retryResults = searcher.Get();

                                var retryUsers = new List<Models.UserProfileInfo>();

                                foreach (var obj in retryResults)
                                {
                                    using (obj)
                                    {
                                        var localPath = obj["LocalPath"]?.ToString();
                                        var lastUseStr = obj["LastUseTime"]?.ToString();
                                        var sid = obj["SID"]?.ToString();

                                        if (string.IsNullOrWhiteSpace(localPath)) continue;

                                        var pathParts = localPath.Split('\\');
                                        var username = pathParts.Length > 0 ? pathParts[^1] : localPath;

                                        DateTime? lastUseTime = null;
                                        if (!string.IsNullOrWhiteSpace(lastUseStr) && lastUseStr.Length >= 14)
                                        {
                                            try { lastUseTime = System.Management.ManagementDateTimeConverter.ToDateTime(lastUseStr); }
                                            catch { }
                                        }

                                        retryUsers.Add(new Models.UserProfileInfo
                                        {
                                            Username = username,
                                            LocalPath = localPath,
                                            LastUseTime = lastUseTime,
                                            Sid = sid,
                                            IsCurrentlyLoggedIn = false
                                        });
                                    }
                                }

                                retryUsers = retryUsers
                                    .OrderByDescending(u => u.LastUseTime ?? DateTime.MinValue)
                                    .ToList();

                                if (retryUsers.Count > 0)
                                    return (true, retryUsers, (string?)null);
                            }
                            catch (Exception retryEx)
                            {
                                return (false, new List<Models.UserProfileInfo>(), retryEx.Message);
                            }
                        }
                        return (false, new List<Models.UserProfileInfo>(), ex.Message);
                    }

                    return (false, new List<Models.UserProfileInfo>(), "No user profiles found");
                });

                // Race between WMI task and timeout/cancellation
                var delayTask = Task.Delay(timeoutMs, cancellationToken);
                var completedTask = await Task.WhenAny(wmiTask, delayTask);

                if (completedTask == delayTask)
                {
                    // Delay completed first - either timeout or cancellation
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }
                    return (false, users, "Timeout");
                }

                // WMI task completed - return its result
                return await wmiTask;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // Re-throw if it was user cancellation
            }
            catch (OperationCanceledException)
            {
                return (false, users, "Timeout");
            }
            catch (UnauthorizedAccessException)
            {
                return (false, users, "Access denied");
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                if (msg.Contains("RPC") || msg.Contains("network path"))
                    return (false, users, "Offline/Unreachable");
                return (false, users, msg);
            }
        }

        /// <summary>
        /// Executes a remote command on a target computer using PowerShell remoting.
        /// </summary>
        /// <param name="computerName">Target computer name</param>
        /// <param name="command">Command to execute (e.g., "gpupdate /force")</param>
        /// <param name="credential">Optional credentials for authentication</param>
        /// <param name="timeoutMs">Timeout in milliseconds</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Tuple with success status, output, and error message</returns>
        public async Task<(bool Success, string? Output, string? Error)> ExecuteRemoteCommandAsync(
            string computerName,
            string command,
            NetworkCredential? credential = null,
            int timeoutMs = 60000,
            CancellationToken cancellationToken = default)
        {
            string? tempScriptPath = null;
            try
            {
                // Build a PowerShell script that creates credentials inline using .NET types only
                tempScriptPath = Path.Combine(Path.GetTempPath(), $"activescanner_{Guid.NewGuid():N}.ps1");
                
                var scriptBuilder = new System.Text.StringBuilder();
                scriptBuilder.AppendLine("$ErrorActionPreference = 'Stop'");
                
                if (credential != null && !string.IsNullOrEmpty(credential.UserName))
                {
                    // Format username for WinRM: UPN form (user@domain.fqdn) when domain looks like an
                    // FQDN, otherwise NetBIOS form (DOMAIN\user). The hybrid "domain.fqdn\user" form is
                    // not valid for Kerberos/SSPI and can cause ~22s WinRMOperationTimeout failures.
                    string username;
                    if (string.IsNullOrEmpty(credential.Domain))
                        username = credential.UserName;
                    else if (credential.Domain.Contains('.'))
                        username = $"{credential.UserName}@{credential.Domain}";
                    else
                        username = $"{credential.Domain}\\{credential.UserName}";
                    
                    // Encode password as Base64 to avoid escaping issues
                    var passwordBytes = System.Text.Encoding.Unicode.GetBytes(credential.Password);
                    var encodedPassword = Convert.ToBase64String(passwordBytes);
                    
                    scriptBuilder.AppendLine($"$user = '{username}'");
                    scriptBuilder.AppendLine($"$encodedPwd = '{encodedPassword}'");
                    scriptBuilder.AppendLine("$pwdBytes = [Convert]::FromBase64String($encodedPwd)");
                    scriptBuilder.AppendLine("$pwd = [Text.Encoding]::Unicode.GetString($pwdBytes)");
                    scriptBuilder.AppendLine("$secPwd = New-Object Security.SecureString");
                    scriptBuilder.AppendLine("$pwd.ToCharArray() | ForEach-Object { $secPwd.AppendChar($_) }");
                    scriptBuilder.AppendLine("$cred = New-Object Management.Automation.PSCredential($user, $secPwd)");
                    scriptBuilder.AppendLine($"Invoke-Command -ComputerName '{computerName}' -Credential $cred -ScriptBlock {{ {command} }}");
                }
                else
                {
                    scriptBuilder.AppendLine($"Invoke-Command -ComputerName '{computerName}' -ScriptBlock {{ {command} }}");
                }
                
                await File.WriteAllTextAsync(tempScriptPath, scriptBuilder.ToString(), cancellationToken);
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tempScriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);

                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
                var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

                await process.WaitForExitAsync(cts.Token);

                var output = await outputTask;
                var error = await errorTask;

                if (process.ExitCode == 0)
                {
                    return (true, string.IsNullOrWhiteSpace(output) ? "Command completed successfully" : output.Trim(), (string?)null);
                }
                else
                {
                    // Combine output and error for better diagnostics
                    var errorMsg = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
                    return (false, (string?)null, string.IsNullOrWhiteSpace(errorMsg) ? $"Exit code: {process.ExitCode}" : errorMsg);
                }
            }
            catch (OperationCanceledException)
            {
                return (false, (string?)null, "Operation timed out or was cancelled");
            }
            catch (Exception ex)
            {
                return (false, (string?)null, ex.Message);
            }
            finally
            {
                // Clean up temp script
                if (tempScriptPath != null && File.Exists(tempScriptPath))
                {
                    try { File.Delete(tempScriptPath); } catch { /* ignore */ }
                }
            }
        }

        /// <summary>
        /// Runs an operator-supplied command on a target machine over WinRM. Unlike
        /// <see cref="ExecuteRemoteCommandAsync"/>, the command text is never concatenated into the
        /// generated script — it is Base64-encoded and rebuilt on the target — so arbitrary input
        /// cannot break out of or alter the surrounding script. The <paramref name="shell"/> selects
        /// whether the target treats the text as PowerShell or runs it through cmd.exe.
        /// </summary>
        /// <param name="computerName">Target computer name or address.</param>
        /// <param name="commandText">The raw command to run on the target.</param>
        /// <param name="shell">Interpret the text as PowerShell or cmd.exe.</param>
        /// <param name="credential">Admin credential used for the WinRM session.</param>
        /// <param name="timeoutMs">Overall timeout in milliseconds.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task<(bool Success, string? Output, string? Error)> ExecuteCustomCommandAsync(
            string computerName,
            string commandText,
            RemoteShell shell,
            NetworkCredential? credential = null,
            int timeoutMs = 120000,
            CancellationToken cancellationToken = default)
        {
            string? tempScriptPath = null;
            try
            {
                tempScriptPath = Path.Combine(Path.GetTempPath(), $"activescanner_cmd_{Guid.NewGuid():N}.ps1");

                // The operator's command travels as Base64 (safe chars only) and is decoded on the
                // target, so nothing in it can alter the surrounding orchestration script.
                var encodedCmd = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(commandText));
                var useCmd = shell == RemoteShell.Cmd;

                var scriptBuilder = new System.Text.StringBuilder();
                scriptBuilder.AppendLine("$ErrorActionPreference = 'Stop'");
                scriptBuilder.AppendLine($"$computer = '{computerName}'");
                scriptBuilder.AppendLine($"$encodedCmd = '{encodedCmd}'");
                scriptBuilder.AppendLine($"$useCmd = ${(useCmd ? "true" : "false")}");

                // The remote decoder: rebuild the command from Base64, then either evaluate it as a
                // PowerShell scriptblock or hand it to cmd.exe. All streams are merged so the caller
                // sees stdout and stderr together.
                scriptBuilder.AppendLine(
                    "$remoteBlock = { param($enc, $isCmd) " +
                    "$c = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($enc)); " +
                    "if ($isCmd) { & cmd.exe /c $c 2>&1 | Out-String } " +
                    "else { & ([ScriptBlock]::Create($c)) 2>&1 | Out-String } }");

                if (credential != null && !string.IsNullOrEmpty(credential.UserName))
                {
                    // Same username shaping as ExecuteRemoteCommandAsync: UPN for FQDN domains,
                    // NetBIOS otherwise; the hybrid "domain.fqdn\user" form is rejected by SSPI.
                    string username;
                    if (string.IsNullOrEmpty(credential.Domain))
                        username = credential.UserName;
                    else if (credential.Domain.Contains('.'))
                        username = $"{credential.UserName}@{credential.Domain}";
                    else
                        username = $"{credential.Domain}\\{credential.UserName}";

                    var passwordBytes = System.Text.Encoding.Unicode.GetBytes(credential.Password);
                    var encodedPassword = Convert.ToBase64String(passwordBytes);

                    scriptBuilder.AppendLine($"$user = '{username}'");
                    scriptBuilder.AppendLine($"$encodedPwd = '{encodedPassword}'");
                    scriptBuilder.AppendLine("$pwdBytes = [Convert]::FromBase64String($encodedPwd)");
                    scriptBuilder.AppendLine("$plain = [Text.Encoding]::Unicode.GetString($pwdBytes)");
                    scriptBuilder.AppendLine("$secPwd = New-Object Security.SecureString");
                    scriptBuilder.AppendLine("$plain.ToCharArray() | ForEach-Object { $secPwd.AppendChar($_) }");
                    scriptBuilder.AppendLine("$cred = New-Object Management.Automation.PSCredential($user, $secPwd)");
                    scriptBuilder.AppendLine("Invoke-Command -ComputerName $computer -Credential $cred -ScriptBlock $remoteBlock -ArgumentList $encodedCmd, $useCmd");
                }
                else
                {
                    scriptBuilder.AppendLine("Invoke-Command -ComputerName $computer -ScriptBlock $remoteBlock -ArgumentList $encodedCmd, $useCmd");
                }

                await File.WriteAllTextAsync(tempScriptPath, scriptBuilder.ToString(), cancellationToken);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tempScriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);

                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
                var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

                await process.WaitForExitAsync(cts.Token);

                var output = await outputTask;
                var error = await errorTask;

                if (process.ExitCode == 0)
                {
                    return (true, string.IsNullOrWhiteSpace(output) ? "(no output)" : output.Trim(), (string?)null);
                }

                var errorMsg = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
                return (false, string.IsNullOrWhiteSpace(output) ? null : output.Trim(),
                    string.IsNullOrWhiteSpace(errorMsg) ? $"Exit code: {process.ExitCode}" : errorMsg);
            }
            catch (OperationCanceledException)
            {
                return (false, (string?)null, "Operation timed out or was cancelled");
            }
            catch (Exception ex)
            {
                return (false, (string?)null, ex.Message);
            }
            finally
            {
                if (tempScriptPath != null && File.Exists(tempScriptPath))
                {
                    try { File.Delete(tempScriptPath); } catch { /* ignore */ }
                }
            }
        }

        /// <summary>
        /// Formats a DateTime as a human-readable "time ago" string
        /// </summary>
        private static string FormatTimeAgo(DateTime dateTime)
        {
            var span = DateTime.Now - dateTime;

            if (span.TotalMinutes < 1)
                return "just now";
            if (span.TotalMinutes < 60)
                return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24)
                return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7)
                return $"{(int)span.TotalDays}d ago";
            if (span.TotalDays < 30)
                return $"{(int)(span.TotalDays / 7)}w ago";
            if (span.TotalDays < 365)
                return $"{(int)(span.TotalDays / 30)}mo ago";
            
            return $"{(int)(span.TotalDays / 365)}y ago";
        }
    }
}
