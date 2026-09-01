using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ActiveScanner.Services
{
    /// <summary>
    /// Service for fetching user sys_ids from ServiceNow JSONv2 API.
    /// Uses a visible WebView2 window for SSO authentication, then HttpClient with captured cookies.
    /// </summary>
    public class SnowUserService2 : IDisposable
    {
        private const string BaseUrl = "https://yourit.va.gov/sys_user_list.do";
        private const string RitmBaseUrl = "https://yourit.va.gov/sc_req_item.do";
        private const string GroupBaseUrl = "https://yourit.va.gov/sys_user_group.do";
        private const string ServiceNowHost = "yourit.va.gov";
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ActiveScanner", "snow_debug.log");
        
        private HttpClient? _httpClient;
        private CookieContainer? _cookieContainer;
        private bool _isAuthenticated;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private TaskCompletionSource<bool>? _authInProgress;

        private static void Log(string message)
        {
            try
            {
                var logDir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(logDir))
                    Directory.CreateDirectory(logDir);
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch { }
        }

        /// <summary>
        /// Shows a login window for the user to authenticate with ServiceNow.
        /// Auto-detects when SSO login is complete and captures cookies.
        /// Must be called from the UI thread.
        /// </summary>
        public async Task<bool> AuthenticateAsync()
        {
            // If already authenticated, return immediately
            if (_isAuthenticated)
                return true;

            // If another auth is already in progress, wait for that result
            if (_authInProgress != null)
            {
                Log("SNOW: Auth already in progress, waiting for existing auth...");
                return await _authInProgress.Task;
            }

            _authInProgress = new TaskCompletionSource<bool>();
            Log("SNOW: Starting authentication...");
            
            var tcs = new TaskCompletionSource<bool>();
            
            var webView = new WebView2();
            
            var loginWindow = new Window
            {
                Title = "ServiceNow Login - Complete SSO login (window will close automatically)",
                Width = 900,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = true,
                Owner = Application.Current.MainWindow,
                Content = webView
            };
            
            try
            {
                loginWindow.Show();
                loginWindow.Activate();
                
                var env = await CoreWebView2Environment.CreateAsync();
                await webView.EnsureCoreWebView2Async(env);
                
                // Navigate to ServiceNow - SSO will redirect
                webView.Source = new Uri($"https://{ServiceNowHost}");
                
                // Auto-detect login completion
                webView.NavigationCompleted += async (s, e) =>
                {
                    if (_isAuthenticated) return; // Already captured
                    
                    var currentUrl = webView.Source?.ToString() ?? "";
                    Log($"SNOW Auth: Nav to {currentUrl}, Success={e.IsSuccess}");
                    
                    // Check if we're on the main ServiceNow UI (not SSO/auth pages)
                    // These URLs indicate full login completion:
                    // - now/nav/ui with pa_dashboard.do (main dashboard)
                    // - navpage.do (classic UI)
                    // - home.do (home page)
                    // NOTE: Must check URL path only, not query string (RelayState contains navpage.do!)
                    var urlPath = currentUrl.Split('?')[0]; // Get just the path, no query string
                    
                    bool containsHost = currentUrl.Contains(ServiceNowHost);
                    bool containsDashboard = urlPath.Contains("pa_dashboard");
                    bool containsNavpage = urlPath.EndsWith("navpage.do");
                    bool containsHome = urlPath.EndsWith("home.do");
                    
                    Log($"SNOW Auth: Check - Host={containsHost}, Dashboard={containsDashboard}, Navpage={containsNavpage}, Home={containsHome}");
                    
                    bool isMainPage = e.IsSuccess && containsHost && (containsDashboard || containsNavpage || containsHome);
                    
                    if (isMainPage)
                    {
                        Log("SNOW Auth: Detected ServiceNow main page, capturing cookies...");
                        
                        // Small delay to ensure all cookies are set
                        await Task.Delay(500);
                        
                        // Get cookies
                        var cookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync($"https://{ServiceNowHost}");
                        
                        Log($"SNOW Auth: Found {cookies.Count} cookies");
                        
                        _cookieContainer = new CookieContainer();
                        bool hasJSession = false;
                        bool hasGlideSession = false;
                        
                        foreach (var cookie in cookies)
                        {
                            Log($"SNOW Auth: Cookie {cookie.Name}");
                            
                            try
                            {
                                _cookieContainer.Add(new Uri($"https://{ServiceNowHost}"), 
                                    new Cookie(cookie.Name, cookie.Value, cookie.Path, ServiceNowHost));
                            }
                            catch (Exception ex)
                            {
                                Log($"SNOW Auth: Failed to add cookie {cookie.Name}: {ex.Message}");
                            }
                            
                            if (cookie.Name == "JSESSIONID") hasJSession = true;
                            if (cookie.Name == "glide_session_store" || cookie.Name == "glide_user_route") hasGlideSession = true;
                        }
                        
                        // Need at least JSESSIONID
                        if (hasJSession)
                        {
                            // Create HttpClient with cookies
                            var handler = new HttpClientHandler
                            {
                                CookieContainer = _cookieContainer,
                                UseCookies = true
                            };
                            _httpClient = new HttpClient(handler);
                            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ActiveScanner/1.0");
                            
                            _isAuthenticated = true;
                            Log($"SNOW Auth: Successfully captured cookies (JSESSIONID={hasJSession}, GlideSession={hasGlideSession})");
                            
                            loginWindow.Title = "✓ Authenticated! Closing...";
                            await Task.Delay(500); // Brief pause so user sees success
                            loginWindow.Close();
                        }
                    }
                };
                
                // Return result when user closes window
                loginWindow.Closed += (s, e) =>
                {
                    tcs.TrySetResult(_isAuthenticated);
                };
                
                var result = await tcs.Task;
                _authInProgress.TrySetResult(result);
                _authInProgress = null;
                return result;
            }
            catch (Exception ex)
            {
                Log($"SNOW Auth failed: {ex.Message}");
                loginWindow.Close();
                _authInProgress?.TrySetResult(false);
                _authInProgress = null;
                return false;
            }
        }

        /// <summary>
        /// Batch fetch sys_ids for multiple users.
        /// </summary>
        public async Task<Dictionary<string, string>> GetSysIdsBatchAsync(
            IEnumerable<string> samAccountNames, 
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            if (!_isAuthenticated || _httpClient == null)
            {
                Log("SNOW: Not authenticated");
                return result;
            }

            var samList = new List<string>();
            foreach (var sam in samAccountNames)
            {
                if (!string.IsNullOrWhiteSpace(sam))
                    samList.Add(sam);
            }

            if (samList.Count == 0)
                return result;

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                // Build OR query for all SAM names
                var queryParts = new List<string>();
                foreach (var sam in samList)
                {
                    queryParts.Add($"user_name={Uri.EscapeDataString(sam)}");
                }
                var query = string.Join("^OR", queryParts);

                var url = $"{BaseUrl}?JSONv2&sysparm_fields=sys_id,user_name&sysparm_query={query}&sysparm_limit={samList.Count}";
                Log($"SNOW batch URL: {url}");
                
                var response = await _httpClient.GetAsync(url, cancellationToken);
                Log($"SNOW batch: HTTP {(int)response.StatusCode}");
                
                if (!response.IsSuccessStatusCode)
                {
                    Log($"SNOW batch failed: {response.StatusCode}");
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _isAuthenticated = false;
                    }
                    return result;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                Log($"SNOW batch: Got {json.Length} chars");

                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("records", out var records))
                {
                    foreach (var user in records.EnumerateArray())
                    {
                        if (user.TryGetProperty("user_name", out var userNameProp) &&
                            user.TryGetProperty("sys_id", out var sysIdProp))
                        {
                            var userName = userNameProp.GetString();
                            var sysId = sysIdProp.GetString();
                            
                            if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(sysId))
                            {
                                result[userName] = sysId;
                            }
                        }
                    }
                }
                
                Log($"SNOW batch: Found {result.Count} users");
            }
            catch (Exception ex)
            {
                Log($"SNOW batch lookup failed: {ex.Message}");
            }
            finally
            {
                _semaphore.Release();
            }

            return result;
        }

        /// <summary>
        /// Lookup a single user's sys_id by email or username.
        /// Returns (sysId, displayName) or (null, null) if not found.
        /// </summary>
        public async Task<(string? SysId, string? DisplayName)> GetSysIdByEmailOrUsernameAsync(
            string input, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(input))
                return (null, null);

            if (!_isAuthenticated || _httpClient == null)
            {
                Log("SNOW: Not authenticated for single lookup");
                return (null, null);
            }

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                string query;
                if (input.Contains('@'))
                {
                    // Email lookup
                    query = $"email={Uri.EscapeDataString(input.Trim())}";
                }
                else
                {
                    // Username lookup
                    query = $"user_name={Uri.EscapeDataString(input.Trim())}";
                }

                var url = $"{BaseUrl}?JSONv2&sysparm_fields=sys_id,user_name,name&sysparm_query={query}&sysparm_limit=1";
                Log($"SNOW single lookup URL: {url}");

                var response = await _httpClient.GetAsync(url, cancellationToken);
                Log($"SNOW single lookup: HTTP {(int)response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _isAuthenticated = false;
                    }
                    return (null, null);
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("records", out var records))
                {
                    foreach (var user in records.EnumerateArray())
                    {
                        var sysId = user.TryGetProperty("sys_id", out var sysIdProp) ? sysIdProp.GetString() : null;
                        var displayName = user.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                        
                        if (!string.IsNullOrEmpty(sysId))
                        {
                            Log($"SNOW single lookup: Found {displayName} ({sysId})");
                            return (sysId, displayName);
                        }
                    }
                }

                Log("SNOW single lookup: User not found");
                return (null, null);
            }
            catch (Exception ex)
            {
                Log($"SNOW single lookup failed: {ex.Message}");
                return (null, null);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Lookup a RITM (Request Item) by its number.
        /// Returns sysId or null if not found.
        /// Accepts number with or without "RITM" prefix.
        /// </summary>
        public async Task<string?> GetRitmSysIdAsync(
            string ritmNumber, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ritmNumber))
                return null;

            if (!_isAuthenticated || _httpClient == null)
            {
                Log("SNOW: Not authenticated for RITM lookup");
                return null;
            }

            // Normalize RITM number - ensure it has the RITM prefix
            var normalizedRitm = ritmNumber.Trim();
            if (!normalizedRitm.StartsWith("RITM", StringComparison.OrdinalIgnoreCase))
            {
                normalizedRitm = "RITM" + normalizedRitm;
            }

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                var url = $"{RitmBaseUrl}?JSONv2&sysparm_query=number={Uri.EscapeDataString(normalizedRitm)}&sysparm_fields=sys_id&sysparm_limit=1";
                Log($"SNOW RITM lookup URL: {url}");

                var response = await _httpClient.GetAsync(url, cancellationToken);
                Log($"SNOW RITM lookup: HTTP {(int)response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _isAuthenticated = false;
                    }
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("records", out var records) && records.GetArrayLength() > 0)
                {
                    var ritm = records[0];
                    var sysId = ritm.TryGetProperty("sys_id", out var sysIdProp) ? sysIdProp.GetString() : null;
                    
                    if (!string.IsNullOrEmpty(sysId))
                    {
                        Log($"SNOW RITM lookup: Found {normalizedRitm} ({sysId})");
                        return sysId;
                    }
                }

                Log($"SNOW RITM lookup: {normalizedRitm} not found");
                return null;
            }
            catch (Exception ex)
            {
                Log($"SNOW RITM lookup failed: {ex.Message}");
                return null;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Lookup a user group by its name.
        /// Returns (sysId, name) or (null, null) if not found.
        /// </summary>
        public async Task<(string? SysId, string? Name)> GetGroupSysIdAsync(
            string groupName, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                return (null, null);

            if (!_isAuthenticated || _httpClient == null)
            {
                Log("SNOW: Not authenticated for group lookup");
                return (null, null);
            }

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                var url = $"{GroupBaseUrl}?JSONv2&sysparm_query=name={Uri.EscapeDataString(groupName.Trim())}&sysparm_fields=sys_id,name,email,description&sysparm_limit=1";
                Log($"SNOW group lookup URL: {url}");

                var response = await _httpClient.GetAsync(url, cancellationToken);
                Log($"SNOW group lookup: HTTP {(int)response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _isAuthenticated = false;
                    }
                    return (null, null);
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("records", out var records) && records.GetArrayLength() > 0)
                {
                    var group = records[0];
                    var sysId = group.TryGetProperty("sys_id", out var sysIdProp) ? sysIdProp.GetString() : null;
                    var name = group.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    
                    if (!string.IsNullOrEmpty(sysId))
                    {
                        Log($"SNOW group lookup: Found {name} ({sysId})");
                        return (sysId, name);
                    }
                }

                Log($"SNOW group lookup: {groupName} not found");
                return (null, null);
            }
            catch (Exception ex)
            {
                Log($"SNOW group lookup failed: {ex.Message}");
                return (null, null);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public bool IsAuthenticated => _isAuthenticated;

        /// <summary>
        /// Performs an authenticated GET against any ServiceNow URL on the same host using the
        /// captured SSO session, returning the raw response body (typically JSONv2). Returns null
        /// if not authenticated or the request fails. Used by other SNOW-backed services (e.g.
        /// vulnerability lookups) so the user only authenticates once.
        /// </summary>
        public async Task<string?> GetRawJsonAsync(string fullUrl, CancellationToken cancellationToken = default)
        {
            if (!_isAuthenticated || _httpClient == null)
            {
                Log("SNOW: Not authenticated for raw GET");
                return null;
            }

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                // Guard against a hung/slow request appearing as a frozen scan.
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

                var response = await _httpClient.GetAsync(fullUrl, timeoutCts.Token);
                Log($"SNOW raw GET: HTTP {(int)response.StatusCode} for {fullUrl}");

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        _isAuthenticated = false;
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                // ServiceNow returns 200 OK with an HTML login/terms page when the session is
                // stale. Detect that so it surfaces as "sign-in required" instead of a silent
                // JSON parse failure. (Matches the SNBulkAssigner "are you logged in?" check.)
                var head = body.TrimStart();
                if (head.StartsWith("<") ||
                    head.Contains("va_termsandconditions", StringComparison.OrdinalIgnoreCase) ||
                    head.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase))
                {
                    Log("SNOW raw GET: got HTML (login/terms) instead of JSON — session likely stale");
                    _isAuthenticated = false;
                    return null;
                }

                return body;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log($"SNOW raw GET timed out for {fullUrl}");
                return null;
            }
            catch (Exception ex)
            {
                Log($"SNOW raw GET failed: {ex.Message}");
                return null;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _semaphore.Dispose();
        }
    }
}
