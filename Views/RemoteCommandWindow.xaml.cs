using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ActiveScanner.Models;
using ActiveScanner.Services;

namespace ActiveScanner.Views
{
    /// <summary>
    /// Modal for the Run Command feature: type one command, run it on every selected machine over
    /// WinRM in parallel, and show each machine's status and captured output. A single-machine
    /// selection is just a broadcast of one.
    /// </summary>
    public partial class RemoteCommandWindow : Window
    {
        private readonly NetworkService _networkService;
        private readonly NetworkCredential _credential;
        private readonly ObservableCollection<RemoteCommandTarget> _targets = new();

        private CancellationTokenSource? _cts;
        private bool _isRunning;

        /// <summary>Per-target outcomes, available to the caller after the dialog closes.</summary>
        public IReadOnlyList<RemoteCommandResult> Results { get; private set; } = Array.Empty<RemoteCommandResult>();

        /// <summary>True once a batch has actually been executed.</summary>
        public bool DidRun { get; private set; }

        /// <summary>The command that was run (for summaries/history).</summary>
        public string CommandText { get; private set; } = string.Empty;

        public RemoteCommandWindow(NetworkService networkService, NetworkCredential credential,
            IEnumerable<(string Name, string Address)> targets)
        {
            InitializeComponent();
            _networkService = networkService;
            _credential = credential;

            foreach (var (name, address) in targets)
            {
                _targets.Add(new RemoteCommandTarget
                {
                    ComputerName = name,
                    Address = string.IsNullOrWhiteSpace(address) ? name : address
                });
            }
            MachinesGrid.ItemsSource = _targets;
            if (_targets.Count > 0) MachinesGrid.SelectedIndex = 0;
        }

        // Clamps the timeout field to a sane 1–120 minute range; defaults to 2 on bad input.
        private int ParseTimeoutMinutes()
        {
            if (int.TryParse(TimeoutBox.Text.Trim(), out var m) && m > 0)
                return Math.Min(m, 120);
            return 2;
        }

        // Reads the concurrency dropdown; defaults to 10, capped at 20.
        private int ParseConcurrency()
        {
            var text = (ConcurrencyCombo.SelectedItem as ComboBoxItem)?.Content as string;
            if (int.TryParse(text, out var n) && n > 0)
                return Math.Min(n, 20);
            return 10;
        }

        private RemoteShell SelectedShell() =>
            (ShellCombo.SelectedItem as ComboBoxItem)?.Tag as string == "Cmd"
                ? RemoteShell.Cmd
                : RemoteShell.PowerShell;

        private void MachinesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MachinesGrid.SelectedItem is RemoteCommandTarget t)
            {
                DetailsHeader.Text = $"Output — {t.ComputerName}";
                OutputBox.Text = string.IsNullOrEmpty(t.Output) ? "(no output yet)" : t.Output;
            }
            else
            {
                DetailsHeader.Text = "Output";
                OutputBox.Text = "Select a machine to see its output.";
            }
        }

        // Refreshes the details pane if the currently selected row is the one that changed.
        private void RefreshSelectedOutput(RemoteCommandTarget target)
        {
            if (ReferenceEquals(MachinesGrid.SelectedItem, target))
            {
                DetailsHeader.Text = $"Output — {target.ComputerName}";
                OutputBox.Text = string.IsNullOrEmpty(target.Output) ? "(no output yet)" : target.Output;
            }
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            var command = CommandBox.Text.Trim();
            if (string.IsNullOrEmpty(command))
            {
                MessageBox.Show(this, "Enter a command to run.", "Terminal",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var shell = SelectedShell();
            var shellName = shell == RemoteShell.Cmd ? "Command Prompt" : "PowerShell";

            var confirm = MessageBox.Show(this,
                $"Run this {shellName} command on {_targets.Count} computer(s)?\n\n{command}\n\n" +
                "It runs remotely with your admin credential.",
                "Confirm Terminal Command", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            CommandText = command;

            var options = new
            {
                TimeoutMs = ParseTimeoutMinutes() * 60000,
                MaxParallelism = ParseConcurrency()
            };

            foreach (var t in _targets)
            {
                t.Stage = RemoteCommandStage.Pending;
                t.StatusDetail = string.Empty;
                t.Output = string.Empty;
            }

            SetRunningState(true);
            _cts = new CancellationTokenSource();

            var results = new System.Collections.Concurrent.ConcurrentBag<RemoteCommandResult>();
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
                    await Dispatcher.InvokeAsync(() => target.Stage = RemoteCommandStage.Running);

                    var result = new RemoteCommandResult
                    {
                        TargetName = target.ComputerName,
                        Command = command,
                        StartTime = target.StartTime.Value
                    };

                    try
                    {
                        var ping = await _networkService.PingAsync(target.Address, 5000, ct);
                        if (!ping.Success)
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                target.Stage = RemoteCommandStage.Offline;
                                target.StatusDetail = "Offline";
                                target.Output = "Ping failed — machine appears offline.";
                                RefreshSelectedOutput(target);
                            });
                            result.Success = false;
                            result.Status = "Offline";
                            result.ErrorMessage = "Ping failed — machine appears offline";
                        }
                        else
                        {
                            var (ok, output, error) = await _networkService.ExecuteCustomCommandAsync(
                                target.Address, command, shell, _credential, options.TimeoutMs, ct);

                            var duration = (DateTime.Now - target.StartTime.Value).TotalSeconds;
                            await Dispatcher.InvokeAsync(() =>
                            {
                                target.Stage = ok ? RemoteCommandStage.Success : RemoteCommandStage.Failed;
                                target.StatusDetail = ok ? $"Done ({duration:F1}s)" : "Failed";
                                target.Output = ok ? (output ?? string.Empty) : (error ?? "Command failed.");
                                RefreshSelectedOutput(target);
                            });
                            result.Success = ok;
                            result.Status = ok ? "Success" : "Failed";
                            result.Output = output;
                            result.ErrorMessage = error;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            target.Stage = RemoteCommandStage.Cancelled;
                            target.StatusDetail = "Cancelled";
                            RefreshSelectedOutput(target);
                        });
                        result.Success = false;
                        result.Status = "Cancelled";
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            target.Stage = RemoteCommandStage.Failed;
                            target.StatusDetail = "Failed";
                            target.Output = ex.Message;
                            RefreshSelectedOutput(target);
                        });
                        result.Success = false;
                        result.Status = "Failed";
                        result.ErrorMessage = ex.Message;
                    }

                    target.EndTime = DateTime.Now;
                    result.EndTime = target.EndTime;
                    results.Add(result);

                    var done = Interlocked.Increment(ref completed);
                    var ok2 = result.Success ? Interlocked.Increment(ref succeeded) : Volatile.Read(ref succeeded);
                    await Dispatcher.InvokeAsync(() => UpdateProgress(done, ok2, total));
                });
            }
            catch (OperationCanceledException)
            {
                // Rows already reflect their last state; remaining show Pending.
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Terminal", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Results = results.ToList();
                DidRun = true;
                SetRunningState(false);
            }
        }

        private void UpdateProgress(int done, int ok, int total)
        {
            ProgressText.Text = $"{done}/{total} done";
            var failed = done - ok;
            FailuresText.Text = failed > 0 ? $"{failed} failed" : string.Empty;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                var r = MessageBox.Show(this, "A run is in progress. Cancel and close?", "Terminal",
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
            CommandBox.IsEnabled = !running;
            ShellCombo.IsEnabled = !running;
            TimeoutBox.IsEnabled = !running;
            ConcurrencyCombo.IsEnabled = !running;
        }
    }
}
