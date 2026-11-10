#Requires -Version 7.0
<#
.SYNOPSIS
Validates the contents and cross-file consistency of a built BGInfoPlus release.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ReleaseRoot,
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+$')] [string]$Version
)

$ErrorActionPreference = 'Stop'
$releaseRoot = [IO.Path]::GetFullPath($ReleaseRoot)
$expectedFiles = @(
    "BGInfoPlus-$Version-win-x64.exe",
    "BGInfoPlus-$Version-win-x64.msi",
    "BGInfoPlus-$Version-win-x64.zip",
    "BGInfoPlus-$Version-azure-vmapp.zip",
    'BGInfoPlus-DefaultConfig.json',
    'azure-vm-application.json',
    'SHA256SUMS.txt'
)
foreach ($name in $expectedFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $releaseRoot $name) -PathType Leaf)) { throw "Release artifact is missing: $name" }
}

$defaultConfig = Get-Content -LiteralPath (Join-Path $releaseRoot 'BGInfoPlus-DefaultConfig.json') -Raw |
    ConvertFrom-Json -ErrorAction Stop
if ([string]::IsNullOrWhiteSpace($defaultConfig.Document) -or $defaultConfig.Placeholders.Count -lt 1 -or $defaultConfig.RunAtSignIn) {
    throw 'The release default configuration is invalid.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
function Assert-ZipEntries([string]$path, [string[]]$required) {
    $archive = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $entries = @($archive.Entries.FullName)
        foreach ($name in $required) { if ($name -notin $entries) { throw "$([IO.Path]::GetFileName($path)) is missing $name" } }
    } finally { $archive.Dispose() }
}
Assert-ZipEntries (Join-Path $releaseRoot "BGInfoPlus-$Version-win-x64.zip") `
    @('BGInfoPlus.exe', 'Deploy-BGInfoPlusForAllUsers.ps1', 'README.txt')
Assert-ZipEntries (Join-Path $releaseRoot "BGInfoPlus-$Version-azure-vmapp.zip") `
    @('BGInfoPlus.exe', 'Deploy-BGInfoPlusForAllUsers.ps1', 'Install-BGInfoPlus.ps1', 'Remove-BGInfoPlus.ps1')

$manifest = Get-Content -LiteralPath (Join-Path $releaseRoot 'azure-vm-application.json') -Raw |
    ConvertFrom-Json -ErrorAction Stop
if ($manifest.version -ne $Version -or $manifest.packageFileName -ne "BGInfoPlus-$Version-azure-vmapp.zip" -or
    $manifest.configurationFileName -ne 'BGInfoPlus-config.json') { throw 'The Azure VM Application manifest is inconsistent.' }

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $null
try {
    $database = $installer.OpenDatabase((Join-Path $releaseRoot "BGInfoPlus-$Version-win-x64.msi"), 0)
    function Read-MsiColumn([string]$query) {
        $view = $database.OpenView($query)
        try {
            $view.Execute()
            $values = @()
            while ($record = $view.Fetch()) {
                $values += $record.StringData(1)
                [Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) | Out-Null
            }
            return $values
        } finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) | Out-Null }
    }
    $msiFiles = @(Read-MsiColumn 'SELECT `FileName` FROM `File`')
    foreach ($name in @('BGInfoPlus.exe', 'Deploy-BGInfoPlusForAllUsers.ps1', 'Remove-BGInfoPlusSharedDeployment.ps1', 'BGInfoPlus-DefaultConfig.json')) {
        if (-not ($msiFiles | Where-Object { $_ -like "*$name*" })) { throw "MSI is missing $name" }
    }
    $customActions = @(Read-MsiColumn 'SELECT `Action` FROM `CustomAction`')
    foreach ($name in @('InstallDefaultDeployment', 'RemoveSharedDeployment')) {
        if ($name -notin $customActions) { throw "MSI is missing custom action $name" }
    }
} finally {
    if ($database) { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null }
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null
}

$checksumLines = Get-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt')
foreach ($line in $checksumLines) {
    if ($line -notmatch '^([a-f0-9]{64})  (.+)$') { throw "Invalid checksum line: $line" }
    $actual = (Get-FileHash -LiteralPath (Join-Path $releaseRoot $Matches[2]) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Matches[1]) { throw "Checksum mismatch: $($Matches[2])" }
}

Write-Host "Release artifact validation passed for BGInfoPlus $Version."
