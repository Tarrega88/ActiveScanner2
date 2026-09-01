using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ActiveScanner.Models;

namespace ActiveScanner.Services
{
    /// <summary>
    /// Service for controlling browser via Chrome DevTools Protocol (CDP).
    /// Works with Chrome, Edge, and other Chromium-based browsers.
    /// No external packages required - uses built-in .NET WebSocket and HTTP clients.
    /// </summary>
    public class ChromeDevToolsService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private ClientWebSocket? _webSocket;
        private int _messageId = 1;
        private const int DefaultPort = 9222;

        public ChromeDevToolsService()
        {
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Launch browser with remote debugging enabled and navigate to URL.
        /// Uses a separate user data directory to avoid conflicts with existing browser instances.
        /// </summary>
        public async Task<bool> LaunchBrowserWithDebugging(string url, TicketBrowser browser, int port = DefaultPort)
        {
            var browserPath = browser switch
            {
                TicketBrowser.Chrome => GetChromePath(),
                TicketBrowser.Edge => GetEdgePath(),
                _ => GetEdgePath()
            };

            // Use a separate user data directory for CDP sessions
            var cdpProfilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ActiveScanner", "CdpBrowserProfile");
            Directory.CreateDirectory(cdpProfilePath);

            try
            {
                // Check if debug port is already in use by a previous CDP session
                bool portInUse = await IsDebugPortInUse(port);
                
                if (!portInUse)
                {
                    // Launch browser with debugging enabled and separate profile
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = browserPath,
                        Arguments = $"--remote-debugging-port={port} --user-data-dir=\"{cdpProfilePath}\" \"{url}\"",
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);

                    // Wait for browser to start and debug port to become available
                    for (int i = 0; i < 20; i++) // Wait up to 10 seconds
                    {
                        await Task.Delay(500);
                        if (await IsDebugPortInUse(port))
                            break;
                    }
                }
                else
                {
                    // CDP browser already running, just open new tab in it
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = browserPath,
                        Arguments = $"--user-data-dir=\"{cdpProfilePath}\" \"{url}\"",
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to launch browser: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if the debug port is responding
        /// </summary>
        private async Task<bool> IsDebugPortInUse(int port)
        {
            try
            {
                var response = await _httpClient.GetAsync($"http://localhost:{port}/json/version");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get list of available browser tabs/targets
        /// </summary>
        public async Task<List<BrowserTab>> GetTabs(int port = DefaultPort)
        {
            try
            {
                var json = await _httpClient.GetStringAsync($"http://localhost:{port}/json");
                var tabs = JsonSerializer.Deserialize<List<BrowserTab>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                return tabs ?? new List<BrowserTab>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get tabs: {ex.Message}");
                return new List<BrowserTab>();
            }
        }

        /// <summary>
        /// Find a tab by URL pattern
        /// </summary>
        public async Task<BrowserTab?> FindTabByUrl(string urlContains, int port = DefaultPort)
        {
            var tabs = await GetTabs(port);
            return tabs.Find(t => t.Url?.Contains(urlContains) == true && t.Type == "page");
        }

        /// <summary>
        /// Connect to a specific tab via WebSocket
        /// </summary>
        public async Task<bool> ConnectToTab(BrowserTab tab)
        {
            if (string.IsNullOrEmpty(tab.WebSocketDebuggerUrl))
                return false;

            try
            {
                _webSocket?.Dispose();
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(tab.WebSocketDebuggerUrl), CancellationToken.None);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to connect to tab: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Execute JavaScript in the connected tab
        /// </summary>
        public async Task<string?> ExecuteJavaScript(string expression)
        {
            if (_webSocket == null || _webSocket.State != WebSocketState.Open)
                return null;

            try
            {
                var message = new
                {
                    id = _messageId++,
                    method = "Runtime.evaluate",
                    @params = new
                    {
                        expression,
                        returnByValue = true
                    }
                };

                var json = JsonSerializer.Serialize(message);
                var bytes = Encoding.UTF8.GetBytes(json);
                await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

                // Receive response
                var buffer = new byte[4096];
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                var responseJson = Encoding.UTF8.GetString(buffer, 0, result.Count);

                return responseJson;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to execute JS: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Set a ServiceNow form field value using g_form.setValue
        /// </summary>
        public async Task<bool> SetServiceNowField(string fieldName, string value)
        {
            var escapedValue = value.Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "");
            var script = $"g_form.setValue('{fieldName}', '{escapedValue}');";
            var result = await ExecuteJavaScript(script);
            return result != null;
        }

        /// <summary>
        /// Open URL, wait for page load, then set post-load fields
        /// </summary>
        public async Task<bool> OpenAndSetPostLoadFields(
            string url, 
            TicketBrowser browser,
            Dictionary<string, string> postLoadFields,
            int waitMs = 2000,
            int port = DefaultPort)
        {
            try
            {
                // Launch browser with the URL
                await LaunchBrowserWithDebugging(url, browser, port);

                // Wait for page to fully load and scripts to run
                await Task.Delay(waitMs);

                // Find the tab we just opened
                var tab = await FindTabByUrl("incident.do", port);
                if (tab == null)
                {
                    Debug.WriteLine("Could not find incident tab");
                    return false;
                }

                // Connect to the tab
                if (!await ConnectToTab(tab))
                {
                    Debug.WriteLine("Could not connect to tab");
                    return false;
                }

                // Wait a bit more for ServiceNow's scripts to finish
                await Task.Delay(500);

                // Set each post-load field
                foreach (var field in postLoadFields)
                {
                    if (!string.IsNullOrWhiteSpace(field.Value))
                    {
                        await SetServiceNowField(field.Key, field.Value);
                        await Task.Delay(100); // Small delay between field sets
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OpenAndSetPostLoadFields failed: {ex.Message}");
                return false;
            }
        }

        private string GetChromePath()
        {
            var paths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 
                    "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), 
                    "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Google", "Chrome", "Application", "chrome.exe")
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                    return path;
            }

            return "chrome";
        }

        private string GetEdgePath()
        {
            var paths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Microsoft", "Edge", "Application", "msedge.exe")
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                    return path;
            }

            return "msedge";
        }

        public void Dispose()
        {
            _webSocket?.Dispose();
            _httpClient.Dispose();
        }
    }

    /// <summary>
    /// Represents a browser tab from CDP /json endpoint
    /// </summary>
    public class BrowserTab
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? WebSocketDebuggerUrl { get; set; }
    }
}
