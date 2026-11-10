#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [switch]$SkipGitHubRelease
)

function Read-YesNo([string]$Prompt) {
    while ($true) {
        $answer = (Read-Host "$Prompt [y/N]").Trim()
        if ([string]::IsNullOrWhiteSpace($answer) -or $answer -match '^(?i:n|no)$') { return $false }
        if ($answer -match '^(?i:y|yes)$') { return $true }
        Write-Warning 'Enter Y or N.'
    }
}

function Invoke-Checked([string]$FilePath, [string[]]$Arguments, [string]$FailureMessage) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) { throw $FailureMessage }
}

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'src\BGInfoPlus\BGInfoPlus.csproj'
[xml]$project = Get-Content -LiteralPath $projectPath
$projectVersion = [string]$project.Project.PropertyGroup.Version
if ($projectVersion -ne $Version) { throw "Project version is $projectVersion, but release version is $Version." }

$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\release\v$Version"))
$expectedParent = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\release'))
if (-not $releaseRoot.StartsWith($expectedParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output resolved outside the expected artifacts directory.'
}
if (Test-Path -LiteralPath $releaseRoot) { Remove-Item -LiteralPath $releaseRoot -Recurse -Force }
[IO.Directory]::CreateDirectory($releaseRoot) | Out-Null

& (Join-Path $PSScriptRoot 'Test-BGInfoPlus.ps1') -Configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Release validation failed.' }

$publishRoot = Join-Path $releaseRoot 'publish'
& dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }

$publishedExe = Join-Path $publishRoot 'BGInfoPlus.exe'
$publishedVersion = (Get-Item -LiteralPath $publishedExe).VersionInfo.FileVersion
if ($publishedVersion -ne "$Version.0") { throw "Published file version is $publishedVersion, expected $Version.0." }
$defaultConfiguration = Join-Path $releaseRoot 'DefaultConfig.json'
$exportProcess = Start-Process -FilePath $publishedExe `
    -ArgumentList @('--export-default-config', "`"$defaultConfiguration`"") `
    -Wait -PassThru -WindowStyle Hidden
if ($exportProcess.ExitCode -ne 0) {
    throw "Default BGInfoPlus configuration export failed with exit code $($exportProcess.ExitCode)."
}
if (-not (Test-Path -LiteralPath $defaultConfiguration -PathType Leaf)) {
    throw 'The default BGInfoPlus configuration was not created.'
}
try {
    $defaultConfigurationData = Get-Content -LiteralPath $defaultConfiguration -Raw | ConvertFrom-Json -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace($defaultConfigurationData.Document) -or
        $defaultConfigurationData.Placeholders.Count -lt 1 -or $defaultConfigurationData.RunAtSignIn) {
        throw 'The generated default configuration is missing required values.'
    }
}
catch {
    throw "Could not generate a valid default BGInfoPlus configuration: $($_.Exception.Message)"
}

$installerProject = Join-Path $repositoryRoot 'packaging\standalone-installer\BGInfoPlus.Setup.wixproj'
$standaloneMsi = Join-Path $releaseRoot "BGInfoPlus-$Version-win-x64.msi"
& dotnet build $installerProject -c Release -p:ProductVersion=$Version -p:PublishDirectory=$publishRoot `
    -p:RepositoryRoot=$repositoryRoot -p:DefaultConfigurationPath=$defaultConfiguration `
    -p:OutputName="BGInfoPlus-$Version-win-x64" -o $releaseRoot
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $standaloneMsi -PathType Leaf)) { throw 'MSI build failed.' }
Remove-Item -LiteralPath (Join-Path $releaseRoot "BGInfoPlus-$Version-win-x64.wixpdb") -Force -ErrorAction SilentlyContinue

$standaloneExe = Join-Path $releaseRoot "BGInfoPlus-$Version-win-x64.exe"
Copy-Item -LiteralPath $publishedExe -Destination $standaloneExe

