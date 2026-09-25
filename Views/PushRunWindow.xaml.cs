using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ActiveScanner.Models;
using ActiveScanner.Services;
using Microsoft.Win32;

namespace ActiveScanner.Views
{
    /// <summary>
    /// Modal for the Push &amp; Run feature: choose a payload (file or folder), pick the entry point,
    /// then copy and run it silently on each selected machine with live per-row status.
    /// </summary>
    public partial class PushRunWindow : Window
    {
        private readonly PushRunService _service;
        private readonly NetworkCredential _credential;
        private readonly ObservableCollection<PushRunTarget> _targets = new();

        private string? _sourcePath;
        private bool _isFolder;
        private bool _updatingArgs;
        private string _lastAppliedDefault = string.Empty;
        private string? _detectedExeFlag;
        private string? _detectedFramework;
        private PackageHint? _packageHint;

        // Entry point (and optional known silent flag) chosen after unpacking a self-extractor.
        private sealed record PackageHint(string Entry, string? Framework, string? Flag);

        // Kodak Alaris setup.ini ships its [SILENT] keys commented out; these are the ones to enable.
        private static readonly Regex KodakSilentLines = new(
            @"^;(?=\[SILENT\]|(?:SKIPTWAINIFNODOTNET|UPDATE|WELCOME|PLEASEWAIT|FINISH|REPORTERROR|TEMPDIR)=)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        private CancellationTokenSource? _cts;
        private bool _isRunning;

        /// <summary>Per-target outcomes, available to the caller after the dialog closes.</summary>
        public IReadOnlyList<PushRunResult> Results { get; private set; } = Array.Empty<PushRunResult>();

        /// <summary>True once a batch has actually been executed.</summary>
        public bool DidRun { get; private set; }

        /// <summary>Display name of the payload that was run (for summaries).</summary>
        public string PayloadName { get; private set; } = string.Empty;

        public PushRunWindow(PushRunService service, NetworkCredential credential,
            IEnumerable<(string Name, string Address)> targets)
        {
            InitializeComponent();
            _service = service;
            _credential = credential;

            foreach (var (name, address) in targets)
            {
                _targets.Add(new PushRunTarget
                {
                    ComputerName = name,
                    Address = string.IsNullOrWhiteSpace(address) ? name : address,
                    AllowTaskFallback = true
                });
            }
            MachinesGrid.ItemsSource = _targets;
        }

        // Clamps the timeout field to a sane 1–120 minute range; defaults to 10 on bad input.
        private int ParseTimeoutMinutes()
        {
            if (int.TryParse(TimeoutBox.Text.Trim(), out var m) && m > 0)
                return Math.Min(m, 120);
            return 10;
        }

        // Reads the concurrency dropdown; defaults to 10, capped at 20.
        private int ParseConcurrency()
        {
            var text = (ConcurrencyCombo.SelectedItem as ComboBoxItem)?.Content as string;
            if (int.TryParse(text, out var n) && n > 0)
                return Math.Min(n, 20);
            return 10;
        }

        // --- Payload selection ---------------------------------------------------------------

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (_isRunning) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
            _ = SelectPayloadAsync(paths[0]);
        }

        private void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select payload file",
                Filter = "Installers, scripts & drivers (*.exe;*.msi;*.ps1;*.bat;*.cmd;*.inf)|*.exe;*.msi;*.ps1;*.bat;*.cmd;*.inf|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
                _ = SelectPayloadAsync(dlg.FileName);
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Select payload folder" };
            if (dlg.ShowDialog() == true)
                SetPayload(dlg.FolderName);
        }

