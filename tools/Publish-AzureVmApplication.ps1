#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$script:SelectedSubscriptionId = $null
$script:AzPythonPath = $null

function Invoke-AzCli {
    param([Parameter(Mandatory)] [string[]]$Arguments)
    if (-not [string]::IsNullOrWhiteSpace($script:AzPythonPath)) {
        & $script:AzPythonPath -IBm azure.cli @Arguments
        return
    }
    & az @Arguments
}

function Invoke-AzJson {
    param([Parameter(Mandatory)] [string[]]$Arguments)
    $effectiveArguments = @($Arguments)
    if (-not [string]::IsNullOrWhiteSpace($script:SelectedSubscriptionId) -and '--subscription' -notin $Arguments) {
        $effectiveArguments += @('--subscription', $script:SelectedSubscriptionId)
    }
    $output = Invoke-AzCli ($effectiveArguments + @('--only-show-errors', '--output', 'json')) 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($output -join [Environment]::NewLine) }
    if ([string]::IsNullOrWhiteSpace(($output -join ''))) { return $null }
    return ($output -join [Environment]::NewLine) | ConvertFrom-Json
}

function Invoke-AzQuiet {
    param([Parameter(Mandatory)] [string[]]$Arguments, [switch]$ContainsSecret)
    $effectiveArguments = @($Arguments)
    if (-not [string]::IsNullOrWhiteSpace($script:SelectedSubscriptionId) -and '--subscription' -notin $Arguments) {
        $effectiveArguments += @('--subscription', $script:SelectedSubscriptionId)
    }
<#if ($ContainsSecret) {
        & az @effectiveArguments --only-show-errors --output none 2>$null
        if ($LASTEXITCODE -ne 0) { throw "Azure CLI operation failed with exit code $LASTEXITCODE. Secret-bearing arguments were not included in this error." }
        return
    }
#>
    $output = Invoke-AzCli ($effectiveArguments + @('--only-show-errors', '--output', 'none')) 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($output -join [Environment]::NewLine) }
}

function Read-Required {
    param([Parameter(Mandatory)] [string]$Prompt, [string]$Default)
    while ($true) {
        $suffix = if ([string]::IsNullOrWhiteSpace($Default)) { '' } else { " [$Default]" }
        $value = Read-Host "$Prompt$suffix"
        if ([string]::IsNullOrWhiteSpace($value)) { $value = $Default }
        if (-not [string]::IsNullOrWhiteSpace($value)) { return $value.Trim() }
        Write-Warning 'A value is required.'
    }
}

function Read-YesNo {
    param([Parameter(Mandatory)] [string]$Prompt, [bool]$Default = $true)
    $label = if ($Default) { 'Y/n' } else { 'y/N' }
    while ($true) {
        $answer = (Read-Host "$Prompt [$label]").Trim()
        if ($answer.Length -eq 0) { return $Default }
        if ($answer -match '^(y|yes)$') { return $true }
        if ($answer -match '^(n|no)$') { return $false }
        Write-Warning 'Enter Y or N.'
    }
}

function Select-FromList {
    param(
        [Parameter(Mandatory)] [string]$Prompt,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]]$Items,
        [Parameter(Mandatory)] [scriptblock]$Label,
        [string]$CreateLabel
    )
    Write-Host "`n$Prompt" -ForegroundColor Cyan
    for ($index = 0; $index -lt $Items.Count; $index++) {
        Write-Host ("  [{0}] {1}" -f ($index + 1), (& $Label $Items[$index]))
    }
    if (-not [string]::IsNullOrWhiteSpace($CreateLabel)) { Write-Host "  [0] $CreateLabel" }
    while ($true) {
        $selection = Read-Host 'Selection'
        $number = 0
        if ([int]::TryParse($selection, [ref]$number)) {
            if ($number -eq 0 -and -not [string]::IsNullOrWhiteSpace($CreateLabel)) { return $null }
            if ($number -ge 1 -and $number -le $Items.Count) { return $Items[$number - 1] }
        }
        Write-Warning 'Choose one of the displayed numbers.'
    }
}

