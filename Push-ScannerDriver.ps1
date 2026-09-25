<#
.SYNOPSIS
  Interactive, menu-driven scanner driver push (no Active Scanner required).

.DESCRIPTION
  Supports:
    1. Kodak Alaris driver packages (InstallSoftware_*.exe, WinZip self-extractor)
       - unpacked locally, [SILENT] mode enabled in (or written to) drivers\setup.ini, runs drivers\setup.exe /S
    2. Fujitsu PaperStream IP packages (PSIP*.exe, PFU wrapper)
       - unpacked locally with /X, runs the inner driver MSI with Fujitsu's own msiexec arguments
    3. Any MSI (msiexec /qn /norestart)

  Remote work uses the account running this script (must be admin on the targets).
  Files are copied over the admin share (\\host\C$), falling back to WinRM; installs run over WinRM.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Push-ScannerDriver.ps1
#>

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

# ---------------------------------------------------------------- prompts

function Write-Title([string]$Text) {
    Write-Host ''
    Write-Host "== $Text ==" -ForegroundColor Cyan
}

function Read-Choice([string]$Prompt, [string[]]$Options, [int]$Default = 0) {
    while ($true) {
        Write-Host ''
        Write-Host $Prompt -ForegroundColor Yellow
        for ($i = 0; $i -lt $Options.Count; $i++) {
            $mark = if ($Default -eq $i + 1) { '  (default)' } else { '' }
            Write-Host ("  [{0}] {1}{2}" -f ($i + 1), $Options[$i], $mark)
        }
        $raw = Read-Host 'Choose a number'
        if ([string]::IsNullOrWhiteSpace($raw) -and $Default -gt 0) { return $Default }
        $n = 0
        if ([int]::TryParse($raw.Trim(), [ref]$n) -and $n -ge 1 -and $n -le $Options.Count) { return $n }
        Write-Host '  Please enter one of the numbers shown.' -ForegroundColor Red
    }
}

function Read-Text([string]$Prompt, [string]$Default = '') {
    $suffix = if ($Default) { " [$Default]" } else { '' }
    $raw = Read-Host "$Prompt$suffix"
    if ([string]::IsNullOrWhiteSpace($raw)) { return $Default }
    return $raw.Trim().Trim('"')
}

# ---------------------------------------------------------------- detection

function Get-InstallerType([string]$Path) {
    $ext = [IO.Path]::GetExtension($Path).ToLowerInvariant()
    if ($ext -eq '.msi') { return 'msi' }
    if ($ext -ne '.exe') { return 'unknown' }

    # PFU wrapper identifies itself in its (partly UTF-16) resources near the start of the file.
    $fs = [IO.File]::OpenRead($Path)
    try {
        $len = [int][Math]::Min(4MB, $fs.Length)
        $buf = New-Object byte[] $len
        [void]$fs.Read($buf, 0, $len)
        # A ZIP's file list (central directory) sits at the end of the file.
        $tailLen = [int][Math]::Min(2MB, $fs.Length)
        $tail = New-Object byte[] $tailLen
        [void]$fs.Seek(-$tailLen, [IO.SeekOrigin]::End)
        [void]$fs.Read($tail, 0, $tailLen)
    } finally { $fs.Dispose() }
    $texts = @(
        [Text.Encoding]::ASCII.GetString($buf),
        [Text.Encoding]::Unicode.GetString($buf),
        [Text.Encoding]::Unicode.GetString($buf, 1, $len - 1)
    )
    foreach ($t in $texts) {
        if ($t.Contains('PsipUpdater.exe') -or $t.Contains('PaperStream IP **** Make Updater')) { return 'fujitsu' }
    }

    if ([Text.Encoding]::ASCII.GetString($tail) -match 'drivers/setup\.(ini|txt)') { return 'kodak' }
    return 'unknown'
}

# ---------------------------------------------------------------- local preparation

$Latin1 = [Text.Encoding]::GetEncoding(28591)

