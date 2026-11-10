#Requires -Version 7.0
<#
.SYNOPSIS
Runs the repeatable pre-release checks for BGInfoPlus.

.DESCRIPTION
Parses every maintained PowerShell script, validates installer XML, and runs the
compiled regression suite. Build-Release.ps1 calls this tool so local and CI
releases cannot accidentally skip the same checks.
#>
[CmdletBinding()]
param([ValidateSet('Debug', 'Release')] [string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

$parseFailures = [Collections.Generic.List[string]]::new()
Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'tools'), (Join-Path $repositoryRoot 'packaging') `
    -Filter '*.ps1' -File -Recurse | ForEach-Object {
        $tokens = $null
        $errors = $null
        [void][Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$errors)
        foreach ($error in $errors) { $parseFailures.Add("$($_.FullName): $($error.Message)") }
    }
if ($parseFailures.Count) { throw "PowerShell parse failures:`n$($parseFailures -join "`n")" }

& (Join-Path $repositoryRoot 'packaging\azure-vm-application\Deploy-BGInfoPlusForAllUsers.ps1') -ValidateOnly
if (-not $?) { throw 'The interactive logon task definition is invalid.' }

[xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'packaging\standalone-installer\Package.wxs') -Raw) | Out-Null
[xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'packaging\standalone-installer\BGInfoPlus.Setup.wixproj') -Raw) | Out-Null

$sampleConfiguration = Get-Content -LiteralPath (Join-Path $repositoryRoot 'tools\LabDefault.json') -Raw |
    ConvertFrom-Json -ErrorAction Stop
if ([string]::IsNullOrWhiteSpace($sampleConfiguration.Document) -or
    $sampleConfiguration.Placeholders.Count -lt 1 -or $sampleConfiguration.RunAtSignIn) {
    throw 'tools\LabDefault.json is not a valid portable BGInfoPlus configuration.'
}

& dotnet run --project (Join-Path $repositoryRoot 'tests\BGInfoPlus.Tests\BGInfoPlus.Tests.csproj') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'BGInfoPlus regression tests failed.' }

Write-Host 'BGInfoPlus validation completed successfully.'