        // Self-extracting ZIP installers (e.g. WinZip SFX) launch a wizard that ignores silent flags,
        // so offer to unpack locally and push the contents with the real installer as entry point.
        private async Task SelectPayloadAsync(string path)
        {
            if (!File.Exists(path)
                || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                SetPayload(path);
                return;
            }

            var entryCount = await Task.Run(() => CountZipEntries(path));
            var isPaperStream = entryCount == 0 && await Task.Run(() => IsPaperStreamWrapper(path));
            if (entryCount == 0 && !isPaperStream)
            {
                SetPayload(path);
                return;
            }

            var what = isPaperStream
                ? "a Fujitsu PaperStream IP package. Its wrapper and Setup.exe crash when run remotely"
                : $"a self-extracting package ({entryCount} files). These usually open a setup wizard that can't be silenced, so they hang when run remotely";
            var answer = MessageBox.Show(this,
                $"\"{Path.GetFileName(path)}\" is {what}.\n\n" +
                "Unpack it on this PC and push the contents instead? (Recommended)",
                "Push & Run", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                SetPayload(path);
                return;
            }

            var target = Path.Combine(Path.GetTempPath(), "ActiveScanner_PushRun",
                Path.GetFileNameWithoutExtension(path));
            DropZone.IsEnabled = false;
            PayloadSummary.Text = $"Unpacking {Path.GetFileName(path)}…";
            PayloadHash.Text = string.Empty;
            try
            {
                var (root, hint) = await Task.Run(() =>
                {
                    if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                    Directory.CreateDirectory(target);
                    if (isPaperStream) return UnpackPaperStream(path, target);
                    ZipFile.ExtractToDirectory(path, target);
                    return (target, PreparePackage(target));
                });
                SetPayload(root, hint);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Couldn't unpack the package: {ex.Message}\n\nUsing the original file instead.",
                    "Push & Run", MessageBoxButton.OK, MessageBoxImage.Warning);
                SetPayload(path);
            }
            finally
            {
                DropZone.IsEnabled = !_isRunning;
            }
        }

        private static int CountZipEntries(string path)
        {
            try
            {
                using var zip = ZipFile.OpenRead(path);
                return zip.Entries.Count;
            }
            catch { return 0; }
        }

        private static bool IsPaperStreamWrapper(string path)
        {
            try
            {
                var text = ReadSignatureText(path);
                return text.Contains("PaperStream IP **** Make Updater", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("PsipUpdater.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // Runs the PFU wrapper with /X (extract only) in an empty folder so it never hits its
        // overwrite prompts; /H stops it hiding the foreground window (which would be ours).
        private static (string Root, PackageHint? Hint) UnpackPaperStream(string exe, string work)
        {
            var copy = Path.Combine(work, Path.GetFileName(exe));
            File.Copy(exe, copy);
            var psi = new ProcessStartInfo(copy, "/X /H")
            {
                WorkingDirectory = work,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi) ?? throw new InvalidOperationException("Unpacker didn't start"))
            {
                if (!p.WaitForExit(TimeSpan.FromMinutes(10)))
                {
                    try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
                    throw new TimeoutException("Unpacking timed out");
                }
                if (p.ExitCode != 0)
                    throw new InvalidOperationException($"Unpacker exited with code {p.ExitCode}");
            }
            File.Delete(copy);

            var setupIni = Directory.EnumerateFiles(work, "fi-setup.ini", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new FileNotFoundException("fi-setup.ini not found after unpacking");
            var root = Path.GetDirectoryName(setupIni)!;
            return (root, FindPaperStreamMsi(root, setupIni));
        }

        // Setup.exe just runs msiexec with the command in the first component INI listed in
        // fi-setup.ini; reproduce that command (minus its placeholders) for the driver MSI.
        private static PackageHint? FindPaperStreamMsi(string root, string setupIni)
        {
            const string framework = "PaperStream IP (Fujitsu/PFU)";
            try
            {
                var inst = File.ReadLines(setupIni, Encoding.Latin1)
                    .Select(l => Regex.Match(l, @"^\s*inst\d+\s*=\s*(.+?)\s*$", RegexOptions.IgnoreCase))
                    .FirstOrDefault(m => m.Success)?.Groups[1].Value
                    .Replace("%OWNDIR%", root, StringComparison.OrdinalIgnoreCase);
                if (inst != null && File.Exists(inst))
                {
                    var para = File.ReadLines(inst, Encoding.Latin1)
                        .Select(l => Regex.Match(l, @"^\s*SetupPara01\s*=.*\\([^""\\]+\.msi)""\s*(.*?)""?\s*$", RegexOptions.IgnoreCase))
                        .FirstOrDefault(m => m.Success);
                    if (para != null)
                    {
                        var msi = Directory.EnumerateFiles(root, para.Groups[1].Value, SearchOption.AllDirectories).FirstOrDefault();
                        if (msi != null)
                        {
                            var extra = para.Groups[2].Value
                                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                .Where(t => !(t.StartsWith('%') && t.EndsWith('%'))
                                    && !t.Equals("/qn", StringComparison.OrdinalIgnoreCase)
                                    && !t.Equals("/norestart", StringComparison.OrdinalIgnoreCase));
                            var flags = string.Join(" ", new[] { "/qn", "/norestart" }.Concat(extra));
                            return new PackageHint(Path.GetRelativePath(root, msi), framework, flags);
                        }
                    }
                }
            }
            catch { /* fall through to the largest MSI */ }

            var largest = Directory.EnumerateFiles(root, "*.msi", SearchOption.AllDirectories)
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();
            return largest == null ? null
                : new PackageHint(Path.GetRelativePath(root, largest), framework, "/qn /norestart");
        }

        // Picks the inner installer and applies any vendor-specific silent configuration.
        private static PackageHint? PreparePackage(string root)
        {
            foreach (var ini in Directory.EnumerateFiles(root, "setup.ini", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(ini, Encoding.Latin1);
                if (!text.Contains("Kodak Alaris", StringComparison.OrdinalIgnoreCase)
                    || !text.Contains(";[SILENT]", StringComparison.OrdinalIgnoreCase))
                    continue;
                var exe = Path.Combine(Path.GetDirectoryName(ini)!, "setup.exe");
                if (!File.Exists(exe)) continue;

                File.WriteAllText(ini, KodakSilentLines.Replace(text, string.Empty), Encoding.Latin1);
                return new PackageHint(Path.GetRelativePath(root, exe), "Kodak Alaris", "/S");
            }

            var candidate = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f))
                .Where(rel =>
                {
                    var name = Path.GetFileName(rel);
                    return name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("setup.exe", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("install.exe", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(rel => rel.Count(c => c == Path.DirectorySeparatorChar))
                .ThenBy(rel => rel.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .FirstOrDefault();
            return candidate == null ? null : new PackageHint(candidate, null, null);
        }

        private void SetPayload(string path, PackageHint? hint = null)
        {
            _isFolder = Directory.Exists(path);
            if (!_isFolder && !File.Exists(path))
            {
                MessageBox.Show(this, "The dropped item could not be found.", "Push & Run",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _sourcePath = path;
            _packageHint = hint;
            EntryPointCombo.Items.Clear();

            if (_isFolder)
            {
                var root = path.TrimEnd(Path.DirectorySeparatorChar);
                var runnable = new[] { ".exe", ".msi", ".ps1", ".bat", ".cmd", ".inf" };
                var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Select(f => f.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar))
                    .ToList();

                // Offer runnable types first so the likely entry point is easy to pick.
                foreach (var rel in files.Where(f => runnable.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                         .Concat(files.Where(f => !runnable.Contains(Path.GetExtension(f).ToLowerInvariant()))))
                {
                    EntryPointCombo.Items.Add(rel);
                }

                PayloadSummary.Text = hint != null
                    ? $"Unpacked: {Path.GetFileName(root)}  ({files.Count} file(s))"
                    : $"Folder: {Path.GetFileName(root)}  ({files.Count} file(s))";
                PayloadHash.Text = string.Empty;
                if (hint != null && EntryPointCombo.Items.Contains(hint.Entry))
                    EntryPointCombo.SelectedItem = hint.Entry;
                else if (EntryPointCombo.Items.Count > 0)
                    EntryPointCombo.SelectedIndex = 0;
            }
            else
            {
                var name = Path.GetFileName(path);
                EntryPointCombo.Items.Add(name);
                EntryPointCombo.SelectedIndex = 0;
                PayloadSummary.Text = $"File: {name}  ({new FileInfo(path).Length / 1024.0:N0} KB)";
                PayloadHash.Text = "SHA256: computing…";
                _ = ShowHashAsync(path);
            }

            RefreshForSelection();
            ApplyCopyOnlyState();
        }

        private async Task ShowHashAsync(string filePath)
        {
            try
            {
                var hash = await Task.Run(() => ComputeSha256(filePath));
                if (_sourcePath == filePath)
                    PayloadHash.Text = "SHA256: " + hash;
            }
            catch
            {
                PayloadHash.Text = string.Empty;
            }
        }

        private static string ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(sha.ComputeHash(stream));
        }

        // --- Run method, flag chips & preview ------------------------------------------------

        private void EntryPointCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshForSelection();

        private void RunAsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyCopyOnlyState();
            RefreshForSelection();
        }

        // Copy-only is the "Don't run anything (copy only)" entry in the Run-as dropdown.
        private bool IsCopyOnly() =>
            (RunAsCombo?.SelectedItem as ComboBoxItem)?.Tag as string == "CopyOnly";

        private void ArgumentsBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingArgs) return;
            RefreshRunUi();
        }

        // Re-detect the installer (for exe), reapply defaults, and refresh chips/preview/note.
        private void RefreshForSelection()
        {
            DetectExeIfNeeded();
            ApplyDefaultFlags();
            RefreshRunUi();
        }

        // Swaps in the current type's default silent flags when the box is empty OR still holds a
        // previously auto-applied default. Once the operator edits the flags, we leave them alone.
        // For exe the "default" is the flag inferred from the installer's framework (or blank).
        private void ApplyDefaultFlags()
        {
            if (ArgumentsBox == null) return;
            var def = ActiveHint()?.Flag ?? GetEffectiveKindString() switch
            {
                "msi" => "/quiet /norestart",
                "inf" or "inf-folder" => "/subdirs",
                "exe" => _detectedExeFlag ?? string.Empty,
                _ => string.Empty
            };
            var current = ArgumentsBox.Text.Trim();
            var pristine = string.IsNullOrEmpty(current)
                || string.Equals(current, _lastAppliedDefault, StringComparison.Ordinal);
            if (!pristine) return;

            _updatingArgs = true;
            ArgumentsBox.Text = def;
            _updatingArgs = false;
            _lastAppliedDefault = def;
        }

        // Sniffs the selected exe's bytes for a known installer-framework signature and caches the
        // matching silent flag. Cleared for non-exe kinds so we don't read files needlessly.
        private void DetectExeIfNeeded()
        {
            _detectedExeFlag = null;
            _detectedFramework = null;
            if (GetEffectiveKindString() != "exe") return;

            if (ActiveHint() is { Framework: not null } hint)
            {
                _detectedFramework = hint.Framework;
                _detectedExeFlag = hint.Flag;
                return;
            }

            var path = GetSelectedEntryLocalPath();
            if (path == null || !File.Exists(path)) return;

            var (framework, flag) = DetectInstallerFramework(path);
            _detectedFramework = framework;
            _detectedExeFlag = flag;
        }

        // The unpack hint only applies while its entry point is the one selected.
        private PackageHint? ActiveHint() =>
            _packageHint != null
            && string.Equals(EntryPointCombo?.SelectedItem as string, _packageHint.Entry, StringComparison.OrdinalIgnoreCase)
                ? _packageHint
                : null;

        private string? GetSelectedEntryLocalPath()
        {
            if (string.IsNullOrEmpty(_sourcePath)) return null;
            if (!_isFolder) return _sourcePath;
            if (EntryPointCombo.SelectedItem is string rel && !string.IsNullOrEmpty(rel))
                return Path.Combine(_sourcePath.TrimEnd(Path.DirectorySeparatorChar), rel);
            return null;
        }

        // Returns (framework name, silent flag) for common installer builders, or (null, null).
        private static (string? Framework, string? Flag) DetectInstallerFramework(string path)
        {
            try
            {
                var text = ReadSignatureText(path);
                if (text.Contains("Inno Setup", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("JR.Inno.Setup", StringComparison.OrdinalIgnoreCase))
                    return ("Inno Setup", "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART");
                if (text.Contains("Nullsoft", StringComparison.OrdinalIgnoreCase))
                    return ("NSIS", "/S");
                if (text.Contains("wixburn", StringComparison.OrdinalIgnoreCase))
                    return ("WiX", "/quiet /norestart");
                if (text.Contains("InstallShield", StringComparison.OrdinalIgnoreCase))
                    return ("InstallShield", "/s /v\"/qn\"");
            }
            catch { /* unreadable → unknown */ }
            return (null, null);
        }

        // Reads the head and tail of the file into a printable string (nulls dropped so UTF-16
        // signatures collapse to ASCII). Signatures live in PE resources (start) or overlays (end).
        private static string ReadSignatureText(string path)
        {
            const int chunk = 4 * 1024 * 1024;
            using var fs = File.OpenRead(path);
            var len = fs.Length;
            var sb = new StringBuilder();

            var head = new byte[(int)Math.Min(chunk, len)];
            _ = fs.Read(head, 0, head.Length);
            AppendPrintable(sb, head);

            if (len > chunk)
            {
                var tailLen = (int)Math.Min(chunk, len - chunk);
                var tail = new byte[tailLen];
                fs.Seek(-tailLen, SeekOrigin.End);
                _ = fs.Read(tail, 0, tail.Length);
                AppendPrintable(sb, tail);
            }
            return sb.ToString();
        }

        private static void AppendPrintable(StringBuilder sb, byte[] bytes)
        {
            foreach (var b in bytes)
            {
                if (b == 0) continue;                       // drop nulls: collapses UTF-16 to ASCII
                sb.Append(b >= 32 && b < 127 ? (char)b : ' ');
            }
        }

        private void RefreshRunUi()
        {
            // Some handlers fire during XAML init before every named element exists.
            if (FlagChips == null || ArgumentsBox == null || DestinationBox == null
                || WillRunText == null || RunAsCombo == null || EntryPointCombo == null
                || DetectionNote == null)
                return;
            RebuildFlagChips();
            UpdatePreview();
            UpdateDetectionNote();
        }

        // Shows what installer we detected for an exe (and whether a silent flag was applied),
        // or warns when the type is unknown so the operator knows to set a flag.
        private void UpdateDetectionNote()
        {
            var framework = _detectedFramework ?? ActiveHint()?.Framework;
            if (IsCopyOnly() || (GetEffectiveKindString() != "exe" && framework == null))
            {
                DetectionNote.Visibility = Visibility.Collapsed;
                return;
            }
            DetectionNote.Visibility = Visibility.Visible;
            if (framework != null)
            {
                DetectionNote.Text = $"Detected {framework} installer — silent flag applied automatically.";
                DetectionNote.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            }
            else
            {
                DetectionNote.Text = "Unknown installer type — set a silent flag manually or it may hang in the background.";
                DetectionNote.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
            }
        }

        private void RebuildFlagChips()
        {
            FlagChips.Children.Clear();
            foreach (var flag in FlagsForKind(GetEffectiveKindString()))
            {
                var chip = new CheckBox
                {
                    Content = flag,
                    Margin = new Thickness(0, 0, 12, 2),
                    FontSize = 11,
                    IsChecked = HasToken(flag) // set before wiring handlers so it doesn't self-fire
                };
                var token = flag;
                chip.Checked += (_, __) => InsertToken(token);
                chip.Unchecked += (_, __) => RemoveToken(token);
                FlagChips.Children.Add(chip);
            }
        }

        private void UpdatePreview()
        {
            WillRunText.Text = IsCopyOnly()
                ? "Copy only — nothing will be run on the target."
                : "Will run:  " + BuildPreviewCommand();
        }

        private string BuildPreviewCommand()
        {
            var kind = GetEffectiveKindString();
            var args = ArgumentsBox.Text.Trim();
            var dest = DestinationBox.Text.Trim().TrimEnd('\\');
            var entry = EntryPointCombo.SelectedItem as string ?? "<file>";
            var full = $"{dest}\\{entry}";
            string cmd = kind switch
            {
                "msi" => $"msiexec /i \"{full}\" {args}",
                "ps1" => $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{full}\" {args}",
                "bat" => $"cmd /c \"{full}\" {args}",
                "inf" => $"pnputil /add-driver \"{full}\" /install {args}",
                "inf-folder" => $"pnputil /add-driver \"{dest}\\*.inf\" /install {args}",
                _ => $"\"{full}\" {args}"
            };
            return cmd.Trim();
        }

        // Resolves the "Run as" choice (or Auto → by extension) to the service's kind string.
        private string GetEffectiveKindString()
        {
            var tag = (RunAsCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Auto";
            if (tag != "Auto")
                return tag switch
                {
                    "Executable" => "exe",
                    "Msi" => "msi",
                    "PowerShell" => "ps1",
                    "Batch" => "bat",
                    "DriverInf" => "inf",
                    "DriverFolder" => "inf-folder",
                    "CopyOnly" => "copyonly",
                    _ => "exe"
                };

            var entry = EntryPointCombo.SelectedItem as string ?? string.Empty;
            return Path.GetExtension(entry).ToLowerInvariant() switch
            {
                ".msi" => "msi",
                ".ps1" => "ps1",
                ".bat" or ".cmd" => "bat",
                ".inf" => "inf",
                _ => "exe"
            };
        }

        private static string[] FlagsForKind(string kind) => kind switch
        {
            "msi" => new[] { "/quiet", "/norestart", "/qn" },
            "inf" or "inf-folder" => new[] { "/subdirs", "/force", "/reboot" },
            _ => Array.Empty<string>()
        };

        private bool HasToken(string token) =>
            ArgumentsBox.Text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(t => string.Equals(t, token, StringComparison.Ordinal));

        private void InsertToken(string token)
        {
            if (HasToken(token)) return;
            _updatingArgs = true;
            var text = ArgumentsBox.Text.TrimEnd();
            ArgumentsBox.Text = string.IsNullOrEmpty(text) ? token : $"{text} {token}";
            _updatingArgs = false;
            UpdatePreview();
        }

        private void RemoveToken(string token)
        {
            _updatingArgs = true;
            var kept = ArgumentsBox.Text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => !string.Equals(t, token, StringComparison.Ordinal));
            ArgumentsBox.Text = string.Join(" ", kept);
            _updatingArgs = false;
            UpdatePreview();
        }

        // --- Fallback master toggle ----------------------------------------------------------

        private void MasterFallback_Changed(object sender, RoutedEventArgs e)
        {
            var value = MasterFallbackCheck.IsChecked == true;
            foreach (var t in _targets)
                t.AllowTaskFallback = value;
        }

        // --- Copy-only toggle ----------------------------------------------------------------

        // Copy-only clears/disables every run-related control (entry point, run-as, arguments,
        // flags, verify, delete-after-run, fallback) since nothing is executed on the target.
        private void ApplyCopyOnlyState()
        {
            if (RunAsCombo == null || EntryPointCombo == null || RunButton == null
                || ArgumentsBox == null || FlagChips == null || VerifyHashCheck == null
                || DeleteAfterCheck == null || MasterFallbackCheck == null)
                return;

            var copyOnly = IsCopyOnly();
            var runEnabled = !copyOnly && !_isRunning;

            EntryPointCombo.IsEnabled = runEnabled;
            ArgumentsBox.IsEnabled = runEnabled;
            FlagChips.IsEnabled = runEnabled;
            VerifyHashCheck.IsEnabled = runEnabled;
            DeleteAfterCheck.IsEnabled = runEnabled;
            MasterFallbackCheck.IsEnabled = runEnabled;

            if (copyOnly)
            {
                EntryPointCombo.SelectedIndex = -1;
                RunButton.Content = "Copy";
            }
            else
            {
                if (EntryPointCombo.Items.Count > 0 && EntryPointCombo.SelectedIndex < 0)
                    EntryPointCombo.SelectedIndex = 0;
                RunButton.Content = "Run";
            }
        }

        // --- Run / cancel --------------------------------------------------------------------

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            if (string.IsNullOrEmpty(_sourcePath))
            {
                MessageBox.Show(this, "Drop or browse to a payload first.", "Push & Run",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var copyOnly = IsCopyOnly();

            if (!copyOnly && (EntryPointCombo.SelectedItem is not string || string.IsNullOrWhiteSpace(EntryPointCombo.SelectedItem as string)))
            {
                MessageBox.Show(this, "Choose the file to run.", "Push & Run",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var entryRel = (EntryPointCombo.SelectedItem as string) ?? string.Empty;
            var dest = DestinationBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(dest))
            {
                MessageBox.Show(this, "Enter a destination folder.", "Push & Run",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var entryFullLocal = _isFolder
                ? (string.IsNullOrEmpty(entryRel) ? _sourcePath : Path.Combine(_sourcePath.TrimEnd(Path.DirectorySeparatorChar), entryRel))
                : _sourcePath;

            // Resolve the launch method and finalize arguments (driver-folder needs a folder + /subdirs).
            var tag = (RunAsCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Auto";
            var launchKind = Enum.TryParse<PushRunLaunchKind>(tag, out var lk) ? lk : PushRunLaunchKind.Auto;
            var effectiveKind = GetEffectiveKindString();
            var argsText = ArgumentsBox.Text.Trim();

            if (!copyOnly && effectiveKind == "inf-folder")
            {
                if (!_isFolder)
                {
                    MessageBox.Show(this, "\"Driver (all .inf in folder)\" needs a folder payload.", "Push & Run",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (!HasToken("/subdirs")) argsText = (argsText + " /subdirs").Trim();
            }

            // Confirmation gate — this is remote code execution (or a pure copy in copy-only mode).
            var confirmMessage = copyOnly
                ? $"Copy {(_isFolder ? "folder" : "file")} \"{Path.GetFileName(_sourcePath.TrimEnd(Path.DirectorySeparatorChar))}\"\n\n" +
                  $"to {_targets.Count} computer(s) at {dest}?\n\nNothing will be run on the target."
                : $"{BuildPreviewCommand()}\n\n" +
                  $"on {_targets.Count} computer(s), copied to {dest}?\n\n" +
                  "The payload runs silently in the SYSTEM/service context.";
            var confirm = MessageBox.Show(this, confirmMessage,
                copyOnly ? "Confirm Copy" : "Confirm Push & Run",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            string? hash = null;
            if (!copyOnly && VerifyHashCheck.IsChecked == true)
            {
                try { hash = await Task.Run(() => ComputeSha256(entryFullLocal)); } catch { /* verify skipped */ }
            }

            var payload = new PushRunPayload
            {
                SourcePath = _sourcePath,
                IsFolder = _isFolder,
                EntryPointRelativePath = entryRel,
                Arguments = argsText,
                LaunchKind = launchKind,
                EntryPointSha256 = hash
            };
            var options = new PushRunOptions
            {
                DestinationFolder = dest,
                VerifyHash = !copyOnly && VerifyHashCheck.IsChecked == true && hash != null,
                DeleteAfterRun = !copyOnly && DeleteAfterCheck.IsChecked == true,
                AllowTaskFallbackByDefault = MasterFallbackCheck.IsChecked == true,
                MaxParallelism = ParseConcurrency(),
                TimeoutMs = ParseTimeoutMinutes() * 60000,
                CopyOnly = copyOnly
            };

            PayloadName = copyOnly
                ? Path.GetFileName(_sourcePath.TrimEnd(Path.DirectorySeparatorChar))
                : Path.GetFileName(entryFullLocal);

            SetRunningState(true);
            _cts = new CancellationTokenSource();

            foreach (var t in _targets)
            {
                t.Stage = PushRunStage.Pending;
                t.StatusDetail = string.Empty;
                t.ExitCode = null;
                t.CopyMethod = null;
                t.RunMethod = null;
            }

            var results = new System.Collections.Concurrent.ConcurrentBag<PushRunResult>();
            var completed = 0;
            var succeeded = 0;
            var total = _targets.Count;
            UpdateProgress(0, 0, total);

            try
            {
                var parallel = new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.MaxParallelism,
                    CancellationToken = _cts.Token
                };

                await Parallel.ForEachAsync(_targets, parallel, async (target, ct) =>
                {
                    target.StartTime = DateTime.Now;
                    var result = await _service.PushAndRunAsync(
                        target, payload, options, _credential, new DispatcherProgress(this, target), ct);
                    target.EndTime = DateTime.Now;
                    results.Add(result);

                    var done = Interlocked.Increment(ref completed);
                    var ok = result.Succeeded ? Interlocked.Increment(ref succeeded) : Volatile.Read(ref succeeded);
                    await Dispatcher.InvokeAsync(() => UpdateProgress(done, ok, total));
                });
            }
            catch (OperationCanceledException)
            {
                // Rows already reflect their last state; remaining show Pending.
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Push & Run", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Results = results.ToList();
                DidRun = true;
                ShowFailures();
                SetRunningState(false);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                var r = MessageBox.Show(this, "A run is in progress. Cancel and close?", "Push & Run",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
                _cts?.Cancel();
            }
            DialogResult = DidRun;
            Close();
        }

        private void SetRunningState(bool running)
        {
            _isRunning = running;
            RunButton.IsEnabled = !running;
            CancelButton.IsEnabled = running;
            DropZone.IsEnabled = !running;
            EntryPointCombo.IsEnabled = !running;
            RunAsCombo.IsEnabled = !running;
            FlagChips.IsEnabled = !running;
            ArgumentsBox.IsEnabled = !running;
            DestinationBox.IsEnabled = !running;
            TimeoutBox.IsEnabled = !running;
            ConcurrencyCombo.IsEnabled = !running;
            VerifyHashCheck.IsEnabled = !running;
            DeleteAfterCheck.IsEnabled = !running;
            MasterFallbackCheck.IsEnabled = !running;

            // Copy-only re-disables the run-related controls that the block above just re-enabled.
            if (!running) ApplyCopyOnlyState();
        }

        private void UpdateProgress(int completed, int succeeded, int total)
        {
            ProgressText.Text = $"{succeeded}/{total} succeeded" +
                                (completed < total ? $"  ·  {completed}/{total} done" : "");
        }

        private void ShowFailures()
        {
            var failures = Results.Where(r => !r.Succeeded).ToList();
            FailuresText.Text = failures.Count == 0
                ? string.Empty
                : "Failed: " + string.Join(", ", failures.Select(f => $"{f.ComputerName} ({f.Detail})"));
        }

        // Marshals service progress onto the UI thread and applies it to the bound row.
        private sealed class DispatcherProgress : IProgress<PushRunProgressUpdate>
        {
            private readonly PushRunWindow _owner;
            private readonly PushRunTarget _target;

            public DispatcherProgress(PushRunWindow owner, PushRunTarget target)
            {
                _owner = owner;
                _target = target;
            }

            public void Report(PushRunProgressUpdate u)
            {
                _owner.Dispatcher.BeginInvoke(() =>
                {
                    _target.Stage = u.Stage;
                    if (u.Detail != null) _target.StatusDetail = u.Detail;
                    if (u.ExitCode != null) _target.ExitCode = u.ExitCode;
                    if (u.CopyMethod != null) _target.CopyMethod = u.CopyMethod;
                    if (u.RunMethod != null) _target.RunMethod = u.RunMethod;
                });
            }
        }
    }
}
