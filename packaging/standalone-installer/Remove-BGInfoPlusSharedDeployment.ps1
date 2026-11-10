#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Unregister-ScheduledTask -TaskName 'BGInfoPlus - Apply wallpaper at logon' -Confirm:$false -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $env:ProgramData 'BGInfoPlus\config.json') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $env:ProgramData 'BGInfoPlus\config.source.sha256') -Force -ErrorAction SilentlyContinue

Get-CimInstance -ClassName Win32_UserProfile -ErrorAction SilentlyContinue |
    Where-Object { -not $_.Special -and -not [string]::IsNullOrWhiteSpace($_.LocalPath) } |
    ForEach-Object {
        $shortcut = Join-Path $_.LocalPath 'AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\BGInfoPlus.lnk'
        Remove-Item -LiteralPath $shortcut -Force -ErrorAction SilentlyContinue
    }
