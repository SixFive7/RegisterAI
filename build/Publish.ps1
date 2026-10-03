# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
Publishes RegisterAI.exe as one NativeAOT file into artifacts\publish.

.DESCRIPTION
The contract tests run this file, not the build output, so publish before running
the suite. The folder also receives RegisterAI.pdb, which is never shipped.
#>
#Requires -Version 7.2
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\RegisterAI\RegisterAI.csproj'
$output = Join-Path $root 'artifacts\publish'

dotnet publish $project -c Release -r win-x64 -o $output -nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish exited $LASTEXITCODE."
}

$exe = Join-Path $output 'RegisterAI.exe'
"{0}  {1:N0} bytes" -f $exe, (Get-Item -LiteralPath $exe).Length
