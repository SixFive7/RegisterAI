# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
Turns one version's section of CHANGELOG.md into the body of its GitHub release.

.DESCRIPTION
The changelog section is the record, and the release page is the short form of it. The
body opens with the section's own opening paragraphs. Then each entry becomes one line:
its icon, its bold headline, and a "read more" link to the entry's own lines in the
changelog the tag carries. The body ends with the icon legend, read from the top of the
changelog, and a link to the whole section.

Every entry is written as

    - <icon> **One-sentence headline.** What happened, in full.

and a section holds its opening paragraphs and then Keep a Changelog groups (### Added,
### Changed and the rest), each holding at least one entry and nothing else. Anything
else is refused with the line that breaks the shape.

The links are line ranges, so they are true of one file only. The changelog on disk must
match HEAD, and the tag the links point into must be at HEAD when it exists; otherwise
the script refuses. A body longer than GitHub's limit of 125,000 characters keeps its
headlines and loses the per-entry links, and the script says which shape it wrote.

Exit code 0 when the body is written, 1 when the script refuses.

.PARAMETER Version
The version whose section becomes the body, without the v: 0.3.0.

.PARAMETER Destination
Where the body is written: UTF-8 without a byte order mark, LF line ends.

.PARAMETER At
The tag the links point into. Defaults to v<Version>. The page of an older release can
link into a newer tag that carries the same section.

.PARAMETER Path
The changelog. Defaults to CHANGELOG.md at the repository root.

.PARAMETER Repository
The repository the links point into.

.PARAMETER Limit
The length at which the per-entry links are dropped. A parameter so that a test can
reach it without a large changelog.

.EXAMPLE
pwsh build/New-ReleaseNotes.ps1 -Version 0.3.0 -Destination .work/notes.md
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $Destination,

    [string] $At,

    [string] $Path = (Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md'),

    [string] $Repository = 'https://github.com/SixFive7/RegisterAI',

    [int] $Limit = 125000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSStyle.OutputRendering = 'PlainText'

# A refusal is read by a person or by a test, so it is the sentence alone on stderr,
# without colour codes or a pointer into this file.
function Stop-Notes([string] $Message) {
    [Console]::Error.WriteLine($Message)
    exit 1
}

if (-not $At) {
    $At = "v$Version"
}

$Path = [System.IO.Path]::GetFullPath($Path)

if (-not (Test-Path -LiteralPath $Path)) {
    Stop-Notes "There is no changelog at '$Path'."
}

# The line numbers have to be the ones the tag carries.
function Invoke-Git([string[]] $Arguments) {
    $output = & git @Arguments 2>&1
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = (($output | Out-String).Trim()) }
}

$folder = Split-Path -Parent $Path
$provenance = "'$Path' is not tracked by git, so nothing ties these line numbers to a tag."
$tracked = $false

if (Get-Command git -CommandType Application -ErrorAction SilentlyContinue) {
    $inside = Invoke-Git @('-C', $folder, 'rev-parse', '--is-inside-work-tree')

    if ($inside.ExitCode -eq 0 -and $inside.Output -eq 'true') {
        $tracked = (Invoke-Git @('-C', $folder, 'ls-files', '--error-unmatch', $Path)).ExitCode -eq 0
    }
}

if ($tracked) {
    $head = (Invoke-Git @('-C', $folder, 'rev-parse', 'HEAD')).Output
    $tag = Invoke-Git @('-C', $folder, 'rev-parse', '--verify', '--quiet', "refs/tags/$At^{commit}")

    if ((Invoke-Git @('-C', $folder, 'diff', '--quiet', 'HEAD', '--', $Path)).ExitCode -ne 0) {
        Stop-Notes "The changelog differs from HEAD ($head), and every link is a line range into the changelog as $At carries it. Commit the changelog first."
    }

    if ($tag.ExitCode -eq 0 -and $tag.Output -ne $head) {
        Stop-Notes "The tag $At is at $($tag.Output) and HEAD is $head, so the changelog read here is not the one the links point into. Generate the body with $At checked out."
    }

    if ($tag.ExitCode -ne 0 -and $At -ne "v$Version") {
        Stop-Notes "The tag $At does not exist, so the links would point into nothing."
    }

    $provenance = if ($tag.ExitCode -eq 0) {
        "The line ranges are CHANGELOG.md at $At ($head), which is HEAD."
    }
    else {
        "The line ranges are CHANGELOG.md at HEAD ($head); the tag $At does not exist yet."
    }
}

$content = (Get-Content -LiteralPath $Path -Raw) -replace "`r`n", "`n"

# The version's section.
$heading = [regex]::Match($content, '(?m)^##[ \t]+\[' + [regex]::Escape($Version) + '\][^\n]*$')

if (-not $heading.Success) {
    Stop-Notes "The changelog has no '## [$Version]' section, so there is nothing to make the release notes from."
}

$headingLine = ([regex]::Matches($content.Substring(0, $heading.Index), "`n")).Count + 1
$rest = $content.Substring($heading.Index + $heading.Length)
$next = [regex]::Match($rest, '(?m)^##[ \t]')
$section = if ($next.Success) { $rest.Substring(0, $next.Index) } else { $rest }

# The legend: the last block above the first version heading whose every line is a
# table row. A legend written any other way is refused, because the release page has
# no legend of its own.
$top = $content.Substring(0, [regex]::Match($content, '(?m)^##[ \t]').Index)
$legendBlock = $top -split "`n`n" | Where-Object {
    $rows = @($_ -split "`n" | Where-Object { $_.Trim().Length -gt 0 })
    $rows.Count -ge 3 -and @($rows | Where-Object { -not $_.TrimStart().StartsWith('|') }).Count -eq 0
} | Select-Object -Last 1

if (-not $legendBlock) {
    Stop-Notes 'The changelog has no icon legend above its first version heading. The legend is a Markdown table: a heading row, a delimiter row and a row per two icons.'
}

$legend = ($legendBlock -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 }) -join "`n"

