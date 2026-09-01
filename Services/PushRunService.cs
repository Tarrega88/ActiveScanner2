using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ActiveScanner.Models;

namespace ActiveScanner.Services
{
    /// <summary>
    /// A single live status change reported while a target is being processed. Consumers apply
    /// this to the bound <see cref="PushRunTarget"/> row on the UI thread (via <see cref="IProgress{T}"/>).
    /// </summary>
    public record PushRunProgressUpdate(
        PushRunStage Stage,
        string? Detail = null,
        int? ExitCode = null,
        string? CopyMethod = null,
        string? RunMethod = null);

    /// <summary>
    /// Terminal outcome for one target, returned when <see cref="PushRunService.PushAndRunAsync"/>
    /// completes. Mirrors the last reported stage so the caller can build a batch summary.
    /// </summary>
    public record PushRunResult(
        string ComputerName,
        PushRunStage Stage,
        string Detail,
        int? ExitCode,
        string? CopyMethod,
        string? RunMethod)
    {
        public bool Succeeded => Stage == PushRunStage.Success;
    }

    /// <summary>
    /// Copies a payload (single file or folder tree) to remote machines and runs an entry-point
    /// silently. Copy uses the admin SMB share (<c>\\host\c$</c>) with a WinRM
    /// <c>Copy-Item -ToSession</c> fallback; the run uses WinRM with an optional scheduled-task
    /// (SYSTEM) fallback. All remote work is authenticated with the supplied admin credential.
    /// </summary>
    public class PushRunService
    {
        private readonly NetworkService _networkService;

        // Every structured line the generated script emits starts with this marker.
        private const string Marker = "PUSHRUN|";

        public PushRunService(NetworkService networkService)
        {
            _networkService = networkService;
        }

