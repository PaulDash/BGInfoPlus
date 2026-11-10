#Requires -Version 7.0
<#
.SYNOPSIS
Removes compiled release directories older than the two releases before the latest.

.DESCRIPTION
Finds directories named v<major>.<minor>.<patch> under artifacts\release, sorts
them by semantic version, and keeps the newest release plus KeepPrevious older
releases. Non-versioned directories and filesystem links are never removed.

Use -WhatIf to review the exact directories before deletion. Because this is a
destructive operation, PowerShell requests confirmation unless -Confirm:$false
is supplied explicitly.

.PARAMETER KeepPrevious
Number of releases before the newest one to retain. The default of 2 keeps three
release directories in total: the newest release and its two predecessors.

.EXAMPLE
./tools/Clean-ReleaseArtifacts.ps1 -WhatIf

.EXAMPLE
./tools/Clean-ReleaseArtifacts.ps1 -Confirm:$false
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [ValidateRange(0, 100)]
    [int]$KeepPrevious = 2
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\release'))

if (-not (Test-Path -LiteralPath $releaseRoot -PathType Container)) {
    Write-Host "No compiled release directory exists at $releaseRoot"
    return
}

$releases = @(
    Get-ChildItem -LiteralPath $releaseRoot -Directory -Force |
        ForEach-Object {
            if ($_.Name -notmatch '^v(?<version>\d+\.\d+\.\d+)$') { return }
            # OneDrive uses ReparsePoint for ordinary synced directories, so use
            # PowerShell's resolved link metadata to reject actual links/junctions.
            if ($null -ne $_.LinkType -or $null -ne $_.Target) {
                Write-Warning "Ignoring filesystem link $($_.FullName)"
                return
            }
            [pscustomobject]@{ Directory = $_; Version = [Version]$Matches.version }
        } |
        Sort-Object Version -Descending
)

$keepCount = $KeepPrevious + 1
if ($releases.Count -le $keepCount) {
    Write-Host "Nothing to clean. Found $($releases.Count) release(s); keeping up to $keepCount."
    return
}

$kept = @($releases | Select-Object -First $keepCount)
Write-Host "Keeping: $($kept.Directory.Name -join ', ')"

foreach ($release in @($releases | Select-Object -Skip $keepCount)) {
    $target = [IO.Path]::GetFullPath($release.Directory.FullName)
    if ([IO.Path]::GetDirectoryName($target) -ne $releaseRoot) {
        throw "Refusing to remove a release directory outside $releaseRoot`: $target"
    }
    if ($PSCmdlet.ShouldProcess($target, 'Remove compiled release artifacts')) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}
