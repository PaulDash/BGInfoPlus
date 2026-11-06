#Requires -Version 5.1
[CmdletBinding()]
param([string]$ConfigurationPath)

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'BGInfoPlus must be installed from an elevated process.'
}

$sourceExe = Join-Path $PSScriptRoot 'BGInfoPlus.exe'
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) { throw "Package is missing $sourceExe." }
$fileVersion = (Get-Item -LiteralPath $sourceExe).VersionInfo.FileVersion
$versionParts = $fileVersion.Split('.')
if ($versionParts.Count -lt 3) { throw "BGInfoPlus has an invalid file version: $fileVersion." }
$productVersion = $versionParts[0..2] -join '.'

$installDirectory = Join-Path $env:ProgramFiles 'BGInfoPlus'
$destinationExe = Join-Path $installDirectory 'BGInfoPlus.exe'
$newExe = Join-Path $installDirectory 'BGInfoPlus.exe.new'
$backupExe = Join-Path $installDirectory 'BGInfoPlus.exe.previous'
[IO.Directory]::CreateDirectory($installDirectory) | Out-Null

Copy-Item -LiteralPath $sourceExe -Destination $newExe -Force
try {
    if (Test-Path -LiteralPath $destinationExe) {
        Move-Item -LiteralPath $destinationExe -Destination $backupExe -Force
    }
    Move-Item -LiteralPath $newExe -Destination $destinationExe -Force
    Remove-Item -LiteralPath $backupExe -Force -ErrorAction SilentlyContinue
}
catch {
    Remove-Item -LiteralPath $newExe -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path -LiteralPath $destinationExe) -and (Test-Path -LiteralPath $backupExe)) {
        Move-Item -LiteralPath $backupExe -Destination $destinationExe -Force
    }
    throw
}

$installedRemoveScript = Join-Path $installDirectory 'Remove-BGInfoPlus.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Remove-BGInfoPlus.ps1') -Destination $installedRemoveScript -Force
$installedDeploymentScript = Join-Path $installDirectory 'Deploy-BGInfoPlusForAllUsers.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Deploy-BGInfoPlusForAllUsers.ps1') -Destination $installedDeploymentScript -Force

if (-not [string]::IsNullOrWhiteSpace($ConfigurationPath)) {
    $configurationSource = [IO.Path]::GetFullPath($ConfigurationPath)
    if (-not (Test-Path -LiteralPath $configurationSource -PathType Leaf)) {
        throw "The supplied BGInfoPlus configuration was not found at $configurationSource."
    }
    $sharedDirectory = Join-Path $env:ProgramData 'BGInfoPlus'
    $sharedConfiguration = Join-Path $sharedDirectory 'config.json'
    $managedHashPath = Join-Path $sharedDirectory 'config.source.sha256'
    $installTemplate = -not (Test-Path -LiteralPath $sharedConfiguration -PathType Leaf)
    if (-not $installTemplate -and (Test-Path -LiteralPath $managedHashPath -PathType Leaf)) {
        $managedHash = (Get-Content -LiteralPath $managedHashPath -Raw).Trim()
        $currentHash = (Get-FileHash -LiteralPath $sharedConfiguration -Algorithm SHA256).Hash
        $installTemplate = $managedHash -match '^[A-Fa-f0-9]{64}$' -and $currentHash -eq $managedHash
    }
    if ($installTemplate) {
        & $installedDeploymentScript -TemplatePath $configurationSource
        $installedHash = (Get-FileHash -LiteralPath $sharedConfiguration -Algorithm SHA256).Hash
        Set-Content -LiteralPath $managedHashPath -Value $installedHash -Encoding ascii
    } else {
        & $installedDeploymentScript -PreserveConfiguration
    }
}

$shortcutPath = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\BGInfoPlus.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $destinationExe
$shortcut.WorkingDirectory = $installDirectory
$shortcut.IconLocation = "$destinationExe,0"
$shortcut.Description = 'Configure Azure-focused system information wallpaper'
$shortcut.Save()
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null

$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\BGInfoPlus'
[void](New-Item -Path $uninstallKey -Force)
Set-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'BGInfoPlus'
Set-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value $productVersion
Set-ItemProperty -Path $uninstallKey -Name Publisher -Value 'Paul Dash'
Set-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value $destinationExe
Set-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installDirectory
Set-ItemProperty -Path $uninstallKey -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$installedRemoveScript`""
New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

Write-Host "BGInfoPlus $productVersion installed at $destinationExe"