        /// <summary>
        /// Processes one target end-to-end: preflight → copy → optional verify → run → optional
        /// cleanup. Progress is reported as each stage begins/completes.
        /// </summary>
        public async Task<PushRunResult> PushAndRunAsync(
            PushRunTarget target,
            PushRunPayload payload,
            PushRunOptions options,
            NetworkCredential credential,
            IProgress<PushRunProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            void Report(PushRunProgressUpdate u) => progress?.Report(u);

            var stage = PushRunStage.Preflight;
            var detail = string.Empty;
            int? exitCode = null;
            string? copyMethod = null;
            string? runMethod = null;

            PushRunResult Finish() =>
                new(target.ComputerName, stage, detail, exitCode, copyMethod, runMethod);

            try
            {
                // --- Preflight: ping, then WinRM (required) and SMB (nice-to-have) reachability ---
                Report(new PushRunProgressUpdate(PushRunStage.Preflight));

                var ping = await _networkService.PingAsync(target.Address, 5000, cancellationToken);
                if (!ping.Success)
                {
                    stage = PushRunStage.Offline;
                    detail = "Ping failed — machine appears offline";
                    Report(new PushRunProgressUpdate(stage, detail));
                    return Finish();
                }

                var winrmOpen = (await _networkService.TestPortAsync(target.Address, 5985, 3000, cancellationToken)).Success;
                if (!winrmOpen)
                {
                    stage = PushRunStage.Failed;
                    detail = "WinRM (5985) unreachable";
                    Report(new PushRunProgressUpdate(stage, detail));
                    return Finish();
                }

                var smbOpen = (await _networkService.TestPortAsync(target.Address, 445, 3000, cancellationToken)).Success;
                var smbEligible = smbOpen &&
                    options.DestinationFolder.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase);

                // --- Run the orchestration script and parse its structured output live ---
                var scriptPath = WriteScript(target, payload, options, credential, smbEligible);
                try
                {
                    await RunScriptAsync(scriptPath, options.TimeoutMs, cancellationToken, line =>
                    {
                        if (!line.StartsWith(Marker, StringComparison.Ordinal))
                            return;

                        var parts = line.Substring(Marker.Length).Split('|');
                        switch (parts[0])
                        {
                            case "STAGE" when parts.Length >= 2:
                                stage = parts[1] switch
                                {
                                    "COPYING" => PushRunStage.Copying,
                                    "VERIFYING" => PushRunStage.Verifying,
                                    "RUNNING" => PushRunStage.Running,
                                    _ => stage
                                };
                                Report(new PushRunProgressUpdate(stage));
                                break;

                            case "COPY" when parts.Length >= 2:
                                if (parts[1] == "OK")
                                {
                                    copyMethod = parts.Length >= 3 ? parts[2] : null;
                                    Report(new PushRunProgressUpdate(PushRunStage.Copying,
                                        $"copied via {copyMethod}", CopyMethod: copyMethod));
                                }
                                else
                                {
                                    stage = PushRunStage.Failed;
                                    detail = "Copy failed: " + (parts.Length >= 3 ? parts[2] : "unknown");
                                    Report(new PushRunProgressUpdate(stage, detail));
                                }
                                break;

                            case "VERIFY" when parts.Length >= 2:
                                if (parts[1] != "OK")
                                {
                                    stage = PushRunStage.Failed;
                                    detail = "Hash mismatch: " + (parts.Length >= 3 ? parts[2] : "unknown");
                                    Report(new PushRunProgressUpdate(stage, detail));
                                }
                                break;

                            case "COPYONLY":
                                stage = PushRunStage.Success;
                                detail = "Copied (no run)";
                                Report(new PushRunProgressUpdate(stage, detail));
                                break;

                            case "RUN" when parts.Length >= 2:
                                if (parts[1] == "OK")
                                {
                                    // PUSHRUN|RUN|OK|<exit>|<method>
                                    exitCode = parts.Length >= 3 && int.TryParse(parts[2], out var c) ? c : null;
                                    runMethod = parts.Length >= 4 ? parts[3] : null;
                                    // 0 = success, 3010 = success but reboot required.
                                    var ok = exitCode is 0 or 3010;
                                    stage = ok ? PushRunStage.Success : PushRunStage.Failed;
                                    detail = exitCode switch
                                    {
                                        0 => "Exit 0",
                                        3010 => "Exit 3010 (reboot required)",
                                        null => "Ran (no exit code)",
                                        _ => $"Exit {exitCode}"
                                    };
                                    Report(new PushRunProgressUpdate(stage, detail, exitCode, RunMethod: runMethod));
                                }
                                else
                                {
                                    stage = PushRunStage.Failed;
                                    detail = "Run failed: " + (parts.Length >= 3 ? parts[2] : "unknown");
                                    Report(new PushRunProgressUpdate(stage, detail));
                                }
                                break;
                        }
                    });
                }
                finally
                {
                    try { if (File.Exists(scriptPath)) File.Delete(scriptPath); } catch { /* best effort */ }
                }

                // If the script produced no terminal RUN/failure marker, treat it as a failure.
                if (stage is PushRunStage.Preflight or PushRunStage.Copying
                    or PushRunStage.Verifying or PushRunStage.Running)
                {
                    stage = PushRunStage.Failed;
                    if (string.IsNullOrEmpty(detail))
                        detail = "No result returned from target";
                    Report(new PushRunProgressUpdate(stage, detail));
                }

                return Finish();
            }
            catch (OperationCanceledException)
            {
                stage = PushRunStage.Cancelled;
                detail = "Cancelled";
                Report(new PushRunProgressUpdate(stage, detail));
                return Finish();
            }
            catch (Exception ex)
            {
                stage = PushRunStage.Failed;
                detail = ex.Message;
                Report(new PushRunProgressUpdate(stage, detail));
                return Finish();
            }
        }