$portableStage = Join-Path $releaseRoot 'portable'
[IO.Directory]::CreateDirectory($portableStage) | Out-Null
Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $portableStage 'BGInfoPlus.exe')
Copy-Item -LiteralPath $defaultConfiguration -Destination (Join-Path $portableStage 'DefaultConfig.json')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\azure-vm-application\Deploy-BGInfoPlusForAllUsers.ps1') -Destination $portableStage
Set-Content -LiteralPath (Join-Path $portableStage 'README.txt') -Encoding UTF8 -Value @"
BGInfoPlus $Version

Run BGInfoPlus.exe to configure, preview, export, and apply the wallpaper.
No separate .NET runtime is required. Personal configuration is stored in LocalAppData.
DefaultConfig.json beside the executable is used when no personal configuration exists.
Use "Deploy for all users" in the editor to create a protected shared template and logon task.
Documentation: https://github.com/PaulDash/BGInfoPlus
"@
$portableZip = Join-Path $releaseRoot "BGInfoPlus-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $portableStage '*') -DestinationPath $portableZip -CompressionLevel Optimal

$vmStage = Join-Path $releaseRoot 'azure-vmapp'
[IO.Directory]::CreateDirectory($vmStage) | Out-Null
Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $vmStage 'BGInfoPlus.exe')
Copy-Item -LiteralPath $defaultConfiguration -Destination (Join-Path $vmStage 'DefaultConfig.json')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\azure-vm-application\Install-BGInfoPlus.ps1') -Destination $vmStage
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\azure-vm-application\Remove-BGInfoPlus.ps1') -Destination $vmStage
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\azure-vm-application\Deploy-BGInfoPlusForAllUsers.ps1') -Destination $vmStage
$vmPackageName = "BGInfoPlus-$Version-azure-vmapp.zip"
$vmPackage = Join-Path $releaseRoot $vmPackageName
Compress-Archive -Path (Join-Path $vmStage '*') -DestinationPath $vmPackage -CompressionLevel Optimal

