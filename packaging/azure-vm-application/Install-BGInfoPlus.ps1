#Requires -Version 5.1
[CmdletBinding()]
param()

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
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Deploy-BGInfoPlusForAllUsers.ps1') -Destination (Join-Path $installDirectory 'Deploy-BGInfoPlusForAllUsers.ps1') -Force

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
