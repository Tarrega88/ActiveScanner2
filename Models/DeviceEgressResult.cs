using CommunityToolkit.Mvvm.ComponentModel;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Outcome of the read-only egress preflight probe for a single device: whether it can
    /// reach the download sources the remediation methods rely on (winget CDN, GitHub, .NET).
    /// </summary>
    public enum EgressState
    {
        Unknown,
        Checking,
        Reachable,  // every tested endpoint responded
        Partial,    // some endpoints reachable, some blocked
        Blocked,    // no tested endpoint reachable
        Offline,    // device did not respond to ping
        DnsMismatch,// name resolves to an IP owned by a different host (stale A record) — do not push
        Error       // probe itself failed (WinRM/auth/etc.)
    }

    /// <summary>
    /// Per-device egress preflight result, shown in the Vulnerabilities tab so a tech knows
    /// which devices can actually download fixes before Phase 3 pushes anything.
    /// </summary>
    public partial class DeviceEgressResult : ObservableObject
    {
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>Overall reachability classification.</summary>
        [ObservableProperty]
        private EgressState _state = EgressState.Unknown;

        /// <summary>Short one-line summary, e.g. "5/6 reachable" or "Blocked".</summary>
        [ObservableProperty]
        private string _summary = string.Empty;

        /// <summary>Per-endpoint detail (multiline), surfaced as a tooltip.</summary>
        [ObservableProperty]
        private string _detail = string.Empty;
    }
}