# The anchor GitHub gives a heading: link text kept, code and emphasis marks dropped,
# letters, digits, hyphens and underscores kept in lower case, spaces made hyphens,
# everything else dropped.
function Get-Anchor([string] $Text) {
    $text = [regex]::Replace($Text, '`([^`]*)`', { param($m) $m.Groups[1].Value -replace '[<>]', '' })
    $text = [regex]::Replace($text, '\[([^\]]*)\]\([^)]*\)', '$1')
    $text = [regex]::Replace($text, '<[^>]*>', '')
    $text = $text -replace '[`*]', ''
    $slug = [System.Text.StringBuilder]::new()

    foreach ($character in $text.Trim().ToCharArray()) {
        if ([char]::IsLetterOrDigit($character) -or $character -eq '-' -or $character -eq '_') {
            $null = $slug.Append([char]::ToLowerInvariant($character))
        }
        elseif ($character -eq ' ' -or $character -eq "`t") {
            $null = $slug.Append('-')
        }
    }

    $slug.ToString()
}

$source = "$Repository/blob/$At/CHANGELOG.md"
$sectionLink = $source + '#' + (Get-Anchor ($heading.Value -replace '^##[ \t]+', ''))

# The section's parts. Line numbers are the file's own: line $index of the section is
# line $headingLine + $index of the file.
$lines = $section -split "`n"
$preamble = [System.Collections.Generic.List[string]]::new()
$groups = [System.Collections.Generic.List[object]]::new()
$group = $null
$entry = $null

function Complete-Entry {
    if ($null -eq $script:entry) {
        return
    }

    if ($null -eq $script:group) {
        Stop-Notes "The [$Version] section has an entry above its first group heading: '$($script:entry.Lines[0].Trim())'. Every entry belongs under ### Added, ### Changed, ### Deprecated, ### Removed, ### Fixed or ### Security."
    }

    $script:group.Entries.Add($script:entry)
    $script:entry = $null
}

