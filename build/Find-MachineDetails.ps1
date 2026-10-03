# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
Finds details of this machine and of the account using it, where they must not be.

.DESCRIPTION
The repository is meant to be public, so no tracked file, commit or released binary
may carry the user name, machine name, profile path or account SID of whoever built
it. Those four patterns are built when the script runs. Three are fixed: an account
SID of any machine, an e-mail address outside the allowed domains, and a drive-rooted
path outside the allowed roots.

Without a switch it scans the index: every file a commit made now would hold, and
every file name. That is what build/hooks/pre-commit runs.

Exit code 0: nothing found. 1: at least one finding, one per line on stdout, as
"where: rule: match". 2: the scan could not run.

.PARAMETER WorkingTree
Scan the files on disk instead of the index: tracked files, and untracked files no
ignore rule covers. The test suite runs this, so a file not yet staged is covered.

.PARAMETER History
Scan every blob reachable from any ref, every commit message, and every author and
committer identity.

.PARAMETER Path
Scan these files as bytes, binaries included: every run of four or more printable
ASCII characters, and every such run encoded as UTF-16LE.

.PARAMETER Repository
The repository to scan. Defaults to the one this script is in.
#>
#Requires -Version 7.2
[CmdletBinding(DefaultParameterSetName = 'Index')]
param(
    [Parameter(ParameterSetName = 'WorkingTree', Mandatory)]
    [switch] $WorkingTree,

    [Parameter(ParameterSetName = 'History', Mandatory)]
    [switch] $History,

    [Parameter(ParameterSetName = 'Path', Mandatory)]
    [string[]] $Path,

    [string] $Repository = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Fictional paths in the documentation and the tests are spelled under these roots.
# A drive-rooted path anywhere else is a finding.
$AllowedPathRoots = @('C:\Apps\', 'C:\Tools\', 'C:\src\', 'D:\Other\', 'C:\Users\<you>\', 'X:\')

# The second-level domains RFC 2606 reserves for documentation.
$AllowedMailDomains = @('example.com', 'example.org', 'example.net')

function New-Rule([string] $Name, [string] $Pattern, [scriptblock] $Allowed = $null) {
    [pscustomobject]@{
        Name    = $Name
        Regex   = [regex]::new($Pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        Allowed = $Allowed
    }
}

function Test-AllowedPath([string] $Text) {
    $normal = $Text.Replace('\\', '\').Replace('/', '\')
    # A drive root alone, or followed by one character or by no letter at all,
    # names nothing. In a binary that shape is mostly a coincidence of bytes.
    if ($normal.Length -le 4 -or $normal.Substring(3) -notmatch '[a-z]') {
        return $true
    }
    foreach ($root in $AllowedPathRoots) {
        if ($normal.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Get-Rules {
    $separator = '(?:\\{1,2}|/)'
    $profilePath = [Environment]::GetFolderPath('UserProfile').TrimEnd('\')
    $segments = $profilePath -split '\\'
    $drive = [regex]::Escape($segments[0].TrimEnd(':'))
    $tail = ($segments | Select-Object -Skip 1 | ForEach-Object { [regex]::Escape($_) }) -join $separator
    $user = [regex]::Escape([Environment]::UserName)
    $machine = [regex]::Escape([Environment]::MachineName)

    @(
        # The profile path in any slash style, JSON-escaped, or as Git Bash prints it.
        New-Rule 'profile-path' "(?:${drive}:|/$drive)$separator$tail(?![\w.-])"
        # The user name as a path segment. As a word it is also a person's name,
        # which the copyright line carries on purpose.
        New-Rule 'user-name' "$separator$user(?![\w.-])"
        New-Rule 'machine-name' "(?<![\w-])$machine(?![\w-])"
        New-Rule 'account-sid' 'S-1-5-21(?:-\d+){3}'
        # The top-level label is all lower or all upper case, which tells an address
        # from a dotted .NET name such as a runtime setting in a binary.
        New-Rule 'mail-address' '[\w.%+-]+@([\w-]+(?:\.[\w-]+)*\.(?-i:[a-z]{2,24}|[A-Z]{2,24}))(?![\w-]|\.\w)' {
            param($Match)
            $AllowedMailDomains -contains $Match.Groups[1].Value.ToLowerInvariant()
        }
        New-Rule 'drive-path' '(?<![\w])[a-z]:(?:\\{1,2}|/)[^\s"''`|*?,;)\]]*' {
            param($Match)
            Test-AllowedPath $Match.Value
        }
    )
}

# A cheap superset of the rules, for git grep to select candidate lines with.
function Get-Prefilter {
    $words = @([Environment]::UserName, [Environment]::MachineName) |
        ForEach-Object { [regex]::Replace($_, '[.^$*+?()\[\]{}|\\]', '\$0') }
    (@('[a-z]:[\\/]', '@', 's-1-5-21') + $words) -join '|'
}

$Rules = Get-Rules
$Findings = [System.Collections.Generic.List[string]]::new()

function Test-Text([string] $Where, [string] $Text, [string[]] $Skip = @()) {
    foreach ($rule in $Rules) {
        if ($Skip -contains $rule.Name) {
            continue
        }
        foreach ($match in $rule.Regex.Matches($Text)) {
            if ($rule.Allowed -and (& $rule.Allowed $match)) {
                continue
            }
            $Findings.Add("${Where}: $($rule.Name): $($match.Value)")
        }
    }
}

function Invoke-Git([string[]] $Arguments) {
    $start = [System.Diagnostics.ProcessStartInfo]::new('git')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $start.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)
    $start.ArgumentList.Add('-C')
    $start.ArgumentList.Add($Repository)
    foreach ($argument in $Arguments) {
        $start.ArgumentList.Add($argument)
    }
    $process = [System.Diagnostics.Process]::Start($start)
    $errors = $process.StandardError.ReadToEndAsync()
    $output = $process.StandardOutput.ReadToEnd()
    $process.WaitForExit()
    [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output; Error = $errors.Result }
}

# Reads git grep --null output: "where<NUL>line<NUL>text" per line.
function Test-GrepOutput($Result) {
    if ($Result.ExitCode -gt 1) {
        throw "git grep failed: $($Result.Error.Trim())"
    }
    foreach ($line in $Result.Output -split "`n") {
        $parts = $line -split "`0", 3
        if ($parts.Count -eq 3) {
            Test-Text "$($parts[0]):$($parts[1])" $parts[2]
        }
    }
}

try {
    $prefilter = Get-Prefilter

    switch ($PSCmdlet.ParameterSetName) {
        'Index' {
            Test-GrepOutput (Invoke-Git @('grep', '--cached', '-I', '-n', '--null', '-i', '-E', '-e', $prefilter))
            foreach ($name in (Invoke-Git @('ls-files', '--cached', '-z')).Output -split "`0") {
                if ($name) {
                    Test-Text 'file name' $name
                }
            }
        }
        'WorkingTree' {
            Test-GrepOutput (Invoke-Git @('grep', '--untracked', '-I', '-n', '--null', '-i', '-E', '-e', $prefilter))
            foreach ($name in (Invoke-Git @('ls-files', '--cached', '--others', '--exclude-standard', '-z')).Output -split "`0") {
                if ($name) {
                    Test-Text 'file name' $name
                }
            }
        }
        'History' {
            $revisions = @((Invoke-Git @('rev-list', '--all')).Output -split "`n" | Where-Object { $_ })
            if ($revisions.Count -gt 0) {
                Test-GrepOutput (Invoke-Git (@('grep', '-I', '-n', '--null', '-i', '-E', '-e', $prefilter) + $revisions))
                foreach ($revision in $revisions) {
                    foreach ($name in (Invoke-Git @('ls-tree', '-r', '-z', '--name-only', $revision)).Output -split "`0") {
                        if ($name) {
                            Test-Text "${revision}: file name" $name
                        }
                    }
                }
            }
            $log = Invoke-Git @('log', '--all', '--format=%H%x1f%an <%ae>%x1f%cn <%ce>%x1f%B%x1e')
            foreach ($record in $log.Output -split "`u{1e}") {
                $fields = $record.Trim() -split "`u{1f}", 4
                if ($fields.Count -eq 4) {
                    # An identity is an address by design, so only the other rules apply to it.
                    Test-Text "$($fields[0]): author" $fields[1] @('mail-address')
                    Test-Text "$($fields[0]): committer" $fields[2] @('mail-address')
                    Test-Text "$($fields[0]): message" $fields[3]
                }
            }
        }
        'Path' {
            foreach ($file in $Path) {
                $text = [System.Text.Encoding]::Latin1.GetString([System.IO.File]::ReadAllBytes($file))
                foreach ($run in [regex]::Matches($text, '[\x20-\x7E]{4,}')) {
                    Test-Text "${file}@$($run.Index)" $run.Value
                }
                foreach ($run in [regex]::Matches($text, '(?:[\x20-\x7E]\x00){4,}')) {
                    Test-Text "${file}@$($run.Index)" $run.Value.Replace("`0", '')
                }
            }
        }
    }
}
catch {
    [Console]::Error.WriteLine("Find-MachineDetails: the scan could not run: $($_.Exception.Message)")
    exit 2
}

foreach ($finding in $Findings) {
    Write-Output $finding
}

if ($Findings.Count -gt 0) {
    exit 1
}
exit 0