function Assert-Name {
    param([string]$Value, [string]$Pattern, [string]$Description)
    if ($Value -notmatch $Pattern) { throw "$Description is invalid: $Value" }
}

Clear-Host
Write-Host 'BGInfoPlus — Azure Compute Gallery VM Application publisher' -ForegroundColor Cyan
Write-Host 'This wizard discovers Azure resources at runtime. It does not write subscription IDs, resource names, URLs, credentials, SAS values, or your answers into the script or local settings.'
Write-Host 'Press Ctrl+C at any prompt to stop.'

$azCommand = Get-Command az -ErrorAction SilentlyContinue
if (-not $azCommand) { throw 'Azure CLI is not installed or is not on PATH.' }
if ($IsWindows -and $azCommand.Source -match '[/\\]wbin[/\\]az\.cmd$') {
    $azureCliRoot = Split-Path -Parent (Split-Path -Parent $azCommand.Source)
    $candidatePython = Join-Path $azureCliRoot 'python.exe'
    if (Test-Path -LiteralPath $candidatePython -PathType Leaf) { $script:AzPythonPath = $candidatePython }
}
Invoke-AzCli @('account', 'show', '--only-show-errors', '--output', 'none') 2>$null
if ($LASTEXITCODE -ne 0) {
    if (-not (Read-YesNo 'Azure CLI is not signed in. Sign in now?')) { throw 'Azure sign-in is required.' }
    Invoke-AzCli @('login', '--only-show-errors')
    if ($LASTEXITCODE -ne 0) { throw 'Azure sign-in failed.' }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$project = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\BGInfoPlus\BGInfoPlus.csproj')
$version = [string]$project.Project.PropertyGroup.Version
$defaultPackage = Join-Path $repositoryRoot "artifacts\release\v$version\BGInfoPlus-$version-azure-vmapp.zip"
if (-not (Test-Path -LiteralPath $defaultPackage)) {
    Write-Warning "The version $version package has not been built."
    if (Read-YesNo 'Run the tested release build now?') {
        & (Join-Path $PSScriptRoot 'Build-Release.ps1') -Version $version
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    }
}
$packagePath = [IO.Path]::GetFullPath((Read-Required 'VM Application ZIP path' $defaultPackage))
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw "Package not found: $packagePath" }
$manifestPath = Join-Path ([IO.Path]::GetDirectoryName($packagePath)) 'azure-vm-application.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Manifest not found beside package: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.version -ne $version) { throw "Package version $($manifest.version) does not match project version $version." }

$subscriptions = @(Invoke-AzJson @('account', 'list', '--query', "[?state=='Enabled']"))
if ($subscriptions.Count -eq 0) { throw 'No enabled Azure subscriptions are available.' }
$subscription = Select-FromList 'Choose a subscription:' $subscriptions { param($item) "$($item.name) — $($item.id)" }
$script:SelectedSubscriptionId = $subscription.id

$locationsUrl = "https://management.azure.com/subscriptions/$($subscription.id)/locations?api-version=2022-12-01"
$locations = @(Invoke-AzJson @('rest', '--method', 'get', '--url', $locationsUrl, '--query', 'value[].{name:name,displayName:displayName}'))
$resourceGroups = @(Invoke-AzJson @('group', 'list', '--query', '[].{name:name,location:location}'))
$resourceGroup = Select-FromList 'Choose the resource group for the Compute Gallery:' $resourceGroups { param($item) "$($item.name) ($($item.location))" } 'Create a new resource group'
$createResourceGroup = $null -eq $resourceGroup
if ($createResourceGroup) {
    $resourceGroupName = Read-Required 'New resource group name'
    Assert-Name $resourceGroupName '^[A-Za-z0-9._()\-]{1,90}$' 'Resource group name'
    $defaultLocation = 'westeurope'
} else {
    $resourceGroupName = $resourceGroup.name
    $defaultLocation = $resourceGroup.location
}
$location = Read-Required 'Gallery location (Azure region name)' $defaultLocation
if ($location -notin $locations.name) { throw "Unknown Azure region: $location" }

