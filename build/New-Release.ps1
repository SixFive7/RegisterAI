# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
Cuts a release: tags, publishes, tests, scans, writes SHA256SUMS and uploads two assets.

.DESCRIPTION
Runs from a clean tree whose HEAD is pushed, whose CHANGELOG.md has a
'## [<Version>] - <date>' section and nothing under '## [Unreleased]'. In order:

  1. builds the release notes from CHANGELOG.md with New-ReleaseNotes.ps1, then tags
     HEAD as v<Version>, so MinVer stamps that version into the build;
  2. publishes and checks that --version prints exactly <Version>;
  3. runs the whole suite with REGISTERAI_RELEASE_RUN=1, which turns a missing real
     client into a failure;
  4. scans the executable with Find-MachineDetails.ps1;
  5. writes artifacts\release\RegisterAI.exe and SHA256SUMS and checks the sums;
  6. pushes the tag and creates the GitHub release with the two files and the notes.

Each step stops the script on failure. A tag made in step 1 stays local until step 6.

.PARAMETER Version
The version, without the v: 0.1.0.

.PARAMETER Draft
Create the GitHub release as a draft.
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [switch] $Draft
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$tag = "v$Version"
$publish = Join-Path $root 'artifacts\publish'
$release = Join-Path $root 'artifacts\release'
$logs = Join-Path $root '.work\release'

function Invoke-Checked([string] $What, [scriptblock] $Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$What exited $LASTEXITCODE."
    }
}

Push-Location $root
try {
    if (git status --porcelain) {
        throw 'The working tree has changes. Commit or remove them first.'
    }

    $changelog = (Get-Content -Raw CHANGELOG.md) -replace "`r`n", "`n"
    if ($changelog -notmatch "(?m)^## \[$([regex]::Escape($Version))\] - \d{4}-\d{2}-\d{2}$") {
        throw "CHANGELOG.md has no '## [$Version] - <date>' section."
    }

    $unreleased = [regex]::Match($changelog, '(?ms)^## \[Unreleased\][ \t]*$(.*?)(?=^## |\z)')
    if (-not $unreleased.Success) {
        throw "CHANGELOG.md has no '## [Unreleased]' heading."
    }
    if ($unreleased.Groups[1].Value -match '(?m)^- ') {
        throw "CHANGELOG.md has entries under '## [Unreleased]'. Move them under '## [$Version]' first."
    }

    Invoke-Checked 'git fetch' { git fetch --quiet origin }
    if ((git rev-parse HEAD) -ne (git rev-parse '@{upstream}')) {
        throw 'HEAD is not the pushed head of its branch. Push first.'
    }

    # 1. The notes, built from the changelog at HEAD, which is the one the tag will
    #    carry; then the tag, local until the release is made. The notes come first
    #    so that a changelog in the wrong shape stops the release before it is tagged.
    New-Item -ItemType Directory -Force -Path $logs | Out-Null
    $notesFile = Join-Path $logs "notes-$Version.md"
    Invoke-Checked 'New-ReleaseNotes.ps1' { pwsh -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'New-ReleaseNotes.ps1') -Version $Version -Destination $notesFile }

    if (-not (git tag --list $tag)) {
        Invoke-Checked 'git tag' { git tag -a $tag -m "RegisterAI $Version" }
    }

    # 2. Publish, and the version the build stamped.
    Invoke-Checked 'Publish.ps1' { pwsh -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Publish.ps1') }
    $exe = Join-Path $publish 'RegisterAI.exe'
    $printed = (& $exe --version).Trim()
    if ($printed -ne $Version) {
        throw "The executable says it is '$printed', not '$Version'."
    }

    # 3. The whole suite, a missing real client being a failure.
    $log = Join-Path $logs "test-$Version.log"
    $env:REGISTERAI_RELEASE_RUN = '1'
    try {
        dotnet test --solution RegisterAI.slnx *> $log
        if ($LASTEXITCODE -ne 0) {
            throw "The suite failed; see $log."
        }
    }
    finally {
        Remove-Item Env:REGISTERAI_RELEASE_RUN
    }
    Select-String -Path $log -Pattern '^\s+(total|failed|succeeded|skipped):' | ForEach-Object { $_.Line.Trim() }

    # 4. No machine detail in what ships.
    Invoke-Checked 'Find-MachineDetails.ps1' { pwsh -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Find-MachineDetails.ps1') -Path $exe }

    # 5. The two assets and their sums.
    New-Item -ItemType Directory -Force -Path $release | Out-Null
    Copy-Item -LiteralPath $exe -Destination (Join-Path $release 'RegisterAI.exe') -Force
    $hash = (Get-FileHash -LiteralPath (Join-Path $release 'RegisterAI.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
    $sums = Join-Path $release 'SHA256SUMS'
    [System.IO.File]::WriteAllText($sums, "$hash  RegisterAI.exe`n")
    $check = (Get-Content -LiteralPath $sums).Split(' ')[0]
    if ($check -ne (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()) {
        throw 'SHA256SUMS does not match the published executable.'
    }
    "RegisterAI.exe  {0:N0} bytes  sha256 {1}" -f (Get-Item -LiteralPath $exe).Length, $hash

    # 6. The tag and the release.
    Invoke-Checked 'git push tag' { git push --quiet origin $tag }
    $arguments = @('release', 'create', $tag, (Join-Path $release 'RegisterAI.exe'), $sums, '--title', "RegisterAI $Version", '--notes-file', $notesFile, '--verify-tag')
    if ($Draft) {
        $arguments += '--draft'
    }
    Invoke-Checked 'gh release create' { gh @arguments }
}
finally {
    Pop-Location
}