$expand = "if (Test-Path '.\BGInfoPlus-payload') { Remove-Item -LiteralPath '.\BGInfoPlus-payload' -Recurse -Force }; Expand-Archive -LiteralPath '.\$vmPackageName' -DestinationPath '.\BGInfoPlus-payload' -Force"
$configurationFileName = 'BGInfoPlus-config.json'
$installAction = "& '.\BGInfoPlus-payload\Install-BGInfoPlus.ps1' -ConfigurationPath '.\$configurationFileName'"
$removeAction = "& '.\BGInfoPlus-payload\Remove-BGInfoPlus.ps1'"
$installCommand = "powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command `"try { $expand; $installAction } catch { Write-Error `$_; exit 1 }`""
$removeCommand = "powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command `"try { $expand; $removeAction } catch { Write-Error `$_; exit 1 }`""
$manifest = [ordered]@{
    applicationName = 'BGInfoPlus'
    version = $Version
    supportedOsType = 'Windows'
    packageFileName = $vmPackageName
    configurationFileName = $configurationFileName
    installCommand = $installCommand
    updateCommand = $installCommand
    removeCommand = $removeCommand
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $releaseRoot 'azure-vm-application.json') -Encoding UTF8

$releaseFiles = @($standaloneExe, $standaloneMsi, $defaultConfiguration, $portableZip, $vmPackage, (Join-Path $releaseRoot 'azure-vm-application.json'))
$checksums = foreach ($file in $releaseFiles) {
    $hash = Get-FileHash -LiteralPath $file -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($file))"
}
$checksums | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ascii

& (Join-Path $PSScriptRoot 'Test-ReleaseArtifacts.ps1') -ReleaseRoot $releaseRoot -Version $Version
if ($LASTEXITCODE -ne 0) { throw 'Release artifact validation failed.' }

Remove-Item -LiteralPath $publishRoot, $portableStage, $vmStage -Recurse -Force
Write-Host "Release artifacts created in $releaseRoot"

if ($SkipGitHubRelease) {
    Write-Host 'Skipped the interactive GitHub release prompt.'
    return
}

$tagName = "v$Version"
if (-not (Read-YesNo "Create GitHub release $tagName from these validated artifacts now?")) {
    Write-Host 'GitHub release creation skipped.'
    return
}

$gh = Get-Command gh -CommandType Application -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    throw 'GitHub CLI (gh) is required to create the release. Install it, run gh auth login, and rerun the build.'
}
Invoke-Checked $gh.Source @('auth', 'status', '--hostname', 'github.com') `
    'GitHub CLI is not authenticated. Run gh auth login and rerun the build.'

$worktreeChanges = @(& git -C $repositoryRoot status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the Git worktree.' }
if ($worktreeChanges.Count -ne 0) {
    throw 'The Git worktree must be clean before publishing a release. Commit or stash the listed changes and rerun the build.'
}

$branch = [string](& git -C $repositoryRoot branch --show-current)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($branch)) {
    throw 'GitHub release creation requires a checked-out branch, not a detached HEAD.'
}
$branch = $branch.Trim()
$headCommit = ([string](& git -C $repositoryRoot rev-parse HEAD)).Trim()
if ($LASTEXITCODE -ne 0 -or $headCommit -notmatch '^[a-f0-9]{40}$') { throw 'Could not resolve the release commit.' }
$originUrl = ([string](& git -C $repositoryRoot remote get-url origin)).Trim()
if ($LASTEXITCODE -ne 0 -or $originUrl -notmatch 'github\.com[/:](?<repository>[^/:\s]+/[^/\s]+?)(?:\.git)?$') {
    throw 'The origin remote must identify a GitHub repository.'
}
$repository = $Matches.repository

$localTagCommit = [string](& git -C $repositoryRoot rev-list -n 1 $tagName 2>$null)
if ($LASTEXITCODE -eq 0 -and $localTagCommit.Trim() -ne $headCommit) {
    throw "Local tag $tagName points to a different commit. Refusing to move a release tag."
}

& $gh.Source release view $tagName --repo $repository *> $null
if ($LASTEXITCODE -eq 0) { throw "GitHub release $tagName already exists." }

$remoteTag = @(& git -C $repositoryRoot ls-remote --tags origin "refs/tags/$tagName" "refs/tags/$tagName^{}")
if ($LASTEXITCODE -ne 0) { throw "Could not inspect remote tag $tagName." }
if ($remoteTag.Count -gt 0) {
    $peeledTag = @($remoteTag | Where-Object { $_ -like "*refs/tags/$tagName^{}" })
    $remoteTagLine = if ($peeledTag.Count) { $peeledTag[0] } else { $remoteTag[0] }
    $remoteTagCommit = ($remoteTagLine -split '\s+')[0]
    if ($remoteTagCommit -ne $headCommit) {
        throw "Remote tag $tagName does not point to the current commit. Refusing to publish the release."
    }
}

Invoke-Checked 'git' @('-C', $repositoryRoot, 'push', 'origin', "HEAD:refs/heads/$branch") `
    "Could not push branch $branch to origin. Resolve the Git error before publishing the release."

$releaseArguments = @('release', 'create', $tagName) +
    @($releaseFiles + (Join-Path $releaseRoot 'SHA256SUMS.txt') | Sort-Object | ForEach-Object { [IO.Path]::GetFullPath($_) }) +
    @('--repo', $repository, '--title', "BGInfoPlus $tagName", '--generate-notes')

if ($remoteTag.Count -gt 0) {
    $releaseArguments += '--verify-tag'
} else {
    $releaseArguments += @('--target', $headCommit)
}

Invoke-Checked $gh.Source $releaseArguments "GitHub release $tagName could not be created."
Invoke-Checked 'git' @('-C', $repositoryRoot, 'fetch', 'origin', 'tag', $tagName, '--no-tags') `
    "GitHub release was created, but local tag $tagName could not be fetched."
Write-Host "GitHub release created: https://github.com/$repository/releases/tag/$tagName"