$galleries = @()
if (-not $createResourceGroup) {
    $galleries = @(Invoke-AzJson @('sig', 'list', '--resource-group', $resourceGroupName))
}
$gallery = Select-FromList 'Choose a Compute Gallery:' $galleries { param($item) "$($item.name) ($($item.location))" } 'Create a new Compute Gallery'
$createGallery = $null -eq $gallery
if ($createGallery) {
    $galleryName = Read-Required 'New Compute Gallery name' 'BGInfoPlusGallery'
    Assert-Name $galleryName '^[A-Za-z0-9._\-]{1,80}$' 'Gallery name'
} else { $galleryName = $gallery.name }

$applicationName = Read-Required 'VM Application definition name' 'BGInfoPlus'
Assert-Name $applicationName '^[A-Za-z0-9._\-]{1,117}$' 'VM Application name'

Write-Host "`nPackage ingestion method:" -ForegroundColor Cyan
Write-Host '  [1] Upload to Azure Storage and use a seven-day read-only user-delegation SAS'
Write-Host '  [2] Use an existing plain blob URL with a preconfigured gallery managed identity (recommended for production)'
while ($true) {
    $method = Read-Host 'Selection'
    if ($method -in @('1', '2')) { break }
    Write-Warning 'Choose 1 or 2.'
}

$storageAccountName = $null
$storageResourceGroup = $null
$createStorage = $false
$packageUri = $null
$containerName = 'vm-applications'
if ($method -eq '1') {
    $storageAccounts = @(Invoke-AzJson @('storage', 'account', 'list', '--query', '[].{name:name,resourceGroup:resourceGroup,location:primaryLocation}'))
    $storage = Select-FromList 'Choose the staging storage account:' $storageAccounts { param($item) "$($item.name) — $($item.resourceGroup) ($($item.location))" } 'Create a new storage account'
    $createStorage = $null -eq $storage
    if ($createStorage) {
        $storageAccountName = Read-Required 'New globally unique storage account name'
        Assert-Name $storageAccountName '^[a-z0-9]{3,24}$' 'Storage account name'
        $storageResourceGroup = $resourceGroupName
    } else {
        $storageAccountName = $storage.name
        $storageResourceGroup = $storage.resourceGroup
    }
    $containerName = Read-Required 'Blob container name' $containerName
    Assert-Name $containerName '^[a-z0-9](?!.*--)[a-z0-9\-]{1,61}[a-z0-9]$' 'Container name'
} else {
    Write-Host 'The gallery must already have a user-assigned managed identity with blob read access.' -ForegroundColor Yellow
    $packageUri = Read-Required 'Plain HTTPS blob URL for the VM Application ZIP'
    if (-not [Uri]::IsWellFormedUriString($packageUri, [UriKind]::Absolute) -or -not $packageUri.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The package URL must be an absolute HTTPS URL.'
    }
}

$targetInput = Read-Required 'Replication regions, comma-separated' $location
$targetRegions = @($targetInput.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique)
foreach ($target in $targetRegions) {
    if ($target -notin $locations.name) { throw "Unknown replication region: $target" }
}

Write-Host "`nPublication plan" -ForegroundColor Cyan
Write-Host "  Subscription:       $($subscription.name) ($($subscription.id))"
Write-Host "  Resource group:     $resourceGroupName$(if ($createResourceGroup) { ' (create)' })"
Write-Host "  Gallery:            $galleryName$(if ($createGallery) { ' (create)' })"
Write-Host "  Application:        $applicationName"
Write-Host "  Version:            $version"
Write-Host "  Package:            $packagePath"
Write-Host "  Target regions:     $($targetRegions -join ', ')"
if ($method -eq '1') {
    Write-Host "  Storage:            $storageAccountName / $containerName$(if ($createStorage) { ' (create account)' })"
    Write-Host '  Ingestion security: seven-day read-only user-delegation SAS (not displayed or saved locally)'
} else {
    Write-Host '  Ingestion security: gallery managed identity with plain blob URL'
}
if (-not (Read-YesNo 'Proceed with these Azure changes?' $false)) { Write-Host 'Cancelled. No publication changes were made.'; return }

if ($createResourceGroup) { Invoke-AzQuiet @('group', 'create', '--name', $resourceGroupName, '--location', $location) }
if ($createGallery) { Invoke-AzQuiet @('sig', 'create', '--resource-group', $resourceGroupName, '--gallery-name', $galleryName, '--location', $location) }