function New-WorkFolder([string]$Name) {
    $work = Join-Path $env:TEMP "DriverPush\$Name"
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    return $work
}

function Initialize-Kodak([string]$Exe) {
    $work = New-WorkFolder ([IO.Path]::GetFileNameWithoutExtension($Exe))
    Write-Host "Unpacking to $work ..."
    try {
        [IO.Compression.ZipFile]::ExtractToDirectory($Exe, $work)
    } catch {
        # Older .NET can refuse some self-extractors; the built-in tar.exe handles them.
        Remove-Item (Join-Path $work '*') -Recurse -Force -ErrorAction SilentlyContinue
        & tar.exe -xf $Exe -C $work
        if ($LASTEXITCODE -ne 0) { throw "Could not unpack $Exe" }
    }

    foreach ($ini in Get-ChildItem $work -Recurse -Filter setup.ini) {
        $text = [IO.File]::ReadAllText($ini.FullName, $Latin1)
        if ($text -notmatch 'Kodak Alaris' -or $text -notmatch ';\[SILENT\]') { continue }
        $setup = Join-Path $ini.DirectoryName 'setup.exe'
        if (-not (Test-Path $setup)) { continue }

        # Kodak ships its [SILENT] keys commented out; enabling them removes every prompt.
        $pattern = '(?im)^;(?=\[SILENT\]|(?:SKIPTWAINIFNODOTNET|UPDATE|WELCOME|PLEASEWAIT|FINISH|REPORTERROR|TEMPDIR)=)'
        [IO.File]::WriteAllText($ini.FullName, ([regex]::Replace($text, $pattern, '')), $Latin1)

        return [pscustomobject]@{
            Source    = $work
            IsFolder  = $true
            EntryRel  = $setup.Substring($work.Length).TrimStart('\')
            Kind      = 'exe'
            Arguments = '/S'
            Label     = 'Kodak Alaris'
        }
    }

    # Newer Kodak packages (v8+) drop the setup.ini template; setup.txt still names the publisher.
    foreach ($txt in Get-ChildItem $work -Recurse -Filter setup.txt) {
        $setup = Join-Path $txt.DirectoryName 'setup.exe'
        $ini = Join-Path $txt.DirectoryName 'setup.ini'
        if (-not (Test-Path $setup) -or (Test-Path $ini)) { continue }
        if ([IO.File]::ReadAllText($txt.FullName, $Latin1) -notmatch 'Kodak Alaris') { continue }

        $silent = "[SILENT]`r`nSKIPTWAINIFNODOTNET=1`r`nUPDATE=1`r`nWELCOME=1`r`nPLEASEWAIT=1`r`nFINISH=1`r`nREPORTERROR=1`r`nTEMPDIR=*`r`n"
        [IO.File]::WriteAllText($ini, $silent, $Latin1)

        return [pscustomobject]@{
            Source    = $work
            IsFolder  = $true
            EntryRel  = $setup.Substring($work.Length).TrimStart('\')
            Kind      = 'exe'
            Arguments = '/S'
            Label     = 'Kodak Alaris'
        }
    }
    throw 'This package does not have the Kodak Alaris layout (drivers\setup.exe with setup.ini or setup.txt).'
}

function Initialize-Fujitsu([string]$Exe) {
    $work = New-WorkFolder ([IO.Path]::GetFileNameWithoutExtension($Exe))
    $copy = Join-Path $work ([IO.Path]::GetFileName($Exe))
    Copy-Item $Exe $copy

    # Empty folder = no overwrite prompts. /H stops the wrapper hiding the foreground window.
    Write-Host "Unpacking to $work (this can take a minute) ..."
    $p = Start-Process -FilePath $copy -ArgumentList '/X', '/H' -WorkingDirectory $work -WindowStyle Hidden -PassThru
    if (-not $p.WaitForExit(600000)) {
        try { $p.Kill() } catch { }
        throw 'Unpacking timed out after 10 minutes.'
    }
    if ($p.ExitCode -ne 0) { throw "Unpacker exited with code $($p.ExitCode)." }
    Remove-Item $copy -Force

    $setupIni = Get-ChildItem $work -Recurse -Filter fi-setup.ini | Select-Object -First 1
    if (-not $setupIni) { throw 'fi-setup.ini not found after unpacking.' }
    $root = $setupIni.DirectoryName

    # Setup.exe just runs the msiexec command from the first component INI in fi-setup.ini.
    $msi = $null; $extra = @()
    $instLine = Get-Content $setupIni.FullName | Where-Object { $_ -match '^\s*inst\d+\s*=\s*(.+?)\s*$' } | Select-Object -First 1
    if ($instLine -and $instLine -match '^\s*inst\d+\s*=\s*(.+?)\s*$') {
        $compIni = $Matches[1].Replace('%OWNDIR%', $root)
        if (Test-Path $compIni) {
            $para = Get-Content $compIni | Where-Object { $_ -match '^\s*SetupPara01\s*=.*\\([^"\\]+\.msi)"\s*(.*?)"?\s*$' } | Select-Object -First 1
            if ($para -and $para -match '^\s*SetupPara01\s*=.*\\([^"\\]+\.msi)"\s*(.*?)"?\s*$') {
                $msiName = $Matches[1]
                $extra = $Matches[2] -split '\s+' | Where-Object {
                    $_ -and -not ($_.StartsWith('%') -and $_.EndsWith('%')) -and $_ -notin '/qn', '/norestart'
                }
                $msi = Get-ChildItem $root -Recurse -Filter $msiName | Select-Object -First 1
            }
        }
    }
    if (-not $msi) {
        Write-Host 'Could not read Fujitsu setup INIs; using the largest MSI in the package.' -ForegroundColor DarkYellow
        $msi = Get-ChildItem $root -Recurse -Filter *.msi | Sort-Object Length -Descending | Select-Object -First 1
        $extra = @()
    }
    if (-not $msi) { throw 'No MSI found in the unpacked Fujitsu package.' }

    return [pscustomobject]@{
        Source    = $root
        IsFolder  = $true
        EntryRel  = $msi.FullName.Substring($root.Length).TrimStart('\')
        Kind      = 'msi'
        Arguments = (@('/qn', '/norestart') + $extra) -join ' '
        Label     = 'Fujitsu PaperStream IP'
    }
}

function Initialize-Msi([string]$Msi) {
    return [pscustomobject]@{
        Source    = $Msi
        IsFolder  = $false
        EntryRel  = [IO.Path]::GetFileName($Msi)
        Kind      = 'msi'
        Arguments = '/qn /norestart'
        Label     = 'MSI package'
    }
}

# ---------------------------------------------------------------- per-machine worker (runs in a background job)

$worker = {
    param($Computer, $Source, $IsFolder, $RemoteDir, $EntryRel, $Kind, $Arguments, $IsKodak)
    $ErrorActionPreference = 'Stop'
    $r = [ordered]@{ Computer = $Computer; Status = 'Failed'; ExitCode = $null; CopyMethod = ''; Detail = '' }
    try {
        if (-not (Test-Connection -ComputerName $Computer -Count 1 -Quiet)) {
            $r.Status = 'Offline'; $r.Detail = 'No ping reply'
            return [pscustomobject]$r
        }
        try { Test-WSMan -ComputerName $Computer | Out-Null }
        catch { $r.Detail = 'WinRM unreachable'; return [pscustomobject]$r }

        # --- copy: admin share first, WinRM session as fallback
        $copied = $false
        if ($RemoteDir -match '^([A-Za-z]):\\(.*)$') {
            $unc = "\\$Computer\$($Matches[1])`$\$($Matches[2])"
            try {
                New-Item -ItemType Directory -Path $unc -Force | Out-Null
                if ($IsFolder) { Copy-Item -Path (Join-Path $Source '*') -Destination $unc -Recurse -Force }
                else { Copy-Item -Path $Source -Destination $unc -Force }
                $copied = $true; $r.CopyMethod = 'SMB'
            } catch { }
        }
        if (-not $copied) {
            $s = New-PSSession -ComputerName $Computer
            try {
                Invoke-Command -Session $s -ArgumentList $RemoteDir -ScriptBlock {
                    param($d) New-Item -ItemType Directory -Path $d -Force | Out-Null
                }
                if ($IsFolder) { Copy-Item -Path (Join-Path $Source '*') -Destination $RemoteDir -ToSession $s -Recurse -Force }
                else { Copy-Item -Path $Source -Destination $RemoteDir -ToSession $s -Force }
                $r.CopyMethod = 'WinRM'
            } finally { Remove-PSSession $s }
        }

        # --- run
        $entry = Join-Path $RemoteDir $EntryRel
        $code = Invoke-Command -ComputerName $Computer -ArgumentList $entry, $Arguments, $Kind -ScriptBlock {
            param($Path, $ArgLine, $Kind)
            if ($Kind -eq 'msi') {
                $p = Start-Process msiexec.exe -ArgumentList "/i `"$Path`" $ArgLine" -Wait -PassThru
            } elseif ($ArgLine) {
                $p = Start-Process -FilePath $Path -ArgumentList $ArgLine -Wait -PassThru
            } else {
                $p = Start-Process -FilePath $Path -Wait -PassThru
            }
            $p.ExitCode
        }
        $r.ExitCode = $code
        switch ($code) {
            0       { $r.Status = 'Success' }
            3010    { $r.Status = 'Success'; $r.Detail = 'Reboot required' }
            1641    { $r.Status = 'Success'; $r.Detail = 'Reboot started by installer' }
            1618    { $r.Detail = 'Another installation is in progress' }
            1603    { $r.Detail = 'Fatal install error (MSI 1603)' }
            default { $r.Detail = "Exit code $code" }
        }

        # Kodak writes ERRORCODE/ERRORTEXT/REBOOT back into its setup.ini (REPORTERROR=1).
        if ($IsKodak) {
            $ini = Join-Path (Split-Path $entry -Parent) 'setup.ini'
            $rep = Invoke-Command -ComputerName $Computer -ArgumentList $ini -ScriptBlock {
                param($i)
                if (Test-Path $i) { Get-Content $i | Where-Object { $_ -match '^(ERRORCODE|ERRORTEXT|REBOOT)=' } }
            }
            if ($rep) {
                $r.Detail = (@($rep) -join '; ')
                $ec = @($rep) | Where-Object { $_ -match '^ERRORCODE=(-?\d+)' } | Select-Object -First 1
                if ($ec -and $ec -match '^ERRORCODE=(-?\d+)' -and [int]$Matches[1] -ne 0) { $r.Status = 'Failed' }
            }
        }
    } catch {
        $r.Detail = $_.Exception.Message -replace '[\r\n]+', ' '
    }
    return [pscustomobject]$r
}

# ================================================================= main

Write-Host ''
Write-Host 'Scanner Driver Push' -ForegroundColor Green
Write-Host 'Kodak Alaris / Fujitsu PaperStream IP / MSI - silent install over WinRM'
Write-Host "Running as: $env:USERDOMAIN\$env:USERNAME (must be admin on the targets)" -ForegroundColor DarkGray

# --- 1. installer
Write-Title 'Step 1: Installer'
while ($true) {
    $installer = Read-Text 'Full path to the installer (.exe or .msi)'
    if ($installer -and (Test-Path $installer -PathType Leaf)) { break }
    Write-Host '  File not found. Try again.' -ForegroundColor Red
}
$installer = (Resolve-Path $installer).Path
Write-Host 'Checking installer type ...'
$detected = Get-InstallerType $installer
$defaultType = switch ($detected) { 'kodak' { 1 } 'fujitsu' { 2 } 'msi' { 3 } default { 0 } }
if ($defaultType -gt 0) {
    Write-Host "Detected: $(@('Kodak Alaris', 'Fujitsu PaperStream IP', 'MSI package')[$defaultType - 1])" -ForegroundColor Green
} else {
    Write-Host 'Could not detect the installer type automatically.' -ForegroundColor DarkYellow
}
$type = Read-Choice 'What kind of installer is this?' @(
    'Kodak Alaris driver (InstallSoftware_*.exe)',
    'Fujitsu PaperStream IP driver (PSIP*.exe)',
    'MSI package',
    'Quit'
) $defaultType
if ($type -eq 4) { return }

try {
    $pkg = switch ($type) {
        1 { Initialize-Kodak $installer }
        2 { Initialize-Fujitsu $installer }
        3 { Initialize-Msi $installer }
    }
} catch {
    Write-Host "Could not prepare the package: $($_.Exception.Message)" -ForegroundColor Red
    return
}

# --- 2. arguments
Write-Title 'Step 2: Silent install command'
while ($true) {
    $preview = if ($pkg.Kind -eq 'msi') { "msiexec /i `"<dest>\$($pkg.EntryRel)`" $($pkg.Arguments)" }
               else { "`"<dest>\$($pkg.EntryRel)`" $($pkg.Arguments)" }
    Write-Host "Will run on each machine:" -ForegroundColor Yellow
    Write-Host "  $preview"
    $c = Read-Choice 'Use this command?' @('Yes, continue', 'Edit the arguments', 'Quit') 1
    if ($c -eq 1) { break }
    if ($c -eq 3) { return }
    $pkg.Arguments = Read-Text 'New arguments' $pkg.Arguments
}

# --- 3. targets
Write-Title 'Step 3: Target machines'
$targets = @()
while ($targets.Count -eq 0) {
    $src = Read-Choice 'How do you want to enter the machines?' @(
        'Type computer names (comma or space separated)',
        'Load from a .txt file (one name per line)'
    ) 1
    if ($src -eq 1) {
        $raw = Read-Host 'Computer names'
        $targets = @($raw -split '[,;\s]+' | Where-Object { $_ })
    } else {
        $file = Read-Text 'Path to the .txt file'
        if ($file -and (Test-Path $file -PathType Leaf)) {
            $targets = @(Get-Content $file | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
        } else {
            Write-Host '  File not found.' -ForegroundColor Red
        }
    }
    $targets = @($targets | Sort-Object -Unique)
    if ($targets.Count -eq 0) { Write-Host '  No machines entered.' -ForegroundColor Red }
}
Write-Host "$($targets.Count) machine(s): $($targets -join ', ')"

# --- 4. options
Write-Title 'Step 4: Options'
$destRoot = Read-Text 'Destination folder on each machine' 'C:\temp\it'
$remoteDir = if ($pkg.IsFolder) { Join-Path $destRoot ([IO.Path]::GetFileName($installer) -replace '\.[^.]+$', '') } else { $destRoot }
$limitChoice = Read-Choice 'How many machines at once?' @('1', '3', '5', '10') 3
$limit = @(1, 3, 5, 10)[$limitChoice - 1]
$timeoutChoice = Read-Choice 'Timeout per machine?' @('10 minutes', '20 minutes', '30 minutes', '60 minutes') 2
$timeoutMin = @(10, 20, 30, 60)[$timeoutChoice - 1]

# --- 5. confirm
Write-Title 'Summary'
Write-Host "  Package     : $($pkg.Label)  ($installer)"
Write-Host "  Copies to   : $remoteDir"
Write-Host ("  Runs        : " + $preview.Replace('<dest>', $remoteDir))
Write-Host "  Machines    : $($targets.Count)"
Write-Host "  At once     : $limit"
Write-Host "  Timeout     : $timeoutMin min per machine"
if ((Read-Choice 'Start the push?' @('Yes, start', 'Cancel') 2) -ne 1) { return }

# --- 6. run
Write-Title 'Running'
$queue = New-Object System.Collections.Queue
$targets | ForEach-Object { $queue.Enqueue($_) }
$running = @{}
$results = New-Object System.Collections.Generic.List[object]
$isKodak = ($pkg.Label -eq 'Kodak Alaris')

function Write-Result($res) {
    $color = switch ($res.Status) { 'Success' { 'Green' } 'Offline' { 'DarkYellow' } default { 'Red' } }
    Write-Host ("  [{0}] {1,-20} exit={2,-6} {3} {4}" -f $res.Status.ToUpper(), $res.Computer, $res.ExitCode, $res.CopyMethod, $res.Detail) -ForegroundColor $color
}

while ($queue.Count -gt 0 -or $running.Count -gt 0) {
    while ($queue.Count -gt 0 -and $running.Count -lt $limit) {
        $computer = $queue.Dequeue()
        $job = Start-Job -Name "DriverPush_$computer" -ScriptBlock $worker -ArgumentList `
            $computer, $pkg.Source, $pkg.IsFolder, $remoteDir, $pkg.EntryRel, $pkg.Kind, $pkg.Arguments, $isKodak
        $running[$job.Id] = @{ Job = $job; Computer = $computer; Start = Get-Date }
        Write-Host "  -> $computer started" -ForegroundColor DarkGray
    }

    foreach ($id in @($running.Keys)) {
        $entry = $running[$id]
        $job = $entry.Job
        if ($job.State -in 'Completed', 'Failed', 'Stopped') {
            $res = Receive-Job $job -ErrorAction SilentlyContinue |
                Where-Object { $_.PSObject.Properties['Computer'] -and $_.PSObject.Properties['Status'] } |
                Select-Object -Last 1
            if (-not $res) {
                $reason = $job.ChildJobs[0].JobStateInfo.Reason
                $res = [pscustomobject]@{ Computer = $entry.Computer; Status = 'Failed'; ExitCode = $null; CopyMethod = ''
                                          Detail = $(if ($reason) { $reason.Message } else { 'No result returned' }) }
            }
            Remove-Job $job -Force
            $running.Remove($id)
            $results.Add($res)
            Write-Result $res
        } elseif (((Get-Date) - $entry.Start).TotalMinutes -ge $timeoutMin) {
            Stop-Job $job; Remove-Job $job -Force
            $running.Remove($id)
            $res = [pscustomobject]@{ Computer = $entry.Computer; Status = 'Timeout'; ExitCode = $null; CopyMethod = ''
                                      Detail = "No result after $timeoutMin min (installer may still be running on the machine)" }
            $results.Add($res)
            Write-Result $res
        }
    }
    Start-Sleep -Milliseconds 500
}

# --- 7. summary + CSV
$ok = @($results | Where-Object Status -eq 'Success').Count
Write-Title "Done: $ok of $($targets.Count) succeeded"
$results | Select-Object Computer, Status, ExitCode, CopyMethod, Detail | Format-Table -AutoSize | Out-Host

$csv = Join-Path ([Environment]::GetFolderPath('Desktop')) ("DriverPush_{0:yyyyMMdd_HHmmss}.csv" -f (Get-Date))
$results | Select-Object @{ n = 'Timestamp'; e = { Get-Date -Format s } }, @{ n = 'Package'; e = { $pkg.Label } },
    Computer, Status, ExitCode, CopyMethod, Detail | Export-Csv -Path $csv -NoTypeInformation
Write-Host "Results saved to $csv" -ForegroundColor Green

if ($pkg.IsFolder -and $pkg.Source.StartsWith((Join-Path $env:TEMP 'DriverPush'))) {
    if ((Read-Choice 'Delete the unpacked files on this PC?' @('Yes', 'No, keep them for another run') 1) -eq 1) {
        Remove-Item (Join-Path $env:TEMP 'DriverPush') -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host 'Deleted.'
    }
}