        /// <summary>
        /// Runs a generated script via powershell.exe and invokes <paramref name="onLine"/> for
        /// each stdout line as it arrives, so the caller can update live status.
        /// </summary>
        private static async Task RunScriptAsync(
            string scriptPath, int timeoutMs, CancellationToken cancellationToken, Action<string> onLine)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);

            process.Start();

            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(cts.Token)) != null)
            {
                onLine(line);
            }

            await process.WaitForExitAsync(cts.Token);
            _ = await errorTask; // drained to avoid a full stderr pipe stalling the child
        }

        /// <summary>Writes the per-machine orchestration script to a temp .ps1 and returns its path.</summary>
        private static string WriteScript(
            PushRunTarget target, PushRunPayload payload, PushRunOptions options,
            NetworkCredential credential, bool smbEligible)
        {
            var path = Path.Combine(Path.GetTempPath(), $"activescanner_pushrun_{Guid.NewGuid():N}.ps1");
            File.WriteAllText(path, BuildScript(target, payload, options, credential, smbEligible));
            return path;
        }

        /// <summary>Builds the PowerShell orchestration script for a single target.</summary>
        private static string BuildScript(
            PushRunTarget target, PushRunPayload payload, PushRunOptions options,
            NetworkCredential credential, bool smbEligible)
        {
            // Top-level names we place on the target, used for optional cleanup afterwards.
            var topNames = payload.IsFolder
                ? Directory.EnumerateFileSystemEntries(payload.SourcePath).Select(Path.GetFileName)
                : new[] { Path.GetFileName(payload.SourcePath) };
            var topNamesLiteral = "@(" + string.Join(",", topNames.Select(n => Q(n ?? string.Empty))) + ")";

            // Username in the form WinRM/Kerberos accepts (UPN for FQDN domains, else NetBIOS).
            string username;
            if (string.IsNullOrEmpty(credential.Domain))
                username = credential.UserName;
            else if (credential.Domain.Contains('.'))
                username = $"{credential.UserName}@{credential.Domain}";
            else
                username = $"{credential.Domain}\\{credential.UserName}";
            var encodedPwd = Convert.ToBase64String(Encoding.Unicode.GetBytes(credential.Password));

            var allowTask = target.AllowTaskFallback;

            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine($"$computer = {Q(target.Address)}");
            sb.AppendLine($"$user = {Q(username)}");
            sb.AppendLine($"$encodedPwd = {Q(encodedPwd)}");
            sb.AppendLine("$pwdBytes = [Convert]::FromBase64String($encodedPwd)");
            sb.AppendLine("$plain = [Text.Encoding]::Unicode.GetString($pwdBytes)");
            sb.AppendLine("$secPwd = New-Object Security.SecureString");
            sb.AppendLine("$plain.ToCharArray() | ForEach-Object { $secPwd.AppendChar($_) }");
            sb.AppendLine("$cred = New-Object Management.Automation.PSCredential($user, $secPwd)");
            sb.AppendLine($"$src = {Q(payload.SourcePath)}");
            sb.AppendLine($"$isFolder = {(payload.IsFolder ? "$true" : "$false")}");
            sb.AppendLine($"$dest = {Q(options.DestinationFolder)}");
            sb.AppendLine($"$entryRel = {Q(payload.EntryPointRelativePath)}");
            sb.AppendLine("$remoteEntry = Join-Path $dest $entryRel");
            sb.AppendLine($"$argline = {Q(payload.Arguments ?? string.Empty)}");
            sb.AppendLine($"$runKind = {Q(ResolveKind(payload))}");
            // Driver-folder mode targets every .inf in the destination; others target the entry file.
            sb.AppendLine("$runTarget = if ($runKind -eq 'inf-folder') { Join-Path $dest '*.inf' } else { $remoteEntry }");
            sb.AppendLine($"$topNames = {topNamesLiteral}");
            sb.AppendLine($"$smbEligible = {(smbEligible ? "$true" : "$false")}");
            sb.AppendLine($"$verify = {(options.VerifyHash && !string.IsNullOrEmpty(payload.EntryPointSha256) ? "$true" : "$false")}");
            sb.AppendLine($"$expectedHash = {Q(payload.EntryPointSha256 ?? string.Empty)}");
            sb.AppendLine($"$deleteAfter = {(options.DeleteAfterRun ? "$true" : "$false")}");
            sb.AppendLine($"$allowTask = {(allowTask ? "$true" : "$false")}");
            sb.AppendLine($"$copyOnly = {(options.CopyOnly ? "$true" : "$false")}");
            sb.AppendLine();

            // --- COPY ---
            sb.AppendLine("Write-Output 'PUSHRUN|STAGE|COPYING'");
            sb.AppendLine("$copyOk = $false; $copyMethod = ''");
            sb.AppendLine("if ($smbEligible) {");
            sb.AppendLine("  try {");
            sb.AppendLine("    $unc = \"\\\\$computer\\c$\"");
            sb.AppendLine("    New-PSDrive -Name PRPUSH -PSProvider FileSystem -Root $unc -Credential $cred -ErrorAction Stop | Out-Null");
            sb.AppendLine("    $destOnDrive = 'PRPUSH:\\' + $dest.Substring(3)"); // strip 'C:\'
            sb.AppendLine("    New-Item -ItemType Directory -Path $destOnDrive -Force | Out-Null");
            sb.AppendLine("    if ($isFolder) { Copy-Item -Path (Join-Path $src '*') -Destination $destOnDrive -Recurse -Force }");
            sb.AppendLine("    else { Copy-Item -Path $src -Destination $destOnDrive -Force }");
            sb.AppendLine("    $copyOk = $true; $copyMethod = 'SMB'");
            sb.AppendLine("  } catch { }");
            sb.AppendLine("  finally { if (Get-PSDrive -Name PRPUSH -ErrorAction SilentlyContinue) { Remove-PSDrive -Name PRPUSH -Force } }");
            sb.AppendLine("}");
            sb.AppendLine("if (-not $copyOk) {");
            sb.AppendLine("  try {");
            sb.AppendLine("    $session = New-PSSession -ComputerName $computer -Credential $cred -ErrorAction Stop");
            sb.AppendLine("    Invoke-Command -Session $session -ScriptBlock { param($d) New-Item -ItemType Directory -Path $d -Force | Out-Null } -ArgumentList $dest");
            sb.AppendLine("    if ($isFolder) {");
            sb.AppendLine("      Get-ChildItem -Path $src -Recurse -File | ForEach-Object {");
            sb.AppendLine("        $rel = $_.FullName.Substring($src.Length).TrimStart('\\')");
            sb.AppendLine("        $tgt = Join-Path $dest $rel");
            sb.AppendLine("        Invoke-Command -Session $session -ScriptBlock { param($p) $dir = Split-Path $p -Parent; if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null } } -ArgumentList $tgt");
            sb.AppendLine("        Copy-Item -Path $_.FullName -Destination $tgt -ToSession $session -Force");
            sb.AppendLine("      }");
            sb.AppendLine("    } else { Copy-Item -Path $src -Destination $dest -ToSession $session -Force }");
            sb.AppendLine("    Remove-PSSession $session");
            sb.AppendLine("    $copyOk = $true; $copyMethod = 'WinRM'");
            sb.AppendLine("  } catch { Write-Output \"PUSHRUN|COPY|FAIL|$($_.Exception.Message -replace '[\\r\\n|]',' ')\"; exit 1 }");
            sb.AppendLine("}");
            sb.AppendLine("Write-Output \"PUSHRUN|COPY|OK|$copyMethod\"");
            sb.AppendLine();

            // --- VERIFY (optional) ---
            sb.AppendLine("if ($verify) {");
            sb.AppendLine("  Write-Output 'PUSHRUN|STAGE|VERIFYING'");
            sb.AppendLine("  try {");
            sb.AppendLine("    $remoteHash = Invoke-Command -ComputerName $computer -Credential $cred -ScriptBlock { param($p) (Get-FileHash -Algorithm SHA256 -Path $p).Hash } -ArgumentList $remoteEntry");
            sb.AppendLine("    if ($remoteHash -ne $expectedHash) { Write-Output \"PUSHRUN|VERIFY|FAIL|expected $expectedHash got $remoteHash\"; exit 1 }");
            sb.AppendLine("    Write-Output 'PUSHRUN|VERIFY|OK'");
            sb.AppendLine("  } catch { Write-Output \"PUSHRUN|VERIFY|FAIL|$($_.Exception.Message -replace '[\\r\\n|]',' ')\"; exit 1 }");
            sb.AppendLine("}");
            sb.AppendLine();

            // --- COPY-ONLY short-circuit: nothing is executed, so stop after a successful copy. ---
            sb.AppendLine("if ($copyOnly) { Write-Output 'PUSHRUN|COPYONLY|OK'; exit 0 }");
            sb.AppendLine();

            // --- RUN (WinRM primary) ---
            sb.AppendLine("Write-Output 'PUSHRUN|STAGE|RUNNING'");
            sb.AppendLine(RunBlockDefinition());
            sb.AppendLine("$ran = $false; $exit = $null; $runMethod = ''");
            sb.AppendLine("try {");
            sb.AppendLine("  $exit = Invoke-Command -ComputerName $computer -Credential $cred -ScriptBlock $runBlock -ArgumentList $runTarget, $argline, $runKind");
            sb.AppendLine("  $ran = $true; $runMethod = 'WinRM'");
            sb.AppendLine("} catch {");
            sb.AppendLine("  if (-not $allowTask) { Write-Output \"PUSHRUN|RUN|FAIL|$($_.Exception.Message -replace '[\\r\\n|]',' ')\" }");
            sb.AppendLine("}");

            // --- RUN (scheduled-task fallback) ---
            sb.AppendLine("if ((-not $ran) -and $allowTask) {");
            sb.AppendLine("  try {");
            sb.AppendLine(TaskBlockDefinition());
            sb.AppendLine("    $exit = Invoke-Command -ComputerName $computer -Credential $cred -ScriptBlock $taskBlock -ArgumentList $runTarget, $argline, $runKind");
            sb.AppendLine("    $ran = $true; $runMethod = 'ScheduledTask'");
            sb.AppendLine("  } catch { Write-Output \"PUSHRUN|RUN|FAIL|$($_.Exception.Message -replace '[\\r\\n|]',' ')\" }");
            sb.AppendLine("}");
            sb.AppendLine("if ($ran) {");
            sb.AppendLine("  if ($null -eq $exit) { $exit = 0 }");
            sb.AppendLine("  Write-Output \"PUSHRUN|RUN|OK|$exit|$runMethod\"");
            sb.AppendLine("}");
            sb.AppendLine();

            // --- CLEANUP (optional) ---
            sb.AppendLine("if ($deleteAfter -and $ran) {");
            sb.AppendLine("  try {");
            sb.AppendLine("    Invoke-Command -ComputerName $computer -Credential $cred -ScriptBlock { param($d,$names) foreach ($n in $names) { $p = Join-Path $d $n; if (Test-Path $p) { Remove-Item -Path $p -Recurse -Force -ErrorAction SilentlyContinue } } } -ArgumentList $dest, $topNames");
            sb.AppendLine("    Write-Output 'PUSHRUN|CLEANUP|OK'");
            sb.AppendLine("  } catch { Write-Output \"PUSHRUN|CLEANUP|FAIL|$($_.Exception.Message -replace '[\\r\\n|]',' ')\" }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>Remote run scriptblock: launches the entry point by resolved kind and returns its exit code.</summary>
        private static string RunBlockDefinition() =>
            "$runBlock = {\n" +
            "  param($path, $argline, $kind)\n" +
            "  switch ($kind) {\n" +
            "    'msi' {\n" +
            "      $a = \"/i `\"$path`\"\"; if ($argline) { $a += \" $argline\" }\n" +
            "      (Start-Process -FilePath 'msiexec.exe' -ArgumentList $a -Wait -PassThru).ExitCode\n" +
            "    }\n" +
            "    'ps1' {\n" +
            "      $a = @('-NoProfile','-ExecutionPolicy','Bypass','-File', $path); if ($argline) { $a += $argline }\n" +
            "      (Start-Process -FilePath 'powershell.exe' -ArgumentList $a -Wait -PassThru).ExitCode\n" +
            "    }\n" +
            "    'bat' {\n" +
            "      $a = \"/c `\"$path`\"\"; if ($argline) { $a += \" $argline\" }\n" +
            "      (Start-Process -FilePath 'cmd.exe' -ArgumentList $a -Wait -PassThru).ExitCode\n" +
            "    }\n" +
            "    { $_ -in 'inf','inf-folder' } {\n" +
            "      $a = \"/add-driver `\"$path`\" /install\"; if ($argline) { $a += \" $argline\" }\n" +
            "      (Start-Process -FilePath 'pnputil.exe' -ArgumentList $a -Wait -PassThru).ExitCode\n" +
            "    }\n" +
            "    default {\n" +
            "      if ($argline) { (Start-Process -FilePath $path -ArgumentList $argline -Wait -PassThru).ExitCode }\n" +
            "      else { (Start-Process -FilePath $path -Wait -PassThru).ExitCode }\n" +
            "    }\n" +
            "  }\n" +
            "}";

        /// <summary>Scheduled-task fallback scriptblock: runs the entry point as SYSTEM and returns its result.</summary>
        private static string TaskBlockDefinition() =>
            "    $taskBlock = {\n" +
            "      param($path, $argline, $kind)\n" +
            "      $taskName = 'ActiveScanner_PushRun'\n" +
            "      switch ($kind) {\n" +
            "        'msi' { $exe = 'msiexec.exe'; $ta = \"/i `\"$path`\" $argline\" }\n" +
            "        'ps1' { $exe = 'powershell.exe'; $ta = \"-NoProfile -ExecutionPolicy Bypass -File `\"$path`\" $argline\" }\n" +
            "        'bat' { $exe = 'cmd.exe'; $ta = \"/c `\"$path`\" $argline\" }\n" +
            "        { $_ -in 'inf','inf-folder' } { $exe = 'pnputil.exe'; $ta = \"/add-driver `\"$path`\" /install $argline\" }\n" +
            "        default { $exe = $path; $ta = $argline }\n" +
            "      }\n" +
            "      $action = New-ScheduledTaskAction -Execute $exe -Argument $ta\n" +
            "      $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest\n" +
            "      Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Force | Out-Null\n" +
            "      Start-ScheduledTask -TaskName $taskName\n" +
            "      $deadline = (Get-Date).AddMinutes(15)\n" +
            "      do { Start-Sleep -Seconds 2; $ti = Get-ScheduledTaskInfo -TaskName $taskName } while ($ti.LastTaskResult -eq 267009 -and (Get-Date) -lt $deadline)\n" +
            "      $code = $ti.LastTaskResult\n" +
            "      Unregister-ScheduledTask -TaskName $taskName -Confirm:$false\n" +
            "      $code\n" +
            "    }";

        /// <summary>Resolves the effective launch kind string used by the run scriptblocks.</summary>
        private static string ResolveKind(PushRunPayload payload)
        {
            switch (payload.LaunchKind)
            {
                case PushRunLaunchKind.Executable: return "exe";
                case PushRunLaunchKind.Msi: return "msi";
                case PushRunLaunchKind.PowerShell: return "ps1";
                case PushRunLaunchKind.Batch: return "bat";
                case PushRunLaunchKind.DriverInf: return "inf";
                case PushRunLaunchKind.DriverFolder: return "inf-folder";
                default:
                    var ext = Path.GetExtension(payload.EntryPointRelativePath).ToLowerInvariant();
                    return ext switch
                    {
                        ".msi" => "msi",
                        ".ps1" => "ps1",
                        ".bat" or ".cmd" => "bat",
                        ".inf" => "inf",
                        _ => "exe"
                    };
            }
        }

        /// <summary>Single-quotes a value for safe literal use in PowerShell.</summary>
        private static string Q(string value) => "'" + value.Replace("'", "''") + "'";
    }
}