Invoke-AzCli @('sig', 'gallery-application', 'show', '--resource-group', $resourceGroupName, '--gallery-name', $galleryName, '--application-name', $applicationName, '--subscription', $subscription.id, '--only-show-errors', '--output', 'none') 2>$null
if ($LASTEXITCODE -ne 0) {
    Invoke-AzQuiet @('sig', 'gallery-application', 'create', '--resource-group', $resourceGroupName, '--gallery-name', $galleryName, '--application-name', $applicationName, '--location', $location, '--os-type', 'Windows', '--description', 'Azure-focused system information wallpaper utility.')
}
Invoke-AzCli @('sig', 'gallery-application', 'version', 'show', '--resource-group', $resourceGroupName, '--gallery-name', $galleryName, '--application-name', $applicationName, '--version-name', $version, '--subscription', $subscription.id, '--only-show-errors', '--output', 'none') 2>$null
if ($LASTEXITCODE -eq 0) { throw "VM Application version $applicationName/$version already exists. Version resources are immutable; increment the application version." }

if ($method -eq '1') {
    if ($createStorage) {
        Invoke-AzQuiet @('storage', 'account', 'create', '--name', $storageAccountName, '--resource-group', $storageResourceGroup, '--location', $location, '--sku', 'Standard_LRS', '--kind', 'StorageV2', '--min-tls-version', 'TLS1_2', '--allow-blob-public-access', 'false')
    }
    try {
        Invoke-AzQuiet @('storage', 'container', 'create', '--name', $containerName, '--account-name', $storageAccountName, '--auth-mode', 'login')
        $blobName = [IO.Path]::GetFileName($packagePath)
        Invoke-AzQuiet @('storage', 'blob', 'upload', '--account-name', $storageAccountName, '--container-name', $containerName, '--name', $blobName, '--file', $packagePath, '--type', 'page', '--overwrite', 'true', '--auth-mode', 'login')
        $expiry = [DateTime]::UtcNow.AddDays(7).ToString('yyyy-MM-ddTHH:mmZ')
        $sas = (Invoke-AzCli @('storage', 'blob', 'generate-sas', '--account-name', $storageAccountName, '--container-name', $containerName, '--name', $blobName, '--permissions', 'r', '--expiry', $expiry, '--https-only', '--auth-mode', 'login', '--as-user', '--subscription', $subscription.id, '--only-show-errors', '--output', 'tsv') 2>$null)
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sas)) { throw 'SAS generation failed.' }
        $packageUri = "https://$storageAccountName.blob.core.windows.net/$containerName/$blobName`?$sas"
    }
    catch {
        throw "Storage access failed. Grant the signed-in identity 'Storage Blob Data Contributor' on the account, wait for role propagation, and run the wizard again. $($_.Exception.Message)"
    }
}

$targetRegionEntries = @($targetRegions | ForEach-Object {
    [ordered]@{ name = $_; regionalReplicaCount = 1; storageAccountType = 'Standard_LRS' }
})
$targetRegionJson = ConvertTo-Json -InputObject $targetRegionEntries -Compress

Invoke-AzQuiet -ContainsSecret @('sig', 'gallery-application', 'version', 'create',
    '--resource-group', $resourceGroupName,
    '--gallery-name', $galleryName,
    '--application-name', $applicationName,
    '--version-name', $version,
    '--location', $location,
    '--package-file-link', $packageUri,
    '--package-file-name', $manifest.packageFileName,
    '--install-command', $manifest.installCommand,
    '--update-command', $manifest.updateCommand,
    '--remove-command', $manifest.removeCommand,
    '--target-regions', $targetRegionJson)

$versionId = "/subscriptions/$($subscription.id)/resourceGroups/$resourceGroupName/providers/Microsoft.Compute/galleries/$galleryName/applications/$applicationName/versions/$version"
Write-Host "`nPublished successfully." -ForegroundColor Green
Write-Host "VM Application version ID:`n$versionId"
Write-Host "`nDeploy it with 'Treat deployment as failure' enabled. This wizard did not save the selected resource information."