for ($index = 0; $index -lt $lines.Count; $index++) {
    $line = $lines[$index]
    $number = $headingLine + $index

    if ($line -match '^###[ \t]+(?<name>.+?)[ \t]*$') {
        Complete-Entry
        $group = [pscustomobject]@{ Name = $Matches['name']; Entries = [System.Collections.Generic.List[object]]::new() }
        $groups.Add($group)
        continue
    }

    if ($line -match '^-[ \t]') {
        Complete-Entry
        $entry = [pscustomobject]@{ Lines = [System.Collections.Generic.List[string]]::new(); First = $number; Last = $number }
        $entry.Lines.Add($line)
        continue
    }

    if ($null -ne $entry) {
        if ($line.Trim().Length -eq 0 -or $line -match '^[ \t]') {
            $entry.Lines.Add($line)

            # The range ends on the entry's last line of text, never on the blank line
            # before the next entry.
            if ($line.Trim().Length -gt 0) {
                $entry.Last = $number
            }

            continue
        }

        Complete-Entry
    }

    if ($null -eq $group) {
        $preamble.Add($line)
        continue
    }

    if ($line.Trim().Length -gt 0) {
        Stop-Notes "The [$Version] section has a paragraph under '### $($group.Name)' that is not an entry: '$($line.Trim())'. A group holds entries only; prose belongs above the first group."
    }
}

Complete-Entry

$rendered = foreach ($g in $groups) {
    if ($g.Entries.Count -eq 0) {
        Stop-Notes "The [$Version] section has a '### $($g.Name)' heading with no entries under it."
    }

    $items = foreach ($e in $g.Entries) {
        $whole = $e.Lines -join "`n"
        $match = [regex]::Match($whole, '(?s)^-[ \t]+(?<icon>\S+)[ \t]+\*\*(?<headline>.+?)\*\*')

        if (-not $match.Success) {
            Stop-Notes "An entry in the [$Version] section is not written as '- <icon> **One-sentence headline.** the rest': $($e.Lines[0].Trim())"
        }

        [pscustomobject]@{
            Icon     = $match.Groups['icon'].Value
            Headline = ($match.Groups['headline'].Value -replace '\s+', ' ').Trim()
            First    = $e.First
            Last     = $e.Last
        }
    }

    [pscustomobject]@{ Name = $g.Name; Items = @($items) }
}

$rendered = @($rendered)

function New-Body([bool] $Linked) {
    $out = [System.Collections.Generic.List[string]]::new()

    # The changelog is wrapped for reading in an editor; a release page is read in a
    # browser column, so each paragraph becomes one line.
    $intro = ($preamble -join "`n") -split "`n[ `t]*`n" |
        ForEach-Object { ($_ -replace '\s+', ' ').Trim() } |
        Where-Object { $_.Length -gt 0 }

    foreach ($paragraph in $intro) {
        $out.Add($paragraph)
        $out.Add('')
    }

    foreach ($g in $rendered) {
        $out.Add("### $($g.Name)")
        $out.Add('')

        foreach ($item in $g.Items) {
            $line = "- $($item.Icon) **$($item.Headline)**"

            if ($Linked) {
                $line += " [read more]($source" + "?plain=1#L$($item.First)-L$($item.Last))"
            }

            $out.Add($line)
        }

        $out.Add('')
    }

    $out.Add('---')
    $out.Add('')
    $out.Add($legend)
    $out.Add('')
    $out.Add("The full changelog for this release: [CHANGELOG.md]($sectionLink)")

    (($out -join "`n") -replace "`n{3,}", "`n`n").Trim() + "`n"
}

$body = New-Body $true
$shape = 'linked'

if ($body.Length -gt $Limit) {
    $body = New-Body $false
    $shape = 'headlines'
}

$Destination = [System.IO.Path]::GetFullPath($Destination)
$null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination)
[System.IO.File]::WriteAllText($Destination, $body, [System.Text.UTF8Encoding]::new($false))

$entries = ($rendered | ForEach-Object { $_.Items.Count } | Measure-Object -Sum).Sum

if ($shape -eq 'headlines') {
    Write-Host "The body for $Version did not fit in $Limit characters with its links, so it lists the headlines alone: $($body.Length) characters, $entries entries."
}
else {
    Write-Host "The body for $Version is $($body.Length) characters: $entries entries in $($rendered.Count) groups, each linked to its own lines."
}

Write-Host $provenance
Write-Host "Wrote $Destination."

# Last, on stdout, for a script that calls this one.
"$shape $($body.Length)"
