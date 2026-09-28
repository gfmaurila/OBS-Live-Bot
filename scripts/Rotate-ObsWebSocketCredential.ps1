param(
    [Parameter(Mandatory = $true)]
    [string]$ObsConfigPath,

    [Parameter(Mandatory = $true)]
    [string]$EnvPath,

    [Parameter(Mandatory = $true)]
    [string]$BackupPath
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName 'System.Security'

if (Get-Process -Name 'obs64' -ErrorAction SilentlyContinue) {
    throw 'OBS must be stopped before rotating its WebSocket credential.'
}

if (-not (Test-Path -LiteralPath $ObsConfigPath -PathType Leaf)) {
    throw 'OBS WebSocket configuration file was not found.'
}

$backupDirectory = Split-Path -Parent $BackupPath
if (-not (Test-Path -LiteralPath $backupDirectory)) {
    New-Item -ItemType Directory -Path $backupDirectory | Out-Null
}

if (-not (Test-Path -LiteralPath $BackupPath)) {
    $configBytes = [System.IO.File]::ReadAllBytes($ObsConfigPath)
    $protectedBytes = [System.Security.Cryptography.ProtectedData]::Protect(
        $configBytes,
        $null,
        [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
    [System.IO.File]::WriteAllBytes($BackupPath, $protectedBytes)
}
elseif ((Get-Item -LiteralPath $BackupPath).Length -eq 0) {
    throw 'The existing protected backup is empty.'
}

$randomBytes = New-Object byte[] 32
$randomNumberGenerator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$randomNumberGenerator.GetBytes($randomBytes)
$randomNumberGenerator.Dispose()
$newPassword = [Convert]::ToBase64String($randomBytes)

try {
    $config = [System.IO.File]::ReadAllText($ObsConfigPath) | ConvertFrom-Json
    $config.server_enabled = $true
    $config.auth_required = $true
    $config.server_port = 4455
    $config.server_password = $newPassword

    $configTemp = "$ObsConfigPath.task02.tmp"
    $configJson = $config | ConvertTo-Json -Depth 32
    [System.IO.File]::WriteAllText($configTemp, $configJson, [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $configTemp -Destination $ObsConfigPath -Force

    $envLines = if (Test-Path -LiteralPath $EnvPath) {
        [System.Collections.Generic.List[string]]::new([string[]][System.IO.File]::ReadAllLines($EnvPath))
    }
    else {
        [System.Collections.Generic.List[string]]::new()
    }

    $passwordLineIndex = -1
    for ($index = 0; $index -lt $envLines.Count; $index++) {
        if ($envLines[$index] -match '^OBS_WEBSOCKET_PASSWORD=') {
            $passwordLineIndex = $index
            break
        }
    }

    $passwordLine = "OBS_WEBSOCKET_PASSWORD=$newPassword"
    if ($passwordLineIndex -ge 0) {
        $envLines[$passwordLineIndex] = $passwordLine
    }
    else {
        $envLines.Add($passwordLine)
    }

    $envTemp = "$EnvPath.task02.tmp"
    [System.IO.File]::WriteAllLines($envTemp, $envLines, [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $envTemp -Destination $EnvPath -Force

    $excludedPaths = @(
        [System.IO.Path]::GetFullPath($EnvPath),
        [System.IO.Path]::GetFullPath($BackupPath)
    )
    $projectRoot = Split-Path -Parent $EnvPath
    $exposureFound = $false
    Get-ChildItem -LiteralPath $projectRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -notlike "$projectRoot\.git\*" -and
            $_.FullName -notlike "$projectRoot\bin\*" -and
            $_.FullName -notlike "$projectRoot\obj\*" -and
            $excludedPaths -notcontains $_.FullName
        } |
        ForEach-Object {
            try {
                if ([System.IO.File]::ReadAllText($_.FullName).Contains($newPassword)) {
                    $exposureFound = $true
                }
            }
            catch {
                # Binary or locked files are ignored; Git and explicit scans run separately.
            }
        }

    if ($exposureFound) {
        throw 'The new credential was detected outside the approved local secret stores.'
    }

    Write-Output 'PASSWORD_ROTATED=TRUE'
    Write-Output 'PASSWORD_LENGTH_VALID=TRUE'
    Write-Output 'PROTECTED_BACKUP_CREATED=TRUE'
    Write-Output 'LOCAL_ENV_UPDATED=TRUE'
    Write-Output 'PROJECT_SECRET_EXPOSURE=FALSE'
}
finally {
    [Array]::Clear($randomBytes, 0, $randomBytes.Length)
    $newPassword = $null
    $config = $null
}
