[CmdletBinding()]
param(
    [string]$ConfigurationRoot = 'D:\OBS-Live\.config',
    [string]$ObsConfigurationRoot = "$env:APPDATA\obs-studio"
)

$ErrorActionPreference = 'Stop'
$providerPath = Join-Path $ConfigurationRoot 'studioos.providers.json'
$socialStreamPath = Join-Path $ConfigurationRoot 'studioos.socialstream.json'

if (-not (Test-Path -LiteralPath $providerPath)) {
    throw 'studioos.providers.json was not found.'
}

if (Test-Path -LiteralPath $socialStreamPath) {
    $existingRaw = Get-Content -Raw -LiteralPath $socialStreamPath
    $existing = $existingRaw | ConvertFrom-Json
    if ($existing.schemaVersion -ne 1) { throw 'Existing Social Stream configuration has an unsupported schema.' }
    $forbidden = '(?i)client.?secret|access.?token|refresh.?token|authorization.?code|password|cookies?|stream.?key|session.?id|room.?id'
    if ($existingRaw -match $forbidden) { throw 'Existing Social Stream configuration contains a protected field name.' }
    Write-Output 'SOCIALSTREAM_CONFIG=PRESERVED'
    Write-Output ('SOCIALSTREAM_CONFIG_PATH=' + $socialStreamPath)
    exit 0
}

$providerDocument = Get-Content -Raw -LiteralPath $providerPath | ConvertFrom-Json
$providers = $providerDocument.providers
if ($null -eq $providers) { throw 'Provider configuration does not contain providers.' }

$profile = $null
$sceneCollection = $null
$globalPath = Join-Path $ObsConfigurationRoot 'global.ini'
if (Test-Path -LiteralPath $globalPath) {
    foreach ($line in Get-Content -LiteralPath $globalPath) {
        if ($line -match '^Profile(?:Dir)?=(.*)$') { $profile = $matches[1] }
        elseif ($line -match '^SceneCollection(?:File)?=(.*)$') { $sceneCollection = $matches[1] }
    }
}

if (-not $profile) {
    $profileDirectories = @(Get-ChildItem -LiteralPath (Join-Path $ObsConfigurationRoot 'basic\profiles') -Directory -ErrorAction SilentlyContinue)
    if ($profileDirectories.Count -eq 1) { $profile = $profileDirectories[0].Name }
}
if (-not $sceneCollection) {
    $sceneFiles = @(Get-ChildItem -LiteralPath (Join-Path $ObsConfigurationRoot 'basic\scenes') -Filter '*.json' -File -ErrorAction SilentlyContinue)
    if ($sceneFiles.Count -eq 1) { $sceneCollection = $sceneFiles[0].BaseName }
}

Write-Output ('OBS_DISCOVERY_PROFILE=' + $(if ($profile) { $profile } else { 'UNKNOWN' }))
Write-Output ('OBS_DISCOVERY_SCENE_COLLECTION=' + $(if ($sceneCollection) { $sceneCollection } else { 'UNKNOWN' }))
Write-Output 'OBS_DISCOVERY_MODE=READ_ONLY'

$configuration = [ordered]@{
    schemaVersion = 1
    enabled = $true
    connection = [ordered]@{
        mode = 'docker'
        endpoint = 'http://gfm-studioos-socialstream:17778'
        configureSources = $true
        maxEventBytes = 65536
    }
    providers = [ordered]@{
        twitch = [ordered]@{
            enabled = ($providers.twitch.enabled -eq $true)
            channel = [string]$providers.twitch.channel
        }
        youtube = [ordered]@{
            enabled = ($providers.youtube.enabled -eq $true)
            channel = [string]$providers.youtube.channelId
        }
        kick = [ordered]@{
            enabled = ($providers.kick.enabled -eq $true)
            channel = [string]$providers.kick.channel
        }
    }
}

$temporaryPath = Join-Path $ConfigurationRoot ('.studioos.socialstream.' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    $json = $configuration | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($temporaryPath, $json, [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporaryPath -Destination $socialStreamPath
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
}

Write-Output 'SOCIALSTREAM_CONFIG=CREATED'
Write-Output ('SOCIALSTREAM_CONFIG_PATH=' + $socialStreamPath)
Write-Output 'CLIENT_IDS_COPIED=NO'
Write-Output 'PROTECTED_VALUES_COPIED=NO'
